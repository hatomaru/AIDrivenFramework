using AIDrivenFW.Config;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;

public class OllamaRequest
{
    public GenAIConfig Config { get; set; }
    public string Prompt { get; set; }
}

internal class OllamaGenerateResponse
{
    public string response { get; set; }
    public bool done { get; set; }
}

public class OllamaHTTPExecutor : IAIExecutor
{
    private HttpClient httpClient;
    private const string ServerHost = "127.0.0.1";
    private const int ServerPort = 11434;
    private string ServerUrl => $"http://{ServerHost}:{ServerPort}";

    private const string DefaultModel = "llama3";
    private string modelName = DefaultModel;

    private Process _ollamaProcess;
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
                string args = "serve";
                var psi = new ProcessStartInfo
                {
                    FileName = AISoftwarePath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // stdin/stdout は リダイレクトしない（ollama serve は stdin を受け付けない）
                    RedirectStandardInput = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
                psi.Environment["LANG"] = "en_US.UTF-8";
                psi.Environment["LC_ALL"] = "en_US.UTF-8";
                ApplyMacOSPermissions(psi.FileName);
                WrapWithBash(psi);
#endif
                _ollamaProcess = Process.Start(psi);
                UnityEngine.Debug.Log($"[AIProcess] VRAM={UnityEngine.SystemInfo.graphicsMemorySize}MB, gpu-layers={AIDrivenConfig.RecommendedGpuLayers}, batch-size={AIDrivenConfig.RecommendedBatchSize}");
                UnityEngine.Debug.Log($"Starting process with command: {AISoftwarePath} {args}");
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
        // モデル指定
        ModelInfo modelInfo = ModelInfo.LoadFromFile();
        modelName = modelInfo.Name;

        string model = modelName;
        string prompt = input;
        string system = sysInput;

        try
        {
            var request = JsonConvert.DeserializeObject<OllamaRequest>(input);
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
        object payload;
        // Ollama APIに送るペイロードを構築
        payload = new
        {
            model,
            prompt,
            system,
            stream
        };
        string requestJson = JsonConvert.SerializeObject(payload);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        var responseBuilder = new StringBuilder();

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/api/generate")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        if (stream)
        { 
            var httpResponse = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorBody = await httpResponse.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Model '{model}' was not found.\nPlease obtain it with ollama pull {model}.\nDetails: {errorBody}");
            }
            httpResponse.EnsureSuccessStatusCode();

            using var responseStream = await httpResponse.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(responseStream, Encoding.UTF8);

            while (!reader.EndOfStream && !cts.Token.IsCancellationRequested)
            {
                string line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) continue;

                var chunk = JsonConvert.DeserializeObject<OllamaGenerateResponse>(line);
                if (chunk == null) continue;

                if (!string.IsNullOrEmpty(chunk.response))
                {
                    responseBuilder.Append(chunk.response);
                    onUpdate.Invoke(chunk.response);
                }

                if (chunk.done) break;
            }
        }
        else
        {
            var httpResponse = await httpClient.SendAsync(httpRequest, cts.Token);
            if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorBody = await httpResponse.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Model '{model}' was not found.\nPlease obtain it with ollama pull {model}.\nDetails: {errorBody}");
            }
            httpResponse.EnsureSuccessStatusCode();

            string responseJson = await httpResponse.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<OllamaGenerateResponse>(responseJson);
            responseBuilder.Append(result?.response ?? "");
        }

        _lastResponse = responseBuilder.ToString();
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
        if (_ollamaProcess != null && !_ollamaProcess.HasExited)
            return true;
        return _serverReady;
    }

    public void KillProcess()
    {
        if (_ollamaProcess != null && !_ollamaProcess.HasExited)
            _ollamaProcess.Kill();
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

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
    private static void ApplyMacOSPermissions(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("/bin/chmod", $"+x \"{filePath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[OllamaHTTPExecutor] chmod +x failed: {ex.Message}");
        }
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        try
        {
            Process.Start(new ProcessStartInfo("/usr/bin/xattr", $"-d com.apple.quarantine \"{filePath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[OllamaHTTPExecutor] xattr -d failed: {ex.Message}");
        }
#endif
    }

    private static void WrapWithBash(ProcessStartInfo psi)
    {
        string execPath = psi.FileName;
        string execArgs = psi.Arguments;
        string shellSafePath = "'" + execPath.Replace("'", "'\\''") + "'";
        string shellSafeArgs = execArgs.Replace("\"", "\\\"");
        psi.FileName = "/bin/bash";
        psi.Arguments = $"-c \"exec {shellSafePath} {shellSafeArgs}\"";
    }
#endif
}