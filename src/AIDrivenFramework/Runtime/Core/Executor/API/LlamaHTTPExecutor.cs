using AIDrivenFW.Config;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using UnityEngine;


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

public class LlamaHTTPExecutor : IProcessExecutor, IGenerateExecutor, IArgumentsExecutor, IExtractExecutor
{

    // HTTPクライアント
    private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

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
    public async UniTask<string> GenerateAsync(string sysInput, string input, CancellationToken ct, Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        ct.ThrowIfCancellationRequested();
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
        var url = $"{ServerUrl}/v1/chat/completions";
        try
        {
            if (stream)
                await HttpApiClient.SendStreamingAsync(url, requestJson,
                    (body, token) => ReadStreamingBodyAsync(body, token, responseBuilder, onUpdate), cts.Token);
            else
                responseBuilder.Append(ParseResponse(await HttpApiClient.SendAsync(url, requestJson, cts.Token)));

            if (string.IsNullOrWhiteSpace(responseBuilder.ToString()))
                throw new GenAIRetryableException("Llama returned no generated text.");

            _lastResponse = responseBuilder.ToString();
            return _lastResponse;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException("Llama generation timed out.");
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

    internal static string ParseResponse(string json)
    {
        var result = ParseChunk(json);
        var text = result?.choices != null && result.choices.Length > 0 ? result.choices[0]?.message?.content : null;
        if (string.IsNullOrWhiteSpace(text)) throw new GenAIRetryableException("llama-server returned no assistant message.");
        return text;
    }

    private static LlamaChatChunk ParseChunk(string json)
    {
        try { return JsonUtility.FromJson<LlamaChatChunk>(json); }
        catch (ArgumentException ex) { throw new GenAIRetryableException("Invalid llama-server JSON.", ex); }
    }

    internal static async UniTask ReadStreamingBodyAsync(Stream body, CancellationToken ct, StringBuilder output, Action<string> onUpdate)
    {
        using var reader = new StreamReader(body, Encoding.UTF8);
        var data = new StringBuilder();
        while (true)
        {
            var line = await HttpApiClient.ReadLineAsync(reader, ct);
            if (line == null) throw new GenAIRetryableException("llama-server stream ended before [DONE].");
            if (line.Length > 0)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (data.Length > 0) data.Append('\n');
                    data.Append(line.Substring(5).TrimStart(' '));
                }
                continue;
            }
            if (data.Length == 0) continue;
            var value = data.ToString();
            data.Clear();
            if (value.Trim() == "[DONE]")
            {
                if (string.IsNullOrWhiteSpace(output.ToString()))
                    throw new GenAIRetryableException("llama-server stream completed without generated text.");
                return;
            }
            var chunk = ParseChunk(value);
            var text = chunk?.choices != null && chunk.choices.Length > 0 ? chunk.choices[0]?.delta?.content : null;
            if (string.IsNullOrEmpty(text)) continue;
            output.Append(text);
            onUpdate?.Invoke(text);
        }
    }

    public UniTask<string> ReceiveAsync(CancellationToken ct)
    {
        return UniTask.FromResult(_lastResponse);
    }

    public UniTask<bool> IsGenerated(CancellationToken token, Action<string> onUpdate = null)
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
        return BuildArguments(raw, genAIConfig);
    }

    internal static string BuildArguments(string raw, GenAIConfig genAIConfig)
    {
        return ModelRepository.ExpandRequiredModelArgument(raw?.Replace("{modelArg}", "{ModelPath}"), genAIConfig)
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
