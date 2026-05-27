# Changelog

All notable changes to **WinKnobs** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> 📝 **Note**: csproj 上の AssemblyVersion はメジャー・マイナー・パッチを反映します
> （例: v0.1.4-dev → AssemblyVersion 0.1.4.0）。UI ステータスバー表示は dev 接尾辞付きで表示されます。

---

## [Unreleased]

### Notes
- 次の候補：snapshot 改ざん対策（SHA256 / 復元前プレビュー）/ HKLM 3 択フォールバックのヘルパー化（M-1 リファクタ）/ 世代またぎ復元の差分件数警告 / v0.2 候補項目

---

## [0.1.5-dev] - 2026-05-27

### Added
- **Phase R-3：スタートアップ系の追加・削除・snapshot 完成**
  - `StartupItemsProvider.Delete()`：Run キーと StartupApproved 両方からエントリ削除（RunOnce 系は StartupApproved スキップ・best-effort）
  - `StartupItemsProvider.AddNew()`：新規 Run 値書込 + 同一ハイブ名重複ブロック + Name 長さバリデーション（255 文字上限）
  - `BackupService.BackupStartupSnapshot()`：全 6 Run ハイブ + 3 StartupApproved を 1 つの統合 .reg にエクスポート
  - `BackupService.GetLatestStartupSnapshotPath()` / `ListStartupSnapshots()`：snapshot 一覧 / 最新パス取得
  - StartupListViewModel に `AddCommand` / `DeleteCommand` / `RestoreSnapshotCommand` 追加
  - StartupListView：「+ 追加」「🗑 削除」を enable + 新規「🔄 復元」ボタン追加（AutomationId 整備）
  - StartupEntryEditViewModel：`IsNewMode` / `EditableName` / `SelectableHives` / `SelectedHive` / `LoadNew()` / `CreateNewEntry()` で Add モード対応
  - StartupEntryEditDialog：Hive ComboBox / Name TextBox を Visibility 切替で Add/Edit モード分岐
  - UI Automation テスト 3 件追加：`tc_startup_add.ps1` / `tc_startup_delete.ps1` / `tc_startup_admin.ps1`（GUID 8 文字でユニーク化・try/finally cleanup）
- **Phase R-2：スタートアップ系の編集・トグル・楽観ロック完成**（commit 75d0c75）
  - CheckBox 即時トグル + 編集ダイアログ + 保存ボタン + 楽観ロック
  - StartupEntryEditViewModel / StartupEntryEditDialog 新規実装
  - 致命 6 件 + 依存 UX バグの対応反映（HKLM_Wow64 View 統一・列ヘッダ「即時」明示・SaveDirty ループ内 try-catch・Original 保持と RevertChanges・CanToggleEnabled で IsDirty 中 disable）
  - UI Automation テスト 2 件追加（tc_startup_smoke / tc_startup_edit）
- **R-2 hotfix 3 件**（commit 5391ec6）
  - IsDirty 残留バグ（無変更 OK で IsDirty=true 残留）→ StartupEntryEditViewModel.Ok() で IsModified=true 時のみ書戻し
  - 楽観ロック比較対象バグ → StartupItemsProvider.Save() の比較対象を `entry.Command` → `entry.OriginalCommand`
  - StartupListViewModel.Edit() で IsDirty 連動ステータス文言
- **UIA × WPF CheckBox 制約調査**（commit 6731b66）
  - 真因確定：WPF CheckBox は UIA `InvokePattern` 非実装・`TogglePattern.Toggle` は WPF Command 非発火
  - 本体コード問題なし（実機マウスクリックでは PASS）
  - tc_startup_edit.ps1 の toggle 3 件を INFO 化（誤検知の塗り重ね回避）
  - run_ui_test.ps1 に `LimitationsFlagged` カウンタ追加（CI 緑化副作用防止）
- **OSS化 Week 3 完遂**：CONTRIBUTING.md / SECURITY.md / CODE_OF_CONDUCT.md / CHANGELOG.md / .github/ ディレクトリ整備
- バージョン bump：v0.1.4-dev → v0.1.5-dev（csproj AssemblyVersion=0.1.5.0 / MainWindow ステータスバー / MainViewModel 環境情報）

### Changed
- BackupService にスタートアップ snapshot 専用サブディレクトリ `%APPDATA%\WinKnobs\backup\startup\` + 独自ローテーション 10 件（既存 28 項目バックアップの 10 件枠と完全分離）
- EnsureSnapshot に 10 秒スロットル（連続 Modify で reg.exe 9 連発による UI 固まり回避）
- StartupListViewModel コンストラクタに BackupService 追加（DI）
- DialogService.OpenStartupAdd 追加（Name 重複チェックコールバック受領）

### Fixed
- plan-validator 致命高1：snapshot 専用サブディレクトリ分離で個別バックアップ瞬殺リスク解消
- plan-validator 致命高2：RestoreSnapshot 前に IsDirty 中エントリ Confirm（編集中作業消失防止）
- cross-reviewer 致命高1：Add Name 重複をダイアログ内リアルタイム警告化（同一ハイブ重複は IsValid=false で OK 無効化・他ハイブ重複は StatusMessage 警告）
- cross-reviewer 致命高2：BackupStartupSnapshot の空 section / 無効 section 混入を検証強化（`[HKEY_` / `[-HKEY_` 含有 + section 数チェック）
- cross-reviewer 致命中4：EnsureSnapshot 10 秒スロットルで連続 Modify の体感固まり回避
- cross-reviewer 致命中7：tc_startup_delete の「はい」「Yes」ボタン検出をパターンマッチ強化（Windows 11 24H2 アクセスキー OFF 対応）

### Security
- パスワード生平文・VM 認証情報・ホスト固有ログ・引き継ぎメモを .gitignore 除外（OSS化 Week 3）

---

## [0.1.4-dev_fix] - 2026-05-20

### Fixed
- **旧プロセス残存バグ**：管理者として再起動後、旧プロセス（非管理者）が即座に終了しないケース対応
  - `PrivilegeService` に `Environment.Exit(0)` 1500ms フォールバック追加
  - `vm_test_full.ps1` のプロセス監視ロジック堅牢化

### Added
- VM 統合テスト第3波 PASS（PID 3248 / Memory 93.1MB）

---

## [0.1.4-dev] - 2026-05-20

### Added
- **BUG-3 対応：HKLM 書込時の管理者権限要求**
  - 新規 `PrivilegeService` クラス（IsRunningAsAdministrator / TryRestartAsAdministrator / ShutdownCurrentProcess / IsShutdownPending）
  - `DialogService.ConfirmElevation` 3 択ダイアログ（管理者として再起動 / このまま続行 / キャンセル）
  - `SettingDialogViewModel` の DoApply / RestoreSetting / LoadSetting で `UnauthorizedAccessException` / `SecurityException` / `InvalidOperationException` を捕捉して管理者再起動を誘導
- VM ステータスバー `v0.1.4-dev` 表示確認 PASS
- 標準権限 → 3 択ダイアログ → UAC → 管理者再起動フロー PASS
- 管理者起動後 PR1 適用成功 PASS

### Changed
- `using System.Security.Principal;` 削除（PrivilegeService に統一）
- `IsAdministrator()` private メソッド削除（DI 経由に統一）

### Reviewed
- @plan-validator 8 件 + エッジケース 6 件指摘 → 全反映
- @cross-reviewer M-2（LoadSetting への適用）+ U-1（ダイアログ文言改善）反映

---

## [0.1.3-dev] - 2026-05-18

### Fixed
- **BUG-1**：レジストリキー不在時のバックアップ失敗
  - `BackupService.ExportKey` にキー存在チェック追加
  - 不在時は `[-FullKeyPath]` 記法のマーカー .reg を生成
- **BUG-2**：K1（NTFS 8.3 名）が 2 択固定だったため、Windows 既定値 2 や 3 を扱えなかった問題
  - 4 択（0/1/2/3）に拡張
  - `BoolBackup` → `IntBackup` パターンに統一（K2 と同じパターン）
  - `ParseDisable8dot3Index` ヘルパー切り出し（英/日両環境対応）

### Discovered
- **BUG-3**：HKLM 書込時の Access denied → v0.1.4 で対応

---

## [0.1.2-dev] - 2026-05-17

### Fixed
- **N1（TCP Auto-Tuning）パースバグ**：netsh 出力の値マッチで他項目（ECN Capability）の値を誤拾い
  - `ParseAutoTuningFromNetsh` ヘルパーに切り出し
  - 行毎に分割 → "Auto-Tuning" を含む行に絞り込み → コロン後抽出

### Changed
- バージョン文字列を `v0.1.0-dev` から `v0.1.2-dev` に更新（MainViewModel.cs / MainWindow.xaml）

---

## [0.1.2] - 2026-05-16

### Added
- **プライバシー系 3 項目**：
  - PR1 Cortana 無効化（HKLM Policies）
  - PR2 Activity History 無効化
  - PR3 SmartScreen 動作モード（3択 Command）
- **ネットワーク系 1 項目**：
  - N1 TCP Auto-Tuning（5択 netsh CommandAction）
- **シェル系 2 項目**：
  - S1 アイコンキャッシュクリア（実行系・CanRestore=false）
  - S2 BagMRU クリア（実行系・CanRestore=false）
- テーマメニューに ✓ チェックマーク連動

### Changed
- 実装済み設定項目：22 → **28 項目**

---

## [0.1.1] - 2026-05-16

### Added
- **基礎カテゴリ 6 項目**：
  - L1 Fast Startup（HKLM）
  - L2 Prefetcher（4 択 Command）
  - K1 NTFS 8.3 名（fsutil CommandAction）
  - K2 NTFS LastAccess（fsutil CommandAction）
  - P1 Win32PrioritySeparation（3 択 Command）
  - P2 SystemResponsiveness

### Changed
- 実装済み設定項目：16 → **22 項目**

---

## [0.1.0] - 2026-05-15

### Added
- **初版リリース**：MVVM / DI / WPF ベースの基盤完成
- **基礎 9 項目** migration 完了（A3-2-c 型拡張）：
  - 既存項目を新 RestartScope（None / Explorer / Logoff / System）に migration
- **Win11 価値項目 W1-W6, W8（7 項目）**：
  - W1/W2 ダークモード（アプリ / システム）
  - W3 MenuShowDelay（数値入力）
  - W4 タスクバーサイズ（3 択 Command）
  - W5 クラシック右クリック（2 択 Command）
  - W6 Bing 検索無効化
  - W8 Link Binary（4 byte）
- **A3-2-c 型拡張**：
  - `RestartScope` enum（None / Explorer / Logoff / System）
  - `CommandAction` クラス（Read / Apply / Backup / Restore / Choices + CanRestore）
  - `SettingValueType` に Binary / Command 追加
  - `SettingItem` 拡張（RestartScope / Command / ExpectedByteLength / NumericStringRange）
- **インライン編集 UX**：
  - 「現在値（読込専用）」+「新しい値（編集可）」2 行 → 「値（クリックで変更）」1 行に統合
  - `OriginalValue / EditableValue / IsModified` 三者同期
  - 「戻す」3 段階（編集破棄 / 適用後復元 / 対象なし）
  - 編集中オレンジ枠 / 適用済み薄緑枠
- **ダーク UI 第 3-6 波修正**：
  - ComboBox / Button / MenuItem ControlTemplate 完全置換
  - チェックマーク復活・位置固定

### Reviewed
- @plan-validator V-1 〜 V-9 / VE-1 〜 VE-6 すべて反映
- @cross-reviewer 致命度高 5 件 + 中度 U-3 / M-2 反映

### Tested
- VM（Hyper-V Win11 クリーン）で T-1 〜 T-10 実機テスト PASS

---

## [0.0.x] - 2026-05-09 〜 2026-05-12

### Added
- Phase 0：プロジェクト準備（README / 開発計画 / 項目リスト / Hyper-V ガイド）
- Phase 1：WPF 骨格・MVVM・サービス層・テーマ・サブダイアログ
- Phase 1-3：仮想 Win11 で T-1 〜 T-10 実機テスト（7 件成功）

---

## バージョン番号方針 / Versioning Policy

- **Major (X.0.0)**：破壊的変更・データ移行が必要な場合
- **Minor (0.X.0)**：機能追加・後方互換維持
- **Patch (0.0.X)**：バグ修正・後方互換維持
- **-dev 接尾辞**：開発中の不安定版（CI/CD 未通過）
- **_fix 接尾辞**：dev 版に対する緊急修正（dev_fix）

### ⚠️ SemVer 自動化ツールとの互換性

`_fix` サフィックスは **SemVer 厳密準拠ではない**ため、`semantic-release` / `github-release-drafter`
等の自動化ツール導入時には注意が必要です。

正式リリース（v1.0.0 以降）では `_fix` サフィックスを使わず、通常の patch increment
（v1.0.1 → v1.0.2）で対応します。dev 期間中の暫定運用としてのみ使用しています。

---

## 関連リンク / Related Links

- [README.md](README.md)：プロジェクト概要
- [CONTRIBUTING.md](CONTRIBUTING.md)：貢献ガイド
- [SECURITY.md](SECURITY.md)：脆弱性報告
- [GitHub Releases](https://github.com/nonlabo/WinKnobs/releases)：バイナリ配布（リリース後）
