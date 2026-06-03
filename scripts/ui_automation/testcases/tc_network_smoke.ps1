# TC-network-smoke: v0.1.7 ネットワークカテゴリ 閲覧UI 動作確認（書込なし）
# 作成日: 2026-06-03
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列
#
# 確認内容（すべて表示確認＝レジストリ/netsh 書込なし・ホスト/VM どちらでも安全）：
#   - Network カテゴリ選択でペイン切替（IsNetworkCategorySelected）
#   - 今の状態（NetworkDnsSummary）表示
#   - かんたん設定3ボタン（帯域抑制 / DNS=Cloudflare / 軽いリセット=flushdns）
#   - Windowsの画面を開く 4ボタン（機能0 純正ランチャー）
#   - ぜんぶ元に戻す（NetworkRevertAllButton）
#   - くわしい設定 Expander 展開 → 3択DNS / アダプタリスト / 手入力3欄+適用 / 既存項目Grid
#   - ステータステキスト
#   - ホーム カテゴリへ戻して回帰なし確認
#
# 注意：詳細層は Expander（既定折りたたみの可能性）の中にあるため、
#       初期展開状態に依存せず Expand-Element で確実に展開してから検証する。

function Invoke-NetworkSmoke {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()

    # ─── 1. Network カテゴリ選択 ───
    $cat = Find-ById $Win "Category_Network" 5
    if (-not $cat) {
        $results += New-TestResult -Id "network_smoke.category.find" -Status "FAIL" -Detail "Category_Network AutomationId not found"
        return $results
    }
    $results += New-TestResult -Id "network_smoke.category.find" -Status "OK"

    if (-not (Select-Element $cat)) {
        $results += New-TestResult -Id "network_smoke.category.select" -Status "FAIL" -Detail "SelectionItemPattern.Select failed"
        return $results
    }
    Start-Sleep -Milliseconds 800
    $results += New-TestResult -Id "network_smoke.category.select" -Status "OK"

    # ─── 2. ペイン切替確認（NetworkDnsSummary 表示＝ネットワークペイン表示判定） ───
    $summary = Find-ById $Win "NetworkDnsSummary" 3
    $results += New-TestResult -Id "network_smoke.pane.visible" -Status $(if ($summary) { "OK" } else { "FAIL" }) -Detail "NetworkDnsSummary TextBlock（今の状態）"
    if ($summary) {
        $results += New-TestResult -Id "network_smoke.dns.summary" -Status "INFO" -Detail (Get-ElementName $summary)
    }

    # ─── 3. かんたん設定 3ボタン ───
    $easyButtons = @(
        @{ Id = "NetworkBandwidthButton"; Label = "🚀 通信を軽くする（配信最適化P2Pオフ）" },
        @{ Id = "NetworkDnsButton";       Label = "🌐 DNSを速くする（Cloudflare）" },
        @{ Id = "NetworkResetButton";     Label = "🔧 不調を直す（flushdns）" }
    )
    foreach ($b in $easyButtons) {
        $btn = Find-ById $Win $b.Id 2
        $results += New-TestResult -Id "network_smoke.easy.$($b.Id)" -Status $(if ($btn) { "OK" } else { "FAIL" }) -Detail $b.Label
    }

    # ─── 4. Windowsの画面を開く 4ボタン（機能0 純正ランチャー） ───
    $launcherButtons = @(
        @{ Id = "NetworkOpenStatusButton";   Label = "🪟 ネットの状態" },
        @{ Id = "NetworkOpenWifiButton";     Label = "📶 Wi-Fi" },
        @{ Id = "NetworkOpenAdaptersButton"; Label = "🔌 アダプター" },
        @{ Id = "NetworkOpenSharingButton";  Label = "🌍 共有センター" }
    )
    foreach ($b in $launcherButtons) {
        $btn = Find-ById $Win $b.Id 2
        $results += New-TestResult -Id "network_smoke.launcher.$($b.Id)" -Status $(if ($btn) { "OK" } else { "FAIL" }) -Detail $b.Label
    }

    # ─── 5. ぜんぶ元に戻す ───
    $revert = Find-ById $Win "NetworkRevertAllButton" 2
    $results += New-TestResult -Id "network_smoke.revertall" -Status $(if ($revert) { "OK" } else { "FAIL" }) -Detail "↩ ぜんぶ元の状態に戻す"

    # ─── 6. くわしい設定 Expander を展開 ───
    $expander = Find-ById $Win "NetworkAdvancedExpander" 3
    if (-not $expander) {
        $results += New-TestResult -Id "network_smoke.advanced.expander" -Status "FAIL" -Detail "NetworkAdvancedExpander not found"
    } else {
        $results += New-TestResult -Id "network_smoke.advanced.expander" -Status "OK" -Detail "くわしい設定 Expander"
        Expand-Element $expander | Out-Null
        Start-Sleep -Milliseconds 600

        # ─── 7. 詳細層 DNS 3択 ───
        $dnsChoiceButtons = @(
            @{ Id = "NetworkDnsCloudflareButton"; Label = "Cloudflare (1.1.1.1)" },
            @{ Id = "NetworkDnsGoogleButton";     Label = "Google (8.8.8.8)" },
            @{ Id = "NetworkDnsAutoButton";       Label = "自動 (DHCP) に戻す" }
        )
        foreach ($b in $dnsChoiceButtons) {
            $btn = Find-ById $Win $b.Id 2
            $results += New-TestResult -Id "network_smoke.dnschoice.$($b.Id)" -Status $(if ($btn) { "OK" } else { "FAIL" }) -Detail $b.Label
        }

        # ─── 8. 対象アダプタ リスト + CheckBox 件数 ───
        $adapterList = Find-ById $Win "NetworkAdapterList" 2
        $results += New-TestResult -Id "network_smoke.adapter.list" -Status $(if ($adapterList) { "OK" } else { "FAIL" }) -Detail "NetworkAdapterList ItemsControl"
        if ($adapterList) {
            $cbCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::CheckBox
            )
            $cbs = $adapterList.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cbCond)
            $cbCount = if ($cbs) { $cbs.Count } else { 0 }
            # VM は仮想NICのみ＝アクティブ物理アダプタ 0 件があり得る（再開ガイド記載）→ INFO 扱い
            $results += New-TestResult -Id "network_smoke.adapter.count" -Status "INFO" -Detail "$cbCount アダプタ（VM=仮想NICのみは0件あり得る）"
        }

        # ─── 9. DNS 手入力 3欄 + 適用ボタン ───
        $customInputs = @(
            @{ Id = "NetworkDnsCustomV4Primary";   Label = "優先 (IPv4)" },
            @{ Id = "NetworkDnsCustomV4Secondary"; Label = "代替 (IPv4)" },
            @{ Id = "NetworkDnsCustomV6";          Label = "IPv6 (任意)" }
        )
        foreach ($b in $customInputs) {
            $tb = Find-ById $Win $b.Id 2
            $results += New-TestResult -Id "network_smoke.custom.$($b.Id)" -Status $(if ($tb) { "OK" } else { "FAIL" }) -Detail $b.Label
        }
        $applyBtn = Find-ById $Win "NetworkDnsCustomApplyButton" 2
        $results += New-TestResult -Id "network_smoke.custom.apply" -Status $(if ($applyBtn) { "OK" } else { "FAIL" }) -Detail "この内容で適用"

        # ─── 10. 既存ネットワーク項目 Grid（HTTP同時接続数 / TCP Auto-Tuning） ───
        $advGrid = Find-ById $Win "NetworkAdvancedGrid" 2
        $results += New-TestResult -Id "network_smoke.advgrid" -Status $(if ($advGrid) { "OK" } else { "FAIL" }) -Detail "NetworkAdvancedGrid DataGrid"
        if ($advGrid) {
            $cond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::DataItem
            )
            $rows = $advGrid.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
            $rowCount = if ($rows) { $rows.Count } else { 0 }
            $results += New-TestResult -Id "network_smoke.advgrid.count" -Status "INFO" -Detail "$rowCount 件（HTTP同時接続数 / TCP Auto-Tuning＝2件想定）"
        }
    }

    # ─── 11. ステータステキスト ───
    $statusText = Find-ById $Win "NetworkStatusText" 2
    if ($statusText) {
        $text = Get-ElementName $statusText
        $results += New-TestResult -Id "network_smoke.status.text" -Status "OK" -Detail $text
    } else {
        $results += New-TestResult -Id "network_smoke.status.text" -Status "FAIL" -Detail "NetworkStatusText not found"
    }

    # ─── 12. ホーム カテゴリへ戻して回帰なし確認 ───
    $homeCat = Find-ById $Win "Category_Home" 3
    if ($homeCat) {
        Select-Element $homeCat | Out-Null
        Start-Sleep -Milliseconds 500
        $itemsGridAfter = Find-ById $Win "ItemsGrid" 3
        $results += New-TestResult -Id "network_smoke.return.home" -Status $(if ($itemsGridAfter) { "OK" } else { "FAIL" }) -Detail "ItemsGrid 再表示確認"
    }

    return $results
}
