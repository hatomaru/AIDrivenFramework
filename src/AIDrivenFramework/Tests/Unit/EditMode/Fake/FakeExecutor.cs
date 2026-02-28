using AIDrivenFW.Core;
using AIDrivenFW.Config;
using Cysharp.Threading.Tasks;
using System.Threading;
using System;

public class FakeExecutor : IAIExecutor
{
    public bool ProcessAlive = true;
    public bool StartCalled = false;
    public bool KillCalled = false;

    public string ReceiveValue = "FAKE";
    public string ExtractValue = "OK";

    public bool CheckOutputValue = true;

    public bool IsProcessAlive() => ProcessAlive;

    public UniTask StartProcessAsync(CancellationToken ct, GenAIConfig config = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        StartCalled = true;
        ProcessAlive = true;
        return UniTask.CompletedTask;
    }

    public UniTask GenerateAsync(string input, CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
        => UniTask.CompletedTask;

    public UniTask<string> ReceiveAsync(CancellationToken ct)
        => UniTask.FromResult(ReceiveValue);

    public string ExtractAssistantOutput(string raw)
        => ExtractValue;

    public UniTask<bool> CheckOutput(CancellationToken token)
        => UniTask.FromResult(CheckOutputValue);

    public void KillProcess()
        => KillCalled = true;

    public string IsFoundAISoftware() => "fake_path";
    public string IsFoundModelFile() => "fake_model";
    public UniTask WaitUntilReadyAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
        => UniTask.CompletedTask;
}
