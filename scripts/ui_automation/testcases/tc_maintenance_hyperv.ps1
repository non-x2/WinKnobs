# TC-maintenance-hyperv: v0.2.1 Step5「Hyper-V（ハイパーバイザー起動）ON/OFF」UIA テスト（🔴Red）
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# 検証内容（非破壊・管理者不要・ホスト/VM 共通で安全＝チェックは入れない／[無効化]は押さない）：
#   1) ツールメニュー(MenuTools)配下に MenuHyperVStatus（常設状態ラベル・読み取り専用）/
#      MenuEnableHyperV / MenuDisableHyperV / MenuRestoreHyperV が存在
#   2) 無効化を Invoke → 🔴Red 専用 WPF ダイアログ HyperVDisableDialog が出現
#   3) ★チェック未投入時は [無効化](BtnHyperVOff) が**非活性**（明示同意ジェスチャの核心）
#   4) [キャンセル](BtnCancel) が存在＝既定フォーカス。CheckUnderstandHyperVRisk は未チェック
#   5) BitLocker 警告（HyperVDisableBitLockerWarningText）が**無条件で表示**（plan-validator 致命2）
#   6) [キャンセル]で閉じる → 結果ダイアログ無し（＝bcdedit 未実行）
#   7) 有効化を Invoke → 確認ダイアログ「Hyper-V を有効化」(Confirm) → 「いいえ」→ 非実行
#   8) launchtype の不変確認は INFO（bcdedit /enum は要昇格＝非昇格では読めない。
#      非実行は no_execution＋退避ファイル不変で間接確認・実書込は VM 実機検証）
#
# 設計方針：tc_maintenance_remotedesktop_enable 同型（Red WPF ダイアログ＋同意ジェスチャ検証）。
#   ※ 既に launchtype=Off の環境では Disable は no-op Info ダイアログ（「既に Off」）→ INFO 降格。
#
# AutomationId：MenuTools / MenuHyperVStatus / MenuEnableHyperV / MenuDisableHyperV / MenuRestoreHyperV /
#               HyperVDisableDialog / CheckUnderstandHyperVRisk / BtnHyperVOff / BtnCancel /
#               HyperVDisableBitLockerWarningText

function Invoke-MaintenanceHyperV {
    param([Parameter(Mandatory=$true)]$Win)

    $rawOutput = & {
        $results = @()
        $results += New-TestResult -Id "maint_hyperv.entry" -Status "INFO" -Detail "Invoke-MaintenanceHyperV 開始（非破壊・チェックも[無効化]も押さない）"

        # ---- ローカルヘルパー（tc_maintenance_remotedesktop_enable 同型） ----

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

        function Get-ElemEnabled { param($e) try { return [bool]$e.Current.IsEnabled } catch { return $null } }

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

        try {
            # ─── 1) メニュー4項目の存在確認 ───
            foreach ($id in @("MenuHyperVStatus","MenuEnableHyperV","MenuDisableHyperV","MenuRestoreHyperV")) {
                $item = Get-MenuItemUnderTools $Win $id
                if ($item) {
                    $en = Get-ElemEnabled $item
                    $results += New-TestResult -Id "maint_hyperv.menu.$id" -Status "OK" -Detail "$id 検出（Enabled=$en）"
                } else {
                    $results += New-TestResult -Id "maint_hyperv.menu.$id" -Status "FAIL" -Detail "$id 未検出（MenuTools 展開後・2回試行）"
                }
            }

            # ─── 2) 無効化 Invoke → Red 専用 WPF ダイアログ ───
            $menuId    = "MenuDisableHyperV"
            $dlgAutoId = "HyperVDisableDialog"
            $item = Get-MenuItemUnderTools $Win $menuId
            if (-not $item) {
                $results += New-TestResult -Id "maint_hyperv.dialog.open" -Status "FAIL" -Detail "$menuId 未検出（再取得失敗）"
                return $results
            }
            $menuEnabled = Get-ElemEnabled $item
            if ($menuEnabled -eq $false) {
                $results += New-TestResult -Id "maint_hyperv.dialog.open" -Status "INFO" -Detail "limitation: $menuId が非活性（Busy）。Red ダイアログ経路は別途検証要"
                return $results
            }

            $dlg = $null
            for ($try = 1; $try -le 2 -and -not $dlg; $try++) {
                $dlg = Wait-WindowByAutomationId -AutomationId $dlgAutoId -TimeoutSeconds 1 -Root $Win
                if ($dlg) { break }
                if ($try -gt 1) { $item = Get-MenuItemUnderTools $Win $menuId; if (-not $item) { continue } }
                Invoke-Element $item | Out-Null
                Start-Sleep -Milliseconds 900
                $dlg = Wait-WindowByAutomationId -AutomationId $dlgAutoId -TimeoutSeconds 4 -Root $Win
                if (-not $dlg) { $dlg = Wait-WindowByAutomationId -AutomationId $dlgAutoId -TimeoutSeconds 2 } # RootElement 直下も保険
            }
            if (-not $dlg) {
                # 既に launchtype=Off の環境：no-op Info「Hyper-V」ダイアログが出る → INFO 降格
                $infoDlg = Wait-WindowByName -Name "Hyper-V" -TimeoutSeconds 1
                if ($infoDlg) {
                    $okBtnCond = New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                        [System.Windows.Automation.ControlType]::Button)
                    $okBtn = $infoDlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $okBtnCond)
                    if ($okBtn) { Invoke-Element $okBtn | Out-Null; Start-Sleep -Milliseconds 400 }
                    $results += New-TestResult -Id "maint_hyperv.dialog.open" -Status "INFO" -Detail "limitation: 既に launchtype=Off（no-op Info ダイアログ）＝Red ダイアログ経路は launchtype=Auto/未設定 環境で検証要。no-op 冪等自体は正常動作"
                    return $results
                }
                $shot = "$PSScriptRoot\..\screenshots\diag_maint_hyperv.png"
                try { Take-Screenshot -Path $shot | Out-Null } catch {}
                $results += New-TestResult -Id "maint_hyperv.dialog.open" -Status "FAIL" -Detail "Red ダイアログ「$dlgAutoId」未検出（Invoke 後・2回試行）"
                return $results
            }
            $results += New-TestResult -Id "maint_hyperv.dialog.open" -Status "OK" -Detail "🔴Red 専用ダイアログ HyperVDisableDialog 出現（WPF Modal）"

            # ─── 3) ★チェック未投入時は [無効化] 非活性 ───
            $btnOff = Find-ById $dlg "BtnHyperVOff" 3
            $chk    = Find-ById $dlg "CheckUnderstandHyperVRisk" 2
            if (-not $btnOff) {
                $results += New-TestResult -Id "maint_hyperv.off.disabled_until_check" -Status "FAIL" -Detail "BtnHyperVOff 未検出"
            } else {
                $offEnabled = Get-ElemEnabled $btnOff
                if ($offEnabled -eq $false) {
                    $results += New-TestResult -Id "maint_hyperv.off.disabled_until_check" -Status "OK" -Detail "[無効化] はチェック未投入で非活性＝明示同意ジェスチャ成立"
                } else {
                    $results += New-TestResult -Id "maint_hyperv.off.disabled_until_check" -Status "FAIL" -Detail "[無効化] がチェック未投入で活性＝安全ジェスチャ破れ（CanDisable 配線要確認）"
                }
            }
            if ($chk) {
                $toggle = $null
                try { $toggle = $chk.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern) } catch {}
                if ($toggle -and $toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off) {
                    $results += New-TestResult -Id "maint_hyperv.check.unchecked" -Status "OK" -Detail "CheckUnderstandHyperVRisk 初期＝未チェック（既定で同意なし）"
                } else {
                    $results += New-TestResult -Id "maint_hyperv.check.unchecked" -Status "INFO" -Detail "limitation: ToggleState を確認できず（環境差）。既定未チェックは ViewModel(IsRiskUnderstood=false) で担保"
                }
            } else {
                $results += New-TestResult -Id "maint_hyperv.check.unchecked" -Status "FAIL" -Detail "CheckUnderstandHyperVRisk 未検出"
            }

            # ─── 5) BitLocker 警告の無条件表示（plan-validator 致命2） ───
            $blWarn = Find-ById $dlg "HyperVDisableBitLockerWarningText" 2
            if ($blWarn) {
                $results += New-TestResult -Id "maint_hyperv.bitlocker_warning" -Status "OK" -Detail "BitLocker 回復キー警告が表示（無条件常時表示＝検知失敗で警告が消えない設計）"
            } else {
                $results += New-TestResult -Id "maint_hyperv.bitlocker_warning" -Status "FAIL" -Detail "HyperVDisableBitLockerWarningText 未検出（致命2 の警告が出ていない）"
            }

            # ─── 4) [キャンセル] 存在＋既定フォーカス ───
            $btnCancel = Find-ById $dlg "BtnCancel" 3
            if (-not $btnCancel) {
                $results += New-TestResult -Id "maint_hyperv.cancel.default_focus" -Status "FAIL" -Detail "BtnCancel 未検出（閉じられない）"
                return $results
            }
            $hasFocus = $false
            try { $hasFocus = [bool]$btnCancel.Current.HasKeyboardFocus } catch {}
            if ($hasFocus) {
                $results += New-TestResult -Id "maint_hyperv.cancel.default_focus" -Status "OK" -Detail "既定フォーカス＝[キャンセル]（誤確定防止）"
            } else {
                $results += New-TestResult -Id "maint_hyperv.cancel.default_focus" -Status "INFO" -Detail "limitation: 既定フォーカスを UIA で確認できず（非アクティブ環境で揺れる）。IsCancel=True/Loaded フォーカスは XAML で担保"
            }

            # ─── 6) [キャンセル]で閉じる ───
            Invoke-Element $btnCancel | Out-Null
            Start-Sleep -Milliseconds 800
            $stillOpen = Wait-WindowByAutomationId -AutomationId $dlgAutoId -TimeoutSeconds 1 -Root $Win
            if ($stillOpen) {
                $results += New-TestResult -Id "maint_hyperv.cancel.click" -Status "FAIL" -Detail "[キャンセル]後もダイアログが残存"
            } else {
                $results += New-TestResult -Id "maint_hyperv.cancel.click" -Status "OK" -Detail "[キャンセル]で Red ダイアログを閉じた"
            }

            $noRes = Test-NoResultDialog
            if ($noRes.Clean) {
                $results += New-TestResult -Id "maint_hyperv.no_execution" -Status "OK" -Detail "結果ダイアログ無し＝bcdedit 未実行"
            } else {
                $results += New-TestResult -Id "maint_hyperv.no_execution" -Status "FAIL" -Detail "「$($noRes.Title)」ダイアログ出現＝キャンセルでも実行された疑い"
            }

            Start-Sleep -Milliseconds 500

            # ─── 7) 有効化（Confirm）→「いいえ」→ 非実行 ───
            # 2026-06-11 VM実測：単発 Invoke＋5秒待ちでは VM コールド時にダイアログ出現が間に合わず
            # FAIL→ダイアログ閉じ残しが起きた → sysmain 同型の2回試行ループ＋待ち延長＋FAIL時も後始末。
            $cdlg = $null
            $enItem = $null
            for ($try = 1; $try -le 2 -and -not $cdlg; $try++) {
                $cdlg = Wait-WindowByName -Name "Hyper-V を有効化" -TimeoutSeconds 2
                if ($cdlg) { break }
                $enItem = Get-MenuItemUnderTools $Win "MenuEnableHyperV"
                if ($enItem) {
                    Invoke-Element $enItem | Out-Null
                    Start-Sleep -Milliseconds 900
                    $cdlg = Wait-WindowByName -Name "Hyper-V を有効化" -TimeoutSeconds 8
                }
            }
            # 部分一致フォールバック（sysmain/wsl 同型・2026-06-11 VM実測：exact Name 検索が
            # このダイアログだけ外れる環境がある＝ダイアログ自体は開いている）
            if (-not $cdlg) {
                $root = [System.Windows.Automation.AutomationElement]::RootElement
                $winCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Window)
                $enNames = @()
                foreach ($scopeRoot in @($root, $Win)) {
                    $scope = if ($scopeRoot -eq $root) { [System.Windows.Automation.TreeScope]::Children } else { [System.Windows.Automation.TreeScope]::Descendants }
                    $ws = $scopeRoot.FindAll($scope, $winCond)
                    foreach ($w in $ws) {
                        $nm = [string]$w.Current.Name
                        $enNames += $nm
                        if (-not $cdlg -and $nm -match "有効化" -and $nm -match "Hyper") { $cdlg = $w }
                    }
                }
                if (-not $cdlg -and $enNames.Count -gt 0) {
                    $results += New-TestResult -Id "maint_hyperv.enable.diag" -Status "INFO" -Detail ("検出Window=[" + ($enNames -join ' | ') + "]")
                }
            }
            if ($enItem -or $cdlg) {
                if ($cdlg) {
                    $results += New-TestResult -Id "maint_hyperv.enable.dialog.open" -Status "OK" -Detail "確認ダイアログ「Hyper-V を有効化」出現"
                    $nb = Find-MsgBoxButton $cdlg "no"
                    if ($nb) {
                        Invoke-Element $nb | Out-Null
                        Start-Sleep -Milliseconds 800
                        $noRes2 = Test-NoResultDialog
                        if ($noRes2.Clean) {
                            $results += New-TestResult -Id "maint_hyperv.enable.no_execution" -Status "OK" -Detail "「いいえ」で非実行（結果ダイアログ無し）"
                        } else {
                            $results += New-TestResult -Id "maint_hyperv.enable.no_execution" -Status "FAIL" -Detail "「$($noRes2.Title)」ダイアログ出現＝いいえ選択でも実行された疑い"
                        }
                    } else {
                        $results += New-TestResult -Id "maint_hyperv.enable.no_execution" -Status "FAIL" -Detail "「いいえ」ボタン未検出"
                    }
                } else {
                    $results += New-TestResult -Id "maint_hyperv.enable.dialog.open" -Status "FAIL" -Detail "確認ダイアログ「Hyper-V を有効化」未検出"
                    # 後始末：遅れて出現したダイアログを閉じ残さない（2026-06-11 VM実測の閉じ残し対策）
                    $late = Wait-WindowByName -Name "Hyper-V を有効化" -TimeoutSeconds 8
                    if ($late) {
                        $lnb = Find-MsgBoxButton $late "no"
                        if ($lnb) { Invoke-Element $lnb | Out-Null; Start-Sleep -Milliseconds 400 }
                        $results += New-TestResult -Id "maint_hyperv.enable.late_cleanup" -Status "INFO" -Detail "遅延出現したダイアログを「いいえ」で後始末（出現自体は確認＝検出タイミングの問題）"
                    }
                }
            } else {
                $results += New-TestResult -Id "maint_hyperv.enable.dialog.open" -Status "FAIL" -Detail "MenuEnableHyperV 再取得失敗"
            }

            # ─── 8) launchtype 不変は INFO（bcdedit /enum は要昇格） ───
            $results += New-TestResult -Id "maint_hyperv.state.unchanged" -Status "INFO" -Detail "limitation: hypervisorlaunchtype（bcdedit /enum）は要昇格のため非昇格 UIA では読めない。非実行は no_execution で確認・実書込/退避/復元は VM 実機検証で担保"
            $results += New-TestResult -Id "maint_hyperv.busy_guard" -Status "INFO" -Detail "limitation: IsSystemFeatureBusy による直列化は実行中のみ観測可。xUnit(CanExecute)＋VM実機で担保"

        } catch {
            $results += New-TestResult -Id "maint_hyperv.main.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
            # 開いているかもしれないダイアログを閉じる
            $d = Wait-WindowByAutomationId -AutomationId "HyperVDisableDialog" -TimeoutSeconds 1 -Root $Win
            if ($d) {
                $bc = Find-ById $d "BtnCancel" 2
                if ($bc) { Invoke-Element $bc | Out-Null; Start-Sleep -Milliseconds 400 }
            }
            foreach ($t in @("Hyper-V を有効化")) {
                $cd = Wait-WindowByName -Name $t -TimeoutSeconds 1
                if ($cd) {
                    $nb = Find-MsgBoxButton $cd "no"
                    if ($nb) { Invoke-Element $nb | Out-Null; Start-Sleep -Milliseconds 400 }
                }
            }
        }

        return $results
    }

    return @($rawOutput | Where-Object { $_ -is [PSCustomObject] -and $null -ne $_.Status })
}
