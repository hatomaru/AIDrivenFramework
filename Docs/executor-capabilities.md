# Executor capabilities and custom input

Only `IGenerateExecutor` is required. `AIExecutorContext` discovers optional capabilities on
the same object, or accepts separate implementations in its multi-argument constructor.

| Capability | When absent | Responsibility when supplied |
| --- | --- | --- |
| Generation | Constructor rejects null | Observe cancellation, complete all work before returning, produce final output |
| Process | No default process config, startup, receive monitor, restart or kill | Own process startup, readiness, receive and shutdown |
| Arguments | No default argument substitution; process backend receives the ordinary config defaults | Supply process arguments and local software discovery |
| Extraction | Preserve the generated string unchanged | Convert final output; null/blank is a retryable empty response |

Core owns the operation deadline, retry classification and serialization of calls to that core.
Context coordinates the optional capabilities; Core has no backend schema or process-type checks.
Caller-owned config assets are never destroyed. Context-created process defaults are reused until
Core disposal. A `GenAI` exclusively owns its context/backend; do not share an executor or context
between instances. `SetExecutor` and `KillProcess` must be called while generation is idle.
Replacement stops the old process, if present, before releasing defaults and switching references.
A shutdown exception leaves the previous references in place. For process-free backends,
cancellation and per-request cleanup are the generator's responsibility; injected clients remain
caller-owned. `IsFoundAISoftware()` returns an empty string when local discovery is unavailable.

## Custom serialized input

`Generate(string)` still forwards the string to the executor. `GenerateInput(IExecutorInput)`
adds validation and one serialization snapshot before generation starts. Validation/serialization
must be synchronous and bounded; exceptions propagate before any backend call, without retry.
The asynchronous deadline starts in `Generate` after this preparation. A pre-cancelled call does
not validate or serialize. All backend retries reuse the snapshot. No shared config fields or
executor subclass are needed to add an input schema. Serialization chooses the wire format;
the selected executor must understand that format. Built-in llama/Ollama executors retain their
existing prompt wrappers; arbitrary HTTP JSON should use `HttpJsonExecutor`.

```csharp
using AIDrivenFW.API;
using AIDrivenFW.Core;
using UnityEngine;

[System.Serializable]
public sealed class DecisionInput : IExecutorInput
{
    public int turn;
    public string[] choices;
    public void Validate()
    {
        if (turn < 0 || choices == null || choices.Length == 0)
            throw new System.ArgumentException("A valid turn and choices are required.");
    }
    public string Serialize() => JsonUtility.ToJson(this);
}

// The endpoint and response format belong to your service.
var ai = new GenAI(new HttpJsonExecutor("https://your-service.example/decision"));
string json = await ai.GenerateInput(new DecisionInput
{
    turn = 7, choices = new[] { "left", "right" }
});
// Existing serialized JSON can also be passed without parsing or wrapping.
string other = await ai.GenerateInput(new SerializedExecutorInput("{\"turn\":8}"));
```

`HttpJsonExecutor` POSTs the exact input as `application/json`. Include system instructions,
model options, and service-specific fields in that body; a nonempty `GenAIConfig.sysPrompt`
is rejected to avoid silently dropping it. Optionally supply `selectOutput: body => ...` to
convert the response and a caller-owned `HttpClient` for authentication/headers. Without a
selector the complete response body is returned. This executor is nonstreaming: `onUpdate`
receives the completed output once per attempt, and progress reports 100 on completion.
Existing streaming executors retain their chunk behavior. This is generic transport support;
service-specific decision semantics and live service authentication are separate integration work.

## Concurrency and failures

The former static lock is now per Core. Calls on one `GenAI` queue; independent local or remote
backends can run concurrently. A remote decision request therefore cannot block another instance
through a global lock. Sharing one GPU may require caller-side scheduling if memory is limited.
Lock waiting, process preparation and all three attempts share one realtime deadline, including
when `Time.timeScale` is zero. Waiting cancellation/timeout does not stop the active request.

Empty final output and explicitly transient failures retry at most three times. Process backends
restart before retry; process-free backends retry only generation. Configuration, authentication,
validation and implementation errors fail immediately. Cancellation preserves the caller token;
an expired operation deadline throws `TimeoutException`. Cancellation/timeout never retries.
Automatic local setup after exhausted retries is restricted to contexts with both process and
argument capabilities; remote failures do not launch the local setup wizard.

## Test procedure

1. Open a Unity 6000.3.10f1 host project with this package and UniTask installed.
2. Open Window > General > Test Runner > EditMode and run `AIDrivenFW.Tests.Unit`.
3. Confirm `ExecutorCapabilityTests` passes: process-free omitted/explicit config, raw output,
   optional extraction and arguments, input validation/snapshot, cancel/timeout, retry exhaustion,
   independent concurrency, unchanged JSON POST, output selection, and HTTP authentication failure.
4. Confirm `GenAIGenerationContractTests` and `GenAIExecutorIsolationTests` pass, including
   same-Core lock waiting, process monitor completion, retries, replacement and cleanup failures.
5. In your service integration, create a custom input matching the service schema, configure
   endpoint/authentication, call `GenerateInput`, and verify the service received all fields.
   Live service access is not required by the deterministic tests.

Batch equivalent (create the result directory and close that host project first):

```powershell
& 'C:/Unity/6000.3.10f1/Editor/Unity.exe' -batchmode -nographics `
  -projectPath C:/Project/Apps/AIDrivenTest -runTests -testPlatform EditMode `
  -testFilter AIDrivenFW.Tests.Unit `
  -testResults C:/test-results/executor-capabilities.xml `
  -logFile C:/test-results/executor-capabilities.log
```

The two optional live HTTP smoke cases require running local models; leave
`AIDRIVEN_HTTP_SMOKE` unset for a deterministic run. Player builds and live decision services
need separate verification.

## Verification on 2026-10-16

Unity 6000.3.10f1 on Windows, isolated host project containing a byte-identical copy of the
changed runtime/test sources: **95 passed, 0 failed, 2 optional live-model cases skipped**
(97 total, 5.35 seconds). The 13 new cases and existing deterministic suite passed.
`git diff --check` passed. The HTTP JSON test captured the request through an injected handler;
no live decision service, setup UI, or Player build was exercised in this run.
