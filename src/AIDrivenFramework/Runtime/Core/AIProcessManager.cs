using AIFW.Config;
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
                return requestFile.Contains(genAIConfig.modelFilePath);
            }
            else
            {
                return requestFile.Contains(".gguf");
            }
        }
    }
}
