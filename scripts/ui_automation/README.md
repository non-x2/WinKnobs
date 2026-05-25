# WinKnobs UI Automation スクリプト群

> **作成日**：2026-05-20（Phase B-1 基盤実装）
> **最終更新**：2026-05-20（Phase B-2 統合ランナー完成）
> **対象バージョン**：WinKnobs v0.1.4-dev 以降
> **計画書**：`docs/UI_Automation_対応計画書_第1版.md`

## このフォルダの役割

WinKnobs を Windows UI Automation（UIA）で自動操作するスクリプト群。
ホスト直接実行（`-Mode Local`）と VM 経由実行（`-Mode VM`）の両方をサポート。

## フォルダ構成

```
scripts/ui_automation/
├── run_ui_test.ps1                ★統合ランナー（Phase B-2）
├── lib/
│   └── uia_common.ps1             共通モジュール（Find/Invoke/Expand/Screenshot ほか）
├── testcases/
│   ├── tc_smoke.ps1               UIA 基盤動作確認＋主要 ID 検出
│   ├── tc_dialog_open.ps1         カテゴリ→項目→ダイアログ起動の基本フロー（3 項目）
│   ├── tc_visual_ui.ps1           UI 表示・検索・テーマ（T-UI-1〜5＋T-V2-TH-1〜3）
│   ├── tc_dialog_choices.ps1      ダイアログ内 ComboBox 選択肢数の網羅確認（12 項目）
│   ├── tc_dialog_inputs.ps1       入力系項目の動作確認（T-W3 数値・T-W8 Binary）
│   ├── tc_destructive_actions.ps1 T-V2-S1/S2 実行系（アイコンキャッシュ・BagMRU・適用なし）
│   ├── tc_bug1_regression.ps1     BUG-1（バックアップ失敗）リグレッション
│   ├── tc_bug2_regression.ps1     BUG-2（K1 4 択拡張）リグレッション
│   ├── tc_apply_registry.ps1      ⚠️VM専用：T-REG-1/3/4 HKCU 書込テスト
│   ├── tc_apply_fsutil.ps1        ⚠️VM専用：T-FSU-2/3 fsutil 設定変更テスト
│   └── tc_apply_v2.ps1            ⚠️VM専用：T-V2-APPLY-PR1/PR2/PR3/N1 HKLM/netsh テスト
├── results/                       JSON 結果＋ログ出力先
├── screenshots/                   FAIL 時の自動スクショ出力先
├── 0_check_uia_available.ps1      旧 Phase B-1 雛形（参考用・互換維持）
├── 1_select_category_and_item.ps1 旧 Phase B-1 雛形（参考用・互換維持）
├── 2_apply_pr1_and_verify.ps1     旧 Phase B-1 雛形（参考用・互換維持）
└── README.md                      本ファイル
```

## 🌟 推奨運用：ユーザー作業中は VM で（2026-05-21〜）

**ユーザーがデスクトップで作業中の時は、UIA テストは VM で実行**してください。
ホスト実行はマウス・画面が一瞬奪われる場面がどうしても残ります。

```powershell
# ★推奨：VM フル統合（管理者 PowerShell・vm_test_full → -Mode VM の連鎖）
C:\AI\Claudo\アプリ\WinKnobs\scripts\run_full_vm_test.ps1

# ショートカット：
#   WinKnobs_25_VMフル統合UIテスト（管理者）.lnk        ← 既定セット
#   WinKnobs_26_VMフル統合UIテスト＋適用（管理者）.lnk ← apply 系も含む
```

## クイックスタート（ホスト実行・開発デバッグ用）

ホストモードは **開発中の即時デバッグ用** と位置づけ。
`-Force` か `-Hidden` 指定がないとガードで停止します。

```powershell
cd C:\AI\Claudo\アプリ\WinKnobs\scripts\ui_automation

# ✅ ホスト隠しモード（推奨・ユーザー作業中も画面奪取なし）
.\run_ui_test.ps1 -Mode Local -AutoLaunch -Hidden

# 特定テストケースのみ
.\run_ui_test.ps1 -Mode Local -TestCase smoke -AutoLaunch -Hidden

# 複数指定
.\run_ui_test.ps1 -Mode Local -TestCase smoke,dialog_open -AutoLaunch -Hidden

# ⚠️ 強制前面実行（デモ・スクショ確認用・ユーザーに見せたい時）
.\run_ui_test.ps1 -Mode Local -AutoLaunch -Force

# 結果ファイル名指定
.\run_ui_test.ps1 -Mode Local -ResultJson results/my_test.json -AutoLaunch -Hidden
```

> **ガード仕様**：`-Mode Local` で `-Hidden` も `-Force` も無い実行は exit 2 で停止。CLAUDE.md「WinKnobs UI Automation テストは VM 優先」ルールに準拠。

## 引数リファレンス

| 引数 | 値 | 説明 |
|---|---|---|
| `-Mode` | `Local` / `VM` | 実行モード（VM は Phase B-3 で拡張予定） |
| `-TestCase` | カンマ区切り | `smoke` / `dialog_open` / `bug1_regression` / `bug2_regression` |
| `-ResultJson` | パス | JSON 結果出力先（未指定なら results/result_yyyyMMdd_HHmmss.json） |
| `-LaunchTimeoutSec` | 秒数 | ウィンドウ取得タイムアウト（既定 15） |
| `-AutoLaunch` | スイッチ | 既存 WinKnobs プロセスを完全 Kill してから 1 プロセス起動（複数プロセス問題対策） |
| `-Hidden` | スイッチ | 🆕 WinKnobs を画面外 (-2000, -2000) に移動して邪魔にならない状態でテスト実行（ユーザー要望対応） |

## テストケース別の特徴

| TC 名 | 検証内容 | 副作用 | ホスト実行可 | VM 推奨 |
|---|---|---|---|---|
| `smoke` | 主要 ID 検出＋メニュー展開 | なし | ✅ | ✅ |
| `dialog_open` | カテゴリ→項目→ダイアログ起動→閉じる（3 項目） | なし | ✅ | ✅ |
| `visual_ui` | カテゴリ数＋検索動作＋テーマメニュー（T-UI-1〜5＋T-V2-TH-1〜3） | 検索入力（テスト後クリア） | ✅ | ✅ |
| `dialog_choices` | 12 項目のダイアログを開いて ComboBox 選択肢数を確認（W1/W2/W4/W6/PR1/PR2/PR3/N1/P1/K1/K2/L2） | なし（Cancel で閉じ） | ✅ | ✅ |
| `dialog_inputs` | 入力系項目（T-W3 MenuShowDelay 数値検証・T-W8 Link Binary 4byte 形式） | なし（適用押さず・Cancel 閉じ） | ✅ | ✅ |
| `destructive_actions` | T-V2-S1/S2 実行系項目のダイアログ動作確認＋ RestoreButton 無効化検証 | なし（適用押さず） | ✅ | ✅ |
| `bug1_regression` | PR1 適用＋バックアップ失敗ダイアログ無し検証 | **HKCU 書込発生** | ⚠️ | ✅ 推奨 |
| `bug2_regression` | K1 4 択拡張の確認 | なし | ✅ | ✅ |
| `apply_registry` | T-REG-1/3/4：W1/W6/W5 の HKCU 書込→検証→戻し | **HKCU 書込発生** | ⚠️非推奨 | ✅ |
| `apply_fsutil` | T-FSU-2/3：K1/K2 の fsutil 設定変更→検証→戻し | **fsutil 設定変更・管理者必須** | ❌ | ✅ |
| `apply_v2` | T-V2-APPLY-PR1/PR2/PR3/N1：HKLM 書込＋netsh 変更 | **HKLM 書込＋netsh 変更・管理者必須** | ❌ | ✅ |

> **bug1_regression は実適用が発生**します。ホスト直接実行はテスト用クリーン環境または VM で。

## ホスト E2E 検証結果（2026-05-20）

### Phase B-2 初回検証
```
✅ smoke           : OK=18  FAIL=0  INFO=1
✅ dialog_open     : OK=27  FAIL=0  INFO=0  (3 項目 × 9 チェック)
✅ bug2_regression : OK=2   FAIL=0  INFO=4  (4 択全選択肢表示)
Total: 52 / FAIL: 0  Duration: 29.78 sec
```

### Phase B-3 拡張テスト追加後
```
✅ visual_ui       : OK=7   FAIL=0  INFO=2  (T-UI-1〜5＋T-V2-TH-1〜3)
✅ dialog_choices  : OK=12  FAIL=0  INFO=36 (12 項目の選択肢数を全件期待値ぴったり一致)
Total: 19 / FAIL: 0  Duration: 65.64 sec
```

全テスト合計：**71 件 OK / 0 件 FAIL**（既存目視テスト 13 項目中 12 項目以上を自動化）

## 既存スクリプトとの関係

`scripts/run_uia_tests.ps1`（2026-05-18 作成・統合前）：
- T-0/T-2-b/T-2-e/T-3/T-9 の統合軽量テスト
- 副作用なし・VM 専用設計
- **継続利用**：併用OK

旧 Phase B-1 雛形（`0_check_uia_available.ps1` / `1_select_category_and_item.ps1` / `2_apply_pr1_and_verify.ps1`）：
- Phase B-2 完成で `run_ui_test.ps1` に統合
- 互換維持のため残置・新規開発は run_ui_test.ps1 推奨

## AutomationId 命名規則（Phase B-1 確定版）

| 種別 | 命名 | 例 |
|---|---|---|
| カテゴリ TreeViewItem | `Category_<CategoryType>` | `Category_Privacy` |
| 項目 DataGridRow | `Item_<SettingId>` | `Item_privacy.cortanaDisabled` |
| 親メニュー | `Menu<Name>` | `MenuFile` / `MenuEdit` / `MenuView` / `MenuTools` / `MenuHelp` |
| 静的メニュー項目 | `Menu<Action>` | `MenuOpenItem` / `MenuExitApp` / `MenuRefresh` |
| ダイアログボタン | `<Action>Button` | `OkButton` / `ApplyButton` / `CancelButton` |
| ダイアログ入力 | `Editable<Type>` | `EditableBoolCombo` / `EditableCommandCombo` / `EditableValueBox` |

## 共通モジュール API（lib/uia_common.ps1）

### 検索系
- `Find-ById -Root <elem> -Id <string> [-TimeoutSeconds 5] [-Scope Descendants/Children]`
- `Find-ByName -Root <elem> -Name <string> [-TimeoutSeconds 5]`
- `Wait-WindowByName -Name <string> [-TimeoutSeconds 15]`

### 操作系
- `Invoke-Element -Element <elem>` → bool
- `Select-Element -Element <elem>` → bool
- `Expand-Element / Collapse-Element` → bool
- `Set-ElementValue / Get-ElementValue / Get-ElementName`

### 複合操作
- `Select-CategoryAndItem -Win <win> -CategoryType <str> -ItemId <str>`
- `Open-DialogViaMenu -Win <win>`（メニュー経由でダイアログ起動・自動リトライ付き）
- `Close-DialogByCancel`
- `Test-DialogExists -Name <str>`

### スクリーンショット
- `Take-Screenshot -Path <path>`（ローカルファイル保存）
- `Take-ScreenshotBase64`（base64 文字列で返却・VM → ホスト転送用）
- `Save-ScreenshotFromBase64 -Base64 <str> -Path <path>`

### 結果オブジェクト
- `New-TestResult -Id <str> -Status <OK/FAIL/SKIP/INFO> -Detail <str>`

## トラブルシュート

### WinKnobs ウィンドウが見つからない
- `-AutoLaunch` スイッチを付けて再実行
- exe パスを確認：`bin\Release\net10.0-windows\WinKnobs.exe`
- 先に `dotnet build -c Release` を実行

### メニュー配下が検出されない
- WPF 仕様：MenuItem 配下は親メニュー Expand 後にしか UIA ツリーに出ない
- `Open-DialogViaMenu` には自動 Expand → Collapse → 再 Expand のリトライ機構あり

### 構文エラー「Array index expression is missing or not valid」
- PowerShell 5.1 が BOM 無しファイルを Shift-JIS で解釈→日本語文字が壊れて構文崩壊
- 対策：`.ps1` ファイルは必ず UTF-8 BOM 付きで保存
- 一括 BOM 付与コマンド：
  ```powershell
  Get-ChildItem -Filter *.ps1 -Recurse | ForEach-Object {
      $c = Get-Content -Raw -Encoding UTF8 $_.FullName
      [System.IO.File]::WriteAllText($_.FullName, $c, [System.Text.UTF8Encoding]::new($true))
  }
  ```

## Phase B-3 への引き継ぎ

Phase B-3（既存テスト自動化）で追加予定：
- VM モード（`-Mode VM`）の実装：PowerShell Direct + Invoke-Command + base64 スクショ転送
- 既存 T-W3/W8/PR1/PR2/S2 等 13 項目のテストケース化
- 適用テスト 9 項目のテストケース化（PowerShell Direct 経由・現在の `apply_test_auto.ps1` を移植）
- CI 統合に向けた exit code とサマリ JSON の標準化

## 履歴

- **2026-05-20 Phase B-1**：AutomationId 付与＋雛形 3 本＋ README
- **2026-05-20 Phase B-2**：統合ランナー＋共通モジュール＋テストケース 4 種＋ JSON 出力＋スクショ自動撮影
- **2026-05-20 Phase B-3 部分**：VM モード実装＋apply_test_auto N1 修正＋visual_ui/dialog_choices テストケース追加（既存目視 13 項目中 12 項目を自動化）
- **2026-05-20 Phase B-3 完了**：apply_registry/apply_fsutil/apply_v2 の VM 専用テストケース追加（既存適用 9 項目を完全移植）。共通ヘルパー（Read/Write/Restore-RegValue、Get-AutoTuningLevel）を uia_common.ps1 に集約
- **2026-05-20 Phase B-3 仕上げ**：tc_dialog_inputs.ps1（T-W3 数値検証＋T-W8 Binary 4byte 確認）追加・run_full_vm_test.ps1（vm_test_full → run_ui_test -Mode VM の連鎖統合スクリプト）追加
