# UI Automation × WPF CheckBox 制約調査 / 2026-05-27

> セッション：AI代行会社 秘書さん経由（本日 3 回目）
> 起因：`tc_startup_edit.ps1` の `startup_edit.toggle.verify_byte` 長期 FAIL の原因解明
> 関連：次セッション再開ガイド L84-87（旧称「StartupApproved 自動作成パス調査」）

---

## 1. 要約（結論先取り）

| 項目 | 結論 |
|---|---|
| 本体コードへの影響 | **なし**（実機マウスクリックでは Command 発火し SetEnabled → StartupApproved 書込まで PASS） |
| UIA テストへの影響 | toggle 系 3 件は **構造的に UIA Pattern では検証不可** → INFO 化で対応 |
| 旧「StartupApproved 自動作成パス調査」との関係 | **同一事象を別角度から観察したもの**。本ファイルが解明 |
| Phase R-3 着手判定 | 影響なし（VM/ホスト 実機検証 16/16 PASS済・GO 維持） |

---

## 2. 調査経緯

### 仮説 1（出発点・実は誤り）
> 「ホスト環境で CheckBox トグル時に `HKCU\...\Explorer\StartupApproved\Run\WinKnobs_Test` が作成されない。StartupApproved\Run キー自体の自動作成パスがホスト環境で効いていない」

→ 次セッション再開ガイド L84-87 の「後続調査」項目はこの推定に基づく。

### 検証 1（仮説 1 を否定）
**実機 PowerShell 確認**：
```powershell
Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'  # → True
(Get-ItemProperty $path).Count  # → 10 件のエントリ既存
```

ホスト環境で StartupApproved\Run キーは **既に存在し 10 件のエントリ**（LINE / Discord / Steam / Chrome / Edge 等）が登録済。`StartupItemsProvider.SetEnabled` 内部の `CreateSubKey` は **既存キーなら開くだけ**で正常動作するはずなのに、UIA テストでは書込が発生していない。

→ **キー自体の問題ではない**。

### 仮説 2（新仮説）
> 「UIA `InvokePattern.Invoke()` が WPF DataGrid 内 CheckBox の Command を発火していない」

### 検証 2（仮説 2 を確定）

実機 ad-hoc PowerShell `scripts/ui_automation/debug_checkbox_invoke.ps1` で各 UIA Pattern の挙動を直接観察：

```
===== Phase 6: 利用可能パターン確認 =====
  Pattern: TogglePatternIdentifiers.Pattern
  Pattern: SynchronizedInputPatternIdentifiers.Pattern

===== Phase 8a: InvokePattern.Invoke 試行 =====
InvokePattern.Invoke() FAILED: Exception calling "GetCurrentPattern" with "1" argument(s): "Unsupported Pattern."

===== Phase 8b: LegacyIAccessiblePattern.DoDefaultAction() 試行 =====
LegacyIAccessiblePattern.DoDefaultAction() FAILED: Unable to find type [System.Windows.Automation.LegacyIAccessiblePattern].
```

**確定事項**：
- WPF CheckBox は `TogglePattern` と `SynchronizedInputPattern` のみ実装
- `InvokePattern` は WPF 標準仕様で **未実装**
- `LegacyIAccessiblePattern` は .NET Framework `System.Windows.Automation` アセンブリに型自体が **含まれない**

→ UIA Pattern 経由で CheckBox の `Command (ToggleEnabledCommand)` を発火させる**正攻法がない**。

---

## 3. 各経路の挙動（決定的整理）

| 経路 | Command 発火 | IsChecked 反転 | StartupApproved 書込 |
|---|:---:|:---:|:---:|
| 🖱 マウスクリック（ButtonBase.OnClick）| ✅ | ✅ | ✅ |
| 🖱 VM 内手動操作（vmconnect 経由）| ✅ | ✅ | ✅（vm_r2_e2e_result.md 3-1〜3-3）|
| 🤖 `InvokePattern.Invoke()` | -（Unsupported Pattern）| - | - |
| 🤖 `TogglePattern.Toggle()` | ❌ | ✅ | ❌ |
| 🤖 `LegacyIAccessiblePattern.DoDefaultAction()` | -（型未定義）| - | - |

`tc_startup_edit.ps1` の Toggle 経路はフォールバックで `TogglePattern.Toggle()` を呼ぶ。
これは **IsChecked のみ反転** し WPF Command を発火させない。結果として：
- `toggle.click` PASS（ToggleState 反転は記録）→ 表面上 OK だが内部で SetEnabled は呼ばれていない誤検知
- `toggle.verify_byte` FAIL（書込発生せず）→ 構造的に不可避

---

## 4. 採用した対応（INFO 化）

`tc_startup_edit.ps1` の toggle 系を以下のように変更：

| TestId | 旧 | 新 | Detail |
|---|---|---|---|
| `startup_edit.toggle.uia_limitation` | （存在せず）| **INFO**（新規）| 制約の存在と対応根拠を必ず履歴に記録 |
| `startup_edit.toggle.click` | OK（誤検知）| **INFO** | ToggleState 遷移のみ・Command 発火は未検証 |
| `startup_edit.toggle.verify_byte` | FAIL（構造制約）| **INFO** | UIA Pattern では書込発生せず・実機マウスは PASS |
| `startup_edit.toggle.revert` | OK（誤検知）| **INFO** | ToggleState 遷移のみ・Command 発火は未検証 |

加えて `run_ui_test.ps1` の集計に `LimitationsFlagged` カウンタを追加し、INFO に `limitation` タグが含まれる場合は警告ヘッダを表示（CI 緑化副作用の防止・plan-validator 指摘 1 対応）。

---

## 5. 将来課題（v0.2.x 候補）

**マウス座標経由クリックの導入**：

UIA Pattern では発火できない CheckBox 操作を、実機ユーザーと同じ「マウスクリック」で発火する方法。具体的選択肢：

| 案 | 内容 | 工数 |
|---|---|---|
| **A. mouse_event API** | `[user32]::mouse_event` で `GetClickablePoint()` の座標に LeftDown→LeftUp 送信 | 30〜60分 |
| **B. SendInput API** | `INPUT_MOUSE` 構造体で同等の操作（mouse_event の後継推奨 API） | 45〜90分 |
| **C. FlaUI ライブラリ導入** | NuGet `FlaUI.UIA3` で `Click()` 抽象化（ライブラリは内部で SendInput を呼ぶ） | 1〜2時間 + 依存追加 |

→ 詳細は `docs/UI_Automation_対応計画書_第1版.md` 末尾「将来課題」セクション参照。

---

## 6. 旧「StartupApproved 自動作成パス調査」との対応

次セッション再開ガイドの旧記載（L84-87）：
> [後続] StartupApproved 自動作成パス調査 | CheckBox トグル時に HKCU\\...\\StartupApproved\\Run キーがホストで作成されない（VMでは PASS済）。R-3 内 or R-3 前に調査 | ⏸ 新発見・要調査

**本ファイルが解明**：
- 観察した症状（「ホストで CheckBox トグル時に StartupApproved\Run\WinKnobs_Test が作成されない」）は事実
- しかし真因は **UIA テスト経路で SetEnabled が呼ばれていなかった**（UIA Pattern 構造制約）
- 「自動作成パスが効いていない」ではなく「自動作成パスを呼ぶ Command が UIA Pattern では発火できない」

→ 次セッション再開ガイドの該当箇所を本ファイルへのリンクと共に「✅ 解明済（2026-05-27）」に更新。

---

## 7. 本体コードへの影響評価

| 観点 | 結論 |
|---|---|
| `StartupItemsProvider.SetEnabled` | ✅ 正常（CreateSubKey + SetValue は仕様通り動作）|
| `StartupApprovedCodec.Build` | ✅ 正常（12 byte 構築は仕様通り）|
| `StartupListViewModel.ToggleEnabled` | ✅ 正常（マウスクリック経由で Command 確実発火）|
| `StartupListView.xaml` の CheckBox バインド | ✅ 正常（IsChecked OneWay + Command バインドは WPF 標準パターン）|

**本体コードの修正は不要**。R-3 着手 GO 判定維持。

---

## 8. 参考資料

### 公式仕様
- [WPF CheckBox UI Automation Support (Microsoft Docs)](https://learn.microsoft.com/dotnet/framework/ui-automation/ui-automation-support-for-the-checkbox-control-type) ※ TogglePattern のみ実装が明記
- [UI Automation Patterns Overview](https://learn.microsoft.com/dotnet/framework/ui-automation/ui-automation-control-patterns-overview)

### 実機ログ
- `scripts/ui_automation/debug_checkbox_invoke.ps1`（一時調査用・スクリプト本体）
- セッションログ（PowerShell 実行結果は本ファイル §2 §3 に転記済）

### 関連ファイル
- `scripts/ui_automation/testcases/tc_startup_edit.ps1`（修正対象・toggle 3 件 INFO 化）
- `scripts/ui_automation/lib/uia_common.ps1`（将来：マウス座標経由クリック関数追加候補）
- `scripts/ui_automation/run_ui_test.ps1`（LimitationsFlagged 集計追加）
- `docs/vm_r2_e2e_result.md`（実機 PASS の引用元・3-1〜3-3）
- `docs/UI_Automation_対応計画書_第1版.md`（将来課題追記）

---

## 9. plan-validator 反映状況

本ファイルおよび関連修正は `@plan-validator` による事前チェックを受けて作成：

| 指摘 | 対応 |
|---|---|
| 1. CI 緑化副作用 | `run_ui_test.ps1` に `LimitationsFlagged` カウンタ追加 |
| 2. 誤検知の塗り重ね | toggle.click / revert を OK → INFO + Detail に「ToggleState 遷移のみ」明記 |
| 3. 旧調査との混同 | 本ファイル §6 で Cross-Ref 明示・次セッション再開ガイド側も更新 |
| エッジ 1. cleanup の StartupApproved キー存在確認 | 既存コードで `Test-Path $approvedPath` 守られている（変更不要） |
| エッジ 2. INFO 集計の警告 | `LimitationsFlagged` で対応（指摘 1 と同根） |
| エッジ 3. 並行実行の testName 衝突 | 現状運用に並行実行なし → 将来課題として記録のみ |
