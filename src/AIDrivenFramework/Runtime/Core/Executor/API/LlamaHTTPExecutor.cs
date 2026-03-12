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

        AISoftwarePath = Path.Combine(
            UnityEngine.Application.persistentDataPath,
            AIDrivenConfig.baseFilePath,
            "llama-server.exe"
        );

        if (!File.Exists(AISoftwarePath))
        {
            UnityEngine.Debug.LogError($"❌ サーバー実行ファイルが見つかりません: {AISoftwarePath}");
            return;
        }
    }

    public async UniTask StartProcessAsync(CancellationToken ct, GenAIConfig config = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        if (AIDrivenConfig.Instance.IsDeepDebug)
        {
            UnityEngine.Debug.Log("Starting new process...");
        }
        string llamaDir = AISoftwarePath;
        if (config == null) config = new GenAIConfig();

        string modelPath = ModelRepository.GetModelExecutablePath();
        string modelArg = string.Empty;
        if (!string.IsNullOrEmpty(modelPath) && modelPath != "null")
        {
            modelArg = $"-m \"{modelPath}\" ";
        }

        string args = $"" +
              $"{modelArg} --host {ServerHost} --port {ServerPort} " +
              $"--gpu-layers 130 " +
              $"--ctx-size 2048 " +
              $"--parallel 1 " +
              $"--mlock";

        config.arguments = args;
        config.aiSoftwarePath = AISoftwarePath;
        aiProcess = new AIProcess(config);

        await UniTask.WaitUntil(
            () => aiProcess.IsProcessAlive(),
            cancellationToken: ct
        );

        UnityEngine.Debug.Log($"[AIProcess] VRAM={UnityEngine.SystemInfo.graphicsMemorySize}MB, gpu-layers={AIDrivenConfig.RecommendedGpuLayers}, batch-size={AIDrivenConfig.RecommendedBatchSize}");
        UnityEngine.Debug.Log($"Starting process with command: {llamaDir} {args}");
        await WaitUntilReadyAsync(ct);
    }

    public async UniTask WaitUntilReadyAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        // ここでプロセスが準備できるまで待機する処理を実装  
        await WaitModelLoadAsync(ct);
    }

    private async UniTask WaitModelLoadAsync(CancellationToken ct)
    {
        // ここでモデルのロードが完了するまで待機する処理を実装  
        if (AIDrivenConfig.Instance.IsDeepDebug)
        {
            UnityEngine.Debug.Log("Model Loading...");
        }
        // Wait for server to be ready (poll health endpoint)
        int maxWaitMs = 180000; // 3 minutes for model loading
        int elapsedMs = 0;
        const int pollIntervalMs = 1000;

        while (elapsedMs < maxWaitMs && !ct.IsCancellationRequested)
        {
            try
            {
                var response = await httpClient.GetAsync($"{ServerUrl}/health", ct);
                if (response.IsSuccessStatusCode)
                {
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

            await UniTask.Delay(pollIntervalMs, cancellationToken: ct);
            elapsedMs += pollIntervalMs;
        }

        throw new TimeoutException("Model loading timed out");
    }

    public async UniTask GenerateAsync(string sysInput, string input, CancellationToken ct, Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        // プロンプトをJSON形式で受け取る場合とプレーンテキストで受け取る場合の両方に対応
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
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v1/chat/completions")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        if (stream)
        {
            var httpResponse = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!httpResponse.IsSuccessStatusCode)
            {
                string errorBody = await httpResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException($"llama-server {(int)httpResponse.StatusCode}: {errorBody}");
            }

            using var responseStream = await httpResponse.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(responseStream, Encoding.UTF8);

            // SSE形式でデータを逐次的に読み取る
            // SSE イベントは複数の "data:" 行を持ち、空行でイベントが終了する。
            var eventBuffer = new StringBuilder();
            bool done = false;

            while (!reader.EndOfStream && !cts.Token.IsCancellationRequested)
            {
                string line = await reader.ReadLineAsync();
                if (line == null) break;

                // 空行はイベントの区切りを示す
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (eventBuffer.Length == 0) continue;

                    // イベント内の data: 行を結合して JSON を得る
                    string evt = eventBuffer.ToString();
                    eventBuffer.Clear();

                    var sbData = new StringBuilder();
                    using (var sr = new StringReader(evt))
                    {
                        string l;
                        const string dataPrefix = "data:";
                        while ((l = sr.ReadLine()) != null)
                        {
                            if (!l.StartsWith(dataPrefix)) continue;
                            string part = l.Substring(dataPrefix.Length).Trim();
                            if (part == "[DONE]")
                            {
                                done = true;
                                break;
                            }
                            sbData.Append(part);
                        }
                    }

                    if (done) break;

                    string data = sbData.ToString();
                    if (string.IsNullOrEmpty(data)) continue;

                    try
                    {
                        var chunk = JsonUtility.FromJson<LlamaChatChunk>(data);
                        if (chunk?.choices == null || chunk.choices.Length == 0) continue;
                        string content = chunk.choices[0].delta?.content ?? chunk.choices[0].message?.content;
                        if (!string.IsNullOrEmpty(content))
                        {
                            responseBuilder.Append(content);
                            onUpdate.Invoke(content);
                        }
                    }
                    catch (Exception)
                    {
                        // JSON 解析失敗は無視して次へ
                    }
                }
                else
                {
                    // イベントの一部としてバッファに追加
                    eventBuffer.AppendLine(line);
                }
            }
        }
        else
        {
            var httpResponse = await httpClient.SendAsync(httpRequest, cts.Token);
            if (!httpResponse.IsSuccessStatusCode)
            {
                string errorBody = await httpResponse.Content.ReadAsStringAsync();
                throw new HttpRequestException($"llama-server {(int)httpResponse.StatusCode}: {errorBody}");
            }

            string responseJson = await httpResponse.Content.ReadAsStringAsync();
            var result = JsonUtility.FromJson<LlamaChatChunk>(responseJson);
            responseBuilder.Append(result?.choices?[0]?.message?.content ?? "");
        }

        _lastResponse = responseBuilder.ToString();
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

    public void KillProcess()
    {
        aiProcess?.KillProcess();
    }

    public string IsFoundAISoftware()
    {
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