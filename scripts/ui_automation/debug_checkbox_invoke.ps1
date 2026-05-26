# debug_checkbox_invoke.ps1
# 2026-05-27 一時調査用：CheckBox の InvokePattern.Invoke が ToggleEnabledCommand を発火するか確認
#
# 目的：UIA テスト toggle.verify_byte FAIL の原因切り分け
#   仮説 A: InvokePattern.Invoke が失敗してフォールバック TogglePattern.Toggle が走り
#           IsChecked は反転するが ToggleEnabledCommand 発火せず → StartupApproved 書込なし
#
# 計測点：
#   1) Invoke-Element の戻り値（true/false）
#   2) InvokePattern 例外メッセージ
#   3) Toggle 前後の StatusText（ToggleEnabledCommand が動けば "✅ ... を{有効/無効}化しました" になる）
#   4) Toggle 前後の HKCU\StartupApproved\Run\WinKnobs_DebugTest（あれば書込発生）
#
# 副作用：
#   - HKCU\Run\WinKnobs_DebugTest を作成 → 終了時に削除
#   - HKCU\StartupApproved\Run\WinKnobs_DebugTest を作成する可能性 → 終了時に削除
#   - WinKnobs に F5 を送る or Startup カテゴリを選択する
#   - finally で必ず cleanup

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$ErrorActionPreference = 'Continue'

# ─── 1. レジストリ準備 ───
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$approvedPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$testName = 'WinKnobs_DebugTest'

# 既存値バックアップ
$beforeRun = Get-ItemProperty -Path $runPath -Name $testName -ErrorAction SilentlyContinue
$beforeApproved = $null
if (Test-Path $approvedPath) {
    $beforeApproved = Get-ItemProperty -Path $approvedPath -Name $testName -ErrorAction SilentlyContinue
}

Write-Output "===== Phase 1: テストエントリ作成 ====="
Set-ItemProperty -Path $runPath -Name $testName -Value 'notepad.exe' -Type String
$created = Get-ItemProperty -Path $runPath -Name $testName -ErrorAction SilentlyContinue
Write-Output ("HKCU\Run\{0} created: {1}" -f $testName, ($null -ne $created))

try {
    # ─── 2. WinKnobs ウィンドウ取得 ───
    Write-Output "`n===== Phase 2: WinKnobs ウィンドウ取得 ====="
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'WinKnobs')
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if (-not $win) {
        Write-Output 'WinKnobs window not found. Please launch WinKnobs first.'
        return
    }
    Write-Output ("WinKnobs hwnd: {0}" -f $win.Current.NativeWindowHandle)

    # ─── 3. Startup カテゴリ選択（Load トリガ） + MenuRefresh で再読込 ───
    Write-Output "`n===== Phase 3: Startup カテゴリ選択 + Refresh ====="
    $catCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'Category_Startup')
    $cat = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $catCond)
    if (-not $cat) {
        Write-Output 'Category_Startup not found'
        return
    }
    $selPattern = $cat.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $selPattern.Select()
    Start-Sleep -Seconds 1

    # MenuFile → MenuRefresh を invoke して再読込を強制
    $menuFileCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MenuFile')
    $menuFile = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $menuFileCond)
    if ($menuFile) {
        try {
            $ecp = $menuFile.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            $ecp.Expand()
            Start-Sleep -Milliseconds 700
            $refreshCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MenuRefresh')
            $menuRefresh = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $refreshCond)
            if ($menuRefresh) {
                $ip = $menuRefresh.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                $ip.Invoke()
                Write-Output 'MenuRefresh.Invoke() called'
                Start-Sleep -Seconds 2
            } else {
                Write-Output 'MenuRefresh not found'
                # フォールバック：別カテゴリ→Startup 再選択（Load 再発火）
                $catHomeCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'Category_Home')
                $catHome = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $catHomeCond)
                if ($catHome) {
                    $catHome.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                    Start-Sleep -Milliseconds 500
                }
                $selPattern.Select()
                Start-Sleep -Seconds 2
            }
            # メニュー閉じる
            try { $ecp.Collapse() } catch {}
        } catch {
            Write-Output ('Refresh failed: {0}' -f $_.Exception.Message)
        }
    }

    # ─── 4. テストエントリの行検索 ───
    Write-Output "`n===== Phase 4: WinKnobs_DebugTest 行検索 ====="
    $gridCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'StartupItemsGrid')
    $grid = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $gridCond)
    if (-not $grid) {
        Write-Output 'StartupItemsGrid not found'
        return
    }

    $row = $null
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $row) {
        $dataItemCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::DataItem)
        $nameCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $testName)
        $rows = $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $dataItemCond)
        foreach ($r in $rows) {
            $found = $r.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
            if ($found) { $row = $r; break }
        }
        if (-not $row) { Start-Sleep -Milliseconds 500 }
    }
    if (-not $row) {
        Write-Output 'WinKnobs_DebugTest row not found in grid'
        Write-Output 'Hint: F5 を試すか、カテゴリ切替→再選択を試行してください'
        return
    }
    Write-Output ("Row found: {0}" -f $testName)

    # ─── 5. CheckBox 取得 ───
    Write-Output "`n===== Phase 5: CheckBox 取得 ====="
    $cbCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'StartupEnabledCheckBox')
    $checkbox = $row.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cbCond)
    if (-not $checkbox) {
        Write-Output 'StartupEnabledCheckBox not found in row'
        return
    }
    Write-Output ("CheckBox IsEnabled: {0}" -f $checkbox.Current.IsEnabled)

    # ─── 6. Patterns 確認 ───
    Write-Output "`n===== Phase 6: 利用可能パターン確認 ====="
    $supportedPatterns = $checkbox.GetSupportedPatterns()
    foreach ($p in $supportedPatterns) {
        Write-Output ("  Pattern: {0}" -f $p.ProgrammaticName)
    }

    $tp = $checkbox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $beforeToggleState = $tp.Current.ToggleState
    Write-Output ("Before ToggleState: {0}" -f $beforeToggleState)

    # ─── 7. StatusText 取得（Before） ───
    Write-Output "`n===== Phase 7: 状態スナップショット（Before） ====="
    $statusCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'StartupStatusText')
    $statusText = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCond)
    $beforeStatus = if ($statusText) { $statusText.Current.Name } else { '(not found)' }
    Write-Output ("Before StatusText: {0}" -f $beforeStatus)

    # レジストリ状態（Before）
    $beforeReg = Get-ItemProperty -Path $approvedPath -Name $testName -ErrorAction SilentlyContinue
    if ($beforeReg) {
        $beforeBytes = $beforeReg.$testName
        $beforeHex = ($beforeBytes | ForEach-Object { '{0:X2}' -f $_ }) -join ''
        Write-Output ("Before Approved: {0} ({1} bytes)" -f $beforeHex, $beforeBytes.Length)
    } else {
        Write-Output 'Before Approved: (not present)'
    }

    # ─── 8a. InvokePattern.Invoke を試す ───
    Write-Output "`n===== Phase 8a: InvokePattern.Invoke 試行 ====="
    $invokeException = $null
    $invokeSucceeded = $false
    try {
        $ip = $checkbox.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        Write-Output ('InvokePattern obtained: {0}' -f ($null -ne $ip))
        if ($ip) {
            $ip.Invoke()
            $invokeSucceeded = $true
            Write-Output 'InvokePattern.Invoke() succeeded'
        }
    } catch {
        $invokeException = $_.Exception
        Write-Output ('InvokePattern.Invoke() FAILED: {0}' -f $_.Exception.Message)
    }

    # ─── 8b. LegacyIAccessiblePattern.DoDefaultAction() フォールバック試行 ───
    Write-Output "`n===== Phase 8b: LegacyIAccessiblePattern.DoDefaultAction() 試行 ====="
    $legacyAttempted = $false
    $legacySucceeded = $false
    if (-not $invokeSucceeded) {
        try {
            # LegacyIAccessiblePattern.Pattern GUID = {6E3C5C8B-5C8C-4D7A-92C5-AAE0E6C8C0B0}
            # PowerShell では System.Windows.Automation.LegacyIAccessiblePattern を直接利用
            $legacyPattern = $checkbox.GetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern)
            $legacyAttempted = $true
            Write-Output ('LegacyIAccessiblePattern obtained: {0}' -f ($null -ne $legacyPattern))
            Write-Output ('DefaultAction: {0}' -f $legacyPattern.Current.DefaultAction)
            $legacyPattern.DoDefaultAction()
            $legacySucceeded = $true
            Write-Output 'LegacyIAccessiblePattern.DoDefaultAction() succeeded'
        } catch {
            Write-Output ('LegacyIAccessiblePattern.DoDefaultAction() FAILED: {0}' -f $_.Exception.Message)
        }
    }
    Start-Sleep -Milliseconds 1500

    # ─── 9. After 状態スナップショット ───
    Write-Output "`n===== Phase 9: 状態スナップショット（After） ====="
    $afterStatus = if ($statusText) { $statusText.Current.Name } else { '(not found)' }
    Write-Output ("After StatusText: {0}" -f $afterStatus)

    $tp2 = $checkbox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $afterToggleState = $tp2.Current.ToggleState
    Write-Output ("After ToggleState: {0}" -f $afterToggleState)

    $afterReg = Get-ItemProperty -Path $approvedPath -Name $testName -ErrorAction SilentlyContinue
    if ($afterReg) {
        $afterBytes = $afterReg.$testName
        $afterHex = ($afterBytes | ForEach-Object { '{0:X2}' -f $_ }) -join ''
        Write-Output ("After Approved: {0} ({1} bytes)" -f $afterHex, $afterBytes.Length)
    } else {
        Write-Output 'After Approved: (not present)'
    }

    # ─── 10. 判定 ───
    Write-Output "`n===== Phase 10: 判定 ====="
    Write-Output ('InvokePattern.Invoke success     : {0}' -f $invokeSucceeded)
    Write-Output ('Legacy DoDefaultAction attempted : {0}' -f $legacyAttempted)
    Write-Output ('Legacy DoDefaultAction success   : {0}' -f $legacySucceeded)
    Write-Output ('ToggleState changed              : {0}' -f ($beforeToggleState -ne $afterToggleState))
    Write-Output ('StatusText changed               : {0}' -f ($beforeStatus -ne $afterStatus))
    Write-Output ('StartupApproved written          : {0}' -f ($null -ne $afterReg))

    if ($legacySucceeded -and $null -ne $afterReg) {
        Write-Output "`n[Conclusion] LegacyIAccessiblePattern.DoDefaultAction() で Command 発火成功！UIA テストの fix 候補"
    } elseif ($legacySucceeded -and ($null -eq $afterReg)) {
        Write-Output "`n[Conclusion] Legacy DoDefaultAction は走ったが Command は発火していない（要追加調査）"
    } elseif (-not $invokeSucceeded -and -not $legacySucceeded) {
        Write-Output "`n[Conclusion] InvokePattern も Legacy も使えない → UIA テストはマウス座標経由が必要"
    } else {
        Write-Output "`n[Conclusion] 想定外の状態"
    }

} finally {
    Write-Output "`n===== Cleanup ====="
    # WinKnobs_DebugTest を Run / StartupApproved から削除（既存値があれば復元）
    if ($null -eq $beforeRun) {
        Remove-ItemProperty -Path $runPath -Name $testName -ErrorAction SilentlyContinue
    } else {
        Set-ItemProperty -Path $runPath -Name $testName -Value $beforeRun.$testName -ErrorAction SilentlyContinue
    }
    if ($null -eq $beforeApproved) {
        if (Test-Path $approvedPath) {
            Remove-ItemProperty -Path $approvedPath -Name $testName -ErrorAction SilentlyContinue
        }
    } else {
        Set-ItemProperty -Path $approvedPath -Name $testName -Value $beforeApproved.$testName -Type Binary -ErrorAction SilentlyContinue
    }
    $finalRun = Get-ItemProperty -Path $runPath -Name $testName -ErrorAction SilentlyContinue
    $finalApproved = if (Test-Path $approvedPath) { Get-ItemProperty -Path $approvedPath -Name $testName -ErrorAction SilentlyContinue } else { $null }
    Write-Output ('Cleanup: HKCU\Run\{0} exists = {1}' -f $testName, ($null -ne $finalRun))
    Write-Output ('Cleanup: HKCU\Approved\{0} exists = {1}' -f $testName, ($null -ne $finalApproved))
}
