# TopBar

Minimal Windows top bar (pure Win32 + C# / .NET 8, zero UI-framework dependencies) that shows your [Monkeytype](https://monkeytype.com) typing activity, a clock, and CPU/RAM usage. Registered as a Windows AppBar, so maximized apps always dock below it.

![preview](https://raw.githubusercontent.com/AROICE-HQ/monkeybar/master/monkeybar.png)

## Features

- Monkeytype daily activity boxes (1-7 days, 12 themes, grade/opacity color modes)
- Public profile fallback (streak only) when no ApeKey is set
- Center clock, CPU/RAM monitor (toggleable)
- AppBar docking, always-on-top, never steals focus
- Left-click the boxes: open monkeytype.com - Right-click the boxes: menu (day breakdown, refresh, settings, quit)
- Global hotkeys: `Win+Shift+R` refresh - `Win+Shift+M` open Monkeytype - `Win+Shift+P` profile - `Win+Shift+B` hide/show bar
- Settings UI (right-click boxes > Settings) persisted to `%APPDATA%\MonkeyBar\settings.json`
- Start with Windows, customizable bar height (24-64 px)

## Build

```bash
dotnet build -c Release
```

Run: `bin\Release\net8.0\win-x64\TopBar.exe`

## Publish

Self-contained single exe (~14 MB, no prerequisites - share this one):

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o publish-sc
```

Tiny exe (~0.2 MB, requires [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)):

```bash
dotnet publish -c Release -p:PublishSingleFile=true -o publish-fd
```

## Release workflow (production)

1. **CI** (`.github/workflows/ci.yml`) builds every push/PR to `main`.
2. **Release** (`.github/workflows/release.yml`) triggers on version tags: publishes both variants and attaches them to a GitHub Release with auto-generated notes.

Cut a release:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The exes appear under the release once the workflow finishes.

## Credits

Inspired by [MonkeyBar](https://github.com/AROICE-HQ/monkeybar) (GNOME Shell extension). Not affiliated with Monkeytype.
