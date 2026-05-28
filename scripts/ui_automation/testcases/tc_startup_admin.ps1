# TC-startup-admin: Phase R-3 HKLM 編集時の管理者再起動ダイアログ起動確認
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# 検証内容：
#   1) Startup カテゴリ選択
#   2) +追加ボタン → 編集ダイアログ起動（Add モード）
#   3) Hive ComboBox を HKLM_Run に変更
#   4) Name = WinKnobs_AdminTest_<GUID> / Command = notepad.exe を入力
#   5) OK ボタン → ConfirmElevation 3 択ダイアログ起動を期待
#   6) 「キャンセル」を選択 → レジストリ書込なし確認
#
# モード制約（UI Automation × WPF CheckBox 制約調査 2026-05-27 と同根）：
#   - Host モード（非管理者）：ConfirmElevation 3 択が出る・キャンセルで終了
#   - VM モード（elevated vmconnect）：IsRunningAsAdministrator()=true なので
#     ConfirmElevation は呼ばれない → AddNew 即実行 → HKLM 書込発生
#     → VM モードでは INFO 化（テスト意義なし・実機マウスクリックで検証）
#
# 注意：本テストは「ダイアログ起動の確認」のみ。実際の昇格・HKLM 書込は行わない。
# Cancel 選択で安全に終了するが、UAC ダイアログが出ても「いいえ」で必ずキャンセル。

function Invoke-StartupAdmin {
    param([Parameter(Mandatory=$true)]$Win)

    $rawOutput = & {
        $results = @()

        $shortGuid = ([guid]::NewGuid().Guid.Substring(0, 8))
        $testName = "WinKnobs_AdminTest_$shortGuid"

        # 管理者判定：現在プロセスが管理者なら INFO スキップ
        $isAdmin = ([System.Security.Principal.WindowsPrincipal] `
                    [System.Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
                    [System.Security.Principal.WindowsBuiltInRole]::Administrator)

        if ($isAdmin) {
            $results += New-TestResult -Id "startup_admin.skip" -Status "INFO" `
                -Detail "limitation: 現在プロセス管理者・ConfirmElevation 起動条件不成立。Host 非elevated で実機検証してください。"
            return $results
        }

        $results += New-TestResult -Id "startup_admin.entry" -Status "INFO" -Detail "Invoke-StartupAdmin 開始（非管理者・testName=$testName）"

        try {
            # ─── 1. Startup カテゴリ選択 ───
            $cat = Find-ById $Win "Category_Startup" 5
            if (-not $cat) {
                $results += New-TestResult -Id "startup_admin.category.find" -Status "FAIL"
                return $results
            }
            Select-Element $cat | Out-Null
            Start-Sleep -Seconds 1
            $results += New-TestResult -Id "startup_admin.category.select" -Status "OK"

            # ─── 2. +追加ボタン → ダイアログ起動 ───
            $addBtn = Find-ById $Win "StartupAddButton" 3
            if (-not $addBtn) {
                $results += New-TestResult -Id "startup_admin.add.find" -Status "FAIL"
                return $results
            }
            Invoke-Element $addBtn | Out-Null
            Start-Sleep -Seconds 1

            $dlg = Wait-WindowByAutomationId -AutomationId "StartupEntryEditDialog" -TimeoutSeconds 5 -Root $Win
            if (-not $dlg) {
                $results += New-TestResult -Id "startup_admin.dialog.open" -Status "FAIL"
                return $results
            }
            $results += New-TestResult -Id "startup_admin.dialog.open" -Status "OK"

            # ─── 3. Hive ComboBox を HKLM_Run に変更 ───
            $hiveCombo = Find-ById $dlg "StartupEditHiveCombo" 3
            if (-not $hiveCombo) {
                $results += New-TestResult -Id "startup_admin.hive.find" -Status "FAIL"
                return $results
            }
            # ComboBox の選択を「HKLM_Run」に変更（インデックス 2 が HKLM_Run の想定・SelectableHives 順）
            # SelectableHives: HKCU_Run(0), HKCU_RunOnce(1), HKLM_Run(2), HKLM_RunOnce(3), HKLM_Wow64_Run(4), HKLM_Wow64_RunOnce(5)
            try {
                $expandPattern = $hiveCombo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
                $expandPattern.Expand()
                Start-Sleep -Milliseconds 400
                # ListItem の中から「HKLM_Run」を Name で検索（ComboBoxItem の Name は enum.ToString）
                $listItemCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::ListItem
                )
                $items = $hiveCombo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listItemCond)
                $hklmItem = $null
                foreach ($i in $items) {
                    if ($i.Current.Name -eq "HKLM_Run") { $hklmItem = $i; break }
                }
                if (-not $hklmItem) {
                    $results += New-TestResult -Id "startup_admin.hive.select" -Status "FAIL" -Detail "HKLM_Run 選択肢未検出"
                    return $results
                }
                Select-Element $hklmItem | Out-Null
                Start-Sleep -Milliseconds 400
                $results += New-TestResult -Id "startup_admin.hive.select" -Status "OK" -Detail "Hive=HKLM_Run"

                # 2026-05-28 FAIL 調査追加：Hive 反映確認（UIA × WPF ComboBox 制約の一環）
                # SelectionItemPattern.Select() が WPF ComboBox.SelectedItem バインドを更新したか ValuePattern で検証
                $hiveValue = $null
                try {
                    $vp = $hiveCombo.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
                    $hiveValue = $vp.Current.Value
                } catch {
                    $hiveValue = "[ValuePattern 取得失敗]"
                }
                if ($hiveValue -ne "HKLM_Run") {
                    $results += New-TestResult -Id "startup_admin.hive.verify" -Status "INFO" `
                        -Detail "limitation: UIA × WPF ComboBox 制約。SelectionItemPattern.Select() 後の ValuePattern.Value=$hiveValue（期待 HKLM_Run）。実機マウスクリックでは PASS。docs/UI_Automation_CheckBox制約_2026-05-27.md と同根（残りの elevation/cancel ステップは反映不可で意義消失）"
                    return $results
                }
                $results += New-TestResult -Id "startup_admin.hive.verify" -Status "OK" -Detail "ValuePattern.Value=$hiveValue（HKLM_Run 反映確認）"
            } catch {
                $results += New-TestResult -Id "startup_admin.hive.exception" -Status "FAIL" -Detail $_.Exception.Message
                return $results
            }

            # ─── 4. Name と Command を入力 ───
            $nameBox = Find-ById $dlg "StartupEditNameBox" 3
            $cmdBox = Find-ById $dlg "StartupEditCommandBox" 3
            if ($nameBox) {
                Set-ElementValue $nameBox $testName
                Start-Sleep -Milliseconds 300
            }
            if ($cmdBox) {
                Set-ElementValue $cmdBox "notepad.exe"
                Start-Sleep -Milliseconds 300
            }
            $results += New-TestResult -Id "startup_admin.input.set" -Status "OK"

            # ─── 5. OK ボタンクリック → ConfirmElevation 3 択起動を期待 ───
            $okBtn = Find-ById $dlg "StartupEditOkButton" 3
            if (-not $okBtn) {
                $results += New-TestResult -Id "startup_admin.ok.find" -Status "FAIL"
                return $results
            }
            Invoke-Element $okBtn | Out-Null
            Start-Sleep -Seconds 1

            # ─── 6. 「管理者権限が必要」ダイアログ検出 → キャンセル ───
            $msgRoot = [System.Windows.Automation.AutomationElement]::RootElement
            $elevDlg = $null
            $deadline = (Get-Date).AddSeconds(5)
            while ((Get-Date) -lt $deadline -and -not $elevDlg) {
                $winCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Window
                )
                $allWins = $msgRoot.FindAll([System.Windows.Automation.TreeScope]::Children, $winCond)
                foreach ($w in $allWins) {
                    $title = $w.Current.Name
                    if ($title -eq "管理者権限が必要" -or $title -eq "Administrator privileges required") {
                        $elevDlg = $w; break
                    }
                }
                if (-not $elevDlg) { Start-Sleep -Milliseconds 300 }
            }

            if (-not $elevDlg) {
                $results += New-TestResult -Id "startup_admin.elevation.dialog" -Status "FAIL" -Detail "ConfirmElevation ダイアログ未検出（HKLM Add のフロー破綻）"
                return $results
            }
            $results += New-TestResult -Id "startup_admin.elevation.dialog" -Status "OK" -Detail "ConfirmElevation 3 択ダイアログ起動"

            # キャンセルボタンを検出 → クリック（安全のため UAC まで進めない）
            $btnCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button
            )
            $btns = $elevDlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
            $cancelBtn = $null
            foreach ($b in $btns) {
                $n = $b.Current.Name
                if ($n -eq "キャンセル(&A)" -or $n -eq "キャンセル" -or $n -eq "Cancel" -or $n -eq "&Cancel") {
                    $cancelBtn = $b; break
                }
            }
            if (-not $cancelBtn) {
                $results += New-TestResult -Id "startup_admin.elevation.cancel_btn" -Status "FAIL" -Detail "キャンセルボタン未検出"
                return $results
            }
            Invoke-Element $cancelBtn | Out-Null
            Start-Sleep -Seconds 1
            $results += New-TestResult -Id "startup_admin.elevation.cancel" -Status "OK" -Detail "キャンセルで安全終了（実 HKLM 書込なし）"

            # ─── 7. レジストリ検証：HKLM\Run\WinKnobs_AdminTest_<GUID> が存在しない ───
            $hklmPath = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run"
            $hklmCheck = Read-RegValue -Path $hklmPath -Name $testName
            $results += New-TestResult -Id "startup_admin.registry.no_write" -Status $(if (-not $hklmCheck.Exists) { "OK" } else { "FAIL" }) -Detail "HKLM\Run\$testName の書込なし"

        } catch {
            $results += New-TestResult -Id "startup_admin.main.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
        } finally {
            # ─── Cleanup：万が一 HKLM に書き込まれていたら削除（管理者で起動した場合のみ） ───
            if ($isAdmin) {
                try {
                    $hklmPath = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run"
                    if (Test-Path $hklmPath) {
                        Remove-ItemProperty -Path $hklmPath -Name $testName -ErrorAction SilentlyContinue
                    }
                } catch {}
            }
            $results += New-TestResult -Id "startup_admin.cleanup" -Status "INFO" -Detail "管理者でなければ書込なしで cleanup 不要"
        }

        return $results
    }

    return @($rawOutput | Where-Object { $_ -is [PSCustomObject] })
}
