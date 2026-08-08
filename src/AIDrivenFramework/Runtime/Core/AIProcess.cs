using AIDrivenFW.Config;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AIDrivenFW.Core
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
        StreamReader reader = null;  // stdout 読み取り用
        StringBuilder outputBuilder = new StringBuilder();
        StringBuilder errorBuilder = new StringBuilder();
        private Stream procStdinStream = null;  // 標準入力ストリーム
        private static readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(false);
        private Thread _stdoutThread = null;           // stdout 読み取りスレッド
        private volatile bool _stopReading = false;    // 読み取り停止フラグ
        // 出力イベント
        public event Action<string> onPartialOutput;

        public Process persistentProc { get; private set; } = null;  // 常駐プロセス
        private readonly bool _redirectStdIn;
        private readonly bool _redirectStdOut;
        private readonly bool _redirectStdErr;

        /// <summary>
        /// AIプロセスのコンストラクタ、プロセスを開始する
        /// </summary>
        /// <param name="genAIConfig">生成設定</param>
        /// <param name="redirectStdOut">標準出力をリダイレクトするか</param>
        /// <param name="redirectStdIn">標準入力をリダイレクトするか</param>
        /// <param name="redirectStdErr">標準エラー出力をリダイレクトするか</param>
        public AIProcess(GenAIConfig genAIConfig = null, bool redirectStdIn = true, bool redirectStdOut = true, bool redirectStdErr = true)
        {
            if (genAIConfig == null)
            {
                genAIConfig = ScriptableObject.CreateInstance<GenAIConfig>();
            }
            aiConfig = genAIConfig;
            _redirectStdIn = redirectStdIn;
            _redirectStdOut = redirectStdOut;
            _redirectStdErr = redirectStdErr;
            // プロセスの生成
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = aiConfig.aiSoftwarePath,    // 呼び出しファイル名
                WorkingDirectory = Path.Combine(Application.persistentDataPath, AIDrivenConfig.baseFilePath),
                UseShellExecute = false,
                CreateNoWindow = true,
                // 書き出し関係
                RedirectStandardInput = redirectStdIn,
                RedirectStandardOutput = redirectStdOut,
                RedirectStandardError = redirectStdErr,
                // StandardOutputEncoding/StandardErrorEncoding are set below only when redirection is enabled
            };
            foreach (string argument in ProcessArgumentParser.Parse(aiConfig.arguments))
            {
                psi.ArgumentList.Add(argument);
            }
            UnityEngine.Debug.Log($"{psi.FileName} {string.Join(" ", psi.ArgumentList)}");
            state = AIState.Prepare;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            // UTF-8 ロケールを明示的に設定
            psi.Environment["LANG"] = "en_US.UTF-8";
            psi.Environment["LC_ALL"] = "en_US.UTF-8";
            EnsureExecutablePermission(psi.FileName);
#endif
            // エンコーディングはリダイレクトが有効な場合のみ設定（無効だと例外になるため）
            if (redirectStdOut)
            {
                psi.StandardOutputEncoding = Encoding.UTF8;
            }
            if (redirectStdErr)
            {
                psi.StandardErrorEncoding = Encoding.UTF8;
            }
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
            if (_redirectStdErr)
            {
                persistentProc.ErrorDataReceived += OnErrorDataReceived;
            }
            Application.quitting += KillProcess;
            // プロセスを開始
            persistentProc.Start();
            if (_redirectStdErr)
            {
                persistentProc.BeginErrorReadLine();
            }

            // stdout を BaseStream から直接読み取るスレッドを起動
            if (_redirectStdOut)
            {
                _stopReading = false;
                _stdoutThread = new Thread(ReadStdoutLoop)
                {
                    IsBackground = true,
                    Name = "AIProcess_StdoutReader"
                };
                _stdoutThread.Start();
            }

            // 標準入力ストリームを取得（StreamWriter を介さず BaseStream を直接使用）
            if (_redirectStdIn)
            {
                procStdinStream = persistentProc.StandardInput.BaseStream;
            }
            state = AIState.Running;
        }

        public void ClearOutputBuffer()
        {
            lock (_outputLock)
            {
                outputBuilder.Clear();
                errorBuilder.Clear();
            }
        }

        /// <summary>
        /// 現在の出力をスナップショットとして取得する
        /// </summary>
        /// <returns>現在の出力</returns>
        public string GetOutputSnapshot()
        {
            lock (_outputLock)
            {
                return outputBuilder.ToString();
            }
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
                if (procStdinStream != null && persistentProc != null && !persistentProc.HasExited)
                {
                    // UTF-8 バイト列を直接書き込み（StreamWriter のバッファリング問題を回避）
                    byte[] data = _utf8NoBom.GetBytes(input + "\n");
                    procStdinStream.Write(data, 0, data.Length);
                    procStdinStream.Flush();
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
                try { reader?.Dispose(); } catch { }
                try { procStdinStream?.Dispose(); } catch { }
                try { persistentProc?.Dispose(); } catch { }
                Application.quitting -= KillProcess;

                persistentProc = null;
                procStdinStream = null;
            }
        }

        /// <summary>
        /// stdout BaseStream を直接読み取るループ（別スレッドで実行）
        /// </summary>
        private void ReadStdoutLoop()
        {
            try
            {
                reader = new StreamReader(
                    persistentProc.StandardOutput.BaseStream,
                    new UTF8Encoding(false));

                var lineBuffer = new StringBuilder();
                int ch;
                while (!_stopReading && (ch = reader.Read()) != -1)
                {
                    if (ch == '\n')
                    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                        // Windows の \r\n に対応: 末尾の \r を除去
                        if (lineBuffer.Length > 0 && lineBuffer[lineBuffer.Length - 1] == '\r')
                            lineBuffer.Length--;
#endif
                        string line = lineBuffer.ToString();
                        lineBuffer.Clear();

                        lock (_outputLock)
                        {
                            outputBuilder.AppendLine(line);
                            onPartialOutput?.Invoke(line);
                            if (AIDrivenConfig.Instance.IsDeepDebug)
                            {
                                UnityEngine.Debug.Log($"[llama stdout] {line}");
                            }
                        }
                    }
                    else if (ch == '\r')
                    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                        // Windows: \r\n の \r をバッファに積む（\n 到達時に除去）
                        lineBuffer.Append((char)ch);
#else
                        // macOS/Linux: スタンドアロン \r（スピナー等）は読み捨てる
                        // キャリッジリターン: 行頭に戻る動作をエミュレート
                        // スピナー等の上書き文字を蓄積しないようバッファをクリア
                        lineBuffer.Clear();
#endif
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
            if (AIDrivenConfig.Instance.IsDeepDebug)
            {
                UnityEngine.Debug.Log($"[llama stderr] {e.Data}");
            }
        }

        /// <summary>
        /// 成功の検出
        /// </summary>
        /// <returns>成功したか</returns>
        public bool isSuccessful()
        {
            return errorBuilder.ToString().Contains("success") || outputBuilder.ToString().Contains("success");
        }


#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        /// <summary>
        /// macOS/Linux: 実行権限を付与する。Gatekeeperの隔離属性は変更しない。
        /// </summary>
        private static void EnsureExecutablePermission(string filePath)
        {
            try
            {
                var chmodStartInfo = new ProcessStartInfo("/bin/chmod")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                chmodStartInfo.ArgumentList.Add("+x");
                chmodStartInfo.ArgumentList.Add(filePath);
                Process.Start(chmodStartInfo)?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[AIProcess] chmod +x failed: {ex.Message}");
            }
        }
#endif
    }
}
