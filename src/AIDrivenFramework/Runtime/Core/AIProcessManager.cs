using Cysharp.Threading.Tasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AIDrivenFW
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
            if (genAIConfig != null && genAIConfig.ModelName != AIDrivenConfig.autoDetect)
            {
                return requestFile.Contains(genAIConfig.ModelName);
            }
            else
            {
                return requestFile.Contains(".gguf");
            }
        }
    }

    /// <summary>
    /// AIソフトウェアをを管理するクラス
    /// </summary>
    public class AISoftwareRepository
    {
        public static string GetLlamaExecutablePath()
        {
            string llamaDir = Path.Combine(Application.persistentDataPath, AIDrivenConfig.baseFilePath);
            return Path.Combine(llamaDir, AIDrivenConfig.aiSoftwareFileName);
        }
    }

    /// <summary>
    /// フレームワーク上でファイルを管理するクラス
    /// </summary>
    public static class FileManager
    {
        /// <summary>
        /// ローカルLLM環境の準備が整っているか確認
        /// </summary>
        /// <returns>ローカルLLM環境の準備が整っているか</returns>
        public static async UniTask<bool> IsPrepared(CancellationToken token)
        {
            AIDriven_RequestFile requestFile = new AIDriven_RequestFile();
            // AIソフトウェアの実行ファイル確認
            if (AIDrivenConfig.isDeepDebug)
            {
                UnityEngine.Debug.Log("Checking AI Software...");
            }
            string result = AISoftwareRepository.GetLlamaExecutablePath();
            if (result == "null") { return false; }
            // モデルファイルの拡張子確認
            if (AIDrivenConfig.isDeepDebug)
            {
                UnityEngine.Debug.Log("Checking Model File...");
            }
            result = ModelRepository.GetModelExecutablePath();
            if (result == "null") { return false; }
            string response = await GenAI.Generate("こんにちは", ct: token);
            UnityEngine.Debug.Log("Test Response: " + response);
            if (GenAI.isResponseError(response))
            {
                return false;
            }
            return true;
        }
    }

}
