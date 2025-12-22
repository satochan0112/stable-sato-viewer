# GitHub Actions Workflow Permissions Fix

## 問題
GitHub Actions で Release を作成しようとすると、以下のエラーが出ます：

```
Error 403: Resource not accessible by integration
```

## 解決方法

### ステップ 1: GitHub で Repository Settings を確認

1. **GitHub リポジトリを開く**
   - https://github.com/satochan0112/stable-sato-viewer

2. **Settings タブをクリック**

3. **左メニューから "Actions" > "General" をクリック**

4. **"Workflow permissions" セクションを確認**

```
? Read and write permissions

または

? Read repository contents and packages permissions
```

### ステップ 2: Workflow Permissions を設定

**"Workflow permissions" セクションで以下を確認：**

```
Read and write permissions
  ↓ このオプションを選択する
  (Allow GitHub Actions to create and approve pull requests)
```

**チェックを入れるもの：**
- :white_check_mark: Read and write permissions

**選択するもの：**
- :white_check_mark: Allow GitHub Actions to create and approve pull requests

### ステップ 3: Save をクリック

設定を保存します。

## ワークフローレベルの権限設定

`.github/workflows/release.yml` に以下の権限宣言を追加しました：

```yaml
permissions:
  contents: write      # Release を作成するために必要
  actions: read        # ワークフロー実行状況を読み込むために必要
```

これにより、このワークフローは以下の権限を持ちます：

- :white_check_mark: Release を作成・更新
- :white_check_mark: Tag を作成
- :white_check_mark: アセット（ZIP ファイル）をアップロード
- :white_check_mark: リポジトリの内容を読み込み

## 確認手順

設定後、以下で動作を確認します：

1. **GitHub Actions > Release をクリック**

2. **Run workflow をクリック**

3. **バージョン番号を入力（例：1.0.0）**

4. **Run workflow をクリック**

5. **ワークフロー実行を監視**
   - 成功時：Release が自動作成
   - 失敗時：詳細ログを確認

## ログで問題を診断

もし再度エラーが出た場合：

1. **GitHub Actions ページを開く**
2. **失敗したワークフロー実行をクリック**
3. **各ステップのログを確認**
4. **"Create Release" ステップのエラーメッセージを確認**

## FAQ

### Q: "contents: write" は安全ですか？

**A:** はい。これは GitHub Actions のデフォルト権限であり、Release を作成するために必要です。

### Q: 他の権限も必要ですか？

**A:** 通常は以下で十分です：

```yaml
permissions:
  contents: write
  actions: read
```

### Q: Repository Settings で "Read and write permissions" が見つからない

**A:** Settings > Actions > General > Workflow permissions セクションを確認。表示されない場合は以下を確認：

- リポジトリが Public か Private か
- 自分が Owner/Admin ロールか
- GitHub Free/Pro/Enterprise プランか

## 参考リンク

- [GitHub Actions - Permissions](https://docs.github.com/en/actions/using-workflows/workflow-syntax-for-github-actions#permissions)
- [GitHub Workflows - Default Permissions](https://docs.github.com/en/actions/security-guides/automatic-token-authentication)

---

**これで Release ワークフローが正常に動作するようになります！**
