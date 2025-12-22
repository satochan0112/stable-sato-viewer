# Stable Sato Viewer - Repository Setup Complete

GitHub リポジトリの安全な公開設定が完了しました。

## 作成されたファイル

### 1. **.github/BRANCH_PROTECTION_SETUP.md**
- Branch Protection Rules の詳細な設定ガイド
- satochan0112 のみが master に直接プッシュ可能な設定方法
- GitHub Web UI での手順を図解入りで説明

### 2. **CONTRIBUTING.md**
- コントリビューション時のガイドライン
- 行動規範
- バグ報告・機能リクエストの方法
- PR 作成フロー
- コーディング規約
- コミットメッセージ規約

### 3. **LICENSE**
- MIT License を採用
- satochan0112 の著作権表示

### 4. **CODEOWNERS** （既存）
- satochan0112 が全ての PR の承認者として指定

## 必要な手動設定（GitHub Web UI）

以下は **GitHub Web UI でのみ設定可能**です：

### 手順：

1. **GitHub リポジトリの Settings を開く**
   - https://github.com/satochan0112/stable-sato-viewer/settings

2. **Branches を選択**
   - 左メニュー > Branches

3. **Branch protection rule を編集/追加**

   **Branch name pattern:** `master`

   **以下をチェック:**
   ```
   ? Require a pull request before merging
     ? Require approvals (1)
     ? Dismiss stale pull request approvals
     ? Require review from Code Owners
   
   ? Require branches to be up to date before merging
   ? Require conversation resolution before merging
   
   ? Include administrators in restrictions （チェック外す）
   
   ? Restrict who can push to matching branches
     └─ Add: @satochan0112
   ```

4. **Save changes をクリック**

## 完成したセキュリティ設定

? **satochan0112 のみ可能な操作：**
- master ブランチへの直接プッシュ
- PR のマージ

? **その他のコントリビューター：**
- 新機能ブランチでの開発
- PR の作成
- satochan0112 の承認を待つ

? **全ユーザー：**
- リポジトリのフォーク
- Issue の作成
- Discussion への参加（設定時）

## ドキュメントチェックリスト

- [x] README.md（日本語版）- 使い方の説明
- [x] README.en.md（英語版）- 使い方の説明
- [x] CONTRIBUTING.md - コントリビューションガイド
- [x] .github/CODEOWNERS - Code Owners 設定
- [x] .github/BRANCH_PROTECTION_SETUP.md - セットアップガイド
- [x] LICENSE - MIT License
- [ ] GitHub Branch Protection Rules - **Web UI で手動設定**

## 次のステップ

1. **GitHub Web UI で Branch Protection Rules を設定**
   - .github/BRANCH_PROTECTION_SETUP.md を参照して設定

2. **リポジトリを公開**
   - Settings > General > Visibility を "Public" に変更

3. **コラボレーターを招待**（必要に応じて）
   - Settings > Collaborators

4. **リリースを準備**
   - Releases ページで最初のリリースを作成

## ファイル一覧

```
stable-sato-viewer/
├── README.md                          # 日本語 README
├── README.en.md                       # 英語 README
├── CONTRIBUTING.md                    # コントリビューション ガイドライン
├── LICENSE                            # MIT License
├── .github/
│   ├── CODEOWNERS                     # Code Owners 設定
│   └── BRANCH_PROTECTION_SETUP.md     # セットアップガイド
├── PngViewer/
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   ├── App.xaml
│   ├── App.xaml.cs
│   ├── Assets/
│   └── StableSatoViewer.csproj
└── .gitignore
```

## トラブルシューティング

**Q: Branch Protection Rules が見つからない**
- A: Settings > Branches を確認。リポジトリが Public の場合は表示されます。

**Q: satochan0112 が直接プッシュできない**
- A: GitHub Web UI の設定を確認。"Include administrators in restrictions" がチェックされていないか確認。

**Q: PR がマージできない**
- A: satochan0112 がレビューして Approve してください。

---

**セットアップ完了です！安全でプロフェッショナルな GitHub リポジトリが整いました。**
