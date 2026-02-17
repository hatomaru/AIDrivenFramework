using Cysharp.Threading.Tasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AIDrivenFW.API
{
    public class GenAI
    {
        private static readonly SemaphoreSlim _generateLock = new(1, 1);
        public static AIProcess process = null;
        private static bool generationComplete = false;
        private static readonly object _outputLock = new object();
        const int checkIntervalMs = 500; // 確認の間隔

        /// <summary>
        /// AIプロセスをアタッチする
        /// </summary>
        /// <param name="target">対象プロセス</param>
        public static async UniTask AttachProcess(AIProcess target, CancellationToken ct = default)
        {
            process = target;
            await LoadModel(ct, ModelRepository.GetModelExecutablePath());
        }

        /// <summary>
        /// 実際の生成部分
        /// </summary>
        public static async UniTask<string> Generate(string input, GenAIConfig genAIConfig = null, IProgress<float> progress = null, CancellationToken ct = default, int timeoutMs = 120000)
        {
            // 設定の初期化
            if (genAIConfig == null)
            {
                genAIConfig = new GenAIConfig();
            }
            // プロンプト準備
            string systemPrompt = genAIConfig.sysPrompt;
            // ロック中は待機
            await _generateLock.WaitAsync(ct);
            try
            {

                // プロセスを準備
                if (process == null || !process.IsProcessAlive() || process.aiConfig.arguments != genAIConfig.arguments)
                {
                    // プロセスが存在する場合は終了する
                    if (process != null && process.IsProcessAlive())
                    {
                        UnityEngine.Debug.Log((process.aiConfig != genAIConfig) + " " + !process.IsProcessAlive());
                        process.KillProcess();
                    }
                    await AttachProcess(new AIProcess(genAIConfig));
                }
                generationComplete = false;
                // 出力バッファをクリアして生成開始位置をマーク
                string outputBeforeGeneration;
                lock (_outputLock)
                {
                    outputBeforeGeneration = process.outputBuilder.ToString();
                    generationComplete = false;
                }

                // システムプロンプトとユーザー入力を結合
                string fullPrompt = string.IsNullOrEmpty(systemPrompt)
                    ? input
                    : $"{systemPrompt}\n\n{input}";
                if (AIDrivenConfig.isDeepDebug)
                {
                    // 標準入力にプロンプトを送信
                    UnityEngine.Debug.Log($"Prompt Send: {fullPrompt[..Math.Min(100, fullPrompt.Length)]}...");
                }
                // 標準入力に書き込む
                process.SendStdin(fullPrompt);

                // 生成完了を待機
                int elapsedMs = 0;

                while (elapsedMs < timeoutMs)
                {
                    ct.ThrowIfCancellationRequested();

                    bool complete;
                    lock (_outputLock)
                    {
                        complete = generationComplete;
                    }

                    if (complete)
                    {
                        if (AIDrivenConfig.isDeepDebug)
                        {
                            UnityEngine.Debug.Log("Detect generation completion");
                        }
                        break;
                    }

                    // プロセスが終了していないかチェック
                    lock (_outputLock)
                    {
                        if (!process.IsProcessAlive())
                        {
                            UnityEngine.Debug.LogWarning("The process has unexpectedly terminated.");
                            break;
                        }
                    }

                    await UniTask.Delay(checkIntervalMs, cancellationToken: ct);
                    elapsedMs += checkIntervalMs;

                    // 部分出力をコールバック
                    string currentOutput;
                    lock (_outputLock)
                    {
                        currentOutput = process.outputBuilder.ToString();
                    }
                    // 現在の暫定進捗を更新
                    progress?.Report(Mathf.Clamp01((float)elapsedMs / timeoutMs) * 100f);
                }

                // タイムアウト時の処理（プロセスをキルする）
                if (elapsedMs >= timeoutMs)
                {
                    UnityEngine.Debug.LogWarning("Generation timed out. Restarting the process.");
                    process.KillProcess();
                }

                // 少し待って出力を確定
                await UniTask.Delay(100, cancellationToken: ct);

                // 結果を抽出
                string fullOutput;
                lock (_outputLock)
                {
                    fullOutput = process.outputBuilder.ToString();
                }

                string generatedOutput = fullOutput[outputBeforeGeneration.Length..];
                string result = ExtractAssistantOutput(generatedOutput, input, systemPrompt);

                if (string.IsNullOrWhiteSpace(result))
                {
                    string rawErr;
                    lock (_outputLock)
                    {
                        rawErr = process.errorBuilder.ToString();
                    }
                    return $"⚠️ An issue occurred during output. \n(response): {rawErr}";
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                UnityEngine.Debug.Log("❌ The process has been canceled.");
                return "❌ The process has been canceled.";
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"❌ Exception occurred: {ex.Message}");
                return $"❌ Exception occurred: {ex.Message}";
            }
            finally
            {
                // ロックを解放
                _generateLock.Release();
            }
        }

        /// <summary>
        /// モデルを読み込む
        /// </summary>
        public async static UniTask LoadModel(CancellationToken ct, string modelPath)
        {
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

                lock (_outputLock)
                {
                    string output = process.outputBuilder.ToString();
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
                }

                await UniTask.Delay(checkIntervalMs, cancellationToken: ct);
                elapsedMs += checkIntervalMs;
            }

            throw new TimeoutException("Model loading timed out");
        }

        /// <summary>
        /// 返答がエラーかどうか確認する
        /// </summary>
        public static bool isResponseError(string response)
        {
            if (response.Contains("Exception") || response.Contains("issue"))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 標準出力を受け取り、マーカーを検出する
        /// </summary>
        public static void OnOutputMarkerReceived(object sender, DataReceivedEventArgs e)
        {
            // 生成完了マーカーを検出
            if (e.Data.Contains("[ Prompt:") && e.Data.Contains("Generation:"))
            {
                generationComplete = true;
            }
        }

        /// 生成された出力を整える関数類
        private static string ExtractAssistantOutput(string raw, string userPrompt, string systemPrompt)
        {
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

        /// <summary>
        /// コマンド入力受付部分を取り除く
        /// </summary>
        /// <param name="source"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static string RemoveFirstOccurrence(string source, string value)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
                return source;

            int index = source.IndexOf(value, StringComparison.Ordinal);
            if (index < 0) return source;

            return source.Remove(index, value.Length);
        }
    }
}