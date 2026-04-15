[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\studyhub-web\src\studyhub.app\studyhub.app.csproj'
$distRoot = Join-Path $repoRoot 'dist\windows\studyhub-windows-x64'
$runtimeOutput = Join-Path $distRoot 'runtime'
$guidePath = Join-Path $distRoot 'como-abrir.txt'
$launcherPath = Join-Path $distRoot 'abrir-studyhub.cmd'
$publishFramework = 'net10.0-windows10.0.19041.0'

if (-not (Test-Path $appProject)) {
    throw "StudyHub app project was not found at '$appProject'."
}

Write-Host "Preparing clean Windows distribution at $distRoot" -ForegroundColor Cyan

if (Test-Path $distRoot) {
    Remove-Item -LiteralPath $distRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $runtimeOutput -Force | Out-Null

$publishArguments = @(
    'publish',
    $appProject,
    '-f', $publishFramework,
    '-c', 'Release',
    '--self-contained', 'false',
    '-o', $runtimeOutput,
    '-v', 'minimal'
)

if ($SkipBuild) {
    $publishArguments += '--no-build'
}

Write-Host 'Running dotnet publish for the clean distribution package...' -ForegroundColor Cyan
& dotnet @publishArguments

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$launcher = @"
@echo off
setlocal
start "" /d "%~dp0runtime" "studyhub.app.exe"
"@

Set-Content -LiteralPath $launcherPath -Value $launcher -Encoding ASCII

$guide = @"
StudyHub for Windows
====================

This folder is ready to be zipped and shared.

How to share
------------
1. Zip the folder 'studyhub-windows-x64'.
2. Send that zip to the other person.

How the other person runs the app
---------------------------------
1. Extract the zip anywhere on the computer.
2. Double-click 'abrir-studyhub.cmd'.

Alternative
-----------
If needed, the real app executable is inside:
runtime\studyhub.app.exe

Important
---------
- This package does not include your personal StudyHub data.
- The app will create its own local database, routine files, and backups on the new machine.
- The recipient must configure their own API keys and import their own courses.

Do not include in the zip
-------------------------
- your local course folders
- your AppData StudyHub database
- routine JSON files
- StudyHub backups
"@

Set-Content -LiteralPath $guidePath -Value $guide -Encoding UTF8

Write-Host ''
Write-Host 'Clean distribution ready.' -ForegroundColor Green
Write-Host "Folder to zip: $distRoot" -ForegroundColor Green
Write-Host "Launcher: $launcherPath" -ForegroundColor Green
Write-Host "Executable: $(Join-Path $runtimeOutput 'studyhub.app.exe')" -ForegroundColor Green
