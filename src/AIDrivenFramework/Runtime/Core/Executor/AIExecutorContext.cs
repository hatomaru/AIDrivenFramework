using System;
using System.Threading;
using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// Exclusively owned backend capabilities. Only generation is required.
    /// Process lifecycle, argument defaults and final-output extraction are optional.
    /// </summary>
    public class AIExecutorContext
    {
        private const int CheckIntervalMs = 500;
        private GenAIConfig defaultConfig;
        public readonly IProcessExecutor ProcessExecutor;
        public readonly IArgumentsExecutor ArgumentsExecutor;
        public readonly IGenerateExecutor GenerateExecutor;
        public readonly IExtractExecutor ExtractExecutor;

        public AIExecutorContext(IGenerateExecutor executor)
            : this(executor, executor as IExtractExecutor, executor as IProcessExecutor, executor as IArgumentsExecutor)
        {
        }

        public AIExecutorContext(IGenerateExecutor generateExecutor, IExtractExecutor extractExecutor = null,
            IProcessExecutor processExecutor = null, IArgumentsExecutor argumentsExecutor = null)
        {
            GenerateExecutor = generateExecutor ?? throw new ArgumentNullException(nameof(generateExecutor));
            ExtractExecutor = extractExecutor;
            ProcessExecutor = processExecutor;
            ArgumentsExecutor = argumentsExecutor;
        }

        internal async UniTask<string> GenerateAsync(string input, GenAIConfig config, bool retry,
            Action<string> onUpdate, IProgress<float> progress, CancellationToken ct, int timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            if (ProcessExecutor != null)
            {
                if (config == null)
                {
                    if (defaultConfig == null)
                    {
                        defaultConfig = GenAIConfigLifecycle.CreateOwned();
                        if (ArgumentsExecutor != null)
                            defaultConfig.arguments = ArgumentsExecutor.SetDefaultArguments();
                    }
                    config = defaultConfig;
                }
                if (retry || !ProcessExecutor.IsProcessAlive())
                {
                    ProcessExecutor.KillProcess();
                    await ProcessExecutor.StartProcessAsync(ct, config, progress, timeoutMs);
                }
            }
            return await GenerateOnceAsync(config?.sysPrompt ?? string.Empty, input, onUpdate, progress, ct, timeoutMs);
        }

        internal void StopAfterFailure(string reason)
        {
            if (ProcessExecutor == null)
                return;

            try
            {
                ProcessExecutor.KillProcess();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to stop the AI executor after {reason}: {ex.Message}");
            }
        }

        internal void ReleaseOwnedConfig() => GenAIConfigLifecycle.DestroyOwned(ref defaultConfig);

        internal async UniTask<string> GenerateOnceAsync(string systemPrompt, string input, Action<string> onUpdate, IProgress<float> progress, CancellationToken ct, int timeoutMs)
        {
            using var loadingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            UniTask loadingTask = ProcessExecutor == null ? UniTask.CompletedTask : LoadingAsync(loadingCts.Token, progress, timeoutMs);
            string output = string.Empty;
            try
            {
                Debug.Log("Generation started, waiting for completion...");
                output = await GenerateExecutor.GenerateAsync(systemPrompt, input, ct, onUpdate, progress, timeoutMs);
            }
            catch
            {
                await StopLoadingAsync(loadingCts, loadingTask, preservePrimaryException: true);
                throw;
            }
            await StopLoadingAsync(loadingCts, loadingTask, preservePrimaryException: false);
            // If the caller requested cancellation or the operation timed out while
            // we were waiting for the final receive, propagate that cancellation so
            // the outer retry/timeout handling can observe and act (e.g. kill process).
            if (ct.IsCancellationRequested)
            {
                // Preserve the original cancellation token semantics.
                ct.ThrowIfCancellationRequested();
            }
            Debug.Log("Generation completed, finalizing output...");

            if (ExtractExecutor != null)
                output = ExtractExecutor.ExtractAssistantOutput(output);

            if (string.IsNullOrWhiteSpace(output))
            {
                throw new GenAIRetryableException("The AI executor returned an empty response.");
            }

            return output;
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

                if (!ProcessExecutor.IsProcessAlive())
                {
                    throw new GenAIRetryableException("The AI executor process terminated unexpectedly.");
                }

                _ = await ProcessExecutor.ReceiveAsync(ct);
                progress?.Report(Mathf.Clamp01((float)elapsedMs / timeoutMs) * 100f);

                await UniTask.Delay(CheckIntervalMs, cancellationToken: ct);
                elapsedMs += CheckIntervalMs;
            }
        }

    }
}
