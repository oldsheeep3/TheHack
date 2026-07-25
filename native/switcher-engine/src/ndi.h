// Native NDI support for switcher-engine, implemented directly against the NDI SDK.
//
// This exists so NDI does not require the DistroAV (obs-ndi) OBS plugin: the SDK is loaded at *runtime*
// via Processing.NDI.DynamicLoad.h, so switcher-engine still builds and runs on a machine with no NDI
// installed - every entry point below degrades to "no NDI" instead of failing to load.
//
// Provides:
//   - source discovery for engine_enumerate_devices("NDI")
//   - an obs source type ("switcher_ndi") that receives an NDI stream into an async obs source, so the
//     rest of the engine treats an NDI input exactly like a webcam.
#pragma once

#include <string>

// Registers the "switcher_ndi" source and "switcher_ndi_output" output types when the NDI runtime can
// be loaded. Safe to call once after obs_startup(). Returns false when NDI is unavailable (the caller
// keeps using DistroAV's ndi_source/ndi_output if that plugin happens to be installed).
bool ndi_register_source(void);

// Whether ndi_register_source() succeeded, i.e. whether the native NDI types can be created.
bool ndi_is_available(void);

// obs source id for a native NDI input, or nullptr when NDI is unavailable.
const char *ndi_source_id(void);

// obs output id for a native NDI sender, or nullptr when NDI is unavailable.
const char *ndi_output_id(void);

// Appends the NDI sources currently visible on the network to `out` as JSON objects
// ({"id":..,"name":..,"formats":null}), comma-separated, without enclosing brackets. `first` tracks
// whether a comma is needed and is updated. No-op when NDI is unavailable.
void ndi_append_sources_json(std::string &out, bool &first);

// Releases the discovery instance and the runtime. Call during engine_shutdown, after all sources are
// destroyed.
void ndi_shutdown(void);
