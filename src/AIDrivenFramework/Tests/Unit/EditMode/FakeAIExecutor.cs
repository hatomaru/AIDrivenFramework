using AIDrivenFW.Config;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;

namespace AIDrivenFW.Tests.Unit
{
    internal sealed class FakeAIExecutor : IAIExecutor
    {
        public FakeAIExecutor(string id, string response)
        {
            Id = id;
            Response = response;
        }

        public string Id { get; }
        public string Response { get; set; }
        public Exception CleanupException { get; set; }
        public int GenerateCallCount { get; private set; }
        public int KillProcessCallCount { get; private set; }
        public string LastSystemInput { get; private set; }
        public string LastInput { get; private set; }
        public bool ProcessAlive { get; private set; } = true;

        public UniTask StartProcessAsync(CancellationToken ct, GenAIConfig genAIConfig = null, IProgress<float> progress = null, int timeoutMs = 120000)
        {
            ct.ThrowIfCancellationRequested();
            ProcessAlive = true;
            return UniTask.CompletedTask;
        }

        public UniTask WaitUntilReadyAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }

        public UniTask GenerateAsync(string sysInput, string input, CancellationToken ct, Action<string> onUpdate = null, IProgress<float> progress = null, int timeoutMs = 120000)
        {
            ct.ThrowIfCancellationRequested();
            GenerateCallCount++;
            LastSystemInput = sysInput;
            LastInput = input;
            onUpdate?.Invoke(Response);
            return UniTask.CompletedTask;
        }

        public UniTask<string> ReceiveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.FromResult(Response);
        }

        public UniTask<bool> CheckOutput(CancellationToken token, Action<string> onUpdate = null)
        {
            token.ThrowIfCancellationRequested();
            return UniTask.FromResult(true);
        }

        public bool IsProcessAlive()
        {
            return ProcessAlive;
        }

        public bool IsDifferentAIConfig(GenAIConfig newAiConfig)
        {
            return false;
        }

        public void KillProcess()
        {
            KillProcessCallCount++;
            if (CleanupException != null)
            {
                throw CleanupException;
            }

            ProcessAlive = false;
        }

        public string SetDefaultArguments()
        {
            return string.Empty;
        }

        public string SetArguments(string raw, GenAIConfig genAIConfig)
        {
            return raw;
        }

        public string IsFoundAISoftware()
        {
            return Id;
        }

        public string IsFoundModelFile()
        {
            return Id;
        }

        public string ExtractAssistantOutput(string raw)
        {
            return raw;
        }
    }
}
