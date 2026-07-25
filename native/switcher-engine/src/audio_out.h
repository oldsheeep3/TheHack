// Per-bus audio output for switcher-engine.
//
// libobs can monitor audio to exactly ONE device for the whole process, which cannot satisfy "each
// program bus drives at least one independent output". So instead of using obs monitoring, this takes
// the raw PCM of a given audio track (obs_add_raw_audio_callback) and renders it to a chosen WASAPI
// endpoint. Every bus gets its own sink — or several, since a bus may be routed to more than one
// device (front-of-house plus a recorder, say).
//
// HDMI audio needs no special case: an HDMI sink appears as an ordinary WASAPI render endpoint, so
// routing a bus to it is the same operation as routing it to a USB interface.
#pragma once

#include <cstddef>
#include <string>

// Enumerates active WASAPI render endpoints as a JSON array
// ([{"id":..,"name":..,"is_default":bool}, ...]). Returns "[]" if enumeration fails.
std::string audio_out_enumerate_devices(void);

// Replaces the whole routing table: each entry sends obs audio track `mix_idx` to `device_id`
// (empty = the system default endpoint). Existing sinks not in the new list are stopped.
// `assignments_json` is [{"bus":0|1,"device_id":"..."}, ...].
// Returns 0 on success, non-zero when the payload could not be parsed.
int audio_out_apply(const char *assignments_json);

// Stops every sink and releases the audio subsystem. Call before obs_shutdown().
void audio_out_shutdown(void);
