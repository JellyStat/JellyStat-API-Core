<#
.SYNOPSIS
    Builds the Windows installer locally (the same steps as the build-windows CI job).

.DESCRIPTION
    Checks for the .NET 10 SDK and Inno Setup, offering to install whichever is missing with winget,
    checks for a UI build in wwwroot\, publishes a self-contained win-x64 build to publish\, then
    compiles installer\jellystat.iss into installer\Output\.

.PARAMETER Yes
    Install missing prerequisites without asking.

.PARAMETER SkipPublish
    Reuse the existing publish\ folder, e.g. after only changing jellystat.iss.

.PARAMETER Version
    Installer version. Defaults to AssemblyVersion from Jellystat-API-Core.csproj.

.EXAMPLE
    .\installer\build-installer.ps1
    .\installer\build-installer.ps1 -Yes
    .\installer\build-installer.ps1 -SkipPublish
#>
[CmdletBinding()]
param(
    [switch]$Yes,
    [switch]$SkipPublish,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Jellystat-API-Core.csproj'
$publishDir = Join-Path $repoRoot 'publish'
$script = Join-Path $PSScriptRoot 'jellystat.iss'
$wwwroot = Join-Path $repoRoot 'wwwroot'

# Picks up PATH changes made by an installer that ran during this session
function Update-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"
}

function Confirm-Install([string]$Name) {
    if ($Yes) { return $true }
    $answer = Read-Host "$Name is not installed. Install it now with winget? [Y/n]"
    return ($answer -eq '' -or $answer -match '^(y|yes)$')
}

function Install-WithWinget([string]$Name, [string]$Id) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "winget is not available, so $Name can't be installed automatically. Install it manually and run this script again."
    }
    Write-Host "Installing $Name..."
    & winget install --id $Id --exact --silent --accept-package-agreements --accept-source-agreements
    # winget's exit codes aren't reliable (e.g. "already installed" is non-zero), so callers check for the tool afterwards
    Update-SessionPath
}

function Test-DotnetSdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    return [bool](& dotnet --list-sdks | Where-Object { $_ -match '^10\.' })
}

function Find-Iscc {
    # Inno Setup records where it was installed, whichever folder or scope winget chose
    $uninstallKeys = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup*',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup*',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup*'
    )
    foreach ($key in Get-Item $uninstallKeys -ErrorAction SilentlyContinue) {
        $location = $key.GetValue('InstallLocation')
        if ($location -and (Test-Path (Join-Path $location 'ISCC.exe'))) {
            return (Join-Path $location 'ISCC.exe')
        }
    }

    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup*\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup*\ISCC.exe",
        "$env:ProgramFiles\Inno Setup*\ISCC.exe"
    )
    $found = Get-ChildItem $candidates -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if ($found) { return $found.FullName }

    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    return $null
}

# 1. .NET 10 SDK
if (-not (Test-DotnetSdk)) {
    if (-not (Confirm-Install '.NET 10 SDK')) {
        throw '.NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download'
    }
    Install-WithWinget '.NET 10 SDK' 'Microsoft.DotNet.SDK.10'
    if (-not (Test-DotnetSdk)) {
        throw '.NET 10 SDK still not found. Open a new terminal and run this script again, or install it from https://dotnet.microsoft.com/download'
    }
}
Write-Host ".NET SDK:   $((& dotnet --list-sdks | Where-Object { $_ -match '^10\.' } | Select-Object -Last 1).Split(' ')[0])"

# 2. Inno Setup, checked before publishing so a missing compiler fails fast
$iscc = Find-Iscc
if (-not $iscc) {
    if (-not (Confirm-Install 'Inno Setup')) {
        throw 'Inno Setup is required to build the installer. Install it from https://jrsoftware.org/isdl.php'
    }
    Install-WithWinget 'Inno Setup' 'JRSoftware.InnoSetup'
    $iscc = Find-Iscc
    if (-not $iscc) {
        throw 'Inno Setup still not found (ISCC.exe). Install it from https://jrsoftware.org/isdl.php and run this script again.'
    }
}
Write-Host "Inno Setup: $iscc"

# 3. UI build. Without it the installer still builds, but Jellystat serves no web UI until it updates itself.
$uiTag = Get-ChildItem (Join-Path $wwwroot '*.emtag') -ErrorAction SilentlyContinue | Select-Object -First 1
if ($uiTag) {
    Write-Host "UI build:   $($uiTag.BaseName)"
}
else {
    Write-Warning "No UI build found in $wwwroot. Extract a jellystat-ui release zip there to bundle the web UI."
}

# 4. Version
if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.AssemblyVersion | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "AssemblyVersion not found in $project. Pass -Version." }
}
Write-Host "Version:    $Version"

# 5. Publish
if ($SkipPublish) {
    if (-not (Test-Path (Join-Path $publishDir 'Jellystat-API-Core.exe'))) {
        throw "-SkipPublish was given but $publishDir has no build. Run without -SkipPublish."
    }
    Write-Host "Reusing $publishDir"
}
else {
    # Start clean so files removed from the project don't linger in the installer
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    Write-Host 'Publishing win-x64...'
    & dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }
}

# 6. Installer
Write-Host 'Compiling installer...'
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" $script
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit code $LASTEXITCODE)." }

$output = Join-Path $PSScriptRoot "Output\Jellystat-Setup-$Version.exe"
Write-Host ''
Write-Host "Installer: $output" -ForegroundColor Green
