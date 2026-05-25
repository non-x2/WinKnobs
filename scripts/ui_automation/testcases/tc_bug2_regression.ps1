# TC-bug2-regression: BUG-2 リグレッション
# K1（NTFS 8.3 名）が 4 択（0/1/2/3）に拡張されていることを確認
# （v0.1.3-dev で BuildNtfs8dot3Item を 2 択 → 4 択 IntBackup に修正）

function Invoke-Bug2Regression {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()
    $tcId = "bug2.k1_4choices"

    # 1. K1（system.ntfs8dot3）を開く
    $sel = Select-CategoryAndItem -Win $Win -CategoryType "System" -ItemId "system.ntfs8dot3"
    if (-not $sel.Success) {
        $results += New-TestResult -Id "$tcId.select" -Status "FAIL" -Detail "$($sel.Step) / $($sel.Detail)"
        return $results
    }

    $open = Open-DialogViaMenu -Win $Win
    if (-not $open.Success) {
        $results += New-TestResult -Id "$tcId.open" -Status "FAIL" -Detail $open.Detail
        return $results
    }
    $results += New-TestResult -Id "$tcId.open" -Status "OK" -Detail "K1 dialog opened"

    # 2. EditableCommandCombo の選択肢を取得（Command 型なので EditableCommandCombo が見える）
    $combo = Find-ById $open.DialogWindow "EditableCommandCombo" 3
    if (-not $combo) {
        $results += New-TestResult -Id "$tcId.find_combo" -Status "FAIL" -Detail "EditableCommandCombo not found（K1 は Command 型のはず）"
        Close-DialogByCancel | Out-Null
        return $results
    }

    # 3. ComboBox の子要素（ListItem）を全取得して数を数える
    try {
        # Expand してから子要素取得（WPF ComboBox 仕様）
        Expand-Element $combo | Out-Null
        Start-Sleep -Milliseconds 500

        $listItemCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem
        )
        $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listItemCond)
        $count = $items.Count

        if ($count -ge 4) {
            $results += New-TestResult -Id "$tcId.choice_count" -Status "OK" -Detail "$count 択（BUG-2 fixed・4 択以上）"
        } else {
            $results += New-TestResult -Id "$tcId.choice_count" -Status "FAIL" -Detail "$count 択しか無い（BUG-2 再発・4 択期待）"
        }

        # 4. 各選択肢の名前を INFO 記録
        for ($i = 0; $i -lt [Math]::Min($count, 6); $i++) {
            $itemName = Get-ElementName $items[$i]
            $results += New-TestResult -Id "$tcId.choice[$i]" -Status "INFO" -Detail $itemName
        }

        Collapse-Element $combo | Out-Null
    } catch {
        $results += New-TestResult -Id "$tcId.choice_count" -Status "FAIL" -Detail "Exception: $($_.Exception.Message)"
    }

    # 5. 閉じる
    Close-DialogByCancel | Out-Null

    return $results
}
