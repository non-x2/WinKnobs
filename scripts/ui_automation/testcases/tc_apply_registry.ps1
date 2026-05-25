# TC-apply-registry: HKCU レジストリ系の適用テスト
# T-REG-1 W1 ダークモード / T-REG-3 W6 Bing 検索 / T-REG-4 W5 クラシック右クリック
#
# ⚠️ VM 専用テストケース（HKCU 書込発生）
# ホスト実行はユーザー設定を一時変更します（戻し処理はあるが念のため VM 推奨）

function Invoke-ApplyRegistry {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()

    # ========== T-REG-1 W1 ダークモード（AppsUseLightTheme） ==========
    $tcId = "apply_registry.T-REG-1.W1_dark_apps"
    try {
        $path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
        $before = Read-RegValue -Path $path -Name "AppsUseLightTheme"
        Write-RegValue -Path $path -Name "AppsUseLightTheme" -Value 0 -Type DWord
        $after = Read-RegValue -Path $path -Name "AppsUseLightTheme"
        $applyOk = ($after.Value -eq 0)
        Restore-RegValue -Path $path -Name "AppsUseLightTheme" -Before $before -Type DWord
        $restored = Read-RegValue -Path $path -Name "AppsUseLightTheme"
        $restoreOk = (($before.Exists -eq $restored.Exists) -and ($before.Value -eq $restored.Value))

        $status = if ($applyOk -and $restoreOk) { "OK" } else { "FAIL" }
        $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Before=$(ConvertTo-RegDisplay $before) → After=$(ConvertTo-RegDisplay $after)"
        $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored=$(ConvertTo-RegDisplay $restored)"
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    # ========== T-REG-3 W6 BingSearchEnabled ==========
    $tcId = "apply_registry.T-REG-3.W6_bing_search"
    try {
        $path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Search"
        $before = Read-RegValue -Path $path -Name "BingSearchEnabled"
        Write-RegValue -Path $path -Name "BingSearchEnabled" -Value 0 -Type DWord
        $after = Read-RegValue -Path $path -Name "BingSearchEnabled"
        $applyOk = ($after.Value -eq 0)
        Restore-RegValue -Path $path -Name "BingSearchEnabled" -Before $before -Type DWord
        $restored = Read-RegValue -Path $path -Name "BingSearchEnabled"
        $restoreOk = (($before.Exists -eq $restored.Exists) -and ($before.Value -eq $restored.Value))

        $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Before=$(ConvertTo-RegDisplay $before) → After=$(ConvertTo-RegDisplay $after)"
        $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored=$(ConvertTo-RegDisplay $restored)"
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    # ========== T-REG-4 W5 クラシック右クリック（CLSID キー作成・削除） ==========
    $tcId = "apply_registry.T-REG-4.W5_classic_context"
    try {
        $clsidKey = "HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}"
        $innerKey = "$clsidKey\InprocServer32"

        $beforeExists = Test-Path $innerKey

        # 作成
        if (-not (Test-Path $clsidKey)) { New-Item -Path $clsidKey -Force | Out-Null }
        if (-not (Test-Path $innerKey)) { New-Item -Path $innerKey -Force | Out-Null }
        $afterExists = Test-Path $innerKey
        $applyOk = $afterExists

        # 戻し（before に存在しなかった場合のみ削除）
        if (-not $beforeExists -and $afterExists) {
            Remove-Item -Path $clsidKey -Recurse -Force -ErrorAction SilentlyContinue
        }
        $restoredExists = Test-Path $innerKey
        $restoreOk = ($beforeExists -eq $restoredExists)

        $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Before=$beforeExists → After=$afterExists（CLSID キー作成）"
        $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored=$restoredExists（before に戻る）"
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    return $results
}
