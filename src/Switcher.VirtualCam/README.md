# Switcher.VirtualCam

Virtual camera output (single and dual) and full-screen physical display output implementing
`IVirtualCameraOutput` (`Switcher.Contracts`) and the `ISwapChainOutput` defined in this project, plus
`OutputRouter`, which resolves `PUT /api/v1/outputs` assignments (docs/specs/00-system-overview.md
§4.2) into a routing table and fans composited frames out to whichever sinks are assigned.

> 親プロジェクト: [`../../README.md`](../../README.md) ／ 仕様書: [`docs/specs/pc-switcher-app.md`](../../docs/specs/pc-switcher-app.md)

## Virtual camera device

`VirtualCameraOutput` converts each composited `FrameData` (BGRA32, matching
`Switcher.Media.Compositing.DirectX11Compositor`'s render-target format) to NV12
(`FrameConversion/Nv12FrameConverter.cs`) and hands it to `Devices/IVirtualCameraDevice`. The device
is opened lazily on the first frame after `Start()` (once the frame resolution is known) and
re-opened if the resolution changes mid-stream.

The real implementation, `Devices/SharedMemoryVirtualCameraDevice`, publishes frames to a companion
**native DirectShow source filter** over a named shared-memory region (`Local\HybridSwitcherVCamFrame`
by default) plus a named event (`Local\HybridSwitcherVCamFrameReady` by default) that signals a new
frame is ready. The wire format is: a `width:int32, height:int32` header followed by the NV12 payload
(`Nv12FrameConverter.GetRequiredBufferSize`). Both names are constructor parameters (rather than fixed
constants) so two devices can coexist without colliding — see "Two virtual cameras" below.

## Two virtual cameras

`DualVirtualCameraOutput` (`IDualVirtualCameraOutput`) manages two independent `VirtualCameraOutput`
instances — one per `OutputSink.Vcam1`/`Vcam2` (`Switcher.Contracts`) — each backed by its own
`SharedMemoryVirtualCameraDevice` opened under a distinct shared-memory/event name
(`..._VCAM1`/`..._VCAM2`), so the native DirectShow filter must likewise be registered twice, once per
name, to expose two separate OS camera devices (see "Virtual camera device" above; that native
registration is outside this .NET project). The two sinks are independent: a failure opening or
pushing to one device does not affect the other.

## Output routing

`OutputRouter.ApplyOutputs(OutputsRequest)` validates and stores `OutputAssignment`s (sink → source,
plus `display_id`/`hide_cursor`/`fullscreen` for HDMI), defaulting to PGM1→VCAM1 / PGM2→VCAM2 with
HDMI unassigned until a `display_id` is provided. `OutputRouter.RouteFrame(source, frame)` then fans a
composited frame out to every sink currently assigned to that source (`IDualVirtualCameraOutput` for
VCAM1/VCAM2, `IHdmiFullscreenOutput` for HDMI); one sink failing does not stop the others, and any
failures are surfaced together as an `AggregateException` once every sink has been attempted. Pulling
frames from `ICompositorEngine.GetProgramFrame(bus)` and calling `RouteFrame` per tick, and attaching
`IHdmiFullscreenOutput` to a real window/HWND, are App integration's job (agent-A2-006).

That native filter is **not part of this .NET project** — a DirectShow source filter must be a COM
DLL, which this managed assembly cannot implement directly. Two options, in order of preference:

1. **Reuse an existing OSS DirectShow virtual camera filter** (e.g. adapt
   [OBS Studio's `win-dshow`/virtualcam plugin](https://github.com/obsproject/obs-studio/tree/master/plugins/win-dshow)
   or a similar wrapper) to read frames from the shared-memory contract above instead of its own.
2. Build a minimal custom filter from the same shared-memory contract.

Either way, installing the filter is a one-time, separate native deployment step:

- Register the filter DLL with `regsvr32 <filter>.dll` (requires an elevated/administrator prompt —
  it writes to `HKEY_CLASSES_ROOT\CLSID\...` and the DirectShow video capture sources category).
  Uninstall with `regsvr32 /u <filter>.dll`.
- The filter should register under its own distinct CLSID/friendly name so it does not collide with
  a real OBS Virtual Camera install, and so it doesn't compete with future recording/streaming
  outputs that may also want to consume the same composited frames.
- No admin rights are required to *run* `Switcher.VirtualCam` itself (writing to the named shared
  memory/event does not need elevation); admin is only needed once, for the filter's registry
  registration.

## Physical display output

`Display/ISwapChainOutput` is a thin interface — App integration owns the native window/HWND and
decides which monitor a given instance is attached to (via `AttachToDisplay(displayIndex, hwnd)`);
this project only owns the presentation surface. `Display/Direct3DSwapChainOutput` is the DXGI/
Direct3D 11 implementation: it creates an exclusive full-screen swap chain on the target
`IDXGIOutput`, uploads each `FrameData` into a dynamic texture (one copy, no intermediate staging —
see `UploadFrame`), and stretches it over a full-viewport quad with no blending (this is a single
full-frame present, not a layered composite like `DirectX11Compositor`).

`Display/IHdmiFullscreenOutput` (default impl `Display/HdmiFullscreenOutput`) wraps an
`ISwapChainOutput` with the HDMI sink's `hide_cursor`/`fullscreen` policy
(docs/specs/00-system-overview.md §4.2): `Attach(displayId, windowHandle, hideCursor, fullscreen)`
attaches the swap chain and applies the cursor policy via `Display/ICursorVisibility` (default impl
`Display/Win32CursorVisibility`, a thin `user32.dll` `ShowCursor` wrapper); `Detach`/`Dispose` always
restore the cursor. As with `ISwapChainOutput`, App integration still owns the HWND/window placement —
this type only owns presentation and the cursor policy.

## Runtime prerequisites

This project **builds** on any platform/CI without additional native dependencies, but **running**
it requires:

- **Windows 10/11** with a DirectX 11-capable GPU/driver — `SharedMemoryVirtualCameraDevice`,
  `Direct3DSwapChainOutput`, and `Win32CursorVisibility` all throw `PlatformNotSupportedException` on
  any other OS. On a machine without a Direct3D 11 runtime (e.g. this Linux dev sandbox, or CI), the
  code still compiles; only `Open`/`AttachToDisplay`/`Hide`/`Show` (i.e. actually talking to the
  OS/GPU) require the runtime.
- The native DirectShow source filter described above, registered via `regsvr32` (admin rights,
  one-time).

Without these, `dotnet build` / `dotnet test` succeed (both native-dependent components are
abstracted behind `IVirtualCameraDevice` / `ISwapChainOutput`, and unit tests substitute a fake for
the device), but actually starting the virtual camera or attaching to a display will fail at the
point they try to talk to the missing native runtime/filter.
