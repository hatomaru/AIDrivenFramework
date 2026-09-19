using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// AI生成インタフェース
    /// </summary>
    public interface IGenerateExecutor
    {
        /// <summary>
        /// プロセスに入力を送り生成を開始する
        /// </summary>
        /// <param name="sysInput">システムプロンプト</param>
        /// <param name="input">入力</param>
        /// <param name="ct">処理を中止するキャンセルトークン。</param>
        /// <param name="onUpdate">生成途中のテキストを受け取るコールバック</param>
        /// <param name="progress">進捗報告用のIProgressインスタンス</param>
        /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
        UniTask GenerateAsync(string sysInput, string input, CancellationToken ct, Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000);

        /// <summary>
        /// 生成が完了したかをプロセスの出力から判断する
        /// </summary>
        /// <param name="token">処理を中止するキャンセルトークン。</param>
        /// <param name="onUpdate">生成途中のテキストを受け取るコールバック</param>
        /// <returns>出力マーカーが存在するか</returns>
        UniTask<bool> IsGenerated(CancellationToken token, Action<string> onUpdate = null);
    }
}