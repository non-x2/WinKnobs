# Changelog

All notable changes to **WinKnobs** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> 📝 **Note**: csproj 上の AssemblyVersion はメジャー・マイナー・パッチを反映します
> （例: v0.1.4-dev → AssemblyVersion 0.1.4.0）。UI ステータスバー表示は dev 接尾辞付きで表示されます。

---

## [Unreleased]

### Planning
- **v0.2.x 候補**（32 項目・[v0.2 候補項目集 v2](docs/v0.2_候補項目集_v2.md)）：
  - v0.2.0：Quick Win 5 項目（メモリ圧縮 / Power Throttling / Sysmain / 夜間モード / 透明効果）
  - v0.2.1：LDPlayer 解消・WSL・ゲーマー向け 6 項目
  - v0.2.2：IME / 音声 / Network 完成 6 項目
  - v0.3.0：Gaming カテゴリ + プリセット機能 14 項目
- **M-1 リファクタ**（v0.2 期間中漸進対応）：HKLM 3 択フォールバックのヘルパー化
- **Public 化前残作業**（[Public化前準備物 v1](docs/Public化前準備物_v1.md)）：v0.1.6 タグ / Releases / Sponsors / Dependabot / Discussions / Security Advisory / Branch Protection / Public 化

---

## [0.1.7] - 2026-06-03

### Added
- **ネットワーク カテゴリ新設（初心者層 + くわしい設定）**（[設計ドラフト](docs/v0.1.7_ネットワーク実用_設計ドラフト.md)・plan-validator / cross-reviewer 反映済・実装順は危険度の低い順 0→3→1→戻す→2→詳細層）
  - **機能0：Windows 純正画面ランチャー**（`ExternalLauncherService.OpenNetworkSettings`・`ms-settings:` / `ncpa.cpl` / 共有センター）＝管理者不要・退避不要の安全弁（抱え込まず純正へ逃がす）
  - **機能1：DNS 切替**（`NetworkService`・netsh・Cloudflare / Google / 自動(DHCP)・物理アダプタ判定は .NET NetworkInterface・退避はレジストリ NameServer・部分適用ロールバック・対象0枚は Info で案内）
  - **機能2：軽いリセット**（flushdns のみ・再起動不要）
  - **機能3：バックグラウンド帯域抑制**（配信最適化 `DODownloadMode` の取得 / 適用 / 復元・read-back・ポリシー検出）
  - **共通：ぜんぶ元に戻す**（`RevertAll`・適用前の値を `backup\network\originals.json` に自動退避・成功分のみ退避削除）
  - **詳細層：DNS 手入力**（IPv4 優先 / 代替・IPv6 のインライン TextBox + 適用ボタン）と **対象アダプタ個別指定**（CheckBox リスト・既定全選択＝従来の全件挙動）
  - アーキ：`NetworkService.ApplyDnsCore(targets, log)` を profile / custom / 個別の共通コアに集約（退避 read-modify-write のアダプタ単位 first-touch + 部分ロールバック）。`BackupService.SaveNetworkOriginal`（無条件上書き）新設。純粋関数 `MergeDnsBackups` / `BuildDnsServers`
  - `MainWindow.xaml`：Visibility を `IsNetworkCategorySelected` で 3 者択一に改修（既存 AutomationId 不変＝UIA 温存）。既存ネットワーク 2 項目（HTTP 同時接続数 / TCP Auto-Tuning）は「くわしい設定」に独立ビューで表示（項目定義は不変）
  - **テスト**：xUnit **84 PASS**（v0.1.6 の 42 + ネットワーク 42＝`NetworkServiceTests` の DNS 引数生成 / IP 検証 / 物理判定 34 + `MergeDnsBackups` / `BuildDnsServers` 8）
  - **VM-UIA**：`tc_network_smoke`（ネットワークペイン全要素の表示確認・新規 12 ブロック）+ 既存 smoke で **OK 41 / FAIL 0**（リグレッションなし・既存項目 Grid 2 件・ホーム復帰 OK・★2方式で非昇格自動実行）

### Notes
- バージョン bump：v0.1.6 → v0.1.7（csproj AssemblyVersion=0.1.7.0 / FileVersion=0.1.7.0 / MainWindow ステータスバー / MainViewModel 環境情報）
- 残（v0.1.8 候補）：**DoH ON/OFF**（暗号化 DNS・OS ビルド判定 + `netsh dns add encryption`・退避はレジストリ直読み）。UI プレースホルダ「（DoH ON/OFF は今後追加予定）」を残置済
- 実機 DNS 適用の往復は VM（仮想 NIC のみ）では検証不可のため、ロジックは xUnit で担保。のんさん実機での目視は任意

---

## [0.1.6] - 2026-06-01

### Added
- **v0.1.6 機能 C：スタートアップ snapshot 復元前プレビュー UI（3 ペイン）**（[設計ドラフト v2](docs/v0.1.6_候補設計ドラフト_v2.md) §C 実装・2026-05-31）
  - `Views/Startup/RestorePreviewDialog.xaml(.cs)` + `ViewModels/Startup/RestorePreviewViewModel.cs`（新規）：🗑 削除予定 / ✏ 変更予定 / ➕ 追加予定 を 3 ペイン（DataGrid・ダークテーマ適用・各ペイン AutomationId 付与）で表示し、「内容を確認しました」チェック後のみ復元ボタンを有効化
  - `StartupListViewModel.RestoreSnapshot()`：機能 B のテキスト Confirm をプレビューダイアログに置換（差分 0 件→案内して終了 / 解析失敗→既定いいえの ConfirmDestructive）。機能 A の整合性検証（差分算出の前）と import 直前の再 Verify は維持
  - `Services/StartupSnapshotDiffer.cs`：変更予定エントリに種別タグ（コマンド変更 / 有効⇔無効 / コマンド・状態変更）を付与（名前だけでは機能 B の件数警告と情報量が変わらないため）
  - `DialogService.OpenRestorePreview(path, diff)`（新規）：DI で VM/Dialog を構築・Init 後注入
  - **テスト**：`RestorePreviewViewModelTests`（チェック連動の OK enable・差分反映・OK/Cancel の 10 ケース）+ Differ 変更種別タグ 3 ケース追加（計 **42 PASS**）
  - plan-validator 反映：ListView→DataGrid（ダーク未定義による白地白文字を回避）/ Modified に変更種別タグ / DialogResult プロパティ方式に統一 / `NotifyPropertyChangedFor(CanRestore)`
  - cross-reviewer 反映：キャンセル時の StatusMessage 更新（重要1）/ Init の空振り通知を除去し順序契約をコメント化（重要2）。見送り項目は設計ドラフト §C 実装後レビューに記録
- **v0.1.6 機能 A：スタートアップ snapshot 改ざん対策（SHA256 + ACL）**（[設計ドラフト v2](docs/v0.1.6_候補設計ドラフト_v2.md) §A 実装・2026-05-31）
  - `Services/StartupSnapshotIntegrity.cs`（新規・純粋静的クラス）：SHA256 サイドカー（`<snapshot>.sha256`・sha256sum 互換）の生成・検証・ACL 保護。`ComputeSha256(Stream)` オーバーロードでファイル無しの単体テストも可能（機能 B の StartupSnapshotDiffer と同じ流儀）
  - `BackupService.BackupStartupSnapshot()`：snapshot 作成時に SHA256 サイドカーを自動生成（失敗しても snapshot 自体は止めない）。ファイル名をミリ秒精度（`_fff`）化し管理者版/非管理者版の同名衝突を回避
  - `BackupService.VerifyStartupSnapshotIntegrity(path)`（新規公開）：一致=true / 改ざん疑い or サイドカー破損=false / サイドカーなし（v0.1.5 以前）=null
  - `BackupService.CleanupOldStartupSnapshots()`：ローテ削除時に `.reg` と対の `.sha256` を個別削除（孤児化防止）。並びキーを LastWriteTime→ファイル名に統一
  - `StartupListViewModel.RestoreSnapshot()`：差分算出より前に整合性検証（改ざん検知で既定いいえの警告 → 中止）。import 直前に再検証して TOCTOU 窓を封鎖
  - `DialogService.ConfirmDestructive()`（新規）：既定ボタン＝いいえ・警告アイコン（破壊側に倒れてはいけない確認用）
  - ACL：app.manifest が asInvoker のため Owner は触らず DACL のみ操作（現ユーザー Modify + SYSTEM/管理者 Full + 継承遮断で他ユーザー上書き防止・`.sha256` のみ・失敗は無害）
  - **テスト**：`StartupSnapshotIntegrityTests`（既知ハッシュベクトル・改ざん検知・fail-safe・ファイル名欄無視・境界 の **17 ケース全 PASS**／機能 B 15 ケースと合わせ計 32 PASS）
  - plan-validator 反映：ACL 自己ロックアウト回避（現ユーザー Modify 維持）/ 同名衝突対策（ミリ秒）/ Verify を差分算出の前に配置 / テストは一時ディレクトリで %APPDATA% 非汚染
  - cross-reviewer 反映：整合性 Confirm を既定いいえ化（軽微1）/ import 直前の再 Verify で TOCTOU 封鎖（重要3）/ ローテ並びをファイル名基準に（軽微3）。見送り項目（ACL 孤児累積 / 健全 snapshot へのフォールバック導線 / エンコード正規化）は設計ドラフト §A 実装後レビューに記録
- **v0.1.6 機能 B：スタートアップ snapshot 復元前の差分件数警告**（[設計ドラフト v2](docs/v0.1.6_候補設計ドラフト_v2.md) §B 実装・2026-05-30）
  - `Services/StartupSnapshotDiffer.cs`（新規・純粋関数）：snapshot(.reg) と現在のレジストリ状態を比較し追加/削除/変更件数を算出（snapshot は読込のみ＝副作用なし）
  - `Models/StartupDiffResult.cs`（新規 record）：差分結果（件数 + エントリ内訳・将来の C プレビュー UI で再利用）
  - `StartupListViewModel.RestoreSnapshot()`：復元確認ダイアログに差分サマリ（追加N/削除N/変更N・10 件以上で大規模警告・解析失敗時は「差分なし」誤表示を回避）を表示
  - 設計改善：設計書の `BackupService.AnalyzeSnapshotDiff(path)` を現在エントリ引数の純粋関数化（I/O 層→VM 層の逆依存を回避・StartupApprovedCodec と同じ static 流儀）
  - 突合は `StartupItemsProvider.MergeApprovedState` と整合（RawName OrdinalIgnoreCase・StartupApproved\Run→HKLM_Run / Run32→Wow64_Run / HKCU→HKCU_Run・RunOnce 系と未登録は有効扱い）
  - **テスト基盤新設**：`tests/WinKnobs.Tests`（xUnit・本リポジトリ初の Unit test プロジェクト）+ `StartupSnapshotDifferTests`（差分 0/1/5/10+・REG_EXPAND_SZ・hex 継続行・削除マーカー・プレフィックス・Wow64・破損 reg の **15 ケース全 PASS**）
  - cross-reviewer 反映：算出失敗時の誤表示回避（重要1）/ プレフィックス・Wow64・Empty テスト追加（重要2）/ 同期実行コメント明示（重要3）/ 閾値 const 化（軽微）

### Notes
- バージョン bump：v0.1.5-dev → v0.1.6（csproj AssemblyVersion=0.1.6.0 / FileVersion=0.1.6.0 / MainWindow ステータスバー / MainViewModel 環境情報）
- 裏付け：xUnit **42 PASS**（B15 + A17 + C10）/ build 0 警告 0 エラー / VM-UIA **65 OK・0 FAIL**（リグレッションなし）/ plan-validator・cross-reviewer 致命 0
- 残（任意）：VM 実機目視（A 改ざん警告 / C プレビュー UI）。Public 化前残作業は Unreleased の Planning を参照

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
- [GitHub Releases](https://github.com/non-x2/WinKnobs/releases)：バイナリ配布（リリース後）
