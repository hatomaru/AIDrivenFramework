using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AIDrivenFW.Setup;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace AIDrivenFW.Tests.Unit
{
    public class PlayerSetupHttpTests
    {
        sealed class Handler : HttpMessageHandler
        {
            public readonly List<string> Calls = new List<string>();
            public string Tags = "{\"models\":[]}";
            public string Pull = "{\"status\":\"pulling layer\",\"completed\":10,\"total\":10}\n{\"status\":\"success\"}\n";
            public string Show = "{\"capabilities\":[\"completion\"]}";
            public string Generate = "{\"done\":true,\"response\":\"Hello!\"}";
            public HttpStatusCode PullCode = HttpStatusCode.OK;
            public Action OnPull;
            public string GenerateRequest;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                string path = request.RequestUri.AbsolutePath;
                Calls.Add(path); ct.ThrowIfCancellationRequested();
                string body;
                switch (path)
                {
                    case "/api/version": body = "{\"version\":\"fixture\"}"; break;
                    case "/api/tags": body = Tags; break;
                    case "/api/pull": OnPull?.Invoke(); ct.ThrowIfCancellationRequested(); body = Pull; break;
                    case "/api/show": body = Show; break;
                    case "/api/generate": GenerateRequest = await request.Content.ReadAsStringAsync(); body = Generate; break;
                    default: throw new AssertionException("Unexpected setup HTTP path: " + path);
                }
                return new HttpResponseMessage(path == "/api/pull" ? PullCode : HttpStatusCode.OK) { Content = new StringContent(body) };
            }
        }

        [UnityTest]
        public IEnumerator InstalledServiceDoesNotLaunchSoftware() => UniTask.ToCoroutine(async () =>
        {
            using var handler = new Handler(); using var client = new HttpClient(handler); using var service = new PlayerSetupServices(12345, client);
            await service.PrepareRuntimeAsync(new PlayerSetupPlan(), null, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "/api/version" }, handler.Calls);
        });

        [UnityTest]
        public IEnumerator InstalledModelSkipsPullAndGenerationIsExplicit() => UniTask.ToCoroutine(async () =>
        {
            using var handler = new Handler { Tags = "{\"models\":[{\"name\":\"Qwen3:0.6B\",\"size\":523000000}]}" };
            using var client = new HttpClient(handler); using var service = new PlayerSetupServices(12345, client);
            var plan = new PlayerSetupPlan(); await service.PrepareModelAsync(plan, null, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "/api/tags", "/api/show" }, handler.Calls);
            await service.VerifyAsync(plan, CancellationToken.None);
            StringAssert.Contains("qwen3:0.6b", handler.GenerateRequest);
            StringAssert.Contains("\"think\":false", handler.GenerateRequest);
        });

        [UnityTest]
        public IEnumerator PullNeedsTerminalSuccess() => UniTask.ToCoroutine(async () =>
        {
            using var handler = new Handler { Pull = "{\"status\":\"pulling layer\",\"completed\":9,\"total\":10}\n" };
            using var client = new HttpClient(handler); using var service = new PlayerSetupServices(12345, client);
            try { await service.PrepareModelAsync(new PlayerSetupPlan(), null, CancellationToken.None); Assert.Fail("Incomplete pull accepted"); }
            catch (SetupException ex) { Assert.AreEqual(SetupProblem.Connection, ex.Problem); }
            Assert.IsFalse(handler.Calls.Contains("/api/show")); Assert.IsFalse(handler.Calls.Contains("/api/generate"));
        });

        [UnityTest]
        public IEnumerator PullHttpFailureStopsBeforeVerification() => UniTask.ToCoroutine(async () =>
        {
            using var handler = new Handler { PullCode = HttpStatusCode.ServiceUnavailable };
            using var client = new HttpClient(handler); using var service = new PlayerSetupServices(12345, client);
            try { await service.PrepareModelAsync(new PlayerSetupPlan(), null, CancellationToken.None); Assert.Fail("HTTP failure accepted"); }
            catch (SetupException ex) { Assert.AreEqual(SetupProblem.Connection, ex.Problem); }
            Assert.IsFalse(handler.Calls.Contains("/api/generate"));
        });

        [UnityTest]
        public IEnumerator PullCancellationPropagates() => UniTask.ToCoroutine(async () =>
        {
            using var cancel = new CancellationTokenSource(); using var handler = new Handler { OnPull = cancel.Cancel };
            using var client = new HttpClient(handler); using var service = new PlayerSetupServices(12345, client);
            try { await service.PrepareModelAsync(new PlayerSetupPlan(), null, cancel.Token); Assert.Fail("Cancellation lost"); }
            catch (OperationCanceledException) { }
            Assert.IsFalse(handler.Calls.Contains("/api/generate"));
        });

        [UnityTest]
        public IEnumerator RemoteModelIsRejectedBeforeSendingPrompt() => UniTask.ToCoroutine(async () =>
        {
            using var handler = new Handler { Show = "{\"remote_host\":\"https://example.com\",\"capabilities\":[\"completion\"]}" };
            using var client = new HttpClient(handler); using var service = new PlayerSetupServices(12345, client);
            try { await service.PrepareModelAsync(new PlayerSetupPlan(), null, CancellationToken.None); Assert.Fail("Remote model accepted"); }
            catch (SetupException ex) { Assert.AreEqual(SetupProblem.Model, ex.Problem); }
            Assert.IsFalse(handler.Calls.Contains("/api/generate"));
        });

        [UnityTest]
        public IEnumerator EmptyOrIncompleteGreetingIsNotReady() => UniTask.ToCoroutine(async () =>
        {
            using var handler = new Handler { Generate = "{\"done\":false,\"response\":\"\"}" };
            using var client = new HttpClient(handler); using var service = new PlayerSetupServices(12345, client);
            try { await service.VerifyAsync(new PlayerSetupPlan(), CancellationToken.None); Assert.Fail("Empty generation accepted"); }
            catch (SetupException ex) { Assert.AreEqual(SetupProblem.Verification, ex.Problem); }
        });
    }
}
