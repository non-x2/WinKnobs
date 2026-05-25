# WinKnobs UI Automation 統合ランナー（Phase B-2 + B-3 一部）
# 作成日: 2026-05-20
#
# 使い方:
#   ホスト側で直接実行（WinKnobs.exe をホストで起動済）:
#     .\run_ui_test.ps1 -Mode Local
#
#   VM 内で実行（PowerShell Direct + Copy-VMFile・管理者必須）:
#     .\run_ui_test.ps1 -Mode VM -AutoLaunch
#     .\run_ui_test.ps1 -Mode VM -TestCase bug1_regression,bug2_regression -AutoLaunch
#
#   テストケースを絞る:
#     .\run_ui_test.ps1 -Mode Local -TestCase smoke
#     .\run_ui_test.ps1 -Mode Local -TestCase smoke,dialog_open
#
#   結果ファイル指定:
#     .\run_ui_test.ps1 -Mode Local -ResultJson custom.json

param(
    [ValidateSet("Local", "VM")]
    [string]$Mode = "Local",

    [string[]]$TestCase = @("smoke", "dialog_open", "visual_ui", "dialog_choices", "dialog_inputs", "destructive_actions", "bug1_regression", "bug2_regression"),

    [string]$ResultJson = "",

    [int]$LaunchTimeoutSec = 15,

    [switch]$AutoLaunch,

    # 🆕 2026-05-20：UI Automation テスト中の WinKnobs を画面外に移動して邪魔にならないように
    # ユーザー要望「デスクトップ作業の邪魔にならないようバックグラウンドで」対応
    [switch]$Hidden,

    # 🆕 2026-05-21：ホストモード実行を承知の上で続行する明示スイッチ
    # ユーザー作業中はホストモード厳禁ルール（CLAUDE.md）の例外として明示確認
    [switch]$Force,

    # VM モード専用パラメータ
    [string]$VMName = "WinKnobs-Test",
    [string]$VMCredPath = "C:\AI\Claudo\アプリ\WinKnobs\scripts\vm_cred.xml",
    [string]$VMScriptsDir = "C:\ui_automation"
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

# ============================================================
# VM モード分岐：VM 内に転送して実行・結果回収
# ============================================================
if ($Mode -eq "VM") {
    if (-not (Test-Path $VMCredPath)) {
        Write-Host "FATAL: vm_cred.xml が見つかりません: $VMCredPath"
        exit 1
    }

    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Host "FATAL: VM モードは管理者 PowerShell 必須"
        exit 1
    }

    Write-Host "==== VM モード ===="
    Write-Host "VM: $VMName"
    Write-Host "TestCase: $($TestCase -join ', ')"

    $cred = Import-Clixml $VMCredPath

    # 1. VM 内に scripts/ui_automation 配下を全転送（Copy-VMFile 再帰相当）
    Write-Host "`n[1/4] VM 内にスクリプト群を投入中..."

    # 投入先フォルダを VM 内で作成
    Invoke-Command -VMName $VMName -Credential $cred -ScriptBlock {
        param($targetDir)
        if (Test-Path $targetDir) { Remove-Item -Recurse -Force $targetDir }
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        New-Item -ItemType Directory -Path "$targetDir\lib" -Force | Out-Null
        New-Item -ItemType Directory -Path "$targetDir\testcases" -Force | Out-Null
        New-Item -ItemType Directory -Path "$targetDir\results" -Force | Out-Null
        New-Item -ItemType Directory -Path "$targetDir\screenshots" -Force | Out-Null
    } -ArgumentList $VMScriptsDir

    # 必要ファイルを Copy-VMFile で転送
    $filesToCopy = @(
        @{ Source = "$scriptRoot\run_ui_test.ps1"; Dest = "$VMScriptsDir\run_ui_test.ps1" },
        @{ Source = "$scriptRoot\lib\uia_common.ps1"; Dest = "$VMScriptsDir\lib\uia_common.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_smoke.ps1"; Dest = "$VMScriptsDir\testcases\tc_smoke.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_dialog_open.ps1"; Dest = "$VMScriptsDir\testcases\tc_dialog_open.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_visual_ui.ps1"; Dest = "$VMScriptsDir\testcases\tc_visual_ui.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_dialog_choices.ps1"; Dest = "$VMScriptsDir\testcases\tc_dialog_choices.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_dialog_inputs.ps1"; Dest = "$VMScriptsDir\testcases\tc_dialog_inputs.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_destructive_actions.ps1"; Dest = "$VMScriptsDir\testcases\tc_destructive_actions.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_bug1_regression.ps1"; Dest = "$VMScriptsDir\testcases\tc_bug1_regression.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_bug2_regression.ps1"; Dest = "$VMScriptsDir\testcases\tc_bug2_regression.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_apply_registry.ps1"; Dest = "$VMScriptsDir\testcases\tc_apply_registry.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_apply_fsutil.ps1"; Dest = "$VMScriptsDir\testcases\tc_apply_fsutil.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_apply_v2.ps1"; Dest = "$VMScriptsDir\testcases\tc_apply_v2.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_startup_smoke.ps1"; Dest = "$VMScriptsDir\testcases\tc_startup_smoke.ps1" },
        @{ Source = "$scriptRoot\testcases\tc_startup_edit.ps1"; Dest = "$VMScriptsDir\testcases\tc_startup_edit.ps1" }
    )
    foreach ($f in $filesToCopy) {
        Copy-VMFile -Name $VMName -SourcePath $f.Source -DestinationPath $f.Dest -FileSource Host -Force
    }
    Write-Host "    投入完了：$($filesToCopy.Count) ファイル"

    # 2. VM 内でテスト実行（Mode=Local として実行・WinKnobs.exe は VM 内 C:\WinKnobs\ に既に投入済前提）
    Write-Host "`n[2/4] VM 内でテスト実行中..."
    $vmResult = Invoke-Command -VMName $VMName -Credential $cred -ScriptBlock {
        param($scriptsDir, $testCases, $autoLaunch)
        $args = @("-Mode", "Local", "-TestCase") + $testCases
        if ($autoLaunch) { $args += "-AutoLaunch" }

        # WinKnobs.exe の VM 内パス
        $vmExe = "C:\WinKnobs\WinKnobs.exe"
        if ($autoLaunch -and (Test-Path $vmExe)) {
            $proc = Get-Process WinKnobs -ErrorAction SilentlyContinue
            if (-not $proc) {
                Start-Process $vmExe
                Start-Sleep -Seconds 4
            }
        }

        # ローカル実行
        & "$scriptsDir\run_ui_test.ps1" @args
        $jsonFiles = Get-ChildItem "$scriptsDir\results\result_*.json" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($jsonFiles) {
            return @{
                ExitCode = $LASTEXITCODE
                JsonPath = $jsonFiles.FullName
                JsonContent = Get-Content -Raw -Encoding UTF8 $jsonFiles.FullName
            }
        } else {
            return @{ ExitCode = $LASTEXITCODE; JsonPath = $null; JsonContent = $null }
        }
    } -ArgumentList $VMScriptsDir, $TestCase, $AutoLaunch.IsPresent

    Write-Host "    VM 内実行完了（ExitCode=$($vmResult.ExitCode)）"

    # 3. 結果 JSON をホストに保存
    Write-Host "`n[3/4] 結果を回収中..."
    if (-not $ResultJson) {
        $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $ResultJson = "$scriptRoot\results\vm_result_$stamp.json"
    }
    if ($vmResult.JsonContent) {
        [System.IO.File]::WriteAllText($ResultJson, $vmResult.JsonContent, [System.Text.UTF8Encoding]::new($true))
        Write-Host "    保存: $ResultJson"
    } else {
        Write-Host "    WARN: VM 内に結果 JSON が見つかりませんでした"
    }

    # 4. スクリーンショット回収（VM 内 screenshots/ から host へ）
    Write-Host "`n[4/4] スクリーンショット回収..."
    $screenshots = Invoke-Command -VMName $VMName -Credential $cred -ScriptBlock {
        param($scriptsDir)
        Get-ChildItem "$scriptsDir\screenshots\fail_*.png" -ErrorAction SilentlyContinue | ForEach-Object {
            @{
                Name = $_.Name
                Base64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($_.FullName))
            }
        }
    } -ArgumentList $VMScriptsDir

    $shotDir = "$scriptRoot\screenshots"
    foreach ($shot in $screenshots) {
        $localPath = "$shotDir\vm_$($shot.Name)"
        [System.IO.File]::WriteAllBytes($localPath, [Convert]::FromBase64String($shot.Base64))
        Write-Host "    保存: $localPath"
    }

    Write-Host "`n==== VM モード完了 ===="
    Write-Host "Result JSON: $ResultJson"
    Write-Host "Screenshots: $shotDir\vm_fail_*.png"
    exit $vmResult.ExitCode
}

# ============================================================
# Local モード（既存処理）
# ============================================================

# 🆕 2026-05-21：ホストモード実行ガード（ユーザー要望「デスクトップ作業の邪魔にならない」対応）
# CLAUDE.md ルール：ユーザー作業中は VM 限定運用
# -Force または -Hidden のいずれかが指定されていない場合は停止
if (-not $Force -and -not $Hidden) {
    Write-Host ""
    Write-Host "⚠️  ホストモード実行ガード ⚠️" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "ホストで UIA テストを走らせると、マウス操作や画面表示がユーザーの作業を邪魔する可能性があります。" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "推奨：" -ForegroundColor Cyan
    Write-Host "  1. VM モードで実行     ：.\run_full_vm_test.ps1（vm_test_full → -Mode VM 連鎖）" -ForegroundColor Cyan
    Write-Host "  2. ホストで隠して実行  ：.\run_ui_test.ps1 -Mode Local -AutoLaunch -Hidden" -ForegroundColor Cyan
    Write-Host "  3. 強制的に前面表示    ：.\run_ui_test.ps1 -Mode Local -AutoLaunch -Force（開発デバッグ用）" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "詳細：CLAUDE.md「WinKnobs UI Automation テストは VM 優先」を参照" -ForegroundColor Gray
    Write-Host ""
    exit 2
}

# 共通モジュール読み込み
. "$scriptRoot\lib\uia_common.ps1"

# 各テストケース読み込み
# 注意：apply_* は副作用ありの VM 専用テストケース。既定の TestCase 配列には含めない
$tcFiles = @{
    "smoke"            = "tc_smoke.ps1"
    "dialog_open"      = "tc_dialog_open.ps1"
    "visual_ui"        = "tc_visual_ui.ps1"
    "dialog_choices"   = "tc_dialog_choices.ps1"
    "dialog_inputs"    = "tc_dialog_inputs.ps1"
    "destructive_actions" = "tc_destructive_actions.ps1"
    "bug1_regression"  = "tc_bug1_regression.ps1"
    "bug2_regression"  = "tc_bug2_regression.ps1"
    "apply_registry"   = "tc_apply_registry.ps1"
    "apply_fsutil"     = "tc_apply_fsutil.ps1"
    "apply_v2"         = "tc_apply_v2.ps1"
    "startup_smoke"    = "tc_startup_smoke.ps1"  # R-2 閲覧UI 動作確認（書込なし）
    "startup_edit"     = "tc_startup_edit.ps1"   # R-2 編集・保存・トグル（HKCU 書込発生・cleanup あり）
}

foreach ($tc in $TestCase) {
    if ($tcFiles.ContainsKey($tc)) {
        . "$scriptRoot\testcases\$($tcFiles[$tc])"
    } else {
        Write-Warning "Unknown test case: $tc"
    }
}

# 結果ファイル名
if (-not $ResultJson) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $ResultJson = "$scriptRoot\results\result_$stamp.json"
}
$screenshotDir = "$scriptRoot\screenshots"
$logPath = $ResultJson -replace "\.json$", ".log"

# 初期化
Initialize-Uia
$allResults = New-Object System.Collections.ArrayList
$startTime = Get-Date

function Write-Log {
    param([string]$Msg)
    Write-Host $Msg
    $Msg | Out-File -Encoding utf8 -Append $logPath
}

# ログ初期化
"==== WinKnobs UI Automation 統合ランナー ====" | Out-File -Encoding utf8 $logPath
("Start: " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss")) | Out-File -Encoding utf8 -Append $logPath
("Mode: " + $Mode) | Out-File -Encoding utf8 -Append $logPath
("TestCase: " + ($TestCase -join ", ")) | Out-File -Encoding utf8 -Append $logPath

# AutoLaunch: 既存 WinKnobs プロセスを完全 Kill してから 1 プロセスだけ起動
# 🆕 改善（2026-05-20）：複数 WinKnobs プロセス残存で UIA 検索が古い参照を返す致命バグ対策
if ($AutoLaunch) {
    $existing = Get-Process WinKnobs -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Log "AutoLaunch: 既存 WinKnobs プロセスを停止します（残存 $($existing.Count) 個）"
        foreach ($p in $existing) {
            try { $p.Kill($true) } catch {}
            taskkill /F /T /PID $p.Id 2>&1 | Out-Null
        }
        Start-Sleep -Seconds 2
    }

    $exe = "C:\AI\Claudo\アプリ\WinKnobs\src\WinKnobs\bin\Release\net10.0-windows\WinKnobs.exe"
    if (Test-Path $exe) {
        Write-Log "AutoLaunch: WinKnobs.exe を起動します"
        Start-Process $exe
        Start-Sleep -Seconds 4
    } else {
        Write-Log "ERROR: $exe が見つかりません。dotnet build -c Release してください"
        exit 1
    }
}

# WinKnobs ウィンドウ取得
Write-Log "WinKnobs ウィンドウ取得中..."
$win = Wait-WindowByName -Name "WinKnobs" -TimeoutSeconds $LaunchTimeoutSec
if (-not $win) {
    Write-Log "FATAL: WinKnobs ウィンドウが見つかりません（先に exe を起動してください）"
    exit 1
}
Write-Log "WinKnobs ウィンドウ取得 OK"

# 🆕 -Hidden: WinKnobs を画面外＋WS_EX_NOACTIVATE で完全隠し
# 2026-05-21 強化（ユーザー指摘「マウス・画面が奪われる」対応）：
# - Move-WindowOffScreen で画面外（-2000,-2000）
# - Set-WindowNoActivate で UIA Invoke によるフォーカス奪取を阻止
# - Set-HiddenMode で Open-DialogViaMenu 内のダイアログにも自動適用
if ($Hidden) {
    Set-HiddenMode -Enabled $true
    if (Hide-WindowCompletely -Window $win) {
        Write-Log "Hidden: WinKnobs を画面外＋NoActivate（フォーカス奪取阻止）に設定"
    } else {
        Write-Log "WARN: Hidden 設定に一部失敗（部分的に前面で実行される可能性）"
    }
} else {
    Set-HiddenMode -Enabled $false
}
Write-Log ""

# テストケース実行
foreach ($tc in $TestCase) {
    if (-not $tcFiles.ContainsKey($tc)) { continue }

    $funcName = switch ($tc) {
        "smoke"            { "Invoke-Smoke" }
        "dialog_open"      { "Invoke-DialogOpen" }
        "visual_ui"        { "Invoke-VisualUi" }
        "dialog_choices"   { "Invoke-DialogChoices" }
        "dialog_inputs"    { "Invoke-DialogInputs" }
        "destructive_actions" { "Invoke-DestructiveActions" }
        "bug1_regression"  { "Invoke-Bug1Regression" }
        "bug2_regression"  { "Invoke-Bug2Regression" }
        "apply_registry"   { "Invoke-ApplyRegistry" }
        "apply_fsutil"     { "Invoke-ApplyFsutil" }
        "apply_v2"         { "Invoke-ApplyV2" }
        "startup_smoke"    { "Invoke-StartupSmoke" }
        "startup_edit"     { "Invoke-StartupEdit" }
    }

    Write-Log ("===== TestCase: " + $tc + " =====")

    # 🆕 2026-05-20：Win 参照リフレッシュ（長時間テスト時の HRESULT: 0x80131505 タイムアウト対策）
    # bug1/2 で 200 秒超累計後に発生していた UI Automation 内部キャッシュ無効化への対処
    $refreshed = Wait-WindowByName -Name "WinKnobs" -TimeoutSeconds 5
    if ($refreshed) {
        $win = $refreshed
        # 🆕 2026-05-21：-Hidden 指定時は画面外＋NoActivate を再適用（フォーカス奪取阻止）
        if ($Hidden) { Hide-WindowCompletely -Window $win | Out-Null }
    } else {
        Write-Log "WARN: Win 参照リフレッシュ失敗（WinKnobs ウィンドウが見つからず・既存参照で続行）"
    }

    try {
        $tcResults = & $funcName -Win $win
        # 暗黙的 pipeline 出力（boolean 等の混入）を除去
        # PSCustomObject (New-TestResult が返す型) のみ通す
        $tcResults = @($tcResults | Where-Object { $_ -is [PSCustomObject] -and $null -ne $_.Status })

        $okCount = 0
        $failCount = 0
        $infoCount = 0
        foreach ($r in $tcResults) {
            $allResults.Add($r) | Out-Null
            switch ($r.Status) {
                "OK"   { $okCount++ }
                "FAIL" { $failCount++ }
                "INFO" { $infoCount++ }
            }
            $line = "[" + $r.Status.PadRight(4) + "] " + $r.Id + " : " + $r.Detail
            Write-Log $line

            # FAIL 時に自動スクショ
            if ($r.Status -eq "FAIL") {
                $stamp = Get-Date -Format "yyyyMMdd_HHmmssfff"
                $shotPath = "$screenshotDir\fail_${tc}_${stamp}.png"
                if (Take-Screenshot -Path $shotPath) {
                    $r.ScreenshotPath = $shotPath
                    Write-Log ("        → screenshot: " + $shotPath)
                }
            }
        }

        Write-Log ("Summary: OK=$okCount  FAIL=$failCount  INFO=$infoCount")
        Write-Log ""
    } catch {
        $errResult = New-TestResult -Id "$tc.exception" -Status "FAIL" -Detail $_.Exception.Message
        $allResults.Add($errResult) | Out-Null
        Write-Log ("[FAIL] " + $tc + " 例外発生: " + $_.Exception.Message)
    }
}

# JSON 出力
$endTime = Get-Date
$duration = ($endTime - $startTime).TotalSeconds

$totalOk   = ($allResults | Where-Object { $_.Status -eq "OK" }).Count
$totalFail = ($allResults | Where-Object { $_.Status -eq "FAIL" }).Count
$totalInfo = ($allResults | Where-Object { $_.Status -eq "INFO" }).Count

$summary = [PSCustomObject]@{
    Mode = $Mode
    StartTime = $startTime.ToString("yyyy-MM-dd HH:mm:ss")
    EndTime = $endTime.ToString("yyyy-MM-dd HH:mm:ss")
    DurationSec = [math]::Round($duration, 2)
    TestCases = $TestCase
    Counts = [PSCustomObject]@{
        OK = $totalOk
        FAIL = $totalFail
        INFO = $totalInfo
        Total = $allResults.Count
    }
    Results = $allResults
}

$summary | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 $ResultJson

Write-Log "========== Final Summary =========="
Write-Log ("Total: " + $allResults.Count)
Write-Log ("OK   : " + $totalOk)
Write-Log ("FAIL : " + $totalFail)
Write-Log ("INFO : " + $totalInfo)
Write-Log ("Duration: " + [math]::Round($duration, 2) + " sec")
Write-Log ""
Write-Log ("Result JSON: " + $ResultJson)
Write-Log ("Result Log : " + $logPath)

if ($totalFail -eq 0) {
    Write-Log "✅ ALL PASS"
    exit 0
} else {
    Write-Log "❌ FAIL: $totalFail 件"
    exit 1
}
