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
/ `fps` / `module_path`). libobs additionally needs its **core data path** and **plugin paths**. The
managed `EngineOptions` now carries these (`data_path` / `module_bin_path` / `module_data_path` /
`graphics_module`); `Switcher.App` populates them by locating the installed OBS runtime (see
`Switcher.App/Configuration/ObsRuntime.cs`). The native layer reads each key from `options_json` if
present, else falls back to the environment:

| purpose | options_json key | environment variable | notes |
| --- | --- | --- | --- |
| **libobs core data** (built-in effects) | `data_path` | `SWITCHER_OBS_DATA_PATH` | `<obs>/data/libobs`. **Required** — registered via `obs_add_data_path` *before* `obs_reset_video`; without it graphics init fails (`default.effect` not found). The engine appends a trailing separator itself, since libobs' `check_path` concatenates path+file with no separator (a path lacking one would search `…/libobsdefault.effect`). |
| plugin binaries | `module_bin_path` | `SWITCHER_OBS_MODULE_BIN` | `<obs>/obs-plugins/64bit/` |
| plugin data | `module_data_path` | `SWITCHER_OBS_MODULE_DATA` | `<obs>/data/obs-plugins/` |
| graphics backend | `graphics_module` | `SWITCHER_OBS_GRAPHICS_MODULE` | defaults to `libobs-d3d11` (shipping); `libobs-opengl` for non-Windows compile/dev |
| libobs log capture | — | `SWITCHER_ENGINE_LOG` | file path; installs a `base_set_log_handler` so startup failures are diagnosable |

Modules are loaded from `module_bin_path` by an **explicit allow-list of headless-safe plugins**
(`win-dshow`, `win-capture`, `win-wasapi`, `obs-ffmpeg`, `image-source`, `obs-text`, `text-freetype2`,
`obs-transitions`, `obs-filters`, `vlc-video`, `obs-x264`, `obs-outputs`, `rtmp-services`, plus optional
`distroav`/`obs-ndi` for NDI). A missing module does not stop startup. The allow-list is deliberate:
`obs_load_all_modules()` over a full OBS install would also load Qt/frontend plugins (`frontend-tools`,
`*-output-ui`, `obs-websocket`, `obs-browser`) whose `module_load` builds Qt widgets and aborts the host
process ("Must construct a QApplication before a QWidget"). `obs-transitions` is required — the per-bus
M/E TAKE uses the `fade_transition` source it registers.

## Status

`engine.h` (ABI) and the scaffold are delivered by **agent-L-001**. The libobs internals are implemented
by **agent-L-002** in [`src/engine.cpp`](src/engine.cpp): options/data-path startup, shared source pool
with contract→OBS type mapping, dual fade-transition + `obs_view` wiring, scene/PiP composition, TAKE
(CUT/AUTO) with per-bus isolation, output routing (virtualcam/NDI), multiview nested-scene composition,
`obs_display` fullscreen, and throttled BGRA readback taps.

> **Host-validated startup + readback (2026-07-22).** Built with VS 2022 + the in-tree `obs-studio`
> libobs and run against an installed **OBS Studio 32.0.4**: `engine_startup` boots libobs (D3D11 +
> effects), loads the source-module allow-list, wires both M/E buses' `fade_transition`, enumerates
> webcam devices, opens a webcam (`dshow_input`), and delivers live BGRA frames for both `PGM1` and
> `SRC:<id>` taps, then shuts down cleanly. Fixes made here: the data-path trailing separator, the Qt/UI
> plugin crash (allow-list), and the tap readback (see below). NDI needs DistroAV installed. Remaining
> completion criteria (webcam shared across ME1/ME2, CUT/AUTO isolation, output routing) still need
> exercising through the App UI. The managed solution (`dotnet build/test`) stays green.

### Preview readback (taps)

`engine_set_tap(target, true)` makes a target's pixels available to the managed frame callback. It does
**not** use `obs_view`/`video_output_connect`: a custom view's mix is never marked raw-active (only the
un-exported `start_raw_video` does that), so that path yields no frames. Instead a single
`obs_add_main_render_callback` renders each active tap's source (via `resolve_target_source`) into a
per-tap `gs_texrender`, stages it to a `gs_stagesurface`, and maps it one frame later to hand BGRA to the
callback. `ctx->taps` is mutated only under `obs_enter_graphics()`. Webcam/NDI (`dshow_input`/`ndi_source`
are async) get `obs_source_inc_showing` while tapped so their device actually captures.

### TAKE and the scene swap

`engine_take` swaps the bus's `program_scene`/`preview_scene` pointers, so anything that resolved a bus
scene to an `obs_source_t` *once* has to follow the swap or it silently renders the wrong bus half.
`rebind_bus_targets_locked` re-points that bus's `PVW*` tap and any `PVW*`/`PGM*` `obs_display`, and the
layout cached in `ctx->last_multiview_json` is re-applied so multiview regions whose content token is
`PVW1`/`PVW2` pick up the new preview scene. Without it, PVW readback shows the composition that was just
promoted to program.

### Webcam capture mode

`WebcamConfig.Format` accepts `"<W>x<H>"` or `"<W>x<H>@<FPS>"` and is translated to win-dshow's
`res_type` = Custom(1) + `resolution` + `frame_interval` (100 ns units). Setting only `resolution` has no
effect — with the default `res_type` = Preferred(0), win-dshow opens the device's first advertised media
type, which on many UVC cameras is a very low frame rate (a BUFFALO BSWHD06M opens at 1280x720 **@8 fps**).
An absent format explicitly selects Preferred.

### NDI (no DistroAV required)

NDI input is implemented in `src/ndi.cpp` directly against the **NDI SDK**, so the DistroAV / obs-ndi
plugin is not needed. Only the SDK *headers* are a build input — the runtime is resolved with
`LoadLibrary` at startup via `Processing.NDI.DynamicLoad.h` (`NDIlib_v5_load`), located through
`NDI_RUNTIME_DIR_V6`/`V5`/`V4` — so a DLL built on a machine with the SDK still runs on one without NDI.
CMake sets `SWITCHER_HAS_NDI` when it finds the headers; without them `ndi.cpp` compiles to stubs.

- **Source**: `obs_register_source("switcher_ndi")`, an async video source. One receive thread per source
  calls `recv_capture_v2` and pushes frames through `obs_source_output_video`. The receiver requests
  `NDIlib_recv_color_format_BGRX_BGRA`, so frames land as `VIDEO_FORMAT_BGRA/BGRX` with no colour
  conversion here. A sender that is absent or appears later is retried, not failed.
- **Discovery**: `engine_enumerate_devices("NDI")` uses the SDK finder instead of an obs property list.
  A source's `id` *is* its NDI name (`MACHINE (Source)`), which is what `recv_create_v3` connects by.
- **Fallback**: `map_source_type("NDI")` prefers `switcher_ndi`, falling back to DistroAV's `ndi_source`
  if this build lacks NDI support and that plugin happens to be installed.
- **Not yet native**: NDI *output* still goes through DistroAV's `ndi_output` (so the NDI1/NDI2/VCAM2
  sinks need that plugin). Sending would reuse the tap readback plus `NDIlib_send_send_video_async_v2`.

Verified against NDI 6 Runtime + NDI Tools "Test Patterns" as the sender.

### Mix sources

`engine_add_source(type = "MIX")` creates a private `obs_scene` and registers it in the shared source
pool under the mix's id. Because a scene *is* an `obs_source`, a mix needs no special handling anywhere
else — it can be mounted on a bus, tapped for preview, or placed in a multiview cell like a camera.
Layers are looked up in the same pool, so a source can be inside a mix and on a bus simultaneously.
Self-reference is skipped explicitly; libobs rejects deeper cycles itself.

### Resolved technical items (spec §9)
- **Pinned OBS version**: **32.0.4** (Windows), validated. Import lib must match the runtime `obs.dll`.
- **Dual virtual camera**: OBS ships a *single* virtual-camera output. **Decision: `VCAM1` → the OBS
  virtual camera; `VCAM2` → an NDI output** (`ndi_output`, DistroAV) so both program buses egress without
  a second camera driver. `HDMI` is presented separately via `engine_start_display` (App owns the HWND).
- **Bundled module set**: `win-dshow` (webcam→`dshow_input`), `obs-ffmpeg` (SRT/media→`ffmpeg_source`,
  `srt://` URLs), `image-source` (IMAGE→`image_source`), `obs-browser` (HTML→`browser_source`),
  `text` (`text_gdiplus`), and DistroAV (NDI, optional). Paths per the *Runtime configuration* table
  above. `obs-browser` is safe to load in this headless host: `ENABLE_BROWSER_QT_LOOP` is a macOS-only
  build flag, so on Windows `obs_module_load` only registers the source type (CEF spins up lazily on the
  plugin's own manager thread when the first `browser_source` is created) — verified loading against
  OBS 32.0.4 / CEF 127.
- **Multiview render method**: libobs-internal composition — a private `obs_scene` whose items are the
  per-bus program transition / preview scene / shared sources, positioned per region into a rows×cols
  grid, rendered to its own view/video (tap) or an `obs_display` (fullscreen).
- **P/Invoke granularity / threading**: synchronous calls guarded by a per-context recursive mutex;
  frame/state delivered via callbacks (buffers valid only for the callback duration).

### Known limitation
- **Per-item PiP opacity** (`PipSettings.opacity`) is not applied yet: a filter on the *shared* source
  would bleed across both buses. Geometry (pos/size/bounds), crop, z-order and visibility are honored;
  opacity awaits a per-bus source wrapper.
