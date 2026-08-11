using System;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// AI生成を開始できない構成不備を表す例外。
    /// </summary>
    public sealed class GenAIConfigurationException : InvalidOperationException
    {
        /// <summary>
        /// 構成エラーの詳細から例外を作成する。
        /// </summary>
        /// <param name="message">構成エラーの詳細。</param>
        public GenAIConfigurationException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// AI生成処理が規定回数の試行後も完了できなかったことを表す例外。
    /// </summary>
    public sealed class GenAIExecutionException : Exception
    {
        /// <summary>
        /// 実行された生成試行の回数。
        /// </summary>
        public int Attempts { get; }

        /// <summary>
        /// 試行回数と最後に発生した原因から例外を作成する。
        /// </summary>
        /// <param name="attempts">実行された試行回数。</param>
        /// <param name="innerException">最後に発生した原因。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="attempts"/>が0以下の場合。</exception>
        public GenAIExecutionException(int attempts, Exception innerException)
            : base($"AI generation failed after {attempts} attempts.", innerException)
        {
            if (attempts <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "The attempt count must be greater than zero.");
            }

            Attempts = attempts;
        }
    }
}
