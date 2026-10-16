using AIDrivenFW.API;
using AIDrivenFW.Config;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIDrivenFW.Tests.Unit
{
    [Timeout(10000)]
    public class ExecutorCapabilityTests
    {
        private sealed class GenerateOnly : IGenerateExecutor
        {
            public readonly FakeAIExecutor Inner = new FakeAIExecutor("remote", "raw response");
            public UniTask<string> GenerateAsync(string sysInput, string input, CancellationToken ct,
                Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000)
                => Inner.GenerateAsync(sysInput, input, ct, onUpdate, progress, timeoutMs);
            public UniTask<bool> IsGenerated(CancellationToken token, Action<string> onUpdate = null)
                => throw new AssertionException("Process-free generation must not be polled.");
        }

        private sealed class CustomInput : IExecutorInput
        {
            public int Validations, Serializations;
            public bool Invalid;
            public string Payload = "{\"state\":{\"turn\":7},\"choices\":[\"left\",\"right\"]}";
            public void Validate()
            {
                Validations++;
                if (Invalid) throw new ArgumentException("turn is invalid");
            }
            public string Serialize() { Serializations++; return Payload; }
        }

        private sealed class NullExtractor : IExtractExecutor
        {
            public string ExtractAssistantOutput(string raw) => null;
        }

        private sealed class InlineProgress : IProgress<float>
        {
            public float Last;
            public void Report(float value) => Last = value;
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            public string Body, MediaType;
            public int Calls;
            public HttpStatusCode Status = HttpStatusCode.OK;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Calls++;
                Body = await request.Content.ReadAsStringAsync();
                MediaType = request.Content.Headers.ContentType.MediaType;
                return new HttpResponseMessage(Status) { Content = new StringContent("{\"decision\":\"left\"}") };
            }
        }

        [Test]
        public async Task ProcessFree_NoConfig_PreservesRawOutputAndSkipsProcessAndPolling()
        {
            var backend = new GenerateOnly();
            var context = new AIExecutorContext(backend);
            Assert.IsNull(context.ProcessExecutor);
            Assert.IsNull(context.ExtractExecutor);
            Assert.IsNull(context.ArgumentsExecutor);
            var ai = new GenAI(context);
            Assert.AreEqual("raw response", await ai.Generate("input"));
            Assert.AreEqual(string.Empty, backend.Inner.LastSystemInput);
            Assert.AreEqual(0, backend.Inner.ReceiveCallCount);
            Assert.AreEqual(0, backend.Inner.StartProcessCallCount);
            Assert.AreEqual(0, backend.Inner.SetDefaultArgumentsCallCount);
            Assert.AreEqual(string.Empty, ai.IsFoundAISoftware());
            ai.KillProcess();
            ai.SetExecutor(new GenerateOnly());
            Assert.AreEqual("raw response", await ai.Generate("again"));
        }

        [Test]
        public async Task ProcessFree_ExplicitConfig_ForwardsSystemPrompt()
        {
            var backend = new GenerateOnly();
            var config = ScriptableObject.CreateInstance<GenAIConfig>();
            try
            {
                config.sysPrompt = "system";
                Assert.AreEqual("raw response", await new GenAI(backend).Generate("input", config));
                Assert.AreEqual("system", backend.Inner.LastSystemInput);
            }
            finally { UnityEngine.Object.DestroyImmediate(config); }
        }

        [Test]
        public async Task CustomInput_ValidatesAndSerializesOnceAcrossRetries()
        {
            var backend = new GenerateOnly();
            backend.Inner.EnqueueGenerateFailure(new GenAIRetryableException("temporary"));
            var input = new CustomInput();
            string chunk = null;
            Assert.AreEqual("raw response", await new GenAI(backend).GenerateInput(input, onUpdate: value => chunk = value));
            Assert.AreEqual(input.Payload, backend.Inner.LastInput);
            Assert.AreEqual(1, input.Validations);
            Assert.AreEqual(1, input.Serializations);
            Assert.AreEqual(2, backend.Inner.GenerateCallCount);
            Assert.AreEqual("raw response", chunk);
        }

        [Test]
        public void CustomInput_InvalidOrNullSerialization_DoesNotCallBackend()
        {
            var backend = new GenerateOnly();
            var ai = new GenAI(backend);
            var invalid = new CustomInput { Invalid = true };
            Assert.Throws<ArgumentException>(() => ai.GenerateInput(invalid));
            Assert.AreEqual(0, invalid.Serializations);
            Assert.Throws<ArgumentException>(() => ai.GenerateInput(new CustomInput { Payload = null }));
            Assert.Throws<ArgumentNullException>(() => ai.GenerateInput(null));
            Assert.AreEqual(0, backend.Inner.GenerateCallCount);
        }

        [Test]
        public void CustomInput_PreCancelled_DoesNotSerialize()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var input = new CustomInput();
            Assert.Throws<OperationCanceledException>(() => new GenAI(new GenerateOnly()).GenerateInput(input, ct: cts.Token));
            Assert.AreEqual(0, input.Validations);
            Assert.AreEqual(0, input.Serializations);
        }

        [Test]
        public async Task ProcessFree_Cancellation_PreservesCallerTokenAndDoesNotRetry()
        {
            var backend = new GenerateOnly();
            backend.Inner.BlockGeneration();
            using var cts = new CancellationTokenSource();
            Task<string> task = new GenAI(backend).Generate("input", ct: cts.Token).AsTask();
            await backend.Inner.GenerationStarted;
            cts.Cancel();
            try { await task; Assert.Fail("Expected cancellation"); }
            catch (OperationCanceledException ex) { Assert.AreEqual(cts.Token, ex.CancellationToken); }
            Assert.AreEqual(1, backend.Inner.GenerateCallCount);
            Assert.AreEqual(0, backend.Inner.KillProcessCallCount);
        }

        [Test]
        public async Task ProcessFree_Timeout_IsRealtimeAndDoesNotRetry()
        {
            var backend = new GenerateOnly();
            backend.Inner.BlockGeneration();
            float saved = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                try { await new GenAI(backend).Generate("input", timeoutMs: 200); Assert.Fail("Expected timeout"); }
                catch (TimeoutException) { }
            }
            finally { Time.timeScale = saved; }
            Assert.AreEqual(1, backend.Inner.GenerateCallCount);
            Assert.AreEqual(0, backend.Inner.KillProcessCallCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ProcessFree_EmptyOrNullExtractedOutput_RetriesExactlyThreeTimes(bool extractNull)
        {
            var backend = new GenerateOnly();
            if (!extractNull) backend.Inner.Response = " ";
            var context = new AIExecutorContext(backend, extractNull ? new NullExtractor() : null);
            try { await new GenAI(context).Generate("input"); Assert.Fail("Expected exhausted retries"); }
            catch (GenAIExecutionException ex) { Assert.AreEqual(3, ex.Attempts); }
            Assert.AreEqual(3, backend.Inner.GenerateCallCount);
            Assert.AreEqual(0, backend.Inner.StartProcessCallCount);
        }

        [Test]
        public async Task ProcessWithoutArguments_CanUseDefaultConfig()
        {
            var backend = new GenerateOnly();
            backend.Inner.SetProcessAlive(false);
            using var core = new GenAICore(new AIExecutorContext(backend, processExecutor: backend.Inner));
            Assert.AreEqual("raw response", await core.GenerateAsync("input"));
            Assert.AreEqual(1, backend.Inner.StartProcessCallCount);
            Assert.AreEqual(0, backend.Inner.SetDefaultArgumentsCallCount);
        }

        [Test]
        public async Task IndependentCores_DoNotBlockEachOther()
        {
            var first = new GenerateOnly();
            first.Inner.BlockGeneration();
            Task<string> active = new GenAI(first).Generate("first").AsTask();
            try
            {
                await first.Inner.GenerationStarted;
                Assert.AreEqual("raw response", await new GenAI(new GenerateOnly()).Generate("second", timeoutMs: 500));
                Assert.IsFalse(active.IsCompleted);
            }
            finally { first.Inner.CompleteGeneration(); }
            await active;
        }

        [Test]
        public async Task HttpJson_CustomPayload_IsPostedUnchangedAndSelectedWithoutExecutorSubclass()
        {
            using var handler = new CaptureHandler();
            using var client = new HttpClient(handler);
            var ai = new GenAI(new HttpJsonExecutor("https://example.invalid/decision", body => body.Contains("left") ? "left" : "right", client));
            var input = new CustomInput();
            var progress = new InlineProgress();
            string chunk = null;
            Assert.AreEqual("left", await ai.GenerateInput(input, onUpdate: value => chunk = value, progress: progress));
            Assert.AreEqual(input.Payload, handler.Body);
            Assert.AreEqual("application/json", handler.MediaType);
            Assert.AreEqual("left", chunk);
            Assert.AreEqual(100f, progress.Last);
            ai.KillProcess();
            Assert.AreEqual("left", await ai.GenerateInput(new SerializedExecutorInput(input.Payload)));
            Assert.AreEqual(2, handler.Calls);
        }

        [Test]
        public async Task HttpJson_AuthenticationFailure_IsNotRetried()
        {
            using var handler = new CaptureHandler { Status = HttpStatusCode.Unauthorized };
            using var client = new HttpClient(handler);
            var ai = new GenAI(new HttpJsonExecutor("https://example.invalid/decision", client: client));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("AI generation failed without retry"));
            try { await ai.GenerateInput(new SerializedExecutorInput("{}")); Assert.Fail("Expected HTTP failure"); }
            catch (GenAIConfigurationException) { }
            Assert.AreEqual(1, handler.Calls);
        }
    }
}
