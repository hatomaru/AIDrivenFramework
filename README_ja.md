# AIDrivenFramework 🚀  
**Unity × ローカルLLM セーフフレームワーク**

Unity にローカル LLM を安全に統合するための  
セットアップ & 実行管理フレームワークです。

<img src="https://github.com/hatomaru/AIDrivenFramework/blob/main/Docs/Banner.png" width="800">

[![License](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)  
[![Discord](https://img.shields.io/badge/Discord-CommunityGuild-5865F2?logo=discord&logoColor=white)](https://discord.gg/dfzwqCHSW2)

🎥 [紹介動画](https://www.youtube.com/watch?v=_Foj7tXq_Ss)  
[English version Readme is here](README.md)

---
## 🎞 Demo

## Model Setup Demo
<img src="https://github.com/hatomaru/AIDrivenFramework/blob/main/Docs/ja/AISetupWalkthrough.gif" width="800">

---

## ✨ 主な機能

- 🛡 セーフ・バイ・デザイン設計  
  モデル読み込み前の生成など、不正な状態を防止します。

- 🚀 ストレスフリー初期化  
  Play Mode へ入る際に LLM 環境を自動準備します。

- 🧙 内蔵セットアップウィザード（Ollama不要）  
  外部インストーラ不要。ダウンロード完結型セットアップ。

- 🔍 自動環境検出  
  未設定状態を検出し、セットアップへガイドします。

- 🖥 GUIベースのモデル／実行ファイル選択  
  `llama.cpp` と `.gguf` ファイルを直感的に選択可能。

- 🔄 インテリジェントなプロセス制御  
  プロセス起動・モデル読み込みを自動管理。

- 🧹 クリーンな出力抽出  
  CLIノイズを除去し、純粋な生成結果のみを返します。

- 🔁 モジュラーExecutor設計  
  CLI / HTTP / Custom 実装へ柔軟に切り替え可能。

- 🎮 Unity特化設計  
  Play Mode、ビルド後実行、ゲームワークフローに最適化。

Unity 側は、最小限かつクリーンな API のみを扱います。

---

## ⚡ クイックスタート

### 1️⃣ インストール

Unity Package Manager から追加：

```
https://github.com/hatomaru/AIDrivenFramework.git?path=src/AIDrivenFramework
```

---

### 2️⃣ ローカルLLMの準備

以下は別途ダウンロードしてください：

- `llama.cpp`
- `.gguf` モデルファイル

（本リポジトリには含まれていません）

> [!TIP]
> llama.cpp のビルド済みバイナリ：  
> https://github.com/ggerganov/llama.cpp/releases  
>  
> 推奨モデル例：  
> https://huggingface.co/elyza/Llama-3-ELYZA-JP-8B-GGUF  

---

### 3️⃣ 初期化

```csharp
await AIDrivenInitializer.Initialize();
```

環境が未設定の場合：

- セットアップシーンが自動で開きます  
- オプションの **AISetup** コンポーネントが必要です  

> [!TIP]
> `IsPrepared()` はオプションの `GenAI` インスタンスを受け取ります。
>
> 既存のインスタンスを渡すことで、ヘルスチェック後にLLMプロセスが終了するのを防ぎ、
> モデルの二重読み込みを回避し、起動パフォーマンスを向上させます。

---

### 4️⃣ 生成

```csharp
var genAI = new GenAI();
var result = await genAI.Generate("Hello AI");
Debug.Log(result);
```

これで準備完了です 🎉

---

## 🧙 セットアップウィザード（AISetupコンポーネント）

オプションの **AISetup** パッケージは、  
初心者向けのセットアップウィザードを提供します。

- 未設定状態の自動検出
- `llama.cpp` 実行ファイル選択GUI
- `.gguf` モデルファイル選択GUI

> [!TIP]
> サンプルシーンのワンクリック導入（任意）
>  
> メニューから手動起動：  
> `AIDrivenFramework > Setup`

セットアップウィンドウまたは  
Unity Package Manager からインストール可能（オプション依存）。

> 初回導入を大幅に簡単にします。特に初心者におすすめです。

---

## 🎯 最小公開API（V1）

```csharp
GenAI.Generate(string input, GenAIConfig config = null);
AIDrivenInitializer.Initialize();
GenAIConfig;
```

その他の構造は内部実装です。

---

## 🧠 なぜAIDrivenFrameworkなのか？

単なるラッパーではありません。

- モデル未ロード状態での生成を防止  
- プロセス未起動状態を防止  
- APIレベルで安全性を保証  
- Executor差し替えに対応  

**長期的な Unity × AI アーキテクチャ設計** を想定しています。

---

## 🔁 Executor差し替え（上級者向け）

実行レイヤーを差し替え可能：

```csharp
GenAI.SetExecutor(customExecutor);
```

デフォルト実装：

```
LlamaProcessExecutor
```

HTTP通信や独自プロセス管理も実装可能です。

---

## 📦 インストール方法

### ✅ OpenUPM（推奨）

```bash
openupm add com.hatomaru.ai.framework
```

---

### 🔧 必須依存パッケージ

- [UniTask](https://github.com/Cysharp/UniTask)（非同期処理）
- [LitMotion](https://github.com/AnnulusGames/LitMotion/blob/main/README_JA.md)（UI / アニメーション制御）

オプション：

- [UnityStandaloneFileBrowser](https://github.com/gkngkc/UnityStandaloneFileBrowser)  
  （AISetup UI 用）

---

## 🖥 動作環境

- Unity 2022.3 LTS 以上  
- Windows 10/11（64bit）  
- 推奨：RAM 16GB以上 / VRAM 8GB以上（モデル依存）

macOS は現在未検証です。

---

## 💬 コミュニティ

質問・実験共有・Unity × ローカルLLM議論はこちら：

👉 CommunityGuild  
https://discord.gg/dfzwqCHSW2

---

## ⚖ ライセンス

- フレームワーク本体：MIT License  
- モデル・LLM実行環境：含まれません  
- 各公式配布元のライセンスに従ってください  

---

## 🎮 想定ユーザー

- UnityでLLMを使いたい開発者  
- 実行安全性を重視する開発者  
- AI × ゲーム表現を試したい方  
- 実験的OSSに興味がある方  

---

## ✍ 作者より

AIDrivenFramework は  
**「ローカルLLM導入の安全な入り口」** を作るために設計されました。

Issue・PR・改善提案は大歓迎です。