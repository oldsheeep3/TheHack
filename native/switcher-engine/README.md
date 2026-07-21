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

- **OBS Studio**: `31.0.x` (pin the exact version here once L-002 validates it). libobs ABI is not
  guaranteed stable across majors, so this version is fixed and must match the runtime OBS install.
- CMake ≥ 3.24, a C++17 compiler (MSVC on Windows).
- libobs dev files: either OBS's CMake package (`find_package(libobs)`) or the headers + `obs.lib`.

## Build

```sh
# Preferred: OBS dev files expose a libobs CMake package
cmake -S . -B build
cmake --build build --config Release

# Fallback: point at headers + import lib explicitly
cmake -S . -B build -DLIBOBS_INCLUDE_DIR="C:/obs-studio/libobs" -DLIBOBS_LIB="C:/obs-studio/build/libobs/Release/obs.lib"
cmake --build build --config Release
```

The output `switcher-engine.dll` must sit next to `Switcher.App.exe` (or on the DLL search path) so
`DllImport("switcher-engine")` resolves. The OBS bundled plugin modules (`win-dshow`, `obs-ffmpeg`,
`image-source`, `text`, and optionally DistroAV for NDI) must be reachable via the module path passed in
`engine_startup(options_json)`.

## Status

`engine.h` (ABI) and this scaffold are delivered by **agent-L-001**. The libobs internals marked
`TODO(L-002)` in [`src/engine.cpp`](src/engine.cpp) — source-type mapping, dual transition/view wiring,
scene/PiP composition, output routing, multiview render, readback taps — are completed by **agent-L-002**.

### Open technical items (spec §9)
- **Dual virtual camera**: OBS ships a single virtual-camera output. Decide/record here whether the 2nd
  bus gets a second virtual-cam device or is routed to NDI/HDMI instead.
- **Bundled module set** to ship and load, and the module path layout.
- **Pinned OBS version** and any ABI shims needed.
