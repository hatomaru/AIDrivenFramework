using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Text.RegularExpressions;
using AIDrivenFW.API;
using AIDrivenFW.Core;
using AIDrivenFW.Config;
using AIDrivenFW.Setup;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;
using UnityEditor;

namespace AIDrivenFW.Tests.Unit
{
    public class PlayerSetupFlowTests
    {
        sealed class Services : IPlayerSetupServices
        {
            public readonly List<string> Calls = new List<string>();
            public ModelInfo Saved;
            public Action AtModel;
            public Exception Failure;
            public SetupStage FailAt = SetupStage.Model;
            public void RecordAttempt(PlayerSetupPlan plan, string time, SetupStage stage) => Calls.Add("journal:" + stage);
            public UniTask<string> PrepareRuntimeAsync(PlayerSetupPlan plan, Action<SetupProgress> p, CancellationToken ct)
            {
                Calls.Add("runtime"); ct.ThrowIfCancellationRequested();
                if (FailAt == SetupStage.Runtime && Failure != null) throw Failure;
                return UniTask.FromResult("verified-runtime");
            }
            public UniTask PrepareModelAsync(PlayerSetupPlan plan, Action<SetupProgress> p, CancellationToken ct)
            {
                Calls.Add("model"); AtModel?.Invoke(); ct.ThrowIfCancellationRequested();
                if (FailAt == SetupStage.Model && Failure != null) throw Failure;
                return UniTask.CompletedTask;
            }
            public UniTask VerifyAsync(PlayerSetupPlan plan, CancellationToken ct)
            {
                Calls.Add("real-generation"); ct.ThrowIfCancellationRequested();
                if (FailAt == SetupStage.Verify && Failure != null) throw Failure;
                return UniTask.CompletedTask;
            }
            public void Commit(ModelInfo settings) { Calls.Add("commit"); if (FailAt == SetupStage.Complete && Failure != null) throw Failure; Saved = settings; }
            public void Dispose() { }
        }

        [UnityTest]
        public IEnumerator RefusalHasNoJournalDownloadProcessOrSave() => UniTask.ToCoroutine(async () =>
        {
            var service = new Services(); var flow = new PlayerSetupFlow(service);
            bool rejected = false;
            try { await flow.RunAsync(new PlayerSetupPlan(), false, CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            Assert.IsTrue(rejected); Assert.IsEmpty(service.Calls);
        });

        [UnityTest]
        public IEnumerator ConsentAutomaticallyAcquiresVerifiesThenCommits() => UniTask.ToCoroutine(async () =>
        {
            var service = new Services(); var flow = new PlayerSetupFlow(service);
            Assert.IsTrue(await flow.RunAsync(new PlayerSetupPlan(), true, CancellationToken.None));
            CollectionAssert.AreEqual(new[] { "journal:Runtime", "runtime", "journal:Model", "model", "journal:Verify", "real-generation", "commit" }, service.Calls);
            Assert.IsTrue(service.Saved.HasPlayerConsent);
            Assert.AreEqual("qwen3:0.6b", service.Saved.Name);
            Assert.AreEqual("verified-runtime", service.Saved.RuntimePath);
            Assert.AreEqual(SetupStage.Complete, flow.Stage);
        });

        [UnityTest]
        public IEnumerator SelectionIsSnapshottedBeforeAsyncWork() => UniTask.ToCoroutine(async () =>
        {
            var selection = new PlayerSetupPlan();
            var service = new Services { AtModel = () => selection.Model = "changed-after-consent" };
            Assert.IsTrue(await new PlayerSetupFlow(service).RunAsync(selection, true, CancellationToken.None));
            Assert.AreEqual("qwen3:0.6b", service.Saved.Name);
        });

        [UnityTest]
        public IEnumerator PauseDoesNotOverwriteAndRetryResumesSameSelection() => UniTask.ToCoroutine(async () =>
        {
            using var cancel = new CancellationTokenSource();
            var previous = new ModelInfo { Name = "previous", Mode = "Llama" };
            var service = new Services { Saved = previous, AtModel = cancel.Cancel };
            var flow = new PlayerSetupFlow(service);
            Assert.IsFalse(await flow.RunAsync(new PlayerSetupPlan(), true, cancel.Token));
            Assert.AreEqual(SetupStage.Cancelled, flow.Stage); Assert.AreSame(previous, service.Saved);
            service.AtModel = null;
            Assert.IsTrue(await flow.RunAsync(new PlayerSetupPlan(), true, CancellationToken.None));
            Assert.AreEqual("qwen3:0.6b", service.Saved.Name);
        });

        [UnityTest]
        public IEnumerator SpaceFailurePreservesWorkingSettings() => FailurePreservesSettings(SetupStage.Runtime, SetupProblem.Space);
        [UnityTest]
        public IEnumerator PullFailurePreservesWorkingSettings() => FailurePreservesSettings(SetupStage.Model, SetupProblem.Connection);
        [UnityTest]
        public IEnumerator GenerationFailureCannotMarkReady() => FailurePreservesSettings(SetupStage.Verify, SetupProblem.Verification);

        [UnityTest]
        public IEnumerator SavingFailurePreservesOldSettingsAndShowsStorageGuidance() => UniTask.ToCoroutine(async () =>
        {
            var previous = new ModelInfo { Name = "working" };
            var service = new Services { Saved = previous, FailAt = SetupStage.Complete, Failure = new UnauthorizedAccessException("fixture") };
            var flow = new PlayerSetupFlow(service);
            Assert.IsFalse(await flow.RunAsync(new PlayerSetupPlan(), true, CancellationToken.None));
            Assert.AreSame(previous, service.Saved); Assert.AreEqual(SetupProblem.Settings, flow.Problem);
        });

        IEnumerator FailurePreservesSettings(SetupStage stage, SetupProblem problem) => UniTask.ToCoroutine(async () =>
        {
            var previous = new ModelInfo { Name = "working" };
            var service = new Services { Saved = previous, FailAt = stage, Failure = new SetupException(problem, "fixture failure") };
            var flow = new PlayerSetupFlow(service);
            Assert.IsFalse(await flow.RunAsync(new PlayerSetupPlan(), true, CancellationToken.None));
            Assert.AreSame(previous, service.Saved); Assert.AreEqual(SetupStage.Failed, flow.Stage); Assert.AreEqual(problem, flow.Problem);
            Assert.IsFalse(service.Calls.Contains("commit"));
        });

        [UnityTest]
        public IEnumerator RestartWithSavedSelectionStillRequiresConsent() => UniTask.ToCoroutine(async () =>
        {
            var service = new Services(); var original = PlayerSetupPlan.Choices()[1];
            var restored = UnityEngine.JsonUtility.FromJson<PlayerSetupPlan>(UnityEngine.JsonUtility.ToJson(original));
            try { await new PlayerSetupFlow(service).RunAsync(restored, false, CancellationToken.None); Assert.Fail("Consent missing"); }
            catch (InvalidOperationException) { }
            Assert.IsEmpty(service.Calls);
            Assert.IsTrue(await new PlayerSetupFlow(service).RunAsync(restored, true, CancellationToken.None));
            Assert.AreEqual(original.Model, service.Saved.Name);
        });

        [TestCase("qwen3:0.6b", true)]
        [TestCase("author/custom-model:tag", true)]
        [TestCase("-model", false)]
        [TestCase("qwen:cloud", false)]
        [TestCase("model\n--evil", false)]
        public void ModelTagsAreValidated(string tag, bool valid)
        { var plan = new PlayerSetupPlan { Model = tag }; if (valid) Assert.DoesNotThrow(plan.Validate); else Assert.Throws<ArgumentException>(plan.Validate); }

        [TestCase("http://example.com/license")]
        [TestCase("file:///license")]
        [TestCase("https://user:pass@example.com/")]
        public void TermsMustBePublicHttps(string url) => Assert.Throws<ArgumentException>(() => PlayerSetupPlan.ValidateHttps(url));

        [Test]
        public void EveryRecommendedChoiceHasSeparateTermsAndPositiveSize()
        { foreach (var plan in PlayerSetupPlan.Choices()) { Assert.DoesNotThrow(plan.Validate); Assert.Greater(plan.ModelBytes, 0); Assert.AreNotEqual(plan.ModelTerms, plan.RuntimeTerms); } }

        [Test]
        public void LegacySettingsDoNotSilentlyGainConsent()
        { var settings = ModelInfo.FromJson("{\"Name\":\"old-model\",\"Mode\":\"Ollama\"}"); Assert.IsFalse(settings.HasPlayerConsent); }

        [TestCase("qwen3:0.6b", "https://ollama.com/library/qwen3:0.6b")]
        [TestCase("author/custom:tag", "https://ollama.com/author/custom:tag")]
        public void CustomOllamaTermsUseTheCorrectDistributionPage(string tag, string page)
            => Assert.AreEqual(page, PlayerSetupPlan.OllamaModelPage(tag));

        [Test]
        public void LocalTermsReceiptCannotReplaceOllamaTerms()
        {
            var plan = new PlayerSetupPlan { ModelTerms = "", ReviewModelTermsAtSource = true };
            Assert.Throws<ArgumentException>(plan.Validate);
            Assert.IsFalse(new ModelInfo { Mode = "Ollama", ConsentRevision = ModelInfo.CurrentConsentRevision,
                ConsentUtc = "fixture", LocalModelTermsReviewed = true, ModelPath = "file.gguf" }.HasPlayerConsent);
        }

        [UnityTest]
        public IEnumerator PlayerOwnedGgufCanProceedWithoutTypingLicenseUrl() => UniTask.ToCoroutine(async () =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "local-model-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try
            {
                string runtime = Path.Combine(folder, "llama-server.exe"), model = Path.Combine(folder, "custom.gguf");
                File.WriteAllText(runtime, "inert fixture"); File.WriteAllText(model, "inert fixture");
                var plan = new PlayerSetupPlan { Mode = "Llama", Model = "custom.gguf", ModelTerms = "", ModelSource = "",
                    RuntimePath = runtime, ModelPath = model, ModelBytes = 13, ReviewModelTermsAtSource = true };
                var service = new Services(); var flow = new PlayerSetupFlow(service);
                try { await flow.RunAsync(plan, false, CancellationToken.None); Assert.Fail("Consent required"); } catch (InvalidOperationException) { }
                Assert.IsEmpty(service.Calls);
                Assert.IsTrue(await flow.RunAsync(plan, true, CancellationToken.None));
                Assert.IsEmpty(service.Saved.ModelTermsUrl); Assert.IsTrue(service.Saved.LocalModelTermsReviewed);
                Assert.IsTrue(ModelInfo.FromJson(service.Saved.ToJson()).HasPlayerConsent);
                plan.ReviewModelTermsAtSource = false; Assert.Throws<ArgumentException>(plan.Validate);
                plan.ReviewModelTermsAtSource = true; plan.ModelTerms = "http://example.com/terms"; Assert.Throws<ArgumentException>(plan.Validate);
                plan.ModelTerms = ""; plan.RuntimePath = model; Assert.Throws<ArgumentException>(plan.Validate);
            }
            finally { Directory.Delete(folder, true); }
        });

        [Test]
        public void DiskSpacePreflightRejectsImpossibleDownload() => Assert.Throws<SetupException>(() => RuntimeAcquisition.RequireSpace(Path.GetTempPath(), long.MaxValue));

        [Test]
        public void AtomicSaveReplacesCompleteJson()
        {
            string directory = Path.Combine(Path.GetTempPath(), "setup-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(directory, "settings.json");
                ModelInfo.WriteAtomic(path, "{\"Name\":\"old\"}"); ModelInfo.WriteAtomic(path, "{\"Name\":\"verified\"}");
                Assert.AreEqual("verified", ModelInfo.FromJson(File.ReadAllText(path)).Name); Assert.IsFalse(File.Exists(path + ".tmp"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [TestCase("ollama.exe", true)]
        [TestCase("../escape.exe", false)]
        [TestCase("nested/../../escape.exe", false)]
        [TestCase("C:/escape.exe", false)]
        public void VerifiedArchiveCannotEscapeStaging(string name, bool valid)
        {
            string directory = Path.Combine(Path.GetTempPath(), "setup-archive-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                string archive = Path.Combine(directory, "test.zip"), staging = Path.Combine(directory, "staging");
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("fixture");
                string hash; using (var sha = SHA256.Create()) using (var file = File.OpenRead(archive)) hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                if (valid) { RuntimeAcquisition.VerifyAndExtract(archive, staging, hash, CancellationToken.None); Assert.AreEqual("fixture", File.ReadAllText(Path.Combine(staging, name))); }
                else Assert.Throws<InvalidDataException>(() => RuntimeAcquisition.VerifyAndExtract(archive, staging, hash, CancellationToken.None));
                Assert.Throws<InvalidDataException>(() => RuntimeAcquisition.VerifyAndExtract(archive, staging, "wrong-hash", CancellationToken.None));
            }
            finally { Directory.Delete(directory, true); }
        }

        [Test]
        public void PullErrorNeverLooksLikeSuccess()
        { Assert.Throws<SetupException>(() => PlayerSetupServices.ParsePullLine("{\"error\":\"model not found\"}", null)); }

        [Test]
        public void PullProgressReportsBytes()
        { SetupProgress result = null; PlayerSetupServices.ParsePullLine("{\"status\":\"pulling layer\",\"completed\":23,\"total\":100}", p => result = p); Assert.AreEqual(23, result.CompletedBytes); Assert.AreEqual(100, result.TotalBytes); }

        [UnityTest]
        public IEnumerator AutomaticRecoveryHonorsUnsuccessfulSetup() => UniTask.ToCoroutine(async () =>
        {
            var scenes = EditorBuildSettings.scenes;
            var executor = new FakeAIExecutor("fixture", "must not generate after false");
            for (int i = 0; i < 6; i++) executor.EnqueueGenerateFailure(new GenAIRetryableException("fixture failure"));
            var ai = new GenAI(executor);
            try
            {
                EditorBuildSettings.scenes = Array.Empty<EditorBuildSettingsScene>();
                LogAssert.Expect(LogType.Error, new Regex("^Error during test generation:"));
                LogAssert.Expect(LogType.Error, "AIDrivenSetup scene is not added to the build settings. Please add it to the build settings to proceed with the setup.");
                try { await ai.Generate("test"); Assert.Fail("Unsuccessful setup was ignored"); }
                catch (GenAIExecutionException) { }
                Assert.AreEqual(6, executor.GenerateCallCount, "No extra generation after Initialize returns false.");
            }
            finally { ai.KillProcess(); EditorBuildSettings.scenes = scenes; }
        });
    }
}
