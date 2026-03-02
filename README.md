# AIDrivenFramework 🚀  
**Unity × Local LLM Safe Framework**

A setup & execution framework for safely integrating local LLMs into Unity.

<img src="https://github.com/hatomaru/AIDrivenFramework/blob/main/Banner.png" width="800">

[![License](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)  
[![Discord](https://img.shields.io/badge/Discord-CommunityGuild-5865F2?logo=discord&logoColor=white)](https://discord.gg/dfzwqCHSW2)

🎥 [Introduction Video](https://www.youtube.com/watch?v=FkSMRITf-4Q)  
🇯🇵 [日本語READMEはこちら](README_ja.md)

---

## ✨ Main Features
- 🛡 Safe-by-Design Architecture  
Prevents invalid states such as generation before model readiness.
- 🚀 Zero-Stress Initialization  
Automatically prepares the LLM environment when entering Play Mode.
- 🧙 Built-in Setup Wizard (No Ollama Required)  
Download-based setup — no external LLM installers needed.
- 🔍 Automatic Environment Detection  
Detects missing configuration and guides users through setup.
- 🖥 GUI-Based Model & Binary Selection  
Select `llama.cpp` and `.gguf` files via an intuitive interface.
- 🔄 Intelligent Process Orchestration  
Manages process lifecycle and model loading seamlessly.
- 🧹 Clean Response Extraction  
Filters CLI artifacts and returns pure assistant output only.
- 🔁 Modular Executor System  
Swap execution backends (CLI / HTTP / Custom) effortlessly.
- 🎮 Built for Unity  
Optimized for Play Mode, runtime builds, and game workflows.

Unity interacts only with a minimal, clean API.

---

## ⚡ Quick Start

### 1️⃣ Install

Add via Unity Package Manager:

```
https://github.com/hatomaru/AIDrivenFramework.git?path=src/AIDrivenFramework
```

---

### 2️⃣ Prepare Local LLM

Download separately:

- `llama.cpp`
- `.gguf` model file

(Not included in this repository)

> [!TIP]
> Use pre-built llama.cpp binaries from https://github.com/ggerganov/llama.cpp/releases  
> Recommended starting model: [https://huggingface.co/bartowski/Llama-3.1-8B-Instruct-GGUF (Q4_K_M)](https://huggingface.co/elyza/Llama-3-ELYZA-JP-8B-GGUF)
---

### 3️⃣ Initialize

```csharp
await AIDrivenInitializer.Initialize();
```

If the environment is not prepared:

- The setup scene opens automatically  
- Requires optional **AISetup** component  

---

### 4️⃣ Generate

```csharp
var genAI = new GenAI();
var result = await genAI.Generate("Hello AI");
Debug.Log(result);
```

You're ready. 🎉

---
##🧙 Setup Wizard (AISetup Component)

The optional **AISetup** package provides a user-friendly wizard:

- Automatic detection of missing setup
- GUI for selecting llama.cpp binary and model file
> [!TIP]
> One-click installation of sample scenes (optional)
> Menu access: AIDrivenFramework > Setup (manual open)

Install via the Setup window or Unity Package Manager (optional dependency).

> This makes first-time setup much easier — especially for beginners!


## 🎯 Minimal Public API (V1)

```csharp
GenAI.Generate(string input, GenAIConfig config = null);
AIDrivenInitializer.Initialize();
GenAIConfig;
```

Everything else is internal.

---

## 🧠 Why AIDrivenFramework?

Unlike simple wrappers, this framework:

- Prevents generation before model load  
- Prevents missing process startup  
- Guarantees execution safety at API level  
- Supports Executor replacement  

Designed for **long-term Unity × AI architecture**, not just quick calls.

---

## 🔁 Executor Replacement (Advanced)

You can replace the execution layer:

```csharp
GenAI.SetExecutor(customExecutor);
```

Default implementation:

```
LlamaProcessExecutor
```

This allows HTTP executors or custom process handlers.

---

## 📦 Installation Options

### ✅ OpenUPM (Recommended)

```bash
openupm add com.hatomaru.ai.framework
```

---

### 🔧 Required Dependencies

The following packages are required:

- [UniTask](https://github.com/Cysharp/UniTask) (Asynchronous processing)
- [LitMotion](https://github.com/AnnulusGames/LitMotion/blob/main/README_JA.md) (UI / animation control) 

Optional:

- - [UnityStandaloneFileBrowser](https://github.com/gkngkc/UnityStandaloneFileBrowser)   (for AISetup UI)

---

## 🖥 System Requirements

- Unity 2022.3 LTS or later  
- Windows 10/11 (64bit)  
- Recommended: 16GB RAM or more / 8GB VRAM or more (depending on the model used)

macOS is currently untested.

---

## 💬 Community

Questions? Experiments? Unity × Local LLM discussion?

👉 Join CommunityGuild  
https://discord.gg/dfzwqCHSW2

---

## ⚖ License

- Framework core: MIT License  
- Models & runtimes: Not included  
- Follow each official distribution license  

---

## 🎮 Target Users

- Unity developers exploring local LLM  
- Developers concerned about execution safety  
- Experimental AI × Game creators  
- OSS contributors  

---

## ✍ From the Author

AIDrivenFramework was created to provide  
a **safe entry point for local LLM integration in Unity**.

Issues, PRs, and feedback are welcome.# AIDrivenFramework 🚀  
**Unity × Local LLM Safe Framework**
