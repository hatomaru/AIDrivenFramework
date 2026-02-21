using AIDrivenFW.Core;
using AIFW.Config;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace AIDrivenFW.API
{
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
            // デフォルトAIエグゼキュータをセットする
            GenAI testAI = new GenAI();
            AIDriven_RequestFile requestFile = new AIDriven_RequestFile();
            // AIソフトウェアの実行ファイル確認
            if (AIDrivenConfig.isDeepDebug)
            {
                UnityEngine.Debug.Log("Checking AI Software...");
            }
            string result = testAI.IsFoundAISoftware();
            if (result == "null") { return false; }
            // モデルファイルの拡張子確認
            if (AIDrivenConfig.isDeepDebug)
            {
                UnityEngine.Debug.Log("Checking Model File...");
            }
            result = ModelRepository.GetModelExecutablePath();
            if (result == "null") { return false; }
            string response = await testAI.Generate("こんにちは", ct: token);
            UnityEngine.Debug.Log("Test Response: " + response);
            testAI.KillProcess();
            if (GenAI.isResponseError(response))
            {
                return false;
            }
            return true;
        }
    }
}