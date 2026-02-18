using AIDrivenFW.API;
using Cysharp.Threading.Tasks;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal class LlamaProcessExecutor : IAIExecutor
{
    private AIProcess aiProcess;
    const int checkIntervalMs = 500; // 確認の間隔  

    public async UniTask StartProcessAsync(CancellationToken ct, GenAIConfig genAIConfig = null)
    {
        aiProcess = new AIProcess(genAIConfig);
        await UniTask.WaitUntil(() => aiProcess.IsProcessAlive(), cancellationToken: ct);

        await WaitUntilReadyAsync(ct);
        await UniTask.CompletedTask;
    }

    public async UniTask WaitUntilReadyAsync(CancellationToken ct)
    {
        // ここでプロセスが準備できるまで待機する処理を実装  
        await WaitModelLoadAsync(ct);
    }

    private async UniTask WaitModelLoadAsync(CancellationToken ct)
    {
        // ここでモデルのロードが完了するまで待機する処理を実装  
        if (AIDrivenConfig.isDeepDebug)
        {
            UnityEngine.Debug.Log("Starting new process...");

            // モデルロード完了を待機 ("> " プロンプトが表示されるまで)  
            UnityEngine.Debug.Log("Model Loading...");
        }
        int timeoutMs = 120000; // 2分  
        int elapsedMs = 0;
        // タイムアウトまで待機  
        while (elapsedMs < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();

            string output = await ReceiveAsync(ct);
            // "available commands:" が表示されたらモデルロード完了  
            // 特定の開始時コマンドを取得するまで待機  
            if (output.Contains("available commands:"))
            {
                if (AIDrivenConfig.isDeepDebug)
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

    public async UniTask GenerateAsync(string input, CancellationToken ct)
    {
        if (aiProcess == null || !aiProcess.IsProcessAlive())
        {
            UnityEngine.Debug.LogWarning("AIProcess is not initialized. Call StartProcessAsync first.");
            await StartProcessAsync(ct, null);
        }
        // プロセスに入力を送る処理  
        aiProcess.SendStdin(input);
        // 生成完了を待機
        await UniTask.WaitUntil(() => CheckOutput(ct).GetAwaiter().GetResult());
    }

    public UniTask<string> ReceiveAsync(CancellationToken ct)
    {
        // ここでプロセスからの出力を受け取る処理を実装  
        return aiProcess.outputBuilder.ToString() != string.Empty
            ? UniTask.FromResult(aiProcess.outputBuilder.ToString())
            : UniTask.FromResult(string.Empty);
    }

    public async UniTask<bool> CheckOutput(CancellationToken token)
    {
        string output = await ReceiveAsync(CancellationToken.None);
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

    public void KillProcess()
    {
        aiProcess.KillProcess();
    }

    public string ExtractAssistantOutput(string raw)
    {
        // ここで出力から必要な情報を抽出する処理を実装  
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        string s = raw.Replace("\r\n", "\n");

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
        if (line.StartsWith("ggml_") || line.StartsWith("load_backend") ||
            line.StartsWith("Loading model") || line.StartsWith("build") ||
            line.StartsWith("model") || line.StartsWith("modalities") ||
            line.StartsWith("available commands") || line == "-")
            return true;
        return false;
    }

    public bool OnOutputMarkerReceived(string output)
    {
        if (output != null && output.Contains("[ Prompt:") && output.Contains("Generation:"))
        {
            return true;
        }
        return false;
    }
}
