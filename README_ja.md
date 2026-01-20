# AIDrivenFramework
UnityでローカルLLMをUXや権利に配慮し、安心して扱うためのセットアップ＆実行フレームワーク
<img src="https://github.com/hatomaru/AIDrivenFramework/blob/main/AISetup/AssetResources/Frame/Bg.png" width="800">

[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)
## 概要
**AIDrivenFramework** は、Unity プロジェクト上で、ローカル LLM（例：llama.cpp）を安全に統合するための実験的フレームワークです。

### 特徴

- 1行のプログラムでllama.cppを触れる
- プロセス未起動時は自動で起動・初期化
- モデル未ロード時は自動でロード
- 標準出力ノイズを吸収し、生成結果のみを返却
- モデルの再配布を行わないように配慮した設計
- Unityと自然に統合可能


## 基本的な使い方
> [!IMPORTANT]
> ### 前提
> **AIDrivenSetup** が完了していること
> (モデルの取得・配置・確認が済んでいる状態)

最小コード例
```
using AIDrivn;

string response = await GenAI.Generate(
    "こんにちは",
    ct: cancellationToken
);

Debug.Log("Response: " + response);
```

これだけで、内部では以下の処理が**自動的**に行われます。
- LLM 実行プロセスの起動（未起動の場合）
- モデルのロード（未ロードの場合）
- 生成コマンドの送信
- 出力の整形・抽出
- 生成結果の返却

> [!CAUTION]
> ## 注意事項
> 本フレームワークは **実験的（Experimental）**です
> API・構造は今後変更される可能性があります
> 商用利用の可否は、使用する LLM 実行環境・モデルのライセンスに依存します

## 前提パッケージ
本フレームワークは以下のパッケージを前提としています。

- UniTask（非同期処理）
- LitMotion（UI / 演出制御）
- StandaloneFileBrowser (AIセットアップ時のファイル読み込み画面)

これらは Unity Package Manager から導入してください。


## ライセンス
本リポジトリに含まれる各要素のライセンスは以下の通りです。

- フレームワーク本体（AIDrivenFramework）：MIT License
- Rounded M+ Fonts：M+ FONTS LICENSE
- 前提パッケージ（UniTask / LitMotion 等）：各パッケージのライセンスに従います

※ 本リポジトリは LLM モデルファイルを含みません。

フレームワーク部分の改変・再配布・Fork を歓迎します
モデルファイル・LLM 実行環境のライセンスは含みません

## フォントについて

本フレームワークには M+ FONTS を含んでいます。

M+ FONTS は M+ FONTS PROJECT により配布されている
フリー（自由な）ソフトウェアであり、
商用利用・再配布・改変が許可されています（無保証）。

詳細なライセンス条件については、
フォントディレクトリに同梱されている LICENSE_J.txt および LICENSE_E.txt を参照してください。


## 設計思想
AIDrivenFrameworkは **「ライブラリ」ではなく「体験を保証するフレームワーク」**として設計されています。

### なぜプロセス管理を隠蔽するのか
LLM 実行では、以下のようなミスが起きがちです。
- プロセス未起動のまま入力を送る
- モデル未ロードのまま生成を行う
- 実行状態が分からず止められない

AIDrivenFrameworkではこれらの**順序ミス**を、APIレベルで起きないようにすることを重視しています。

## モデルについて
> [!CAUTION]
> 本フレームワークは LLM モデルファイルを同梱・配布しません
> モデルは 各ユーザーが公式配布元から取得してください
> 本リポジトリでは 取得手順の案内のみを行います

これは、再配布禁止モデルへの配慮とユーザー自身による管理を両立するための設計です。

## Config と Args について

AIDrivenFrameworkでは、設定を以下の2層で扱います。
### Config（フレームワークが責任を持つ部分）
- モデルパス
- 自動起動 / 自動終了
- 安全な実行ポリシー
- Args（上級者向け）
- config.Args = "--ctx-size 2048 --n-gpu-layers 32 --temp 0.7";
詳細な制御が必要な場合にLLM 実行時の引数を文字列で指定できます。

## 対象ユーザー
- Unity で LLM を扱ってみたい方
- ローカル LLM に興味はあるが、導入が不安な方
- LLM × ゲーム / インタラクティブ表現を試したい方
- 実験的に使える OSS を探している方

## 開発状況
✔ プロセス分離
✔ 自動起動・生成フロー
✔ Namespace 整理
⏳ TestMode / ExampleScene（予定）

### 作者からのメッセージ
AIDrivenFrameworkは「LLM をゲームに組み込む」前に、「LLM に安心して触れる入口を作りたい」という動機から生まれました。
不具合・改善案・思想の違いを含め、Issue / PR を歓迎します。
