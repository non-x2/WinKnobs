# TC-startup-edit: Phase R-2 スタートアップ系 編集・保存・トグル動作確認（HKCU 書込発生）
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# ⚠️ HKCU 書込発生（テストエントリ WinKnobs_Test を作成・編集・トグル・削除）
# 終了時に自動 cleanup で完全削除する（finally ブロック）
#
# 検証内容：
#   1) HKCU\Run にテストエントリ作成
#   2) Startup カテゴリ選択でリスト読込 → エントリ表示
#   3) 編集ボタンで編集ダイアログ起動
#   4) Command を変更 → OK → IsDirty 状態確認（HasDirtyEntries 経由）
#   5) 保存ボタン → レジストリで値検証
#   6) CheckBox トグル → StartupApproved 12byte 検証（先頭 0x02/0x03）
#   7) cleanup（Run + StartupApproved から削除）

function Invoke-StartupEdit {
    param([Parameter(Mandatory=$true)]$Win)

    # 関数全体を script block で wrapping し、暗黙的 pipeline 出力（True/エラーストリーム漏れ）を $rawOutput に吸収
    # 末尾で PSCustomObject だけ抽出して返す
    $rawOutput = & {
        $results = @()
        $results += New-TestResult -Id "startup_edit.entry" -Status "INFO" -Detail "Invoke-StartupEdit 開始"

    $runPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    $approvedPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
    $testName = "WinKnobs_Test"

    # ─── 既存値バックアップ（万が一同名値があった場合に復元するため） ───
    $beforeRun = $null
    $beforeApprovedBytes = $null
    try {
        $beforeRun = Read-RegValue -Path $runPath -Name $testName
        $results += New-TestResult -Id "startup_edit.backup.run" -Status "INFO" -Detail "beforeRun.Exists=$($beforeRun.Exists)"
    } catch {
        $results += New-TestResult -Id "startup_edit.backup.run.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | $($_.ScriptStackTrace)"
        return $results
    }
    if (Test-Path $approvedPath) {
        try {
            $av = Get-ItemProperty -Path $approvedPath -Name $testName -ErrorAction SilentlyContinue
            if ($av) { $beforeApprovedBytes = $av.$testName }
        } catch {}
    }
    $results += New-TestResult -Id "startup_edit.backup.done" -Status "INFO" -Detail "バックアップ完了"

    try {
        # ─── 1. Setup：HKCU\Run にテストエントリ追加 ───
        Write-RegValue -Path $runPath -Name $testName -Value "notepad.exe" -Type String
        $createdRun = Read-RegValue -Path $runPath -Name $testName
        $createOk = ($createdRun.Exists -and $createdRun.Value -eq "notepad.exe")
        $results += New-TestResult -Id "startup_edit.setup.create" -Status $(if ($createOk) { "OK" } else { "FAIL" }) -Detail "HKCU\Run\$testName = notepad.exe"
        if (-not $createOk) { return $results }

        # ─── 2. Startup カテゴリ選択（Load トリガ） ───
        $cat = Find-ById $Win "Category_Startup" 5
        if (-not $cat) {
            $results += New-TestResult -Id "startup_edit.category.find" -Status "FAIL"
            return $results
        }
        Select-Element $cat | Out-Null
        Start-Sleep -Seconds 1
        $results += New-TestResult -Id "startup_edit.category.select" -Status "OK"

        # ─── 3. テストエントリ行が表示されているか確認 ───
        # WPF DataGridRow.Style の動的 AutomationId は UIA Tree に出ないケースがあるため、
        # テキスト "WinKnobs_Test" を名前に持つ要素を探して、その祖先 DataItem を行とする。
        $row = $null
        $nameCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $testName
        )
        $grid = Find-ById $Win "StartupItemsGrid" 5
        if ($grid) {
            $dataItemCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::DataItem
            )
            # 最大 5 秒間、エントリ出現を待つ
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

        if (-not $row) {
            $results += New-TestResult -Id "startup_edit.row.appear" -Status "FAIL" -Detail "$testName を含む DataGridRow が見つかりません"
            return $results
        }
        $results += New-TestResult -Id "startup_edit.row.appear" -Status "OK" -Detail "$testName 行検出（Name 検索）"

        # 行を選択
        if (-not (Select-Element $row)) {
            $results += New-TestResult -Id "startup_edit.row.select" -Status "FAIL"
            return $results
        }
        Start-Sleep -Milliseconds 400

        # ─── 4. 編集ボタンを押して編集ダイアログ起動 ───
        $editBtn = Find-ById $Win "StartupEditButton" 3
        if (-not $editBtn) {
            $results += New-TestResult -Id "startup_edit.button.find" -Status "FAIL"
            return $results
        }
        Invoke-Element $editBtn | Out-Null
        Start-Sleep -Seconds 1

        $dlg = Wait-WindowByAutomationId -AutomationId "StartupEntryEditDialog" -TimeoutSeconds 5 -Root $Win
        if (-not $dlg) {
            $results += New-TestResult -Id "startup_edit.dialog.open" -Status "FAIL" -Detail "StartupEntryEditDialog not found"
            return $results
        }
        $results += New-TestResult -Id "startup_edit.dialog.open" -Status "OK"

        # ─── 5. ダイアログ要素確認 ───
        $cmdBox = Find-ById $dlg "StartupEditCommandBox" 3
        $okBtn = Find-ById $dlg "StartupEditOkButton" 3
        $cancelBtn = Find-ById $dlg "StartupEditCancelButton" 3
        $regSzRadio = Find-ById $dlg "StartupEditRegSzRadio" 3
        $expandRadio = Find-ById $dlg "StartupEditRegExpandSzRadio" 3
        $results += New-TestResult -Id "startup_edit.dialog.CommandBox" -Status $(if ($cmdBox) { "OK" } else { "FAIL" })
        $results += New-TestResult -Id "startup_edit.dialog.OkButton" -Status $(if ($okBtn) { "OK" } else { "FAIL" })
        $results += New-TestResult -Id "startup_edit.dialog.CancelButton" -Status $(if ($cancelBtn) { "OK" } else { "FAIL" })
        $results += New-TestResult -Id "startup_edit.dialog.RegSzRadio" -Status $(if ($regSzRadio) { "OK" } else { "FAIL" })
        $results += New-TestResult -Id "startup_edit.dialog.RegExpandSzRadio" -Status $(if ($expandRadio) { "OK" } else { "FAIL" })

        # ─── 6. Command を変更 ───
        $newCmd = "notepad.exe /A"
        if ($cmdBox) {
            Set-ElementValue $cmdBox $newCmd
            Start-Sleep -Milliseconds 400
            $afterSet = Get-ElementValue $cmdBox
            $results += New-TestResult -Id "startup_edit.command.set" -Status $(if ($afterSet -eq $newCmd) { "OK" } else { "FAIL" }) -Detail "EditableCommand = $afterSet"
        }

        # ─── 7. OK で確定 ───
        if ($okBtn) {
            Invoke-Element $okBtn | Out-Null
            Start-Sleep -Seconds 1
            $results += New-TestResult -Id "startup_edit.dialog.confirm" -Status "OK"
        }

        # ─── 8. IsDirty 状態確認（💾保存ボタンが enable になっているか） ───
        $saveBtn = Find-ById $Win "StartupSaveButton" 3
        if ($saveBtn) {
            $isEnabled = $saveBtn.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsEnabledProperty)
            $results += New-TestResult -Id "startup_edit.dirty.save_enabled" -Status $(if ($isEnabled) { "OK" } else { "FAIL" }) -Detail "SaveButton.IsEnabled = $isEnabled（IsDirty 反映）"
        }

        # ─── 9. 💾保存 ───
        if ($saveBtn) {
            Invoke-Element $saveBtn | Out-Null
            Start-Sleep -Seconds 1
            $results += New-TestResult -Id "startup_edit.save.click" -Status "OK"
        }

        # ─── 10. レジストリ検証 ───
        $afterRun = Read-RegValue -Path $runPath -Name $testName
        $saveOk = ($afterRun.Exists -and $afterRun.Value -eq $newCmd)
        $results += New-TestResult -Id "startup_edit.save.verify" -Status $(if ($saveOk) { "OK" } else { "FAIL" }) -Detail "Run value = $($afterRun.Value)（期待 $newCmd）"

        # ─── 11. CheckBox トグル ───
        # 行を Name 検索で再取得（DataGridRow 動的 AutomationId 非対応のため）
        $rowAfter = $null
        $grid2 = Find-ById $Win "StartupItemsGrid" 3
        if ($grid2) {
            $dataItemCond2 = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::DataItem
            )
            $nameCond2 = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, $testName
            )
            $rows2 = $grid2.FindAll([System.Windows.Automation.TreeScope]::Descendants, $dataItemCond2)
            foreach ($r in $rows2) {
                $found = $r.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond2)
                if ($found) { $rowAfter = $r; break }
            }
        }
        if ($rowAfter) {
            $cbCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "StartupEnabledCheckBox"
            )
            $checkbox = $rowAfter.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cbCond)
            if ($checkbox) {
                try {
                    $togglePattern = $checkbox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
                    $beforeState = $togglePattern.Current.ToggleState

                    # TogglePattern.Toggle() は WPF + UIA で "Unrecognized error" を返すケースがある
                    # → Click と等価な InvokePattern.Invoke() で代替（CheckBox は ToggleButton 派生で両方実装）
                    $invokeOk = Invoke-Element $checkbox
                    if (-not $invokeOk) {
                        # フォールバック：TogglePattern.Toggle() を試す
                        $togglePattern.Toggle()
                    }
                    Start-Sleep -Milliseconds 1000

                    $togglePattern2 = $checkbox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
                    $afterState = $togglePattern2.Current.ToggleState
                    $results += New-TestResult -Id "startup_edit.toggle.click" -Status "OK" -Detail "ToggleState: $beforeState → $afterState (Invoke 経由)"

                    # StartupApproved\Run のバイナリ確認
                    $approvedBytes = $null
                    if (Test-Path $approvedPath) {
                        try {
                            $av = Get-ItemProperty -Path $approvedPath -Name $testName -ErrorAction SilentlyContinue
                            if ($av) { $approvedBytes = $av.$testName }
                        } catch {}
                    }

                    if ($approvedBytes -and $approvedBytes.Length -ge 12) {
                        $firstByte = "{0:X2}" -f $approvedBytes[0]
                        $expectedByte = if ($afterState -eq [System.Windows.Automation.ToggleState]::Off) { "03" } else { "02" }
                        $byteOk = ($firstByte -eq $expectedByte)
                        $results += New-TestResult -Id "startup_edit.toggle.verify_byte" -Status $(if ($byteOk) { "OK" } else { "FAIL" }) -Detail "byte[0]=0x$firstByte（期待 0x$expectedByte / 12byte length OK）"
                    } else {
                        $len = if ($approvedBytes) { $approvedBytes.Length } else { 0 }
                        $results += New-TestResult -Id "startup_edit.toggle.verify_byte" -Status "FAIL" -Detail "StartupApproved entry not found or length=$len（期待 12）"
                    }

                    # 再度トグル（元に戻す）：Invoke 経由
                    Invoke-Element $checkbox | Out-Null
                    Start-Sleep -Milliseconds 1000
                    $results += New-TestResult -Id "startup_edit.toggle.revert" -Status "OK" -Detail "状態を戻した（Invoke 経由）"
                } catch {
                    $results += New-TestResult -Id "startup_edit.toggle.exception" -Status "FAIL" -Detail $_.Exception.Message
                }
            } else {
                $results += New-TestResult -Id "startup_edit.toggle.checkbox" -Status "FAIL" -Detail "StartupEnabledCheckBox not found"
            }
        }

    } catch {
        $results += New-TestResult -Id "startup_edit.main.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
    } finally {
        # ─── Cleanup：必ず実行（テストエントリを完全削除） ───
        try {
            if (Test-Path $runPath) {
                # 元々存在しなかった → 削除 / 既存値あれば復元
                if ($null -eq $beforeRun -or -not $beforeRun.Exists) {
                    Remove-ItemProperty -Path $runPath -Name $testName -ErrorAction SilentlyContinue
                } else {
                    Set-ItemProperty -Path $runPath -Name $testName -Value $beforeRun.Value -ErrorAction SilentlyContinue
                }
            }
            if (Test-Path $approvedPath) {
                if ($null -eq $beforeApprovedBytes) {
                    Remove-ItemProperty -Path $approvedPath -Name $testName -ErrorAction SilentlyContinue
                } else {
                    Set-ItemProperty -Path $approvedPath -Name $testName -Value $beforeApprovedBytes -Type Binary -ErrorAction SilentlyContinue
                }
            }
            # 検証：確実に消えたか
            $finalRun = Read-RegValue -Path $runPath -Name $testName
            $beforeExists = if ($null -eq $beforeRun) { $false } else { $beforeRun.Exists }
            $cleanOk = ($beforeExists -eq $finalRun.Exists)
            $results += New-TestResult -Id "startup_edit.cleanup" -Status $(if ($cleanOk) { "OK" } else { "FAIL" }) -Detail "Run cleanup（before.Exists=$beforeExists / final.Exists=$($finalRun.Exists)）"
        } catch {
            $results += New-TestResult -Id "startup_edit.cleanup.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
        }
    }

        # script block の戻り値（$rawOutput に集まる全暗黙出力 + $results）
        return $results
    }

    # PowerShell の暗黙的 pipeline 出力混入対策：PSCustomObject 以外（boolean 等）を除去
    return @($rawOutput | Where-Object { $_ -is [PSCustomObject] })
}
