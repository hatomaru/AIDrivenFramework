# AIDrivenFramework
UnityでローカルLLMをUXや権利に配慮し、安心して扱うためのセットアップ＆実行フレームワーク
<img src="https://github.com/hatomaru/AIDrivenFramework/blob/main/Banner.png" width="800">
 
[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)
[![Discord](https://img.shields.io/badge/Discord-CommunityGuild-5865F2?logo=discord&logoColor=white)](https://discord.gg/dfzwqCHSW2)

> [!NOTE]
> フレームワークは最近、よりクリーンなAPIと改良されたExecutorアーキテクチャへと進化しました。

[紹介動画](https://www.youtube.com/watch?v=_Foj7tXq_Ss)

## 🚀 クイックスタート  

### 1. インストール（Git URL）

Unity の Package Manager から以下の URL を追加してください。

```
https://github.com/hatomaru/AIDrivenFramework.git?path=src/AIDrivenFramework
```

> [!INFO]
> 依存パッケージの詳細については、下部の [インストール](#インストール) セクションをご確認ください。

---

### 2. ローカルLLMの準備

以下を別途ダウンロードしてください。

- `llama.cpp`
- `.gguf` モデルファイル

これらは本フレームワークには含まれていません。

---

### 3. 初期化

ゲームコード内から `Initialize` を呼び出します。

```csharp
await AIDrivenInitializer.Initialize();
```

環境が未設定の場合：

- セットアップシーン（`AIDrivenSetup`）が自動で表示されます  
- 任意コンポーネント **AISetup** の導入が必要です  

---

### 4. テキスト生成

```csharp
var genAI = new GenAI();
var result = await genAI.Generate("Hello AI");
Debug.Log(result);
```

これで、Unity プロジェクトに安全なローカルLLM統合が可能になります。

## 目次
- [概要](#概要)
- [V1 公開API](#-v1-公開api)
- [特徴](#特徴)
- [動作環境](#動作環境)
- [インストール](#インストール)
- [セットアップ](#セットアップ)
- [基本的な使い方](#基本的な使い方)
- [Executorについて](#executorについてv1)
- [設計思想](#設計思想)
- [ライセンス](#ライセンス)
---
## 概要
 
**AIDrivenFramework** は、Unityプロジェクト上で  
ローカルLLM（例：llama.cpp）を安全に統合するためのフレームワークです。
 
本フレームワークは、
 
- プロセス管理
- モデルロード制御
- 出力ノイズ吸収
- 実行順序の保証
 
を内部で管理し、  
**Unity側からは最小APIのみで扱える設計**になっています。

---
## 💬 Community & Support

AIDrivenFW に関する質問やフィードバックは、
CommunityGuild で受け付けています。

実験の共有や、Unity × Local LLM 開発についてのディスカッションも歓迎です。

👉 [CommunityGuild に参加する](https://discord.gg/dfzwqCHSW2)

---

## 🎯 V1 公開API
 
V1では、公開APIを最小構成に制限しています。
 
```csharp
GenAI.Generate(string input, GenAIConfig genAIConfig = null)
GenAIConfig
AIDrivenInitializer.Initialize();
```
 
それ以外の構造は内部実装です。
 
---
 
## 特徴
 
- 1行でローカルLLMを呼び出せる
- 未起動時は自動でプロセス起動
- 未ロード時は自動でモデルロード
- CLIノイズを除去し生成結果のみ返却
- モデル再配布を行わない設計
- Executor差し替え可能（V1では最小限サポート）
 
---
 
## 動作環境
 
- Unity 2022.3 LTS 以上
- Windows 10/11 (64bit)
- 推奨: RAM 16GB以上 / VRAM 8GB以上（使用モデルに依存）
 
※ macOS は現時点では未検証です。
 
---

## インストール

AIDrivenFramework は OpenUPM（推奨）または  
Unity Package Manager の Git URL 経由でインストールできます。

---

### 方法1：OpenUPM（推奨）

まず OpenUPM CLI をインストールします：
```bash
npm install -g openupm-cli
```
Unity プロジェクトへ追加：
```bash
openupm add com.hatomaru.ai.framework
```
または、manifest.json に OpenUPM レジストリを追加してください：
```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.hatomaru"
      ]
    }
  ],
  "dependencies": {
    "com.hatomaru.ai.framework": "2.1.2" // 最新版はPackage Managerで確認してください
  }
}
```

### 方法2：Unity Package Manager（Git URL）

 Unityの Package Manager を開き、`+` ボタン内の`Add package from git URL...` を選択して以下を入力してください：
```
https://github.com/hatomaru/AIDrivenFramework.git?path=src/AIDrivenFramework
```

### 📦 必須依存パッケージ

コア機能の利用には以下のパッケージが必要です：

- [UniTask](https://github.com/Cysharp/UniTask) (非同期処理)

- [LitMotion](https://github.com/AnnulusGames/LitMotion/blob/main/README_JA.md) (UI / 演出制御)

自動解決されない場合は manifest.json に追加してください：
```json
{
  "dependencies": {
    "com.cysharp.unitask": "2.5.10",
    "com.annulusgames.lit-motion": "2.0.1"
  }
}
```

### オプション依存（AISetup使用時のみ）

AISetup ウィンドウ（ファイル選択UI）を使用する場合のみ、  
以下の追加パッケージが必要です。

- **[UnityStandaloneFileBrowser](https://github.com/gkngkc/UnityStandaloneFileBrowser)**  
  （セットアップ時のファイル選択ダイアログ用）

> [!NOTE]  
> このパッケージは自動で解決されません。  
> AISetupを導入する場合は、以下の方法で手動追加してください：
> 
> - Releasesから .unitypackage をダウンロードしてインポート

> [!IMPORTANT]  
> StandaloneFileBrowser は AISetup ウィンドウ使用時のみ必要です。  
> コアの生成機能（GenAI.Generate() など）には依存しません。

---
 
# セットアップ

## 1. LLMの準備

本フレームワークには以下は同梱されていません。

- `llama.cpp`
- `.gguf` モデルファイル

各自で Hugging Face 等の公式配布元から取得してください。

---

## 2. 初期化の実行（推奨）

任意のクラスから、以下のコードを呼び出してください。

### 呼び出し例

```csharp
// 例: MonoBehaviour の Start() などで一度だけ呼び出す
await AIDrivenInitializer.Initialize();
```

このメソッドは以下を自動で行います。

1. ローカルLLM環境が準備済みか確認
2. 未セットアップの場合、 `AIDrivenSetup` シーンを表示（AISetupコンポーネント導入時のみ）  

> [!IMPORTANT]
> `AIDrivenSetup` シーンを使用するには、任意コンポーネント **AISetup** の導入が必要です。  
> AISetup がインストールされていない場合、自動セットアップは利用できません。
> 詳細はインストールセクションのオプション依存を参照してください。

セットアップ画面では、ダウンロードした `llama.cpp` 実行ファイルおよび `.gguf` モデルファイルを指定してください。

指示に従って設定を完了させると、環境が有効化されます。

> [!TIP]
> 本処理はエディタ上だけでなく、ビルド後の実行環境でも動作します。  
> エンドユーザーも同様の手順で初期設定を行うことが可能です。

---

# オプション

## Setupウィンドウを手動で開く

Unityメニューから直接セットアップウィンドウを開くこともできます。

```
AIDrivenFramework > Setup
```

---

## 任意コンポーネントのインストール

Setupウィンドウでは、以下のコンポーネントを選択できます。

- **AISetup**（セットアップ機能 + AIDrivenSetupシーン）
- **Example Scene**（サンプルシーン）

必要な項目にチェックを入れ、  
**「Install Selected」** を押してください。

Importダイアログが表示されますので、内容を確認のうえ Import を実行してください。

> [!TIP]
> 初回利用時は **AISetup の導入を強く推奨** します。

---

## Setup Complete! が表示されれば完了

インポート完了後、ウィンドウ下部に **「Setup Complete!」** と表示されます。

インポートが完了すると、ウインドウ下部に「Setup Complete!」と表示されます。
 
---

## 基本的な使い方
> [!IMPORTANT]
> ### 前提
> **上記のセットアップ** が完了していること
> (モデルの取得・配置・確認が済んでいる状態)

```csharp
using AIDrivenFW.API;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class AIDriven_SmallCode : MonoBehaviour
{
    GenAI genAI;

    void Start()
    {
        TestCode().Forget();
    }

    async UniTask TestCode()
    {
        // Set the default AI executor
        genAI = new GenAI();

        string response = await genAI.Generate(
            "Hello",
            ct: destroyCancellationToken
        );

        Debug.Log("Response: " + response);
    }
}
```
 
内部では以下を自動実行します：
 
- プロセス起動
- モデルロード
- 入力送信
- 出力抽出
- 結果返却
 
---
 
## Executorについて（V1）
 
デフォルトでは `LlamaProcessExecutor` が使用されます。
 
Executorを自前で用意することで、プロセスとの通信部分の差し替えは可能です。
 
```csharp
GenAI.SetExecutor(customExecutor);
```
 
---
 
## IsPrepared()
 
ローカルLLM環境が利用可能かを確認します。
未セットアップの場合は、ユーザーが安全に利用開始できるよう、セットアップシーンへ遷移します。

> [!NOTE]
> 本機能を利用するには 任意コンポーネント **AISetup** の導入が必要です。
 
```csharp
await AIDrivenInitializer.Initialize();
```
 
---
 
## Configについて
AIDrivenFrameworkでは、設定を以下のように扱います。
### Configで設定できるもの
- モデルパス
- Args（上級者向け）
- (例) config.Args = "--ctx-size 2048 --n-gpu-layers 32 --temp 0.7";
詳細な制御が必要な場合にLLM 実行時の引数を文字列で指定できます。

## Executor差し替え例（上級者向け）
> [!NOTE]
> このセクションは、HTTP通信や別プロセス管理を自前で実装したい方向けです。  
> 基本利用では変更不要です。

AIDrivenFrameworkでは、`IAIExecutor` を実装することで  
LLM通信部分を差し替えることができます。

 
---
 
### 1.IAIExecutor を実装する
 
#### カスタムExecutorの例

```csharp
using AIDrivenFW.Config;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
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

    public async UniTask StartProcessAsync(CancellationToken ct, GenAIConfig genAIConfig = null, IProgress<float> progress = null, int timeoutMs = 120000)
    {
        if (genAIConfig == null) genAIConfig = new GenAIConfig();

        genAIConfig.aiSoftwarePath = AISoftwarePath;
        aiProcess = new AIProcess(genAIConfig);

        await UniTask.WaitUntil(
            () => aiProcess.IsProcessAlive(),
            cancellationToken: ct
        );

        await WaitUntilReadyAsync(ct,progress);
    }

    public async UniTask WaitUntilReadyAsync(CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
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

    public async UniTask GenerateAsync(string input, CancellationToken ct, IProgress<float> progress = null, int timeoutMs = 120000)
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
 
### 2.GenAIに注入する
 
```csharp
using AIDrivenFW.API;
 
GenAI genAI = new GenAI(new CustomExecutor());
 
string result = await genAI.Generate("Hello");
```
 
---
 
### デフォルト実装の例
 
フレームワークには `LlamaProcessExecutor` が同梱されています。
 
```csharp
GenAI genAI = new GenAI(new LlamaProcessExecutor());
```
 
このExecutorでは以下を内部で管理しています：
 
- llama-cli.exe の起動
- モデルロード待機
- stdout監視
- CLIノイズ除去
- マーカー判定による生成完了検知
 
---
 
## 設計思想
 
AIDrivenFrameworkは  
**「LLMを安全に扱える体験を保証する」ためのフレームワーク**です。
 
LLM統合で起こりがちな：
 
- 起動忘れ
- モデル未ロードのまま生成
- 実行状態が分からず止められない
 
をAPIレベルで起きないようにすることを重視しています。
 
---
 
## モデルについて
> [!CAUTION]
> モデルおよびLLM実行環境は含まれません。
> ライセンスは各公式配布元に従ってください。
 
---
 
## ライセンス
 
- フレームワーク本体（AIDrivenFramework）：MIT License
- Rounded M+ Fonts：M+ FONTS LICENSE
- 前提パッケージ（UniTask / LitMotion 等）：各パッケージのライセンスに従います

※ 本リポジトリはモデルファイル・LLM 実行環境を含みません。
 
---
 
## 対象ユーザー
 
- Unity で LLM を扱ってみたい方
- ローカル LLM に興味はあるが、導入が不安な方
- LLM × ゲーム / インタラクティブ表現を試したい方
- 実験的に使える OSS を探している方
 
---
 
## 作者より
 
AIDrivenFrameworkは  
「LLMを組み込む前に、安心して触れる入口を作る」ために設計しました。
 
不具合・改善案・思想の違いを含め、Issue / PR を歓迎します。
