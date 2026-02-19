using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace AIDrivenFW.API
{
    public class GenAI
    {
        private static IAIExecutor executor;

        public GenAI(IAIExecutor aiExecutor = null)
        {
            SetExecutor(aiExecutor ?? new LlamaProcessExecutor());
        }

        /// <summary>
        /// AI実行クラスをセットする
        /// </summary>
        /// <param name="aiExecutor">変更先のAI実行クラス</param>
        public void SetExecutor(IAIExecutor aiExecutor)
        {
            executor = aiExecutor;
        }

        /// <summary>
        /// 実際の生成部分
        /// </summary>
        public async UniTask<string> Generate(string input, GenAIConfig genAIConfig = null, IProgress<float> progress = null, CancellationToken ct = default, int timeoutMs = 120000)
        {
           var core = new GenAICore(executor);
           return await core.GenerateAsync(input, genAIConfig, progress, ct, timeoutMs);
        }

        /// <summary>
        /// プロセスを強制終了する
        /// </summary>
        public void KillProcess()
        {
            executor.KillProcess();
        }

        /// <summary>
        /// 出力がエラーかどうか確認する
        /// </summary>
        public static bool isResponseError(string response)
        {
            if (response.Contains("Exception") || response.Contains("issue"))
            {
                return true;
            }
            return false;
        }

        public string IsFoundAISoftware()
        {
            return executor.IsFoundAISoftware();
        }
    }
}