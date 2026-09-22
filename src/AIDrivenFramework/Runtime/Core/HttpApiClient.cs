using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AIDrivenFW.Core
{
    public static class HttpApiClient
    {
        /// <summary>
        /// Http経由でAPIを呼び出す
        /// </summary>
        public static async UniTask<string> SendAsync(string url, string content, CancellationToken ct)
        {
            var handler = new HttpClientHandler();
            var httpClient = new HttpClient(handler);
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };

            var httpResponse = await httpClient.SendAsync(httpRequest, ct);
            if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorBody = await httpResponse.Content.ReadAsStringAsync();
                throw new GenAIConfigurationException($"Details: {errorBody}");
            }
            if (!httpResponse.IsSuccessStatusCode)
            {
                string errorBody = await httpResponse.Content.ReadAsStringAsync();
                throw GenAIExceptionClassifier.CreateHttpStatusException(
                    "Ollama", (int)httpResponse.StatusCode, errorBody);
            }
            return await httpResponse.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Http経由でAPIを呼び出す（ストリーミング）
        /// </summary>
        public static async UniTask<string> SendStreamingAsync(string url, string content,StringBuilder responseBuilder, Action<string> onUpdate, CancellationToken ct)
        {
            var handler = new HttpClientHandler();
            var httpClient = new HttpClient(handler);
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };

            var httpResponse = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorBody = await httpResponse.Content.ReadAsStringAsync();
                throw new GenAIConfigurationException($"Details: {errorBody}");
            }
            if (!httpResponse.IsSuccessStatusCode)
            {
                string errorBody = await httpResponse.Content.ReadAsStringAsync();
                throw GenAIExceptionClassifier.CreateHttpStatusException(
                    "Ollama", (int)httpResponse.StatusCode, errorBody);
            }

            using var responseStream = await httpResponse.Content.ReadAsStreamAsync();
            return responseStream.ToString();
        }
    }
}