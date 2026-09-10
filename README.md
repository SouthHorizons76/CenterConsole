# CenterConsole

A small Windows tray utility that bundles three things into one app:

1. **Global microphone mute** — mute/unmute a chosen capture device with a global hotkey that keeps working even while a fullscreen app has focus.
2. **Camera block** — completely disable a webcam at the driver level, so *no* application (Win32 or UWP/Store) can turn it on, no matter what permission it requests.
3. **Per-app volume control** — independent volume sliders for individual running applications (e.g. Spotify), plus global hotkeys to nudge the volume of a chosen "target" app up or down.

## Why does this need Administrator rights?

Windows' built-in camera privacy toggle (Settings → Privacy → Camera) only restricts **UWP/Store apps**. Desktop applications (Zoom, OBS, Teams, browsers, etc.) can bypass it entirely by talking to the camera directly through DirectShow or Media Foundation. The only way to *actually* guarantee a camera can't be used by anything is to disable the device itself — the same mechanism Device Manager's "Disable device" uses. That requires administrator privileges, so CenterConsole requests elevation (`requireAdministrator`) on launch and runs as a single elevated process.

If you only care about mic mute and per-app volume, you can ignore the camera block tab — but the whole process still runs elevated in this version (see [Known limitations](#known-limitations)).

## Features

- **Microphone**: pick any capture device, mute/unmute it, bind a global hotkey (default `Ctrl+Alt+M`). Stays in sync if muted externally via the Windows Sound Control Panel.
- **Camera Block**: lists all camera/imaging devices present on the system; toggle any of them off to disable the driver, toggle back on to re-enable. Optional "re-apply on startup" so a block persists across reboots.
- **Per-App Volume**: shows every application currently producing audio, with an independent 0–100% slider and mute checkbox each. Pick one app as the hotkey "target" and use the global volume-up/down hotkeys (default `Ctrl+Alt+Up` / `Ctrl+Alt+Down`) to adjust just that app.
- Runs from the system tray — no taskbar window unless you open Settings.

## Requirements

- Windows 10 (1809+) or Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or build from source with the .NET 8 SDK)
- Administrator rights (see above)

## Build

```powershell
dotnet build CenterConsole.sln
```

Run tests:

```powershell
dotnet test tests\CenterConsole.Tests\CenterConsole.Tests.csproj
```

Publish a self-contained executable:

```powershell
dotnet publish src\CenterConsole.App\CenterConsole.App.csproj -c Release -r win-x64 --self-contained
```

## Default hotkeys

| Action | Default |
|---|---|
| Mute/unmute microphone | `Ctrl + Alt + M` |
| Target app volume up | `Ctrl + Alt + Up` |
| Target app volume down | `Ctrl + Alt + Down` |

All hotkeys are rebindable from the Settings window (click the current binding, then press the new combination).

## Known limitations

- The whole app runs elevated for simplicity, even though only the camera-block feature strictly needs it. A future version could split this into an unelevated tray process plus a small elevated helper.
- Per-app volume only tracks the *default* audio output device — apps playing through a non-default device won't appear.
- Some USB webcams expose their microphone and camera as one composite USB device; blocking the camera on such hardware could theoretically also disable the bundled mic. Verify with your specific hardware before relying on this for privacy-critical use.
- Global hotkeys use `RegisterHotKey`, which a small number of exclusive-fullscreen DirectX games can block. If you hit this, please open an issue with the game name.

## Credits

- [NAudio](https://github.com/naudio/NAudio) — Core Audio API access (mute, per-app session volume).
- [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon) — system tray icon.
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM helpers.
- Per-app volume control design informed by [VolumeControl](https://github.com/radj307/volume-control) by radj307.

## License

[MIT](LICENSE)
