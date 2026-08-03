using AIDrivenFW.Core;
using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace AIDrivenFW.API
{
    /// <summary>
    /// AI生成処理を管理するAPI。
    /// </summary>
    /// <remarks>
    /// <para>
    /// このクラスは、渡された<see cref="IAIExecutor"/>を排他的に所有する前提でライフサイクルを管理します。
    /// </para>
    /// <para>
    /// 同じ実行器オブジェクトを複数の<see cref="GenAI"/>で共有すると、一方の<see cref="SetExecutor"/>または
    /// <see cref="KillProcess"/>が他方の生成資源も停止する可能性があるため、共有は推奨されません。
    /// </para>
    /// </remarks>
    public class GenAI
    {
        private IAIExecutor executor;
        private GenAICore core;

        /// <summary>
        /// 指定したAI実行クラスを所有する生成APIを作成する。
        /// </summary>
        /// <param name="aiExecutor">
        /// このインスタンスが排他的に所有するAI実行クラス。<see langword="null"/>の場合は
        /// <see cref="LlamaCliExecutor"/>を作成する。
        /// </param>
        /// <remarks>
        /// 同じ実行器オブジェクトを複数の<see cref="GenAI"/>へ渡すと、一方のライフサイクル操作が
        /// 他方の生成資源を停止する可能性があるため、インスタンスごとに異なる実行器を渡してください。
        /// </remarks>
        public GenAI(IAIExecutor aiExecutor = null)
        {
            SetExecutor(aiExecutor ?? new LlamaCliExecutor());
        }

        /// <summary>
        /// AI実行クラスをセットする
        /// </summary>
        /// <param name="aiExecutor">このインスタンスが排他的に所有する変更先のAI実行クラス。</param>
        /// <exception cref="ArgumentNullException"><paramref name="aiExecutor"/>が<see langword="null"/>の場合。</exception>
        /// <remarks>
        /// <para>
        /// 現在の実行器と同じオブジェクトを渡した場合は何もしません。異なるオブジェクトを渡した場合は、
        /// 旧実行器の<see cref="IAIExecutor.KillProcess"/>を同期的に実行し、成功した後に実行器を切り替えて生成コアを破棄します。
        /// </para>
        /// <para>
        /// 旧実行器の<see cref="IAIExecutor.KillProcess"/>が例外を送出した場合、例外をそのまま伝播し、実行器と生成コアの参照は
        /// 旧状態のまま維持します。ただし、例外までに旧実行器が外部プロセスなどへ与えた副作用はロールバックできません。
        /// </para>
        /// <para>
        /// 同じ実行器オブジェクトを他の<see cref="GenAI"/>と共有すると、その生成資源も停止する可能性があります。
        /// また、<see cref="Generate"/>の実行中に呼び出した場合の動作は保証されません。
        /// </para>
        /// </remarks>
        public void SetExecutor(IAIExecutor aiExecutor)
        {
            if (aiExecutor == null)
            {
                throw new ArgumentNullException(nameof(aiExecutor));
            }

            if (ReferenceEquals(executor, aiExecutor))
            {
                return;
            }

            executor?.KillProcess();
            executor = aiExecutor;
            core = null;
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
        /// <remarks>
        /// 生成中に<see cref="SetExecutor"/>または<see cref="KillProcess"/>を呼び出した場合の動作は保証されません。
        /// </remarks>
        public async UniTask<string> Generate(string input, GenAIConfig genAIConfig = null, Action<string> onUpdate = null, IProgress<float> progress = null, CancellationToken ct = default, int timeoutMs = 120000, bool retryAfterInitialization = true)
        {
            GenAICore activeCore = core;
            if (activeCore == null)
            {
                activeCore = new GenAICore(executor);
                core = activeCore;
            }

            string result = await activeCore.GenerateAsync(input, genAIConfig, onUpdate, progress, ct, timeoutMs);
            Debug.Log("AI generation result: " + result);
            if (retryAfterInitialization && result.Contains("❌"))
            {
                Debug.LogError("AI generation failed: " + result);
                //await AIDriven_AISetupHandler.Initialize(_genAI, config);
                await AIDrivenInitializer.Initialize(ct, this);
                result = await activeCore.GenerateAsync(input, genAIConfig, onUpdate, progress, ct, timeoutMs);
            }
            return result;
        }

        /// <summary>
        /// 所有するAI実行クラスのプロセスを強制終了する
        /// </summary>
        /// <remarks>
        /// <para>
        /// 所有する実行器の<see cref="IAIExecutor.KillProcess"/>を同期的に実行します。同じ実行器オブジェクトを
        /// 他の<see cref="GenAI"/>と共有している場合、その生成資源も停止する可能性があります。
        /// </para>
        /// <para>
        /// 実行器からの例外はそのまま伝播し、生成コアの参照は維持します。ただし、例外までに実行器が外部プロセスなどへ
        /// 与えた副作用はロールバックできません。<see cref="Generate"/>の実行中に呼び出した場合の動作は保証されません。
        /// </para>
        /// </remarks>
        public void KillProcess()
        {
            executor.KillProcess();
            core = null;
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
