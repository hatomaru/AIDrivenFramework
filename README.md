# AIDrivenFramework  
A setup & execution framework for safely handling local LLMs in Unity with consideration for UX and licensing  
<img src="https://github.com/hatomaru/AIDrivenFramework/blob/main/Banner.png" width="800">  
[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)
 
## 🚀 Quick Start  
[Installation](#installation)
 
## Table of Contents
 
- [Overview](#overview)
- [V1 Public API](#-v1-public-api)
- [Features](#features)
- [System Requirements](#system-requirements)
- [Installation](#installation)
- [Setup](#setup)
- [Basic Usage](#basic-usage)
- [About Executor](#about-executor-v1)
- [Design Philosophy](#design-philosophy)
- [License](#license)
 
---
 
## Overview
 
**AIDrivenFramework** is a framework for safely integrating local LLMs (e.g., llama.cpp) into Unity projects.
 
This framework internally manages:
 
- Process management  
- Model loading control  
- Output noise filtering  
- Execution order guarantees  
 
It is designed so that **Unity only interacts with a minimal API surface**.
 
---
 
## 🎯 V1 Public API
 
In V1, the public API is intentionally limited to a minimal set.
 
```csharp
GenAI.Generate(string input, GenAIConfig genAIConfig = null)
GenAIConfig
GenAI.IsPrepared()
```
 
All other structures are internal implementations.
 
---
 
## Features
 
- Call a local LLM in a single line  
- Automatically starts the process if not running  
- Automatically loads the model if not loaded  
- Removes CLI noise and returns only the generated result  
- Designed not to redistribute models  
- Executor replacement supported (minimal support in V1)
 
---
 
## System Requirements
 
- Unity 2022.3 LTS or later  
- Windows 10/11 (64bit)  
- Recommended: 16GB+ RAM / 8GB+ VRAM (model dependent)  
 
*MacOS not tested*
 
---
 
## Installation
 
### Package Manager (Git URL)
 
Open Unity’s Package Manager, select `+` → `Add package from git URL...`, and enter:
 
```
https://github.com/hatomaru/AIDrivenFramework.git?path=src/AIDrivenFramework
```
 
---
 
## Dependencies
 
The following packages are required for this framework.  
Please install them via Unity Package Manager.
 
- [UniTask](https://github.com/Cysharp/UniTask) (asynchronous processing)  
- [LitMotion](https://github.com/AnnulusGames/LitMotion/blob/main/README_JA.md) (UI / animation control)  
- [StandaloneFileBrowser](https://github.com/gkngkc/UnityStandaloneFileBrowser) (file selection during setup)
 
---
 
## Setup
 
### 1. Prepare the LLM
 
This framework does NOT include:
 
- llama.cpp  
- .gguf models  
 
Please obtain them individually from official distribution sources such as Hugging Face.
 
---
 
### 2. Run the Setup Window
 
Open any scene in the Unity Editor and press **Play**.
 
The framework automatically checks whether the local LLM environment is configured.  
If it is not configured, the setup window (AIDrivenSetup) will open.
 
Select your downloaded `llama.cpp` and `.gguf` model files and follow the instructions.
 
This also works in builds, so end users can configure it in the same way.
 
---
 
## Optional
 
### 1. Open Setup Window from Menu
 
Unity Menu:
 
```
AIDrivenFramework > Setup
```
 
In the Setup window, you can optionally select:
 
- AISetup (AI setup utility)  
- Example Scene (sample scene)  
 
Check the required components and press **“Install Selected”**.
 
*An Import dialog will appear. Please review the contents before confirming the import.*
 
It is recommended to check both **AISetup** and **Example Scene** initially.
 
---
 
### 2. Completion
 
When the import finishes, “Setup Complete!” will appear at the bottom of the window.
 
---
 
## Basic Usage
 
> [!IMPORTANT]
> ### Prerequisite
> **The above setup must be completed**
> (Model downloaded, placed, and verified)
 
```csharp
using AIDrivenFW.API;
using Cysharp.Threading.Tasks;
using UnityEngine;
 
public class AIDriven_SmallCode : MonoBehaviour
{
    GenAI genAI = new GenAI();
 
    void Start()
    {
        TestCode().Forget();
    }
 
    async UniTask TestCode()
    {
        // Set the default AI executor
        //genAI.SetExecutor(ExecutorFactory.CreateDefault());
 
        string response = await genAI.Generate(
            "Hello",
            ct: destroyCancellationToken
        );
 
        Debug.Log("Response: " + response);
    }
}
```
 
Internally, the framework automatically performs:
 
- Process startup  
- Model loading  
- Input transmission  
- Output extraction  
- Result return  
 
---
 
## About Executor (V1)
 
By default, `LlamaProcessExecutor` is used.
 
By preparing your own Executor, you can replace the process communication layer.
 
```csharp
GenAI.SetExecutor(customExecutor);
```
 
---
 
## IsPrepared()
 
You can check whether the local LLM environment is available.
 
```csharp
bool ready = GenAI.IsPrepared();
```
 
---
 
## About Config
 
In AIDrivenFramework, configuration is handled as follows.
 
### Configurable Parameters
 
- Model path  
- Args (for advanced users)  
 
Example:
 
```csharp
config.Args = "--ctx-size 2048 --n-gpu-layers 32 --temp 0.7";
```
 
If detailed control is required, you can specify LLM execution arguments as a string.
 
---
 
## Executor Replacement Example (Advanced)
 
> [!NOTE]
> This section is intended for users who want to implement HTTP communication or separate process management themselves.
> No changes are required for basic usage.
 
In AIDrivenFramework, you can replace the LLM communication layer by implementing `IAIExecutor`.
 
---
 
### 1. Implement IAIExecutor
 
```csharp
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
 
public class CustomExecutor : IAIExecutor
{
    private AIProcess aiProcess;
    const int checkIntervalMs = 500;
    string AISoftwarePath = "";
 
    public CustomExecutor()
    {
        AISoftwarePath = Path.Combine(
            UnityEngine.Application.persistentDataPath,
            AIDrivenConfig.baseFilePath,
            "mock-cli.exe"
        );
    }
 
    public async UniTask StartProcessAsync(CancellationToken ct, GenAIConfig config = null)
    {
        if (config == null) config = new GenAIConfig();
 
        config.aiSoftwarePath = AISoftwarePath;
        aiProcess = new AIProcess(config);
 
        await UniTask.WaitUntil(
            () => aiProcess.IsProcessAlive(),
            cancellationToken: ct
        );
 
        await WaitUntilReadyAsync(ct);
    }
 
    public async UniTask WaitUntilReadyAsync(CancellationToken ct)
    {
        await WaitModelLoadAsync(ct);
    }
 
    private async UniTask WaitModelLoadAsync(CancellationToken ct)
    {
        int timeoutMs = 120000;
        int elapsedMs = 0;
 
        while (elapsedMs < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
 
            string output = await ReceiveAsync(ct);
 
            if (output.Contains("available commands:"))
                return;
 
            await UniTask.Delay(checkIntervalMs, cancellationToken: ct);
            elapsedMs += checkIntervalMs;
        }
 
        throw new TimeoutException("Model loading timed out");
    }
 
    public async UniTask GenerateAsync(string input, CancellationToken ct)
    {
        aiProcess.ClearOutputBuffer();
        aiProcess.SendStdin(input);
 
        while (!await CheckOutput(ct))
        {
            await UniTask.Delay(checkIntervalMs, cancellationToken: ct);
        }
    }
 
    public UniTask<string> ReceiveAsync(CancellationToken ct)
    {
        return UniTask.FromResult("mock response");
    }
 
    public async UniTask<bool> CheckOutput(CancellationToken token)
    {
        string output = await ReceiveAsync(token);
        return true;
    }
 
    public bool IsProcessAlive()
    {
        return aiProcess != null && aiProcess.IsProcessAlive();
    }
 
    public void KillProcess()
    {
        aiProcess?.KillProcess();
    }
 
    public string IsFoundAISoftware()
    {
        return File.Exists(AISoftwarePath) ? AISoftwarePath : "null";
    }
 
    public string IsFoundModelFile()
    {
        string modelPath = ModelRepository.GetModelExecutablePath();
        return modelPath != "null" ? modelPath : "null";
    }
 
    public string ExtractAssistantOutput(string raw)
    {
        return raw;
    }
}
```
 
---
 
### 2. Inject into GenAI
 
```csharp
using AIDrivenFW.API;
 
GenAI genAI = new GenAI(new CustomExecutor());
string result = await genAI.Generate("Hello");
```
 
---
 
### Default Implementation Example
 
The framework includes `LlamaProcessExecutor`.
 
```csharp
GenAI genAI = new GenAI(new LlamaProcessExecutor());
```
 
This Executor internally manages:
 
- Launching `llama-cli.exe`  
- Waiting for model loading  
- Monitoring stdout  
- Removing CLI noise  
- Detecting generation completion via markers  
 
---
 
## Design Philosophy
 
AIDrivenFramework is a framework designed to  
**“guarantee a safe experience when handling LLMs.”**
 
Common issues during LLM integration such as:
 
- Forgetting to start the process  
- Generating before the model is loaded  
- Not knowing the execution state  
 
are prevented at the API level.
 
---
 
## About Models
 
> [!CAUTION]
> Models and LLM runtime environments are NOT included.
> Please follow the licenses of each official distribution source.
 
---
 
## License
 
- Framework core (AIDrivenFramework): MIT License  
- Rounded M+ Fonts: M+ FONTS LICENSE  
- Required packages (UniTask / LitMotion etc.): Follow each package’s license  
 
This repository does NOT include model files or LLM runtime environments.
 
---
 
## Target Users
 
- Those who want to use LLMs in Unity  
- Those interested in local LLMs but unsure about setup  
- Those who want to experiment with LLM × games / interactive expression  
- Those looking for experimental OSS  
 
---
 
## From the Author
 
AIDrivenFramework was designed to  
“create a safe entry point before integrating LLMs.”
 
Bug reports, improvement suggestions, and philosophical differences are welcome via Issue / PR.
