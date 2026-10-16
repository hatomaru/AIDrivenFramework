using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace AIDrivenFW.Core
{
    public static class HttpApiClient
    {
        // Application-lifetime connection pool. Injected clients remain caller-owned.
        private static readonly HttpClient SharedClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        public static UniTask<string> SendAsync(string url, string content, CancellationToken ct)
            => SendAsync(SharedClient, url, content, ct);

        internal static async UniTask<string> SendAsync(HttpClient client, string url, string content, CancellationToken ct)
        {
            string body = null;
            await SendStreamingAsync(client, url, content, async (stream, token) =>
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                body = await reader.ReadToEndAsync().AsUniTask().AttachExternalCancellation(token);
                if (string.IsNullOrWhiteSpace(body))
                    throw new GenAIRetryableException("HTTP response body was empty.");
            }, ct);
            return body;
        }

        /// <summary>
        /// Processes a live body inside the response lifetime. The callback must finish all reads
        /// before returning and must not retain the stream. This method owns request/response/body.
        /// The caller supplies the operation deadline through ct.
        /// </summary>
        public static UniTask SendStreamingAsync(string url, string content,
            Func<Stream, CancellationToken, UniTask> consumeBody, CancellationToken ct)
            => SendStreamingAsync(SharedClient, url, content, consumeBody, ct);

        internal static async UniTask SendStreamingAsync(HttpClient client, string url, string content,
            Func<Stream, CancellationToken, UniTask> consumeBody, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw GenAIExceptionClassifier.CreateHttpStatusException("HTTP API", (int)response.StatusCode, response.ReasonPhrase);
            using var stream = await response.Content.ReadAsStreamAsync().AsUniTask().AttachExternalCancellation(ct);
            // Older Unity profiles lack cancellable StreamReader methods. Close the body to
            // abort pending I/O as well as cancelling the awaiting task.
            using var registration = ct.Register(() => stream.Dispose());
            try
            {
                await consumeBody(stream, ct);
                ct.ThrowIfCancellationRequested();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
        }

        internal static async UniTask<string> ReadLineAsync(StreamReader reader, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return await reader.ReadLineAsync().AsUniTask().AttachExternalCancellation(ct);
        }
    }
}
