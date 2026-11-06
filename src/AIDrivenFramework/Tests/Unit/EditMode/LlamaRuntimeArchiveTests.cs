using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using AIDrivenFW.Config;
using AIDrivenFW.Setup;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AIDrivenFW.Tests.Unit
{
    public class LlamaRuntimeArchiveTests
    {
        string root;
        [SetUp] public void SetUp() => root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "llama-zip-test-" + Guid.NewGuid().ToString("N"))).FullName;
        [TearDown] public void TearDown()
        {
            if (root != null && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
        static byte[] Executable(bool wrongMachine = false)
        {
            // Header-only inert fixture. Never executed or loaded as native code.
            var bytes = new byte[70]; bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; bytes[60] = 64; bytes[64] = (byte)'P'; bytes[65] = (byte)'E';
            ushort machine = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? (ushort)0x8664 : RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? (ushort)0xaa64 : (ushort)0x14c;
            if (wrongMachine) machine = machine == 0x8664 ? (ushort)0xaa64 : (ushort)0x8664;
            Array.Copy(BitConverter.GetBytes(machine), 0, bytes, 68, 2); return bytes;
        }
        string Zip(params (string name, byte[] data)[] entries)
        {
            string path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var item in entries) { var entry = zip.CreateEntry(item.name); using var stream = entry.Open(); stream.Write(item.data, 0, item.data.Length); }
            return path;
        }
        string RuntimeZip() => Zip(("runtime/llama-server.exe", Executable()), ("runtime/ggml.dll", new byte[] { 1, 2, 3 }), ("LICENSE", new byte[] { 4, 5 }), ("notices/third-party.txt", new byte[] { 6 }));
        PlayerSetupPlan Plan(string archive)
        {
            var info = LlamaRuntimeArchive.Inspect(archive, CancellationToken.None); string model = Path.Combine(root, "model.gguf"); File.WriteAllText(model, "inert fixture");
            return new PlayerSetupPlan { Mode = "Llama", Model = "model.gguf", ModelPath = model, ModelBytes = 13, RuntimePath = "", RuntimeArchivePath = archive, RuntimeArchiveSha256 = info.Sha256, RuntimeExpandedBytes = info.ExpandedBytes };
        }
        [Test]
        public void InspectFindsNestedServerAndReviewsWholeArchiveWithoutExtracting()
        {
            var info = LlamaRuntimeArchive.Inspect(RuntimeZip(), CancellationToken.None);
            Assert.AreEqual("runtime/llama-server.exe", info.ServerEntry); Assert.AreEqual(76, info.ExpandedBytes); Assert.AreEqual(64, info.Sha256.Length);
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "runtime")));
        }
        [UnityTest]
        public IEnumerator ExtractionKeepsDllsNoticesAndReusesOnlyTheCompletedInstall() => UniTask.ToCoroutine(async () =>
        {
            string archive = RuntimeZip(), install = Path.Combine(root, "installed"); var info = LlamaRuntimeArchive.Inspect(archive, CancellationToken.None); SetupProgress last = null;
            string executable = await LlamaRuntimeArchive.ExtractAsync(archive, info.Sha256, install, p => last = p, CancellationToken.None);
            Assert.IsTrue(File.Exists(executable)); Assert.IsTrue(executable.EndsWith(Path.Combine("runtime", "llama-server.exe")));
            string directory = Directory.GetParent(Path.GetDirectoryName(executable)).FullName;
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(executable), "ggml.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(directory, "LICENSE"))); Assert.IsTrue(File.Exists(Path.Combine(directory, "notices", "third-party.txt")));
            Assert.AreEqual(info.ExpandedBytes, last.CompletedBytes); Assert.AreEqual(last.CompletedBytes, last.TotalBytes);
            Assert.AreEqual(executable, await LlamaRuntimeArchive.ExtractAsync(archive, info.Sha256, install, null, CancellationToken.None));
            Assert.AreEqual(1, Directory.GetDirectories(install).Length); Assert.IsTrue(File.Exists(archive));
        });
        [Test] public void DamagedZipHasTypedFailure()
        {
            string path = Path.Combine(root, "broken.zip"); File.WriteAllText(path, "not a ZIP");
            Assert.AreEqual(LlamaArchiveFailure.InvalidZip, Assert.Throws<LlamaArchiveException>(() => LlamaRuntimeArchive.Inspect(path, CancellationToken.None)).Failure);
        }
        [Test] public void SourceCodeArchiveIsNotAWindowsRuntime()
        {
            string archive = Zip(("examples/server/server.cpp", new byte[] { 1 }));
            Assert.AreEqual(LlamaArchiveFailure.MissingServer, Assert.Throws<LlamaArchiveException>(() => LlamaRuntimeArchive.Inspect(archive, CancellationToken.None)).Failure);
        }
        [Test] public void MultipleRuntimeVariantsRequireAChoice()
        {
            string archive = Zip(("cpu/llama-server.exe", Executable()), ("cuda/llama-server.exe", Executable()));
            Assert.AreEqual(LlamaArchiveFailure.MultipleServers, Assert.Throws<LlamaArchiveException>(() => LlamaRuntimeArchive.Inspect(archive, CancellationToken.None)).Failure);
        }
        [Test] public void WrongArchitectureIsRejectedBeforeExtraction()
        {
            string archive = Zip(("llama-server.exe", Executable(true)));
            Assert.AreEqual(LlamaArchiveFailure.WrongPlatform, Assert.Throws<LlamaArchiveException>(() => LlamaRuntimeArchive.Inspect(archive, CancellationToken.None)).Failure);
        }
        [Test] public void ExcessiveEntryCountIsRejected()
        {
            string archive = Zip(("llama-server.exe", Executable()));
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) for (int i = 0; i < 10000; i++) zip.CreateEntry("empty-" + i);
            Assert.AreEqual(LlamaArchiveFailure.TooLarge, Assert.Throws<LlamaArchiveException>(() => LlamaRuntimeArchive.Inspect(archive, CancellationToken.None)).Failure);
        }
        [Test] public void CancelledInspectionDoesNotReadOrExtractTheArchive()
        {
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => LlamaRuntimeArchive.Inspect(Path.Combine(root, "missing.zip"), cancel.Token));
            Assert.IsEmpty(Directory.GetFiles(root));
        }
        [TestCase("../escape.dll")]
        [TestCase("/absolute.dll")]
        [TestCase("C:/absolute.dll")]
        [TestCase("bin/../../escape.dll")]
        [TestCase("bin/license:stream")]
        [TestCase("bin/CON.txt")]
        [TestCase("bin/name.")]
        [TestCase(".setup-source.sha256")]
        public void UnsafePathsAreRejected(string entry)
        {
            string archive = Zip(("llama-server.exe", Executable()), (entry, new byte[] { 1 }));
            Assert.AreEqual(LlamaArchiveFailure.UnsafeEntry, Assert.Throws<LlamaArchiveException>(() => LlamaRuntimeArchive.Inspect(archive, CancellationToken.None)).Failure);
        }
        [Test] public void SymlinksAreNotExtracted()
        {
            string archive = RuntimeZip(); using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) zip.CreateEntry("linked").ExternalAttributes = unchecked((int)0xa1ff0000);
            Assert.AreEqual(LlamaArchiveFailure.UnsafeEntry, Assert.Throws<LlamaArchiveException>(() => LlamaRuntimeArchive.Inspect(archive, CancellationToken.None)).Failure);
        }
        [Test] public void ConflictingPathsAreRejected()
        {
            foreach (var entries in new[] {
                new[] { ("llama-server.exe", Executable()), ("LIB.dll", new byte[] { 1 }), ("lib.dll", new byte[] { 2 }) },
                new[] { ("llama-server.exe", Executable()), ("bin", new byte[] { 1 }), ("bin/lib.dll", new byte[] { 2 }) } })
                Assert.AreEqual(LlamaArchiveFailure.UnsafeEntry, Assert.Throws<LlamaArchiveException>(() => LlamaRuntimeArchive.Inspect(Zip(entries), CancellationToken.None)).Failure);
        }
        [UnityTest] public IEnumerator ChangedArchiveCannotBeInstalledUnderTheOldConsent() => UniTask.ToCoroutine(async () =>
        {
            string archive = RuntimeZip(), install = Path.Combine(root, "installed"); var info = LlamaRuntimeArchive.Inspect(archive, CancellationToken.None);
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) zip.CreateEntry("additional.txt");
            try { await LlamaRuntimeArchive.ExtractAsync(archive, info.Sha256, install, null, CancellationToken.None); Assert.Fail("Changed archive accepted"); }
            catch (SetupException e) { Assert.AreEqual(SetupProblem.RuntimeArchive, e.Problem); Assert.AreEqual(LlamaArchiveFailure.Changed.ToString(), e.Message); }
            Assert.IsFalse(Directory.Exists(install));
        });
        [UnityTest] public IEnumerator CancellationRemovesOnlyTheCurrentStagingDirectory() => UniTask.ToCoroutine(async () =>
        {
            string archive = RuntimeZip(), install = Path.Combine(root, "installed"); var info = LlamaRuntimeArchive.Inspect(archive, CancellationToken.None); using var cancel = new CancellationTokenSource();
            try { await LlamaRuntimeArchive.ExtractAsync(archive, info.Sha256, install, _ => cancel.Cancel(), cancel.Token); Assert.Fail("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            Assert.IsTrue(File.Exists(archive)); Assert.IsEmpty(Directory.GetDirectories(install));
        });
        [UnityTest] public IEnumerator SpaceFailurePrecedesAnyExtractionOutput() => UniTask.ToCoroutine(async () =>
        {
            string archive = RuntimeZip(), install = Path.Combine(root, "installed"); var info = LlamaRuntimeArchive.Inspect(archive, CancellationToken.None); long required = 0;
            try { await LlamaRuntimeArchive.ExtractAsync(archive, info.Sha256, install, null, (p, bytes) => { required = bytes; throw new SetupException(SetupProblem.Space, "fixture"); }, CancellationToken.None); Assert.Fail("Space preflight ignored"); }
            catch (SetupException e) { Assert.AreEqual(SetupProblem.Space, e.Problem); }
            Assert.Greater(required, info.ExpandedBytes); Assert.IsFalse(Directory.Exists(install));
        });
        [UnityTest] public IEnumerator InaccessibleDestinationPreservesExistingData() => UniTask.ToCoroutine(async () =>
        {
            string archive = RuntimeZip(), install = Path.Combine(root, "existing-file"); File.WriteAllText(install, "keep"); var info = LlamaRuntimeArchive.Inspect(archive, CancellationToken.None);
            try { await LlamaRuntimeArchive.ExtractAsync(archive, info.Sha256, install, null, CancellationToken.None); Assert.Fail("Invalid destination accepted"); }
            catch (SetupException e) { Assert.AreEqual(SetupProblem.RuntimeArchive, e.Problem); Assert.AreEqual(LlamaArchiveFailure.Access.ToString(), e.Message); }
            Assert.AreEqual("keep", File.ReadAllText(install));
        });
        [Test] public void ZipPlanRequiresItsReviewDigestAndExpansionSize()
        {
            var plan = Plan(RuntimeZip()); Assert.DoesNotThrow(plan.Validate);
            plan.RuntimeArchiveSha256 = ""; Assert.Throws<ArgumentException>(plan.Validate);
        }
        [UnityTest] public IEnumerator ZipFlowDoesNotExtractUntilConsentAndSavesTheExtractedPath() => UniTask.ToCoroutine(async () =>
        {
            var plan = Plan(RuntimeZip()); string install = Path.Combine(root, "installed"); var services = new ZipServices(install); var flow = new PlayerSetupFlow(services);
            try { await flow.RunAsync(plan, false, CancellationToken.None); Assert.Fail("Consent required"); } catch (InvalidOperationException) { }
            Assert.IsFalse(Directory.Exists(install)); Assert.IsNull(services.Saved);
            Assert.IsTrue(await flow.RunAsync(plan, true, CancellationToken.None));
            Assert.IsTrue(File.Exists(services.Saved.RuntimePath)); Assert.AreEqual(plan.RuntimeArchiveSha256, services.Saved.RuntimeArchiveSha256); Assert.IsTrue(services.Verified);
        });
        sealed class ZipServices : IPlayerSetupServices
        {
            readonly string install; public ModelInfo Saved; public bool Verified;
            public ZipServices(string install) => this.install = install;
            public async UniTask<string> PrepareRuntimeAsync(PlayerSetupPlan plan, Action<SetupProgress> progress, CancellationToken ct) => await LlamaRuntimeArchive.ExtractAsync(plan.RuntimeArchivePath, plan.RuntimeArchiveSha256, install, progress, ct);
            public UniTask PrepareModelAsync(PlayerSetupPlan plan, Action<SetupProgress> progress, CancellationToken ct) => UniTask.CompletedTask;
            public UniTask VerifyAsync(PlayerSetupPlan plan, CancellationToken ct) { Verified = true; return UniTask.CompletedTask; }
            public void RecordAttempt(PlayerSetupPlan plan, string utc, SetupStage stage) { }
            public void Commit(ModelInfo settings) { if (!Verified) throw new Exception("No verification"); Saved = settings; }
            public void Dispose() { }
        }
    }
}
