using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AIDrivenFW.Setup
{
    public static class RuntimeAcquisition
    {
        static readonly HttpClient downloads = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        const long GiB = 1024L * 1024 * 1024;

        public static void RequireSpace(string directory, long bytes)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory)));
                if (drive.AvailableFreeSpace < bytes)
                    throw new SetupException(SetupProblem.Space, "Not enough free disk space.");
            }
            catch (SetupException) { throw; }
            catch (Exception ex) { throw new SetupException(SetupProblem.Space, "Could not check available disk space.", ex); }
        }

        public static async Task<string> AcquireAsync(string destination, Action<SetupProgress> progress, CancellationToken ct)
        {
            var executable = Path.Combine(destination, "ollama.exe");
            var stamp = Path.Combine(destination, "verified.sha256");
            if (File.Exists(executable) && File.Exists(stamp) && File.ReadAllText(stamp) == RuntimeRelease.Sha256)
                return executable;
            if (Directory.Exists(destination))
                throw new SetupException(SetupProblem.Runtime, "The runtime folder exists but could not be verified. Choose an installed runtime or repair this folder.");

            RequireSpace(destination, RuntimeRelease.Bytes + 5 * GiB);
            string parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            string staging = Path.Combine(parent, ".setup-" + Guid.NewGuid().ToString("N"));
            string archive = staging + ".zip";
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromMinutes(45));
                using var response = await downloads.GetAsync(RuntimeRelease.Url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                response.EnsureSuccessStatusCode();
                using (var body = await response.Content.ReadAsStreamAsync())
                using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                {
                    byte[] buffer = new byte[131072];
                    long count = 0;
                    while (true)
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                        idle.CancelAfter(TimeSpan.FromSeconds(90));
                        int read = await body.ReadAsync(buffer, 0, buffer.Length, idle.Token);
                        if (read == 0) break;
                        count += read;
                        if (count > RuntimeRelease.Bytes) throw new InvalidDataException("Unexpected runtime download size.");
                        await output.WriteAsync(buffer, 0, read, deadline.Token);
                        progress?.Invoke(new SetupProgress { Stage = SetupStage.Runtime, CompletedBytes = count, TotalBytes = RuntimeRelease.Bytes });
                    }
                    if (count != RuntimeRelease.Bytes) throw new InvalidDataException("The runtime download is incomplete.");
                }
                await Task.Run(() => VerifyAndExtract(archive, staging, RuntimeRelease.Sha256, deadline.Token), deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                if (!File.Exists(Path.Combine(staging, "ollama.exe")))
                    throw new InvalidDataException("The archive contains no Ollama executable.");
                File.WriteAllText(Path.Combine(staging, "verified.sha256"), RuntimeRelease.Sha256);
                Directory.Move(staging, destination);
                return executable;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                throw new SetupException(ex is IOException io && IsDiskFull(io) ? SetupProblem.Space : SetupProblem.Runtime,
                    "Runtime download or verification failed. Retry on a stable connection.", ex);
            }
            finally
            {
                // Both paths are unique to this attempt; a working install is never removed.
                if (File.Exists(archive)) File.Delete(archive);
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }

        public static bool IsDiskFull(IOException ex) => (ex.HResult & 0xffff) == 112 || (ex.HResult & 0xffff) == 39;

        public static void VerifyAndExtract(string archive, string staging, string sha256, CancellationToken ct)
        {
            using (var file = File.OpenRead(archive))
            using (var hash = SHA256.Create())
            {
                var buffer = new byte[131072];
                int read;
                while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    hash.TransformBlock(buffer, 0, read, null, 0);
                }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                string actual = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Runtime SHA-256 does not match the pinned release.");
            }
            using var zip = ZipFile.OpenRead(archive);
            string root = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            long expanded = 0;
            if (zip.Entries.Count > 10000) throw new InvalidDataException("Too many archive entries.");
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('\\', '/')));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                    ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || entry.FullName.Contains(":"))
                    throw new InvalidDataException("Unsafe runtime archive entry.");
                expanded = checked(expanded + entry.Length);
                if (expanded > 5 * GiB) throw new InvalidDataException("Runtime archive exceeds its expansion limit.");
                if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using var source = entry.Open();
                using var target = new FileStream(path, FileMode.CreateNew);
                var buffer = new byte[131072];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    target.Write(buffer, 0, read);
                }
            }
        }
    }
}
