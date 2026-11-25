# My Task Manager

A small Windows app for today's and tomorrow's tasks: reminders that pop up by the clock, a sticky note on the desktop, repeating tasks, checklists, history with remarks, archive, and dark mode.

## Install

1. Download **MyTaskManager.exe** from the [latest release](https://github.com/trmiizi/my-task-manager/releases/latest).
2. Put it somewhere permanent, e.g. `Documents\MyTaskManager\`, and double-click it.
   If Windows shows "Windows protected your PC", click **More info > Run anyway**.

The app checks for new versions once a day and installs them with one click (or **Settings > Check for updates**). Tasks are stored in `%APPDATA%\MyTaskManager` and are kept across updates.

See [dist/README.txt](dist/README.txt) for how to use it.

## Build

Needs only Windows (.NET Framework 4.8 and its built-in C# compiler):

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

Checks: `dist\MyTaskManager.exe --selftest report.txt` and `--screenshot <folder>`.

## Release a new version

```
powershell -ExecutionPolicy Bypass -File release.ps1 -Version 2.2.0 -Notes "What changed"
```

Builds, runs the self-test, tags, pushes and publishes a GitHub Release with the .exe, which installed copies pick up as an update.
