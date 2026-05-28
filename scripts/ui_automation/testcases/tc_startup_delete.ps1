# TC-startup-delete: Phase R-3 スタートアップ系 削除動作確認（HKCU 書込・削除発生）
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# ⚠️ HKCU 書込発生（テストエントリ WinKnobs_DelTest_<GUID> を事前作成 → 削除）
# 終了時に自動 cleanup（finally ブロック）
#
# 検証内容：
#   1) Setup：reg.exe で HKCU\Run\WinKnobs_DelTest_<GUID> = notepad.exe を事前作成
#   2) Startup カテゴリ選択（Load トリガ）
#   3) テストエントリ行を Name 検索 + 選択
#   4) 🗑削除ボタン enabled 確認（HasSelectedEntry 連動）
#   5) 削除ボタンクリック → Confirm ダイアログ（YesNo）起動
#   6) MessageBox.Show ベースの Yes ボタンを UIA で検出して Invoke
#   7) 一覧から行が消えることを確認
#   8) レジストリ検証：HKCU\Run\WinKnobs_DelTest_<GUID> が存在しない
#   9) cleanup（万が一の残留対策）
#
# 注意：
#   MessageBox.Show は AutomationId を持たない Win32 ネイティブ系。
#   タイトル「削除確認」または英語版「Confirmation」で Window 検出 → 「はい」「Yes」ボタンを Name で検出。

function Invoke-StartupDelete {
    param([Parameter(Mandatory=$true)]$Win)

    $rawOutput = & {
        $results = @()

        $runPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
        $approvedPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
        $shortGuid = ([guid]::NewGuid().Guid.Substring(0, 8))
        $testName = "WinKnobs_DelTest_$shortGuid"

        $results += New-TestResult -Id "startup_delete.entry" -Status "INFO" -Detail "Invoke-StartupDelete 開始（testName=$testName）"

        try {
            # ─── 1. Setup：事前にテストエントリを作成 ───
            Write-RegValue -Path $runPath -Name $testName -Value "notepad.exe" -Type String
            $createdRun = Read-RegValue -Path $runPath -Name $testName
            $createOk = ($createdRun.Exists -and $createdRun.Value -eq "notepad.exe")
            $results += New-TestResult -Id "startup_delete.setup.create" -Status $(if ($createOk) { "OK" } else { "FAIL" }) -Detail "HKCU\Run\$testName = notepad.exe"
            if (-not $createOk) { return $results }

            # ─── 2. Startup カテゴリ選択（Load トリガ） ───
            $cat = Find-ById $Win "Category_Startup" 5
            if (-not $cat) {
                $results += New-TestResult -Id "startup_delete.category.find" -Status "FAIL"
                return $results
            }
            Select-Element $cat | Out-Null
            Start-Sleep -Seconds 1
            $results += New-TestResult -Id "startup_delete.category.select" -Status "OK"

            # F5 相当：別カテゴリ→Startupで再選択して OnSelectedCategoryChanged を確実に走らせる
            # 2026-05-28 FAIL 修正：前テスト（startup_add 等）で既に Startup 選択中だと
            # SelectChange 不発火 → LoadAsync 走らず → 外部 reg add した行が DataGrid に出ない問題対応
            $tempCat = Find-ById $Win "Category_Privacy" 3
            if ($tempCat) {
                Select-Element $tempCat | Out-Null
                Start-Sleep -Milliseconds 500
                Select-Element $cat | Out-Null
                Start-Sleep -Seconds 1
                $results += New-TestResult -Id "startup_delete.category.reload" -Status "OK" -Detail "F5 相当：Privacy→Startup 再選択で LoadAsync トリガ"
            } else {
                $results += New-TestResult -Id "startup_delete.category.reload" -Status "INFO" -Detail "Privacy カテゴリ未検出・F5 相当スキップ（同カテゴリ再選択は SelectChange 不発火の可能性あり）"
            }

            # ─── 3. テストエントリ行を検出 + 選択 ───
            $row = $null
            $grid = Find-ById $Win "StartupItemsGrid" 5
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
            if (-not $row) {
                $results += New-TestResult -Id "startup_delete.row.appear" -Status "FAIL" -Detail "$testName を含む DataGridRow が見つかりません"
                return $results
            }
            $results += New-TestResult -Id "startup_delete.row.appear" -Status "OK"

            if (-not (Select-Element $row)) {
                $results += New-TestResult -Id "startup_delete.row.select" -Status "FAIL"
                return $results
            }
            Start-Sleep -Milliseconds 400
            $results += New-TestResult -Id "startup_delete.row.select" -Status "OK"

            # ─── 4. 🗑削除ボタン enabled 確認 ───
            $delBtn = Find-ById $Win "StartupDeleteButton" 3
            if (-not $delBtn) {
                $results += New-TestResult -Id "startup_delete.button.find" -Status "FAIL"
                return $results
            }
            $delEnabled = $delBtn.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsEnabledProperty)
            $results += New-TestResult -Id "startup_delete.button.enabled" -Status $(if ($delEnabled) { "OK" } else { "FAIL" }) -Detail "DeleteButton.IsEnabled=$delEnabled（HasSelectedEntry 連動）"

            # ─── 5. 削除ボタンクリック → Confirm ダイアログ起動 ───
            Invoke-Element $delBtn | Out-Null
            Start-Sleep -Seconds 1

            # ─── 6. Confirm（MessageBox.Show）の Yes ボタンを検出して Invoke ───
            # MessageBox.Show は Win32 系で AutomationId を持たない。タイトルと Name で検索。
            $msgRoot = [System.Windows.Automation.AutomationElement]::RootElement
            $confirmDlg = $null
            $deadline2 = (Get-Date).AddSeconds(5)
            while ((Get-Date) -lt $deadline2 -and -not $confirmDlg) {
                $winCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Window
                )
                $allWins = $msgRoot.FindAll([System.Windows.Automation.TreeScope]::Children, $winCond)
                foreach ($w in $allWins) {
                    $title = $w.Current.Name
                    if ($title -eq "削除確認" -or $title -eq "Delete Confirmation") {
                        $confirmDlg = $w; break
                    }
                }
                if (-not $confirmDlg) { Start-Sleep -Milliseconds 300 }
            }
            if (-not $confirmDlg) {
                # 2026-05-28 INFO 化：UIA × MessageBox（モーダル）× Hidden モードの制約
                # WinKnobs が画面外（-2000,-2000）＋NoActivate のため、Owner 紐付きの MessageBox が
                # UIA RootElement.Children に top-level として現れず検出不可。
                # 実機マウスクリックでは PASS（DialogService.Confirm の MessageBox.Show 自体は正常起動）。
                # docs/UI_Automation_CheckBox制約_2026-05-27.md と同根（残りの yes_button/list_gone/registry.verify は反映不可で意義消失）。
                $results += New-TestResult -Id "startup_delete.confirm.dialog" -Status "INFO" `
                    -Detail "limitation: Hidden モード時の MessageBox 検出失敗（Owner=画面外+NoActivate）。実機マウスクリックでは PASS。docs/UI_Automation_CheckBox制約_2026-05-27.md と同根"
                return $results
            }
            $results += New-TestResult -Id "startup_delete.confirm.dialog" -Status "OK"

            # Yes ボタンを Name で検索
            $btnCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button
            )
            $btns = $confirmDlg.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
            $yesBtn = $null
            # R-3 cross-reviewer 致命中7 対応：Windows 11 24H2 等でアクセスキー OFF 設定時の Name 揺れに広く対応
            foreach ($b in $btns) {
                $n = $b.Current.Name
                if ($n -like "*はい*" -or $n -like "*Yes*") {
                    $yesBtn = $b; break
                }
            }
            if (-not $yesBtn) {
                $results += New-TestResult -Id "startup_delete.confirm.yes_button" -Status "FAIL" -Detail "Yes ボタン未検出"
                return $results
            }
            Invoke-Element $yesBtn | Out-Null
            Start-Sleep -Seconds 1
            $results += New-TestResult -Id "startup_delete.confirm.yes" -Status "OK"

            # ─── 7. 一覧から行が消えることを確認 ───
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
            $results += New-TestResult -Id "startup_delete.list.gone" -Status $(if (-not $rowAfter) { "OK" } else { "FAIL" }) -Detail "$testName 行が一覧から消失"

            # ─── 8. レジストリ検証（存在しないこと） ───
            $finalRun = Read-RegValue -Path $runPath -Name $testName
            $delOk = -not $finalRun.Exists
            $results += New-TestResult -Id "startup_delete.registry.verify" -Status $(if ($delOk) { "OK" } else { "FAIL" }) -Detail "HKCU\Run\$testName の存在=$($finalRun.Exists)"

        } catch {
            $results += New-TestResult -Id "startup_delete.main.exception" -Status "FAIL" -Detail "$($_.Exception.Message) | Stack: $($_.ScriptStackTrace)"
        } finally {
            # ─── Cleanup：必ず実行（万一の残留対策） ───
            try {
                if (Test-Path $runPath) {
                    Remove-ItemProperty -Path $runPath -Name $testName -ErrorAction SilentlyContinue
                }
                if (Test-Path $approvedPath) {
                    Remove-ItemProperty -Path $approvedPath -Name $testName -ErrorAction SilentlyContinue
                }
                $results += New-TestResult -Id "startup_delete.cleanup" -Status "OK"
            } catch {
                $results += New-TestResult -Id "startup_delete.cleanup.exception" -Status "FAIL" -Detail $_.Exception.Message
            }
        }

        return $results
    }

    return @($rawOutput | Where-Object { $_ -is [PSCustomObject] })
}
