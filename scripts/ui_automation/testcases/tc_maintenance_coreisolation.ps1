# TC-maintenance-coreisolation: v0.2.1 Step4「コア分離（メモリ整合性・HVCI）ON/OFF」UIA テスト（🔴Red）
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# 検証内容（非破壊・管理者不要・ホスト/VM 共通で安全＝「はい」は押さない）：
#   1) ツールメニュー(MenuTools)配下に MenuEnableCoreIsolation / MenuDisableCoreIsolation が存在
#      ※ GPO/MDM 管理中は CanExecute=false でグレーアウト → INFO 降格（tc_maintenance_remotedesktop_enable 同流儀）
#   2) 有効化を Invoke → 確認ダイアログ「コア分離を有効化」(Confirm) → 「いいえ」→ 非実行
#   3) 無効化を Invoke → 確認ダイアログ「⚠️ コア分離を無効化」(ConfirmDestructive・既定いいえ) → 「いいえ」→ 非実行
#      既定フォーカス「いいえ」は INFO 降格可（環境で揺れる・XAML/MessageBoxResult.No で担保）
#   4) 全工程を通して HKLM DeviceGuard\...\HVCI\Enabled が不変（非昇格でも読める＝正の確認）
#
# 設計方針：tc_maintenance_sysmain 同型＋OFF 側のみ ConfirmDestructive（既定いいえ）検証を追加。
#   実書込（UEFI ロック・AccessDenied 経路）は別途 VM 実機検証で確認。
#
# AutomationId：MenuTools / MenuEnableCoreIsolation / MenuDisableCoreIsolation

function Invoke-MaintenanceCoreIsolation {
    param([Parameter(Mandatory=$true)]$Win)

    $rawOutput = & {
        $results = @()
        $results += New-TestResult -Id "maint_hvci.entry" -Status "INFO" -Detail "Invoke-MaintenanceCoreIsolation 開始（非破壊・はいは押さない）"

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
            foreach ($t in @("成功","失敗","権限不足","設定完了（再起動で反映）","エラー")) {
                $cond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $t)
                $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
                if ($w) { return @{ Clean=$false; Title=$t } }
            }
            return @{ Clean=$true; Title=$null }
        }

        function Get-ElemEnabled { param($e) try { return [bool]$e.Current.IsEnabled } catch { return $null } }

        # HVCI Enabled（DWORD・非昇格でも読める）を読む（read-only・無害）
        function Get-HvciEnabled {
            $p = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity' -Name Enabled -ErrorAction SilentlyContinue
            if ($p -and ($p.PSObject.Properties.Name -contains 'Enabled')) { return [int]$p.Enabled }
            return $null   # 値なし環境（無効・未設定）
        }

        function Invoke-HvciMenuCase {
            param($win, [string]$menuId, [string]$expectTitle, [string]$idPrefix, [bool]$checkDefaultNo)
            $r = @()

            # メニュー検出＋グレーアウト判定（GPO/MDM 管理中は INFO 降格）
            $item = Get-MenuItemUnderTools $win $menuId
            if (-not $item) {
                $r += New-TestResult -Id "$idPrefix.menu.find" -Status "FAIL" -Detail "$menuId 未検出（MenuTools 展開後・2回試行）"
                return $r
            }
            $r += New-TestResult -Id "$idPrefix.menu.find" -Status "OK" -Detail "$menuId 検出"

            $menuEnabled = Get-ElemEnabled $item
            if ($menuEnabled -eq $false) {
                $r += New-TestResult -Id "$idPrefix.menu.enabled" -Status "INFO" -Detail "limitation: $menuId が非活性（GPO/MDM 管理 or Busy）。ダイアログ経路は非ポリシー環境で検証要。グレーアウト自体は CanExecute 正常動作"
                $mt = Find-ById $win "MenuTools" 2
                if ($mt) { try { Collapse-Element $mt | Out-Null } catch {} }
                return $r
            }

            $dlg = $null
            for ($try = 1; $try -le 2 -and -not $dlg; $try++) {
                $dlg = Wait-WindowByName -Name $expectTitle -TimeoutSeconds 1
                if ($dlg) { break }
                if ($try -gt 1) { $item = Get-MenuItemUnderTools $win $menuId; if (-not $item) { continue } }
                Invoke-Element $item | Out-Null
                Start-Sleep -Milliseconds 900
                $dlg = Wait-WindowByName -Name $expectTitle -TimeoutSeconds 5
            }

            # 部分一致フォールバック：「コア分離」を含む Window
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
                        if (-not $dlg -and $nm -match "コア分離") { $dlg = $w }
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

            # OFF 側のみ：既定フォーカス「いいえ」（ConfirmDestructive・🔴締め出し防止と同流儀）
            if ($checkDefaultNo) {
                $hasFocus = $false
                try { $hasFocus = [bool]$noBtn.Current.HasKeyboardFocus } catch {}
                if ($hasFocus) {
                    $r += New-TestResult -Id "$idPrefix.default_no" -Status "OK" -Detail "既定フォーカス＝「いいえ」（ConfirmDestructive・誤確定防止）"
                } else {
                    $r += New-TestResult -Id "$idPrefix.default_no" -Status "INFO" -Detail "limitation: 既定フォーカスを UIA で確認できず（非アクティブ環境で揺れる）。MessageBoxResult.No 既定は DialogService.ConfirmDestructive で担保"
                }
            }

            Invoke-Element $noBtn | Out-Null
            Start-Sleep -Milliseconds 800
            $r += New-TestResult -Id "$idPrefix.cancel.click" -Status "OK" -Detail "「いいえ」で確認ダイアログを閉じた"

            $noRes = Test-NoResultDialog
            if ($noRes.Clean) {
                $r += New-TestResult -Id "$idPrefix.no_execution" -Status "OK" -Detail "結果ダイアログ無し＝レジストリ書込 未実行"
            } else {
                $r += New-TestResult -Id "$idPrefix.no_execution" -Status "FAIL" -Detail "「$($noRes.Title)」ダイアログ出現＝いいえ選択でも実行された疑い"
            }
            return $r
        }

        try {
            # 全工程前の HVCI Enabled（read-only・非昇格可）
            $hvciBefore = Get-HvciEnabled

            # ─── ケースA：有効化（Confirm）───
            $results += Invoke-HvciMenuCase -win $Win -menuId "MenuEnableCoreIsolation" `
                        -expectTitle "コア分離を有効化" -idPrefix "maint_hvci.enable" -checkDefaultNo:$false

            Start-Sleep -Milliseconds 500

            # ─── ケースB：無効化（ConfirmDestructive・既定いいえ・🔴）───
            $results += Invoke-HvciMenuCase -win $Win -menuId "MenuDisableCoreIsolation" `
                        -expectTitle "⚠️ コア分離を無効化" -idPrefix "maint_hvci.disable" -checkDefaultNo:$true

            # ─── 全工程後の HVCI Enabled が不変（正の確認・非昇格で読める）───
            $hvciAfter = Get-HvciEnabled
            if ($hvciBefore -eq $hvciAfter) {
                $results += New-TestResult -Id "maint_hvci.state.unchanged" -Status "OK" -Detail "HVCI Enabled 不変（before=$hvciBefore after=$hvciAfter）＝副作用なし"
            } else {
                $results += New-TestResult -Id "maint_hvci.state.unchanged" -Status "FAIL" -Detail "HVCI Enabled が変化（before=$hvciBefore after=$hvciAfter）＝想定外の実行"
            }

            $results += New-TestResult -Id "maint_hvci.busy_guard" -Status "INFO" -Detail "limitation: IsSystemFeatureBusy による直列化は実行中のみ観測可。xUnit(CanExecute)＋VM実機で担保"

        } catch {
            $results += New-TestResult -Id "maint_hvci.main.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
            foreach ($t in @("コア分離を有効化","⚠️ コア分離を無効化")) {
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
