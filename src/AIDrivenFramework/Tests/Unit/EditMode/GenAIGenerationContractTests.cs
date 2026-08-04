using AIDrivenFW.API;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AIDrivenFW.Tests.Unit
{
    [Timeout(10000)]
    public class GenAIGenerationContractTests
    {
        [Test]
        public async Task GenerateAsync_WithNonPositiveTimeout_ThrowsBeforeCallingExecutor()
        {
            var executor = new FakeAIExecutor("fake", "response");
            var core = new GenAICore(executor);

            var exception = await CaptureExceptionAsync<ArgumentOutOfRangeException>(
                core.GenerateAsync("input", timeoutMs: 0).AsTask());

            Assert.AreEqual("timeoutMs", exception.ParamName);
            Assert.AreEqual(0, executor.GenerateCallCount);
            Assert.AreEqual(0, executor.KillProcessCallCount);
        }

        [Test]
        public async Task GenerateAsync_WithPreCancelledToken_DoesNotCallExecutor()
        {
            var executor = new FakeAIExecutor("fake", "response");
            var core = new GenAICore(executor);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await CaptureExceptionAsync<OperationCanceledException>(
                core.GenerateAsync("input", ct: cts.Token).AsTask());

            Assert.AreEqual(0, executor.GenerateCallCount);
            Assert.AreEqual(0, executor.KillProcessCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenCallerCancels_PropagatesCancellationAndStopsOnce()
        {
            var executor = new FakeAIExecutor("fake", "response");
            executor.BlockGeneration();
            var core = new GenAICore(executor);
            using var cts = new CancellationTokenSource();

            Task<string> generation = core.GenerateAsync("input", ct: cts.Token, timeoutMs: 5000).AsTask();
            await executor.GenerationStarted;
            cts.Cancel();

            var exception = await CaptureExceptionAsync<OperationCanceledException>(generation);
            Assert.AreEqual(cts.Token, exception.CancellationToken);
            Assert.AreEqual(1, executor.GenerateCallCount);
            Assert.AreEqual(1, executor.CancellationObservedCount);
            Assert.AreEqual(1, executor.KillProcessCallCount);
            Assert.AreEqual(0, executor.ActiveGenerateCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenDeadlineExpires_ThrowsTimeoutAndStopsOnce()
        {
            var executor = new FakeAIExecutor("fake", "response");
            executor.BlockGeneration();
            var core = new GenAICore(executor);

            var exception = await CaptureExceptionAsync<TimeoutException>(
                core.GenerateAsync("input", timeoutMs: 200).AsTask());

            StringAssert.Contains("200", exception.Message);
            Assert.AreEqual(1, executor.GenerateCallCount);
            Assert.AreEqual(1, executor.CancellationObservedCount);
            Assert.AreEqual(1, executor.KillProcessCallCount);
            Assert.AreEqual(0, executor.ActiveGenerateCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenTimeScaleIsZero_DeadlineStillUsesRealtime()
        {
            var executor = new FakeAIExecutor("fake", "response");
            executor.BlockGeneration();
            var core = new GenAICore(executor);
            float originalTimeScale = Time.timeScale;

            try
            {
                Time.timeScale = 0f;
                await CaptureExceptionAsync<TimeoutException>(
                    core.GenerateAsync("input", timeoutMs: 200).AsTask());
            }
            finally
            {
                Time.timeScale = originalTimeScale;
            }

            Assert.AreEqual(1, executor.KillProcessCallCount);
        }

        [Test]
        public async Task Generate_WhenCallerCancels_DoesNotConvertCancellationToTimeout()
        {
            var executor = new FakeAIExecutor("fake", "response");
            executor.BlockGeneration();
            var genAI = new GenAI(executor);
            using var cts = new CancellationTokenSource();

            Task<string> generation = genAI.Generate("input", ct: cts.Token, timeoutMs: 5000).AsTask();
            await executor.GenerationStarted;
            cts.Cancel();

            var exception = await CaptureExceptionAsync<OperationCanceledException>(generation);
            Assert.AreEqual(cts.Token, exception.CancellationToken);
            Assert.AreEqual(1, executor.KillProcessCallCount);
        }

        [Test]
        public async Task Generate_WhenRequestDeadlineExpires_ConvertsLinkedCancellationToTimeout()
        {
            var executor = new FakeAIExecutor("fake", "response");
            executor.BlockGeneration();
            var genAI = new GenAI(executor);

            await CaptureExceptionAsync<TimeoutException>(
                genAI.Generate("input", timeoutMs: 200).AsTask());
            Assert.AreEqual(1, executor.KillProcessCallCount);
        }

        [Test]
        public async Task GenerateAsync_AfterThreeExecutorFailures_ThrowsTypedExceptionWithLastCause()
        {
            var first = new InvalidOperationException("first");
            var second = new InvalidOperationException("second");
            var last = new InvalidOperationException("last");
            var executor = new FakeAIExecutor("fake", "response");
            executor.EnqueueGenerateFailure(first);
            executor.EnqueueGenerateFailure(second);
            executor.EnqueueGenerateFailure(last);
            var core = new GenAICore(executor);

            var exception = await CaptureExceptionAsync<GenAIExecutionException>(
                core.GenerateAsync("input").AsTask());

            Assert.AreEqual(3, exception.Attempts);
            Assert.AreSame(last, exception.InnerException);
            Assert.AreEqual(3, executor.GenerateCallCount);
            Assert.AreEqual(2, executor.StartProcessCallCount);
        }

        [Test]
        public async Task GenerateAsync_AfterTransientFailure_ReturnsSuccessfulRetry()
        {
            var executor = new FakeAIExecutor("fake", "recovered");
            executor.EnqueueGenerateFailure(new InvalidOperationException("transient"));
            var core = new GenAICore(executor);

            string result = await core.GenerateAsync("input").AsTask();

            Assert.AreEqual("recovered", result);
            Assert.AreEqual(2, executor.GenerateCallCount);
            Assert.AreEqual(1, executor.StartProcessCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenExecutorThrowsTimeoutEarly_RetriesAsExecutorFailure()
        {
            var executor = new FakeAIExecutor("fake", "recovered");
            executor.EnqueueGenerateFailure(new TimeoutException("executor startup timed out early"));
            var core = new GenAICore(executor);

            string result = await core.GenerateAsync("input", timeoutMs: 5000).AsTask();

            Assert.AreEqual("recovered", result);
            Assert.AreEqual(2, executor.GenerateCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenCallerCancelsAsExecutorThrowsTimeout_PrefersCancellation()
        {
            var executor = new FakeAIExecutor("fake", "response");
            executor.EnqueueGenerateFailure(new TimeoutException("executor timeout"));
            using var cts = new CancellationTokenSource();
            executor.BeforeGenerateFailure = cts.Cancel;
            var core = new GenAICore(executor);

            var exception = await CaptureExceptionAsync<OperationCanceledException>(
                core.GenerateAsync("input", ct: cts.Token, timeoutMs: 5000).AsTask());

            Assert.AreEqual(cts.Token, exception.CancellationToken);
            Assert.AreEqual(1, executor.GenerateCallCount);
            Assert.AreEqual(1, executor.KillProcessCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenReceiveFails_WrapsLastReceiveFailure()
        {
            var receiveFailure = new InvalidOperationException("receive failed");
            var executor = new FakeAIExecutor("fake", "response")
            {
                ReceiveException = receiveFailure
            };
            var core = new GenAICore(executor);

            var exception = await CaptureExceptionAsync<GenAIExecutionException>(
                core.GenerateAsync("input").AsTask());

            Assert.AreEqual(3, exception.Attempts);
            Assert.AreSame(receiveFailure, exception.InnerException);
            Assert.AreEqual(3, executor.ReceiveCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenOutputIsEmpty_ThrowsTypedException()
        {
            var executor = new FakeAIExecutor("fake", "   ");
            var core = new GenAICore(executor);

            var exception = await CaptureExceptionAsync<GenAIExecutionException>(
                core.GenerateAsync("input").AsTask());

            Assert.AreEqual(3, exception.Attempts);
            Assert.IsInstanceOf<InvalidOperationException>(exception.InnerException);
        }

        [Test]
        public async Task GenerateAsync_WhenCancellationCleanupFails_PreservesCancellation()
        {
            var executor = new FakeAIExecutor("fake", "response")
            {
                CleanupException = new InvalidOperationException("cleanup failed")
            };
            executor.BlockGeneration();
            var core = new GenAICore(executor);
            using var cts = new CancellationTokenSource();

            Task<string> generation = core.GenerateAsync("input", ct: cts.Token, timeoutMs: 5000).AsTask();
            await executor.GenerationStarted;
            cts.Cancel();

            await CaptureExceptionAsync<OperationCanceledException>(generation);
            Assert.AreEqual(1, executor.KillProcessCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenTimeoutCleanupFails_PreservesTimeout()
        {
            var executor = new FakeAIExecutor("fake", "response")
            {
                CleanupException = new InvalidOperationException("cleanup failed")
            };
            executor.BlockGeneration();
            var core = new GenAICore(executor);

            await CaptureExceptionAsync<TimeoutException>(
                core.GenerateAsync("input", timeoutMs: 200).AsTask());
            Assert.AreEqual(1, executor.KillProcessCallCount);
        }

        [Test]
        public async Task GenerateAsync_AwaitsGenerationMonitorBeforeReturning()
        {
            var executor = new FakeAIExecutor("fake", "response");
            executor.BlockGeneration();
            executor.BlockNextReceive();
            var core = new GenAICore(executor);

            Task<string> generation = core.GenerateAsync("input", timeoutMs: 5000).AsTask();
            await executor.GenerationStarted;
            await executor.ReceiveStarted;
            Assert.AreEqual(1, executor.ActiveReceiveCallCount);

            executor.CompleteGeneration();
            string result = await generation;

            Assert.AreEqual("response", result);
            Assert.AreEqual(0, executor.ActiveGenerateCallCount);
            Assert.AreEqual(0, executor.ActiveReceiveCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenCallerCancelsDuringExtraction_DoesNotReturnSuccess()
        {
            var executor = new FakeAIExecutor("fake", "response");
            using var cts = new CancellationTokenSource();
            executor.BeforeExtract = cts.Cancel;
            var core = new GenAICore(executor);

            var exception = await CaptureExceptionAsync<OperationCanceledException>(
                core.GenerateAsync("input", ct: cts.Token, timeoutMs: 5000).AsTask());

            Assert.AreEqual(cts.Token, exception.CancellationToken);
            Assert.AreEqual(1, executor.GenerateCallCount);
            Assert.AreEqual(1, executor.KillProcessCallCount);
        }

        [Test]
        public async Task Generate_WhenCancelledDuringInitialization_PropagatesCancellation()
        {
            var executor = CreateExecutorThatBlocksDuringInitialization();
            var genAI = new GenAI(executor);
            using var cts = new CancellationTokenSource();

            Task<string> generation = genAI.Generate("input", ct: cts.Token, timeoutMs: 5000).AsTask();
            await executor.GenerationStarted;
            cts.Cancel();

            var exception = await CaptureExceptionAsync<OperationCanceledException>(generation);
            Assert.AreEqual(cts.Token, exception.CancellationToken);
        }

        [Test]
        public async Task Generate_WhenDeadlineExpiresDuringInitialization_ThrowsTimeout()
        {
            var executor = CreateExecutorThatBlocksDuringInitialization();
            var genAI = new GenAI(executor);

            Task<string> generation = genAI.Generate("input", timeoutMs: 1000).AsTask();
            await executor.GenerationStarted;

            await CaptureExceptionAsync<TimeoutException>(generation);
        }

        [Test]
        public async Task Initialize_WhenFinalCallbackCancels_PropagatesCancellation()
        {
            var executor = new FakeAIExecutor("fake", "response");
            var genAI = new GenAI(executor);
            using var cts = new CancellationTokenSource();
            UnityEngine.Events.UnityAction<bool> handler = _ => cts.Cancel();
            AIDrivenInitializer.onPreparationFinished += handler;

            try
            {
                var exception = await CaptureExceptionAsync<OperationCanceledException>(
                    AIDrivenInitializer.Initialize(cts.Token, genAI).AsTask());
                Assert.AreEqual(cts.Token, exception.CancellationToken);
            }
            finally
            {
                AIDrivenInitializer.onPreparationFinished -= handler;
                genAI.KillProcess();
            }
        }

        [Test]
        public async Task Generate_WithErrorSymbolInSuccessfulOutput_DoesNotInitializeOrRetry()
        {
            var executor = new FakeAIExecutor("fake", "Use ❌ to render the failure icon.");
            var genAI = new GenAI(executor);

            string result = await genAI.Generate("input").AsTask();

            Assert.AreEqual("Use ❌ to render the failure icon.", result);
            Assert.AreEqual(1, executor.GenerateCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenCallerCancelsDuringFinalReceive_PropagatesOriginalTokenAndStopsOnce()
        {
            var executor = new FakeAIExecutor("fake", "response");
            executor.BlockNextReceive();
            var core = new GenAICore(executor);
            using var cts = new CancellationTokenSource();

            Task<string> generation = core.GenerateAsync("input", ct: cts.Token, timeoutMs: 5000).AsTask();
            await executor.ReceiveStarted;
            cts.Cancel();

            var exception = await CaptureExceptionAsync<OperationCanceledException>(generation);
            Assert.AreEqual(cts.Token, exception.CancellationToken);
            Assert.AreEqual(1, executor.KillProcessCallCount);
            Assert.AreEqual(0, executor.ActiveReceiveCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenDeadlineExpiresDuringFinalReceive_ThrowsTimeoutAndStopsOnce()
        {
            var executor = new FakeAIExecutor("fake", "response");
            executor.BlockNextReceive();
            var core = new GenAICore(executor);

            Task<string> generation = core.GenerateAsync("input", timeoutMs: 300).AsTask();
            await executor.ReceiveStarted;

            await CaptureExceptionAsync<TimeoutException>(generation);
            Assert.AreEqual(1, executor.KillProcessCallCount);
            Assert.AreEqual(0, executor.ActiveReceiveCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenCallerCancelsWhileWaitingForLock_DoesNotTouchWaitingExecutor()
        {
            var activeExecutor = new FakeAIExecutor("active", "first response");
            activeExecutor.BlockGeneration();
            var waitingExecutor = new FakeAIExecutor("waiting", "second response");
            var activeCore = new GenAICore(activeExecutor);
            var waitingCore = new GenAICore(waitingExecutor);
            using var cts = new CancellationTokenSource();
            Task<string> activeGeneration = activeCore.GenerateAsync("first", timeoutMs: 5000).AsTask();

            try
            {
                await activeExecutor.GenerationStarted;
                Task<string> waitingGeneration = waitingCore.GenerateAsync("second", ct: cts.Token, timeoutMs: 5000).AsTask();
                cts.Cancel();

                var exception = await CaptureExceptionAsync<OperationCanceledException>(waitingGeneration);
                Assert.AreEqual(cts.Token, exception.CancellationToken);
                Assert.AreEqual(0, waitingExecutor.GenerateCallCount);
                Assert.AreEqual(0, waitingExecutor.KillProcessCallCount);
            }
            finally
            {
                activeExecutor.CompleteGeneration();
            }

            Assert.AreEqual("first response", await activeGeneration);
        }

        [Test]
        public async Task GenerateAsync_WhenDeadlineExpiresWhileWaitingForLock_DoesNotTouchWaitingExecutor()
        {
            var activeExecutor = new FakeAIExecutor("active", "first response");
            activeExecutor.BlockGeneration();
            var waitingExecutor = new FakeAIExecutor("waiting", "second response");
            var activeCore = new GenAICore(activeExecutor);
            var waitingCore = new GenAICore(waitingExecutor);
            Task<string> activeGeneration = activeCore.GenerateAsync("first", timeoutMs: 5000).AsTask();

            try
            {
                await activeExecutor.GenerationStarted;
                await CaptureExceptionAsync<TimeoutException>(
                    waitingCore.GenerateAsync("second", timeoutMs: 300).AsTask());
                Assert.AreEqual(0, waitingExecutor.GenerateCallCount);
                Assert.AreEqual(0, waitingExecutor.KillProcessCallCount);
            }
            finally
            {
                activeExecutor.CompleteGeneration();
            }

            Assert.AreEqual("first response", await activeGeneration);
        }

        [Test]
        public async Task GenerateAsync_AfterTransientReceiveFailure_RetriesWholeGeneration()
        {
            var executor = new FakeAIExecutor("fake", "recovered");
            executor.EnqueueReceiveFailure(new InvalidOperationException("receive failed"));
            var core = new GenAICore(executor);

            string result = await core.GenerateAsync("input").AsTask();

            Assert.AreEqual("recovered", result);
            Assert.AreEqual(2, executor.GenerateCallCount);
            Assert.AreEqual(2, executor.ReceiveCallCount);
            Assert.AreEqual(1, executor.KillProcessCallCount);
            Assert.AreEqual(1, executor.StartProcessCallCount);
        }

        [Test]
        public async Task GenerateAsync_AfterTransientExtractionFailure_RetriesWholeGeneration()
        {
            var executor = new FakeAIExecutor("fake", "recovered");
            executor.EnqueueExtractFailure(new InvalidOperationException("extract failed"));
            var core = new GenAICore(executor);

            string result = await core.GenerateAsync("input").AsTask();

            Assert.AreEqual("recovered", result);
            Assert.AreEqual(2, executor.GenerateCallCount);
            Assert.AreEqual(1, executor.KillProcessCallCount);
            Assert.AreEqual(1, executor.StartProcessCallCount);
        }

        [Test]
        public async Task GenerateAsync_AfterTwoGenerateFailures_SucceedsOnThirdAttempt()
        {
            var executor = new FakeAIExecutor("fake", "recovered");
            executor.EnqueueGenerateFailure(new InvalidOperationException("first"));
            executor.EnqueueGenerateFailure(new InvalidOperationException("second"));
            var core = new GenAICore(executor);

            string result = await core.GenerateAsync("input").AsTask();

            Assert.AreEqual("recovered", result);
            Assert.AreEqual(3, executor.GenerateCallCount);
            Assert.AreEqual(2, executor.KillProcessCallCount);
            Assert.AreEqual(2, executor.StartProcessCallCount);
        }

        [Test]
        public async Task GenerateAsync_WhenProcessIsInitiallyDead_RestartsBeforeGenerating()
        {
            var executor = new FakeAIExecutor("fake", "response");
            executor.SetProcessAlive(false);
            var core = new GenAICore(executor);

            string result = await core.GenerateAsync("input").AsTask();

            Assert.AreEqual("response", result);
            Assert.AreEqual(1, executor.GenerateCallCount);
            Assert.AreEqual(1, executor.KillProcessCallCount);
            Assert.AreEqual(1, executor.StartProcessCallCount);
        }

        [Test]
        public async Task Generate_WithInitializationRetryDisabled_ThrowsTypedLastCauseWithoutExtraGeneration()
        {
            var last = new InvalidOperationException("last");
            var executor = new FakeAIExecutor("fake", "response");
            executor.EnqueueGenerateFailure(new InvalidOperationException("first"));
            executor.EnqueueGenerateFailure(new InvalidOperationException("second"));
            executor.EnqueueGenerateFailure(last);
            var genAI = new GenAI(executor);

            var exception = await CaptureExceptionAsync<GenAIExecutionException>(
                genAI.Generate("input", retryAfterInitialization: false).AsTask());

            Assert.AreEqual(3, exception.Attempts);
            Assert.AreSame(last, exception.InnerException);
            Assert.AreEqual(3, executor.GenerateCallCount);
        }

        private static FakeAIExecutor CreateExecutorThatBlocksDuringInitialization()
        {
            var executor = new FakeAIExecutor("fake", "response");
            executor.EnqueueGenerateFailure(new InvalidOperationException("first"));
            executor.EnqueueGenerateFailure(new InvalidOperationException("second"));
            executor.EnqueueGenerateFailure(new InvalidOperationException("third"));
            executor.BlockGeneration();
            return executor;
        }

        private static async Task<TException> CaptureExceptionAsync<TException>(Task task)
            where TException : Exception
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                Assert.IsInstanceOf<TException>(ex);
                return (TException)ex;
            }

            Assert.Fail($"Expected an exception of type {typeof(TException).Name}.");
            return null;
        }
    }
}
