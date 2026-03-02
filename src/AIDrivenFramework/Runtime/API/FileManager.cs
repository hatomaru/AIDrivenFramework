using AIDrivenFW.Core;
using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System;
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
        /// <param name="defaultGenAI">準備済のGenAIクラス (オプション)</param>
        /// <returns>ローカルLLM環境の準備が整っているか</returns>
        public static async UniTask<bool> IsPrepared(CancellationToken token, GenAI defaultGenAI = null)
        {
            // デフォルトAIエグゼキュータをセットする
            GenAI testAI = defaultGenAI == null ? new GenAI() : defaultGenAI;
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

            // 実際にプロセスを起動してテスト生成を行う
            string response = null;
            try
            {
                response = await testAI.Generate("こんにちは", ct: token);
                UnityEngine.Debug.Log("Test Response: " + response);
            }
            catch (OperationCanceledException)
            {
                UnityEngine.Debug.LogWarning("Test generation was canceled.");
                try { testAI.KillProcess(); } catch { }
                return false;
            }
            catch (Exception ex)
            {
                // 生成中の例外はログ出力して準備失敗とする
                UnityEngine.Debug.LogError($"Error during test generation: {ex.Message}");
                try { testAI.KillProcess(); } catch { }
                return false;
            }
            finally
            {
                // 準備済のGenAIクラスを指定している場合はプロセスを終了しない
                try { if(defaultGenAI == null) testAI.KillProcess(); } catch { }
            }

            if (GenAI.isResponseError(response))
            {
                return false;
            }
            return true;
        }
    }
}