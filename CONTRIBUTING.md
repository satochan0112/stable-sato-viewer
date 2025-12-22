# Contributing to Stable Sato Viewer

Stable Sato Viewer へのコントリビューションをありがとうございます！このプロジェクトに貢献するための方法をご説明します。

## 行動規範

すべてのコントリビューターは以下の行動規範に従うことが期待されます：

- 他のコントリビューターに対して尊重と敬意を示す
- 建設的でポジティブなコミュニケーションを心がける
- 差別的または攻撃的な言葉を使用しない

## 貢献の種類

### バグ報告

バグを見つけた場合は、以下の情報を含めて Issue を作成してください：

```
タイトル: [簡潔なバグの説明]

説明:
- 何が起こったか
- 期待していた動作
- 実際の動作
- 再現手順

環境:
- OS: Windows 10/11
- .NET バージョン: 10.0
- アプリケーションバージョン: v1.0.0
```

### 機能リクエスト

新機能を提案する場合は、以下を含める Issue を作成してください：

```
タイトル: [新機能の説明]

説明:
- どのような問題を解決するのか
- 提案する解決策
- 代替案がある場合はその説明

例:
- [ユースケースの説明]
```

### プルリクエスト（Pull Request）

#### 開発フロー

```bash
# 1. リポジトリをフォーク
git clone https://github.com/[your-username]/stable-sato-viewer.git
cd stable-sato-viewer

# 2. 新しいブランチを作成（develop ブランチから）
git checkout -b feature/your-feature-name

# 3. 変更を加える
# ... 実装 ...

# 4. コミット（明確なメッセージで）
git commit -m "Add feature: description of changes"

# 5. フォークにプッシュ
git push origin feature/your-feature-name

# 6. GitHub で Pull Request を作成
```

#### ブランチ命名規則

```
feature/[機能名]        # 新機能
bugfix/[バグ名]         # バグ修正
docs/[説明]             # ドキュメント更新
refactor/[説明]         # リファクタリング
test/[説明]             # テスト追加
```

例：
- `feature/add-batch-operations`
- `bugfix/fix-metadata-parsing`
- `docs/update-readme`

#### PR のタイトル・説明の書き方

**良い例：**
```
[Feature] Add batch delete operation for multiple images

Allows users to select multiple images and delete them at once.
Implements checkbox selection in the file list view.

Related Issue: #123
```

**避けるべき：**
```
fix something
update code
changes
```

#### PR マージの流れ

1. **自分のフォークで PR を作成** → master ブランチへ
2. **satochan0112 によるレビュー** を待つ
3. **satochan0112 が承認** → マージ
4. **master に反映** → リリースの準備

## コーディング規約

### C# / .NET 規約

```csharp
// ファイル名は PascalCase（クラス名と一致）
public class MyFeature
{
    // 定数は UPPER_CASE
    private const string DefaultValue = "value";
    
    // プロパティは PascalCase
    public string MyProperty { get; set; }
    
    // メソッドは PascalCase
    public void MyMethod()
    {
        // ローカル変数は camelCase
        int myVariable = 0;
    }
    
    // private フィールドは _camelCase
    private int _privateField;
}
```

### 命名規則

| 要素 | 規則 | 例 |
|------|------|-----|
| クラス | PascalCase | `MainWindow`, `ImageLoader` |
| メソッド | PascalCase | `LoadImage()`, `SaveState()` |
| プロパティ | PascalCase | `ImageSource`, `FileName` |
| ローカル変数 | camelCase | `fileName`, `imageList` |
| 定数 | UPPER_CASE | `MAX_RETRY_COUNT` |
| プライベートフィールド | _camelCase | `_filePath` |

### コメント・ドキュメント

```csharp
/// <summary>
/// 画像ファイルを読み込んで表示します。
/// </summary>
/// <param name="filePath">画像ファイルの完全パス</param>
/// <returns>読み込み成功時は true、失敗時は false</returns>
public bool LoadImage(string filePath)
{
    // 実装
}
```

## 開発環境のセットアップ

```bash
# リポジトリのクローン
git clone https://github.com/satochan0112/stable-sato-viewer.git
cd stable-sato-viewer

# プロジェクトのビルド
cd PngViewer
dotnet build

# テスト（必要に応じて）
dotnet test

# 実行
dotnet run
```

## テスト

機能追加やバグ修正の際は、以下をテストしてください：

- :ballot_box_with_check: 追加/修正した機能が正常に動作する
- :ballot_box_with_check: 既存機能に影響がないか確認
- :ballot_box_with_check: ウィンドウのサイズ変更時の表示が正常か
- :ballot_box_with_check: 異常系（不正なファイルなど）の処理が正常か

## コミットメッセージの規約

```
[Type] Brief summary (50 chars max)

Detailed explanation of changes (if needed)

Related Issue: #123
```

**Type の種類：**
- `[Feature]` - 新機能
- `[Bugfix]` - バグ修正
- `[Docs]` - ドキュメント更新
- `[Refactor]` - コード改善（機能変更なし）
- `[Test]` - テスト追加
- `[Chore]` - ビルド設定など

**例：**
```
[Feature] Add image batch delete functionality

- Implement checkbox selection in file list
- Add confirmation dialog for batch deletion
- Update UI to show selection count

Related Issue: #42
```

## PR がマージされるまでの流れ

1. **PR を作成** → コミットメッセージとタイトルが明確であることを確認
2. **自動チェックを待つ** （設定されている場合）
3. **satochan0112 がレビュー** → コメント・要求があれば対応
4. **変更を push** → PR は自動で更新されます
5. **Approve を獲得** → satochan0112 が承認
6. **マージ** → master ブランチに統合

## リリース

- **メンテナー**（satochan0112）のみがリリースを担当します
- 重要なバグ修正は優先的にリリースされます
- 新機能は次のマイナーバージョンでリリースされることが多いです

詳細は [RELEASE_GUIDE.md](.github/RELEASE_GUIDE.md) を参照してください。

## ライセンス

このプロジェクトに貢献することで、あなたはあなたの貢献がプロジェクトのライセンスの下でライセンスされることに同意するものとします。

## 質問・サポート

- **バグ報告**: Issue を作成
- **機能リクエスト**: Issue を作成（"enhancement" ラベル付け）
- **質問**: Discussions（設定されている場合）または Issue

## 感謝

Stable Sato Viewer に貢献いただき、ありがとうございます！
あなたのコントリビューションはプロジェクトをより良くするのに役立ちます。

---

**さらに詳しい情報:** [GitHub CONTRIBUTING ガイドライン](https://docs.github.com/en/communities/setting-up-your-project-for-healthy-contributions)
