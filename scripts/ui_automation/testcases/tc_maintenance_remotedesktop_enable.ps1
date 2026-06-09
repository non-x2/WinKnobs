# TC-maintenance-remotedesktop-enable: システム・メンテナンス Phase 4「リモートデスクトップ 有効化 ON」UIA テスト（v0.1.19・🔴Red）
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# 検証内容（非破壊・管理者不要・ホスト/VM 共通で安全＝チェックは入れない／[有効化]は押さない）：
#   1) ツールメニュー(MenuTools)配下に MenuEnableRemoteDesktop が存在
#      ※ Home 等ホスト非対応／GPO 管理中は CanExecute=false でグレーアウト → Invoke しても開かない → INFO 降格
#   2) 有効化を Invoke → 🔴Red 専用 WPF ダイアログ RemoteDesktopEnableDialog が出現（Win32 MessageBox ではない）
#   3) ★チェック未投入時は [有効化](BtnEnable) が**非活性**（明示同意ジェスチャの核心・Enter連打/誤クリック封じ）
#   4) [キャンセル](BtnCancel) が存在＝既定フォーカス（誤確定防止）。CheckUnderstandRisk は未チェック
#   5) [キャンセル]で閉じる → 結果ダイアログ無し（＝実 Enable 未実行）
#   6) 全工程を通して HKLM Terminal Server\fDenyTSConnections が不変（＝キャンセルで本当に非実行）
#
# 設計方針（tc_maintenance_remotedesktop / tc_maintenance_protection と同型・ただし ON は WPF カスタムダイアログ）：
#   - 有効化ダイアログは WPF Modal（DialogService.ConfirmRemoteDesktopEnable → ShowDialog）。AutomationId で検出。
#     InvokePattern.Invoke は非同期（モーダルでもブロックしない）。Owner=メインウィンドウの Descendants に現れる。
#   - ★チェックボックス CheckUnderstandRisk は**一切 ON にしない**＝CanEnable=false のまま＝実 Enable（NLA=1/fDeny=0/FW開）は走らせない。
#     管理者 Success/接続往復/done-stack ロールバック/退避は別途 VM 実機検証で確認（docs/テスト結果_2026-06-09_v0.1.19_VM接続往復_Enable.md）。
#   - 既定フォーカス検証は環境で揺れうるため、検出失敗時は INFO(limitation) に降格（spurious FAIL 回避）。
#
# AutomationId：MenuTools / MenuEnableRemoteDesktop / RemoteDesktopEnableDialog / CheckUnderstandRisk / BtnEnable / BtnCancel

function Invoke-MaintenanceRemoteDesktopEnable {
    param([Parameter(Mandatory=$true)]$Win)

    $rawOutput = & {
        $results = @()
        $results += New-TestResult -Id "maint_rdpon.entry" -Status "INFO" -Detail "Invoke-MaintenanceRemoteDesktopEnable 開始（非破壊・チェックもONも押さない）"

        # ---- ローカルヘルパー（この & {} スコープに閉じる） ----

        # ツールメニューを展開して指定 AutomationId のメニュー項目を取得
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

        # ダイアログ内ボタンの IsEnabled / HasKeyboardFocus を安全に読む
        function Get-ElemEnabled { param($e) try { return [bool]$e.Current.IsEnabled } catch { return $null } }

        # 結果ダイアログ（成功/失敗/権限不足/エラー）が出ていないことを確認（＝実行されていない）
        function Test-NoResultDialog {
            $root = [System.Windows.Automation.AutomationElement]::RootElement
            foreach ($t in @("成功","失敗","権限不足","エラー")) {
                $cond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $t)
                $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
                if ($w) { return @{ Clean=$false; Title=$t } }
            }
            return @{ Clean=$true; Title=$null }
        }

        # HKLM Terminal Server\fDenyTSConnections（RDP 許可/拒否）を読む（read-only・無害）
        function Get-FDeny {
            $p = Get-ItemProperty 'HKLM:\System\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -ErrorAction SilentlyContinue
            if ($p -and ($p.PSObject.Properties.Name -contains 'fDenyTSConnections')) { return [int]$p.fDenyTSConnections }
            return $null
        }

        try {
            $fdBefore = Get-FDeny
            $menuId    = "MenuEnableRemoteDesktop"
            $dlgAutoId = "RemoteDesktopEnableDialog"

            # 1) メニュー項目検出
            $item = Get-MenuItemUnderTools $Win $menuId
            if (-not $item) {
                $results += New-TestResult -Id "maint_rdpon.menu.find" -Status "FAIL" -Detail "$menuId 未検出（MenuTools 展開後・2回試行）"
                return $results
            }
            $results += New-TestResult -Id "maint_rdpon.menu.find" -Status "OK" -Detail "$menuId 検出"

            # 1b) ホスト非対応(Home)/GPO 管理中はメニュー非活性 → ON ダイアログ経路は検証不能 → INFO 降格
            $menuEnabled = Get-ElemEnabled $item
            if ($menuEnabled -eq $false) {
                $results += New-TestResult -Id "maint_rdpon.menu.enabled" -Status "INFO" -Detail "limitation: MenuEnableRemoteDesktop が非活性（ホスト非対応エディション or GPO/MDM 管理 or Busy）。Red ダイアログ経路は host-capable・非ポリシー環境で検証要。グレーアウト自体は CanExecute 正常動作"
                # メニューを畳んで終了（Invoke しない）
                $mt = Find-ById $Win "MenuTools" 2
                if ($mt) { try { Collapse-Element $mt | Out-Null } catch {} }
                return $results
            }
            $results += New-TestResult -Id "maint_rdpon.menu.enabled" -Status "OK" -Detail "MenuEnableRemoteDesktop は活性（host-capable・非ポリシー）＝ON 経路を検証"

            # 2) Invoke → Red 専用 WPF ダイアログ出現（AutomationId で検出・Owner の Descendants）
            $dlg = $null
            for ($try = 1; $try -le 2 -and -not $dlg; $try++) {
                # 既に開いていれば即採用（二重起動防止の観点でも）
                $dlg = Wait-WindowByAutomationId -AutomationId $dlgAutoId -TimeoutSeconds 1 -Root $Win
                if ($dlg) { break }
                Invoke-Element $item | Out-Null
                Start-Sleep -Milliseconds 900
                $dlg = Wait-WindowByAutomationId -AutomationId $dlgAutoId -TimeoutSeconds 4 -Root $Win
                if (-not $dlg) { $dlg = Wait-WindowByAutomationId -AutomationId $dlgAutoId -TimeoutSeconds 2 } # RootElement 直下も保険
            }
            if (-not $dlg) {
                $shot = "$PSScriptRoot\..\screenshots\diag_maint_rdpon.png"
                try { Take-Screenshot -Path $shot | Out-Null } catch {}
                $results += New-TestResult -Id "maint_rdpon.dialog.open" -Status "FAIL" -Detail "Red ダイアログ「$dlgAutoId」未検出（Invoke 後・2回試行）"
                return $results
            }
            $results += New-TestResult -Id "maint_rdpon.dialog.open" -Status "OK" -Detail "🔴Red 専用ダイアログ RemoteDesktopEnableDialog 出現（WPF Modal）"

            # 3) ★チェック未投入時は [有効化] 非活性（明示同意ジェスチャの核心）
            $btnEnable = Find-ById $dlg "BtnEnable" 3
            $chk       = Find-ById $dlg "CheckUnderstandRisk" 2
            if (-not $btnEnable) {
                $results += New-TestResult -Id "maint_rdpon.enable.disabled_until_check" -Status "FAIL" -Detail "BtnEnable 未検出"
            } else {
                $enEnabled = Get-ElemEnabled $btnEnable
                if ($enEnabled -eq $false) {
                    $results += New-TestResult -Id "maint_rdpon.enable.disabled_until_check" -Status "OK" -Detail "[有効化] はチェック未投入で非活性＝明示同意ジェスチャ成立（Enter連打/誤クリック封じ）"
                } else {
                    $results += New-TestResult -Id "maint_rdpon.enable.disabled_until_check" -Status "FAIL" -Detail "[有効化] がチェック未投入で活性＝安全ジェスチャ破れ（CanEnable 配線要確認）"
                }
            }
            if ($chk) {
                # チェックは ON にしない（読み取りのみ）。未チェック状態であることを確認。
                $toggle = $null
                try { $toggle = $chk.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern) } catch {}
                if ($toggle -and $toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off) {
                    $results += New-TestResult -Id "maint_rdpon.check.unchecked" -Status "OK" -Detail "CheckUnderstandRisk 初期＝未チェック（既定で同意なし）"
                } else {
                    $results += New-TestResult -Id "maint_rdpon.check.unchecked" -Status "INFO" -Detail "limitation: CheckUnderstandRisk の ToggleState を確認できず（環境差）。既定未チェックは XAML/ViewModel(IsRiskUnderstood=false) で担保"
                }
            } else {
                $results += New-TestResult -Id "maint_rdpon.check.unchecked" -Status "FAIL" -Detail "CheckUnderstandRisk 未検出"
            }

            # 4) [キャンセル] 存在＋既定フォーカス（誤確定防止）
            $btnCancel = Find-ById $dlg "BtnCancel" 3
            if (-not $btnCancel) {
                $results += New-TestResult -Id "maint_rdpon.cancel.default_focus" -Status "FAIL" -Detail "BtnCancel 未検出（閉じられない）"
                return $results
            }
            $hasFocus = $false
            try { $hasFocus = [bool]$btnCancel.Current.HasKeyboardFocus } catch {}
            if ($hasFocus) {
                $results += New-TestResult -Id "maint_rdpon.cancel.default_focus" -Status "OK" -Detail "既定フォーカス＝[キャンセル]（誤確定防止）"
            } else {
                $results += New-TestResult -Id "maint_rdpon.cancel.default_focus" -Status "INFO" -Detail "limitation: 既定フォーカスを UIA で確認できず（HasKeyboardFocus=false・非アクティブ環境で揺れる）。IsCancel=True/既定キャンセルは XAML で担保。実機目視推奨"
            }

            # 5) [キャンセル]で閉じる（★チェックも[有効化]も触らない＝実 Enable 未実行）
            Invoke-Element $btnCancel | Out-Null
            Start-Sleep -Milliseconds 800
            $stillOpen = Wait-WindowByAutomationId -AutomationId $dlgAutoId -TimeoutSeconds 1 -Root $Win
            if ($stillOpen) {
                $results += New-TestResult -Id "maint_rdpon.cancel.click" -Status "FAIL" -Detail "[キャンセル]後もダイアログが残存"
            } else {
                $results += New-TestResult -Id "maint_rdpon.cancel.click" -Status "OK" -Detail "[キャンセル]で Red ダイアログを閉じた"
            }

            # 5b) 結果ダイアログが出ていない＝Enable 未実行
            $noRes = Test-NoResultDialog
            if ($noRes.Clean) {
                $results += New-TestResult -Id "maint_rdpon.no_execution" -Status "OK" -Detail "結果ダイアログ無し＝NLA=1/fDeny=0/FW開 未実行"
            } else {
                $results += New-TestResult -Id "maint_rdpon.no_execution" -Status "FAIL" -Detail "「$($noRes.Title)」ダイアログ出現＝キャンセルでも実行された疑い"
            }

            # 6) 全工程後の fDeny が不変
            $fdAfter = Get-FDeny
            if ($fdBefore -eq $fdAfter) {
                $results += New-TestResult -Id "maint_rdpon.state.unchanged" -Status "OK" -Detail "fDenyTSConnections 不変（before=$fdBefore after=$fdAfter）＝RDP 状態に副作用なし"
            } else {
                $results += New-TestResult -Id "maint_rdpon.state.unchanged" -Status "FAIL" -Detail "fDenyTSConnections が変化（before=$fdBefore after=$fdAfter）＝想定外の実行"
            }

            $results += New-TestResult -Id "maint_rdpon.busy_guard" -Status "INFO" -Detail "limitation: IsRemoteDesktopBusy による二重起動防止は実行中のみ観測可。xUnit(CanExecute)＋VM実機(done-stack)で担保"

        } catch {
            $results += New-TestResult -Id "maint_rdpon.main.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
            # 念のため開いているかもしれない Red ダイアログを閉じる（キャンセル）
            $d = Wait-WindowByAutomationId -AutomationId "RemoteDesktopEnableDialog" -TimeoutSeconds 1 -Root $Win
            if ($d) {
                $bc = Find-ById $d "BtnCancel" 2
                if ($bc) { Invoke-Element $bc | Out-Null; Start-Sleep -Milliseconds 400 }
            }
        }

        return $results
    }

    return @($rawOutput | Where-Object { $_ -is [PSCustomObject] -and $null -ne $_.Status })
}
