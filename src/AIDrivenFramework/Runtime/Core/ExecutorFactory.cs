using AIDrivenFW.API;

namespace AIDrivenFW.Core
{
    public static class ExecutorFactory
    {
        public static IAIExecutor CreateDefault()
        {
            return new LlamaProcessExecutor();
        }
    }
}
