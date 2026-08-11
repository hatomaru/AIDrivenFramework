using AIDrivenFW.Config;
using AIDrivenFW.Core;
using NUnit.Framework;
using System;
using System.IO;
using UnityEngine;

namespace AIDrivenFW.Tests.Unit
{
    public class ProcessArgumentParserTests
    {
        [Test]
        public void Parse_PreservesShellSyntaxAsLiteralArgumentData()
        {
            var arguments = ProcessArgumentParser.Parse("--prompt \"$(touch /tmp/pwned) `id`\"");

            CollectionAssert.AreEqual(
                new[] { "--prompt", "$(touch /tmp/pwned) `id`" },
                arguments);
        }

        [Test]
        public void Parse_PreservesQuotedPathsAndEmptyArguments()
        {
            var arguments = ProcessArgumentParser.Parse("--model '/models/my model.gguf' --prompt \"\"");

            CollectionAssert.AreEqual(
                new[] { "--model", "/models/my model.gguf", "--prompt", string.Empty },
                arguments);
        }

        [Test]
        public void Parse_WithUnterminatedQuote_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => ProcessArgumentParser.Parse("--prompt \"unfinished"));
        }

        [Test]
        public void LlamaCppArguments_WithMissingModel_ThrowBeforeProcessLaunch()
        {
            var config = ScriptableObject.CreateInstance<GenAIConfig>();
            config.modelFilePath = null;

            try
            {
                Assert.Throws<GenAIConfigurationException>(() =>
                    LlamaCliExecutor.BuildArguments("-m {ModelPath}", config));
                Assert.Throws<GenAIConfigurationException>(() =>
                    LlamaHTTPExecutor.BuildArguments("-m {ModelPath}", config));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void LlamaCppArguments_WithConfiguredModel_UseItForServerAndClient()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), $"AIDrivenFW-{Guid.NewGuid():N}");
            string modelPath = Path.Combine(tempDirectory, "configured-model.gguf");
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllBytes(modelPath, Array.Empty<byte>());

            var config = ScriptableObject.CreateInstance<GenAIConfig>();
            config.modelFilePath = modelPath;

            try
            {
                string clientArguments = LlamaCliExecutor.BuildArguments(
                    "-m {ModelPath} --system-prompt {sysPrompt}", config);
                string serverArguments = LlamaHTTPExecutor.BuildArguments(
                    "-m {ModelPath} --host {ServerHost} --port {ServerPort}", config);
                string quotedModelPath = $"\"{Path.GetFullPath(modelPath)}\"";

                StringAssert.Contains(quotedModelPath, clientArguments);
                StringAssert.Contains(quotedModelPath, serverArguments);
                StringAssert.DoesNotContain("-m null", clientArguments);
                StringAssert.DoesNotContain("-m null", serverArguments);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                File.Delete(modelPath);
                Directory.Delete(tempDirectory);
            }
        }
    }
}
