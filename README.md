# Stable Sato Viewer

**Stable Sato Viewer** は、Stable Diffusion で生成された PNG 画像のメタデータ（プロンプト、ネガティブプロンプト、学習パラメータ）を表示・管理するための Windows デスクトップアプリケーションです。

![Stable Sato Viewer](PngViewer/Assets/stablesatoviewer.png)

[English README is here](README.en.md)

## 主な機能

- :file_folder: **フォルダツリー表示** - ドライブ全体を階層的に表示、フォルダ間を簡単に移動
- :framed_picture: **画像ビューア** - PNG 画像の表示と拡大縮小
- :mag_right: **マウス操作でズーム** - スクロール拡大/縮小、ドラッグパン、マウス中心ズーム、ウィンドウリサイズ対応
- :memo: **メタデータ表示** - PNG の tEXt/iTXt チャンクから情報を抽出・表示
  - プロンプト (Parameters)
  - ネガティブプロンプト (Negative Prompt)
  - 学習パラメータ (Steps、CFG Scale、Sampler など)
- :mag: **フィルター機能** - プロンプトの内容で画像を検索・絞込
- :star: **お気に入り管理** - よく使う画像をブックマーク
- :art: **レイアウトモード** - パネルに応じた表示切替
- :clipboard: **クリップボードコピー** - 選んだテキストをコピー
- :wastebasket: **ゴミ箱削除** - 不要な画像を安全に削除
- :floppy_disk: **状態保存** - ウィンドウサイズ、位置、最後に開いた画像を記録

## システム要件

- **OS**: Windows 10/11
- **.NET**: .NET 10.0 以上を推奨
- **その他**: WPF 対応

## インストール

### リリース版（推奨）

1. [Releases](https://github.com/satochan0112/stable-sato-viewer/releases) から最新版をダウンロード
2. ZIP ファイルを解凍
3. `StableSatoViewer.exe` を実行

### ソースからビルド

```bash
git clone https://github.com/satochan0112/stable-sato-viewer.git
cd stable-sato-viewer/PngViewer
dotnet build -c Release
```

## 使い方

### ズーム機能

#### スクロールでズーム

- **マウスホイール上**: 拡大（最大 500%）
- **マウスホイール下**: 縮小（最小はウィンドウサイズに合わせたサイズ）
- 拡大/縮小は段階的に行われます

#### マウス位置中心ズーム

- スクロール時、マウスカーソルがある箇所を中心にズーム
- 直感的に見たい部分を拡大できます

#### ドラッグ＆パン

- 画像が拡大されている場合、ドラッグして見える範囲を移動
- 拡大後の詳細確認に便利

#### ウィンドウリサイズ対応

- ウィンドウをリサイズすると、ズームが新しいウィンドウサイズに合わせた最小値にリセット
- ウィンドウが小さい場合でも、画像全体を見ることができます

#### ウィンドウタイトルに拡大率表示

- 現在のズーム率が「Zoom: 100%」などの形式で表示
- 拡大/縮小の目安として確認可能

### その他の基本機能

詳しい使い方は以下を参照：

- **画像の開き方**: フォルダツリー、ドラッグ＆ドロップ、コマンドライン引数
- **メタデータの表示**: プロンプト、ネガティブプロンプト、学習パラメータ
- **お気に入い機能**: 画像をブックマーク、個別削除、一括削除
- **履歴機能**: 開いた画像を管理、履歴から再表示
- **フィルター機能**: プロンプトで画像を検索

## キーボードショートカット

| キー | 機能 |
|------|------|
| `左` / `右` | 前の画像 / 次の画像 |
| `Delete` | 画像をゴミ箱へ移動 |
| `Esc` | 全画面モード終了 |

## 設定ファイル

アプリケーションの状態は以下のディレクトリに保存されます：

```
%AppData%\StableSatoViewer\
├─ windowstate.json    # ウィンドウサイズ、位置、レイアウトモード
├─ favorites.json      # お気に入りのリスト（JSON形式）
├─ history.json        # 履歴（JSON形式）
├─ lastfolder.txt      # 最後に選択したフォルダ
└─ lastimage.txt       # 最後に表示した画像
```

## 技術スタック

- **フレームワーク**: .NET 10.0
- **UI**: WPF (Windows Presentation Foundation)
- **言語**: C# 14.0

## ライセンス

このプロジェクトのライセンスについては、リポジトリのライセンスファイルを参照してください。

## 作者

- **satochan0112** - [GitHub](https://github.com/satochan0112)

---

**注意**: このアプリケーションは Stable Diffusion で生成された PNG 画像のメタデータ表示が目的です。
