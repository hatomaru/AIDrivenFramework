using AIDrivenFW.API;
using Cysharp.Threading.Tasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace AIDrivenFW.API
{
    public interface IAIExecutor
    {
        /// <summary>
        /// プロセスを起動する
        /// </summary>
        UniTask StartProcessAsync(CancellationToken ct);
        /// <summary>
        /// プロセスが準備できるまで待機する
        /// </summary>
        UniTask WaitUntilReadyAsync(CancellationToken ct);
        /// <summary>
        /// プロセスに入力を送る
        /// </summary>
        /// <param name="input">入力</param>
        UniTask SendAsync(string input, CancellationToken ct);
        /// <summary>
        /// プロセスからの出力を受け取る
        /// </summary>
        /// <returns>出力</returns>
        UniTask<string> ReceiveAsync(CancellationToken ct);
    }
}
