# OpenCrosshair

A free, open-source crosshair overlay for Windows. It draws a custom crosshair in the middle of your screen, on top
of any game running in **borderless/windowed** mode. No ads, no telemetry, no network access.

## Features

- **Designer:** arm length, thickness, gap, T-style, center dot, circle, outline, color and opacity, with a live zoomed preview
- **Unlimited crosshairs:** save as many as you like and switch from the tray menu or with a hotkey
- **Share codes:** copy a short code like `OC1:00FF00,100,6,2,3,0,0,0,1,1,1,000000` and import other people's
- **PNG export** of any crosshair
- **Multi-monitor:** pick the display; nudge the crosshair pixel by pixel, remembered per display
- **Hide while aiming:** optionally hides the crosshair while the right mouse button is held
- **Global hotkeys** (customizable), **Start with Windows**, tray icon
- Click-through and never takes focus, so it can't get in the way of your game

## Install

Download `OpenCrosshair.exe` from [Releases](../../releases) and run it. It's a single self-contained file, so
nothing else needs installing. The app lives in the system tray: right-click the icon for options, double-click
for the designer.

## Default hotkeys

| Keys | Action |
|---|---|
| `Ctrl+Alt+H` | Show / hide the crosshair |
| `Ctrl+Alt+N` | Next saved crosshair |
| `Ctrl+Alt+D` | Open the designer |
| `Ctrl+Alt+Arrow keys` | Move the crosshair 1 px |
| `Ctrl+Alt+R` | Reset position to the exact center |

Hotkeys use Windows' `RegisterHotKey`, so the app only ever sees these combinations, never your other keystrokes.
To change them, edit the `Hotkeys` section of `%APPDATA%\OpenCrosshair\settings.json` (e.g. `"Toggle": "Shift+F1"`)
and restart the app.

## Settings

Everything is stored in `%APPDATA%\OpenCrosshair\settings.json`. If that file is ever unreadable, it's kept as
`settings.json.bad` and the app starts with defaults instead of overwriting it.

## Limitations

- **Exclusive fullscreen:** Windows doesn't let normal windows draw over exclusive-fullscreen games. Switch the game
  to *borderless* or *windowed fullscreen*.
- **Anti-cheat and game rules:** OpenCrosshair only draws a transparent window on top of your screen. It doesn't read
  or modify game memory or inject anything into games. Some games or tournaments still don't allow crosshair
  overlays, so check the rules of what you play. Use at your own risk.
- English only.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```bash
dotnet test
dotnet run --project src/OpenCrosshair
dotnet publish src/OpenCrosshair -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## License

[MIT](LICENSE). OpenCrosshair is an independent project and isn't affiliated with any other crosshair app.
