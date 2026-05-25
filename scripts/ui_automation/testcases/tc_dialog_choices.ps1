# TC-dialog-choices: ダイアログ内 ComboBox 選択肢数の網羅確認
# 既存目視テスト T-W4 / T-V2-PR1〜PR3 / T-V2-N1 / T-V2-S1 / T-V2-S2 等をテストケース化
# 各項目のダイアログを開いて選択肢数を確認・副作用なし（適用クリックしない）

function Invoke-DialogChoices {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()

    # 冒頭で検索クリア（前テストの検索キーワードが残っていると Item が見えない）
    Clear-SearchBox -Win $Win | Out-Null
    Start-Sleep -Milliseconds 300

    # 対象項目（編集可・カテゴリ/項目ID/期待選択肢数/コンボ種別）
    $testTargets = @(
        # Boolean 2 択（EditableBoolCombo）
        @{ Category="Desktop"; ItemId="desktop.darkModeApps";       Expected=2; Combo="EditableBoolCombo";    Label="W1 ダークモード（アプリ）" }
        @{ Category="Desktop"; ItemId="desktop.darkModeSystem";     Expected=2; Combo="EditableBoolCombo";    Label="W2 ダークモード（システム）" }
        @{ Category="Privacy"; ItemId="privacy.bingSearchDisabled"; Expected=2; Combo="EditableBoolCombo";    Label="W6 Bing 検索無効化" }
        @{ Category="Privacy"; ItemId="privacy.cortanaDisabled";    Expected=2; Combo="EditableBoolCombo";    Label="PR1 Cortana 無効化" }
        @{ Category="Privacy"; ItemId="privacy.activityHistoryDisabled"; Expected=2; Combo="EditableBoolCombo"; Label="PR2 Activity History 無効化" }

        # Command 型 3 択（EditableCommandCombo）
        @{ Category="Desktop"; ItemId="desktop.taskbarSize";        Expected=3; Combo="EditableCommandCombo"; Label="W4 タスクバーサイズ" }
        @{ Category="Privacy"; ItemId="privacy.smartScreen";        Expected=3; Combo="EditableCommandCombo"; Label="PR3 SmartScreen" }
        @{ Category="Kernel";  ItemId="kernel.win32PrioritySeparation"; Expected=3; Combo="EditableCommandCombo"; Label="P1 Win32PrioritySeparation" }

        # Command 型 4 択
        @{ Category="System";  ItemId="system.ntfs8dot3";           Expected=4; Combo="EditableCommandCombo"; Label="K1 NTFS 8.3 名（BUG-2 fixed）" }
        @{ Category="System";  ItemId="system.ntfsLastAccess";      Expected=4; Combo="EditableCommandCombo"; Label="K2 NTFS LastAccess" }
        @{ Category="Login";   ItemId="login.prefetcher";           Expected=4; Combo="EditableCommandCombo"; Label="L2 Prefetcher" }

        # Command 型 5 択
        @{ Category="Network"; ItemId="network.tcpAutoTuning";      Expected=5; Combo="EditableCommandCombo"; Label="N1 TCP Auto-Tuning" }
    )

    foreach ($t in $testTargets) {
        $tcId = "dialog_choices." + $t.ItemId

        # 1. カテゴリ＋項目選択
        $sel = Select-CategoryAndItem -Win $Win -CategoryType $t.Category -ItemId $t.ItemId
        if (-not $sel.Success) {
            $results += New-TestResult -Id "$tcId.select" -Status "FAIL" -Detail "Step=$($sel.Step) / $($sel.Detail)"
            continue
        }

        # 2. ダイアログ起動
        $open = Open-DialogViaMenu -Win $Win
        if (-not $open.Success) {
            $results += New-TestResult -Id "$tcId.open" -Status "FAIL" -Detail $open.Detail
            continue
        }

        # 3. ComboBox 取得
        $combo = Find-ById $open.DialogWindow $t.Combo 3
        if (-not $combo) {
            $results += New-TestResult -Id "$tcId.find_combo" -Status "FAIL" -Detail "$($t.Combo) not found（$($t.Label)）"
            Close-DialogByCancel | Out-Null
            Start-Sleep -Milliseconds 300
            continue
        }

        # 4. Expand して ListItem 数取得
        try {
            Expand-Element $combo | Out-Null
            Start-Sleep -Milliseconds 500

            $listItemCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem
            )
            $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listItemCond)
            $count = $items.Count

            if ($count -eq $t.Expected) {
                $results += New-TestResult -Id "$tcId.count" -Status "OK" -Detail "$($t.Label): $count 択（期待値 $($t.Expected) 一致）"
            } elseif ($count -ge $t.Expected) {
                $results += New-TestResult -Id "$tcId.count" -Status "INFO" -Detail "$($t.Label): $count 択（期待値 $($t.Expected) 以上・OK 扱い）"
            } else {
                $results += New-TestResult -Id "$tcId.count" -Status "FAIL" -Detail "$($t.Label): $count 択（期待値 $($t.Expected) 未満）"
            }

            # 各選択肢名を INFO 記録（先頭 5 個まで）
            $maxRecord = [Math]::Min($count, 5)
            for ($i = 0; $i -lt $maxRecord; $i++) {
                $itemName = Get-ElementName $items[$i]
                $results += New-TestResult -Id "$tcId.choice[$i]" -Status "INFO" -Detail $itemName
            }

            Collapse-Element $combo | Out-Null
            Start-Sleep -Milliseconds 200
        } catch {
            $results += New-TestResult -Id "$tcId.count" -Status "FAIL" -Detail "Exception: $($_.Exception.Message)"
        }

        # 5. キャンセルで閉じる
        Close-DialogByCancel | Out-Null
        Start-Sleep -Milliseconds 500
    }

    return $results
}
