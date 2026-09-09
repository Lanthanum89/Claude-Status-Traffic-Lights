# Claude Status Light

Small always-on-top overlay that mirrors Claude Code's state, same idea as the
physical traffic light: grey = idle, amber = running, red = waiting for
confirmation, green = finished and ready for a new task.

## How it works

- Claude Code hooks write status to `%LOCALAPPDATA%\ClaudeStatusLight\status.json`
- The WinForms overlay polls that file every 500ms and recolours a small
  draggable dot pinned on top of everything else

## Setup

**1. Install the hooks (one-off)**

```
powershell -ExecutionPolicy Bypass -File hooks\install-hooks.ps1
```

This merges hook entries into `~\.claude\settings.json`, backing up any
existing file first. Safe to re-run.

**2. Build the overlay app**

Requires the .NET 8 SDK (`dotnet --version` to check).

```
cd ClaudeStatusLight
dotnet publish -c Release
```

Output exe: `bin\Release\net8.0-windows\win-x64\publish\ClaudeStatusLight.exe`
It's self-contained, so you can copy that one file anywhere without installing
the .NET runtime.

**3. Run it**

Double-click `ClaudeStatusLight.exe`. A grey dot appears top-right. Drag it
anywhere, its position is remembered on restart. Right-click to exit.

**Run at login (optional):** press `Win+R`, type `shell:startup`, drop a
shortcut to the exe in there.

## Known limitation

The status file is global, not per-project. If you run more than one Claude
Code session at once, the light reflects whichever session fired a hook most
recently, not each session individually.

## Testing without Claude Code

```
powershell -File hooks\set-status.ps1 waiting
powershell -File hooks\set-status.ps1 running
powershell -File hooks\set-status.ps1 done
```
