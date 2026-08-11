using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System;
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
                throw new InvalidOperationException(
                    "A llama.cpp model file is required. Set GenAIConfig.modelFilePath to an existing model file or install a .gguf model for auto-detection.");
            }

            return modelPath;
        }
    }
}
