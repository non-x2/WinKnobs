# Contributing to WinKnobs

WinKnobs にご興味を持っていただきありがとうございます！
Thank you for your interest in contributing to WinKnobs!

このプロジェクトは個人開発の OSS です。バグ報告・機能要望・PR を歓迎しますが、
レビューに時間がかかる場合がある点をご了承ください。

This is a personal OSS project. Bug reports, feature requests, and PRs are welcome,
but review may take time.

---

## 📋 目次 / Table of Contents

- [🐛 Issue 起票](#-issue-起票)
- [🔀 Pull Request フロー](#-pull-request-フロー)
- [💻 開発環境](#-開発環境)
- [🛡️ 必須実装ルール](#-必須実装ルール)
- [📝 コーディング規約](#-コーディング規約)
- [🧪 テスト方針](#-テスト方針)
- [🔒 セキュリティ](#-セキュリティ)
- [📜 行動規範](#-行動規範)

---

## 🐛 Issue 起票

### バグ報告
- [バグ報告テンプレ](.github/ISSUE_TEMPLATE/bug_report.md) を使ってください
- 再現手順・環境情報（Windows ver / WinKnobs ver / 該当項目）を必ず含める
- スクリーンショットがあると助かります

### 機能要望
- [機能要望テンプレ](.github/ISSUE_TEMPLATE/feature_request.md) を使ってください
- ユースケース・既存機能との比較を明記

### 質問・議論
- バグ・機能要望以外は [GitHub Discussions](https://github.com/non-x2/WinKnobs/discussions) へ

### セキュリティ脆弱性
- **Issue では報告しないでください**
- [Security Advisory](https://github.com/non-x2/WinKnobs/security/advisories/new)（プライベート）から報告

---

## 🔀 Pull Request フロー

1. **Issue を先に作成**（重複防止・方針すり合わせ）
2. **Fork** → 自分のリポジトリにクローン
3. **feature ブランチ作成**（`feature/<topic>` or `fix/<issue-id>`）
4. **コミット作成**（Conventional Commits 推奨）
   - `feat: 〇〇を追加`
   - `fix: 〇〇のバグを修正`
   - `docs: README 更新`
5. **テスト追加**（UI Automation testcase or 手動テスト手順）
6. **Pull Request 作成**（main ブランチへ）
7. **レビュー対応** → マージ

### main ブランチへの直接 push は不可

---

## 💻 開発環境

### 必須
- **Windows 10/11**（プロジェクトの動作対象）
- **.NET 10 SDK**（[ダウンロード](https://dotnet.microsoft.com/download/dotnet/10.0)）
- **PowerShell 5.1 以上**（UI Automation テスト用）

### 推奨
- **Visual Studio 2022** または **VS Code + C# Dev Kit**
- **Hyper-V**（VM テスト用・Windows Pro 以上）

### ビルド
```powershell
cd src\WinKnobs
dotnet build -c Release
```

### 実行
```powershell
dotnet run -c Release
```

### 単一 exe 化（リリース用）
```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
# 出力：bin\Release\net10.0-windows\win-x64\publish\WinKnobs.exe
```

---

## 🛡️ 必須実装ルール

WinKnobs は **「安全に Windows 設定を変更できる」** が最優先です。
新規設定項目（SettingItem）を追加する場合、以下が**必須**です：

### 1. Backup（バックアップ）実装
- 変更前の値を必ず取得し、`%APPDATA%\WinKnobs\backup\` に保存
- レジストリ系：`reg export` ベース
- コマンド系（fsutil / netsh 等）：JSON で値を記録

### 2. Restore（復元）実装
- バックアップから元の値に戻せること
- `CanRestore` プロパティで「戻せない」状態の場合は明示（実行系項目など）

### 3. 危険度マーキング
- `RiskLevel.Safe`（🟢 安全：いつでも戻せる）
- `RiskLevel.Caution`（🟡 注意：副作用あり）
- `RiskLevel.Advanced`（🔴 上級者向け：システム影響大）

### 4. 日本語説明文
- `Description`：何が変わるか・影響範囲
- `Hint`：推奨値・注意点

### 5. テスト
- 目視テスト or UI Automation testcase で動作確認

---

## 📝 コーディング規約

### C# 標準
- **.NET 標準のコーディング規則**に準拠（[公式ガイド](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions)）
- **PascalCase**：クラス・メソッド・プロパティ
- **camelCase**：ローカル変数・パラメータ
- **_camelCase**：private フィールド

### 日本語コメント OK
- 日本語コメント歓迎（むしろ推奨）
- ただし**公開 API（public）の XML ドキュメンテーション**は英語推奨

### 設計パターン
- **MVVM**（CommunityToolkit.Mvvm）
- **DI**（Microsoft.Extensions.DependencyInjection）
- **サービス層**（Services/ 配下にビジネスロジック）

### NG パターン
- `Thread.Sleep`（UI スレッドブロック）→ `Task.Delay` を使う
- `MessageBox.Show` 直接呼び出し → `IDialogService` 経由
- `Process.Start` 直接呼び出し → `IExternalLauncherService` 経由

---

## 🧪 テスト方針

### 単体テスト
- 現状未整備（v0.2 以降で MSTest 導入予定）

### UI Automation テスト
- `scripts/ui_automation/` 配下に PowerShell ベースのテストランナーあり
- `run_ui_test.ps1 -Mode Local -AutoLaunch` で実行
- 新規 SettingItem 追加時は **`testcases/tc_dialog_choices.ps1` に追加**

### VM 統合テスト
- Hyper-V VM（Win11 クリーン）でフル E2E
- `run_full_vm_test.ps1` を管理者で実行
- HKLM 系・実行系の項目は VM 必須

### 非公開ファイル
- `scripts/` 配下の VM 構築関連スクリプト（`create_vm.ps1` 等）は個人環境固有のため
  `.gitignore` で除外しています。汎用化前のため OSS には含まれません。

### 外部コントリビューター向けテスト方針
- **ホストモードでのテストのみ対応してください**：`scripts/ui_automation/run_ui_test.ps1 -Mode Local -Hidden`
- VM 統合テスト（`-Mode VM`）はメンテナがレビュー時に実施します
- HKLM 系・実行系の項目を変更する PR の場合、PR 説明に「VM テストは未実施」と明記してください
- メンテナが VM 環境で検証してからマージ判断します

---

## 🔒 セキュリティ

### 脆弱性報告
- [SECURITY.md](SECURITY.md) を参照
- **Issue では報告しないでください**
- [Security Advisory](https://github.com/non-x2/WinKnobs/security/advisories/new) で報告

### PR レビュー時の機密情報チェック
- パスワード・APIキー・トークンが含まれていないか
- 個人情報（ユーザー名・自宅パス）が含まれていないか
- ホスト固有パスがハードコードされていないか

---

## 📜 行動規範

このプロジェクトは [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md) を採用しています。
すべての参加者には、敬意ある建設的なコミュニケーションを期待しています。

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md).
All participants are expected to maintain respectful and constructive communication.

---

## 🙏 謝辞

WinKnobs は「いじくるツール」（高橋忍さん作）の哲学とユーザー体験から大きなインスピレーションを受けています。
完全独自実装ですが、先駆者への敬意を込めて開発しています。

WinKnobs is deeply inspired by Ijikuru Tool (by Shinobu Takahashi).
While it is a complete clean-room reimplementation, we maintain deep respect for the pioneer.

---

## 📬 質問

開発に関する質問は [GitHub Discussions](https://github.com/non-x2/WinKnobs/discussions) へお願いします。

For development questions, please use [GitHub Discussions](https://github.com/non-x2/WinKnobs/discussions).
