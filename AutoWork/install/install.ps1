<#
.SYNOPSIS
    Builds and installs AutoWork on Windows.

.DESCRIPTION
    Publishes a self-contained-ish build, copies it under %LOCALAPPDATA%\Programs\AutoWork,
    and creates Start Menu and optional Desktop shortcuts.

    The script is deliberately re-runnable: running it again upgrades in place and leaves
    %APPDATA%\AutoWork (config, secrets, knowledge, logs) untouched.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -Desktop
    .\install.ps1 -InstallDir "D:\Apps\AutoWork"

.NOTES
    AutoWork — Gravicode Studios, led by Kang Fadhil.
#>

[CmdletBinding()]
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\AutoWork",
    [switch]$Desktop,
    [switch]$SkipShortcuts,
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    # Fast: precompiled ahead of time, so the window appears in well under a second.
    # Small: about 55 MB less on disk, and roughly twice as long to start.
    # Measured on the developer machine: 878 ms versus 1,821 ms to a visible window,
    # and 137 MB versus 82 MB installed.
    [ValidateSet('Fast', 'Small')]
    [string]$Startup = 'Fast'
)

$ErrorActionPreference = 'Stop'

function Write-Step  { param($m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok    { param($m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn2 { param($m) Write-Host "    $m" -ForegroundColor Yellow }

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\AutoWork.Desktop\AutoWork.Desktop.csproj'

Write-Host ''
Write-Host '  AutoWork installer' -ForegroundColor White
Write-Host '  Gravicode Studios - led by Kang Fadhil' -ForegroundColor DarkGray
Write-Host ''

# ── Prerequisites ────────────────────────────────────────────────────────────────────────
Write-Step 'Checking prerequisites'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw "The .NET SDK was not found. Install .NET 10 from https://dotnet.microsoft.com/download and run this again."
}

$sdks = & dotnet --list-sdks
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw "AutoWork needs the .NET 10 SDK. Installed SDKs:`n$($sdks -join "`n")"
}
Write-Ok ".NET 10 SDK found"

if (-not (Test-Path $project)) {
    throw "Could not find $project. Run this script from the repository's install folder."
}

# ── Build ────────────────────────────────────────────────────────────────────────────────
Write-Step 'Building AutoWork (this takes a minute on a first run)'

$staging = Join-Path ([System.IO.Path]::GetTempPath()) "autowork-publish-$([guid]::NewGuid().ToString('n').Substring(0,8))"

$readyToRun = if ($Startup -eq 'Fast') { 'true' } else { 'false' }

& dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained false `
    --output $staging `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=$readyToRun `
    --nologo `
    --verbosity quiet

if ($LASTEXITCODE -ne 0) { throw "The build failed. See the output above." }

$size = [math]::Round((Get-ChildItem $staging -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Ok "Built for $Runtime — $Startup startup, $size MB"

# ── Install ──────────────────────────────────────────────────────────────────────────────
Write-Step "Installing to $InstallDir"

# Stop a running copy, or the file copy below fails with a lock.
Get-Process -Name 'AutoWork' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Warn2 "Closing the running AutoWork (pid $($_.Id))"
    $_.Kill()
    $_.WaitForExit(5000) | Out-Null
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $staging '*') -Destination $InstallDir -Recurse -Force
Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue

$exe = Join-Path $InstallDir 'AutoWork.exe'
if (-not (Test-Path $exe)) { throw "The build finished but AutoWork.exe is missing from $InstallDir." }
Write-Ok 'Files installed'

# ── Shortcuts ────────────────────────────────────────────────────────────────────────────
if (-not $SkipShortcuts) {
    Write-Step 'Creating shortcuts'

    $shell = New-Object -ComObject WScript.Shell

    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\AutoWork.lnk'
    $link = $shell.CreateShortcut($startMenu)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $InstallDir
    $link.Description = 'AutoWork - your digital coworker'
    $link.Save()
    Write-Ok 'Start Menu shortcut created'

    if ($Desktop) {
        $desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'AutoWork.lnk'
        $link = $shell.CreateShortcut($desktopLink)
        $link.TargetPath = $exe
        $link.WorkingDirectory = $InstallDir
        $link.Description = 'AutoWork - your digital coworker'
        $link.Save()
        Write-Ok 'Desktop shortcut created'
    }
}

# ── Done ─────────────────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Installed.' -ForegroundColor Green
Write-Host ''
Write-Host "  Program:  $InstallDir"
Write-Host "  Your data: $env:APPDATA\AutoWork"
Write-Host ''
Write-Host '  Next: open AutoWork, go to Settings > Models and add a provider key,' -ForegroundColor White
Write-Host '        then Settings > Permissions and grant it a folder to work in.' -ForegroundColor White
Write-Host ''
Write-Host '  Nothing on your computer is reachable until you grant a folder.' -ForegroundColor DarkGray
Write-Host ''

# Only ask when there is someone to answer. Run under -NonInteractive — CI, a provisioning
# script, an MDM push — Read-Host throws, and the installer would exit non-zero having already
# succeeded, telling the caller that a completed install had failed.
if ($Host.UI.RawUI -and -not [Environment]::UserInteractive) {
    Write-Host '  Run AutoWork.exe when you are ready.' -ForegroundColor DarkGray
    return
}

try {
    $answer = Read-Host '  Start AutoWork now? [Y/n]'
}
catch {
    Write-Host '  Run AutoWork.exe when you are ready.' -ForegroundColor DarkGray
    return
}

if ($answer -eq '' -or $answer -match '^[Yy]') { Start-Process $exe }
