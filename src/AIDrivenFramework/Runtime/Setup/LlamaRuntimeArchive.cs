using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AIDrivenFW.Setup
{
    public enum LlamaArchiveFailure { InvalidZip, MissingServer, MultipleServers, WrongPlatform, UnsafeEntry, TooLarge, Changed, Access, Timeout }
    public sealed class LlamaArchiveException : Exception
    {
        public LlamaArchiveFailure Failure { get; }
        public LlamaArchiveException(LlamaArchiveFailure failure, Exception inner = null) : base(failure.ToString(), inner) => Failure = failure;
    }
    public sealed class LlamaArchiveInfo
    {
        public string Sha256, ServerEntry;
        public long ExpandedBytes;
    }

    /// <summary>Reads a player-selected Windows ZIP; extraction occurs only in the consent-gated runtime stage.</summary>
    public static class LlamaRuntimeArchive
    {
        const long MaximumBytes = 5L * 1024 * 1024 * 1024;
        const long ReserveBytes = 64L * 1024 * 1024;
        const string Stamp = ".setup-source.sha256";
        public static bool IsZip(string path) => !string.IsNullOrWhiteSpace(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        static ushort Machine => RuntimeInformation.ProcessArchitecture == Architecture.X64 ? (ushort)0x8664 :
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? (ushort)0xaa64 : (ushort)0x14c;

        public static LlamaArchiveInfo Inspect(string archive, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new LlamaArchiveException(LlamaArchiveFailure.WrongPlatform);
                using var file = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Inspect(file, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (LlamaArchiveException) { throw; }
            catch (InvalidDataException ex) { throw new LlamaArchiveException(LlamaArchiveFailure.InvalidZip, ex); }
            catch (UnauthorizedAccessException ex) { throw new LlamaArchiveException(LlamaArchiveFailure.Access, ex); }
            catch (System.Security.SecurityException ex) { throw new LlamaArchiveException(LlamaArchiveFailure.Access, ex); }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is NotSupportedException || ex is OverflowException)
            { throw new LlamaArchiveException(LlamaArchiveFailure.InvalidZip, ex); }
        }
        static LlamaArchiveInfo Inspect(FileStream file, CancellationToken ct)
        {
            var result = new LlamaArchiveInfo(); var buffer = new byte[131072];
            using (var hash = SHA256.Create())
            {
                int read;
                while ((read = file.Read(buffer, 0, buffer.Length)) > 0) { ct.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, read, null, 0); }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0); result.Sha256 = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
            }
            file.Position = 0;
            using var zip = new ZipArchive(file, ZipArchiveMode.Read, true);
            if (zip.Entries.Count == 0) throw new LlamaArchiveException(LlamaArchiveFailure.MissingServer);
            if (zip.Entries.Count > 10000) throw new LlamaArchiveException(LlamaArchiveFailure.TooLarge);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested(); string path = SafeName(entry);
                if (!paths.Add(path.TrimEnd('/')) || path.TrimEnd('/').Equals(Stamp, StringComparison.OrdinalIgnoreCase)) throw new LlamaArchiveException(LlamaArchiveFailure.UnsafeEntry);
                string key = path.TrimEnd('/');
                if (!path.EndsWith("/") && parents.Contains(key)) throw new LlamaArchiveException(LlamaArchiveFailure.UnsafeEntry);
                for (int slash = key.LastIndexOf('/'); slash >= 0; slash = key.LastIndexOf('/', slash - 1))
                {
                    string parent = key.Substring(0, slash);
                    if (files.Contains(parent)) throw new LlamaArchiveException(LlamaArchiveFailure.UnsafeEntry);
                    parents.Add(parent); if (slash == 0) break;
                }
                if (!path.EndsWith("/")) files.Add(key);
                result.ExpandedBytes = checked(result.ExpandedBytes + entry.Length);
                if (result.ExpandedBytes > MaximumBytes) throw new LlamaArchiveException(LlamaArchiveFailure.TooLarge);
                if (!Path.GetFileName(path).Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (result.ServerEntry != null) throw new LlamaArchiveException(LlamaArchiveFailure.MultipleServers);
                using (var input = entry.Open()) CheckWindowsExecutable(input, ct);
                result.ServerEntry = path;
            }
            if (result.ServerEntry == null) throw new LlamaArchiveException(LlamaArchiveFailure.MissingServer);
            return result;
        }
        static string SafeName(ZipArchiveEntry entry)
        {
            string path = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(path) || path.Length > 1024 || path.StartsWith("/") || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new LlamaArchiveException(LlamaArchiveFailure.UnsafeEntry);
            foreach (string segment in path.TrimEnd('/').Split('/'))
            {
                foreach (char character in segment) if (character < 32) throw new LlamaArchiveException(LlamaArchiveFailure.UnsafeEntry);
                string stem = Path.GetFileNameWithoutExtension(segment).ToUpperInvariant();
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".") || segment.EndsWith(" ") ||
                    segment.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|', '\0' }) >= 0 || stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                    (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9'))
                    throw new LlamaArchiveException(LlamaArchiveFailure.UnsafeEntry);
            }
            return path;
        }
        static void CheckWindowsExecutable(Stream input, CancellationToken ct)
        {
            // Bounded PE header probe, without loading or executing any native code.
            var header = new byte[64]; ReadExactly(input, header, ct);
            if (header[0] != 'M' || header[1] != 'Z') throw new LlamaArchiveException(LlamaArchiveFailure.WrongPlatform);
            int offset = BitConverter.ToInt32(header, 60);
            if (offset < 64 || offset > 1024 * 1024) throw new LlamaArchiveException(LlamaArchiveFailure.WrongPlatform);
            var skip = new byte[Math.Min(offset - 64, 8192)]; int remaining = offset - 64;
            while (remaining > 0) { ct.ThrowIfCancellationRequested(); int read = input.Read(skip, 0, Math.Min(skip.Length, remaining)); if (read == 0) throw new LlamaArchiveException(LlamaArchiveFailure.InvalidZip); remaining -= read; }
            var pe = new byte[6]; ReadExactly(input, pe, ct);
            if (pe[0] != 'P' || pe[1] != 'E' || pe[2] != 0 || pe[3] != 0 || BitConverter.ToUInt16(pe, 4) != Machine)
                throw new LlamaArchiveException(LlamaArchiveFailure.WrongPlatform);
        }
        static void ReadExactly(Stream input, byte[] bytes, CancellationToken ct)
        {
            int offset = 0;
            while (offset < bytes.Length) { ct.ThrowIfCancellationRequested(); int read = input.Read(bytes, offset, bytes.Length - offset); if (read == 0) throw new LlamaArchiveException(LlamaArchiveFailure.InvalidZip); offset += read; }
        }
        public static Task<string> ExtractAsync(string archive, string reviewedSha256, string managedRoot, Action<SetupProgress> progress, CancellationToken ct)
            => ExtractAsync(archive, reviewedSha256, managedRoot, progress, RuntimeAcquisition.RequireSpace, ct);

        internal static Task<string> ExtractAsync(string archive, string reviewedSha256, string managedRoot, Action<SetupProgress> progress, Action<string, long> requireSpace, CancellationToken ct)
            => Task.Run(() => Extract(archive, reviewedSha256, managedRoot, progress, requireSpace, ct), ct);

        static string Extract(string archive, string reviewedSha256, string managedRoot, Action<SetupProgress> progress, Action<string, long> requireSpace, CancellationToken ct)
        {
            string staging = null; bool ownsStaging = false;
            try
            {
                ct.ThrowIfCancellationRequested();
                using var file = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
                var info = Inspect(file, ct);
                if (!string.Equals(info.Sha256, reviewedSha256, StringComparison.OrdinalIgnoreCase)) throw new LlamaArchiveException(LlamaArchiveFailure.Changed);
                string root = Path.GetFullPath(managedRoot), destination = Path.Combine(root, info.Sha256);
                string executable = Path.Combine(destination, info.ServerEntry.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(destination))
                {
                    if (File.Exists(executable) && File.Exists(Path.Combine(destination, Stamp)) && File.ReadAllText(Path.Combine(destination, Stamp)) == info.Sha256)
                    { ct.ThrowIfCancellationRequested(); return executable; }
                    throw new LlamaArchiveException(LlamaArchiveFailure.Access);
                }
                requireSpace(root, checked(info.ExpandedBytes + ReserveBytes)); ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(root); staging = Path.Combine(root, ".extract-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(staging)) throw new LlamaArchiveException(LlamaArchiveFailure.Access);
                Directory.CreateDirectory(staging); ownsStaging = true; file.Position = 0;
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read, true))
                {
                    long completed = 0; var buffer = new byte[131072]; var clock = System.Diagnostics.Stopwatch.StartNew();
                    foreach (var entry in zip.Entries)
                    {
                        ct.ThrowIfCancellationRequested(); string name = SafeName(entry);
                        string target = Path.GetFullPath(Path.Combine(staging, name.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new LlamaArchiveException(LlamaArchiveFailure.UnsafeEntry);
                        if (name.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using var input = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                        long copied = 0; int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested(); copied += read; completed += read;
                            if (copied > entry.Length || completed > MaximumBytes) throw new LlamaArchiveException(LlamaArchiveFailure.InvalidZip);
                            output.Write(buffer, 0, read);
                            if (clock.ElapsedMilliseconds >= 50) { progress?.Invoke(new SetupProgress { Stage = SetupStage.Runtime, Detail = "extracting archive", CompletedBytes = completed, TotalBytes = info.ExpandedBytes }); clock.Restart(); }
                        }
                        if (copied != entry.Length) throw new LlamaArchiveException(LlamaArchiveFailure.InvalidZip);
                    }
                    ct.ThrowIfCancellationRequested();
                    progress?.Invoke(new SetupProgress { Stage = SetupStage.Runtime, Detail = "extracting archive", CompletedBytes = completed, TotalBytes = info.ExpandedBytes });
                }
                ct.ThrowIfCancellationRequested(); File.WriteAllText(Path.Combine(staging, Stamp), info.Sha256);
                Directory.Move(staging, destination); ownsStaging = false; return executable;
            }
            catch (OperationCanceledException) { throw; }
            catch (SetupException) { throw; }
            catch (LlamaArchiveException ex) { throw new SetupException(SetupProblem.RuntimeArchive, ex.Failure.ToString(), ex); }
            catch (InvalidDataException ex) { throw new SetupException(SetupProblem.RuntimeArchive, LlamaArchiveFailure.InvalidZip.ToString(), ex); }
            catch (UnauthorizedAccessException ex) { throw new SetupException(SetupProblem.RuntimeArchive, LlamaArchiveFailure.Access.ToString(), ex); }
            catch (IOException ex) { throw new SetupException(RuntimeAcquisition.IsDiskFull(ex) ? SetupProblem.Space : SetupProblem.RuntimeArchive, (ex is InvalidDataException || ex is FileNotFoundException || ex is DirectoryNotFoundException ? LlamaArchiveFailure.InvalidZip : LlamaArchiveFailure.Access).ToString(), ex); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is OverflowException || ex is System.Security.SecurityException)
            { throw new SetupException(SetupProblem.RuntimeArchive, LlamaArchiveFailure.InvalidZip.ToString(), ex); }
            finally
            {
                if (ownsStaging && staging != null && Path.GetFullPath(staging).StartsWith(Path.GetFullPath(managedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + ".extract-", StringComparison.OrdinalIgnoreCase))
                { try { Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }
    }
}
