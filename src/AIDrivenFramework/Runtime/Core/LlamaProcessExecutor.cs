using AIDrivenFW.API;
using Cysharp.Threading.Tasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

internal class LlamaProcessExecutor : IAIExecutor
{
    private AIProcess aiProcess;

    public async UniTask StartProcessAsync(CancellationToken ct)
    {
        // ここでプロセスの初期化や準備が必要なら行う
        await UniTask.CompletedTask;
    }

    public async UniTask WaitUntilReadyAsync(CancellationToken ct)
    {
        // ここでプロセスが準備できるまで待機する処理を実装
        await UniTask.CompletedTask;
    }

    public async UniTask SendAsync(string input, CancellationToken ct)
    {
        // ここでプロセスに入力を送る処理を実装
        await UniTask.CompletedTask;
    }

    public UniTask<string> ReceiveAsync(CancellationToken ct)
    {
        // 修正: 空の文字列を返すために、UniTask.FromResult を使用  
        return UniTask.FromResult(string.Empty);
    }

    public bool OnOutputMarkerReceived()
    {
        // ここで出力を受け取り、マーカーを検出する処理を実装
        throw new NotImplementedException();
    }
}
