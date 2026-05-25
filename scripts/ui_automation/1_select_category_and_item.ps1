# カテゴリ選択 → 項目選択 → ダイアログ起動の自動化
# Phase B-1（2026-05-20）: UI Automation 対応計画書 第1版
#
# 使い方:
#   .\1_select_category_and_item.ps1
#   引数で対象を変えたい場合は $CategoryType / $ItemId を編集
#
# 動作:
#   1. WinKnobs ウィンドウ取得
#   2. Category_Privacy（プライバシー）TreeViewItem を選択
#   3. Item_privacy_cortana DataGridRow を選択
#   4. Enter キー相当でダイアログを開く（OpenSelectedItemCommand）
#   5. SettingDialog の Title プロパティを取得して確認

$ErrorActionPreference = "Continue"
$logPath = "C:\AI\Claudo\アプリ\WinKnobs\docs\uia_select_result.log"
$credPath = "C:\AI\Claudo\アプリ\WinKnobs\scripts\vm_cred.xml"
$vmName = "WinKnobs-Test"

# 検証対象（編集可）
# ItemId は SettingItem.Id の値そのまま（ドット区切り）
# 例: privacy.cortanaDisabled / kernel.systemResponsiveness / shell.iconCacheClear
$CategoryType = "Privacy"
$ItemId = "privacy.cortanaDisabled"

"==== カテゴリ・項目選択テスト ====" | Out-File -Encoding utf8 $logPath
("Target: Category_" + $CategoryType + " / Item_" + $ItemId) | Out-File -Encoding utf8 -Append $logPath
("Timestamp: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss")) | Out-File -Encoding utf8 -Append $logPath

try {
    $cred = Import-Clixml $credPath

    $script = {
        param($CategoryType, $ItemId)

        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes

        $log = New-Object System.Collections.ArrayList

        function Find-ById {
            param($Root, $Id, $Timeout = 5)
            $c = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id
            )
            $start = Get-Date
            while (((Get-Date) - $start).TotalSeconds -lt $Timeout) {
                $e = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
                if ($e) { return $e }
                Start-Sleep -Milliseconds 200
            }
            return $null
        }

        function Find-WindowByName {
            param($Name, $Timeout = 10)
            $root = [System.Windows.Automation.AutomationElement]::RootElement
            $c = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, $Name
            )
            $start = Get-Date
            while (((Get-Date) - $start).TotalSeconds -lt $Timeout) {
                $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $c)
                if ($w) { return $w }
                Start-Sleep -Milliseconds 500
            }
            return $null
        }

        # 1. WinKnobs ウィンドウ取得
        $win = Find-WindowByName "WinKnobs" 15
        if (-not $win) {
            $log.Add("[FAIL] WinKnobs ウィンドウ未検出") | Out-Null
            return $log
        }
        $log.Add("[OK] WinKnobs ウィンドウ検出") | Out-Null

        # 2. カテゴリ選択
        $categoryId = "Category_" + $CategoryType
        $cat = Find-ById $win $categoryId 5
        if (-not $cat) {
            $log.Add("[FAIL] " + $categoryId + " 未検出") | Out-Null
            return $log
        }
        $log.Add("[OK] " + $categoryId + " 検出") | Out-Null

        try {
            $selPattern = $cat.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern
            )
            $selPattern.Select()
            Start-Sleep -Milliseconds 500
            $log.Add("[OK] " + $categoryId + " 選択") | Out-Null
        } catch {
            $log.Add("[FAIL] カテゴリ選択失敗: " + $_.Exception.Message) | Out-Null
            return $log
        }

        # 3. 項目選択
        $itemId = "Item_" + $ItemId
        $item = Find-ById $win $itemId 5
        if (-not $item) {
            $log.Add("[FAIL] " + $itemId + " 未検出（カテゴリ切替後 FilteredItems に出ない可能性）") | Out-Null
            return $log
        }
        $log.Add("[OK] " + $itemId + " 検出") | Out-Null

        try {
            $selPattern2 = $item.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern
            )
            $selPattern2.Select()
            Start-Sleep -Milliseconds 300
            $log.Add("[OK] " + $itemId + " 選択") | Out-Null
        } catch {
            $log.Add("[FAIL] 項目選択失敗: " + $_.Exception.Message) | Out-Null
            return $log
        }

        # 4. メニュー操作で SettingDialog を開く
        #    WPF の仕様：MenuItem 配下は親メニューが展開されないと UIA ツリーに出ない
        #    → 親 MenuFile を ExpandCollapsePattern で展開してから MenuOpenItem を取得
        $menuFile = Find-ById $win "MenuFile" 3
        if (-not $menuFile) {
            $log.Add("[FAIL] MenuFile 未検出") | Out-Null
            return $log
        }
        try {
            $expandPattern = $menuFile.GetCurrentPattern(
                [System.Windows.Automation.ExpandCollapsePattern]::Pattern
            )
            $expandPattern.Expand()
            Start-Sleep -Milliseconds 500
            $log.Add("[OK] MenuFile 展開") | Out-Null
        } catch {
            $log.Add("[FAIL] MenuFile 展開失敗: " + $_.Exception.Message) | Out-Null
            return $log
        }

        $menuOpen = Find-ById $win "MenuOpenItem" 3
        if (-not $menuOpen) {
            $log.Add("[FAIL] MenuOpenItem 未検出（展開後）") | Out-Null
            return $log
        }
        try {
            $invokePattern = $menuOpen.GetCurrentPattern(
                [System.Windows.Automation.InvokePattern]::Pattern
            )
            $invokePattern.Invoke()
            Start-Sleep -Seconds 1
            $log.Add("[OK] MenuOpenItem Invoke 成功") | Out-Null
        } catch {
            $log.Add("[FAIL] MenuOpenItem Invoke 失敗: " + $_.Exception.Message) | Out-Null
            return $log
        }

        # 5. SettingDialog が開いたか確認（OkButton の存在で判定）
        $dialogRoot = [System.Windows.Automation.AutomationElement]::RootElement
        $okBtn = Find-ById $dialogRoot "OkButton" 5
        if ($okBtn) {
            $log.Add("[OK] SettingDialog 開いた（OkButton 検出）") | Out-Null
            # ApplyButton / RestoreButton も検出
            $apply = Find-ById $dialogRoot "ApplyButton" 2
            $restore = Find-ById $dialogRoot "RestoreButton" 2
            if ($apply) { $log.Add("[OK] ApplyButton 検出") | Out-Null } else { $log.Add("[FAIL] ApplyButton 未検出") | Out-Null }
            if ($restore) { $log.Add("[OK] RestoreButton 検出") | Out-Null } else { $log.Add("[FAIL] RestoreButton 未検出") | Out-Null }

            # キャンセルで閉じる
            $cancel = Find-ById $dialogRoot "CancelButton" 2
            if ($cancel) {
                try {
                    $cancelInvoke = $cancel.GetCurrentPattern(
                        [System.Windows.Automation.InvokePattern]::Pattern
                    )
                    $cancelInvoke.Invoke()
                    Start-Sleep -Milliseconds 500
                    $log.Add("[OK] CancelButton で閉じた") | Out-Null
                } catch {
                    $log.Add("[FAIL] CancelButton Invoke 失敗") | Out-Null
                }
            }
        } else {
            $log.Add("[FAIL] SettingDialog が開かない（OkButton 未検出）") | Out-Null
        }

        return $log
    }

    $report = Invoke-Command -VMName $vmName -Credential $cred -ScriptBlock $script -ArgumentList $CategoryType, $ItemId -ErrorAction Stop

    "" | Out-File -Encoding utf8 -Append $logPath
    "===== Result =====" | Out-File -Encoding utf8 -Append $logPath
    foreach ($line in $report) {
        $line | Out-File -Encoding utf8 -Append $logPath
    }

    $failCount = ($report | Where-Object { $_ -like "[FAIL]*" }).Count
    if ($failCount -eq 0) {
        "ALL PASS" | Out-File -Encoding utf8 -Append $logPath
        Write-Host "ALL PASS"
        exit 0
    } else {
        ("FAIL: " + $failCount + " 件") | Out-File -Encoding utf8 -Append $logPath
        Write-Host ("FAIL: " + $failCount + " 件")
        exit 1
    }
} catch {
    ("FATAL: " + $_.Exception.Message) | Out-File -Encoding utf8 -Append $logPath
    Write-Host ("FATAL: " + $_.Exception.Message)
    exit 1
}
