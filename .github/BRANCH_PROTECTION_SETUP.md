# GitHub Branch Protection Rules - Setup Guide

このガイドでは、satochan0112 のみが master ブランチに直接プッシュできるように設定する手順を説明します。

## 前提条件

- GitHub のリポジトリ管理者権限を持つアカウント
- satochan0112 がリポジトリのコラボレーターまたはメンバーとして追加されていることを確認

## 設定手順

### ステップ 1: Settings ページへアクセス

1. GitHub でリポジトリを開く
2. **Settings** タブをクリック

### ステップ 2: Branches セクションへ移動

1. 左メニューから **Branches** をクリック
2. **Branch protection rules** セクションが表示されます

### ステップ 3: Branch Protection Rule を追加

#### 現在のルールがない場合：
- **Add rule** ボタンをクリック

#### 既存のルールを編集する場合：
- 既存の **master** ルールをクリックして編集

### ステップ 4: 詳細設定

以下の項目を設定してください：

| 項目 | 設定値 | チェック |
|------|--------|---------|
| **Branch name pattern** | `master` | - |
| **Require a pull request before merging** | オン | :white_check_mark: |
| Require approvals | オン | :white_check_mark: |
| Minimum number of approvals | `1` | - |
| Dismiss stale pull request approvals | オン | :white_check_mark: |
| Require review from Code Owners | オン | :white_check_mark: |
| Require branches to be up to date | オン | :white_check_mark: |
| Require status checks to pass | オン（設定済みなら） | :white_check_mark: または :o: |
| Require conversation resolution | オン | :white_check_mark: |
| **Include administrators in restrictions** | - | **:ballot_box: チェック外す** |
| **OR Restrict who can push to matching branches** | - | **:white_check_mark: チェックを入れる** |

### ステップ 5: 許可ユーザーの指定（最重要）

**"Restrict who can push to matching branches"** を有効にした後：

1. **"Restrict who can push to matching branches"** セクション下の **Add** ボタンをクリック
2. **@satochan0112** を検索して追加

追加後は以下のように表示されます：
```
:white_check_mark: Restrict who can push to matching branches
  Users:
  - @satochan0112
```

### ステップ 6: 設定を保存

ページ下部の **Save changes** または **Update** ボタンをクリック

## 設定完了後の動作

| ユーザー | 直接プッシュ | PR マージ | 説明 |
|---------|-----------|---------|------|
| satochan0112 | :white_check_mark: 可能 | :white_check_mark: 可能 | master に直接プッシュ可能 |
| その他のコラボレーター | :x: 不可 | :x: 不可（PR 承認待ち） | PR 経由のみ、satochan0112 の承認が必須 |

## 図解：Web UI での操作

```
Settings
  └─ Branches
      └─ Add rule ボタン
          ↓
      Branch name pattern: master
      
      ? Require a pull request before merging
        ? Require approvals (1)
        ? Dismiss stale pull request approvals
        ? Require review from Code Owners
      
      ? Require branches to be up to date before merging
      ? Require conversation resolution before merging
      
      ? Include administrators in restrictions
      
      ? Restrict who can push to matching branches
        └─ Add: @satochan0112
      
      [Save changes] ボタン
```

## よくある質問

### Q1: satochan0112 が PR を使わずに直接プッシュできるんですか？

**A:** はい。"Restrict who can push to matching branches" に satochan0112 を指定しているため、satochan0112 のみ直接プッシュ可能です。ただし、github.com の Settings で satochan0112 が Owner または Admin ロールを持っている必要があります。

### Q2: 他のコラボレーターが master にプッシュしようとしたらどうなるか？

**A:** Git でプッシュ時に以下のエラーが表示されます：
```
remote: error: You do not have permission to push to this branch on this repository.
```

### Q3: PR はすべて satochan0112 が承認する必要があるのか？

**A:** はい。CODEOWNERS ファイルで `* @satochan0112` と指定されているため、すべての PR は satochan0112 の承認が必須です。

### Q4: 設定後、既存の PR はどうなるのか？

**A:** 既存の PR は影響を受けません。新しい PR から保護ルールが適用されます。

### Q5: 緊急時に他のユーザーを一時的に追加できるか？

**A:** はい。Settings の "Restrict who can push to matching branches" から追加のユーザーを指定できます。

## トラブルシューティング

### 設定項目が見つからない

- リポジトリが **Public** に設定されているか確認
- GitHub Free プランでは一部機能が利用できない可能性があります

### 設定後も直接プッシュできる（してはいけない）

- satochan0112 のアカウントか確認
- "Include administrators in restrictions" のチェックが外れているか確認

### CODEOWNERS ファイルが反映されない

- ファイルが `.github/CODEOWNERS` または `CODEOWNERS` (ルート) に存在することを確認
- ファイルが UTF-8 エンコーディングであることを確認
- 設定後、キャッシュをクリアするため数分待つ

## セキュリティベストプラクティス

:white_check_mark: **推奨:**
- PR で全ての変更を記録
- satochan0112 が担当者として変更を確認
- コミット履歴を追跡可能に保つ

:x: **避けるべき:**
- 複数のユーザーに直接プッシュ権限を与える
- 保護ルールを頻繁に変更する
- Admin アカウント情報を共有する

## 参考リンク

- [GitHub 公式ドキュメント - ブランチ保護ルール](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/managing-a-branch-protection-rule)
- [GitHub 公式ドキュメント - Code Owners](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners)

---

**このガイドに従うことで、satochan0112 のみが master ブランチを完全に管理できる安全な環境が構築されます。**
