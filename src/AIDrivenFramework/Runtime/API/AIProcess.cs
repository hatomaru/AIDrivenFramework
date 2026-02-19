using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AIDrivenFW.API
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

        private AIState state = AIState.Idle;
        // AI設定クラス
        public GenAIConfig aiConfig { get; private set; } = null;
        // 占有ロック
        private readonly object _lock = new object();
        private readonly object _outputLock = new object();
        // 出力を受け取るビルダー
        public StringBuilder outputBuilder { get; private set; } = new StringBuilder();
        public StringBuilder errorBuilder { get; private set; } = new StringBuilder();
        private static bool isProcReady = false;       // プロセスを使用できるのか
        private static StreamWriter procStdin = null;  // 標準入力
        private Thread _stdoutThread = null;           // stdout 読み取りスレッド
        private volatile bool _stopReading = false;    // 読み取り停止フラグ
        // 出力イベント
        public static event Action<string> onPartialOutput;

        public Process persistentProc { get; private set; } = null;  // 常駐プロセス

        /// <summary>
        /// AIプロセスのコンストラクタ、プロセスを開始する
        /// </summary>
        /// <param name="psi">プロセス情報</param>
        public AIProcess(GenAIConfig genAIConfig = null)
        {
            if (genAIConfig == null)
            {
                genAIConfig = new GenAIConfig();
            }
            aiConfig = genAIConfig;
            // コマンド引数
            string args = $"-m \"{ModelRepository.GetModelExecutablePath()}\" {genAIConfig.arguments}";
            // プロセスの生成
            ProcessStartInfo psi = new ProcessStartInfo
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
            UnityEngine.Debug.Log($"{psi.FileName} {psi.Arguments}");
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
            persistentProc.ErrorDataReceived += OnErrorDataReceived;
            Application.quitting += KillProcess;
            // プロセスを開始
            persistentProc.Start();
            persistentProc.BeginErrorReadLine();

            // stdout を BaseStream から直接読み取るスレッドを起動
            _stopReading = false;
            _stdoutThread = new Thread(ReadStdoutLoop)
            {
                IsBackground = true,
                Name = "AIProcess_StdoutReader"
            };
            _stdoutThread.Start();

            // 標準入力ストリームを取得
            procStdin = new StreamWriter(persistentProc.StandardInput.BaseStream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            state = AIState.Running;
        }

        public void RegisterOutputListener(Action<string> listener)
        {
            onPartialOutput += listener;
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
                return persistentProc != null && !persistentProc.HasExited && state >= AIState.Running;
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
                Application.quitting -= KillProcess;
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

                // stdout 読み取りスレッドを停止
                _stopReading = true;

                try { procStdin?.Dispose(); } catch { }
                try { persistentProc?.Dispose(); } catch { }

                persistentProc = null;
                procStdin = null;
            }
        }

        /// <summary>
        /// stdout BaseStream を直接読み取るループ（別スレッドで実行）
        /// </summary>
        private void ReadStdoutLoop()
        {
            try
            {
                var reader = new StreamReader(
                    persistentProc.StandardOutput.BaseStream,
                    new UTF8Encoding(false));

                var lineBuffer = new StringBuilder();
                int ch;
                while (!_stopReading && (ch = reader.Read()) != -1)
                {
                    if (ch == '\n')
                    {
                        // 行末の \r を除去
                        if (lineBuffer.Length > 0 && lineBuffer[lineBuffer.Length - 1] == '\r')
                            lineBuffer.Length--;

                        string line = lineBuffer.ToString();
                        lineBuffer.Clear();

                        lock (_outputLock)
                        {
                            outputBuilder.AppendLine(line);
                            onPartialOutput?.Invoke(line);
                            if (AIDrivenConfig.isDeepDebug)
                            {
                                UnityEngine.Debug.Log($"[llama stdout] {line}");
                            }
                        }
                    }
                    else
                    {
                        lineBuffer.Append((char)ch);
                    }
                }

                // ストリーム終端にバッファが残っている場合も処理
                if (lineBuffer.Length > 0)
                {
                    string line = lineBuffer.ToString();
                    lock (_outputLock)
                    {
                        outputBuilder.AppendLine(line);
                        onPartialOutput?.Invoke(line);
                    }
                }
            }
            catch (Exception ex) when (!_stopReading)
            {
                UnityEngine.Debug.LogError($"[AIProcess] stdout 読み取りエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// エラーの検出
        /// </summary>
        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            //if (string.IsNullOrEmpty(e.Data)) return;

            lock (_outputLock)
            {
                errorBuilder.AppendLine(e.Data);
            }
            if (AIDrivenConfig.isDeepDebug)
            {
                UnityEngine.Debug.Log($"[llama stderr] {e.Data}");
            }
        }
    }
}