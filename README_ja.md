# AIDrivenFramework
UnityでローカルLLMをUXや権利に配慮し、安心して扱うためのセットアップ＆実行フレームワーク
<img src="https://github.com/hatomaru/AIDrivenFramework/blob/main/Banner.png" width="800">

[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)
## 概要
**AIDrivenFramework** は、Unity プロジェクト上で、ローカル LLM（例：llama.cpp）を安全に統合するための**実験的フレームワーク**です。

### 特徴

- 1行のプログラムでllama.cppを扱える
- プロセス未起動時は自動で起動・初期化
- モデル未ロード時は自動でロード
- 標準出力ノイズを吸収し、生成結果のみを返却
- モデルの再配布を行わないように配慮した設計
- Unityと自然に統合可能

## 動作環境
- **Unity 2022.3 LTS** 以上推奨
- **OS**: Windows 10/11 (64bit)
※MacOSは動作検証を行っていません。
- **推奨スペック**: VRAM 8GB以上 / RAM 16GB以上（使用するモデルに依存します）

## インストール

### Package Manager (Git URL)
Unityの Package Manager を開き、`+` ボタン内の`Add package from git URL...` を選択して以下を入力してください：

```text
https://github.com/hatomaru/AIDrivenFramework.git?path=src/AIDrivenFramework
```

## 前提パッケージ
本フレームワークの動作には以下のパッケージが必要です。これらを Unity Package Manager 等からプロジェクトに導入してください。

- [UniTask](https://github.com/Cysharp/UniTask) (非同期処理)

- [LitMotion](https://github.com/AnnulusGames/LitMotion/blob/main/README_JA.md) (UI / 演出制御)

- [StandaloneFileBrowser](https://github.com/gkngkc/UnityStandaloneFileBrowser) (セットアップ時のファイル選択)


## セットアップ
導入後、以下の手順で初期設定を行います。

**1.モデルの準備**

Hugging Face 等から .gguf 形式のモデルファイルと[Llama.cpp](https://github.com/ggerganov/llama.cpp/releases)をご自身でダウンロードしてください。 （本フレームワークにはモデルファイルとLlama.cppは同梱されていません）

**2.セットアップウィザードの実行**

Unityエディタで任意のシーンを開いて**再生**してください

開くとローカルLLMの環境構築が出来ているのかを自動で判断し、出来ていない場合はセットアップ画面(AIDrivenSetup)が開きます。

ダウンロードしたLlama.cppと.ggufモデルファイルを選択し、初期設定を完了させてください。これが完了すると、初期設定が済んだ状態になります。

また、ビルド上でも動作するので、ユーザーも同様の手順で設定できます。

## 基本的な使い方
> [!IMPORTANT]
> ### 前提
> **上記のセットアップ** が完了していること
> (モデルの取得・配置・確認が済んでいる状態)

AI呼び出しの最小コード例
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
            "こんにちは",
            ct: destroyCancellationToken
        );

        Debug.Log("Response: " + response);
    }
}
```

これだけで、内部では以下の処理が**自動的**に行われます。
- LLM 実行プロセスの起動（未起動の場合）
- モデルのロード（未ロードの場合）
- 生成コマンドの送信
- 出力の整形・抽出
- 生成結果の返却

> [!CAUTION]
> ### 注意事項
> 本フレームワークは実験的(Experimental)です。
> 
> API・構造は今後変更される可能性があります
> 
> 商用利用の可否は、使用する LLM 実行環境・モデルのライセンスに依存します。


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
AIDrivenFrameworkは「ライブラリ」ではなく「**体験を保証するフレームワーク**」として設計されています。

### なぜプロセス管理を隠蔽するのか
LLM 実行では、以下のようなミスが起きがちです。
- プロセス未起動のまま入力を送る
- モデル未ロードのまま生成を行う
- 実行状態が分からず止められない

AIDrivenFrameworkではこれらの**順序ミス**を、APIレベルで起きないようにすることを重視しています。

## モデルについて
> [!CAUTION]
> 本フレームワークは LLM モデルファイル及びLlama.cppを同梱・配布しません。
> 
> モデルは 各ユーザーが公式配布元から取得してください
>
> 本リポジトリでは 取得手順の案内のみを行います

## Configについて
AIDrivenFrameworkでは、設定を以下のように扱います。
### Configで設定できるもの
- モデルパス
- 自動起動 / 自動終了
- Args（上級者向け）
- config.Args = "--ctx-size 2048 --n-gpu-layers 32 --temp 0.7";
詳細な制御が必要な場合にLLM 実行時の引数を文字列で指定できます。

## 対象ユーザー
- Unity で LLM を扱ってみたい方
- ローカル LLM に興味はあるが、導入が不安な方
- LLM × ゲーム / インタラクティブ表現を試したい方
- 実験的に使える OSS を探している方

### 作者からのメッセージ
AIDrivenFrameworkは「LLM をゲームに組み込む」前に、「LLM に安心して触れる入口を作りたい」という動機から生まれました。

不具合・改善案・思想の違いを含め、Issue / PR を歓迎します。
