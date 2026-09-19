using System;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// AI生成バックエンドの実行契約。
    /// </summary>
    public class AIProcessCoordinator
    {
        public readonly IProcessExecutor ProcessExecutor;
        public readonly IAArgumentsExecutor ArgumentsExecutor;
        public readonly IGenerateExecutor GenerateExecutor;
        public readonly IExtractExecutor ExtractExecutor;

        public AIProcessCoordinator(IGenerateExecutor executor)
        {
            if(executor is not IExtractExecutor || executor is not IProcessExecutor || executor is not IAArgumentsExecutor)
            {
                throw new ArgumentException("The provided executor must implement IExtractExecutor, IProcessExecutor, and IAArgumentsExecutor interfaces.");
            }

            GenerateExecutor = executor;
            ExtractExecutor = (IExtractExecutor)executor;
            ProcessExecutor = (IProcessExecutor)executor;
            ArgumentsExecutor = (IAArgumentsExecutor)executor;
        }

        public AIProcessCoordinator(IGenerateExecutor generateExecutor, IExtractExecutor extractExecutor, IProcessExecutor processExecutor, IAArgumentsExecutor argumentsExecutor)
        {
            GenerateExecutor = generateExecutor;
            ExtractExecutor = extractExecutor;
            ProcessExecutor = processExecutor;
            ArgumentsExecutor = argumentsExecutor;
        }
    }
}
