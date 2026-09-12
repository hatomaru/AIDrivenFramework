using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;

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
    /// プロセス再起動後の再試行で回復する可能性がある一時的なAI実行エラーを表す例外。
    /// </summary>
    public sealed class GenAIRetryableException : Exception
    {
        public GenAIRetryableException(string message)
            : base(message)
        {
        }

        public GenAIRetryableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal static class GenAIExceptionClassifier
    {
        internal static bool IsRetryable(Exception exception)
        {
            if (exception == null)
            {
                return false;
            }

            return exception is GenAIRetryableException ||
                   exception is TimeoutException ||
                   exception is OperationCanceledException ||
                   exception is HttpRequestException ||
                   exception is SocketException ||
                   (exception is IOException &&
                    exception is not FileNotFoundException &&
                    exception is not DirectoryNotFoundException &&
                    exception is not DriveNotFoundException &&
                    exception is not PathTooLongException);
        }

        internal static Exception CreateHttpStatusException(string service, int statusCode, string details)
        {
            string message = $"{service} returned HTTP {statusCode}: {details}";
            if (statusCode == 408 || statusCode == 429 || statusCode >= 500)
            {
                return new GenAIRetryableException(message);
            }

            return new GenAIConfigurationException(message);
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
