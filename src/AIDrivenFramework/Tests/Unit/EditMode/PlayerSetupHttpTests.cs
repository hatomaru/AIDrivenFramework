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
            public HttpStatusCode ShowCode = HttpStatusCode.OK;
            public string ShowRequest;
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
                    case "/api/show": ShowRequest = await request.Content.ReadAsStringAsync(); body = Show; break;
                    case "/api/generate": GenerateRequest = await request.Content.ReadAsStringAsync(); body = Generate; break;
                    default: throw new AssertionException("Unexpected setup HTTP path: " + path);
                }
                return new HttpResponseMessage(path == "/api/pull" ? PullCode : path == "/api/show" ? ShowCode : HttpStatusCode.OK) { Content = new StringContent(body) };
            }
        }

        [UnityTest]
        public IEnumerator ModelLicenseReadsOnlyTheSelectedModelsMetadata() => UniTask.ToCoroutine(async () =>
        {
            using var handler = new Handler { Show = "{\"license\":\"Exact model terms\\nSecond line\"}" }; using var client = new HttpClient(handler);
            Assert.AreEqual("Exact model terms\nSecond line", await PlayerSetupServices.ReadModelLicenseAsync(client, "http://127.0.0.1:12345", "author/custom:1b", CancellationToken.None));
            StringAssert.Contains("author/custom:1b", handler.ShowRequest);
            CollectionAssert.AreEqual(new[] { "/api/show" }, handler.Calls);
        });

        [UnityTest]
        public IEnumerator MissingMalformedOrOversizedLicenseFallsBackToManualReview() => UniTask.ToCoroutine(async () =>
        {
            using var handler = new Handler(); using var client = new HttpClient(handler);
            foreach (string json in new[] { "{}", "{\"license\":\" \"}", "not json", "{\"license\":\"" + new string('x', 262145) + "\"}", new string('x', 1048577) })
            { handler.Show = json; Assert.IsEmpty(await PlayerSetupServices.ReadModelLicenseAsync(client, "http://127.0.0.1:12345", "custom:1b", CancellationToken.None)); }
            handler.ShowCode = HttpStatusCode.NotFound;
            Assert.IsEmpty(await PlayerSetupServices.ReadModelLicenseAsync(client, "http://127.0.0.1:12345", "missing:1b", CancellationToken.None));
            Assert.IsTrue(handler.Calls.TrueForAll(p => p == "/api/show"));
        });

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
