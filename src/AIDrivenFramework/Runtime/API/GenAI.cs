using AIDrivenFW.Core;
using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace AIDrivenFW.API
{
    public class GenAI
    {
        private static IAIExecutor executor;
        GenAICore core;

        public GenAI(IAIExecutor aiExecutor = null)
        {
            SetExecutor(aiExecutor ?? new LlamaCliExecutor());
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
        /// <param name="input">プロンプト</param>
        /// <param name="genAIConfig">GenAIオプション</param>
        /// <param name="onUpdate">生成途中のテキストを受け取るコールバック</param>
        /// <param name="progress">生成の進行度を受け取るコールバック</param>
        /// <param name="timeoutMs">生成のタイムアウト時間（ミリ秒）</param>
        /// <param name="retryAfterInitialization">生成失敗時に初期化を実行後再試行するかどうか (デフォルト:true)</param>
        public async UniTask<string> Generate(string input, GenAIConfig genAIConfig = null, Action<string> onUpdate = null, IProgress<float> progress = null, CancellationToken ct = default, int timeoutMs = 120000, bool retryAfterInitialization = true)
        {
            if (core == null)
            {
                core = new GenAICore(executor);
            }

            string result = await core.GenerateAsync(input, genAIConfig, onUpdate, progress, ct, timeoutMs);
            Debug.Log("AI generation result: " + result);
            if (retryAfterInitialization && result.Contains("❌"))
            {
                Debug.LogError("AI generation failed: " + result);
                //await AIDriven_AISetupHandler.Initialize(_genAI, config);
                await AIDrivenInitializer.Initialize(ct, this);
                result = await core.GenerateAsync(input, genAIConfig, onUpdate, progress, ct, timeoutMs);
            }
            return result;
        }

        /// <summary>
        /// プロセスを強制終了する
        /// </summary>
        public void KillProcess()
        {
            core = null;
            executor.KillProcess();
        }

        /// <summary>
        /// 出力がエラーかどうか確認する
        /// </summary>
        /// <param name="response">GenAIからの出力</param>
        public static bool isResponseError(string response)
        {
            if (string.IsNullOrEmpty(response))
            {
                return true;
            }

            if (response.Contains("Exception") || response.Contains("issue") || response.Contains("❌") || response.Contains("⚠️"))
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