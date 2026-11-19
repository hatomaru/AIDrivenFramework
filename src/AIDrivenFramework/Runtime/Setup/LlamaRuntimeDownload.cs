using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AIDrivenFW.Setup
{
    /// <summary>Pinned CPU releases. GPU runtimes remain an explicit player file selection.</summary>
    public static class LlamaRuntimeDownload
    {
        public const string Version = "b12541";
        public const string TermsUrl = "https://github.com/ggml-org/llama.cpp/blob/" + Version + "/LICENSE";
        public sealed class Asset
        {
            public string Platform { get; }
            public string Url { get; }
            public long Bytes { get; }
            public string Sha256 { get; }
            internal Asset(string platform, string url, long bytes, string sha256)
            { Platform = platform; Url = url; Bytes = bytes; Sha256 = sha256; }
        }

        static readonly Asset x64 = new Asset("Windows x64 CPU",
            "https://github.com/ggml-org/llama.cpp/releases/download/b12541/llama-b12541-bin-win-cpu-x64.zip",
            24814829, "957fb93cd180ac904f8b1d94cf118e078890f4692cfd7f77680c096c98e9af2e");
        static readonly Asset arm64 = new Asset("Windows ARM64 CPU",
            "https://github.com/ggml-org/llama.cpp/releases/download/b12541/llama-b12541-bin-win-cpu-arm64.zip",
            13000230, "4d6c0869e548ffcd67dd10f7c4a54966b1b461cac0078a30dd49484d9dd66a24");
        static readonly HttpClient http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        public static Asset Select(RuntimePlatform platform, Architecture architecture) =>
            platform != RuntimePlatform.WindowsEditor && platform != RuntimePlatform.WindowsPlayer ? null :
            architecture == Architecture.X64 ? x64 : architecture == Architecture.Arm64 ? arm64 : null;
        // Match the process architecture checked by the archive inspector, including emulated Editors.
        public static Asset Current => Select(Application.platform, RuntimeInformation.ProcessArchitecture);

        public static Task<string> AcquireAsync(string managedRoot, Action<SetupProgress> progress, CancellationToken ct)
        {
            var asset = Current ?? throw new SetupException(SetupProblem.Runtime, "Select an installed llama-server or compatible ZIP for this platform.");
            return AcquireAsync(managedRoot, asset, http, progress, ct);
        }

        internal static async Task<string> AcquireAsync(string managedRoot, Asset asset, HttpClient client,
            Action<SetupProgress> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // A unique archive is owned by this attempt; installed hash folders are never deleted.
            string archive = Path.Combine(managedRoot, ".download-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                string installed = Path.Combine(managedRoot, asset.Sha256, "llama-server.exe");
                string stamp = Path.Combine(managedRoot, asset.Sha256, ".setup-source.sha256");
                if (File.Exists(installed) && File.Exists(stamp) && new FileInfo(stamp).Length == 64 &&
                    string.Equals(File.ReadAllText(stamp), asset.Sha256, StringComparison.Ordinal))
                    return installed;
                RuntimeAcquisition.RequireSpace(managedRoot, checked(asset.Bytes + 5L * 1024 * 1024 * 1024));
                Directory.CreateDirectory(managedRoot);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromMinutes(15));
                using var response = await client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long size && size != asset.Bytes)
                    throw new InvalidDataException("Unexpected llama.cpp ZIP size.");
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                {
                    var buffer = new byte[131072]; long completed = 0;
                    while (true)
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                        idle.CancelAfter(TimeSpan.FromSeconds(90));
                        int read = await input.ReadAsync(buffer, 0, buffer.Length, idle.Token).ConfigureAwait(false);
                        if (read == 0) break;
                        completed += read;
                        if (completed > asset.Bytes) throw new InvalidDataException("Unexpected llama.cpp ZIP size.");
                        await output.WriteAsync(buffer, 0, read, deadline.Token).ConfigureAwait(false);
                        progress?.Invoke(new SetupProgress { Stage = SetupStage.Runtime, CompletedBytes = completed, TotalBytes = asset.Bytes, Detail = "downloading runtime" });
                    }
                    if (completed != asset.Bytes) throw new InvalidDataException("Incomplete llama.cpp ZIP.");
                }
                // The existing extractor checks the pinned hash before extracting and validates PE/path/DLL layout.
                return await LlamaRuntimeArchive.ExtractAsync(archive, asset.Sha256, managedRoot, progress, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException ex)
            { throw new SetupException(SetupProblem.RuntimeArchive, LlamaArchiveFailure.Timeout.ToString(), ex); }
            catch (SetupException) { throw; }
            catch (IOException ex) when (RuntimeAcquisition.IsDiskFull(ex))
            { throw new SetupException(SetupProblem.Space, "Not enough space to prepare llama.cpp.", ex); }
            catch (Exception ex) when (ex is HttpRequestException || ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            { throw new SetupException(SetupProblem.Runtime, "llama.cpp download failed. Review the connection and retry.", ex); }
            finally
            {
                try { File.Delete(archive); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
        }
    }
}
