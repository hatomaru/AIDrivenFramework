using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AIDrivenFW.Tests.Unit
{
    [Timeout(10000)]
    public class HttpStreamingContractTests
    {
        private sealed class Handler : HttpMessageHandler
        {
            public HttpResponseMessage Response;
            public HttpRequestMessage Request;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Request = request;
                return Task.FromResult(Response);
            }
        }

        private sealed class UnavailableHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromException<HttpResponseMessage>(new HttpRequestException("fixture connection refused"));
        }

        private sealed class PendingHeadersHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException();
            }
        }

        private sealed class TrackedStream : MemoryStream
        {
            public bool Closed;
            public TrackedStream(string text) : base(Encoding.UTF8.GetBytes(text)) { }
            protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
        }

        private sealed class WaitingStream : Stream
        {
            private readonly TaskCompletionSource<int> pending = new TaskCompletionSource<int>();
            public bool Closed;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override Task<int> ReadAsync(byte[] b, int o, int c, CancellationToken ct) => pending.Task;
            public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { Closed = true; pending.TrySetResult(0); base.Dispose(disposing); }
        }

        private static async Task<Exception> Failure(Task task)
        {
            try { await task; } catch (Exception ex) { return ex; }
            Assert.Fail("Expected failure"); return null;
        }

        [TestCase(401, false)] [TestCase(404, false)] [TestCase(408, true)]
        [TestCase(429, true)] [TestCase(500, true)] [TestCase(503, true)]
        public async Task StatusFailure_IsClassifiedAndDisposesBody(int status, bool retryable)
        {
            var body = new TrackedStream("error");
            var handler = new Handler { Response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StreamContent(body) } };
            using var client = new HttpClient(handler);
            var ex = await Failure(HttpApiClient.SendAsync(client, "http://fixture/", "{}", default).AsTask());
            Assert.AreEqual(retryable, GenAIExceptionClassifier.IsRetryable(ex));
            Assert.IsTrue(body.Closed);
        }

        [TestCase(false)] [TestCase(true)]
        public async Task Stream_ParsesRealChunksAndDisposesBody(bool llama)
        {
            string text = llama ? ": heartbeat\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\ndata: [DONE]\n\n"
                : "{\"response\":\"hello\",\"done\":false}\n{\"done\":true}\n";
            var body = new TrackedStream(text);
            var handler = new Handler { Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) } };
            using var client = new HttpClient(handler);
            var output = new StringBuilder(); var updates = new StringBuilder();
            await HttpApiClient.SendStreamingAsync(client, "http://fixture/", "{}", (s, ct) => llama
                ? LlamaHTTPExecutor.ReadStreamingBodyAsync(s, ct, output, t => updates.Append(t))
                : OllamaHTTPExecutor.ReadStreamingBodyAsync(s, ct, output, t => updates.Append(t)), default).AsTask();
            Assert.AreEqual("hello", output.ToString()); Assert.AreEqual("hello", updates.ToString()); Assert.IsTrue(body.Closed);
        }

        [TestCase(false)] [TestCase(true)]
        public async Task Stream_EofBeforeMarkerFails(bool llama)
        {
            using var body = new TrackedStream(llama ? "data: {\"choices\":[]}\n\n" : "{\"response\":\"partial\"}\n");
            var output = new StringBuilder();
            var task = llama ? LlamaHTTPExecutor.ReadStreamingBodyAsync(body, default, output, null)
                : OllamaHTTPExecutor.ReadStreamingBodyAsync(body, default, output, null);
            Assert.IsInstanceOf<GenAIRetryableException>(await Failure(task.AsTask()));
        }

        [TestCase(false, false)] [TestCase(true, false)]
        [TestCase(false, true)] [TestCase(true, true)]
        public async Task Stream_EmptyOrMalformedResponseFails(bool llama, bool malformed)
        {
            string text = malformed ? (llama ? "data: invalid-json\n\n" : "invalid-json\n")
                : (llama ? "data: [DONE]\n\n" : "{\"done\":true}\n");
            using var body = new TrackedStream(text);
            var task = llama ? LlamaHTTPExecutor.ReadStreamingBodyAsync(body, default, new StringBuilder(), null)
                : OllamaHTTPExecutor.ReadStreamingBodyAsync(body, default, new StringBuilder(), null);
            Assert.IsInstanceOf<GenAIRetryableException>(await Failure(task.AsTask()));
        }

        [TestCase(false)] [TestCase(true)]
        public async Task Stream_CallbackExceptionIsPreserved(bool llama)
        {
            using var body = new TrackedStream(llama ? "data: {\"choices\":[{\"delta\":{\"content\":\"x\"}}]}\n\n" : "{\"response\":\"x\"}\n");
            var expected = new InvalidOperationException("callback");
            Action<string> callback = _ => throw expected;
            var task = llama ? LlamaHTTPExecutor.ReadStreamingBodyAsync(body, default, new StringBuilder(), callback)
                : OllamaHTTPExecutor.ReadStreamingBodyAsync(body, default, new StringBuilder(), callback);
            Assert.AreSame(expected, await Failure(task.AsTask()));
        }

        [TestCase(false)] [TestCase(true)]
        public async Task PendingBody_CancellationClosesIo(bool wholeBody)
        {
            var body = new WaitingStream();
            var handler = new Handler { Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) } };
            using var client = new HttpClient(handler);
            using var cts = new CancellationTokenSource();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            cts.CancelAfter(150);
            Task task = wholeBody ? HttpApiClient.SendAsync(client, "http://fixture/", "{}", cts.Token).AsTask()
                : HttpApiClient.SendStreamingAsync(client, "http://fixture/", "{}", (s, ct) => OllamaHTTPExecutor.ReadStreamingBodyAsync(s, ct, new StringBuilder(), null), cts.Token).AsTask();
            Assert.IsInstanceOf<OperationCanceledException>(await Failure(task));
            Assert.IsTrue(body.Closed); Assert.Less(clock.ElapsedMilliseconds, 2000);
        }

        [Test]
        public async Task Nonstream_EmptyBodyFailsAndCloses()
        {
            var body = new TrackedStream("  ");
            using var client = new HttpClient(new Handler { Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) } });
            Assert.IsInstanceOf<GenAIRetryableException>(await Failure(HttpApiClient.SendAsync(client, "http://fixture/", "{}", default).AsTask()));
            Assert.IsTrue(body.Closed);
        }

        [Test]
        public async Task TransportFailure_RemainsRetryable()
        {
            using var client = new HttpClient(new UnavailableHandler());
            var ex = await Failure(HttpApiClient.SendAsync(client, "http://fixture/", "{}", default).AsTask());
            Assert.IsInstanceOf<HttpRequestException>(ex);
            Assert.IsTrue(GenAIExceptionClassifier.IsRetryable(ex));
        }

        [Test]
        public async Task PendingHeaders_CancelPromptly()
        {
            using var client = new HttpClient(new PendingHeadersHandler());
            using var cts = new CancellationTokenSource(150);
            Assert.IsInstanceOf<OperationCanceledException>(await Failure(HttpApiClient.SendAsync(client, "http://fixture/", "{}", cts.Token).AsTask()));
        }

        [Test]
        public async Task ConsumerFailure_DisposesBodyAndPreservesException()
        {
            var body = new TrackedStream("hello");
            using var client = new HttpClient(new Handler { Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) } });
            var expected = new InvalidOperationException("consumer failure");
            var ex = await Failure(HttpApiClient.SendStreamingAsync(client, "http://fixture/", "{}", (s, ct) => throw expected, default).AsTask());
            Assert.AreSame(expected, ex); Assert.IsTrue(body.Closed);
        }

        [TestCase(false)] [TestCase(true)]
        [Timeout(120000)]
        public async Task LiveBackend_StreamAndNonstream(bool llama)
        {
            if (Environment.GetEnvironmentVariable("AIDRIVEN_HTTP_SMOKE") != "1")
                Assert.Ignore("Set AIDRIVEN_HTTP_SMOKE=1 and start both local backends.");
            string url = llama ? "http://127.0.0.1:18080/v1/chat/completions" : "http://127.0.0.1:11434/api/generate";
            string payload = llama ? "{\"messages\":[{\"role\":\"user\",\"content\":\"Say hello briefly.\"}],\"max_tokens\":32,\"stream\":STREAM}"
                : "{\"model\":\"gemma3:1b\",\"prompt\":\"Say hello briefly.\",\"options\":{\"num_predict\":32},\"stream\":STREAM}";
            using var cts = new CancellationTokenSource(110000);
            var output = new StringBuilder(); var updates = new StringBuilder();
            await HttpApiClient.SendStreamingAsync(url, payload.Replace("STREAM", "true"), (s, ct) => llama
                ? LlamaHTTPExecutor.ReadStreamingBodyAsync(s, ct, output, t => updates.Append(t))
                : OllamaHTTPExecutor.ReadStreamingBodyAsync(s, ct, output, t => updates.Append(t)), cts.Token).AsTask();
            Assert.IsNotEmpty(output.ToString()); Assert.AreEqual(output.ToString(), updates.ToString());
            string json = await HttpApiClient.SendAsync(url, payload.Replace("STREAM", "false"), cts.Token).AsTask();
            string text = llama ? LlamaHTTPExecutor.ParseResponse(json) : OllamaHTTPExecutor.ParseResponse(json);
            Assert.IsNotEmpty(text);
            UnityEngine.Debug.Log($"HTTP_SMOKE {(llama ? "llama" : "ollama")} stream={output} nonstream={text}");
        }

        [Test]
        public void Nonstream_ParsesBothBackends()
        {
            Assert.AreEqual("hello", OllamaHTTPExecutor.ParseResponse("{\"response\":\"hello\",\"done\":true}"));
            Assert.AreEqual("hello", LlamaHTTPExecutor.ParseResponse("{\"choices\":[{\"message\":{\"content\":\"hello\"}}]}"));
            Assert.Throws<GenAIRetryableException>(() => OllamaHTTPExecutor.ParseResponse("{}"));
            Assert.Throws<GenAIRetryableException>(() => LlamaHTTPExecutor.ParseResponse("{}"));
        }
    }
}
