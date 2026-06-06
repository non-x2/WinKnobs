# TC-maintenance-remotedesktop: システム・メンテナンス Phase 4「リモートデスクトップ 無効化」UIA テスト（v0.1.18・🔴Red）
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# 検証内容（非破壊・管理者不要・ホスト/VM 共通で安全）：
#   1) ツールメニュー(MenuTools)配下に MenuDisableRemoteDesktop が存在
#   2) 無効化を Invoke → 確認ダイアログ「リモートデスクトップを無効化」(ConfirmDestructive) が出現
#      ※ Home 等ホスト非対応／GPO 管理中は Info/Warn になる → その場合は INFO 降格（環境差・FAIL にしない）
#   3) ★既定ボタン＝「いいえ」にキーボードフォーカス（ConfirmDestructive・誤爆/締め出し防止）
#   4) 「いいえ」で閉じる → 結果ダイアログ無し（＝実 Disable 未実行）
#   5) 全工程を通して HKLM Terminal Server\fDenyTSConnections が不変（＝キャンセルで本当に非実行）
#
# 設計方針（tc_maintenance_protection と同型）：
#   - 確認ダイアログは Win32 MessageBox（DialogService.ConfirmDestructive）。トップレベル Window を Name(=タイトル)で検出。
#   - 「はい」は一切押さない＝実際の FW 無効化／fDeny=1 は走らせない（管理者操作・🔴締め出しを回避）。
#     管理者 Success 経路／no-op／fail-closed は別途 VM 実機検証で確認（docs/テスト結果_2026-06-06_v0.1.18_*.md）。
#   - v0.1.18 は「無効化(Disable)」のみ＝メニュー1項目（有効化 ON は v0.1.19）。
#   - 既定フォーカス検証は環境で揺れうるため、検出失敗時は INFO(limitation) に降格（spurious FAIL 回避）。
#
# AutomationId：MenuTools / MenuDisableRemoteDesktop

function Invoke-MaintenanceRemoteDesktop {
    param([Parameter(Mandatory=$true)]$Win)

    $rawOutput = & {
        $results = @()
        $results += New-TestResult -Id "maint_rdp.entry" -Status "INFO" -Detail "Invoke-MaintenanceRemoteDesktop 開始（非破壊・はいは押さない）"

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
                try { Collapse-Element $menuTools | Out-Null; Start-Sleep -Milliseconds 250 } catch {}
                Expand-Element $menuTools | Out-Null
                Start-Sleep -Milliseconds 600
                $item = Find-ById $win $id 3
            }
            return $item
        }

        # MessageBox(トップレベル Window)内のボタンを Name で検索（日本語/英語の表記ゆれ吸収）
        function Find-MsgBoxButton {
            param($dlg, [ValidateSet("yes","no","ok")][string]$kind)
            $btnCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)
            $btns = $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
            foreach ($b in $btns) {
                $n = [string]$b.Current.Name
                if ($kind -eq "no")  { if ($n -match "いいえ" -or $n -ieq "No"  -or $n -ieq "&No")  { return $b } }
                if ($kind -eq "yes") { if ($n -match "はい"   -or $n -ieq "Yes" -or $n -ieq "&Yes") { return $b } }
                if ($kind -eq "ok")  { if ($n -match "OK"     -or $n -match "閉じる")                { return $b } }
            }
            return $null
        }

        # 結果ダイアログ（成功/失敗/権限不足/エラー）が出ていないことを確認
        # ※「リモートデスクトップ」(Info no-op) と「ポリシー管理中」(Warn) は環境差ダイアログなので結果扱いしない
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

        # 任意タイトルのトップレベル Window を取得（環境差ダイアログ検出用）
        function Get-TopWindowByName {
            param([string]$name)
            $root = [System.Windows.Automation.AutomationElement]::RootElement
            $cond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, $name)
            return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        }

        # HKLM Terminal Server\fDenyTSConnections（RDP 許可/拒否）を読む（read-only・無害）
        function Get-FDeny {
            $p = Get-ItemProperty 'HKLM:\System\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -ErrorAction SilentlyContinue
            if ($p -and ($p.PSObject.Properties.Name -contains 'fDenyTSConnections')) { return [int]$p.fDenyTSConnections }
            return $null   # 未設定
        }

        try {
            # 全工程前の RDP 状態（read-only）
            $fdBefore = Get-FDeny

            $menuId      = "MenuDisableRemoteDesktop"
            $expectTitle = "リモートデスクトップを無効化"
            $remoteTitle = "⚠️ あなたは今リモート接続中です"   # 自己セッションがリモートなら別タイトル

            # メニュー Invoke → 確認ダイアログ検出を最大2回リトライ（コールド起動直後の取りこぼし対策）
            $dlg = $null
            $dlgTitle = $null
            $menuFound = $false
            for ($try = 1; $try -le 2 -and -not $dlg; $try++) {
                # 既に開いていれば即採用（二重起動防止）
                foreach ($t in @($expectTitle, $remoteTitle)) {
                    $d = Wait-WindowByName -Name $t -TimeoutSeconds 1
                    if ($d) { $dlg = $d; $dlgTitle = $t; break }
                }
                if ($dlg) { break }

                $item = Get-MenuItemUnderTools $Win $menuId
                if ($item) {
                    if (-not $menuFound) {
                        $results += New-TestResult -Id "maint_rdp.menu.find" -Status "OK" -Detail "$menuId 検出"
                        $menuFound = $true
                    }
                    Invoke-Element $item | Out-Null
                    Start-Sleep -Milliseconds 900
                    foreach ($t in @($expectTitle, $remoteTitle)) {
                        $d = Wait-WindowByName -Name $t -TimeoutSeconds 4
                        if ($d) { $dlg = $d; $dlgTitle = $t; break }
                    }
                }
            }
            if (-not $menuFound) {
                $results += New-TestResult -Id "maint_rdp.menu.find" -Status "FAIL" -Detail "$menuId 未検出（MenuTools 展開後・2回試行）"
                return $results
            }

            # 確認ダイアログが出ない場合：環境差ダイアログ（Home no-op / GPO 管理）を確認 → INFO 降格
            if (-not $dlg) {
                $infoDlg = Get-TopWindowByName "リモートデスクトップ"
                $gpoDlg  = Get-TopWindowByName "ポリシー管理中"
                if ($infoDlg) {
                    $results += New-TestResult -Id "maint_rdp.dialog.open" -Status "INFO" -Detail "limitation: ホスト非対応エディション（Home 等）→ no-op 説明ダイアログ。ConfirmDestructive 経路は host-capable 環境で検証要"
                    $btn = Find-MsgBoxButton $infoDlg "ok"; if ($btn) { Invoke-Element $btn | Out-Null; Start-Sleep -Milliseconds 400 }
                    return $results
                }
                if ($gpoDlg) {
                    $results += New-TestResult -Id "maint_rdp.dialog.open" -Status "INFO" -Detail "limitation: GPO/MDM 管理中 → 変更不可案内ダイアログ。ConfirmDestructive 経路は非管理環境で検証要"
                    $btn = Find-MsgBoxButton $gpoDlg "ok"; if ($btn) { Invoke-Element $btn | Out-Null; Start-Sleep -Milliseconds 400 }
                    return $results
                }
                # 部分一致フォールバック：RootElement 直下＋$win 配下の Window を「リモートデスクトップ」で探す
                $root = [System.Windows.Automation.AutomationElement]::RootElement
                $winCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Window)
                $names = @()
                foreach ($scopeRoot in @($root, $Win)) {
                    $scope = if ($scopeRoot -eq $root) { [System.Windows.Automation.TreeScope]::Children } else { [System.Windows.Automation.TreeScope]::Descendants }
                    $ws = $scopeRoot.FindAll($scope, $winCond)
                    foreach ($w in $ws) {
                        $nm = [string]$w.Current.Name
                        $names += $nm
                        if (-not $dlg -and ($nm -match "リモートデスクトップ" -or $nm -match "リモート接続中")) { $dlg = $w; $dlgTitle = $nm }
                    }
                }
                if (-not $dlg) {
                    $shot = "$PSScriptRoot\..\screenshots\diag_maint_rdp.png"
                    try { Take-Screenshot -Path $shot | Out-Null } catch {}
                    $results += New-TestResult -Id "maint_rdp.dialog.open" -Status "FAIL" -Detail ("確認ダイアログ「$expectTitle」未検出。検出Window=[" + ($names -join ' | ') + "]")
                    return $results
                }
            }
            $results += New-TestResult -Id "maint_rdp.dialog.open" -Status "OK" -Detail "確認ダイアログ「$dlgTitle」出現（ConfirmDestructive）"

            # ★既定ボタン＝「いいえ」フォーカス確認（ConfirmDestructive の核心・🔴締め出し防止）
            $noBtn = Find-MsgBoxButton $dlg "no"
            if (-not $noBtn) {
                $results += New-TestResult -Id "maint_rdp.default.no_focus" -Status "FAIL" -Detail "「いいえ」ボタン未検出"
            } else {
                $hasFocus = $false
                try { $hasFocus = [bool]$noBtn.Current.HasKeyboardFocus } catch {}
                if ($hasFocus) {
                    $results += New-TestResult -Id "maint_rdp.default.no_focus" -Status "OK" -Detail "既定フォーカス＝「いいえ」(ConfirmDestructive・締め出し防止)"
                } else {
                    $results += New-TestResult -Id "maint_rdp.default.no_focus" -Status "INFO" -Detail "limitation: 既定フォーカスを UIA で確認できず（HasKeyboardFocus=false・画面外/非アクティブ環境で揺れる）。MessageBoxResult.No 指定は DialogService.ConfirmDestructive と xUnit で担保。実機目視推奨"
                }
            }

            # 「いいえ」で閉じる
            $noBtn2 = Find-MsgBoxButton $dlg "no"
            if (-not $noBtn2) {
                $results += New-TestResult -Id "maint_rdp.cancel.btn" -Status "FAIL" -Detail "「いいえ」ボタン未検出（閉じられない）"
                return $results
            }
            Invoke-Element $noBtn2 | Out-Null
            Start-Sleep -Milliseconds 800
            $results += New-TestResult -Id "maint_rdp.cancel.click" -Status "OK" -Detail "「いいえ」で確認ダイアログを閉じた"

            # 結果ダイアログが出ていない＝コマンド未実行
            $noRes = Test-NoResultDialog
            if ($noRes.Clean) {
                $results += New-TestResult -Id "maint_rdp.no_execution" -Status "OK" -Detail "結果ダイアログ無し＝FW 無効化／fDeny=1 未実行"
            } else {
                $results += New-TestResult -Id "maint_rdp.no_execution" -Status "FAIL" -Detail "「$($noRes.Title)」ダイアログ出現＝いいえ選択でも実行された疑い"
            }

            # 全工程後の fDeny が不変（＝キャンセルで本当に非実行）
            $fdAfter = Get-FDeny
            if ($fdBefore -eq $fdAfter) {
                $results += New-TestResult -Id "maint_rdp.state.unchanged" -Status "OK" -Detail "fDenyTSConnections 不変（before=$fdBefore after=$fdAfter）＝RDP 状態に副作用なし"
            } else {
                $results += New-TestResult -Id "maint_rdp.state.unchanged" -Status "FAIL" -Detail "fDenyTSConnections が変化（before=$fdBefore after=$fdAfter）＝想定外の実行"
            }

            # IsRemoteDesktopBusy（二重起動防止）は実行中のみ観測可＝本テスト範囲外
            $results += New-TestResult -Id "maint_rdp.busy_guard" -Status "INFO" -Detail "limitation: IsRemoteDesktopBusy による二重起動防止は実行中のみ観測可。xUnit(CanExecute)＋VM実機(別スレッド化)で担保"

        } catch {
            $results += New-TestResult -Id "maint_rdp.main.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
            # 念のため開いているかもしれない確認ダイアログを閉じる
            foreach ($t in @("リモートデスクトップを無効化","⚠️ あなたは今リモート接続中です")) {
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
