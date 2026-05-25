# UI Automation 動作確認スクリプト（VM 内で UIA が利用可能か検証）
# Phase B-1（2026-05-20）: UI Automation 対応計画書 第1版
#
# 使い方:
#   ホスト側で実行 → PowerShell Direct で VM 内に送信
#   .\0_check_uia_available.ps1
#
# 期待:
#   - UIAutomationClient/UIAutomationTypes アセンブリのロード成功
#   - WinKnobs ウィンドウの検出成功
#   - 主要 AutomationId 要素（SearchBox, CategoriesTree, ItemsGrid, MenuApplyDark）の検出成功

$ErrorActionPreference = "Continue"
$logPath = "C:\AI\Claudo\アプリ\WinKnobs\docs\uia_check_result.log"
$credPath = "C:\AI\Claudo\アプリ\WinKnobs\scripts\vm_cred.xml"
$vmName = "WinKnobs-Test"

"==== UI Automation 動作確認 ====" | Out-File -Encoding utf8 $logPath
("Timestamp: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss")) | Out-File -Encoding utf8 -Append $logPath

try {
    $cred = Import-Clixml $credPath

    $script = {
        $report = New-Object System.Collections.ArrayList

        # 1. UIA アセンブリロード
        try {
            Add-Type -AssemblyName UIAutomationClient
            Add-Type -AssemblyName UIAutomationTypes
            $report.Add("[OK] UIAutomationClient/UIAutomationTypes ロード成功") | Out-Null
        } catch {
            $report.Add("[FAIL] UIA アセンブリロード失敗: " + $_.Exception.Message) | Out-Null
            return $report
        }

        # 2. WinKnobs ウィンドウ検出
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, "WinKnobs"
        )
        $win = $null
        $start = Get-Date
        while (((Get-Date) - $start).TotalSeconds -lt 10) {
            $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
            if ($win) { break }
            Start-Sleep -Milliseconds 500
        }
        if (-not $win) {
            $report.Add("[FAIL] WinKnobs ウィンドウ未検出（先に起動してください）") | Out-Null
            return $report
        }
        $report.Add("[OK] WinKnobs ウィンドウ検出") | Out-Null

        # 3. 主要 AutomationId 要素の検出
        $targets = @(
            "SearchBox",
            "CategoriesTree",
            "ItemsGrid",
            "MenuApplyDark",
            "MenuApplyLight",
            "MenuApplySystem",
            "MenuTools",
            "StatusMessageText"
        )
        foreach ($id in $targets) {
            $c = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id
            )
            $e = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
            if ($e) {
                $report.Add("[OK] " + $id + " 検出") | Out-Null
            } else {
                $report.Add("[FAIL] " + $id + " 未検出") | Out-Null
            }
        }

        return $report
    }

    $report = Invoke-Command -VMName $vmName -Credential $cred -ScriptBlock $script -ErrorAction Stop

    "" | Out-File -Encoding utf8 -Append $logPath
    "===== Result =====" | Out-File -Encoding utf8 -Append $logPath
    foreach ($line in $report) {
        $line | Out-File -Encoding utf8 -Append $logPath
    }

    $failCount = ($report | Where-Object { $_ -like "[FAIL]*" }).Count
    if ($failCount -eq 0) {
        "" | Out-File -Encoding utf8 -Append $logPath
        "ALL PASS: UI Automation は VM 内で正常動作します" | Out-File -Encoding utf8 -Append $logPath
        Write-Host "ALL PASS"
        exit 0
    } else {
        "" | Out-File -Encoding utf8 -Append $logPath
        ("FAIL: " + $failCount + " 件") | Out-File -Encoding utf8 -Append $logPath
        Write-Host ("FAIL: " + $failCount + " 件")
        exit 1
    }
} catch {
    ("FATAL: " + $_.Exception.Message) | Out-File -Encoding utf8 -Append $logPath
    Write-Host ("FATAL: " + $_.Exception.Message)
    exit 1
}
