using AIDrivenFW.Config;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIDrivenFW.Tests.Unit
{
    internal sealed class FakeAIExecutor : IAIExecutor
    {
        private readonly Queue<Exception> generateFailures = new Queue<Exception>();
        private readonly Queue<Exception> receiveFailures = new Queue<Exception>();
        private readonly Queue<Exception> extractFailures = new Queue<Exception>();
        private TaskCompletionSource<bool> generationCompletion;
        private TaskCompletionSource<bool> generationStarted;
        private TaskCompletionSource<bool> nextReceiveCompletion;
        private TaskCompletionSource<bool> receiveStarted;
        private int activeGenerateCallCount;
        private int activeReceiveCallCount;

        public FakeAIExecutor(string id, string response)
        {
            Id = id;
            Response = response;
        }

        public string Id { get; }
        public string Response { get; set; }
        public Exception CleanupException { get; set; }
        public Exception ReceiveException { get; set; }
        public Exception ExtractException { get; set; }
        public Action BeforeGenerateFailure { get; set; }
        public Action BeforeExtract { get; set; }
        public int GenerateCallCount { get; private set; }
        public int StartProcessCallCount { get; private set; }
        public int KillProcessCallCount { get; private set; }
        public int ReceiveCallCount { get; private set; }
        public int CancellationObservedCount { get; private set; }
        public int SetDefaultArgumentsCallCount { get; private set; }
        public int ActiveGenerateCallCount => Volatile.Read(ref activeGenerateCallCount);
        public int ActiveReceiveCallCount => Volatile.Read(ref activeReceiveCallCount);
        public string LastSystemInput { get; private set; }
        public string LastInput { get; private set; }
        public GenAIConfig LastStartConfig { get; private set; }
        public bool ProcessAlive { get; private set; } = true;
        public Task GenerationStarted => generationStarted?.Task ?? Task.CompletedTask;
        public Task ReceiveStarted => receiveStarted?.Task ?? Task.CompletedTask;

        public void EnqueueGenerateFailure(Exception exception)
        {
            generateFailures.Enqueue(exception ?? throw new ArgumentNullException(nameof(exception)));
        }

        public void EnqueueReceiveFailure(Exception exception)
        {
            receiveFailures.Enqueue(exception ?? throw new ArgumentNullException(nameof(exception)));
        }

        public void EnqueueExtractFailure(Exception exception)
        {
            extractFailures.Enqueue(exception ?? throw new ArgumentNullException(nameof(exception)));
        }

        public void SetProcessAlive(bool processAlive)
        {
            ProcessAlive = processAlive;
        }

        public void BlockGeneration()
        {
            generationCompletion = CreateCompletionSource();
            generationStarted = CreateCompletionSource();
        }

        public void CompleteGeneration()
        {
            generationCompletion?.TrySetResult(true);
        }

        public void BlockNextReceive()
        {
            nextReceiveCompletion = CreateCompletionSource();
            receiveStarted = CreateCompletionSource();
        }

        public UniTask StartProcessAsync(CancellationToken ct, GenAIConfig genAIConfig = null, IProgress<float> progress = null, int timeoutMs = 120000)
        {
            ct.ThrowIfCancellationRequested();
            StartProcessCallCount++;
            LastStartConfig = genAIConfig;
            ProcessAlive = true;
            return UniTask.CompletedTask;
        }

        public UniTask WaitUntilReadyAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }

        public async UniTask GenerateAsync(string sysInput, string input, CancellationToken ct, Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000)
        {
            ct.ThrowIfCancellationRequested();
            GenerateCallCount++;
            LastSystemInput = sysInput;
            LastInput = input;

            if (generateFailures.Count > 0)
            {
                Exception failure = generateFailures.Dequeue();
                BeforeGenerateFailure?.Invoke();
                throw failure;
            }

            TaskCompletionSource<bool> completion = generationCompletion;
            if (completion != null)
            {
                Interlocked.Increment(ref activeGenerateCallCount);
                generationStarted.TrySetResult(true);
                try
                {
                    using (ct.Register(() => completion.TrySetCanceled()))
                    {
                        await completion.Task;
                    }
                }
                catch (OperationCanceledException)
                {
                    CancellationObservedCount++;
                    throw;
                }
                finally
                {
                    Interlocked.Decrement(ref activeGenerateCallCount);
                }
            }

            onUpdate?.Invoke(Response);
        }

        public async UniTask<string> ReceiveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ReceiveCallCount++;

            if (ReceiveException != null)
            {
                throw ReceiveException;
            }

            if (receiveFailures.Count > 0)
            {
                throw receiveFailures.Dequeue();
            }

            TaskCompletionSource<bool> completion = nextReceiveCompletion;
            if (completion != null)
            {
                nextReceiveCompletion = null;
                Interlocked.Increment(ref activeReceiveCallCount);
                receiveStarted.TrySetResult(true);
                try
                {
                    using (ct.Register(() => completion.TrySetCanceled()))
                    {
                        await completion.Task;
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref activeReceiveCallCount);
                }
            }

            return Response;
        }

        public UniTask<bool> CheckOutput(CancellationToken token, Action<string> onUpdate = null)
        {
            token.ThrowIfCancellationRequested();
            return UniTask.FromResult(true);
        }

        public bool IsProcessAlive()
        {
            return ProcessAlive;
        }

        public bool IsDifferentAIConfig(GenAIConfig newAiConfig)
        {
            return false;
        }

        public void KillProcess()
        {
            KillProcessCallCount++;
            if (CleanupException != null)
            {
                throw CleanupException;
            }

            ProcessAlive = false;
        }

        public string SetDefaultArguments()
        {
            SetDefaultArgumentsCallCount++;
            return string.Empty;
        }

        public string SetArguments(string raw, GenAIConfig genAIConfig)
        {
            return raw;
        }

        public string IsFoundAISoftware()
        {
            return Id;
        }

        public string IsFoundModelFile()
        {
            return Id;
        }

        public string ExtractAssistantOutput(string raw)
        {
            BeforeExtract?.Invoke();

            if (ExtractException != null)
            {
                throw ExtractException;
            }

            if (extractFailures.Count > 0)
            {
                throw extractFailures.Dequeue();
            }

            return raw;
        }

        private static TaskCompletionSource<bool> CreateCompletionSource()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    internal static class UniTaskTestExtensions
    {
        public static async Task AsTask(this UniTask task)
        {
            await task;
        }

        public static async Task<T> AsTask<T>(this UniTask<T> task)
        {
            return await task;
        }
    }
}
