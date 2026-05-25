# hypervisorlaunchtype を auto に変更（admin 必要）
# 変更後はもう一度再起動が必要

param(
    [string]$LogPath = "C:\AI\Claudo\アプリ\WinKnobs\hyperv_enable_launch.log"
)

$result = [ordered]@{
    timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    isAdmin = $false
    before = ""
    after = ""
    bcdeditExit = -1
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
    $beforeLine = (bcdedit /enum '{current}' 2>&1 | Select-String "hypervisorlaunchtype").Line
    $result.before = if ($beforeLine) { $beforeLine.Trim() } else { "(not found)" }

    Write-Host "BEFORE: $($result.before)"
    Write-Host "Setting hypervisorlaunchtype to Auto..."

    bcdedit /set hypervisorlaunchtype auto | Out-Null
    $result.bcdeditExit = $LASTEXITCODE

    $afterLine = (bcdedit /enum '{current}' 2>&1 | Select-String "hypervisorlaunchtype").Line
    $result.after = if ($afterLine) { $afterLine.Trim() } else { "(not found)" }
    Write-Host "AFTER:  $($result.after)"
} catch {
    $result.errors += $_.Exception.Message
}

$result | ConvertTo-Json -Depth 5 | Out-File -Encoding UTF8 -Force $LogPath
Write-Host "Log: $LogPath"
