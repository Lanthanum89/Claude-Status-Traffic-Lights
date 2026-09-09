<#
    Builds ClaudeStatusLight and installs it to
    %LOCALAPPDATA%\Programs\ClaudeStatusLight\, so "deploy" is one command
    instead of a manual dotnet publish + copy every time.

    Run from PowerShell, from anywhere in the repo:
        powershell -ExecutionPolicy Bypass -File publish.ps1

    If the app is currently running, exit it first (right-click the dot ->
    Exit) so the exe isn't locked when this script overwrites it.
#>

$ErrorActionPreference = 'Stop'

$projectDir = Join-Path $PSScriptRoot 'ClaudeStatusLight'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\ClaudeStatusLight'

Write-Host "Publishing ClaudeStatusLight..."
dotnet publish $projectDir -c Release
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishDir = Join-Path $projectDir 'bin\Release\net8.0-windows\win-x64\publish'
$exePath = Join-Path $publishDir 'ClaudeStatusLight.exe'
if (-not (Test-Path $exePath)) {
    throw "Expected publish output not found at $exePath"
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -Path $exePath -Destination $installDir -Force

Write-Host ""
Write-Host "Installed to $installDir\ClaudeStatusLight.exe"
Write-Host "Re-launch the app (or re-add it to shell:startup) to pick up the update."
