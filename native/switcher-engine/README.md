# switcher-engine (native libobs video engine)

`switcher-engine.dll` is the native映像エンジン behind the managed `Switcher.Engine` P/Invoke layer
(`src/Switcher.Engine`). It links **libobs** (the OBS Studio core) and implements the C ABI in
[`include/engine.h`](include/engine.h). Spec: [`docs/specs/libobs-engine-migration.md`](../../docs/specs/libobs-engine-migration.md).

> **GPLv2**: libobs is GPLv2, so this library is GPL. The GPL boundary is kept here — the managed side
> depends only on `IVideoEngine` (Switcher.Contracts) and the `engine.h`-shaped P/Invoke declarations.

> **Windows + OBS only**: libobs links and runs on a configured OBS host. This directory is **not** part
> of `HybridSwitcher.sln` and is built separately with CMake. The managed solution builds/tests without it
> (tests inject `FakeVideoEngine`).

## Prerequisites (pinned)

- **OBS Studio**: **`31.0.3`** (Windows). libobs ABI is not guaranteed stable across majors, so this
  version is fixed and **must match the runtime OBS install** (import lib ↔ `obs.dll`).
- CMake ≥ 3.24, a C++17 compiler (MSVC on Windows).
- libobs dev files. The validated path (see the L-002 notes in
  [`docs/tasks/agent-L-002-native-libobs-engine.md`](../../docs/tasks/agent-L-002-native-libobs-engine.md))
  is to build **just libobs** from the pinned obs-studio source tree:
  `cmake --build build_x64 --target libobs`, which yields `obs.lib` (import), `obs.dll`, and the
  generated header `obsconfig.h` under `build_x64/config/`. Public headers live under `<obs-studio>/libobs/`.
  Point CMake at **both** include roots (see method B below).

## Build

Use the helper [`build.bat`](build.bat) (it configures, builds, and copies the DLL next to
`Switcher.App.exe`), or run CMake directly:

```sh
# Method A: OBS dev files expose a libobs CMake package
cmake -S . -B build -DCMAKE_PREFIX_PATH="C:/obs-studio/build_x64/install"
cmake --build build --config Release

# Method B (validated): explicit headers + import lib. LIBOBS_INCLUDE_DIR must list BOTH the public
# headers AND the generated-config dir (which holds obsconfig.h), ';'-separated.
cmake -S . -B build -DSWITCHER_ENGINE_USE_FIND_PACKAGE=OFF ^
  -DLIBOBS_INCLUDE_DIR="C:/obs-studio/libobs;C:/obs-studio/build_x64/config" ^
  -DLIBOBS_LIB="C:/obs-studio/build_x64/libobs/Release/obs.lib"
cmake --build build --config Release
```

The output `switcher-engine.dll` must sit next to `Switcher.App.exe` (or on the DLL search path) so
`DllImport("switcher-engine")` resolves (`Switcher.App.csproj` copies it automatically after a native
build). At runtime `obs.dll`, its `data/` folder, the plugin modules, and `libobs-d3d11.dll` must all be
reachable — the simplest way is to put an installed OBS `bin\64bit` (matching version 31.0.3) on `PATH`.

## Runtime configuration (paths & startup)

`engine_startup(options_json)` receives the serialized `EngineOptions` (`canvas_width` / `canvas_height`
/ `fps` / `module_path`). libobs additionally needs its **core data path** and **plugin paths**, which
the managed `EngineOptions` does not yet carry (a follow-up task widens the contract — see the L-002
notes). Until then the native layer reads them from `options_json` if present, else from the environment:

| purpose | options_json key | environment variable | notes |
| --- | --- | --- | --- |
| **libobs core data** (built-in effects) | `data_path` | `SWITCHER_OBS_DATA_PATH` | `<obs>/data/libobs/`. **Required** — registered via `obs_add_data_path` *before* `obs_reset_video`; without it graphics init fails (`default.effect` not found). |
| plugin binaries | `module_bin_path` | `SWITCHER_OBS_MODULE_BIN` | `<obs>/obs-plugins/64bit/` |
| plugin data | `module_data_path` | `SWITCHER_OBS_MODULE_DATA` | `<obs>/data/obs-plugins/` |
| graphics backend | `graphics_module` | `SWITCHER_OBS_GRAPHICS_MODULE` | defaults to `libobs-d3d11` (shipping); `libobs-opengl` for non-Windows compile/dev |
| libobs log capture | — | `SWITCHER_ENGINE_LOG` | file path; installs a `base_set_log_handler` so startup failures are diagnosable |

Bundled modules loaded (a missing one does not stop startup): `win-dshow`, `obs-ffmpeg`, `image-source`,
`text`, and — for NDI — DistroAV (optional; NDI sinks/sources degrade gracefully when absent).

## Status

`engine.h` (ABI) and the scaffold are delivered by **agent-L-001**. The libobs internals are implemented
by **agent-L-002** in [`src/engine.cpp`](src/engine.cpp): options/data-path startup, shared source pool
with contract→OBS type mapping, dual fade-transition + `obs_view` wiring, scene/PiP composition, TAKE
(CUT/AUTO) with per-bus isolation, output routing (virtualcam/NDI), multiview nested-scene composition,
`obs_display` fullscreen, and throttled BGRA readback taps.

> **Not yet host-validated.** L-002 was authored on a Linux dev box where libobs/CMake are unavailable,
> so the DLL has **not** been compiled or run here. Building and the runtime completion criteria
> (webcam shared across ME1/ME2, CUT/AUTO isolation, output routing, sink-failure isolation) must be
> verified on the Windows + OBS 31.0.3 host. The managed solution (`dotnet build/test`) is unaffected and
> stays green (native is a separate build).

### Resolved technical items (spec §9)
- **Pinned OBS version**: **31.0.3** (Windows). Import lib must match the runtime `obs.dll`.
- **Dual virtual camera**: OBS ships a *single* virtual-camera output. **Decision: `VCAM1` → the OBS
  virtual camera; `VCAM2` → an NDI output** (`ndi_output`, DistroAV) so both program buses egress without
  a second camera driver. `HDMI` is presented separately via `engine_start_display` (App owns the HWND).
- **Bundled module set**: `win-dshow` (webcam→`dshow_input`), `obs-ffmpeg` (SRT/media→`ffmpeg_source`,
  `srt://` URLs), `image-source`, `text` (`text_gdiplus`), and DistroAV (NDI, optional). Paths per the
  *Runtime configuration* table above.
- **Multiview render method**: libobs-internal composition — a private `obs_scene` whose items are the
  per-bus program transition / preview scene / shared sources, positioned per region into a rows×cols
  grid, rendered to its own view/video (tap) or an `obs_display` (fullscreen).
- **P/Invoke granularity / threading**: synchronous calls guarded by a per-context recursive mutex;
  frame/state delivered via callbacks (buffers valid only for the callback duration).

### Known limitation
- **Per-item PiP opacity** (`PipSettings.opacity`) is not applied yet: a filter on the *shared* source
  would bleed across both buses. Geometry (pos/size/bounds), crop, z-order and visibility are honored;
  opacity awaits a per-bus source wrapper.
