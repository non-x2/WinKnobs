# TC-destructive-actions: 実行系項目（破壊的）のダイアログ動作確認
# T-V2-S1 アイコンキャッシュクリア / T-V2-S2 BagMRU クリア
#
# 安全設計：適用は押さない（読込＋RestoreButton 無効化確認＋Cancel 閉じのみ）
# 副作用なし
# 実行系項目の特徴：Backup=null → CanRestore=false → RestoreButton.IsEnabled=False

function Invoke-DestructiveActions {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()

    # 冒頭で検索クリア
    Clear-SearchBox -Win $Win | Out-Null
    Start-Sleep -Milliseconds 300

    $targets = @(
        @{ Category="Shell"; ItemId="shell.iconCacheClear"; Label="S1 アイコンキャッシュクリア" }
        @{ Category="Shell"; ItemId="shell.bagMRUClear";    Label="S2 BagMRU クリア" }
    )

    foreach ($t in $targets) {
        $tcId = "destructive_actions." + $t.ItemId

        # 1. カテゴリ＋項目選択
        $sel = Select-CategoryAndItem -Win $Win -CategoryType $t.Category -ItemId $t.ItemId
        if (-not $sel.Success) {
            $results += New-TestResult -Id "$tcId.select" -Status "FAIL" -Detail "$($sel.Step) / $($sel.Detail)"
            continue
        }

        # 2. ダイアログ起動
        $open = Open-DialogViaMenu -Win $Win
        if (-not $open.Success) {
            $results += New-TestResult -Id "$tcId.open" -Status "FAIL" -Detail $open.Detail
            continue
        }
        $results += New-TestResult -Id "$tcId.open" -Status "OK" -Detail $t.Label

        # 3. ApplyButton 存在確認（実行系も Apply はある）
        $applyBtn = Find-ById $open.DialogWindow "ApplyButton" 3
        if ($applyBtn) {
            $applyEnabled = $applyBtn.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsEnabledProperty)
            $results += New-TestResult -Id "$tcId.apply_button" -Status "OK" -Detail "ApplyButton Enabled=$applyEnabled"
        } else {
            $results += New-TestResult -Id "$tcId.apply_button" -Status "FAIL" -Detail "ApplyButton not found"
        }

        # 4. RestoreButton 無効化確認（実行系の特徴：Backup=null → CanRestore=false）
        $restoreBtn = Find-ById $open.DialogWindow "RestoreButton" 3
        if ($restoreBtn) {
            $restoreEnabled = $restoreBtn.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::IsEnabledProperty)
            if (-not $restoreEnabled) {
                $results += New-TestResult -Id "$tcId.restore_disabled" -Status "OK" -Detail "RestoreButton 無効化 OK（実行系の期待動作）"
            } else {
                $results += New-TestResult -Id "$tcId.restore_disabled" -Status "INFO" -Detail "RestoreButton が有効（実行系設計と異なる可能性）"
            }
        } else {
            $results += New-TestResult -Id "$tcId.restore_disabled" -Status "FAIL" -Detail "RestoreButton not found"
        }

        # 5. EditableBoolCombo（実行系は Boolean 2 択："実行しない/する" 想定）
        $combo = Find-ById $open.DialogWindow "EditableBoolCombo" 3
        if ($combo) {
            try {
                Expand-Element $combo | Out-Null
                Start-Sleep -Milliseconds 500

                $listCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::ListItem
                )
                $items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listCond)
                $count = $items.Count
                if ($count -ge 2) {
                    $results += New-TestResult -Id "$tcId.choice_count" -Status "OK" -Detail "$count 択（実行系 Boolean）"
                    for ($i = 0; $i -lt [Math]::Min($count, 3); $i++) {
                        $itemName = Get-ElementName $items[$i]
                        $results += New-TestResult -Id "$tcId.choice[$i]" -Status "INFO" -Detail $itemName
                    }
                } else {
                    $results += New-TestResult -Id "$tcId.choice_count" -Status "FAIL" -Detail "$count 択しか無い"
                }
                Collapse-Element $combo | Out-Null
            } catch {
                $results += New-TestResult -Id "$tcId.choice_count" -Status "FAIL" -Detail "Exception: $($_.Exception.Message)"
            }
        } else {
            $results += New-TestResult -Id "$tcId.find_combo" -Status "INFO" -Detail "EditableBoolCombo 未検出（別の入力 UI の可能性）"
        }

        # 6. キャンセルで閉じる
        Close-DialogByCancel | Out-Null
        Start-Sleep -Milliseconds 500
    }

    return $results
}
