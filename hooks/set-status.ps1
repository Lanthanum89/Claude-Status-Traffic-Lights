<#
    Writes the current Claude Code session's status to
    %LOCALAPPDATA%\ClaudeStatusLight\sessions\<session-id>.json.
    Called by Claude Code hooks (see install-hooks.ps1), which pipe a JSON
    payload (session_id, cwd, ...) on stdin. Not meant to be run manually,
    though `powershell -File set-status.ps1 running` works fine for testing
    -- with no piped input it falls back to a "manual" session id.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('idle', 'waiting', 'running', 'done', 'ended')]
    [string]$Status
)

$dir = Join-Path $env:LOCALAPPDATA 'ClaudeStatusLight'
$sessionsDir = Join-Path $dir 'sessions'
New-Item -ItemType Directory -Path $sessionsDir -Force | Out-Null

$sessionId = 'manual'
$cwd = (Get-Location).Path

if ([Console]::IsInputRedirected) {
    try {
        $raw = [Console]::In.ReadToEnd()
        if (-not [string]::IsNullOrWhiteSpace($raw)) {
            $payload = $raw | ConvertFrom-Json
            if ($payload.session_id) { $sessionId = $payload.session_id }
            if ($payload.cwd) { $cwd = $payload.cwd }
        }
    } catch {
        # Malformed or unexpected payload -- fall back to the defaults above
        # rather than failing the hook.
    }
}

$safeId = ($sessionId -replace '[^A-Za-z0-9_-]', '-')
$file = Join-Path $sessionsDir "$safeId.json"

if ($Status -eq 'ended') {
    Remove-Item -Path $file -Force -ErrorAction SilentlyContinue
    exit 0
}

$payload = @{
    status  = $Status
    updated = (Get-Date).ToUniversalTime().ToString('o')
    cwd     = $cwd
} | ConvertTo-Json -Compress

$tempFile = Join-Path $sessionsDir ([System.IO.Path]::GetRandomFileName())
Set-Content -Path $tempFile -Value $payload -Encoding UTF8
Move-Item -Path $tempFile -Destination $file -Force
