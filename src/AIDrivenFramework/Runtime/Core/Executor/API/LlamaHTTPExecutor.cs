using AIDrivenFW.Config;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;


[Serializable]
public class Request
{
    public GenAIConfig Config;
    public string Prompt;
}

[Serializable]
internal class LlamaChatChunk
{
    public LlamaChatChoice[] choices;
}

[Serializable]
internal class LlamaChatChoice
{
    public LlamaChatDelta delta;     // streaming
    public LlamaChatMessage message; // non-streaming
}

[Serializable]
internal class LlamaChatDelta
{
    public string content;
}

[Serializable]
internal class LlamaChatMessage
{
    public string content;
}

[Serializable]
internal class Message
{
    public string role;
    public string content;
}

[Serializable]
internal class RequestPayload
{
    public Message[] messages;
    public bool stream;
}

public class LlamaHTTPExecutor : IAIExecutor
{

    // HTTPクライアント
    private HttpClient httpClient;

    private const string ServerHost = "127.0.0.1";
    private const int ServerPort = 8080;
    private string ServerUrl => $"http://{ServerHost}:{ServerPort}";

    private AIProcess aiProcess;
    private string _lastResponse = string.Empty;
    const int checkIntervalMs = 500;
    string AISoftwarePath = "";

    public LlamaHTTPExecutor()
    {
        // Initialize HTTP client with appropriate settings
        var handler = new HttpClientHandler();
        httpClient = new HttpClient(handler);
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");

        AISoftwarePath = FindServerExecutable();
    }

    private static string FindServerExecutable()
    {
        string root = Path.Combine(Application.persistentDataPath, AIDrivenConfig.baseFilePath);
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        const string name = "llama-server";
#else
        const string name = "llama-server.exe";
#endif
        string directPath = Path.Combine(root, name);
        if (File.Exists(directPath) || !Directory.Exists(root)) return directPath;
        var files = Directory.GetFiles(root, name, SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        return files.Length > 0 ? files[0] : directPath;
    }

    public async UniTask StartProcessAsync(CancellationToken ct, GenAIConfig config = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        ct.ThrowIfCancellationRequested();
        KillProcess();
        AISoftwarePath = !string.IsNullOrEmpty(config?.aiSoftwarePath)
            ? config.aiSoftwarePath : FindServerExecutable();
        if (!File.Exists(AISoftwarePath))
            throw new FileNotFoundException("llama-server executable was not found.", AISoftwarePath);

        // Keep the caller's asset and argument template unchanged.
        var processConfig = new GenAIConfig();
        processConfig.aiSoftwarePath = AISoftwarePath;
        processConfig.modelFilePath = config?.modelFilePath ?? AIDrivenConfig.autoDetect;
        processConfig.sysPrompt = config?.sysPrompt ?? "";
        processConfig.arguments = SetArguments(config?.arguments, processConfig);
        try
        {
            aiProcess = new AIProcess(processConfig, redirectStdIn: false);
            await WaitUntilReadyAsync(ct, progress, timeoutMs);
        }
        catch
        {
            KillProcess();
            throw;
        }
    }

    public async UniTask WaitUntilReadyAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (!IsProcessAlive())
                    throw new InvalidOperationException("llama-server exited before becoming ready. Check the server log.");
                try
                {
                    using var response = await httpClient.GetAsync($"{ServerUrl}/health", timeout.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        progress?.Report(1f);
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // The listening socket may not exist yet while the model loads.
                }
                await UniTask.Delay(checkIntervalMs, cancellationToken: timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"llama-server startup timed out ({timeoutMs}ms).");
        }
    }
    public async UniTask GenerateAsync(string sysInput, string input, CancellationToken ct, Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        if (aiProcess == null || !aiProcess.IsProcessAlive())
        {
            UnityEngine.Debug.LogWarning("AIProcess is not initialized. Call StartProcessAsync first.");
            await StartProcessAsync(ct, null, progress, timeoutMs);
        }
        // プロンプトをJSON形式で受け取る場合とプレーンテキストで受け取る場合の両方に対応
        _lastResponse = string.Empty;
        string prompt = input;
        string systemPrompt = sysInput;
        try
        {
            var request = JsonUtility.FromJson<Request>(input);
            if (request != null && !string.IsNullOrEmpty(request.Prompt))
            {
                prompt = request.Prompt;
                if (request.Config != null)
                    systemPrompt = request.Config.sysPrompt ?? "";
            }
        }
        catch (Exception)
        {
            // JSONのパースに失敗した場合は、inputをそのままプロンプトとして使用
        }

        bool stream = onUpdate != null;
        Message[] messages = string.IsNullOrEmpty(systemPrompt)
            ? new Message[] { new Message { role = "user", content = prompt } }
            : new Message[] { new Message { role = "system", content = systemPrompt }, new Message { role = "user", content = prompt } };

        var payload = new RequestPayload { messages = messages, stream = stream };
        string requestJson = JsonUtility.ToJson(payload);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        var responseBuilder = new StringBuilder();
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v1/chat/completions")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        try
        {
            if (stream)
            {
                await ProcessStreamingResponseAsync(httpRequest, cts.Token, responseBuilder, onUpdate);
            }
            else
            {
                await ProcessNonStreamingResponseAsync(httpRequest, cts.Token, responseBuilder);
            }

            _lastResponse = responseBuilder.ToString();
        }
        catch (OperationCanceledException)
        {
            if (AIDrivenConfig.Instance.IsDeepDebug)
            {
                UnityEngine.Debug.Log("Generation was cancelled");
            }
            throw;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Error during generation: {ex.Message}");
            throw;
        }
    }

    private async UniTask ProcessStreamingResponseAsync(HttpRequestMessage httpRequest, CancellationToken ct, StringBuilder responseBuilder, Action<string> onUpdate)
    {
        using var httpResponse = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!httpResponse.IsSuccessStatusCode)
        {
            string errorBody = await httpResponse.Content.ReadAsStringAsync();
            throw new HttpRequestException($"llama-server {(int)httpResponse.StatusCode}: {errorBody}");
        }

        using var responseStream = await httpResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(responseStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192);

        // SSE形式でデータを逐次的に読み取る
        while (true)
        {
            // キャンセルチェック
            if (ct.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
            }

            string line = await reader.ReadLineAsync().AsUniTask().AttachExternalCancellation(ct);
            if (line == null) break;
            if (string.IsNullOrEmpty(line)) continue;

            // SSE format: "data: {...}" or "data: [DONE]"
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            string data = line.Substring(5).Trim();
            if (data == "[DONE]")
            {
                if (AIDrivenConfig.Instance.IsDeepDebug)
                {
                    UnityEngine.Debug.Log("Streaming completed");
                }
                break;
            }

            var chunk = JsonUtility.FromJson<LlamaChatChunk>(data);
            if (chunk?.choices == null || chunk.choices.Length == 0) continue;

            string content = chunk.choices[0]?.delta?.content;
            if (!string.IsNullOrEmpty(content))
            {
                responseBuilder.Append(content);
                onUpdate?.Invoke(content);
                await UniTask.Yield();
            }
        }
    }

    private async UniTask ProcessNonStreamingResponseAsync(HttpRequestMessage httpRequest, CancellationToken ct, StringBuilder responseBuilder)
    {
        using var httpResponse = await httpClient.SendAsync(httpRequest, ct);
        if (!httpResponse.IsSuccessStatusCode)
        {
            string errorBody = await httpResponse.Content.ReadAsStringAsync();
            throw new HttpRequestException($"llama-server {(int)httpResponse.StatusCode}: {errorBody}");
        }

        string responseJson = await httpResponse.Content.ReadAsStringAsync();
        var result = JsonUtility.FromJson<LlamaChatChunk>(responseJson);
        if (result?.choices == null || result.choices.Length == 0 || result.choices[0]?.message?.content == null)
            throw new InvalidDataException("llama-server returned no assistant message: " + responseJson);
        string content = result.choices[0].message.content;
        responseBuilder.Append(content);

        if (AIDrivenConfig.Instance.IsDeepDebug)
        {
            UnityEngine.Debug.Log($"Non-streaming response received: {content.Length} characters");
        }
    }

    public UniTask<string> ReceiveAsync(CancellationToken ct)
    {
        return UniTask.FromResult(_lastResponse);
    }

    public UniTask<bool> CheckOutput(CancellationToken token, Action<string> onUpdate = null)
    {
        return UniTask.FromResult(!string.IsNullOrEmpty(_lastResponse));
    }

    public bool IsProcessAlive()
    {
        return aiProcess != null && aiProcess.IsProcessAlive();
    }

    public bool IsDifferentAIConfig(GenAIConfig newAiConfig)
    {
        return aiProcess != null && (aiProcess.aiConfig.arguments != SetArguments(newAiConfig?.arguments, newAiConfig)
            || (!string.IsNullOrEmpty(newAiConfig?.aiSoftwarePath) && aiProcess.aiConfig.aiSoftwarePath != newAiConfig.aiSoftwarePath));
    }

    public string SetDefaultArguments()
    {
        return "-m {ModelPath} --host {ServerHost} --port {ServerPort} " +
               $"--gpu-layers {AIDrivenConfig.RecommendedGpuLayers} " +
               $"--batch-size {AIDrivenConfig.RecommendedBatchSize} --ctx-size 4096 --parallel 1";
    }

    public string SetArguments(string raw, GenAIConfig genAIConfig)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == AIDrivenConfig.autoDetect || raw == AIDrivenConfig.defaultArguments)
            raw = SetDefaultArguments();
        string modelPath = genAIConfig?.modelFilePath;
        if (string.IsNullOrWhiteSpace(modelPath) || modelPath == AIDrivenConfig.autoDetect)
            modelPath = ModelRepository.GetModelExecutablePath();
        return raw.Replace("{ModelPath}", $"\"{modelPath}\"")
            .Replace("{modelArg}", $"\"{modelPath}\"")
            .Replace("{ServerHost}", ServerHost)
            .Replace("{ServerPort}", ServerPort.ToString());
    }

    public void KillProcess()
    {
        aiProcess?.KillProcess();
        aiProcess = null;
        _lastResponse = string.Empty;
    }

    public string IsFoundAISoftware()
    {
        AISoftwarePath = FindServerExecutable();
        return File.Exists(AISoftwarePath) ? AISoftwarePath : "null";
    }

    public string IsFoundModelFile()
    {
        string modelPath = ModelRepository.GetModelExecutablePath();
        return modelPath != "null" ? modelPath : "null";
    }

    public string ExtractAssistantOutput(string raw)
    {
        return raw;
    }
}