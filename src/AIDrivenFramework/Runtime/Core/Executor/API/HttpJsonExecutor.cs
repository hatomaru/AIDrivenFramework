using System;
using System.Net.Http;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// Process-free JSON POST backend. Input is the complete request body, including any
    /// system instructions. A caller-supplied selector converts the response without subclassing.
    /// Requests are nonstreaming; onUpdate receives the completed result once.
    /// An injected HttpClient remains caller-owned (including authentication/headers).
    /// </summary>
    public sealed class HttpJsonExecutor : IGenerateExecutor
    {
        private readonly string endpoint;
        private readonly Func<string, string> selectOutput;
        private readonly HttpClient client;

        public HttpJsonExecutor(string endpoint, Func<string, string> selectOutput = null, HttpClient client = null)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("An absolute HTTP(S) endpoint is required.", nameof(endpoint));
            this.endpoint = endpoint;
            this.selectOutput = selectOutput;
            this.client = client;
        }

        public async UniTask<string> GenerateAsync(string sysInput, string input, CancellationToken ct,
            Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000)
        {
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            ct.ThrowIfCancellationRequested();
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (!string.IsNullOrEmpty(sysInput))
                throw new GenAIConfigurationException("Serialize system instructions into the JSON body instead of GenAIConfig.sysPrompt.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(timeoutMs);
            try
            {
                string body = client == null
                    ? await HttpApiClient.SendAsync(endpoint, input, deadline.Token)
                    : await HttpApiClient.SendAsync(client, endpoint, input, deadline.Token);
                string output = selectOutput == null ? body : selectOutput(body);
                deadline.Token.ThrowIfCancellationRequested();
                onUpdate?.Invoke(output);
                progress?.Report(100f);
                deadline.Token.ThrowIfCancellationRequested();
                return output;
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
            {
                throw new TimeoutException("HTTP JSON generation timed out.", ex);
            }
        }

        // Generation completion is represented by GenerateAsync; Core does not poll this method.
        public UniTask<bool> IsGenerated(CancellationToken token, Action<string> onUpdate = null)
        {
            token.ThrowIfCancellationRequested();
            return UniTask.FromResult(true);
        }
    }
}
