using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AIDrivenFW.Core
{

    /// <summary>
    /// モデルファイルを管理するクラス
    /// </summary>
    public class ModelRepository
    {
        public static string GetModelExecutablePath(GenAIConfig genAIConfig = null)
        {
            // モデルファイルの拡張子確認
            AIDriven_RequestFile requestFile = new AIDriven_RequestFile();
            requestFile.Reload();
            if (genAIConfig != null && genAIConfig.modelFilePath != AIDrivenConfig.autoDetect)
            {
                string configuredPath = genAIConfig.modelFilePath;
                if (string.IsNullOrWhiteSpace(configuredPath) ||
                    string.Equals(configuredPath, "null", StringComparison.OrdinalIgnoreCase))
                {
                    return "null";
                }

                if (File.Exists(configuredPath))
                {
                    return Path.GetFullPath(configuredPath);
                }

                return requestFile.Contains(configuredPath);
            }
            else
            {
                return requestFile.Contains(".gguf");
            }
        }

        public static string GetRequiredModelExecutablePath(GenAIConfig genAIConfig)
        {
            string modelPath = GetModelExecutablePath(genAIConfig);
            if (string.IsNullOrWhiteSpace(modelPath) ||
                string.Equals(modelPath, "null", StringComparison.OrdinalIgnoreCase))
            {
                throw new GenAIConfigurationException(
                    "A llama.cpp model file is required. Set GenAIConfig.modelFilePath to an existing model file or install a .gguf model for auto-detection.");
            }

            return modelPath;
        }

        internal static string ExpandRequiredModelArgument(string arguments, GenAIConfig genAIConfig)
        {
            if (genAIConfig == null)
            {
                throw new ArgumentNullException(nameof(genAIConfig));
            }

            if (string.IsNullOrWhiteSpace(arguments))
            {
                throw new GenAIConfigurationException(
                    "llama.cpp arguments are required and must include a model argument (-m {ModelPath}).");
            }

            string modelPath = GetRequiredModelExecutablePath(genAIConfig);
            if (arguments.Contains("{ModelPath}"))
            {
                return arguments.Replace("{ModelPath}", $"\"{modelPath}\"");
            }

            IReadOnlyList<string> parsedArguments = ProcessArgumentParser.Parse(arguments);
            for (int index = 0; index < parsedArguments.Count; index++)
            {
                if (parsedArguments[index] != "-m" && parsedArguments[index] != "--model")
                {
                    continue;
                }

                if (index + 1 >= parsedArguments.Count ||
                    string.IsNullOrWhiteSpace(parsedArguments[index + 1]) ||
                    string.Equals(parsedArguments[index + 1], "null", StringComparison.OrdinalIgnoreCase))
                {
                    throw new GenAIConfigurationException("The llama.cpp model argument has no model path.");
                }

                if (!File.Exists(parsedArguments[index + 1]))
                {
                    throw new GenAIConfigurationException(
                        $"The llama.cpp model specified in the process arguments does not exist: {parsedArguments[index + 1]}");
                }

                return arguments;
            }

            throw new GenAIConfigurationException(
                "llama.cpp arguments must include a model argument (-m {ModelPath}).");
        }
    }
}
