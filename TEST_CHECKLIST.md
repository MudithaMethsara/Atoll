# Atoll Verification & Test Checklist

## Automated Unit Tests (`Atoll.Tests`)
Run `dotnet test Atoll.sln -c Release`:
- [x] `SpringSolverTests` — target convergence, mid-flight target reversal continuity, `Snap`.
- [x] `IslandStateMachineTests` — `Idle`/`Compact`/`Minimal` transitions, `Alert` auto-collapse, swipe dismiss, `Pause`.
- [x] `ActivityManagerTests` — priority sorting, `CycleNext`/`CyclePrevious`, `RemoveByModule`.
- [x] `DominantColorTests` — dominant color extraction, `ImageCacheService` LRU caching & `<=128px` downscaling.
- [x] `SettingsServiceTests` — JSON persistence, clamping, corrupt-file recovery.

## Feature & Runtime Verification Checklist
- [ ] **Window & Hit-Testing**: Verify pill sits centered ~8px from top, never steals focus, and clicks/drags pass through transparent areas around the pill.
- [ ] **Notch Mode & Multi-Monitor**: Toggle Notch Mode (0px top) and switch monitors/DPI in Settings; verify smooth repositioning.
- [ ] **Fullscreen Auto-Hide**: Open a fullscreen video or game; verify island hides (or shrinks if configured) and restores on exit.
- [ ] **Spring Physics & Gestures**: Verify hover bounce, press squish, click/long-press expand, swipe left/right between activities, and scroll wheel volume adjustment.
- [ ] **Media Player**: Play music/video in Spotify, Chrome/Edge, or VLC; verify album art, dominant glow, 5-bar visualizer, seek scrubbing, and Play/Pause/Next/Prev.
- [ ] **Timers & Stopwatch**: Start `1m`/`5m` timer and `Stopwatch` from Expanded Quick Actions; verify compact countdown ring and expiry alert.
- [ ] **Minimal Split Bubble**: Run Media + Timer simultaneously; verify island splits into primary pill + detached bubble.
- [ ] **Calls**: Join a Teams/Zoom/Discord call; verify live timer, Mute toggle, and Window focus.
- [ ] **Notifications**: Trigger a Windows toast notification; verify peek alert & swipe-to-dismiss (note: unpackaged OS policy may require MSIX packaging for `UserNotificationListener`).
- [ ] **Battery & Bluetooth**: Plug/unplug laptop charger and connect Bluetooth headphones; verify plug-in/BT connect alerts.
- [ ] **Volume & Brightness HUD**: Press hardware volume/brightness keys or scroll over pill; verify interactive island slider.
- [ ] **Privacy Indicators**: Open Camera app or Voice Recorder; verify green camera dot / orange mic dot on Idle & Compact.
- [ ] **File Shelf**: Drag files onto the pill, release to stash, then drag an item back out into Explorer.
- [ ] **Quick Actions & Glance**: Test Clipboard History copy, Screenshot (`ms-screenclip:`), DND toggle, and optional Weather & Clock Glance.
- [ ] **Low Power Mode & Idle Targets**: Enable Diagnostics Overlay (`Tray -> Diagnostics Overlay`), verify `<1% CPU` and `<50 MB RAM` at idle, and `0` FPS when settled.

