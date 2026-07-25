# Third-party components and licensing

Switcher is distributed under **GPL-2.0-or-later** (see `LICENSE`), because it links and ships libobs.

This file records what is redistributed, under what terms, and — importantly — **what is not
redistributed and why**.

> These are engineering notes on the licences involved, not legal advice. The NDI question in §3 in
> particular should be reviewed by someone qualified before a public release.

---

## 1. Redistributed: OBS Studio / libobs — GPL-2.0

**What.** `obs-runtime/` in a release contains libobs, its core data (`data/libobs`), and this
allow-list of OBS plugins:

`win-dshow`, `win-capture`, `win-wasapi`, `obs-ffmpeg`, `image-source`, `obs-text`, `text-freetype2`,
`obs-transitions`, `obs-filters`, `vlc-video`, `obs-x264`, `obs-outputs`, `rtmp-services`, `obs-browser`

Assembled by `tools/bundle-obs-runtime.ps1`; the exact version is recorded in
`obs-runtime/bundle-info.json`.

**Why it makes the whole GPL.** `native/switcher-engine` links libobs directly and the app ships it.
That is a derivative work under the GPL, so the combined distribution is GPL-2.0-or-later. Practical
consequences:

- **Source must be available** to anyone who receives a binary — publish the repository, and include
  the corresponding source for the exact libobs build that is bundled (or a written offer for it).
- **No additional restrictions** may be placed on redistribution.
- Every file bundled from OBS keeps its own copyright notices.

**Transitively bundled with the OBS plugins:** FFmpeg (LGPL-2.1+/GPL-2+ depending on build), x264
(GPL-2+), libvlc (LGPL-2.1+), FreeType (FTL/GPL-2), CEF & Chromium (BSD-3-Clause + others), Qt (LGPL-3,
present in `bin/64bit` even though the engine loads no Qt plugin). These ship as OBS built them; their
notices travel with the binaries.

---

## 2. Redistributed: .NET runtime and NuGet packages — MIT

`Microsoft.Extensions.*` (DI, Logging, Options, Primitives) and `HidSharp` are MIT-licensed and
GPL-compatible. The .NET runtime itself is MIT.

---

## 3. NDI — headers redistributed (MIT), runtime installed by the user

**Redistributed.** `native/switcher-engine/third_party/ndi/include/` — the NDI SDK's public headers,
verbatim. Each one carries its own notice: *"the following MIT license applies to this file ONLY and
not to the SDK as a whole."* MIT is GPL-compatible, so they can travel with this work, and their
notices are intact. Vendoring them means the source we ship can be rebuilt without downloading a
proprietary SDK first — which is what the GPL asks of us.

**Not redistributed.** `Processing.NDI.Lib.x64.dll` and everything else in the SDK. The runtime is
resolved at startup with `LoadLibrary` via `NDIlib_v5_load`, from the path the **NDI Tools / NDI
Runtime** installer records in `NDI_RUNTIME_DIR_V6` (or V5/V4). The NDI SDK licence does not grant
general redistribution of the runtime, so the user installs it — the same approach the OBS NDI plugin
takes. Absent it, every NDI entry point reports "unavailable" and the rest of the app is unaffected.

**⚠ What remains open.** GPL-2.0 is about the *combined work*, not only about redistribution. The
engine links GPL libobs and calls into the proprietary NDI library in the same binary, so shipping no
NDI binary does not by itself settle the question:

- The NDI library is loaded dynamically and is entirely optional; a build with `SWITCHER_HAS_NDI`
  undefined contains no NDI code at all. That weakens — but does not automatically settle — a
  derivative-work argument.
- The "system library" exception in GPL-2 §3 is usually read narrowly and probably does not cover NDI.
- The only structural fix is to move NDI into a **separate process** that does not link libobs, so the
  GPL binary and the proprietary library are not one work. That costs an IPC path for frames.
- The cheap alternative is to build public binaries with NDI disabled and offer the NDI-enabled build
  separately.

**Current position (decided 2026-07-25):** ship as-is — headers vendored under MIT, runtime installed
by the user. This matches the posture of DistroAV, which has been distributed under the GPL with the
same structure for years. Revisit before a wide public release; the separate-process split is the
fallback if the combined-work question has to be answered cleanly.

---

## 4. Not redistributed: hardware firmware

`firmware/` (Pico 2W controller) is part of this repository and carries the repository's licence. The
Pico SDK and its dependencies are fetched at build time and keep their own (BSD-3-Clause) terms.

---

## 5. Checklist before distributing a binary

- [ ] `LICENSE` (GPL-2.0-or-later) is included in the package
- [ ] This file is included
- [ ] `obs-runtime/bundle-info.json` records the bundled libobs version
- [ ] Corresponding source for the bundled libobs build is published or offered in writing
- [ ] The repository containing Switcher's own source is publicly reachable
- [ ] §3 (NDI) has been resolved for the configuration being shipped
