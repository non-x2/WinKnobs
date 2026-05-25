# TC-bug1-regression: BUG-1 リグレッション
# 「バックアップ失敗」ダイアログが PR1 適用時に出ないことを確認
# （v0.1.3-dev で BackupService にキー存在チェック追加・キー削除マーカー .reg 生成）
#
# 注意: HKLM 書込は管理者権限必須。標準権限の場合は BUG-3 対応ダイアログが出る
#       → これは正常動作（管理者再起動誘導）。BUG-1 とは別物として扱う。

function Invoke-Bug1Regression {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()
    $tcId = "bug1.cortana_apply"

    # 1. PR1 を開く
    $sel = Select-CategoryAndItem -Win $Win -CategoryType "Privacy" -ItemId "privacy.cortanaDisabled"
    if (-not $sel.Success) {
        $results += New-TestResult -Id "$tcId.select" -Status "FAIL" -Detail "$($sel.Step) / $($sel.Detail)"
        return $results
    }

    $open = Open-DialogViaMenu -Win $Win
    if (-not $open.Success) {
        $results += New-TestResult -Id "$tcId.open" -Status "FAIL" -Detail $open.Detail
        return $results
    }
    $results += New-TestResult -Id "$tcId.open" -Status "OK" -Detail "PR1 dialog opened"

    # 2. ApplyButton クリック
    $apply = Find-ById $open.DialogWindow "ApplyButton" 3
    if (-not $apply) {
        $results += New-TestResult -Id "$tcId.find_apply" -Status "FAIL" -Detail "ApplyButton not found"
        Close-DialogByCancel | Out-Null
        return $results
    }

    Invoke-Element $apply | Out-Null
    Start-Sleep -Seconds 2

    # 3. BUG-1 検出：「バックアップ失敗」ダイアログが出ていないか
    $bug1 = Test-DialogExists -Name "バックアップ失敗"
    if ($bug1) {
        $results += New-TestResult -Id "$tcId.no_backup_fail_dialog" -Status "FAIL" -Detail "BUG-1 再発：バックアップ失敗ダイアログ検出"
    } else {
        $results += New-TestResult -Id "$tcId.no_backup_fail_dialog" -Status "OK" -Detail "BUG-1 fixed (no backup fail dialog)"
    }

    # 4. 別ダイアログ（BUG-3 対応・管理者再起動誘導）の確認（INFO）
    $elev = Test-DialogExists -Name "管理者権限が必要"
    if ($elev) {
        $results += New-TestResult -Id "$tcId.elevation_dialog" -Status "INFO" -Detail "BUG-3 対応ダイアログ出現（標準権限起動時の期待動作）"
    }

    # 5. DialogStatusText の内容記録（INFO）
    $statusText = Find-ById $open.DialogWindow "DialogStatusText" 2
    if ($statusText) {
        $msg = Get-ElementName $statusText
        $results += New-TestResult -Id "$tcId.status_message" -Status "INFO" -Detail "DialogStatus: $msg"
    }

    # 6. ダイアログを閉じる（複数のダイアログが開いてる可能性あるので念のため複数試行）
    for ($i = 0; $i -lt 3; $i++) {
        if (-not (Close-DialogByCancel)) { break }
        Start-Sleep -Milliseconds 300
    }

    return $results
}
