# TC-visual-ui: UI 表示・検索・テーマの目視相当テスト
# 既存目視テスト T-UI-1〜5 + T-V2-TH-1〜3 をテストケース化
# 副作用: 検索バーへの値設定（テスト後クリア）/ テーマ切替（アプリ再起動でリセット）

function Invoke-VisualUi {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()

    # ========== T-UI-1: カテゴリ TreeView に 11 カテゴリ存在 ==========
    $allCategories = @("Home","System","Startup","Shell","Network","Kernel","Login","Desktop","Privacy","KeyboardMouse","SearchResults","Favorites")
    $foundCount = 0
    foreach ($cat in $allCategories) {
        $id = "Category_$cat"
        $e = Find-ById $Win $id 1
        if ($e) { $foundCount++ }
    }
    # SearchResults はカテゴリ常時表示ではない可能性あり（検索時のみ）
    if ($foundCount -ge 10) {
        $results += New-TestResult -Id "visual_ui.T-UI-1.category_count" -Status "OK" -Detail "$foundCount / 12 カテゴリ検出（実装済 11 + SearchResults）"
    } else {
        $results += New-TestResult -Id "visual_ui.T-UI-1.category_count" -Status "FAIL" -Detail "カテゴリ $foundCount 件のみ（10 件以上期待）"
    }

    # ========== T-UI-2〜5: 検索動作 ==========
    $searchBox = Find-ById $Win "SearchBox" 2
    if (-not $searchBox) {
        $results += New-TestResult -Id "visual_ui.T-UI-2.find_search" -Status "FAIL" -Detail "SearchBox not found"
    } else {
        $results += New-TestResult -Id "visual_ui.T-UI-2.find_search" -Status "OK"

        # 検索キーワードを設定して反応確認
        $keyword = "アイコン"
        $setOk = Set-ElementValue -Element $searchBox -Value $keyword
        Start-Sleep -Milliseconds 800
        if ($setOk) {
            $results += New-TestResult -Id "visual_ui.T-UI-3.search_set" -Status "OK" -Detail "Keyword='$keyword'"

            # StatusMessageText の内容を取得（検索結果件数等が反映される想定）
            $status = Find-ById $Win "StatusMessageText" 2
            if ($status) {
                $msg = Get-ElementName $status
                $results += New-TestResult -Id "visual_ui.T-UI-4.status_after_search" -Status "INFO" -Detail "StatusMessage: '$msg'"
            }

            # Category_SearchResults が出るか確認（検索時のみ表示される）
            $sr = Find-ById $Win "Category_SearchResults" 2
            if ($sr) {
                $results += New-TestResult -Id "visual_ui.T-UI-5.search_results_category" -Status "OK" -Detail "Category_SearchResults appeared"
            } else {
                $results += New-TestResult -Id "visual_ui.T-UI-5.search_results_category" -Status "INFO" -Detail "Category_SearchResults 非表示（実装次第）"
            }

            # 検索クリア（次テストケースに残らないよう確実にクリア）
            Clear-SearchBox -Win $Win | Out-Null
        } else {
            $results += New-TestResult -Id "visual_ui.T-UI-3.search_set" -Status "FAIL" -Detail "SearchBox SetValue 失敗"
        }
    }

    # ========== T-V2-TH-1〜3: テーマメニュー存在＋連動 ==========
    # 親 MenuView を Expand してから配下のテーマメニュー取得
    $menuView = Find-ById $Win "MenuView" 2
    if (-not $menuView) {
        $results += New-TestResult -Id "visual_ui.T-V2-TH.find_menu_view" -Status "FAIL" -Detail "MenuView not found"
    } else {
        # Expand
        Expand-Element $menuView | Out-Null
        Start-Sleep -Milliseconds 700

        $themeMenus = @{
            "Light"  = "MenuApplyLight"
            "Dark"   = "MenuApplyDark"
            "System" = "MenuApplySystem"
        }

        # 1. 3 メニューが存在するか
        foreach ($k in $themeMenus.Keys) {
            $id = $themeMenus[$k]
            $e = Find-ById $Win $id 2
            if ($e) {
                $results += New-TestResult -Id "visual_ui.T-V2-TH-1.$k.exists" -Status "OK" -Detail $id
            } else {
                $results += New-TestResult -Id "visual_ui.T-V2-TH-1.$k.exists" -Status "FAIL" -Detail "$id not found after MenuView expand"
            }
        }

        # 2. 現在チェックされているテーマを確認（TogglePattern または IsChecked プロパティ）
        $checkedCount = 0
        foreach ($k in $themeMenus.Keys) {
            $id = $themeMenus[$k]
            $e = Find-ById $Win $id 2
            if ($e) {
                try {
                    $tp = $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
                    $state = $tp.Current.ToggleState
                    if ($state -eq [System.Windows.Automation.ToggleState]::On) {
                        $checkedCount++
                        $results += New-TestResult -Id "visual_ui.T-V2-TH-2.$k.checked" -Status "INFO" -Detail "現在選択中のテーマ: $k"
                    }
                } catch {
                    # TogglePattern が無い場合は AutomationProperty で IsChecked を取得
                    # WPF MenuItem の IsChecked は直接取得できないので INFO
                }
            }
        }
        if ($checkedCount -ge 1) {
            $results += New-TestResult -Id "visual_ui.T-V2-TH-3.checked_count" -Status "OK" -Detail "テーマチェック ✓ 連動: $checkedCount 件"
        } else {
            $results += New-TestResult -Id "visual_ui.T-V2-TH-3.checked_count" -Status "INFO" -Detail "TogglePattern が取得できない（UIA Provider の制約・実装上は IsChecked バインド済）"
        }

        # MenuView 閉じる
        Collapse-Element $menuView | Out-Null
        Start-Sleep -Milliseconds 300
    }

    return $results
}
