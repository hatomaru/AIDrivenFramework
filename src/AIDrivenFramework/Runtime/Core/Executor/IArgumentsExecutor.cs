using AIDrivenFW.Config;
namespace AIDrivenFW.Core
{
    /// <summary>
    /// AI生成引数インタフェース
    /// </summary>
    public interface IAArgumentsExecutor
    {
        /// <summary>
        /// AI設定が異なるか確認する (削除候補)
        /// </summary>
        /// <param name="newAiConfig">新しいAI設定</param>
        /// <returns>AI設定が異なるか</returns>
        bool IsDifferentAIConfig(GenAIConfig newAiConfig);

        /// <summary>
        /// デフォルト引数を設定する
        /// </summary>
        /// <returns>デフォルト引数</returns>
        string SetDefaultArguments();

        /// <summary>
        /// 引数を設定する
        /// </summary>
        /// <param name="raw">引数の生データ</param>
        /// <returns>設定後の引数</returns>
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
    }
}