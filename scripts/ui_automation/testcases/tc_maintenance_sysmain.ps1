# TC-maintenance-sysmain: v0.2.0「Sysmain サービス 有効/無効」UIA テスト
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# 検証内容（非破壊・管理者不要・ホスト/VM 共通で安全）：
#   1) ツールメニュー(MenuTools)配下に MenuEnableSysmain / MenuDisableSysmain が存在
#   2) 有効化を Invoke → 確認ダイアログ「Sysmain を有効化」(Confirm) が出現
#   3) 「いいえ」で閉じる → 結果ダイアログ無し（＝実行されない）
#   4) 無効化を Invoke → 確認ダイアログ「Sysmain を無効化」(Confirm) が出現
#   5) 「いいえ」で閉じる → 結果ダイアログ無し
#   6) 全工程を通して HKLM Services\SysMain\Start（スタートアップ種別）が不変（＝キャンセルで本当に非実行）
#
# 設計方針：
#   - tc_maintenance_protection 同型。Sysmain は可逆操作＝両方とも通常 Confirm（ConfirmDestructive 無し）
#     → 既定フォーカス「いいえ」の検証は無し（checkDefaultNo は両ケース $false）。
#   - 「はい」は一切押さない＝実際の Set-Service/Stop-Service は走らせない（管理者操作を回避）。
#     管理者 Success 経路／ServiceNotFound／AccessDenied は別途 VM 実機検証で確認。
#
# AutomationId：MenuTools / MenuEnableSysmain / MenuDisableSysmain

function Invoke-MaintenanceSysmain {
    param([Parameter(Mandatory=$true)]$Win)

    $rawOutput = & {
        $results = @()
        $results += New-TestResult -Id "maint_sysmain.entry" -Status "INFO" -Detail "Invoke-MaintenanceSysmain 開始（非破壊・はいは押さない）"

        # ---- ローカルヘルパー（この & {} スコープに閉じる・tc_maintenance_protection 同型） ----

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
            foreach ($t in @("成功","失敗","権限不足","サービスなし","注意","エラー")) {
                $cond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $t)
                $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
                if ($w) { return @{ Clean=$false; Title=$t } }
            }
            return @{ Clean=$true; Title=$null }
        }

        # HKLM Services\SysMain\Start（スタートアップ種別 2=自動/3=手動/4=無効）を読む（read-only・無害）
        function Get-SysmainStart {
            $p = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\SysMain' -Name Start -ErrorAction SilentlyContinue
            if ($p -and ($p.PSObject.Properties.Name -contains 'Start')) { return [int]$p.Start }
            return $null   # サービス不在環境
        }

        function Invoke-SysmainMenuCase {
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

            # 部分一致フォールバック：「Sysmain」を含む Window を探す
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
                        if (-not $dlg -and $nm -match "Sysmain") { $dlg = $w }
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

            # 「いいえ」で閉じる
            $noBtn = Find-MsgBoxButton $dlg "no"
            if (-not $noBtn) {
                $r += New-TestResult -Id "$idPrefix.cancel.btn" -Status "FAIL" -Detail "「いいえ」ボタン未検出（閉じられない）"
                return $r
            }
            Invoke-Element $noBtn | Out-Null
            Start-Sleep -Milliseconds 800
            $r += New-TestResult -Id "$idPrefix.cancel.click" -Status "OK" -Detail "「いいえ」で確認ダイアログを閉じた"

            # 結果ダイアログが出ていない＝コマンド未実行
            $noRes = Test-NoResultDialog
            if ($noRes.Clean) {
                $r += New-TestResult -Id "$idPrefix.no_execution" -Status "OK" -Detail "結果ダイアログ無し＝Set-Service/Stop-Service 未実行"
            } else {
                $r += New-TestResult -Id "$idPrefix.no_execution" -Status "FAIL" -Detail "「$($noRes.Title)」ダイアログ出現＝いいえ選択でも実行された疑い"
            }
            return $r
        }

        try {
            # 全工程前のスタートアップ種別（read-only）
            $startBefore = Get-SysmainStart

            # ─── ケースA：有効化（Confirm）───
            $results += Invoke-SysmainMenuCase -win $Win -menuId "MenuEnableSysmain" `
                        -expectTitle "Sysmain を有効化" -idPrefix "maint_sysmain.enable"

            Start-Sleep -Milliseconds 500

            # ─── ケースB：無効化（Confirm）───
            $results += Invoke-SysmainMenuCase -win $Win -menuId "MenuDisableSysmain" `
                        -expectTitle "Sysmain を無効化" -idPrefix "maint_sysmain.disable"

            # ─── 全工程後のスタートアップ種別が不変（＝キャンセルで本当に非実行）───
            $startAfter = Get-SysmainStart
            if ($startBefore -eq $startAfter) {
                $results += New-TestResult -Id "maint_sysmain.state.unchanged" -Status "OK" -Detail "Services\SysMain\Start 不変（before=$startBefore after=$startAfter）＝副作用なし"
            } else {
                $results += New-TestResult -Id "maint_sysmain.state.unchanged" -Status "FAIL" -Detail "Services\SysMain\Start が変化（before=$startBefore after=$startAfter）＝想定外の実行"
            }

            # IsSysmainBusy/CanExecute（二重起動防止）は実行中のみ観測可＝本テスト範囲外
            $results += New-TestResult -Id "maint_sysmain.busy_guard" -Status "INFO" -Detail "limitation: IsSysmainBusy による二重起動防止は実行中のみ観測可。xUnit(CanExecute)＋VM実機(別スレッド化)で担保"

        } catch {
            $results += New-TestResult -Id "maint_sysmain.main.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
            foreach ($t in @("Sysmain を有効化","Sysmain を無効化")) {
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
