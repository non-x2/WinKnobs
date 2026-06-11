# TC-maintenance-wsl: v0.2.1 Step3「WSL ON/OFF」UIA テスト
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# 検証内容（非破壊・管理者不要・ホスト/VM 共通で安全＝「はい」は押さない）：
#   1) ツールメニュー(MenuTools)配下に MenuEnableWsl / MenuDisableWsl が存在
#   2) 有効化を Invoke → 確認ダイアログ「WSL を有効化」(Confirm) が出現
#   3) 「いいえ」で閉じる → 結果ダイアログ無し（＝Enable-WindowsOptionalFeature 未実行）
#   4) 無効化を Invoke → 確認ダイアログ「WSL を無効化」(Confirm) → 「いいえ」→ 非実行
#   5) 機能状態の不変確認は INFO（Get-WindowsOptionalFeature は要昇格＝非昇格環境では読めない）
#
# 設計方針：tc_maintenance_sysmain 同型（WSL は可逆操作＝両方とも通常 Confirm）。
#   実 Enable/Disable（done-stack・PartiallyEnabled・300秒タイムアウト）は別途 VM 実機検証で確認。
#
# AutomationId：MenuTools / MenuEnableWsl / MenuDisableWsl

function Invoke-MaintenanceWsl {
    param([Parameter(Mandatory=$true)]$Win)

    $rawOutput = & {
        $results = @()
        $results += New-TestResult -Id "maint_wsl.entry" -Status "INFO" -Detail "Invoke-MaintenanceWsl 開始（非破壊・はいは押さない）"

        # ---- ローカルヘルパー（tc_maintenance_sysmain 同型） ----

        function Get-MenuItemUnderTools {
            param($win, [string]$id)
            $menuTools = Find-ById $win "MenuTools" 3
            if (-not $menuTools) { return $null }
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
                try { Collapse-Element $menuTools | Out-Null; Start-Sleep -Milliseconds 250 } catch {}
                Expand-Element $menuTools | Out-Null
                Start-Sleep -Milliseconds 600
                $item = Find-ById $win $id 3
            }
            return $item
        }

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

        function Test-NoResultDialog {
            $root = [System.Windows.Automation.AutomationElement]::RootElement
            foreach ($t in @("成功","失敗","権限不足","機能なし","中途半端な状態","エラー")) {
                $cond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $t)
                $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
                if ($w) { return @{ Clean=$false; Title=$t } }
            }
            return @{ Clean=$true; Title=$null }
        }

        function Invoke-WslMenuCase {
            param($win, [string]$menuId, [string]$expectTitle, [string]$idPrefix)
            $r = @()

            $dlg = $null
            $menuFound = $false
            for ($try = 1; $try -le 2 -and -not $dlg; $try++) {
                $dlg = Wait-WindowByName -Name $expectTitle -TimeoutSeconds 1
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

            # 部分一致フォールバック：「WSL」を含む Window
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
                        if (-not $dlg -and $nm -match "WSL") { $dlg = $w }
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

            $noBtn = Find-MsgBoxButton $dlg "no"
            if (-not $noBtn) {
                $r += New-TestResult -Id "$idPrefix.cancel.btn" -Status "FAIL" -Detail "「いいえ」ボタン未検出（閉じられない）"
                return $r
            }
            Invoke-Element $noBtn | Out-Null
            Start-Sleep -Milliseconds 800
            $r += New-TestResult -Id "$idPrefix.cancel.click" -Status "OK" -Detail "「いいえ」で確認ダイアログを閉じた"

            $noRes = Test-NoResultDialog
            if ($noRes.Clean) {
                $r += New-TestResult -Id "$idPrefix.no_execution" -Status "OK" -Detail "結果ダイアログ無し＝Enable/Disable-WindowsOptionalFeature 未実行"
            } else {
                $r += New-TestResult -Id "$idPrefix.no_execution" -Status "FAIL" -Detail "「$($noRes.Title)」ダイアログ出現＝いいえ選択でも実行された疑い"
            }
            return $r
        }

        try {
            # ─── ケースA：有効化（Confirm）───
            $results += Invoke-WslMenuCase -win $Win -menuId "MenuEnableWsl" `
                        -expectTitle "WSL を有効化" -idPrefix "maint_wsl.enable"

            Start-Sleep -Milliseconds 500

            # ─── ケースB：無効化（Confirm）───
            $results += Invoke-WslMenuCase -win $Win -menuId "MenuDisableWsl" `
                        -expectTitle "WSL を無効化" -idPrefix "maint_wsl.disable"

            # 機能状態の不変確認：Get-WindowsOptionalFeature は要昇格＝ここでは検証できない（limitation）
            $results += New-TestResult -Id "maint_wsl.state.unchanged" -Status "INFO" -Detail "limitation: 機能状態（Get-WindowsOptionalFeature）は要昇格のため非昇格 UIA では読めない。非実行は no_execution（結果ダイアログ無し）で確認・実トグルは VM 実機検証で担保"
            $results += New-TestResult -Id "maint_wsl.busy_guard" -Status "INFO" -Detail "limitation: IsSystemFeatureBusy による直列化は実行中のみ観測可。xUnit(CanExecute)＋VM実機で担保"

        } catch {
            $results += New-TestResult -Id "maint_wsl.main.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
            foreach ($t in @("WSL を有効化","WSL を無効化")) {
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
