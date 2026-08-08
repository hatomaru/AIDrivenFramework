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
public class OllamaRequest
{
    public GenAIConfig Config;
    public string Prompt;
}

[Serializable]
internal class OllamaGenerateResponse
{
    public string response;
    public bool done;
}

[Serializable]
internal class OllamaPayload
{
    public string model;
    public string prompt;
    public string system;
    public bool stream;
}

public class OllamaHTTPExecutor : IAIExecutor
{
    private HttpClient httpClient;
    private const string ServerHost = "127.0.0.1";
    private const int ServerPort = 11434;
    private string ServerUrl => $"http://{ServerHost}:{ServerPort}";

    private const string DefaultModel = "llama3";
    private string modelName = DefaultModel;

    private AIProcess _ollamaProcess;
    private bool _serverReady = false;
    private string _lastResponse = "";
    string AISoftwarePath = "";

    public OllamaHTTPExecutor()
    {
        var handler = new HttpClientHandler();
        httpClient = new HttpClient(handler);
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");

        // Ollamaの実行ファイルパス（サービスとして起動する場合に使用）
        AISoftwarePath = "ollama";
    }

    public async UniTask StartProcessAsync(CancellationToken ct, GenAIConfig config = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        if (_ollamaProcess != null && _ollamaProcess.IsProcessAlive())
        {
            _ollamaProcess.KillProcess();
            if (AIDrivenConfig.Instance.IsDeepDebug)
            {
                UnityEngine.Debug.Log("Existing process killed.");
            }
        }
        if (AIDrivenConfig.Instance.IsDeepDebug)
        {
            UnityEngine.Debug.Log("Starting new process...");
        }

        // Ollama が既に起動しているか確認し、起動中でなければ ollama serve を起動
        bool alreadyRunning = await CheckOutput(ct);
        if (!alreadyRunning)
        {
            try
            {
                // Start Ollama via AIProcess so we have unified process management.
                var gen = ScriptableObject.CreateInstance<GenAIConfig>();
                gen.aiSoftwarePath = AISoftwarePath;
                gen.arguments = "serve";
                // Ollama serve does not use stdio for streaming, so disable redirection.
                _ollamaProcess = new AIDrivenFW.Core.AIProcess(gen, redirectStdIn: false, redirectStdOut: false, redirectStdErr: true);
                UnityEngine.Debug.Log($"[AIProcess] VRAM={UnityEngine.SystemInfo.graphicsMemorySize}MB, gpu-layers={AIDrivenConfig.RecommendedGpuLayers}, batch-size={AIDrivenConfig.RecommendedBatchSize}");
                UnityEngine.Debug.Log($"Starting process with command: {AISoftwarePath} serve");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"❌ Ollama起動に失敗しました: {e.Message}");
            }
        }

        await WaitUntilReadyAsync(ct, progress, timeoutMs);
    }

    public async UniTask WaitUntilReadyAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        await WaitModelLoadAsync(ct, progress, timeoutMs);
    }

    private async UniTask WaitModelLoadAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 180000)
    {
        int elapsedMs = 0;
        const int pollIntervalMs = 1000;

        while (elapsedMs < timeoutMs && !ct.IsCancellationRequested)
        {
            try
            {
                var response = await httpClient.GetAsync($"{ServerUrl}/", ct);
                if (response.IsSuccessStatusCode)
                {
                    _serverReady = true;
                    if (AIDrivenConfig.Instance.IsDeepDebug)
                    {
                        UnityEngine.Debug.Log("ModelLoad Complete");
                    }
                    return;
                }
            }
            catch (Exception)
            {
                // サーバーがまだ起動していない
            }

            progress?.Report((float)elapsedMs / timeoutMs);
            await UniTask.Delay(pollIntervalMs, cancellationToken: ct);
            elapsedMs += pollIntervalMs;
        }

        throw new TimeoutException($"Ollamaサーバーの起動がタイムアウトしました ({timeoutMs}ms)");
    }

    public async UniTask GenerateAsync(string sysInput, string input, CancellationToken ct, Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        // Ensure server/process is running
        if ((_ollamaProcess == null || !_ollamaProcess.IsProcessAlive()) && !_serverReady)
        {
            UnityEngine.Debug.LogWarning("Ollama process is not initialized. Call StartProcessAsync first.");
            await StartProcessAsync(ct, null);
        }
        // モデル指定
        ModelInfo modelInfo = ModelInfo.LoadFromFile();
        modelName = modelInfo.Name;

        string model = modelName;
        string prompt = input;
        string system = sysInput;

        try
        {
            var request = JsonUtility.FromJson<OllamaRequest>(input);
            if (request != null)
            {
                prompt = request.Prompt ?? input;
                if (request.Config != null)
                {
                    system = request.Config.sysPrompt ?? "";
                    if (!string.IsNullOrEmpty(request.Config.modelFilePath) &&
                        request.Config.modelFilePath != AIDrivenConfig.autoDetect)
                        model = request.Config.modelFilePath;
                }
            }
        }
        catch (Exception)
        {
            // inputがJSONでない場合はそのままプロンプトとして使用
        }

        bool stream = onUpdate != null;
        // Ollama APIに送るペイロードを構築
        var payload = new OllamaPayload { model = model, prompt = prompt, system = system, stream = stream };
        string requestJson = JsonUtility.ToJson(payload);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        var responseBuilder = new StringBuilder();
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/api/generate")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        try
        {
            if (stream)
            {
                await ProcessStreamingResponseAsync(httpRequest, cts.Token, responseBuilder, onUpdate, model);
            }
            else
            {
                await ProcessNonStreamingResponseAsync(httpRequest, cts.Token, responseBuilder, model);
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

    private async UniTask ProcessStreamingResponseAsync(HttpRequestMessage httpRequest, CancellationToken ct, StringBuilder responseBuilder, Action<string> onUpdate, string model)
    {
        var httpResponse = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            string errorBody = await httpResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Model '{model}' was not found.\nPlease obtain it with ollama pull {model}.\nDetails: {errorBody}");
        }
        httpResponse.EnsureSuccessStatusCode();

        using var responseStream = await httpResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(responseStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192);

        while (!reader.EndOfStream)
        {
            // キャンセルチェック
            if (ct.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
            }

            string line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) continue;

            try
            {
                var chunk = JsonUtility.FromJson<OllamaGenerateResponse>(line);
                if (chunk == null) continue;

                if (!string.IsNullOrEmpty(chunk.response))
                {
                    responseBuilder.Append(chunk.response);
                    onUpdate?.Invoke(chunk.response);

                    // Yield to allow Unity to process other tasks
                    await UniTask.Yield();
                }

                if (chunk.done)
                {
                    if (AIDrivenConfig.Instance.IsDeepDebug)
                    {
                        UnityEngine.Debug.Log("Streaming completed");
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                if (AIDrivenConfig.Instance.IsDeepDebug)
                {
                    UnityEngine.Debug.LogWarning($"Failed to parse streaming chunk: {line}. Error: {ex.Message}");
                }
                // Continue processing next chunks even if one fails
                continue;
            }
        }
    }

    private async UniTask ProcessNonStreamingResponseAsync(HttpRequestMessage httpRequest, CancellationToken ct, StringBuilder responseBuilder, string model)
    {
        var httpResponse = await httpClient.SendAsync(httpRequest, ct);
        if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            string errorBody = await httpResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Model '{model}' was not found.\nPlease obtain it with ollama pull {model}.\nDetails: {errorBody}");
        }
        httpResponse.EnsureSuccessStatusCode();

        string responseJson = await httpResponse.Content.ReadAsStringAsync();
        var result = JsonUtility.FromJson<OllamaGenerateResponse>(responseJson);
        string content = result?.response ?? "";
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

    public async UniTask<bool> CheckOutput(CancellationToken token, Action<string> onUpdate = null)
    {
        try
        {
            var response = await httpClient.GetAsync($"{ServerUrl}/", token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public bool IsProcessAlive()
    {
        if (_ollamaProcess != null && _ollamaProcess.IsProcessAlive())
            return true;
        return _serverReady;
    }

    public bool IsDifferentAIConfig(GenAIConfig newAiConfig)
    {
        return _ollamaProcess != null && _ollamaProcess.aiConfig.arguments != newAiConfig.arguments;
    }

    public string SetDefaultArguments()
    {
        return "serve";
    }

    public string SetArguments(string raw, GenAIConfig genAIConfig)
    {
        string args = raw;
        args = args.Replace("{ModelPath}", $"\"{ModelRepository.GetModelExecutablePath()}\"");
        args = args.Replace("{sysPrompt}", $"\"{genAIConfig.sysPrompt}\"");
        return args;
    }

    public void KillProcess()
    {
        try
        {
            _ollamaProcess?.KillProcess();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"Failed to kill ollama process: {ex.Message}");
        }
        _ollamaProcess = null;
        _serverReady = false;
    }

    public string IsFoundAISoftware()
    {
        return File.Exists(AISoftwarePath) ? AISoftwarePath : "null";
    }

    public string IsFoundModelFile()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = httpClient.GetAsync($"{ServerUrl}/api/tags", cts.Token).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
            {
                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return json.Contains(modelName) ? modelName : "null";
            }
        }
        catch { }
        return "null";
    }

    public string ExtractAssistantOutput(string raw)
    {
        return raw;
    }

    public string SetArguments(string raw)
    {
        throw new NotImplementedException();
    }

    public string GetDefaultArguments()
    {
        throw new NotImplementedException();
    }

}
