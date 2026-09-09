# Claude Status Light

Small always-on-top overlay that mirrors Claude Code's state, same idea as the
physical traffic light: grey = idle, amber = running, red = waiting for
confirmation, green = finished and ready for a new task. One row per session,
each labelled with its project folder, so it stays useful with more than one
Claude Code session running at once.

## How it works

- Claude Code hooks write each session's status to
  `%LOCALAPPDATA%\ClaudeStatusLight\sessions\<session-id>.json`
- The WinForms overlay polls that folder every 500ms and redraws a small
  draggable panel: one coloured dot + session label per active session. A
  session's row disappears when it ends (`SessionEnd` deletes its file), or
  after 15 minutes with no update if it crashed without firing that hook

## Setup

**1. Install the hooks (one-off)**

```
powershell -ExecutionPolicy Bypass -File hooks\install-hooks.ps1
```

This merges hook entries into `~\.claude\settings.json`, backing up any
existing file first. Safe to re-run.

**2. Build and install the overlay app**

Requires the .NET 8 SDK (`dotnet --version` to check).

```
powershell -ExecutionPolicy Bypass -File publish.ps1
```

This publishes the app and copies the exe to
`%LOCALAPPDATA%\Programs\ClaudeStatusLight\ClaudeStatusLight.exe`. Re-run it
after pulling changes to redeploy; it's self-contained, so nothing else needs
installing. If the app is currently running, exit it first (right-click the
dot -> Exit) so the exe isn't locked when the script overwrites it.

To build without installing, `dotnet publish -c Release` from
`ClaudeStatusLight\` produces the same exe under
`bin\Release\net8.0-windows\win-x64\publish\`.

**3. Run it**

Double-click `ClaudeStatusLight.exe` (in the install location above, or
wherever you copied it). A small panel appears top-right, showing "No active
session" while nothing's running. Drag it anywhere, its position is
remembered on restart. Right-click to exit.

**Run at login (optional):** press `Win+R`, type `shell:startup`, drop a
shortcut to the installed exe in there.

## Renaming sessions

By default a row is labelled with its project's folder name. To override
that, create `%LOCALAPPDATA%\ClaudeStatusLight\aliases.json` mapping folder
names to whatever label you want:

```json
{
  "Claude-Status-Traffic-Lights": "Status Light",
  "Portfolio": "Website"
}
```

Picked up within one poll tick (no restart needed) whenever the file
changes. Not created automatically; make the folder and file yourself if you
want it.

## Known limitation

Two sessions open in the same folder (e.g. two windows on the same project,
without git worktrees) get distinct rows disambiguated as `name (1)`,
`name (2)`, but there's no way to tell which row is which session beyond
that -- aliases.json renames by folder, not by individual session.

## Testing without Claude Code

Each call below writes to a "manual" session row, since there's no real
Claude Code hook payload behind it:

```
powershell -File hooks\set-status.ps1 waiting
powershell -File hooks\set-status.ps1 running
powershell -File hooks\set-status.ps1 done
powershell -File hooks\set-status.ps1 ended
```
