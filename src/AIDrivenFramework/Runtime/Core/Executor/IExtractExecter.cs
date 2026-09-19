using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// AI抽出インタフェース
    /// </summary>
    public interface IExtractExecutor
    {
        /// <summary>
        /// プロセカ出力から、生成された結果を抽出する
        /// </summary>
        /// <param name="raw">プロセスからの出力</param>
        /// <returns>生成結果</returns>
        string ExtractAssistantOutput(string raw);
    }
}