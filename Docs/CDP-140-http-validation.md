# CDP-140 HTTP validation

The HTTP transport now processes a live body callback inside the request/response lifetime. It owns and disposes the request, response, and body on completion, failure, and cancellation. Shared HttpClient pools have application lifetime; clients injected into tests remain caller-owned.

`SendStreamingAsync` now accepts `Func<Stream, CancellationToken, UniTask>` rather than returning a string. Consume the body completely inside the callback; never retain it. Callers must supply an operation deadline via the cancellation token. Backend parsing remains in the executors: Ollama NDJSON requires `done`, llama SSE requires a blank-line-delimited `[DONE]` event. Empty output and premature EOF are retryable failures. Callback exceptions propagate unchanged. HTTP 401/404 fail immediately; 408/429/5xx retain the existing retry classification. Retry count and process restart policy remain owned by GenAICore.

## Automated tests

Open the host Unity project (6000.3.10f1), then Window > General > Test Runner > EditMode. Run `AIDrivenFW.Tests.Unit`. `HttpStreamingContractTests` covers streamed chunks/callbacks, nonstream parsing, empty/malformed output, missing completion markers, HTTP failure classification, connection failure, pending headers, pending body/line cancellation, and body disposal on success/failure/cancellation. Existing generation tests cover retry limits and realtime deadlines with Time.timeScale=0.

Command-line equivalent (adjust paths):

```powershell
& 'C:/Unity/6000.3.10f1/Editor/Unity.exe' -batchmode -nographics `
  -projectPath C:/Project/Apps/AIDrivenTest -runTests -testPlatform EditMode `
  -testFilter AIDrivenFW.Tests.Unit `
  -testResults C:/test-results/cdp140.xml -logFile C:/test-results/cdp140.log
```

Create the result directory first. Normal test runs skip the two live-backend cases.

## Live backend generation

1. Start Ollama on 127.0.0.1:11434 with `gemma3:1b` installed.
2. Start llama-server on 127.0.0.1:18080 with a compatible GGUF model. For this verification, the existing `llama-3-elyza-jp-8b-q4_k_m (2).gguf` was used, with `--ctx-size 2048 --n-gpu-layers 0 --parallel 1`.
3. Set `$env:AIDRIVEN_HTTP_SMOKE='1'` before launching Unity with the command above. To run only the live cases, use `-testFilter AIDrivenFW.Tests.Unit.HttpStreamingContractTests.LiveBackend_StreamAndNonstream`.
4. Confirm both cases pass, and check the `HTTP_SMOKE` lines. Each case sends streaming and nonstreaming requests through the production HttpApiClient and executor parsers, and asserts concatenated callbacks equal the streamed output.
5. Stop the llama-server instance started for this test. The live tests do not change saved model settings or start/stop existing backend processes.

These live tests verify HTTP generation and backend parsing, not the setup wizard, process launch configuration, or player builds.

## Verification on 2026-10-14

Unity 6000.3.10f1 on Windows. Live verification used Ollama gemma3:1b and llama-server with the ELYZA 8B GGUF above. Final full run: **84 passed, 0 failed, 0 skipped**, including both live cases (6.76 seconds). `git diff --check` passed. `dotnet build AIDrivenFW.Unit.Tests.csproj --no-restore --verbosity quiet` succeeded; regenerated Unity dependency projects emit package/analyzer warnings.

Final live output:

```text
HTTP_SMOKE ollama stream=Hi! Nice chatting with you. 😊 nonstream=Hello!
HTTP_SMOKE llama stream=Hello! nonstream=Hello!
```

Host paths for full local artifacts: `C:/Users/08har/.codex/tmp/cdp140/final.xml` and `final.log`. Logs are local execution evidence; this document preserves a compact reproducible record.
