# TC-dialog-open: カテゴリ選択→項目選択→ダイアログ起動→閉じるの基本フロー
# 入力: $Win
# 出力: TestResult 配列

function Invoke-DialogOpen {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()
    $testItems = @(
        @{ Category = "Privacy"; Item = "privacy.cortanaDisabled"; Label = "PR1 Cortana" },
        @{ Category = "Privacy"; Item = "privacy.activityHistoryDisabled"; Label = "PR2 ActivityHistory" },
        @{ Category = "Network"; Item = "network.tcpAutoTuning"; Label = "N1 TCP Auto-Tuning" }
    )

    foreach ($t in $testItems) {
        $tcId = "dialog_open." + $t.Category + "." + $t.Item

        # 1. カテゴリ + 項目選択
        $sel = Select-CategoryAndItem -Win $Win -CategoryType $t.Category -ItemId $t.Item
        if (-not $sel.Success) {
            $results += New-TestResult -Id "$tcId.select" -Status "FAIL" -Detail "Step=$($sel.Step) / $($sel.Detail)"
            continue
        }
        $results += New-TestResult -Id "$tcId.select" -Status "OK" -Detail $t.Label

        # 2. ダイアログ起動
        $open = Open-DialogViaMenu -Win $Win
        if (-not $open.Success) {
            $results += New-TestResult -Id "$tcId.open" -Status "FAIL" -Detail $open.Detail
            continue
        }
        $results += New-TestResult -Id "$tcId.open" -Status "OK"

        # 3. ダイアログ内主要要素確認
        $dlgIds = @("OkButton", "ApplyButton", "RestoreButton", "CancelButton", "OriginalValueText", "DialogStatusText")
        foreach ($id in $dlgIds) {
            $e = Find-ById $open.DialogWindow $id 2
            if ($e) {
                $results += New-TestResult -Id "$tcId.dialog.$id" -Status "OK"
            } else {
                $results += New-TestResult -Id "$tcId.dialog.$id" -Status "FAIL" -Detail "$id not found in dialog"
            }
        }

        # 4. CancelButton でダイアログ閉じ
        $closed = Close-DialogByCancel
        if ($closed) {
            $results += New-TestResult -Id "$tcId.close" -Status "OK"
        } else {
            $results += New-TestResult -Id "$tcId.close" -Status "FAIL" -Detail "Cancel close failed"
        }
        Start-Sleep -Milliseconds 500
    }

    return $results
}
