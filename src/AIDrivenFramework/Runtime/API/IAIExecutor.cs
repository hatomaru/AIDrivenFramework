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
        /// <param name="genAIConfig">LLMの設定</param>
        UniTask StartProcessAsync(CancellationToken ct, GenAIConfig genAIConfig = null);
        /// <summary>
        /// プロセスが準備できるまで待機する
        /// </summary>
        UniTask WaitUntilReadyAsync(CancellationToken ct);
        /// <summary>
        /// プロセスに入力を送り生成を開始する
        /// </summary>
        /// <param name="input">入力</param>
        UniTask GenerateAsync(string input, CancellationToken ct);
        /// <summary>
        /// プロセスからの出力を受け取る
        /// </summary>
        /// <returns>出力</returns>
        UniTask<string> ReceiveAsync(CancellationToken ct);
        /// <summary>
        /// 生成が完了したかをプロセスの出力から判断する
        /// </summary>
        /// <returns>出力マーカーが存在するか</returns>
        UniTask<bool> CheckOutput(CancellationToken token);
        /// <summary>
        /// プロセスが生きているか確認する
        /// </summary>
        /// <returns>プロセスの生存情報</returns>
        bool IsProcessAlive();
        /// <summary>
        /// プロセスを強制終了する
        /// </summary>
        void KillProcess();
        /// <summary>
        /// AIソフトウェアが存在するか確認しファイルパスを返す
        /// </summary>
        /// <returns>AIソフトウェアのファイルパス</returns>
        string IsFoundAISoftware();
        /// <summary>
        /// モデルファイルが存在するか確認しファイルパスを返す
        /// </summary>
        /// <returns>モデルファイルのファイルパス</returns>
        string IsFoundModelFile();
        /// <summary>
        /// プロセスからの出力を解析して、必要な情報を抽出する
        /// </summary>
        /// <param name="raw">プロセスからの出力</param>
        /// <returns>抽出した出力</returns>
        string ExtractAssistantOutput(string raw);
    }
}
