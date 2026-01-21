# AIDrivenFramework

A setup and execution framework for safely working with local LLMs in Unity, with careful consideration for UX and licensing. <img src="https://github.com/hatomaru/AIDrivenFramework/blob/main/Banner.png" width="800">

[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)

[日本語版READMEはこちら](README_ja.md)

## Overview

**AIDrivenFramework** is an **experimental framework** for safely integrating local LLMs (e.g., llama.cpp) into Unity projects.

### Features

* Interact with llama.cpp using a single line of code
* Automatically starts and initializes the process if it is not running
* Automatically loads the model if it is not loaded
* Filters out standard output noise and returns only the generated results
* Designed to avoid redistribution of models
* Seamlessly integrates with Unity

## Runtime Environment

* **Unity 2022.3 LTS** or later recommended
* **OS**: Windows 10/11 (64-bit)
  *macOS has not been tested*
* **Recommended specs**: VRAM 8GB or more / RAM 16GB or more (depends on the model used)

## Installation

### Package Manager (Git URL)

Open Unity’s Package Manager, select `Add package from git URL...` from the `+` button, and enter the following:

```text
https://github.com/hatomaru/AIDrivenFramework.git?path=/Assets/AIDrivenFramework
```

## Required Packages

The following packages are required to use this framework. Please add them to your project via the Unity Package Manager or other means.

* [UniTask](https://github.com/Cysharp/UniTask) (asynchronous processing)
* [LitMotion](https://github.com/AnnulusGames/LitMotion/blob/main/README_JA.md) (UI / animation control)
* [StandaloneFileBrowser](https://github.com/gkngkc/UnityStandaloneFileBrowser) (file selection during setup)

## Setup

After installation, perform the initial setup using the steps below.

**1. Prepare the model**

Please download a `.gguf` format model file and Llama.cpp yourself from sources such as Hugging Face.
(This framework does not include model files or Llama.cpp.)

**2. Run the setup wizard**

Open any scene in the Unity Editor and run it.

The framework will automatically detect whether the local LLM environment is set up.
If not, the setup screen (AIDrivenSetup) will open.

Select the downloaded Llama.cpp and `.gguf` model file, and complete the initial configuration.
Once finished, the environment will be ready for use.

This setup also works in builds, allowing end users to configure it in the same way.

## Basic Usage

> [!IMPORTANT]
>
> ### Prerequisite
>
> The **above setup** must be completed
> (models have been downloaded, placed, and verified)

Minimal example for calling the AI:

```
using AIDrivenFW;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class AIDriven_SmallCode : MonoBehaviour
{
    void Start()
    {
        TestCode().Forget();
    }

    async UniTask TestCode()
    {
        string response = await GenAI.Generate(
            "Hello",
            ct: destroyCancellationToken
        );

        Debug.Log("Response: " + response);
    }
}
```

With this alone, the following processes are handled **automatically** internally:

* Starting the LLM execution process (if not running)
* Loading the model (if not loaded)
* Sending the generation command
* Formatting and extracting the output
* Returning the generated result

> [!CAUTION]
>
> ### Notes
>
> This framework is experimental.
>
> APIs and structure may change in the future.
>
> Whether commercial use is permitted depends on the license of the LLM runtime environment and model you use.

## License

The licenses for each component included in this repository are as follows:

* Framework core (AIDrivenFramework): MIT License
* Rounded M+ Fonts: M+ FONTS LICENSE
* Prerequisite packages (UniTask / LitMotion, etc.): subject to each package’s license

※ This repository does not include any LLM model files.

Modifications, redistribution, and forks of the framework are welcome.
Licenses for model files and LLM runtime environments are not included.

## About Fonts

This framework includes M+ FONTS.

M+ FONTS are free software distributed by the M+ FONTS PROJECT,
and commercial use, redistribution, and modification are permitted (without warranty).

For detailed license terms, please refer to LICENSE_J.txt and LICENSE_E.txt included in the font directory.

## Design Philosophy

AIDrivenFramework is designed not as a “library,” but as a **framework that guarantees a safe experience**.

### Why hide process management

When running LLMs, the following mistakes commonly occur:

* Sending input while the process is not running
* Generating output without loading a model
* Being unable to stop execution due to unclear runtime state

AIDrivenFramework prioritizes preventing these **ordering mistakes** at the API level.

## About Models

> [!CAUTION]
> This framework does not bundle or distribute LLM model files or Llama.cpp.
>
> Users must obtain models from official sources themselves.
>
> This repository only provides guidance on how to obtain them.

## About Config

AIDrivenFramework handles configuration as follows.

### Configurable items

* Model path
* Automatic startup / shutdown
* Args (for advanced users)
* `config.Args = "--ctx-size 2048 --n-gpu-layers 32 --temp 0.7";`

When more detailed control is required, execution arguments for the LLM can be specified as a string.

## Target Users

* Those who want to try using LLMs in Unity
* Those interested in local LLMs but unsure about setup
* Those who want to experiment with LLM × games / interactive experiences
* Those looking for experimental OSS

### Message from the Author

AIDrivenFramework was created from the motivation to “build a safe entry point to interact with LLMs” before “embedding LLMs into games.”

Issues and PRs are welcome, including bug reports, improvement ideas, and differing design philosophies.
