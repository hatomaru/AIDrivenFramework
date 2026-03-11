using AIDrivenFW.Config;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Threading;

public class CustomExecutor : IAIExecutor
{
    private AIProcess aiProcess;
    const int checkIntervalMs = 500;
    string AISoftwarePath = "";
    int outStartIndex = 0;

    public CustomExecutor()
    {
        string baseDir = Path.Combine(UnityEngine.Application.persistentDataPath, AIDrivenConfig.baseFilePath);
        AISoftwarePath = FindRunFile(baseDir);
    }

    public async UniTask StartProcessAsync(CancellationToken ct, GenAIConfig genAIConfig = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        if (genAIConfig == null) genAIConfig = new GenAIConfig();

        genAIConfig.aiSoftwarePath = AISoftwarePath;
        aiProcess = new AIProcess(genAIConfig);

        await UniTask.WaitUntil(
            () => aiProcess.IsProcessAlive(),
            cancellationToken: ct
        );

        await WaitUntilReadyAsync(ct, progress);
    }

    public async UniTask WaitUntilReadyAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        await WaitModelLoadAsync(ct);
    }

    private async UniTask WaitModelLoadAsync(CancellationToken ct)
    {
        int timeoutMs = 120000;
        int elapsedMs = 0;

        while (elapsedMs < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();

            string output = await ReceiveAsync(ct);

            if (output.Contains("available commands:"))
                return;

            await UniTask.Delay(checkIntervalMs, cancellationToken: ct);
            elapsedMs += checkIntervalMs;
        }

        throw new TimeoutException("Model loading timed out");
    }

    public async UniTask GenerateAsync(string sysInput, string input, CancellationToken ct, Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        aiProcess.ClearOutputBuffer();
        outStartIndex = 0;

        aiProcess.SendStdin(input);

        while (!await CheckOutput(ct,onUpdate))
        {
            await UniTask.Delay(checkIntervalMs, cancellationToken: ct);
        }
    }

    public UniTask<string> ReceiveAsync(CancellationToken ct)
    {
        return UniTask.FromResult("mock response");
    }

    public async UniTask<bool> CheckOutput(CancellationToken token, Action<string> onUpdate)
    {
        string output = await ReceiveAsync(token);
        // ストリーミング出力の更新を処理
        if (onUpdate != null)
        {
            string extracted = ExtractAssistantOutput(output);
            if (!string.IsNullOrEmpty(extracted) && extracted.Length > outStartIndex)
            {
                for (int i = outStartIndex; i < extracted.Length; i++)
                {
                    onUpdate(extracted[i].ToString());
                    outStartIndex = i + 1;
                    await UniTask.Delay(20, cancellationToken: token);
                }
            }
        }
        return true;
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

    /// <summary>
    /// baseDir 内から実行ファイルを検索して返す
    /// </summary>
    private static string FindRunFile(string baseDir)
    {
        string softwareName = "mock-cli";
        if (!Directory.Exists(baseDir))
            return Path.Combine(baseDir, $"{softwareName}.exe");
        // アーカイブ・ライブラリ・データファイルは除外
        string[] excludeExtensions = { ".zip", ".tar", ".gz", ".gguf", ".dylib", ".dll", ".json", ".txt", ".md" };
        string[] all = Directory.GetFiles(baseDir, $"{softwareName}*", SearchOption.AllDirectories);

        string fallback = null;
        foreach (var f in all)
        {
            string ext = Path.GetExtension(f);
            bool isExcluded = false;
            foreach (var ex in excludeExtensions)
            {
                if (string.Equals(ext, ex, StringComparison.OrdinalIgnoreCase))
                {
                    isExcluded = true;
                    break;
                }
            }
            if (isExcluded) continue;

            if (fallback == null) fallback = f;

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            // macOS: 拡張子なしの実行ファイルを優先
            if (Path.GetFileName(f) == $"{softwareName}")
                return f;
#else
            // Windows: "llama-cli.exe" を優先
            if (string.Equals(Path.GetFileName(f), $"{softwareName}.exe", StringComparison.OrdinalIgnoreCase))
                return f;
#endif
        }

        return fallback ?? Path.Combine(baseDir, softwareName);
    }
}