# Atoll — Native Dynamic Island for Windows 10/11

**Atoll** is a lightweight, native C# / .NET 8 WPF "Dynamic Island" overlay for Windows 10 (2004+) and Windows 11. It renders a borderless, topmost, per-monitor DPI-aware, click-through black squircle capsule at the top edge of your screen that morphs organically with interruptible spring physics while dropping to 0% CPU when idle.

---

## Key Features

- **Authentic Window Behavior**:
  - Borderless, transparent, topmost, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (no Alt+Tab entry, never steals focus).
  - Dynamic `WM_NCHITTEST` hit-testing: clicks and scroll events pass cleanly through everywhere outside the pill and detached bubble bounds.
  - Per-Monitor DPI v2 aware, multi-monitor selector, top offset adjustment, and **Notch Mode** (attached directly to the top edge).
  - Event-driven fullscreen auto-hide or shrink (`SetWinEventHook` + `SHQueryUserNotificationState`).
- **Organic Spring Physics Engine**:
  - Coupled damped harmonic oscillators (`SpringSolver`, damping ratio ~0.70–0.85) with interruptible mid-flight reversals.
  - Continuous squircle geometry (`IslandGeometry.CreateSquircle`), hover scale bounce, press squish, long-press expand, swipe left/right activity switching & dismiss, and mouse-wheel volume control.
  - Subscribes to `CompositionTarget.Rendering` **only** while springs are active and unsubscribes on settle for zero idle CPU.
- **5 iOS-Style States**: `Idle`, `Compact`, `Minimal` (with detached split bubble), `Expanded`, and `Alert/Peek`.
- **12 Built-in Modules & Live Activities API (`IIslandActivity`)**:
  1. **Media Player**: GSMTC integration (Spotify, Chrome/Edge, Apple Music, VLC, etc.), downscaled cached album art, dominant-color ambient glow, scrubbing slider, play/pause/prev/next, and 5-bar WASAPI loopback audio visualizer.
  2. **Timers & Stopwatch**: Quick-start presets (`1m`, `5m`, `10m`, `25m`, `Stopwatch`), circular progress ring, completion alert.
  3. **Call & Meeting Indicator**: Detects Teams, Zoom, Discord, Slack, and Webex calls via CoreAudio communications sessions & window titles with live timer, Mute toggle, and Window focus.
  4. **System Notifications**: `UserNotificationListener` integration with app icon, title, body, and swipe-to-dismiss (gracefully degrades if OS policy restricts unpackaged access; MSIX manifest included).
  5. **Battery & Charging**: Power setting notifications (`RegisterPowerSettingNotification`) for plug-in animation, low-battery alert, and compact percentage ring.
  6. **Bluetooth Device Connect**: `DeviceInformation.CreateWatcher` + PnP battery property (`DEVPKEY_Bluetooth_Battery`) connect popup.
  7. **Volume & Brightness HUD**: CoreAudio endpoint notifications + WMI/DDC-CI brightness with interactive island slider.
  8. **Privacy Indicators**: Event-driven `RegNotifyChangeKeyValue` watcher on `CapabilityAccessManager\ConsentStore` for green camera, orange microphone, and red screen-recording indicators.
  9. **Calendar Countdown**: WinRT `AppointmentManager` upcoming event countdown.
  10. **File Shelf**: Drag files onto the island to stash them temporarily, then drag back out to Explorer/apps.
  11. **Quick Actions**: Clipboard history quick-copy (`Clipboard.GetHistoryItemsAsync`), Screenshot (`ms-screenclip:`), and Focus Assist / Do Not Disturb toggle.
  12. **Weather & Clock Glance**: Optional idle-expanded clock and cached Open-Meteo weather (off by default).
- **Settings UI, Tray Icon, Hotkeys & Diagnostics**:
  - Native Win32 `Shell_NotifyIcon` tray menu (`Toggle Island`, `Settings...`, `Pause Island`, `Diagnostics Overlay`, `Exit`).
  - Global hotkey (`Ctrl+Alt+Space` by default, interactive shortcut capture), Low Power Mode (30fps cap, no glow/visualizer), and two-pane dark Settings window with live accent presets and Island preview triggers.

---

## Build, Test & Publish

### Prerequisites
- Windows 10 (build 19041+) or Windows 11, x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optional: [Inno Setup 6](https://jrsoftware.org/isinfo.php) to compile `installer/Atoll.iss`

### Build & Run Tests
```powershell
dotnet build Atoll.sln -c Release
dotnet test Atoll.sln -c Release --no-build --verbosity normal
```

### Run Locally
```powershell
dotnet run --project src\Atoll\Atoll.csproj -c Release
```

### Publish Self-Contained Single-File ReadyToRun Trimmed `.exe` + Installer
```powershell
.\scripts\publish.ps1
```
Outputs:
- Portable executable: `dist\win-x64\Atoll.exe`
- Windows installer (when Inno Setup 6 is installed): `dist\installer\AtollSetup-1.0.0-x64.exe`
- MSIX manifest template (for Store/sideloaded notification identity): `installer\Package.appxmanifest`

### Idle Memory & CPU Soak Check
```powershell
.\scripts\soak-check.ps1 -DurationSeconds 30
```

