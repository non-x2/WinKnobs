# UI Automation 共通モジュール（Phase B-2）
# 作成日: 2026-05-20
# 用途: ホスト/VM 両方で使える UIA ヘルパー関数群
#
# 使い方:
#   . "$PSScriptRoot\lib\uia_common.ps1"
#   Initialize-Uia
#   $win = Wait-WindowByName "WinKnobs" 15
#   $btn = Find-ById $win "ApplyButton" 5

# ============================================================
# 初期化
# ============================================================

function Initialize-Uia {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms

    # Win32 API（ウィンドウ移動・前面化制御）の P/Invoke を 1 回だけ登録
    if (-not ([System.Management.Automation.PSTypeName]'WinKnobsUia.Win32').Type) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
namespace WinKnobsUia {
    public class Win32 {
        [DllImport("user32.dll")]
        public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        // SetWindowPos flags
        public const uint SWP_NOSIZE         = 0x0001;
        public const uint SWP_NOZORDER       = 0x0004;
        public const uint SWP_NOACTIVATE     = 0x0010;
        public const uint SWP_SHOWWINDOW     = 0x0040;
        public const uint SWP_FRAMECHANGED   = 0x0020;

        // ShowWindow constants
        public const int SW_HIDE             = 0;
        public const int SW_SHOWMINNOACTIVE  = 7;
        public const int SW_SHOWNOACTIVATE   = 4;

        // GetWindowLong/SetWindowLong indices
        public const int GWL_EXSTYLE         = -20;

        // Extended window styles
        public const int WS_EX_NOACTIVATE    = 0x08000000;
        public const int WS_EX_TOOLWINDOW    = 0x00000080;
    }
}
"@
    }
}

# 🆕 2026-05-21：UIA テスト中のフォーカス・前面化抑制（ユーザー作業を邪魔しない対応）
# Hidden モードのグローバルフラグ（uia_common.ps1 内のスクリプトスコープ）
$script:UiaHiddenMode = $false

function Set-HiddenMode {
    param([bool]$Enabled)
    $script:UiaHiddenMode = $Enabled
}

function Get-HiddenMode {
    return $script:UiaHiddenMode
}

function Set-WindowNoActivate {
    # ウィンドウを「アクティブ化されない」状態にする（WS_EX_NOACTIVATE）
    # 効果：UIA Invoke / マウス・キーボード経由でも前面化されない
    # 制約：ユーザーがクリックしてもアクティブにならない → テスト用プロセス専用
    param([Parameter(Mandatory=$true)]$Window)
    try {
        $hwnd = [IntPtr]::new([int]$Window.Current.NativeWindowHandle)
        if ($hwnd -eq [IntPtr]::Zero) { return $false }

        $exStyle = [WinKnobsUia.Win32]::GetWindowLong($hwnd, [WinKnobsUia.Win32]::GWL_EXSTYLE)
        $newStyle = $exStyle -bor [WinKnobsUia.Win32]::WS_EX_NOACTIVATE
        [WinKnobsUia.Win32]::SetWindowLong($hwnd, [WinKnobsUia.Win32]::GWL_EXSTYLE, $newStyle) | Out-Null

        # スタイル変更を即時反映
        [WinKnobsUia.Win32]::SetWindowPos(
            $hwnd, [IntPtr]::Zero,
            0, 0, 0, 0,
            [WinKnobsUia.Win32]::SWP_NOSIZE -bor `
            [WinKnobsUia.Win32]::SWP_NOZORDER -bor `
            [WinKnobsUia.Win32]::SWP_NOACTIVATE -bor `
            [WinKnobsUia.Win32]::SWP_FRAMECHANGED
        ) | Out-Null
        return $true
    } catch {
        return $false
    }
}

function Hide-WindowCompletely {
    # 「画面外移動 + フォーカス奪取阻止」をワンセットで適用
    # 戻り値：両方成功で $true・どちらか失敗で $false
    param(
        [Parameter(Mandatory=$true)]$Window,
        [int]$X = -2000,
        [int]$Y = -2000
    )
    $moveOk = Move-WindowOffScreen -Window $Window -X $X -Y $Y
    $styleOk = Set-WindowNoActivate -Window $Window
    return ($moveOk -and $styleOk)
}

function Move-WindowOffScreen {
    # ウィンドウを画面外に移動して「邪魔にならない」状態にする
    # 2026-05-20 追加（デスクトップ作業の邪魔にならないように対応）
    # UIA はバックグラウンド・画面外でも操作可能
    param(
        [Parameter(Mandatory=$true)]$Window,
        [int]$X = -2000,
        [int]$Y = -2000,
        [int]$Width = 1280,
        [int]$Height = 720
    )
    try {
        $hwnd = [IntPtr]::new([int]$Window.Current.NativeWindowHandle)
        if ($hwnd -eq [IntPtr]::Zero) { return $false }
        # SWP_NOACTIVATE で前面化を抑制しつつ移動
        [WinKnobsUia.Win32]::SetWindowPos(
            $hwnd,
            [IntPtr]::Zero,
            $X, $Y, $Width, $Height,
            [WinKnobsUia.Win32]::SWP_NOZORDER -bor [WinKnobsUia.Win32]::SWP_NOACTIVATE
        ) | Out-Null
        return $true
    } catch {
        return $false
    }
}

# ============================================================
# 要素検索（AutomationId / Name ベース）
# ============================================================

function Find-ById {
    param(
        [Parameter(Mandatory=$true)]$Root,
        [Parameter(Mandatory=$true)][string]$Id,
        [double]$TimeoutSeconds = 5.0,
        [string]$Scope = "Descendants"
    )
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id
    )
    $treeScope = if ($Scope -eq "Children") {
        [System.Windows.Automation.TreeScope]::Children
    } else {
        [System.Windows.Automation.TreeScope]::Descendants
    }
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $TimeoutSeconds) {
        $elem = $Root.FindFirst($treeScope, $cond)
        if ($elem) { return $elem }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Find-ByName {
    param(
        [Parameter(Mandatory=$true)]$Root,
        [Parameter(Mandatory=$true)][string]$Name,
        [double]$TimeoutSeconds = 5.0
    )
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name
    )
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $TimeoutSeconds) {
        $elem = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($elem) { return $elem }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Wait-WindowByName {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [double]$TimeoutSeconds = 15.0
    )
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name
    )
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $TimeoutSeconds) {
        $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
        if ($win) { return $win }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Wait-WindowByAutomationId {
    # Window レベルの AutomationId で対象ウィンドウを取得（ダイアログ等）
    # 実機調査結果（2026-05-20）：WPF Modal Dialog は Owner Window の Children として現れる
    # → Descendants で全階層を検索（Children だと拾えない）
    # 検索ルートを -Root で指定可能（既定: RootElement）。Owner Window 指定で高速化＆一意性向上
    param(
        [Parameter(Mandatory=$true)][string]$AutomationId,
        [double]$TimeoutSeconds = 5.0,
        $Root = $null
    )
    if (-not $Root) {
        $Root = [System.Windows.Automation.AutomationElement]::RootElement
    }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId
    )
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $TimeoutSeconds) {
        $win = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($win) { return $win }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

# ============================================================
# 操作パターン（Invoke / Select / Expand / Value）
# ============================================================

function Invoke-Element {
    param([Parameter(Mandatory=$true)]$Element)
    try {
        $p = $Element.GetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern
        )
        $p.Invoke()
        return $true
    } catch {
        return $false
    }
}

function Select-Element {
    param([Parameter(Mandatory=$true)]$Element)
    try {
        $p = $Element.GetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern
        )
        $p.Select()
        return $true
    } catch {
        return $false
    }
}

function Expand-Element {
    param([Parameter(Mandatory=$true)]$Element)
    try {
        $p = $Element.GetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern
        )
        $p.Expand()
        return $true
    } catch {
        return $false
    }
}

function Collapse-Element {
    param([Parameter(Mandatory=$true)]$Element)
    try {
        $p = $Element.GetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern
        )
        $p.Collapse()
        return $true
    } catch {
        return $false
    }
}

function Set-ElementValue {
    param(
        [Parameter(Mandatory=$true)]$Element,
        [Parameter(Mandatory=$true)]
        [AllowEmptyString()]
        [string]$Value
    )
    try {
        $p = $Element.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern
        )
        $p.SetValue($Value)
        return $true
    } catch {
        return $false
    }
}

function Clear-SearchBox {
    param([Parameter(Mandatory=$true)]$Win)
    $sb = Find-ById $Win "SearchBox" 2
    if (-not $sb) { return $false }
    try {
        $vp = $sb.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $vp.SetValue("")
        Start-Sleep -Milliseconds 400
        return $true
    } catch {
        return $false
    }
}

function Get-ElementValue {
    param([Parameter(Mandatory=$true)]$Element)
    try {
        $p = $Element.GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern
        )
        return $p.Current.Value
    } catch {
        return $null
    }
}

function Get-ElementName {
    param([Parameter(Mandatory=$true)]$Element)
    return $Element.GetCurrentPropertyValue(
        [System.Windows.Automation.AutomationElement]::NameProperty
    )
}

# ============================================================
# 複合操作
# ============================================================

function Select-CategoryAndItem {
    param(
        [Parameter(Mandatory=$true)]$Win,
        [Parameter(Mandatory=$true)][string]$CategoryType,
        [Parameter(Mandatory=$true)][string]$ItemId
    )
    # カテゴリ選択
    $catId = "Category_" + $CategoryType
    $cat = Find-ById $Win $catId 5
    if (-not $cat) { return @{ Success = $false; Step = "find_category"; Detail = "$catId not found" } }
    if (-not (Select-Element $cat)) { return @{ Success = $false; Step = "select_category"; Detail = "$catId select failed" } }
    Start-Sleep -Milliseconds 500

    # 項目選択
    $itId = "Item_" + $ItemId
    $item = Find-ById $Win $itId 5
    if (-not $item) { return @{ Success = $false; Step = "find_item"; Detail = "$itId not found" } }
    if (-not (Select-Element $item)) { return @{ Success = $false; Step = "select_item"; Detail = "$itId select failed" } }
    Start-Sleep -Milliseconds 300

    return @{ Success = $true; Category = $cat; Item = $item }
}

function Open-DialogViaMenu {
    param([Parameter(Mandatory=$true)]$Win)
    $menuFile = Find-ById $Win "MenuFile" 3
    if (-not $menuFile) { return @{ Success = $false; Detail = "MenuFile not found" } }

    # 念のため事前に Collapse（前回の展開が残っている場合の対策）
    try {
        $ecp = $menuFile.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        if ($ecp.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Expanded) {
            $ecp.Collapse()
            Start-Sleep -Milliseconds 200
        }
    } catch {}

    if (-not (Expand-Element $menuFile)) { return @{ Success = $false; Detail = "MenuFile expand failed" } }
    Start-Sleep -Milliseconds 700

    # MenuOpenItem 取得は子要素として直接検索（メニュー閉じている時のフォールバック）
    $menuOpen = Find-ById $Win "MenuOpenItem" 5
    if (-not $menuOpen) {
        # 1 回 Collapse → 再 Expand のリトライ
        try { $ecp.Collapse() } catch {}
        Start-Sleep -Milliseconds 300
        Expand-Element $menuFile | Out-Null
        Start-Sleep -Milliseconds 700
        $menuOpen = Find-ById $Win "MenuOpenItem" 3
    }
    if (-not $menuOpen) { return @{ Success = $false; Detail = "MenuOpenItem not found after expand (retried)" } }
    if (-not (Invoke-Element $menuOpen)) { return @{ Success = $false; Detail = "MenuOpenItem invoke failed" } }
    Start-Sleep -Seconds 1

    # 🆕 改善（2026-05-20）：SettingDialog Window を AutomationId で一意取得
    # WPF Modal Dialog は WinKnobs Window の Children として現れる（実機調査）
    # → 検索ルートに $Win を指定して高速＆一意取得
    $dlgWindow = Wait-WindowByAutomationId -AutomationId "SettingDialog" -TimeoutSeconds 5 -Root $Win
    if (-not $dlgWindow) {
        return @{ Success = $false; Detail = "SettingDialog Window 未検出（WinKnobs 配下）" }
    }

    # 🆕 2026-05-21：Hidden モード時はダイアログにも NoActivate + 画面外移動を適用
    # UIA Invoke で前面化される現象（マウス奪取・画面に出現）への対策
    if (Get-HiddenMode) {
        Hide-WindowCompletely -Window $dlgWindow | Out-Null
        # メインウィンドウも念のため再押し戻し（ダイアログ起動で前面化される可能性）
        Hide-WindowCompletely -Window $Win | Out-Null
    }

    # 互換性のため DialogRoot も維持（旧コード対応）
    $dlgRoot = [System.Windows.Automation.AutomationElement]::RootElement
    $ok = Find-ById $dlgWindow "OkButton" 5
    if (-not $ok) { return @{ Success = $false; Detail = "OkButton 未検出（SettingDialog Window 配下）" } }

    return @{
        Success = $true
        DialogWindow = $dlgWindow       # 🆕 推奨：Window 配下で検索すれば残骸を拾わない
        DialogRoot = $dlgRoot           # 旧互換用
        OkButton = $ok
    }
}

function Close-DialogByCancel {
    # 🆕 改善（2026-05-20）：Cancel クリック後に「未保存の変更」MessageBox が出る場合があるため
    # 自動的に「いいえ」（変更破棄）を選んでダイアログを確実に閉じる
    # 旧実装は MessageBox を無視し、結果ダイアログが残存 → 次のテストで前ダイアログの値を引きずる致命的バグの原因
    $dlgRoot = [System.Windows.Automation.AutomationElement]::RootElement
    $cancel = Find-ById $dlgRoot "CancelButton" 2
    if (-not $cancel) { return $false }
    $ok = Invoke-Element $cancel
    Start-Sleep -Milliseconds 800

    # 「未保存の変更」MessageBox が出ているか確認・出ていたら「いいえ」（変更破棄）クリック
    $msgBoxCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "未保存の変更"
    )
    $msgBox = $dlgRoot.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $msgBoxCond)
    if ($msgBox) {
        # 「いいえ(N)」または英語版「No」を探してクリック
        foreach ($noLabel in @("いいえ(N)", "No")) {
            $noCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, $noLabel
            )
            $noBtn = $msgBox.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $noCond)
            if ($noBtn) {
                Invoke-Element $noBtn | Out-Null
                Start-Sleep -Milliseconds 500
                break
            }
        }
    }
    return $ok
}

# ============================================================
# スクリーンショット撮影
# ============================================================

function Take-Screenshot {
    param([Parameter(Mandatory=$true)][string]$Path)
    try {
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose()
        $bmp.Dispose()
        return $true
    } catch {
        return $false
    }
}

function Take-ScreenshotBase64 {
    # VM 内で撮影してホストに base64 で返却する用途
    try {
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $b64 = [Convert]::ToBase64String($ms.ToArray())
        $g.Dispose()
        $bmp.Dispose()
        $ms.Dispose()
        return $b64
    } catch {
        return $null
    }
}

function Save-ScreenshotFromBase64 {
    param(
        [Parameter(Mandatory=$true)][string]$Base64,
        [Parameter(Mandatory=$true)][string]$Path
    )
    try {
        $bytes = [Convert]::FromBase64String($Base64)
        [System.IO.File]::WriteAllBytes($Path, $bytes)
        return $true
    } catch {
        return $false
    }
}

# ============================================================
# ダイアログ存在チェック
# ============================================================

function Test-DialogExists {
    param([Parameter(Mandatory=$true)][string]$Name)
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name
    )
    $dlg = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    return ($dlg -ne $null)
}

# ============================================================
# レジストリ・netsh ヘルパー（適用テスト用・VM 専用想定）
# ⚠️ ホスト実行は HKCU/HKLM への書込発生。VM 内またはチェックポイント環境で。
# ============================================================

function Read-RegValue {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Name
    )
    $p = Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
    if ($p -and ($p.PSObject.Properties.Name -contains $Name)) {
        return @{ Exists = $true; Value = $p.$Name }
    }
    return @{ Exists = $false; Value = $null }
}

function Write-RegValue {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)]$Value,
        [Parameter(Mandatory=$true)][string]$Type
    )
    if (-not (Test-Path $Path)) { New-Item -Path $Path -Force | Out-Null }
    Set-ItemProperty -Path $Path -Name $Name -Value $Value -Type $Type
}

function Restore-RegValue {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)]$Before,
        [Parameter(Mandatory=$true)][string]$Type
    )
    if ($Before.Exists) {
        if (-not (Test-Path $Path)) { New-Item -Path $Path -Force | Out-Null }
        Set-ItemProperty -Path $Path -Name $Name -Value $Before.Value -Type $Type
    } else {
        Remove-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
    }
}

function ConvertTo-RegDisplay {
    param([Parameter(Mandatory=$true)]$RegResult)
    return "Exists=$($RegResult.Exists) Value=$($RegResult.Value)"
}

function Get-AutoTuningLevel {
    # netsh interface tcp show global を実行して AutoTuning レベルを抽出（英/日両対応）
    $output = (& netsh interface tcp show global 2>&1) | Out-String
    foreach ($line in ($output -split "`r?`n")) {
        if ($line -match "(?i)(Auto[- ]?Tuning|自動チューニング)") {
            if ($line -match "[:：]\s*(\w+)\s*$") {
                return $Matches[1]
            }
        }
    }
    return $null
}

# ============================================================
# テスト結果構造体ヘルパー
# ============================================================

function New-TestResult {
    param(
        [Parameter(Mandatory=$true)][string]$Id,
        [Parameter(Mandatory=$true)][string]$Status,  # OK / FAIL / SKIP / INFO
        [string]$Detail = "",
        [string]$ScreenshotPath = ""
    )
    return [PSCustomObject]@{
        Id = $Id
        Status = $Status
        Detail = $Detail
        ScreenshotPath = $ScreenshotPath
        Timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff")
    }
}
