# TC-startup-smoke: Phase R-2 スタートアップ系 閲覧UI 動作確認（書込なし）
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# 確認内容：
#   - Startup カテゴリ選択でペイン切替
#   - StartupListView / StartupItemsGrid 表示
#   - ツールバー 5 ボタン（追加・編集・削除・保存・戻す）
#   - 既存 R-1 で完成済の閲覧機能リグレッション

function Invoke-StartupSmoke {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()

    # ─── 1. Startup カテゴリ選択 ───
    $cat = Find-ById $Win "Category_Startup" 5
    if (-not $cat) {
        $results += New-TestResult -Id "startup_smoke.category.find" -Status "FAIL" -Detail "Category_Startup AutomationId not found"
        return $results
    }
    $results += New-TestResult -Id "startup_smoke.category.find" -Status "OK"

    if (-not (Select-Element $cat)) {
        $results += New-TestResult -Id "startup_smoke.category.select" -Status "FAIL" -Detail "SelectionItemPattern.Select failed"
        return $results
    }
    Start-Sleep -Milliseconds 800
    $results += New-TestResult -Id "startup_smoke.category.select" -Status "OK"

    # ─── 2. ペイン切替確認（StartupListView 表示・既存 ItemsGrid は非表示） ───
    $view = Find-ById $Win "StartupListView" 3
    $results += New-TestResult -Id "startup_smoke.view.visible" -Status $(if ($view) { "OK" } else { "FAIL" }) -Detail "StartupListView UserControl"

    $grid = Find-ById $Win "StartupItemsGrid" 3
    $results += New-TestResult -Id "startup_smoke.grid.visible" -Status $(if ($grid) { "OK" } else { "FAIL" }) -Detail "StartupItemsGrid DataGrid"

    # ─── 3. ツールバー 5 ボタン確認 ───
    $toolbarButtons = @(
        @{ Id = "StartupAddButton";    Label = "+ 追加 (R-3 で実装予定)" },
        @{ Id = "StartupEditButton";   Label = "✏ 編集" },
        @{ Id = "StartupDeleteButton"; Label = "🗑 削除 (R-3 で実装予定)" },
        @{ Id = "StartupSaveButton";   Label = "💾 保存" },
        @{ Id = "StartupRevertButton"; Label = "↺ 戻す" }
    )
    foreach ($b in $toolbarButtons) {
        $btn = Find-ById $Win $b.Id 2
        $results += New-TestResult -Id "startup_smoke.toolbar.$($b.Id)" -Status $(if ($btn) { "OK" } else { "FAIL" }) -Detail $b.Label
    }

    # ─── 4. ステータステキスト確認 ───
    $statusText = Find-ById $Win "StartupStatusText" 2
    if ($statusText) {
        $text = Get-ElementName $statusText
        $results += New-TestResult -Id "startup_smoke.status.text" -Status "OK" -Detail $text
    } else {
        $results += New-TestResult -Id "startup_smoke.status.text" -Status "FAIL" -Detail "StartupStatusText not found"
    }

    # ─── 5. エントリ表示件数（最低 1 件あるはず・実機 Run キー登録分） ───
    if ($grid) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::DataItem
        )
        $rows = $grid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
        $count = if ($rows) { $rows.Count } else { 0 }
        $results += New-TestResult -Id "startup_smoke.entries.count" -Status "INFO" -Detail "$count エントリ表示（Run キー登録分）"
    }

    # ─── 6. ホーム カテゴリへ戻して回帰なし確認 ───
    $homeCat = Find-ById $Win "Category_Home" 3
    if ($homeCat) {
        Select-Element $homeCat | Out-Null
        Start-Sleep -Milliseconds 500
        $itemsGridAfter = Find-ById $Win "ItemsGrid" 3
        $results += New-TestResult -Id "startup_smoke.return.home" -Status $(if ($itemsGridAfter) { "OK" } else { "FAIL" }) -Detail "ItemsGrid 再表示確認"
    }

    return $results
}
