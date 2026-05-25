# TC-apply-fsutil: fsutil 系の適用テスト
# T-FSU-2 K1 NTFS 8.3 / T-FSU-3 K2 NTFS LastAccess
#
# ⚠️ VM 専用テストケース（管理者権限必須・fsutil 設定変更）
# 戻し処理はあるが念のため VM 推奨

function Invoke-ApplyFsutil {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()

    # ========== T-FSU-2 K1 NTFS 8.3（disable8dot3） ==========
    $tcId = "apply_fsutil.T-FSU-2.K1_disable8dot3"
    try {
        $beforeOutput = (& fsutil behavior query disable8dot3 2>&1) | Out-String
        $beforeMatch = [regex]::Match($beforeOutput, "=\s*(\d+)")
        $beforeValue = if ($beforeMatch.Success) { $beforeMatch.Groups[1].Value } else { "?" }

        # disable8dot3 を 1 に設定
        & fsutil behavior set disable8dot3 1 2>&1 | Out-Null
        $afterOutput = (& fsutil behavior query disable8dot3 2>&1) | Out-String
        $applyOk = ($afterOutput -match "=\s*1")

        # 戻す
        if ($beforeMatch.Success) {
            & fsutil behavior set disable8dot3 $beforeValue 2>&1 | Out-Null
        }
        $restoredOutput = (& fsutil behavior query disable8dot3 2>&1) | Out-String
        $restoreOk = ($restoredOutput.Trim() -eq $beforeOutput.Trim())

        $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Before=$beforeValue → After: $($afterOutput.Trim())"
        $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored: $($restoredOutput.Trim())"
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    # ========== T-FSU-3 K2 NTFS LastAccess（disablelastaccess） ==========
    $tcId = "apply_fsutil.T-FSU-3.K2_disablelastaccess"
    try {
        $beforeOutput = (& fsutil behavior query disablelastaccess 2>&1) | Out-String
        $beforeMatch = [regex]::Match($beforeOutput, "=\s*(\d+)")
        $beforeValue = if ($beforeMatch.Success) { $beforeMatch.Groups[1].Value } else { "?" }

        # disablelastaccess を 1 に設定
        & fsutil behavior set disablelastaccess 1 2>&1 | Out-Null
        $afterOutput = (& fsutil behavior query disablelastaccess 2>&1) | Out-String
        $applyOk = ($afterOutput -match "=\s*1")

        # 戻す
        if ($beforeMatch.Success) {
            & fsutil behavior set disablelastaccess $beforeValue 2>&1 | Out-Null
        }
        $restoredOutput = (& fsutil behavior query disablelastaccess 2>&1) | Out-String
        $restoreOk = ($restoredOutput.Trim() -eq $beforeOutput.Trim())

        $results += New-TestResult -Id "$tcId.apply" -Status $(if ($applyOk) { "OK" } else { "FAIL" }) -Detail "Before=$beforeValue → After: $($afterOutput.Trim())"
        $results += New-TestResult -Id "$tcId.restore" -Status $(if ($restoreOk) { "OK" } else { "FAIL" }) -Detail "Restored: $($restoredOutput.Trim())"
    } catch {
        $results += New-TestResult -Id "$tcId.exception" -Status "FAIL" -Detail $_.Exception.Message
    }

    return $results
}
