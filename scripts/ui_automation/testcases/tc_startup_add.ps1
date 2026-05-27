# TC-startup-add: Phase R-3 スタートアップ系 新規追加動作確認（HKCU 書込発生）
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# ⚠️ HKCU 書込発生（テストエントリ WinKnobs_AutoTest_<GUID> を新規作成）
# 終了時に自動 cleanup で完全削除する（finally ブロック）
#
# 検証内容：
#   1) Startup カテゴリ選択
#   2) +追加ボタン → 編集ダイアログ起動（Add モード）
#   3) Hive ComboBox / Name TextBox / Command TextBox の Add モード要素確認
#   4) Name に WinKnobs_AutoTest_<GUID>・Command に notepad.exe を入力
#   5) OK ボタン → ダイアログ閉じる → 一覧に新規エントリ表示
#   6) レジストリ検証：HKCU\Run\WinKnobs_AutoTest_<GUID> = notepad.exe
#   7) cleanup（Run + StartupApproved から削除）
#
# R-3 plan-validator 致命中6 反映：
#   - Name に GUID 8 文字付与でユニーク化（前回テストの残留があっても衝突しない）
#   - 冒頭で同名値の先掃除（万一の残留対策）
#   - try/finally で末尾 cleanup 保証

function Invoke-StartupAdd {
    param([Parameter(Mandatory=$true)]$Win)

    # 関数全体を script block で wrapping し、暗黙的 pipeline 出力を吸収
    $rawOutput = & {
        $results = @()

        $runPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
        $approvedPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
        # ユニーク名（R-3 plan-validator 致命中6：前回残留があっても衝突しない）
        $shortGuid = ([guid]::NewGuid().Guid.Substring(0, 8))
        $testName = "WinKnobs_AutoTest_$shortGuid"

        $results += New-TestResult -Id "startup_add.entry" -Status "INFO" -Detail "Invoke-StartupAdd 開始（testName=$testName）"

        # 念のため事前掃除（同名が万一残っていたら削除）
        try {
            if (Test-Path $runPath) {
                Remove-ItemProperty -Path $runPath -Name $testName -ErrorAction SilentlyContinue
            }
            if (Test-Path $approvedPath) {
                Remove-ItemProperty -Path $approvedPath -Name $testName -ErrorAction SilentlyContinue
            }
        } catch {}

        try {
            # ─── 1. Startup カテゴリ選択 ───
            $cat = Find-ById $Win "Category_Startup" 5
            if (-not $cat) {
                $results += New-TestResult -Id "startup_add.category.find" -Status "FAIL" -Detail "Category_Startup AutomationId not found"
                return $results
            }
            Select-Element $cat | Out-Null
            Start-Sleep -Seconds 1
            $results += New-TestResult -Id "startup_add.category.select" -Status "OK"

            # ─── 2. +追加ボタン押下 → ダイアログ起動 ───
            $addBtn = Find-ById $Win "StartupAddButton" 3
            if (-not $addBtn) {
                $results += New-TestResult -Id "startup_add.button.find" -Status "FAIL" -Detail "StartupAddButton not found"
                return $results
            }
            $isEnabled = $addBtn.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsEnabledProperty)
            $results += New-TestResult -Id "startup_add.button.enabled" -Status $(if ($isEnabled) { "OK" } else { "FAIL" }) -Detail "AddButton.IsEnabled=$isEnabled"

            Invoke-Element $addBtn | Out-Null
            Start-Sleep -Seconds 1

            $dlg = Wait-WindowByAutomationId -AutomationId "StartupEntryEditDialog" -TimeoutSeconds 5 -Root $Win
            if (-not $dlg) {
                $results += New-TestResult -Id "startup_add.dialog.open" -Status "FAIL" -Detail "StartupEntryEditDialog not found"
                return $results
            }
            $results += New-TestResult -Id "startup_add.dialog.open" -Status "OK"

            # ─── 3. Add モード要素確認（Hive ComboBox / Name TextBox 表示） ───
            $hiveCombo = Find-ById $dlg "StartupEditHiveCombo" 3
            $nameBox = Find-ById $dlg "StartupEditNameBox" 3
            $cmdBox = Find-ById $dlg "StartupEditCommandBox" 3
            $okBtn = Find-ById $dlg "StartupEditOkButton" 3
            $cancelBtn = Find-ById $dlg "StartupEditCancelButton" 3

            $results += New-TestResult -Id "startup_add.dialog.HiveCombo" -Status $(if ($hiveCombo) { "OK" } else { "FAIL" }) -Detail "Add モードで Hive ComboBox 表示"
            $results += New-TestResult -Id "startup_add.dialog.NameBox" -Status $(if ($nameBox) { "OK" } else { "FAIL" }) -Detail "Add モードで Name TextBox 表示"
            $results += New-TestResult -Id "startup_add.dialog.CommandBox" -Status $(if ($cmdBox) { "OK" } else { "FAIL" })
            $results += New-TestResult -Id "startup_add.dialog.OkButton" -Status $(if ($okBtn) { "OK" } else { "FAIL" })
            $results += New-TestResult -Id "startup_add.dialog.CancelButton" -Status $(if ($cancelBtn) { "OK" } else { "FAIL" })

            # ─── 4. Name と Command を入力（既定 Hive=HKCU_Run のまま） ───
            if ($nameBox) {
                Set-ElementValue $nameBox $testName
                Start-Sleep -Milliseconds 400
                $afterName = Get-ElementValue $nameBox
                $results += New-TestResult -Id "startup_add.name.set" -Status $(if ($afterName -eq $testName) { "OK" } else { "FAIL" }) -Detail "EditableName=$afterName"
            }

            if ($cmdBox) {
                Set-ElementValue $cmdBox "notepad.exe"
                Start-Sleep -Milliseconds 400
                $afterCmd = Get-ElementValue $cmdBox
                $results += New-TestResult -Id "startup_add.command.set" -Status $(if ($afterCmd -eq "notepad.exe") { "OK" } else { "FAIL" }) -Detail "EditableCommand=$afterCmd"
            }

            # ─── 5. OK ボタンクリック → ダイアログ閉じる ───
            if ($okBtn) {
                $okEnabled = $okBtn.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsEnabledProperty)
                $results += New-TestResult -Id "startup_add.ok.enabled" -Status $(if ($okEnabled) { "OK" } else { "FAIL" }) -Detail "OK.IsEnabled=$okEnabled（IsValid 連動）"

                Invoke-Element $okBtn | Out-Null
                Start-Sleep -Seconds 1
                $results += New-TestResult -Id "startup_add.ok.click" -Status "OK"
            }

            # ─── 6. レジストリ検証（HKCU\Run\WinKnobs_AutoTest_<GUID> = notepad.exe） ───
            $afterRun = Read-RegValue -Path $runPath -Name $testName
            $addOk = ($afterRun.Exists -and $afterRun.Value -eq "notepad.exe")
            $results += New-TestResult -Id "startup_add.registry.verify" -Status $(if ($addOk) { "OK" } else { "FAIL" }) -Detail "HKCU\Run\$testName = $($afterRun.Value)"

            # ─── 7. 一覧反映確認（Name 検索） ───
            $grid = Find-ById $Win "StartupItemsGrid" 5
            $row = $null
            if ($grid) {
                $dataItemCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::DataItem
                )
                $nameCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $testName
                )
                $deadline = (Get-Date).AddSeconds(5)
                while ((Get-Date) -lt $deadline -and -not $row) {
                    $rows = $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $dataItemCond)
                    foreach ($r in $rows) {
                        $found = $r.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
                        if ($found) { $row = $r; break }
                    }
                    if (-not $row) { Start-Sleep -Milliseconds 300 }
                }
            }
            $results += New-TestResult -Id "startup_add.list.reflect" -Status $(if ($row) { "OK" } else { "FAIL" }) -Detail "$testName 行が一覧に表示"

        } catch {
            $results += New-TestResult -Id "startup_add.main.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
        } finally {
            # ─── Cleanup：必ず実行（テストエントリを完全削除） ───
            try {
                if (Test-Path $runPath) {
                    Remove-ItemProperty -Path $runPath -Name $testName -ErrorAction SilentlyContinue
                }
                if (Test-Path $approvedPath) {
                    Remove-ItemProperty -Path $approvedPath -Name $testName -ErrorAction SilentlyContinue
                }
                $finalRun = Read-RegValue -Path $runPath -Name $testName
                $cleanOk = -not $finalRun.Exists
                $results += New-TestResult -Id "startup_add.cleanup" -Status $(if ($cleanOk) { "OK" } else { "FAIL" }) -Detail "Run cleanup final.Exists=$($finalRun.Exists)"
            } catch {
                $results += New-TestResult -Id "startup_add.cleanup.exception" -Status "FAIL" -Detail "$($_.Exception.Message)"
            }
        }

        return $results
    }

    # PowerShell の暗黙的 pipeline 出力混入対策：PSCustomObject 以外を除去
    return @($rawOutput | Where-Object { $_ -is [PSCustomObject] })
}
