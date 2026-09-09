<#
    Writes the current Claude Code status to %LOCALAPPDATA%\ClaudeStatusLight\status.json
    Called by Claude Code hooks (see install-hooks.ps1). Not meant to be run manually,
    though `powershell -File set-status.ps1 running` works fine for testing.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('idle', 'waiting', 'running', 'done')]
    [string]$Status
)

$dir = Join-Path $env:LOCALAPPDATA 'ClaudeStatusLight'
if (-not (Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$file = Join-Path $dir 'status.json'
$payload = @{
    status  = $Status
    updated = (Get-Date).ToUniversalTime().ToString('o')
} | ConvertTo-Json -Compress

$tempFile = Join-Path $dir ([System.IO.Path]::GetRandomFileName())
Set-Content -Path $tempFile -Value $payload -Encoding UTF8
Move-Item -Path $tempFile -Destination $file -Force
