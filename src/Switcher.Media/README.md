# Switcher.Media

Multi-source input manager (UVC / NDI / SRT) and GPU/PiP compositor engine implementing
`IInputSourceManager` / `ICompositorEngine` from `Switcher.Contracts`.

> 親プロジェクト: [`../../README.md`](../../README.md) ／ 仕様書: [`docs/specs/pc-switcher-app.md`](../../docs/specs/pc-switcher-app.md)

## Runtime prerequisites

This project **builds** on any platform/CI without additional native dependencies, but
**running** it requires:

- **GStreamer 1.18+** installed on the host, with the following plugins on `GST_PLUGIN_PATH`:
  - `ksvideosrc` / `mfvideosrc` (UVC capture, Windows)
  - the [NDI GStreamer plugin](https://github.com/teltek/gst-ndi) (`ndisrc`) for NDI sources
  - `srtsrc` (part of `gst-plugins-bad`) for SRT listener sources
  - `videoconvert`, `appsink`
  - `GstSharp` only provides the managed bindings; it does not bundle the native
    `gstreamer-1.0` runtime.
- **Windows 10/11** with a DirectX 11-capable GPU/driver for `DirectX11Compositor`. On a
  machine without a Direct3D 11 runtime (e.g. this Linux dev sandbox, or CI), the code still
  compiles; only calls that touch the GPU device (i.e. actually rendering a frame) require the
  runtime.

Without these, `dotnet build` / `dotnet test` succeed (all GStreamer/DirectX-dependent code is
abstracted behind `IGstPipeline` / `IGpuCompositor`, and unit tests substitute fakes for both),
but running the real pipelines/renderer will fail at the point they try to talk to the missing
native runtime.
