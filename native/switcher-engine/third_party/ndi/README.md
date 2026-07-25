# NDI SDK headers (vendored)

`include/` holds the NDI SDK's public headers, copied verbatim from **NDI 6 SDK**
(`C:\Program Files\NDI\NDI 6 SDK\Include`). Nothing else from the SDK is here: no import library, no
`Processing.NDI.Lib.x64.dll`, no tools.

## Why they are in the repository

Every header in the SDK's `Include` directory carries its own notice:

> NOTE : The following MIT license applies to this file ONLY and not to the SDK as a whole.

MIT is GPL-compatible, so these files can travel with a GPL-2.0-or-later work. Vendoring them means:

- **Building needs no NDI SDK install.** Anyone who receives the source can rebuild the exact binary
  we ship — which is what the GPL asks of us — without downloading a proprietary SDK first.
- **The NDI runtime is still the user's to install.** These are declarations only. `src/ndi.cpp`
  resolves `Processing.NDI.Lib.x64.dll` at runtime with `LoadLibrary` (`NDIlib_v5_load`), from the
  path the NDI Tools / NDI Runtime installer records in `NDI_RUNTIME_DIR_V6` (or V5/V4). With no NDI
  installed, every NDI entry point reports "unavailable" and the rest of the app is unaffected.

Hand-declaring the ABI instead was considered and rejected: `NDIlib_v5` is a struct of ~180 function
pointers, and the calls are made by member position. A single mis-ordered entry is a jump to the
wrong function at runtime, and the headers being MIT removes any reason to take that risk.

## Updating

Copy `Include\*.h` from a newer SDK over `include/` and rebuild. Keep the files verbatim — their MIT
notices must stay intact — and note the version below.

**Vendored version:** NDI 6 SDK (headers dated 2023-2026, Vizrt NDI AB).

The build prefers this copy. To compile against a different SDK instead, pass
`-DNDI_INCLUDE_DIR=<path>` to CMake.
