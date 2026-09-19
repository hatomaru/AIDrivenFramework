using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// AI生成プロセスのインタフェース
    /// </summary>
    public interface IProcessExecutor
    {
        /// <summary>
        /// プロセスを起動する
        /// </summary>
        /// <param name="ct">処理を中止するキャンセルトークン。</param>
        /// <param name="genAIConfig">LLMの設定</param>
        /// <param name="progress">進捗報告用のIProgressインスタンス</param>
        /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
        UniTask StartProcessAsync(CancellationToken ct, GenAIConfig genAIConfig = null, IProgress<float> progress = null, int timeoutMs = 120000);

        /// <summary>
        /// プロセスが準備できるまで待機する
        /// </summary>
        /// <param name="ct">処理を中止するキャンセルトークン。</param>
        /// <param name="progress">進捗報告用のIProgressインスタンス</param>
        /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
        UniTask WaitUntilReadyAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000);

        /// <summary>
        /// プロセスからの出力を受け取る
        /// </summary>
        /// <param name="ct">処理を中止するキャンセルトークン。</param>
        /// <returns>出力</returns>
        UniTask<string> ReceiveAsync(CancellationToken ct);

        /// <summary>
        /// プロセスが生きているか確認する
        /// </summary>
        /// <returns>プロセスの生存情報</returns>
        bool IsProcessAlive();

        /// <summary>
        /// プロセスを強制終了する
        /// </summary>
        void KillProcess();
    }
}