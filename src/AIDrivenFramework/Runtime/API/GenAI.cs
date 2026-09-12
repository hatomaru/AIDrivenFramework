using AIDrivenFW.Config;
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
        GenAICore core;

        public GenAI(IAIExecutor aiExecutor = null)
        {
            // aiExecutor が指定されていない場合は保存された設定を読み込み、実行モードに応じてルーティングする
            if (aiExecutor == null)
            {
                try
                {
                    Debug.Log("Auto routing AI executor based on saved configuration.");
                    var saved = AIDrivenFW.Config.ModelInfo.LoadFromFile();
                    if (saved != null && !string.IsNullOrEmpty(saved.Mode) && saved.Mode.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        // Ollama モード
                        SetExecutor(new OllamaHTTPExecutor());
                    }
                    else
                    {
                        // デフォルトは llama.cpp
                        SetExecutor(new LlamaCliExecutor());
                    }

                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"Failed to auto routing: {ex.Message}");
                    // フォールバック
                    SetExecutor(new LlamaCliExecutor());
                }
                return;   
            }
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