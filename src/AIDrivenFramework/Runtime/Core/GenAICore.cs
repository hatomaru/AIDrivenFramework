using Cysharp.Threading.Tasks;
using System.Threading;
using System;
using UnityEngine;
using AIFW.Config;

namespace AIDrivenFW.Core
{
    public class GenAICore : MonoBehaviour
    {
        private static readonly SemaphoreSlim _generateLock = new(1, 1);
        //public static AIProcess process = null;
        private static bool generationComplete = false;
        const int checkIntervalMs = 500; // 確認の間隔  
        private readonly IAIExecutor executor;

        public GenAICore(IAIExecutor aiExecutor)
        {
            executor = aiExecutor;
        }

        public async UniTask<string> GenerateAsync(string input, GenAIConfig genAIConfig = null, IProgress<float> progress = null, CancellationToken ct = default, int timeoutMs = 120000)
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
            if (executor == null)
            {
                throw new InvalidOperationException("AI Executor is not set. Please set an executor before calling Generate.");
            }
            try
            {
                // プロセスを準備              
                bool needRestart = false;
                if (!executor.IsProcessAlive())
                {
                    needRestart = true;
                }
                if (needRestart)
                {
                    await executor.StartProcessAsync(ct, genAIConfig);
                }
                generationComplete = false;

                // システムプロンプトとユーザー入力を結合
                string fullPrompt = string.IsNullOrEmpty(systemPrompt)
                    ? input
                    : $"{systemPrompt}\n\n{input}";
                if (AIDrivenConfig.isDeepDebug)
                {
                    // 標準入力にプロンプトを送信
                    UnityEngine.Debug.Log($"Prompt Send: {fullPrompt[..Math.Min(100, fullPrompt.Length)]}...");
                }

                var cts = new CancellationTokenSource();
                // プロンプトを送信して生成開始
                var mainTask = executor.GenerateAsync(fullPrompt, cts.Token);
                var loadingTask = LoadingAsync(cts.Token, progress, timeoutMs);
                Debug.Log("Generation completed, waiting for loading task to finish...");
                // 生成完了を待機
                await mainTask;
                // ロードィングタスクをキャンセル
                cts.Cancel();
                Debug.Log("Loading task finished, finalizing output...");

                // 少し待って出力を確定
                await UniTask.Delay(100, cancellationToken: ct);

                // 結果を抽出
                string fullOutput;
                fullOutput = await executor.ReceiveAsync(ct);

                string generatedOutput = fullOutput;
                string result = executor.ExtractAssistantOutput(generatedOutput);

                if (string.IsNullOrWhiteSpace(result))
                {
                    /*
                    string rawErr;
                    lock (_outputLock)
                    {
                        rawErr = process.errorBuilder.ToString();
                    }
                    */
                    return $"⚠️ An issue occurred during output. \n(response): ";
                    //return $"⚠️ An issue occurred during output. \n(response): {rawErr}";
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
        /// 生成完了を待機する。タイムアウトも設定可能で、タイムアウトした場合はプロセスをキルする
        /// </summary>
        /// <param name="progress">プログレス</param>
        /// <param name="timeoutMs">タイムアウトまでの秒数</param>
        private async UniTask LoadingAsync(CancellationToken ct, IProgress<float> progress = null, float timeoutMs = 120000)
        {
            // 生成完了を待機
            int elapsedMs = 0;

            while (elapsedMs < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();

                // プロセスが終了していないかチェック
                if (!executor.IsProcessAlive())
                {
                    UnityEngine.Debug.LogWarning("The process has unexpectedly terminated.");
                    break;
                }

                await UniTask.Delay(checkIntervalMs, cancellationToken: ct);
                elapsedMs += checkIntervalMs;

                // 部分出力をコールバック
                string currentOutput;
                currentOutput = await executor.ReceiveAsync(ct);
                // 現在の暫定進捗を更新
                progress?.Report(Mathf.Clamp01((float)elapsedMs / timeoutMs) * 100f);
            }

            // タイムアウト時の処理（プロセスをキルする）
            if (elapsedMs >= timeoutMs)
            {
                UnityEngine.Debug.LogWarning("Generation timed out. Restarting the process.");
                executor.KillProcess();
            }
            await UniTask.CompletedTask;
        }
    }
}
