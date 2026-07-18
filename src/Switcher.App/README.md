# Switcher.App

The WPF host app (`net9.0-windows`) that wires every module together into one resident process
(docs/tasks/agent-A-004-app-integration.md, docs/specs/pc-switcher-app.md).

> 親プロジェクト: [`../../README.md`](../../README.md) ／ 仕様書: [`docs/specs/pc-switcher-app.md`](../../docs/specs/pc-switcher-app.md)

## Composition

`Composition/ServiceCollectionExtensions.AddSwitcherApp` is the DI composition root. It registers:

| Contract | Concrete | Notes |
| --- | --- | --- |
| `IInputSourceManager` | `Switcher.Media.InputSourceManager` | also registered under its concrete type, since `CompositorEngine`'s public constructor needs it |
| `ICompositorEngine` | `Switcher.Media.CompositorEngine` | built from the same `InputSourceManager` singleton |
| `ITallyBroadcaster` | `Switcher.Web.TallyBroadcaster` | UDP broadcast, `255.255.255.255:9999` |
| `IAtemController` | `Switcher.Atem.AtemController` | also registered under its concrete type, so the UI can read `ConnectionStateChanged`/`State`, which aren't on the interface |
| `IVirtualCameraOutput` | `Switcher.VirtualCam.VirtualCameraOutput` | |
| `ISwitcherConfigService` / `IControllerInputSink` | `Orchestration.AppOrchestrator` | the "中核ファサード/オーケストレーター": delegates Web's config/sources calls to Media, routes controller button events to either the compositor (TAKE/PiP) or the ATEM client, and publishes tally state on every PGM/PVW change |
| `Switcher.Web.WebHost` | - | constructed directly with the three interfaces above, port from `AppConfig.WebPort` (default 8080) |

`Services/AppHostService` owns the startup/shutdown *order* (connect ATEM -> start web host -> start
frame pump; reverse on shutdown). `App.xaml.cs` owns final disposal: disposing the built
`ServiceProvider` cascades into every registered `IDisposable`/`IAsyncDisposable` singleton
(`InputSourceManager`, `CompositorEngine`, `AtemController`, `VirtualCameraOutput`,
`TallyBroadcaster`, `WebHost`).

Configuration (`Configuration/AppConfig.cs`) - ATEM IP, web port, projector display index, and the two
button-routing tables - loads from `appsettings.json` next to the executable, falling back to
built-in defaults if the file is missing or invalid.

### Controller button routing

`AppOrchestrator.Enqueue` first checks `AppConfig.CompositeButtonMappings` (App-owned:
`Take` / `TogglePip`); anything not matched there is relayed to `IAtemController.SendCommand`, which
itself no-ops if it has no mapping for that button or isn't connected. This matches
docs/specs/pc-switcher-app.md §2.6 ("メイン側コントローラーの特定ボタン入力を ATEM コマンドに変換").
Both tables are just example defaults in `appsettings.json` - real operator bindings are configuration,
not code.

## Known integration gaps (flagged for review)

- **Physical full-screen output**: `Switcher.VirtualCam.Display.Direct3DSwapChainOutput` (the
  `ISwapChainOutput` implementation meant for HDMI/Type-C full-screen output) is `internal` to
  `Switcher.VirtualCam` with no public factory, so App cannot construct it from outside that
  assembly. `ProjectorWindow` instead renders the PGM frame itself via WPF
  (`Rendering/FrameBitmapWriter` + `WriteableBitmap`) on a borderless, topmost window pinned to
  `AppConfig.ProjectorDisplayIndex`. This satisfies the "サブオペレーター向けソースプロジェクター（全画面）"
  requirement, just via WPF compositing rather than a dedicated D3D swap chain - worth reconciling
  with `agent-B-002` if a public factory should be added there instead.
- **Per-channel multiview thumbnails**: `IInputSourceManager` exposes source status
  (name/protocol/resolution/connection state) but not a per-channel raw frame - only
  `ICompositorEngine.GetProgramFrame()/GetPreviewFrame()` (the composited canvases) are public. The
  multiview's input tiles therefore show status, not live per-channel video; PGM/PVW panels are the
  only live video shown, since those are the only frames the public contracts expose.

## Runtime prerequisites

Builds on any platform (this repo's dev sandbox is Linux, via
`EnableWindowsTargeting`/`UseWindowsForms`), but **running** the full app requires:

- **Windows 10/11**, since it hosts a WPF UI, `System.Windows.Forms.NotifyIcon`/`Screen`, and the
  Direct3D 11-backed compositor/virtual camera.
- Everything listed in `Switcher.Media/README.md` (GStreamer 1.18+ with `mfvideosrc`/`ksvideosrc`,
  the NDI plugin, `srtsrc`) and `Switcher.VirtualCam/README.md` (the native DirectShow virtual-camera
  filter registered via `regsvr32`).
- An ATEM Mini (or something answering on UDP 9910) if you want to see `AtemController` actually
  reach `Connected`; without one, the app still runs fine - it just retries the handshake and drops
  button events routed there.

## Manual E2E verification

Automated coverage stops at the module boundary (see each module's own test project); the full
wired-together app is meant to be exercised by hand, per
docs/tasks/agent-A-004-app-integration.md's completion criteria. On a Windows machine with the
prerequisites above:

1. **Startup**: run `Switcher.App.exe` (or `dotnet run --project src/Switcher.App`). Expect:
   - The main window opens (dark multiview layout, empty source list, PGM/PVW panels black).
   - A tray icon appears; closing the main window hides it instead of exiting (use the tray menu's
     "Exit" to actually quit).
   - Debug output shows the ATEM connect attempt and the web host starting on port 8080.
2. **`GET /api/v1/sources`**: `curl http://localhost:8080/api/v1/sources` should return `[]` before
   any source is configured.
3. **Add a dummy source**: `POST /api/v1/config` with a UVC source, e.g.:
   ```bash
   curl -X POST http://localhost:8080/api/v1/config -H "Content-Type: application/json" -d '{
     "target_channel": 1,
     "source_type": "UVC",
     "source_url": null,
     "pip_settings": { "enabled": true, "x_position": 0, "y_position": 0, "width": 1920, "height": 1080, "opacity": 1.0 }
   }'
   ```
   Expect a `204`, then `GET /api/v1/sources` shows channel 1, and its tile appears in the main
   window's source list (status updates as the underlying GStreamer pipeline connects/errors).
4. **Controller input -> switch**: connect to `ws://localhost:8080/ws` (e.g. with `websocat` or a
   browser console) and send:
   ```json
   {"event": "button_press", "data": {"controller_id": "main", "button_id": 0, "timestamp": 0}}
   ```
   Button `0` is mapped to TAKE by default (`appsettings.json`) - the PROGRAM panel should update to
   mirror whatever is in PREVIEW, and the status bar's PGM/PVW channel lists should update.
5. **Tally UDP receive**: listen for the broadcast while doing step 4, e.g.
   `nc -ul 9999` (or a small UDP listener) should print a JSON payload like
   `{"active_pgm":[1],"active_pvw":[1]}` immediately on the TAKE, and again every ~250ms afterward.
6. **Shutdown**: use the tray menu's "Exit". Expect the web host, ATEM client, GStreamer pipelines,
   and virtual camera to all stop cleanly (no exceptions in the debug output, process exits).
