# TC-apply-v2: V2 系適用テスト（HKLM ＋ netsh）
# T-V2-APPLY-PR1 Cortana / PR2 ActivityHistory / PR3 SmartScreen / N1 TCP Auto-Tuning
#
# ⚠️ VM 専用テストケース（HKLM 書込＋netsh 設定変更・管理者権限必須）
# ホスト実行は絶対 NG。VM チェックポイント環境で実行すること。

function Invoke-ApplyV2 {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()

    # ========== T-V2-APPLY-PR1 Cortana 無効化（HKLM） ==========
    $tcId = "apply_v2.T-V2-APPLY-PR1.cortana"
    try {
        $path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search"
        $name = "AllowCortana"
        $before = Read-RegValue -Path $path -Name $name
        Write-RegValue -Path $path -Name $name -Value 0 -Type DWord
        $after = Read-RegValue -Path $path -Name $name
        $applyOk = ($after.Value -eq 0)
        Restore-RegValue -Path $path -Name $name -Before $before -Type DWord
        $restored = Read-RegValue -Path $path -Name $name
        $restoreOk = (($before.Exists -eq $restored.Exists) -and ($before.Value -eq $restored.Value))

        $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Before=$(ConvertTo-RegDisplay $before) → After=$(ConvertTo-RegDisplay $after)"
        $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored=$(ConvertTo-RegDisplay $restored)"
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    # ========== T-V2-APPLY-PR2 Activity History 無効化（HKLM） ==========
    $tcId = "apply_v2.T-V2-APPLY-PR2.activity_history"
    try {
        $path = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\System"
        $name = "EnableActivityFeed"
        $before = Read-RegValue -Path $path -Name $name
        Write-RegValue -Path $path -Name $name -Value 0 -Type DWord
        $after = Read-RegValue -Path $path -Name $name
        $applyOk = ($after.Value -eq 0)
        Restore-RegValue -Path $path -Name $name -Before $before -Type DWord
        $restored = Read-RegValue -Path $path -Name $name
        $restoreOk = (($before.Exists -eq $restored.Exists) -and ($before.Value -eq $restored.Value))

        $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Before=$(ConvertTo-RegDisplay $before) → After=$(ConvertTo-RegDisplay $after)"
        $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored=$(ConvertTo-RegDisplay $restored)"
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    # ========== T-V2-APPLY-PR3 SmartScreen 動作モード（HKLM） ==========
    $tcId = "apply_v2.T-V2-APPLY-PR3.smartscreen"
    try {
        $path = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer"
        $name = "SmartScreenEnabled"
        $before = Read-RegValue -Path $path -Name $name
        Write-RegValue -Path $path -Name $name -Value "Off" -Type String
        $after = Read-RegValue -Path $path -Name $name
        $applyOk = ($after.Value -eq "Off")
        Restore-RegValue -Path $path -Name $name -Before $before -Type String
        $restored = Read-RegValue -Path $path -Name $name
        $restoreOk = (($before.Exists -eq $restored.Exists) -and ($before.Value -eq $restored.Value))

        $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Before=$(ConvertTo-RegDisplay $before) → After=$(ConvertTo-RegDisplay $after)"
        $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored=$(ConvertTo-RegDisplay $restored)"
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    # ========== T-V2-APPLY-N1 TCP Auto-Tuning（netsh） ==========
    # 修正済み：英/日両対応の Parse-AutoTuningFromNetsh ロジックを uia_common.ps1 の Get-AutoTuningLevel に集約
    $tcId = "apply_v2.T-V2-APPLY-N1.tcp_autotuning"
    try {
        $beforeValue = Get-AutoTuningLevel
        if (-not $beforeValue) { $beforeValue = "normal" }

        & netsh interface tcp set global autotuninglevel=restricted 2>&1 | Out-Null
        $afterValue = Get-AutoTuningLevel
        if (-not $afterValue) { $afterValue = "?" }
        $applyOk = ($afterValue -eq "restricted")

        & netsh interface tcp set global autotuninglevel=$beforeValue 2>&1 | Out-Null
        $restoredValue = Get-AutoTuningLevel
        if (-not $restoredValue) { $restoredValue = "?" }
        $restoreOk = ($restoredValue -eq $beforeValue)

        $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Before=$beforeValue → After=$afterValue（期待 restricted）"
        $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored=$restoredValue（期待 $beforeValue）"
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    return $results
}
