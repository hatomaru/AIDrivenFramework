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
        private readonly SemaphoreSlim _generateLock = new(1, 1);
        private readonly AIExecutorContext executor;

        public GenAICore(AIExecutorContext aiExecutor)
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

                Exception lastException = null;

                for (int attempt = 1; attempt <= MaxGenerationAttempts; attempt++)
                {
                    try
                    {
                        executorOperationStarted = true;
                        string result = await executor.GenerateAsync(input, genAIConfig, attempt > 1,
                            onUpdate, progress, operationToken, timeoutMs);
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
                    executor.StopAfterFailure("cancellation");
                }

                ct.ThrowIfCancellationRequested();
                throw;
            }
            catch (OperationCanceledException ex) when (operationToken.IsCancellationRequested)
            {
                if (executorOperationStarted)
                {
                    executor.StopAfterFailure("timeout");
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

        public void Dispose()
        {
            executor.ReleaseOwnedConfig();
        }
    }
}
