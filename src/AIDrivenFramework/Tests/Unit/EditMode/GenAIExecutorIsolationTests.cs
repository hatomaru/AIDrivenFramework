using AIDrivenFW.API;
using AIDrivenFW.Config;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace AIDrivenFW.Tests.Unit
{
    public class GenAIExecutorIsolationTests
    {
        [Test]
        public void Instances_KeepTheirOwnExecutors_WhenConstructedBeforeGeneration()
        {
            var firstExecutor = new FakeAIExecutor("first", "first response");
            var secondExecutor = new FakeAIExecutor("second", "second response");
            var first = new GenAI(firstExecutor);
            var second = new GenAI(secondExecutor);

            Assert.AreEqual("first", first.IsFoundAISoftware());
            Assert.AreEqual("second", second.IsFoundAISoftware());
        }

        [Test]
        public async Task Generate_UsesTheExecutorOwnedByEachInstance()
        {
            var firstExecutor = new FakeAIExecutor("first", "first response");
            var secondExecutor = new FakeAIExecutor("second", "second response");
            var first = new GenAI(firstExecutor);
            var second = new GenAI(secondExecutor);

            string firstResult = await first.Generate("first input", retryAfterInitialization: false);
            string secondResult = await second.Generate("second input", retryAfterInitialization: false);

            Assert.AreEqual("first response", firstResult);
            Assert.AreEqual("second response", secondResult);
            Assert.AreEqual(1, firstExecutor.GenerateCallCount);
            Assert.AreEqual(1, secondExecutor.GenerateCallCount);
        }

        [Test]
        public void KillProcess_KillsOnlyTheOwnedExecutor()
        {
            var firstExecutor = new FakeAIExecutor("first", "first response");
            var secondExecutor = new FakeAIExecutor("second", "second response");
            var first = new GenAI(firstExecutor);
            var second = new GenAI(secondExecutor);

            first.KillProcess();

            Assert.AreEqual(1, firstExecutor.KillProcessCallCount);
            Assert.AreEqual(0, secondExecutor.KillProcessCallCount);
            Assert.AreEqual("second", second.IsFoundAISoftware());
        }

        [Test]
        public async Task SetExecutor_AfterCoreCreation_KillsOldExecutorAndUsesReplacement()
        {
            var oldExecutor = new FakeAIExecutor("old", "old response");
            var replacement = new FakeAIExecutor("replacement", "replacement response");
            var genAI = new GenAI(oldExecutor);

            string firstResult = await genAI.Generate("before replacement", retryAfterInitialization: false);
            genAI.SetExecutor(replacement);
            string secondResult = await genAI.Generate("after replacement", retryAfterInitialization: false);

            Assert.AreEqual("old response", firstResult);
            Assert.AreEqual("replacement response", secondResult);
            Assert.AreEqual(1, oldExecutor.GenerateCallCount);
            Assert.AreEqual(1, oldExecutor.KillProcessCallCount);
            Assert.AreEqual(1, replacement.GenerateCallCount);
            Assert.AreEqual("replacement", genAI.IsFoundAISoftware());
        }

        [Test]
        public async Task SetExecutor_WithSameInstance_IsNoOp()
        {
            var executor = new FakeAIExecutor("same", "same response");
            var genAI = new GenAI(executor);

            await genAI.Generate("before no-op", retryAfterInitialization: false);
            genAI.SetExecutor(executor);
            string result = await genAI.Generate("after no-op", retryAfterInitialization: false);

            Assert.AreEqual("same response", result);
            Assert.AreEqual(0, executor.KillProcessCallCount);
            Assert.AreEqual(2, executor.GenerateCallCount);
        }

        [Test]
        public async Task SetExecutor_WithNull_ThrowsAndKeepsCurrentExecutor()
        {
            var executor = new FakeAIExecutor("current", "current response");
            var genAI = new GenAI(executor);

            var exception = Assert.Throws<ArgumentNullException>(() => genAI.SetExecutor(null));
            string result = await genAI.Generate("after null", retryAfterInitialization: false);

            Assert.AreEqual("aiExecutor", exception.ParamName);
            Assert.AreEqual("current", genAI.IsFoundAISoftware());
            Assert.AreEqual("current response", result);
            Assert.AreEqual(0, executor.KillProcessCallCount);
        }

        [Test]
        public async Task SetExecutor_WhenOldCleanupThrows_PropagatesAndKeepsOldExecutor()
        {
            var cleanupFailure = new InvalidOperationException("cleanup failed");
            var oldExecutor = new FakeAIExecutor("old", "before failure")
            {
                CleanupException = cleanupFailure
            };
            var replacement = new FakeAIExecutor("replacement", "replacement response");
            var genAI = new GenAI(oldExecutor);

            await genAI.Generate("create core", retryAfterInitialization: false);
            var exception = Assert.Throws<InvalidOperationException>(() => genAI.SetExecutor(replacement));
            oldExecutor.Response = "after failure";
            string result = await genAI.Generate("keep old", retryAfterInitialization: false);

            Assert.AreSame(cleanupFailure, exception);
            Assert.AreEqual("old", genAI.IsFoundAISoftware());
            Assert.AreEqual("after failure", result);
            Assert.AreEqual(1, oldExecutor.KillProcessCallCount);
            Assert.AreEqual(2, oldExecutor.GenerateCallCount);
            Assert.AreEqual(0, replacement.GenerateCallCount);
        }

        [Test]
        public void GenAICore_WithNullExecutor_ThrowsArgumentNullException()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new GenAICore(null));

            Assert.AreEqual("aiExecutor", exception.ParamName);
        }

        [Test]
        public async Task KillProcess_AfterGeneration_RecreatesCoreAndRestartsExecutor()
        {
            var executor = new FakeAIExecutor("fake", "response");
            var genAI = new GenAI(executor);

            Assert.AreEqual("response", await genAI.Generate("first", retryAfterInitialization: false).AsTask());
            genAI.KillProcess();
            Assert.AreEqual("response", await genAI.Generate("second", retryAfterInitialization: false).AsTask());

            Assert.AreEqual(2, executor.GenerateCallCount);
            Assert.AreEqual(2, executor.KillProcessCallCount);
            Assert.AreEqual(1, executor.StartProcessCallCount);
        }

        [Test]
        public async Task SetExecutor_BeforeCoreCreation_KillsOldExecutorAndUsesReplacement()
        {
            var oldExecutor = new FakeAIExecutor("old", "old response");
            var replacement = new FakeAIExecutor("replacement", "replacement response");
            var genAI = new GenAI(oldExecutor);

            genAI.SetExecutor(replacement);
            string result = await genAI.Generate("input", retryAfterInitialization: false).AsTask();

            Assert.AreEqual("replacement response", result);
            Assert.AreEqual(1, oldExecutor.KillProcessCallCount);
            Assert.AreEqual(0, oldExecutor.GenerateCallCount);
            Assert.AreEqual(1, replacement.GenerateCallCount);
        }

        [Test]
        public async Task Generate_WithExplicitConfig_ForwardsPromptsWithoutRequestingDefaultArguments()
        {
            var executor = new FakeAIExecutor("fake", "response");
            var genAI = new GenAI(executor);
            var config = ScriptableObject.CreateInstance<GenAIConfig>();
            config.sysPrompt = "system prompt";
            Assert.AreNotEqual(AIDrivenConfig.autoDetect, config.arguments);

            try
            {
                string result = await genAI.Generate(
                    "user input",
                    config,
                    retryAfterInitialization: false).AsTask();

                Assert.AreEqual("response", result);
                Assert.AreEqual("system prompt", executor.LastSystemInput);
                Assert.AreEqual("user input", executor.LastInput);
                Assert.AreEqual(0, executor.SetDefaultArgumentsCallCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }
    }
}
