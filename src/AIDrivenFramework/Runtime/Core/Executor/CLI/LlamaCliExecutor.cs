using AIDrivenFW.Config;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

public class LlamaCliExecutor : IAIExecutor
{
    private AIProcess aiProcess;
    const int checkIntervalMs = 100; // 確認の間隔  
    string AISoftwarePath = "";
    int outStartIndex = 0;

    public LlamaCliExecutor()
    {
        string baseDir = Path.Combine(UnityEngine.Application.persistentDataPath, AIDrivenConfig.Instance.BaseFilePath);
        AISoftwarePath = FindRunFile(baseDir);
    }

    public async UniTask StartProcessAsync(CancellationToken ct, GenAIConfig genAIConfig = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        if (aiProcess != null && aiProcess.IsProcessAlive())
        {
            aiProcess.KillProcess();
            if (AIDrivenConfig.Instance.IsDeepDebug)
            {
                UnityEngine.Debug.Log("Existing process killed.");
            }
        }
        if (AIDrivenConfig.Instance.IsDeepDebug)
        {
            UnityEngine.Debug.Log("Starting new process...");
        }
        string llamaDir = AISoftwarePath;
        if (genAIConfig == null)
        {
            genAIConfig = UnityEngine.ScriptableObject.CreateInstance<GenAIConfig>();
        }
        genAIConfig.aiSoftwarePath = llamaDir;
        // コマンド引数
        string args = SetArguments(genAIConfig.arguments, genAIConfig);
        UnityEngine.Debug.Log($"[AIProcess] VRAM={UnityEngine.SystemInfo.graphicsMemorySize}MB, gpu-layers={AIDrivenConfig.RecommendedGpuLayers}, batch-size={AIDrivenConfig.RecommendedBatchSize}");
        UnityEngine.Debug.Log($"Starting process with command: {llamaDir} {args}");
        genAIConfig.arguments = args;
        aiProcess = new AIProcess(genAIConfig);
        await UniTask.WaitUntil(() => aiProcess.IsProcessAlive(), cancellationToken: ct);

        await WaitUntilReadyAsync(ct, progress, timeoutMs);
        await UniTask.CompletedTask;
    }

    public async UniTask WaitUntilReadyAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        // ここでプロセスが準備できるまで待機する処理を実装  
        await WaitModelLoadAsync(ct, progress, timeoutMs);
    }

    private async UniTask WaitModelLoadAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        // ここでモデルのロードが完了するまで待機する処理を実装  
        if (AIDrivenConfig.Instance.IsDeepDebug)
        {
            // モデルロード完了を待機 ("> " プロンプトが表示されるまで)  
            UnityEngine.Debug.Log("Model Loading...");
        }
        int elapsedMs = 0;
        // タイムアウトまで待機  
        while (elapsedMs < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();

            string output = await ReceiveAsync(ct);
            //UnityEngine.Debug.Log(output);
            // "available commands:" が表示されたらモデルロード完了  
            // 特定の開始時コマンドを取得するまで待機  
            if (output.Contains("available commands:"))
            {
                if (AIDrivenConfig.Instance.IsDeepDebug)
                {
                    UnityEngine.Debug.Log("ModelLoad Complete");
                }
                return;
            }

            await UniTask.Delay(checkIntervalMs, cancellationToken: ct);
            elapsedMs += checkIntervalMs;
        }

        throw new TimeoutException("Model loading timed out");
    }

    public async UniTask GenerateAsync(string sysInput, string input, CancellationToken ct, Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        if (aiProcess == null || !aiProcess.IsProcessAlive())
        {
            UnityEngine.Debug.LogWarning("AIProcess is not initialized. Call StartProcessAsync first.");
            await StartProcessAsync(ct, null);
        }
        aiProcess.ClearOutputBuffer();
        outStartIndex = 0;
        // プロセスに入力を送る処理  
        aiProcess.SendStdin(input);
        // 生成完了を待機
        while (!await CheckOutput(ct, onUpdate))
        {
            await UniTask.Delay(checkIntervalMs, cancellationToken: ct);
        }
    }

    public UniTask<string> ReceiveAsync(CancellationToken ct)
    {
        // ここでプロセスからの出力を受け取る処理を実装  
        return UniTask.FromResult(aiProcess.GetOutputSnapshot());
    }

    public async UniTask<bool> CheckOutput(CancellationToken token, Action<string> onUpdate)
    {
        string output = await ReceiveAsync(token);
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
        return OnOutputMarkerReceived(output);
    }

    public bool IsProcessAlive()
    {
        if (aiProcess == null)
        {
            return false;
        }
        return aiProcess.IsProcessAlive();
    }

    public bool IsDifferentAIConfig(GenAIConfig newAiConfig)
    {
        return aiProcess != null　&& aiProcess.aiConfig != newAiConfig;
    }

    public void KillProcess()
    {
        aiProcess?.KillProcess();
    }

    public bool OnOutputMarkerReceived(string output)
    {
        if (output != null && output.Contains("[ Prompt:") && output.Contains("Generation:"))
        {
            return true;
        }
        return false;
    }

    public string IsFoundAISoftware()
    {
        string llamaDir = AISoftwarePath;
        if (File.Exists(llamaDir))
        {
            return llamaDir;
        }
        else
        {
            return "null";
        }
    }

    public string IsFoundModelFile()
    {
        string modelPath = ModelRepository.GetModelExecutablePath();
        if (ModelRepository.GetModelExecutablePath() != "null")
        {
            return ModelRepository.GetModelExecutablePath();
        }
        else
        {
            return "null";
        }
    }

    public string ExtractAssistantOutput(string raw)
    {
        // ここで出力から必要な情報を抽出する処理を実装
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        string s = raw.Replace("\r\n", "\n");

        // [Start thinking] から [End thinking] までのブロックを削除（存在する場合のみ）
        s = Regex.Replace(s, @"\[Start thinking\][\s\S]*?\[End thinking\]\s*", "", RegexOptions.Singleline);
        // もし [Start thinking] のみ存在する場合は、その位置以降を全て削除
        if (s.Contains("[Start thinking]") && !s.Contains("[End thinking]"))
        {
            s = Regex.Replace(s, @"\[Start thinking\][\s\S]*$", "", RegexOptions.Singleline);
        }

        // \r によるキャリッジリターン（スピナー等）をエミュレート: 各行で最後の \r 以降のみ残す
        s = Regex.Replace(s, @"[^\n]*\r", "", RegexOptions.Multiline);

        // ロール文の削除
        s = Regex.Replace(s, @"(^|\n)\s*(system|user|assistant)\s*[:：]?\s*", "$1", RegexOptions.IgnoreCase);

        // 文字列トークンの削除
        s = s.Replace("<|begin_of_text|>", "")
             .Replace("<|end_of_text|>", "")
             .Replace("<|eot_id|>", "")
             .Replace("EOF by user", "");

        // ブロックを削除
        var fenceMatch = Regex.Match(s, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
            s = fenceMatch.Groups[1].Value;

        // プレーンなテキストに変換する
        return ExtractPlainText(s);
    }

    private static string ExtractPlainText(string s)
    {
        var lines = s.Split('\n');
        var sb = new StringBuilder();
        bool inGeneration = false;

        foreach (var rawLine in lines)
        {
            string line = rawLine.TrimEnd();

            if (IsCliNoise(line)) continue;

            if (!inGeneration)
            {
                if (line.StartsWith(">"))
                {
                    inGeneration = true;
                    continue;
                }
                inGeneration = true;
            }

            if (line.StartsWith(">") || line.StartsWith("[ Prompt:") || line.StartsWith("/exit"))
                break;

            sb.AppendLine(line);
        }

        string result = sb.ToString().Trim();
        result = Regex.Replace(result, @"\n{3,}", "\n\n");
        return result;
    }

    /// <summary>
    /// ログ出力をカットする
    /// </summary>
    /// <param name="line"></param>
    /// <returns></returns>
    private static bool IsCliNoise(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        if (Regex.IsMatch(line, @"^[▄█▀]+")) return true;
        // スピナー文字 (|/-\) のみで構成された行を除去
        if (Regex.IsMatch(line, @"^[\|/\-\\]+$")) return true;
        if (line.StartsWith("ggml_") || line.StartsWith("load_backend") ||
            line.StartsWith("Loading model") || line.StartsWith("build") ||
            line.StartsWith("model") || line.StartsWith("modalities") ||
            line.StartsWith("available commands") || line == "-")
            return true;
        return false;
    }

    /// <summary>
    /// baseDir 内から実行ファイルを検索して返す
    /// </summary>
    private static string FindRunFile(string baseDir)
    {
        string softwareName = AIDrivenConfig.Instance.AiSoftwareFileName;
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

    public string SetDefaultArguments()
    { 
              return "-m {ModelPath} --system-prompt {sysPrompt} " +
              "--gpu-layers 130 " +
              "--ctx-size 2048 " +
              "--parallel 1 " +
              "--mlock";
    }

    public string SetArguments(string raw,GenAIConfig genAIConfig)
    {
        string args = raw;
        args = args.Replace("{ModelPath}", $"\"{ModelRepository.GetModelExecutablePath()}\"");
        args = args.Replace("{sysPrompt}", $"\"{genAIConfig.sysPrompt}\"");
        return args;
    }
}
