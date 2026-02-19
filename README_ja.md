# AIDrivenFramework
UnityでローカルLLMをUXや権利に配慮し、安心して扱うためのセットアップ＆実行フレームワーク
<img src="https://github.com/hatomaru/AIDrivenFramework/blob/main/Banner.png" width="800">
 
[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)
 
 ## 🚀 Quick Start
[インストール](#インストール)
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
 
## 🎯 V1 公開API
 
V1では、公開APIを最小構成に制限しています。
 
```csharp
GenAI.Generate(string input, GenAIConfig genAIConfig = null)
GenAIConfig
GenAI.IsPrepared()
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
- 推奨: RAM 16GB以上 / VRAM 8GB以上（モデル依存）
 
※MacOSは未検証
 
---
 
## インストール
 
### Package Manager (Git URL)
 Unityの Package Manager を開き、`+` ボタン内の`Add package from git URL...` を選択して以下を入力してください：
```
https://github.com/hatomaru/AIDrivenFramework.git?path=src/AIDrivenFramework
```
 
---
 
## 依存パッケージ
 
本フレームワークの動作には以下のパッケージが必要です。これらを Unity Package Manager 等からプロジェクトに導入してください。

- [UniTask](https://github.com/Cysharp/UniTask) (非同期処理)

- [LitMotion](https://github.com/AnnulusGames/LitMotion/blob/main/README_JA.md) (UI / 演出制御)

- [StandaloneFileBrowser](https://github.com/gkngkc/UnityStandaloneFileBrowser) (セットアップ時のファイル選択)
 
---
 
## セットアップ
 
### 1. LLMの準備
 
本フレームワークには
 
- llama.cpp
- .ggufモデル
 
は同梱されていません。
 
各自でHugging Face 等など公式配布元から取得してください。
 
---
 
### 2. Setupウィンドウ実行
 
Unityエディタで任意のシーンを開いて**再生**してください

開くとローカルLLMの環境構築が出来ているのかを自動で判断し、出来ていない場合はセットアップ画面(AIDrivenSetup)が開きます。

ダウンロードしたLlama.cppと.ggufモデルファイルを選択し、
指示に従い設定してください。

また、ビルド上でも動作するので、ユーザーも同様の手順で設定できます。
 
## オプション
### 1. Setupウィンドウ実行
 
Unityメニュー：
 
```
AIDrivenFramework > Setup
```
 
Setup ウインドウでは、以下の 任意コンポーネントを選択できます。

- AISetup（AIセットアップ）
- Example Scene（サンプルシーン）

必要なものにチェックを入れ、**「Install Selected」** を押してください。
※ Import ダイアログが表示されます。内容を確認した上で Import を行ってください。

※ まずは「AISetup」と「Example Scene」にチェックを入れることをおすすめします。

### 2.Setup Complete! が表示されれば完了

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
    GenAI genAI = new GenAI();

    void Start()
    {
        TestCode().Forget();
    }

    async UniTask TestCode()
    { 
        // デフォルトAIエグゼキュータをセットする
        //genAI.SetExecutor(ExecutorFactory.CreateDefault());
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
 
ローカルLLM環境が利用可能かを確認できます。
 
```csharp
bool ready = GenAI.IsPrepared();
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
        if (config == null)
            config = new GenAIConfig();
 
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
