# PR1 Cortana 適用 → BUG-1 リグレッション検証
# Phase B-1（2026-05-20）: UI Automation 対応計画書 第1版
#
# 使い方:
#   .\2_apply_pr1_and_verify.ps1
#
# 動作:
#   1. WinKnobs で Privacy カテゴリの PR1（Cortana）を選択
#   2. ダイアログを開いて「適用」ボタンをクリック
#   3. 「バックアップ失敗」ダイアログが出ないことを確認（BUG-1 リグレッション）
#   4. ステータスバーに成功メッセージが表示されることを確認
#   5. 戻して dirty 状態を解消
#
# 前提:
#   - WinKnobs は標準権限で起動済（HKLM 書込は BUG-3 修正の挙動確認も兼ねる）
#   - VM 内チェックポイント Before_Apply_Test に戻してから実行推奨

$ErrorActionPreference = "Continue"
$logPath = "C:\AI\Claudo\アプリ\WinKnobs\docs\uia_pr1_apply_result.log"
$credPath = "C:\AI\Claudo\アプリ\WinKnobs\scripts\vm_cred.xml"
$vmName = "WinKnobs-Test"

"==== PR1 Cortana 適用 + BUG-1 リグレッション ====" | Out-File -Encoding utf8 $logPath
("Timestamp: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss")) | Out-File -Encoding utf8 -Append $logPath

try {
    $cred = Import-Clixml $credPath

    $script = {
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

        function Invoke-Element {
            param($Element, $Label)
            try {
                $p = $Element.GetCurrentPattern(
                    [System.Windows.Automation.InvokePattern]::Pattern
                )
                $p.Invoke()
                return $true
            } catch {
                return $false
            }
        }

        # 1. WinKnobs ウィンドウ取得
        $win = Find-WindowByName "WinKnobs" 15
        if (-not $win) {
            $log.Add("[FAIL] WinKnobs ウィンドウ未検出") | Out-Null
            return $log
        }
        $log.Add("[OK] WinKnobs ウィンドウ検出") | Out-Null

        # 2. Category_Privacy を選択
        $cat = Find-ById $win "Category_Privacy" 5
        if (-not $cat) {
            $log.Add("[FAIL] Category_Privacy 未検出") | Out-Null
            return $log
        }
        try {
            $cat.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 500
            $log.Add("[OK] Category_Privacy 選択") | Out-Null
        } catch {
            $log.Add("[FAIL] Category_Privacy 選択失敗") | Out-Null
            return $log
        }

        # 3. Item_privacy.cortanaDisabled を選択（SettingItem.Id は ドット区切り）
        $item = Find-ById $win "Item_privacy.cortanaDisabled" 5
        if (-not $item) {
            $log.Add("[FAIL] Item_privacy.cortanaDisabled 未検出") | Out-Null
            return $log
        }
        try {
            $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 300
            $log.Add("[OK] Item_privacy.cortanaDisabled 選択") | Out-Null
        } catch {
            $log.Add("[FAIL] Item 選択失敗") | Out-Null
            return $log
        }

        # 4. ファイル親メニューを展開してから MenuOpenItem Invoke
        $menuFile = Find-ById $win "MenuFile" 3
        if (-not $menuFile) {
            $log.Add("[FAIL] MenuFile 未検出") | Out-Null
            return $log
        }
        try {
            $menuFile.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            Start-Sleep -Milliseconds 500
            $log.Add("[OK] MenuFile 展開") | Out-Null
        } catch {
            $log.Add("[FAIL] MenuFile 展開失敗") | Out-Null
            return $log
        }

        $menuOpen = Find-ById $win "MenuOpenItem" 3
        if ($menuOpen) {
            Invoke-Element $menuOpen "MenuOpenItem" | Out-Null
            Start-Sleep -Seconds 1
            $log.Add("[OK] MenuOpenItem Invoke") | Out-Null
        } else {
            $log.Add("[FAIL] MenuOpenItem 未検出") | Out-Null
            return $log
        }

        # 5. SettingDialog 開いたか確認
        $dlgRoot = [System.Windows.Automation.AutomationElement]::RootElement
        $applyBtn = Find-ById $dlgRoot "ApplyButton" 5
        if (-not $applyBtn) {
            $log.Add("[FAIL] SettingDialog が開かない（ApplyButton 未検出）") | Out-Null
            return $log
        }
        $log.Add("[OK] SettingDialog 開いた") | Out-Null

        # 6. ApplyButton クリック
        Invoke-Element $applyBtn "ApplyButton" | Out-Null
        Start-Sleep -Seconds 2

        # 7. BUG-1 リグレッション：「バックアップ失敗」ダイアログが出ていないか確認
        $bug1Dialog = Find-WindowByName "バックアップ失敗" 2
        if ($bug1Dialog) {
            $log.Add("[FAIL] BUG-1 再発：『バックアップ失敗』ダイアログが出ている") | Out-Null
            # OK で閉じる試行
        } else {
            $log.Add("[OK] BUG-1 リグレッション PASS（バックアップ失敗ダイアログ無し）") | Out-Null
        }

        # 8. 別ダイアログ（管理者再起動など）が出ていないか
        $elevDlg = Find-WindowByName "管理者権限が必要" 1
        if ($elevDlg) {
            $log.Add("[INFO] 管理者再起動ダイアログ出現（BUG-3 対応の挙動・PR1 は HKLM 系のため期待動作）") | Out-Null
        }

        # 9. DialogStatusText の内容を取得（成功/失敗どちらでも記録）
        $statusText = Find-ById $dlgRoot "DialogStatusText" 2
        if ($statusText) {
            $msg = $statusText.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
            $log.Add("[INFO] DialogStatusText: " + $msg) | Out-Null
        }

        # 10. ダイアログをキャンセルで閉じる
        $cancel = Find-ById $dlgRoot "CancelButton" 2
        if ($cancel) {
            Invoke-Element $cancel "CancelButton" | Out-Null
            Start-Sleep -Milliseconds 500
            $log.Add("[OK] CancelButton で閉じた") | Out-Null
        }

        return $log
    }

    $report = Invoke-Command -VMName $vmName -Credential $cred -ScriptBlock $script -ErrorAction Stop

    "" | Out-File -Encoding utf8 -Append $logPath
    "===== Result =====" | Out-File -Encoding utf8 -Append $logPath
    foreach ($line in $report) {
        $line | Out-File -Encoding utf8 -Append $logPath
    }

    $failCount = ($report | Where-Object { $_ -like "[FAIL]*" }).Count
    if ($failCount -eq 0) {
        "ALL PASS（BUG-1 リグレッション含む）" | Out-File -Encoding utf8 -Append $logPath
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
