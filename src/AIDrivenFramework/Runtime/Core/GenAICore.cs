using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace AIDrivenFW.Core
{
    public class GenAICore : IDisposable
    {
        private const int MaxGenerationAttempts = 3;
        private const int CheckIntervalMs = 500;
        private static readonly SemaphoreSlim _generateLock = new(1, 1);
        private readonly IAIExecutor executor;
        private GenAIConfig defaultConfig;

        public GenAICore(IAIExecutor aiExecutor)
        {
            executor = aiExecutor ?? throw new ArgumentNullException(nameof(aiExecutor));
        }

        /// <summary>
        /// Executorを使用してAI生成を実行する。
        /// </summary>
        /// <param name="input">ユーザー入力。</param>
        /// <param name="genAIConfig">生成設定。</param>
        /// <param name="onUpdate">生成途中のテキストを受け取るコールバック。</param>
        /// <param name="progress">生成進捗を受け取るコールバック。</param>
        /// <param name="ct">呼び出し元からのキャンセルトークン。</param>
        /// <param name="timeoutMs">ロック待機、プロセス準備、全試行を含む実時間の期限（ミリ秒）。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeoutMs"/>が0以下の場合。</exception>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/>がキャンセルされた場合。</exception>
        /// <exception cref="TimeoutException">処理が期限内に完了しなかった場合。</exception>
        /// <exception cref="GenAIConfigurationException">AI生成を開始できない構成不備がある場合。</exception>
        /// <exception cref="GenAIExecutionException">3回の試行後も生成が失敗した場合。</exception>
        public async UniTask<string> GenerateAsync(string input, GenAIConfig genAIConfig = null, Action<string> onUpdate = null, IProgress<float> progress = null, CancellationToken ct = default, int timeoutMs = 120000)
        {
            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), timeoutMs, "The generation timeout must be greater than zero.");
            }

            ct.ThrowIfCancellationRequested();

            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var timeoutRegistration = operationCts.CancelAfterSlim(timeoutMs, DelayType.Realtime);
            CancellationToken operationToken = operationCts.Token;

            bool lockTaken = false;
            bool executorOperationStarted = false;

            try
            {
                await _generateLock.WaitAsync(operationToken);
                lockTaken = true;

                GenAIConfig effectiveConfig = genAIConfig;
                bool needRestart = false;
                Exception lastException = null;

                for (int attempt = 1; attempt <= MaxGenerationAttempts; attempt++)
                {
                    try
                    {
                        operationToken.ThrowIfCancellationRequested();

                        if (effectiveConfig == null)
                        {
                            if (defaultConfig == null)
                            {
                                defaultConfig = GenAIConfigLifecycle.CreateOwned();
                                defaultConfig.arguments = executor.SetDefaultArguments();
                            }
                            effectiveConfig = defaultConfig;
                        }

                        if (attempt == 1)
                        {
                            needRestart = !executor.IsProcessAlive();
                        }

                        if (attempt > 1 && AIDrivenConfig.Instance.IsDeepDebug)
                        {
                            Debug.LogWarning($"Attempt {attempt}: Restarting the process and retrying generation...");
                        }

                        if (needRestart || attempt > 1)
                        {
                            executorOperationStarted = true;
                            executor.KillProcess();
                            await executor.StartProcessAsync(operationToken, effectiveConfig, progress, timeoutMs);
                            needRestart = false;
                        }

                        executorOperationStarted = true;
                        string result = await GenerateOnceAsync(effectiveConfig.sysPrompt, input, onUpdate, progress, operationToken, timeoutMs);
                        operationToken.ThrowIfCancellationRequested();
                        return result;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested || operationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (!GenAIExceptionClassifier.IsRetryable(ex))
                    {
                        operationToken.ThrowIfCancellationRequested();
                        Debug.LogError($"AI generation failed without retry ({ex.GetType().Name}): {ex.Message}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        operationToken.ThrowIfCancellationRequested();
                        lastException = ex;
                        Debug.LogWarning($"AI generation attempt {attempt} failed: {ex.Message}");
                    }

                    if (attempt < MaxGenerationAttempts)
                    {
                        await UniTask.Yield(cancellationToken: operationToken);
                    }
                }

                operationToken.ThrowIfCancellationRequested();
                throw new GenAIExecutionException(MaxGenerationAttempts, lastException);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (executorOperationStarted)
                {
                    TryKillProcess("cancellation");
                }

                ct.ThrowIfCancellationRequested();
                throw;
            }
            catch (OperationCanceledException ex) when (operationToken.IsCancellationRequested)
            {
                if (executorOperationStarted)
                {
                    TryKillProcess("timeout");
                }

                throw new TimeoutException($"AI generation timed out after {timeoutMs} ms.", ex);
            }
            finally
            {
                if (lockTaken)
                {
                    _generateLock.Release();
                }
            }
        }

        private async UniTask<string> GenerateOnceAsync(string systemPrompt, string input, Action<string> onUpdate, IProgress<float> progress, CancellationToken ct, int timeoutMs)
        {
            string fullPrompt = string.IsNullOrEmpty(systemPrompt)
                ? input
                : $"{systemPrompt}\n\n{input}";
            if (AIDrivenConfig.Instance.IsDeepDebug)
            {
                Debug.Log($"Prompt Send: {fullPrompt[..Math.Min(100, fullPrompt.Length)]}...");
            }

            using var loadingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            UniTask loadingTask = LoadingAsync(loadingCts.Token, progress, timeoutMs);

            try
            {
                Debug.Log("Generation started, waiting for completion...");
                await executor.GenerateAsync(systemPrompt, input, ct, onUpdate, timeoutMs: timeoutMs);
            }
            catch
            {
                await StopLoadingAsync(loadingCts, loadingTask, preservePrimaryException: true);
                throw;
            }

            await StopLoadingAsync(loadingCts, loadingTask, preservePrimaryException: false);
            Debug.Log("Generation completed, finalizing output...");

            await UniTask.Delay(100, cancellationToken: ct);
            string fullOutput = await executor.ReceiveAsync(ct);
            string result = executor.ExtractAssistantOutput(fullOutput);

            if (string.IsNullOrWhiteSpace(result))
            {
                throw new GenAIRetryableException("The AI executor returned an empty response.");
            }

            return result;
        }

        private static async UniTask StopLoadingAsync(CancellationTokenSource loadingCts, UniTask loadingTask, bool preservePrimaryException)
        {
            try
            {
                if (!loadingCts.IsCancellationRequested)
                {
                    loadingCts.Cancel();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to cancel the generation monitor: {ex.Message}");
            }

            try
            {
                await loadingTask;
            }
            catch (OperationCanceledException) when (loadingCts.IsCancellationRequested)
            {
                // Expected when generation completes, is cancelled, or times out.
            }
            catch (Exception ex) when (preservePrimaryException)
            {
                Debug.LogWarning($"Generation monitor also failed: {ex.Message}");
            }
        }

        private async UniTask LoadingAsync(CancellationToken ct, IProgress<float> progress, int timeoutMs)
        {
            int elapsedMs = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (!executor.IsProcessAlive())
                {
                    throw new GenAIRetryableException("The AI executor process terminated unexpectedly.");
                }

                await UniTask.Delay(CheckIntervalMs, cancellationToken: ct);
                elapsedMs += CheckIntervalMs;

                _ = await executor.ReceiveAsync(ct);
                progress?.Report(Mathf.Clamp01((float)elapsedMs / timeoutMs) * 100f);
            }
        }

        private void TryKillProcess(string reason)
        {
            try
            {
                executor.KillProcess();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to stop the AI executor after {reason}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            GenAIConfigLifecycle.DestroyOwned(ref defaultConfig);
        }
    }
}
