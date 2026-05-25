# Hyper-V 診断スクリプト（admin 必要）
# 状態詳細・bcdedit・モジュール配置を確認

param(
    [string]$LogPath = "C:\AI\Claudo\アプリ\WinKnobs\hyperv_diagnose.log"
)

$result = [ordered]@{
    timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    isAdmin = $false
    features = [ordered]@{}
    hypervisorLaunchType = ""
    bcdeditFull = ""
    moduleSearch = @()
    services = [ordered]@{}
    errors = @()
}

$id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$p = New-Object System.Security.Principal.WindowsPrincipal($id)
$result.isAdmin = $p.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $result.isAdmin) {
    $result.errors += "Not admin"
    $result | ConvertTo-Json -Depth 5 | Out-File -Encoding UTF8 -Force $LogPath
    exit 1
}

try {
    $featureNames = @(
        'Microsoft-Hyper-V-All',
        'Microsoft-Hyper-V',
        'Microsoft-Hyper-V-Hypervisor',
        'Microsoft-Hyper-V-Management-PowerShell',
        'Microsoft-Hyper-V-Management-Clients',
        'Microsoft-Hyper-V-Services',
        'Microsoft-Hyper-V-Tools-All'
    )
    foreach ($f in $featureNames) {
        try {
            $feat = Get-WindowsOptionalFeature -Online -FeatureName $f -ErrorAction Stop
            $result.features[$f] = $feat.State.ToString()
        } catch {
            $result.features[$f] = "NotFound"
        }
    }
} catch {
    $result.errors += "Features: $($_.Exception.Message)"
}

# bcdedit hypervisorlaunchtype
try {
    $bcd = bcdedit /enum '{current}' 2>&1
    $result.bcdeditFull = ($bcd -join "`n")
    $line = $bcd | Select-String -Pattern "hypervisorlaunchtype" | Select-Object -First 1
    if ($line) {
        $result.hypervisorLaunchType = $line.Line.Trim()
    } else {
        $result.hypervisorLaunchType = "(not set)"
    }
} catch {
    $result.errors += "bcdedit: $($_.Exception.Message)"
}

# モジュール検索
$searchPaths = @(
    'C:\Windows\System32\WindowsPowerShell\v1.0\Modules\Hyper-V',
    'C:\Windows\SysWOW64\WindowsPowerShell\v1.0\Modules\Hyper-V',
    'C:\Program Files\WindowsPowerShell\Modules\Hyper-V'
)
foreach ($sp in $searchPaths) {
    if (Test-Path $sp) {
        $result.moduleSearch += "$sp : EXISTS"
    } else {
        $result.moduleSearch += "$sp : missing"
    }
}

# サービス確認
$svcNames = @('vmms','vmcompute','vmickvpexchange','vmicguestinterface','vmicheartbeat','vmicrdv','vmicshutdown','vmictimesync','vmicvss')
foreach ($s in $svcNames) {
    $sv = Get-Service -Name $s -ErrorAction SilentlyContinue
    if ($sv) {
        $result.services[$s] = "$($sv.Status) / $($sv.StartType)"
    } else {
        $result.services[$s] = "NotFound"
    }
}

$result | ConvertTo-Json -Depth 5 | Out-File -Encoding UTF8 -Force $LogPath
Write-Host "Diagnose done. Log: $LogPath"
