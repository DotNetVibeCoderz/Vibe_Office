<#
.SYNOPSIS
    Removes AutoWork from Windows.

.DESCRIPTION
    Deletes the program files and shortcuts. Your configuration, API keys, knowledge bases and
    logs under %APPDATA%\AutoWork are kept unless you pass -PurgeData, because losing an API
    key to an uninstall is a nasty surprise.

.EXAMPLE
    .\uninstall.ps1
    .\uninstall.ps1 -PurgeData
#>

[CmdletBinding()]
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\AutoWork",
    [switch]$PurgeData
)

$ErrorActionPreference = 'Stop'

Write-Host ''
Write-Host '  Removing AutoWork' -ForegroundColor White
Write-Host ''

Get-Process -Name 'AutoWork' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "    Closing the running AutoWork (pid $($_.Id))" -ForegroundColor Yellow
    $_.Kill(); $_.WaitForExit(5000) | Out-Null
}

if (Test-Path $InstallDir) {
    Remove-Item -Recurse -Force $InstallDir
    Write-Host "    Removed $InstallDir" -ForegroundColor Green
} else {
    Write-Host "    Nothing installed at $InstallDir" -ForegroundColor DarkGray
}

foreach ($link in @(
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\AutoWork.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'AutoWork.lnk'))) {
    if (Test-Path $link) {
        Remove-Item -Force $link
        Write-Host "    Removed shortcut $(Split-Path -Leaf $link)" -ForegroundColor Green
    }
}

$dataDir = Join-Path $env:APPDATA 'AutoWork'

if ($PurgeData) {
    if (Test-Path $dataDir) {
        Remove-Item -Recurse -Force $dataDir
        Write-Host "    Removed $dataDir (config, keys, knowledge, logs)" -ForegroundColor Green
    }
} elseif (Test-Path $dataDir) {
    Write-Host ''
    Write-Host "  Your data is still at $dataDir" -ForegroundColor White
    Write-Host '  Run again with -PurgeData to delete it as well.' -ForegroundColor DarkGray
}

Write-Host ''
Write-Host '  Done.' -ForegroundColor Green
Write-Host ''
