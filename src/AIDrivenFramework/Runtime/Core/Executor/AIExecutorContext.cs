using System;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// AI生成バックエンドの実行契約。
    /// </summary>
    public class AIExecutorContext
    {
        public readonly IProcessExecutor ProcessExecutor;
        public readonly IArgumentsExecutor ArgumentsExecutor;
        public readonly IGenerateExecutor GenerateExecutor;
        public readonly IExtractExecutor ExtractExecutor;

        public AIExecutorContext(IGenerateExecutor executor)
        {
            if(executor is not IExtractExecutor || executor is not IProcessExecutor || executor is not IArgumentsExecutor)
            {
                throw new ArgumentException("The provided executor must implement IExtractExecutor, IProcessExecutor, and IAArgumentsExecutor interfaces.");
            }

            GenerateExecutor = executor;
            ExtractExecutor = (IExtractExecutor)executor;
            ProcessExecutor = (IProcessExecutor)executor;
            ArgumentsExecutor = (IArgumentsExecutor)executor;
        }

        public AIExecutorContext(IGenerateExecutor generateExecutor, IExtractExecutor extractExecutor, IProcessExecutor processExecutor, IArgumentsExecutor argumentsExecutor)
        {
            GenerateExecutor = generateExecutor
                ?? throw new ArgumentNullException(nameof(generateExecutor));
            ExtractExecutor = extractExecutor
                ?? throw new ArgumentNullException(nameof(extractExecutor));
            ProcessExecutor = processExecutor
                ?? throw new ArgumentNullException(nameof(processExecutor));
            ArgumentsExecutor = argumentsExecutor
                ?? throw new ArgumentNullException(nameof(argumentsExecutor));
        }
    }
}
