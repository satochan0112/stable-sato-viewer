# Release Guide - Stable Sato Viewer

このドキュメントでは、Stable Sato Viewer のリリース手順を説明します。

## 自動リリース（GitHub Actions）

### 準備

1. **バージョン番号を決定**
   - セマンティックバージョニングに従う（例：1.0.0, 1.1.0, 2.0.0）
   - [セマンティックバージョニング](https://semver.org/lang/ja/) を参照

2. **変更ログを準備（オプション）**
   - リリースノートに含めるための変更内容をメモ

### リリース手順

#### 方法 1: GitHub Web UI から（推奨）

1. **GitHub リポジトリを開く**
   - https://github.com/satochan0112/stable-sato-viewer

2. **Actions タブをクリック**
   - ナビゲーションから **Actions** を選択

3. **Release ワークフローを選択**
   - 左メニューから **Release** をクリック

4. **Run workflow をクリック**
   - **Run workflow** ボタンをクリック

5. **パラメータを入力**
   ```
   Release version: 1.0.0  # バージョン番号（v は不要）
   Mark as pre-release: チェック不要（通常リリース）
   ```

6. **Run workflow ボタンをクリック**
   - ワークフローが開始されます

7. **ワークフロー実行を監視**
   - Actions ページでリアルタイム実行状況を確認

8. **リリースが完了**
   - [Releases ページ](https://github.com/satochan0112/stable-sato-viewer/releases) に新しいリリースが作成されます

#### 方法 2: GitHub CLI から

```bash
# GitHub CLI をインストール（未インストールの場合）
# https://cli.github.com/

# リポジトリディレクトリで実行
cd stable-sato-viewer

# リリースワークフローをトリガー
gh workflow run release.yml -f version=1.0.0 -f prerelease=false
```

### ワークフロー実行中の処理

Release ワークフローは以下を自動実行します：

1. :gear: **リポジトリをチェックアウト**
   - 最新のコードを取得

2. :gear: **.NET 10 をセットアップ**
   - ビルド環境を準備

3. :gear: **依存ライブラリを復元**
   - NuGet パッケージを復元

4. :gear: **Release ビルドを実行**
   - `dotnet build --configuration Release`

5. :gear: **Windows x64 版をパブリッシュ**
   - `dotnet publish -r win-x64`

6. :gear: **Windows x86 版をパブリッシュ**
   - `dotnet publish -r win-x86`

7. :gear: **ZIP アーカイブを作成**
   - 両方のアーキテクチャをパッケージ化

8. :gear: **GitHub Release を作成**
   - タグ、リリースノート、アセットを生成

## リリース成果物

### 作成されるファイル

| ファイル | 説明 |
|---------|------|
| `StableSatoViewer-1.0.0-win-x64.zip` | Windows 64-bit 実行ファイル |
| `StableSatoViewer-1.0.0-win-x86.zip` | Windows 32-bit 実行ファイル |

### ダウンロード場所

- [GitHub Releases ページ](https://github.com/satochan0112/stable-sato-viewer/releases)
- 最新版からダウンロード可能

## リリースノートの編集

### 自動生成されたリリースノートを編集

1. **Releases ページを開く**
   - GitHub リポジトリ > Releases

2. **対象リリースの Edit をクリック**
   - 編集可能なドラフト形式で開きます

3. **リリースノートを更新**
   ```markdown
   ## v1.0.0 - 2024-01-15
   
   ### 新機能
   - フォルダツリーの表示機能
   - メタデータの抽出と表示
   - フィルター機能
   
   ### 改善
   - UI のレスポンス改善
   - メモリ使用量の最適化
   
   ### バグ修正
   - 画像表示のレイアウト問題を修正
   - フィルター結果の表示不具合を修正
   ```

4. **Save changes をクリック**

## バージョン管理

### セマンティックバージョニング

```
MAJOR.MINOR.PATCH
  ↓      ↓      ↓
  1.     2.     3
```

- **MAJOR**: 破壊的変更（API 変更など）
  - 例：1.0.0 → 2.0.0

- **MINOR**: 新機能追加（下位互換あり）
  - 例：1.0.0 → 1.1.0

- **PATCH**: バグ修正
  - 例：1.0.0 → 1.0.1

### リリース前のチェックリスト

Release ワークフローを実行する前に確認：

:ballot_box_with_check: master ブランチが最新の状態
:ballot_box_with_check: すべての PR がマージ済み
:ballot_box_with_check: ローカルでビルド成功を確認
:ballot_box_with_check: バージョン番号を決定
:ballot_box_with_check: リリースノートの内容を準備（オプション）

## トラブルシューティング

### ワークフローが失敗した

1. **Actions ページで失敗の詳細を確認**
   - GitHub Actions > Release > 失敗したワークフロー実行

2. **ログを確認**
   - 各ステップの出力を確認

3. **一般的な原因と対処法**

| エラー | 原因 | 対処法 |
|--------|------|--------|
| `dotnet build failed` | ビルドエラー | ローカルでビルド確認 |
| `Permission denied` | トークン権限不足 | リポジトリ設定を確認 |
| `Publish failed` | .NET SDK の問題 | ワークフロー内の .NET バージョン確認 |

### リリースが GitHub に作成されない

1. **トークン権限を確認**
   - Settings > Actions > General > Workflow permissions
   - "Read and write permissions" を選択

2. **ブランチ保護ルールを確認**
   - Settings > Branches > Branch protection rules
   - タグ作成権限があるか確認

3. **ワークフローファイルを確認**
   - `.github/workflows/release.yml` が正しいか確認

## リリース後の処理

### 1. リリースを公開

```bash
# GitHub CLI でリリース公開（ドラフトの場合）
gh release edit v1.0.0 --draft=false
```

### 2. ドキュメント更新

- README.md に最新バージョン情報を記載（必要に応じて）
- リリースノートを充実させる

### 3. アナウンス

- GitHub Discussions でリリースを公開
- Twitter/X などで告知（必要に応じて）

## 継続的インテグレーション

### 自動ビルド

- `build.yml` ワークフローが master/develop ブランチへのプッシュ時に自動実行
- PR 作成時も自動ビルド

### リリース前の品質確認

Release ワークフロー実行前に以下を確認：

1. **ビルドが成功しているか**
   - Actions > Build で最新の実行結果を確認

2. **テストがパスしているか**
   - Build ワークフロー内のテストステップ確認

3. **コードレビューが完了しているか**
   - PR がすべてマージされているか確認

## 参考リンク

- [GitHub Actions ドキュメント](https://docs.github.com/en/actions)
- [Semantic Versioning](https://semver.org/lang/ja/)
- [GitHub Releases について](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)

---

**リリース手順に関する質問や問題は、GitHub Issues で報告してください。**
