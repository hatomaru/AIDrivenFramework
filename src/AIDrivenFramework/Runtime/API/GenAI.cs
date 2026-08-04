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
        /// <param name="timeoutMs">初回生成、必要な初期化、再試行を含む呼び出し全体の実時間タイムアウト（ミリ秒）。ゲームのtime scaleには依存しない。</param>
        /// <param name="retryAfterInitialization"><see cref="GenAIExecutionException"/>発生時に初期化を実行後、1回再試行するかどうか (デフォルト:true)</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeoutMs"/>が0以下の場合。</exception>
        /// <exception cref="OperationCanceledException"><paramref name="ct"/>がキャンセルされた場合。</exception>
        /// <exception cref="TimeoutException">生成処理が<paramref name="timeoutMs"/>以内に完了しなかった場合。</exception>
        /// <exception cref="GenAIExecutionException">Executorでの生成が規定回数の試行後も失敗した場合。</exception>
        /// <remarks>
        /// <para>
        /// 初期化後の再試行はExecutorの通常失敗にだけ適用されます。呼び出し元からのキャンセルとタイムアウトは再試行しません。
        /// </para>
        /// <para>
        /// 生成中に<see cref="SetExecutor"/>または<see cref="KillProcess"/>を呼び出した場合の動作は保証されません。
        /// </para>
        /// </remarks>
        public async UniTask<string> Generate(string input, GenAIConfig genAIConfig = null, Action<string> onUpdate = null, IProgress<float> progress = null, CancellationToken ct = default, int timeoutMs = 120000, bool retryAfterInitialization = true)
        {
            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), timeoutMs, "The generation timeout must be greater than zero.");
            }

            ct.ThrowIfCancellationRequested();

            GenAICore activeCore = core;
            if (activeCore == null)
            {
                activeCore = new GenAICore(executor);
                core = activeCore;
            }

            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var timeoutRegistration = requestCts.CancelAfterSlim(timeoutMs, DelayType.Realtime);
            CancellationToken requestToken = requestCts.Token;

            try
            {
                string result;
                try
                {
                    result = await activeCore.GenerateAsync(input, genAIConfig, onUpdate, progress, requestToken, timeoutMs);
                }
                catch (GenAIExecutionException ex) when (retryAfterInitialization)
                {
                    Debug.LogWarning($"AI generation failed after {ex.Attempts} attempts. Initializing and retrying once: {ex.Message}");
                    await AIDrivenInitializer.Initialize(requestToken, this);
                    result = await activeCore.GenerateAsync(input, genAIConfig, onUpdate, progress, requestToken, timeoutMs);
                }

                requestToken.ThrowIfCancellationRequested();
                Debug.Log("AI generation result: " + result);
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
                throw;
            }
            catch (OperationCanceledException ex) when (requestToken.IsCancellationRequested)
            {
                throw new TimeoutException($"AI generation timed out after {timeoutMs} ms.", ex);
            }
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
