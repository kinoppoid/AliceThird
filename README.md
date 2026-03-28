# AliceThird

**AliceSecond Ver 2.10a**（2000年）の26年ぶりの後継エンジン。
オリジナルと互換のスクリプト形式（`.alc`）でビジュアルノベル・AVGを動かすことができます。

## 動作環境

- Windows 10/11
- [.NET 8 ランタイム](https://dotnet.microsoft.com/download/dotnet/8.0)

## 使い方

```
AliceThird.exe [スクリプトパス]
```

引数を省略すると、実行ファイルと同じフォルダの `index.alc` を読み込みます。

### フォルダ構成

```
ゲームフォルダ/
  index.alc        スタートスクリプト
  alice.ini        設定ファイル（省略可）
  pic/             画像ファイル（BMP, PNG, JPG 等）
  bgm/             BGM（WAV, MP3 等）
  se/              効果音（WAV 等）
  avi/             動画（AVI, MP4 等）
```

### alice.ini

```ini
# テキスト行間ウェイト（ミリ秒）。デフォルト 0
TextDelay=200
```

## スクリプト仕様

命令一覧・詳細は [AliceThird_仕様.md](./AliceThird_仕様.md) を参照してください。

## オリジナルについて

本ソフトウェアは、**AliceSecond Ver 2.10a**（製作：時津城 克己 氏）の配布物に含まれる公開された仕様をもとに、スクリプト形式・命令体系を互換実装したものです。リバースエンジニアリングは行っていません。

- オリジナル配布元：[AliceSecond Ver2.10a — Vector](https://www.vector.co.jp/soft/dl/win95/game/se093675.html)

## ライセンス

[The Unlicense](./LICENSE) — パブリックドメイン。自由にお使いください。
