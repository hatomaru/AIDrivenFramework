using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace AIDrivenFW.Core
{
    public interface IAIExecutor
    {
        /// <summary>
        /// プロセスを起動する
        /// </summary>
        /// <param name="genAIConfig">LLMの設定</param>
        /// <param name="progress">進捗報告用のIProgressインスタンス</param>
        /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
        UniTask StartProcessAsync(CancellationToken ct, GenAIConfig genAIConfig = null, IProgress<float> progress = null, int timeoutMs = 120000);
        /// <summary>
        /// プロセスが準備できるまで待機する
        /// </summary>
        /// <param name="progress">進捗報告用のIProgressインスタンス</param>
        /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
        UniTask WaitUntilReadyAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000);
        /// <summary>
        /// プロセスに入力を送り生成を開始する
        /// </summary>
        /// <param name="sysInput">システムプロンプト</param>
        /// <param name="input">入力</param>
        /// <param name="onUpdate">生成途中のテキストを受け取るコールバック</param>
        /// <param name="progress">進捗報告用のIProgressインスタンス</param>
        /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
        UniTask GenerateAsync(string sysInput,string input, CancellationToken ct, Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000);
        /// <summary>
        /// プロセスからの出力を受け取る
        /// </summary>
        /// <returns>出力</returns>
        UniTask<string> ReceiveAsync(CancellationToken ct);
        /// <summary>
        /// 生成が完了したかをプロセスの出力から判断する
        /// </summary>
        /// <param name="onUpdate">生成途中のテキストを受け取るコールバック</param>
        /// <returns>出力マーカーが存在するか</returns>
        UniTask<bool> CheckOutput(CancellationToken token, Action<string> onUpdate = null);
        /// <summary>
        /// プロセスが生きているか確認する
        /// </summary>
        /// <returns>プロセスの生存情報</returns>
        bool IsProcessAlive();
        /// <summary>
        /// AI設定が異なるか確認する
        /// </summary>
        /// <param name="newAiConfig">新しいAI設定</param>
        /// <returns>AI設定が異なるか</returns>
        bool IsDifferentAIConfig(GenAIConfig newAiConfig);
        /// <summary>
        /// プロセスを強制終了する
        /// </summary>
        void KillProcess();
        /// <summary>
        /// 引数を設定する
        /// </summary>
        /// <param name="raw">引数の生データ</param>
        /// <returns>デフォルト引数</returns>
        string SetArguments(string raw, GenAIConfig genAIConfig);
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
