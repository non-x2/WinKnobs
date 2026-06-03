# TC-smoke: UIA 基盤動作確認＋主要 AutomationId 検出
# 入力: $Win (WinKnobs ウィンドウ AutomationElement)
# 出力: TestResult 配列

function Invoke-Smoke {
    param([Parameter(Mandatory=$true)]$Win)

    $results = @()

    # 1. ウィンドウ取得済（呼び出し元で実施済）
    $results += New-TestResult -Id "smoke.window_detected" -Status "OK" -Detail "WinKnobs window found"

    # 2. 主要静的要素
    $statics = @(
        "SearchBox", "CategoriesTree", "ItemsGrid", "StatusMessageText",
        "MenuFile", "MenuEdit", "MenuView", "MenuTools", "MenuHelp"
    )
    foreach ($id in $statics) {
        $e = Find-ById $Win $id 2
        if ($e) {
            $results += New-TestResult -Id "smoke.static.$id" -Status "OK"
        } else {
            $results += New-TestResult -Id "smoke.static.$id" -Status "FAIL" -Detail "AutomationId not found"
        }
    }

    # 3. 動的カテゴリ（12 カテゴリ中代表 5 個・v0.1.9 で Accessibility 追加）
    $categories = @("Home", "System", "Privacy", "Network", "Kernel")
    foreach ($cat in $categories) {
        $id = "Category_$cat"
        $e = Find-ById $Win $id 2
        if ($e) {
            $results += New-TestResult -Id "smoke.category.$cat" -Status "OK"
        } else {
            $results += New-TestResult -Id "smoke.category.$cat" -Status "FAIL" -Detail "$id not found"
        }
    }

    # 4. メニュー展開→配下取得
    $menuFile = Find-ById $Win "MenuFile" 2
    if ($menuFile) {
        $expanded = Expand-Element $menuFile
        Start-Sleep -Milliseconds 500
        if ($expanded) {
            $children = @("MenuOpenItem", "MenuExitApp", "MenuOpenBackupFolderFile")
            foreach ($c in $children) {
                $e = Find-ById $Win $c 2
                if ($e) {
                    $results += New-TestResult -Id "smoke.menu.$c" -Status "OK"
                } else {
                    $results += New-TestResult -Id "smoke.menu.$c" -Status "FAIL" -Detail "$c not found after expand"
                }
            }
            Collapse-Element $menuFile | Out-Null
            Start-Sleep -Milliseconds 300
        } else {
            $results += New-TestResult -Id "smoke.menu.expand" -Status "FAIL" -Detail "MenuFile expand failed"
        }
    }

    # 5. メモリ使用量
    $proc = Get-Process WinKnobs -ErrorAction SilentlyContinue
    if ($proc) {
        $memMB = [math]::Round($proc.WorkingSet64 / 1MB, 1)
        $results += New-TestResult -Id "smoke.memory" -Status "INFO" -Detail "$memMB MB / PID $($proc.Id)"
    }

    return $results
}
