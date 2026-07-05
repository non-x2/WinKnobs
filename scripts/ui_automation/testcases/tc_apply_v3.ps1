# TC-apply-v3: v0.4.3〜v0.4.6 ネットワーク細分化＋デスクトップUI 適用テスト
# T-V3-APPLY-1 HideRecommendedSection（HKCU Policies）
# T-V3-APPLY-2 TCPNoDelay（HKLM・物理アダプタ1台目）
# T-V3-APPLY-3 DODownloadMode（HKLM Policies）
# T-V3-APPLY-4 InterruptModeration（PowerShell Set-NetAdapterAdvancedProperty・物理アダプタ1台目）
#
# ⚠️ VM 専用テストケース（HKLM/HKCU 書込＋NIC アダプタプロパティ変更・管理者権限必須）
# ホスト実行は絶対 NG。VM チェックポイント環境で実行すること。
# tc_apply_v2.ps1 と同型：apply→検証→restore→検証のラウンドトリップを1項目ずつ確認。

function Invoke-ApplyV3 {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()

    # ========== T-V3-APPLY-1 スタートメニュー「おすすめ」非表示（HideRecommendedSection・HKCU Policies） ==========
    $tcId = "apply_v3.T-V3-APPLY-1.hideRecommendedSection"
    try {
        $path = "HKCU:\Software\Policies\Microsoft\Windows\Explorer"
        $name = "HideRecommendedSection"
        $before = Read-RegValue -Path $path -Name $name
        Write-RegValue -Path $path -Name $name -Value 1 -Type DWord
        $after = Read-RegValue -Path $path -Name $name
        $applyOk = ($after.Value -eq 1)
        Restore-RegValue -Path $path -Name $name -Before $before -Type DWord
        $restored = Read-RegValue -Path $path -Name $name
        $restoreOk = (($before.Exists -eq $restored.Exists) -and ($before.Value -eq $restored.Value))

        $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Before=$(ConvertTo-RegDisplay $before) → After=$(ConvertTo-RegDisplay $after)"
        $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored=$(ConvertTo-RegDisplay $restored)"
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    # ========== T-V3-APPLY-2 TCP Nagle無効化（TCPNoDelay・物理アダプタ1台目のインターフェースGUID） ==========
    $tcId = "apply_v3.T-V3-APPLY-2.tcpNoDelay"
    try {
        $adapter = Get-NetAdapter -Physical | Select-Object -First 1
        if (-not $adapter) {
            $results += New-TestResult -Id "$tcId.skip" -Status "SKIP" -Detail "物理アダプタが見つかりません"
        } else {
            $path = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$($adapter.InterfaceGuid)"
            $name = "TCPNoDelay"
            $before = Read-RegValue -Path $path -Name $name
            Write-RegValue -Path $path -Name $name -Value 1 -Type DWord
            $after = Read-RegValue -Path $path -Name $name
            $applyOk = ($after.Value -eq 1)
            Restore-RegValue -Path $path -Name $name -Before $before -Type DWord
            $restored = Read-RegValue -Path $path -Name $name
            $restoreOk = (($before.Exists -eq $restored.Exists) -and ($before.Value -eq $restored.Value))

            $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Adapter=$($adapter.Name) Before=$(ConvertTo-RegDisplay $before) → After=$(ConvertTo-RegDisplay $after)"
            $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored=$(ConvertTo-RegDisplay $restored)"
        }
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    # ========== T-V3-APPLY-3 配信の最適化 P2P無効化（DODownloadMode・HKLM Policies） ==========
    $tcId = "apply_v3.T-V3-APPLY-3.doDownloadMode"
    try {
        $path = "HKLM:\Software\Policies\Microsoft\Windows\DeliveryOptimization"
        $name = "DODownloadMode"
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

    # ========== T-V3-APPLY-4 割り込み加減無効化（InterruptModeration・物理アダプタ1台目・NetAdapterAdvancedProperty経由） ==========
    $tcId = "apply_v3.T-V3-APPLY-4.interruptModeration"
    try {
        $adapter = Get-NetAdapter -Physical | Select-Object -First 1
        $prop = if ($adapter) { Get-NetAdapterAdvancedProperty -Name $adapter.Name -RegistryKeyword "*InterruptModeration" -ErrorAction SilentlyContinue } else { $null }
        if (-not $adapter -or -not $prop) {
            $results += New-TestResult -Id "$tcId.skip" -Status "SKIP" -Detail "対象アダプタ、または *InterruptModeration 非対応（本 NIC ドライバでは無い場合あり）"
        } else {
            $beforeValue = [string]@($prop.RegistryValue)[0]
            Set-NetAdapterAdvancedProperty -Name $adapter.Name -RegistryKeyword "*InterruptModeration" -RegistryValue "0" -NoRestart -ErrorAction Stop
            $afterProp = Get-NetAdapterAdvancedProperty -Name $adapter.Name -RegistryKeyword "*InterruptModeration" -ErrorAction SilentlyContinue
            $afterValue = [string]@($afterProp.RegistryValue)[0]
            $applyOk = ($afterValue -eq "0")

            Set-NetAdapterAdvancedProperty -Name $adapter.Name -RegistryKeyword "*InterruptModeration" -RegistryValue $beforeValue -NoRestart -ErrorAction Stop
            $restoredProp = Get-NetAdapterAdvancedProperty -Name $adapter.Name -RegistryKeyword "*InterruptModeration" -ErrorAction SilentlyContinue
            $restoredValue = [string]@($restoredProp.RegistryValue)[0]
            $restoreOk = ($restoredValue -eq $beforeValue)

            $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Adapter=$($adapter.Name) Before=$beforeValue → After=$afterValue（期待 0）"
            $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored=$restoredValue（期待 $beforeValue）"
        }
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    return $results
}
