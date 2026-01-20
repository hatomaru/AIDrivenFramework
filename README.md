# AIDrivenFramework

A setup and execution framework for safely working with local LLMs in Unity, with careful consideration for UX and licensing.

<img src="https://github.com/hatomaru/AIDrivenFramework/blob/main/AISetup/AssetResources/Frame/Bg.png" width="800">

[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)

[日本語版READMEはこちら](README_ja.md)

## Overview

**AIDrivenFramework** is an experimental framework for safely integrating local LLMs (e.g., llama.cpp) into Unity projects.

### Features

* Interact with llama.cpp using a single line of code
* Automatically starts and initializes the process if it is not running
* Automatically loads the model if it is not loaded
* Filters out standard output noise and returns only the generated results
* Designed to avoid redistribution of models
* Seamlessly integrates with Unity

## Basic Usage

> [!IMPORTANT]
>
> ### Prerequisites
>
> **AIDrivenSetup** must be completed
> (Models have been downloaded, placed, and verified)

Minimal code example

```
using AIDrivn;

string response = await GenAI.Generate(
    "Hello",
    ct: cancellationToken
);

Debug.Log("Response: " + response);
```

With just this, the following processes are handled **automatically** internally:

* Starting the LLM execution process (if not running)
* Loading the model (if not loaded)
* Sending the generation command
* Formatting and extracting the output
* Returning the generated result

> [!CAUTION]
>
> ## Notes
>
> This framework is **experimental**
> APIs and structure may change in the future
> Commercial use depends on the license of the LLM runtime environment and model you use

## Required Packages

This framework depends on the following packages:

* UniTask (asynchronous processing)
* LitMotion (UI / animation control)
* StandaloneFileBrowser (file selection UI during AI setup)

Please install them via the Unity Package Manager.

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

AIDrivenFramework is designed not as a **“library”**, but as a **“framework that guarantees a safe experience.”**

### Why hide process management

When running LLMs, the following mistakes commonly occur:

* Sending input while the process is not running
* Generating output without loading a model
* Being unable to stop execution due to unclear runtime state

AIDrivenFramework prioritizes preventing these **ordering mistakes** at the API level.

## About Models

> [!CAUTION]
> This framework does not bundle or distribute LLM model files
> Users must obtain models from official sources themselves
> This repository only provides guidance on how to obtain them

This design balances respect for non-redistributable models with user-managed control.

## About Config and Args

AIDrivenFramework handles configuration in two layers.

### Config (managed by the framework)

* Model path
* Automatic startup / shutdown
* Safe execution policies

### Args (for advanced users)

* `config.Args = "--ctx-size 2048 --n-gpu-layers 32 --temp 0.7";`

When more detailed control is required, execution arguments for the LLM can be specified as a string.

## Target Users

* Those who want to try using LLMs in Unity
* Those interested in local LLMs but unsure about setup
* Those who want to experiment with LLM × games / interactive experiences
* Those looking for experimental OSS

## Development Status

✔ Process separation
✔ Automatic startup and generation flow
✔ Namespace organization
⏳ TestMode / ExampleScene (planned)

### Message from the Author

AIDrivenFramework was created from the motivation to “build a safe entry point to interact with LLMs” before “embedding LLMs into games.”
Issues and PRs are welcome, including bug reports, improvement ideas, and differing design philosophies.
