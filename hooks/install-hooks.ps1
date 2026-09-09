<#
    Installs Claude Status Light hooks into ~\.claude\settings.json.
    - Copies set-status.ps1 into ~\.claude\hooks\ so the path is stable
    - Adds SessionStart, UserPromptSubmit, Notification, Stop, SessionEnd hooks
    - Preserves any existing settings.json content; backs it up first
    - Safe to re-run: skips hooks that are already installed

    Run from PowerShell:
        powershell -ExecutionPolicy Bypass -File install-hooks.ps1
#>

$claudeDir    = Join-Path $env:USERPROFILE '.claude'
$hooksDir     = Join-Path $claudeDir 'hooks'
$settingsPath = Join-Path $claudeDir 'settings.json'

New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null

$sourceScript = Join-Path $PSScriptRoot 'set-status.ps1'
$destScript   = Join-Path $hooksDir 'set-status.ps1'
Copy-Item -Path $sourceScript -Destination $destScript -Force

function Get-HookCommand([string]$status) {
    return "powershell -NoProfile -ExecutionPolicy Bypass -File `"$destScript`" $status"
}

if (Test-Path $settingsPath) {
    Copy-Item -Path $settingsPath -Destination "$settingsPath.bak" -Force
    $raw = Get-Content -Path $settingsPath -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        $settings = [PSCustomObject]@{}
    } else {
        $settings = $raw | ConvertFrom-Json
    }
} else {
    $settings = [PSCustomObject]@{}
}

if (-not ($settings.PSObject.Properties.Name -contains 'hooks')) {
    $settings | Add-Member -NotePropertyName 'hooks' -NotePropertyValue ([PSCustomObject]@{})
}
$hooks = $settings.hooks

function Add-StatusHook([string]$eventName, [string]$status) {
    $commandStr = Get-HookCommand $status
    $newEntry = [PSCustomObject]@{
        hooks = @([PSCustomObject]@{ type = 'command'; command = $commandStr })
    }

    if ($hooks.PSObject.Properties.Name -contains $eventName) {
        $existingEntries = @($hooks.$eventName)
        $alreadyPresent = $false
        foreach ($entry in $existingEntries) {
            foreach ($h in @($entry.hooks)) {
                if ($h.command -eq $commandStr) { $alreadyPresent = $true }
            }
        }
        if ($alreadyPresent) {
            Write-Host "Skipping $eventName (already installed)"
            return
        }
        $hooks.$eventName = $existingEntries + $newEntry
    } else {
        $hooks | Add-Member -NotePropertyName $eventName -NotePropertyValue @($newEntry)
    }
    Write-Host "Added $eventName hook"
}

Add-StatusHook 'SessionStart'     'idle'
Add-StatusHook 'UserPromptSubmit' 'running'
Add-StatusHook 'Notification'     'waiting'
Add-StatusHook 'Stop'             'done'
Add-StatusHook 'SessionEnd'       'idle'

$settings | ConvertTo-Json -Depth 20 | Set-Content -Path $settingsPath -Encoding UTF8

Write-Host ""
Write-Host "Done. Hooks written to $settingsPath"
Write-Host "Previous settings (if any) backed up to $settingsPath.bak"
Write-Host "Restart any running Claude Code sessions for the hooks to take effect."
