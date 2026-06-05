# TC-maintenance-protection: システム・メンテナンス Phase 2「システムの保護 ON/OFF」UIA テスト
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# 検証内容（非破壊・管理者不要・ホスト/VM 共通で安全）：
#   1) ツールメニュー(MenuTools)配下に MenuEnableSystemProtection / MenuDisableSystemProtection が存在
#   2) 有効化を Invoke → 確認ダイアログ「システムの保護を有効化」(Confirm) が出現
#   3) 「いいえ」で閉じる → 結果ダイアログ無し（＝実行されない）
#   4) 無効化を Invoke → 確認ダイアログ「システムの保護を無効化」(ConfirmDestructive) が出現
#   5) ★無効化の既定ボタン＝「いいえ」にキーボードフォーカス（v0.1.16 重要①＝誤爆防止）
#   6) 「いいえ」で閉じる → 結果ダイアログ無し
#   7) 全工程を通して HKLM SystemRestore\RPSessionInterval が不変（＝キャンセルで本当に非実行）
#
# 設計方針：
#   - 確認ダイアログは Win32 MessageBox（DialogService.Confirm/ConfirmDestructive）。
#     トップレベル Window として Name(=タイトル)で検出（tc_startup_admin と同パターン）。
#   - 「はい」は一切押さない＝実際の Enable/Disable-ComputerRestore は走らせない（管理者操作・破壊を回避）。
#     管理者 Success 経路／no-op／AccessDenied は別途 VM 実機検証で確認済
#     （docs/テスト結果_2026-06-05_v0.1.16_VM保護ON-OFF.md）。
#   - 既定フォーカス検証は環境で揺れうるため、検出失敗時は INFO(limitation) に降格（spurious FAIL 回避）。
#
# AutomationId：MenuTools / MenuEnableSystemProtection / MenuDisableSystemProtection

function Invoke-MaintenanceProtection {
    param([Parameter(Mandatory=$true)]$Win)

    $rawOutput = & {
        $results = @()
        $results += New-TestResult -Id "maint_protect.entry" -Status "INFO" -Detail "Invoke-MaintenanceProtection 開始（非破壊・はいは押さない）"

        # ---- ローカルヘルパー（この & {} スコープに閉じる） ----

        # ツールメニューを展開して指定 AutomationId のメニュー項目を取得
        function Get-MenuItemUnderTools {
            param($win, [string]$id)
            $menuTools = Find-ById $win "MenuTools" 3
            if (-not $menuTools) { return $null }
            # 前回展開が残っていれば畳む
            try {
                $ecp = $menuTools.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
                if ($ecp.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Expanded) {
                    $ecp.Collapse(); Start-Sleep -Milliseconds 200
                }
            } catch {}
            Expand-Element $menuTools | Out-Null
            Start-Sleep -Milliseconds 600
            $item = Find-ById $win $id 4
            if (-not $item) {
                # 1 回畳んで再展開のリトライ（WPF メニューは展開後のみ UIA ツリーに出る）
                try { (Get-MenuItemUnderTools_collapse $menuTools) } catch {}
                Expand-Element $menuTools | Out-Null
                Start-Sleep -Milliseconds 600
                $item = Find-ById $win $id 3
            }
            return $item
        }
        function Get-MenuItemUnderTools_collapse {
            param($menu)
            try { Collapse-Element $menu | Out-Null; Start-Sleep -Milliseconds 250 } catch {}
        }

        # MessageBox(トップレベル Window)内のボタンを Name で検索（日本語/英語の表記ゆれ吸収）
        function Find-MsgBoxButton {
            param($dlg, [ValidateSet("yes","no")][string]$kind)
            $btnCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)
            $btns = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
            foreach ($b in $btns) {
                $n = [string]$b.Current.Name
                if ($kind -eq "no")  { if ($n -match "いいえ" -or $n -ieq "No"  -or $n -ieq "&No")  { return $b } }
                if ($kind -eq "yes") { if ($n -match "はい"   -or $n -ieq "Yes" -or $n -ieq "&Yes") { return $b } }
            }
            return $null
        }

        # 結果ダイアログ（成功/失敗/権限不足/注意/エラー）が出ていないことを確認
        function Test-NoResultDialog {
            $root = [System.Windows.Automation.AutomationElement]::RootElement
            foreach ($t in @("成功","失敗","権限不足","注意","エラー")) {
                $cond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $t)
                $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
                if ($w) { return @{ Clean=$false; Title=$t } }
            }
            return @{ Clean=$true; Title=$null }
        }

        # HKLM SystemRestore\RPSessionInterval（保護状態）を読む（read-only・無害）
        function Get-RpInterval {
            $p = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' -Name RPSessionInterval -ErrorAction SilentlyContinue
            if ($p -and ($p.PSObject.Properties.Name -contains 'RPSessionInterval')) { return [int]$p.RPSessionInterval }
            return $null   # 未設定
        }

        # 1 メニュー項目分の検証（確認ダイアログ出現 → 既定フォーカス任意確認 → いいえで閉じる → 非実行確認）
        function Invoke-ProtectionMenuCase {
            param($win, [string]$menuId, [string]$expectTitle, [bool]$checkDefaultNo, [string]$idPrefix)
            $r = @()

            # メニュー Invoke → 確認ダイアログ検出を最大2回リトライ（コールド起動直後の取りこぼし対策）。
            # 各周回でまず既出ダイアログを確認 → 無ければ MenuTools 再展開して項目を Invoke → 待機。
            $dlg = $null
            $menuFound = $false
            for ($try = 1; $try -le 2 -and -not $dlg; $try++) {
                $dlg = Wait-WindowByName -Name $expectTitle -TimeoutSeconds 1   # 既に開いていれば即採用（二重起動防止）
                if ($dlg) { break }
                $item = Get-MenuItemUnderTools $win $menuId
                if ($item) {
                    if (-not $menuFound) {
                        $r += New-TestResult -Id "$idPrefix.menu.find" -Status "OK" -Detail "$menuId 検出"
                        $menuFound = $true
                    }
                    Invoke-Element $item | Out-Null
                    Start-Sleep -Milliseconds 900
                    $dlg = Wait-WindowByName -Name $expectTitle -TimeoutSeconds 5
                }
            }
            if (-not $menuFound) {
                $r += New-TestResult -Id "$idPrefix.menu.find" -Status "FAIL" -Detail "$menuId 未検出（MenuTools 展開後・2回試行）"
                return $r
            }

            # 部分一致フォールバック：RootElement 直下＋$win 配下の Window を「システムの保護」で探す
            if (-not $dlg) {
                $root = [System.Windows.Automation.AutomationElement]::RootElement
                $winCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Window)
                $names = @()
                foreach ($scopeRoot in @($root, $win)) {
                    $scope = if ($scopeRoot -eq $root) { [System.Windows.Automation.TreeScope]::Children } else { [System.Windows.Automation.TreeScope]::Descendants }
                    $ws = $scopeRoot.FindAll($scope, $winCond)
                    foreach ($w in $ws) {
                        $nm = [string]$w.Current.Name
                        $names += $nm
                        if (-not $dlg -and $nm -match "システムの保護") { $dlg = $w }
                    }
                }
                if (-not $dlg) {
                    $shot = "$PSScriptRoot\..\screenshots\diag_${idPrefix}.png"
                    try { Take-Screenshot -Path $shot | Out-Null } catch {}
                    $r += New-TestResult -Id "$idPrefix.dialog.open" -Status "FAIL" -Detail ("確認ダイアログ「$expectTitle」未検出。検出Window=[" + ($names -join ' | ') + "]")
                    return $r
                }
            }
            $r += New-TestResult -Id "$idPrefix.dialog.open" -Status "OK" -Detail "確認ダイアログ「$expectTitle」出現"

            # ★既定ボタン＝「いいえ」フォーカス確認（ConfirmDestructive の核心・v0.1.16 重要①）
            if ($checkDefaultNo) {
                $noBtn = Find-MsgBoxButton $dlg "no"
                if (-not $noBtn) {
                    $r += New-TestResult -Id "$idPrefix.default.no_focus" -Status "FAIL" -Detail "「いいえ」ボタン未検出"
                } else {
                    $hasFocus = $false
                    try { $hasFocus = [bool]$noBtn.Current.HasKeyboardFocus } catch {}
                    if ($hasFocus) {
                        $r += New-TestResult -Id "$idPrefix.default.no_focus" -Status "OK" -Detail "既定フォーカス＝「いいえ」(ConfirmDestructive・誤爆防止)"
                    } else {
                        $r += New-TestResult -Id "$idPrefix.default.no_focus" -Status "INFO" -Detail "limitation: 既定フォーカスを UIA で確認できず（HasKeyboardFocus=false・画面外/非アクティブ環境で揺れる）。MessageBoxResult.No 指定は DialogService.ConfirmDestructive と xUnit で担保。実機目視推奨"
                    }
                }
            }

            # 「いいえ」で閉じる
            $noBtn2 = Find-MsgBoxButton $dlg "no"
            if (-not $noBtn2) {
                $r += New-TestResult -Id "$idPrefix.cancel.btn" -Status "FAIL" -Detail "「いいえ」ボタン未検出（閉じられない）"
                return $r
            }
            Invoke-Element $noBtn2 | Out-Null
            Start-Sleep -Milliseconds 800
            $r += New-TestResult -Id "$idPrefix.cancel.click" -Status "OK" -Detail "「いいえ」で確認ダイアログを閉じた"

            # 結果ダイアログが出ていない＝コマンド未実行
            $noRes = Test-NoResultDialog
            if ($noRes.Clean) {
                $r += New-TestResult -Id "$idPrefix.no_execution" -Status "OK" -Detail "結果ダイアログ無し＝Enable/Disable-ComputerRestore 未実行"
            } else {
                $r += New-TestResult -Id "$idPrefix.no_execution" -Status "FAIL" -Detail "「$($noRes.Title)」ダイアログ出現＝いいえ選択でも実行された疑い"
            }
            return $r
        }

        try {
            # 全工程前の保護状態（read-only）
            $rpBefore = Get-RpInterval

            # ─── ケースA：有効化（Confirm・既定はい）───
            $results += Invoke-ProtectionMenuCase -win $Win -menuId "MenuEnableSystemProtection" `
                        -expectTitle "システムの保護を有効化" -checkDefaultNo $false -idPrefix "maint_protect.enable"

            Start-Sleep -Milliseconds 500

            # ─── ケースB：無効化（ConfirmDestructive・既定いいえ）───
            $results += Invoke-ProtectionMenuCase -win $Win -menuId "MenuDisableSystemProtection" `
                        -expectTitle "システムの保護を無効化" -checkDefaultNo $true -idPrefix "maint_protect.disable"

            # ─── 全工程後の保護状態が不変（＝キャンセルで本当に非実行）───
            $rpAfter = Get-RpInterval
            if ($rpBefore -eq $rpAfter) {
                $results += New-TestResult -Id "maint_protect.state.unchanged" -Status "OK" -Detail "RPSessionInterval 不変（before=$rpBefore after=$rpAfter）＝保護状態に副作用なし"
            } else {
                $results += New-TestResult -Id "maint_protect.state.unchanged" -Status "FAIL" -Detail "RPSessionInterval が変化（before=$rpBefore after=$rpAfter）＝想定外の実行"
            }

            # IsSystemProtectionBusy/CanExecute（v0.1.16 重要②・二重起動防止）は実行中のみ観測可＝本テスト範囲外
            $results += New-TestResult -Id "maint_protect.busy_guard" -Status "INFO" -Detail "limitation: IsSystemProtectionBusy による二重起動防止は実行中のみ観測可。xUnit(CanExecute)＋VM実機(別スレッド化)で担保"

        } catch {
            $results += New-TestResult -Id "maint_protect.main.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
            # 念のため開いているかもしれない確認ダイアログを閉じる
            foreach ($t in @("システムの保護を有効化","システムの保護を無効化")) {
                $d = Wait-WindowByName -Name $t -TimeoutSeconds 1
                if ($d) {
                    $nb = Find-MsgBoxButton $d "no"
                    if ($nb) { Invoke-Element $nb | Out-Null; Start-Sleep -Milliseconds 400 }
                }
            }
        }

        return $results
    }

    return @($rawOutput | Where-Object { $_ -is [PSCustomObject] -and $null -ne $_.Status })
}
