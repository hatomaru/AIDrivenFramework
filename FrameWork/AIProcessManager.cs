using Cysharp.Threading.Tasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace AIDrivenFW
{
    /// <summary>
    /// AIソフトウェアのプロセスを管理するクラス
    /// </summary>
    public class AIProcess
    {
        /// <summary>
        /// プロセス状態を定義
        /// </summary>
        private enum AIState
        {
            Idle,
            Prepare,
            Running,
            Stopped
        }

        private static AIState state = AIState.Idle;
        // 占有ロック
        private static readonly object _lock = new object();
        private static readonly object _outputLock = new object();
        // 出力を受け取るビルダー
        public StringBuilder outputBuilder { get; private set; } = new StringBuilder();
        public StringBuilder errorBuilder { get; private set; } = new StringBuilder();
        private static bool isProcReady = false;       // プロセスを使用できるのか
        private static StreamWriter procStdin = null;  // 標準入力
        public static event Action<string> onPartialOutput;

        public static Process persistentProc { get; private set; } = null;  // 常駐プロセス

        /// <summary>
        /// AIプロセスのコンストラクタ、プロセスを開始する
        /// </summary>
        /// <param name="psi">プロセス情報</param>
        public AIProcess(ProcessStartInfo psi = null)
        {
            if (psi == null)
            {
                // コマンド引数
                string args = $"-m \"{ModelRepository.GetModelExecutablePath()}\" --gpu-layers 150 --batch-size 16 --prio 2 -cnv";
                // プロセスの生成
                psi = new ProcessStartInfo
                {
                    FileName = AISoftwareRepository.GetLlamaExecutablePath(),    // 呼び出しファイル名
                    Arguments = args,
                    WorkingDirectory = Path.Combine(Application.persistentDataPath, AIDrivenConfig.baseFilePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // 書き出し関係
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
            }
            state = AIState.Prepare;
            Boot(psi);
        }

        /// <summary>
        /// プロセスを起動する
        /// </summary>
        /// <param name="psi">プロセス情報</param>
        public void Boot(ProcessStartInfo psi)
        {
            // プロセスを登録
            persistentProc = new Process { StartInfo = psi };

            // 出力バッファをクリア
            lock (_outputLock)
            {
                outputBuilder.Clear();
                errorBuilder.Clear();
            }
            // レシーブ設定 (マーカー判定)
            persistentProc.OutputDataReceived += OnOutputDataReceived;
            persistentProc.OutputDataReceived += GenAI.OnOutputMarkerReceived;
            persistentProc.ErrorDataReceived += OnErrorDataReceived;
            QuitHookBehaviour.onProcessKill += KillProcess;
            // プロセスを開始
            persistentProc.Start();
            persistentProc.BeginOutputReadLine();
            persistentProc.BeginErrorReadLine();

            procStdin = persistentProc.StandardInput;
            procStdin.AutoFlush = true;
            state = AIState.Running;
        }

        /// <summary>
        /// プロセス状態を取得する
        /// </summary>
        /// <returns>プロセス状態</returns>
        public string GetProcessStatus()
        {
            return state.ToString();
        }


        /// <summary>
        /// プロセスが利用可能かチェック
        /// </summary>
        public bool IsProcessAlive()
        {
            lock (_lock)
            {
                return persistentProc != null && !persistentProc.HasExited && state <= AIState.Running;
            }
        }

        public void SendStdin(string input)
        {
            // 標準入力に書き込む
            lock (_lock)
            {
                // プロセスを使用できるか確認
                if (procStdin != null && persistentProc != null && !persistentProc.HasExited)
                {
                    // 標準入力で書き込む
                    procStdin.WriteLine(input);
                }
                else
                {
                    throw new InvalidOperationException("The process is not available.");
                }
            }
        }

        /// <summary>
        /// プロセスを強制終了する
        /// </summary>
        public void KillProcess()
        {
            if (persistentProc == null)
            {
                return;
            }
            lock (_lock)
            {
                state = AIState.Stopped;
                QuitHookBehaviour.onProcessKill -= KillProcess;
                try
                {
                    if (!persistentProc.HasExited)
                    {
                        persistentProc.Kill();
                        UnityEngine.Debug.Log("❌ The process has been forcibly terminated.");
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"❌ Failed to force quit the process: {ex.Message}");
                }

                try { procStdin?.Dispose(); } catch { }
                try { persistentProc?.Dispose(); } catch { }

                persistentProc = null;
                procStdin = null;
                isProcReady = false;
            }
        }

        /// <summary>
        /// 標準出力を受け取り、マーカーを検出する
        /// </summary>
        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            lock (_outputLock)
            {
                outputBuilder.AppendLine(e.Data);
                UnityEngine.Debug.Log($"[llama stdout] {e.Data}");
            }
        }

        /// <summary>
        /// エラーの検出
        /// </summary>
        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            lock (_outputLock)
            {
                errorBuilder.AppendLine(e.Data);
            }
            UnityEngine.Debug.Log($"[llama stderr] {e.Data}");
        }
    }

    public class GenAI
    {
        private static AIProcess process = null;
        private static bool generationComplete = false;
        private static readonly object _outputLock = new object();
        const int checkIntervalMs = 500; // 確認の間隔

        /// <summary>
        /// 実際の生成部分
        /// </summary>
        public static async UniTask<string> Generate(string input, string sysPrompt = "", IProgress<float> progress = null, CancellationToken ct = default, int timeoutMs = 120000, AIProcess attachProcess = null)
        {
            // プロンプト準備
            string systemPrompt = "";

            try
            {
                //  プロセスをアタッチ
                if (attachProcess != null)
                {
                    process = attachProcess;
                }
                // プロセスを準備
                if (process == null || !process.IsProcessAlive())
                {
                    process = new AIProcess();
                    await LoadModel(ct, ModelRepository.GetModelExecutablePath());
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

                // 標準入力にプロンプトを送信
                UnityEngine.Debug.Log($"Prompt Send: {fullPrompt[..Math.Min(100, fullPrompt.Length)]}...");
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
                        UnityEngine.Debug.Log("Detect generation completion");
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
        }

        public async static UniTask LoadModel(CancellationToken ct, string modelPath)
        {
            UnityEngine.Debug.Log("Starting new process...");

            // モデルロード完了を待機 ("> " プロンプトが表示されるまで)
            UnityEngine.Debug.Log("Model Loading...");

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
                        UnityEngine.Debug.Log("ModelLoad Complete");
                        return;
                    }
                }

                await UniTask.Delay(checkIntervalMs, cancellationToken: ct);
                elapsedMs += checkIntervalMs;
            }

            throw new TimeoutException("Model loading timed out");
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
            s = RemovePromptEcho(s, systemPrompt);
            s = RemovePromptEcho(s, userPrompt);

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
        /// プロンプトの繰り返しを取り除く
        /// </summary>
        /// <param name="source"></param>
        /// <param name="prompt"></param>
        /// <returns></returns>
        private static string RemovePromptEcho(string source, string prompt)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(prompt))
                return source;

            string normalizedPrompt = prompt?.Replace("\r\n", "\n").Trim('\r');
            if (string.IsNullOrEmpty(normalizedPrompt))
                return source;

            string result = RemoveFirstOccurrence(source, "> " + normalizedPrompt);
            result = RemoveFirstOccurrence(result, normalizedPrompt);
            return result;
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

    /// <summary>
    /// モデルファイルを管理するクラス
    /// </summary>
    public class ModelRepository
    {
        public static string GetModelExecutablePath()
        {
            // モデルファイルの拡張子確認
            RequestFile requestFile = new RequestFile();
            requestFile.Reload();
            return requestFile.Contains(".gguf");
        }
    }

    /// <summary>
    /// AIソフトウェアをを管理するクラス
    /// </summary>
    public class AISoftwareRepository
    {
        public static string GetLlamaExecutablePath()
        {
            string llamaDir = Path.Combine(Application.persistentDataPath, AIDrivenConfig.baseFilePath);
            return Path.Combine(llamaDir, "llama-cli.exe");
        }
    }

    /// <summary>
    /// フレームワーク上でファイルを管理するクラス
    /// </summary>
    public static class FileManager
    {
        /// <summary>
        /// ローカルLLM環境の準備が整っているか確認
        /// </summary>
        /// <returns>ローカルLLM環境の準備が整っているか</returns>
        public static bool IsPrepared()
        {
            RequestFile requestFile = new RequestFile();
            // AIソフトウェアの実行ファイル確認
            string result = AISoftwareRepository.GetLlamaExecutablePath();
            if (result == "null")
            {
                string llamaDir = Path.Combine(Application.persistentDataPath, AIDrivenConfig.baseFilePath);
                UnityEngine.Debug.LogError($"❌ 実行ファイルが見つかりません: {llamaDir}");
                return false;
            }
            // モデルファイルの拡張子確認
            result = ModelRepository.GetModelExecutablePath();
            UnityEngine.Debug.Log("Model file check (.gguf): " + result);
            if (result == "null") { return false; }
            return true;
        }
    }

}
