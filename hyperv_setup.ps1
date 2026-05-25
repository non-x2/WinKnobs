# Hyper-V 機能 確認＋有効化スクリプト
# 管理者権限で実行されることを前提
# 結果は JSON ログとして hyperv_setup.log に書き出す

param(
    [string]$LogPath = "C:\AI\Claudo\アプリ\WinKnobs\hyperv_setup.log"
)

$result = [ordered]@{
    timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    status = "Unknown"
    needRestart = $false
    isAdmin = $false
    featuresBefore = [ordered]@{}
    featuresAfter = [ordered]@{}
    errors = @()
}

function Save-Result {
    $result | ConvertTo-Json -Depth 5 |
        Out-File -FilePath $LogPath -Encoding UTF8 -Force
}

# 管理者権限チェック
try {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object System.Security.Principal.WindowsPrincipal($id)
    $result.isAdmin = $p.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
} catch {
    $result.errors += "AdminCheck: $($_.Exception.Message)"
}

if (-not $result.isAdmin) {
    $result.status = "NotElevated"
    Save-Result
    Write-Host "ERROR: Not elevated"
    exit 1
}

try {
    $features = @(
        'Microsoft-Hyper-V-All',
        'Microsoft-Hyper-V-Hypervisor',
        'Microsoft-Hyper-V-Management-PowerShell',
        'Microsoft-Hyper-V-Management-Clients',
        'Microsoft-Hyper-V-Services'
    )

    # 現状確認
    foreach ($f in $features) {
        try {
            $feat = Get-WindowsOptionalFeature -Online -FeatureName $f -ErrorAction Stop
            $result.featuresBefore[$f] = $feat.State.ToString()
        } catch {
            $result.featuresBefore[$f] = "NotFound"
        }
    }

    if ($result.featuresBefore['Microsoft-Hyper-V-All'] -eq 'Enabled') {
        $result.status = "AlreadyEnabled"
        $result.needRestart = $false
        Write-Host "Hyper-V is already enabled. No action needed."
    } else {
        Write-Host "Enabling Hyper-V (this may take a few minutes)..."
        $r = Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All `
            -NoRestart -ErrorAction Stop
        $result.status = "JustEnabled"
        $result.needRestart = [bool]$r.RestartNeeded

        foreach ($f in $features) {
            try {
                $feat = Get-WindowsOptionalFeature -Online -FeatureName $f -ErrorAction Stop
                $result.featuresAfter[$f] = $feat.State.ToString()
            } catch {
                $result.featuresAfter[$f] = "NotFound"
            }
        }
        Write-Host "Hyper-V enabled. RestartNeeded: $($result.needRestart)"
    }
} catch {
    $result.status = "Error"
    $result.errors += $_.Exception.Message
    Write-Host "ERROR: $($_.Exception.Message)"
}

Save-Result
Write-Host "Log saved: $LogPath"
