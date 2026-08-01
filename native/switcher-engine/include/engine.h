/*
 * switcher-engine - native libobs video engine C ABI.
 *
 * This header is the contract shared by the managed P/Invoke layer
 * (src/Switcher.Engine/NativeMethods.cs) and the libobs implementation (src/engine.cpp).
 * Spec: docs/specs/libobs-engine-migration.md §2.1. Defined by agent-L-001; implemented by agent-L-002.
 *
 * Conventions:
 *  - All strings are UTF-8, null-terminated. JSON payloads use the same snake_case contract as
 *    Switcher.Contracts (ProtocolJsonOptions).
 *  - `bus`: 0 = ME1 (PGM1/PVW1), 1 = ME2 (PGM2/PVW2).
 *  - Boolean parameters are passed as 4-byte int (0/1) to match .NET UnmanagedType.Bool.
 *  - int-returning functions return 0 on success, non-zero on failure.
 *  - Frame-callback buffers are only valid for the duration of the callback; copy before returning.
 *  - The engine owns a libobs graphics thread; callers may invoke these functions from any thread.
 */
#ifndef SWITCHER_ENGINE_H
#define SWITCHER_ENGINE_H

#include <stdint.h>

#if defined(_WIN32)
#  define SWITCHER_ENGINE_API __declspec(dllexport)
#else
#  define SWITCHER_ENGINE_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct engine_ctx engine_ctx;

/* target identifiers used by taps/displays: "PGM1" "PGM2" "PVW1" "PVW2" "MULTIVIEW" "SRC:<id>", plus
 * "HDMI1".."HDMI3" for displays. An HDMI target renders the program of whichever bus that sink carries
 * in the current output table (bus 0 when it carries none), so two HDMI projectors showing the same bus
 * still address two distinct displays - which addressing them as "PGM1"/"PGM2" could not do. */
typedef void (*engine_frame_cb)(void *user, const char *target,
                                const uint8_t *bgra, int width, int height, int stride);
typedef void (*engine_state_cb)(void *user, const char *state_json);

/* lifecycle */
SWITCHER_ENGINE_API engine_ctx *engine_startup(const char *options_json);
SWITCHER_ENGINE_API void engine_shutdown(engine_ctx *ctx);

/* shared source pool (opened once, referenced by both buses) */
SWITCHER_ENGINE_API int engine_add_source(engine_ctx *ctx, const char *id,
                                          const char *type, const char *settings_json);
SWITCHER_ENGINE_API int engine_remove_source(engine_ctx *ctx, const char *id);

/*
 * Enumerate selectable input devices for a source kind via libobs source-property lists.
 *   kind: "WEBCAM"/"UVC" -> dshow_input "video_device_id"; "NDI" -> ndi_source "ndi_source_name".
 * Returns a UTF-8 JSON array [{"id":"..","name":"..","formats":["WxH",..]|null}], owned by the engine
 * and valid until the next engine_enumerate_devices call on the same ctx (copy it before then). Returns
 * "[]" for an unknown kind or when the backing module (e.g. DistroAV for NDI) is not loaded.
 */
SWITCHER_ENGINE_API const char *engine_enumerate_devices(engine_ctx *ctx, const char *kind);

/* dual M/E */
SWITCHER_ENGINE_API void engine_set_preview(engine_ctx *ctx, int bus, const char *source_id);
SWITCHER_ENGINE_API void engine_set_source_enabled(engine_ctx *ctx, int bus,
                                                   const char *source_id, int enabled);
SWITCHER_ENGINE_API int engine_apply_program(engine_ctx *ctx, const char *program_json);
SWITCHER_ENGINE_API void engine_set_pip(engine_ctx *ctx, int bus,
                                        const char *source_id, const char *pip_json);
SWITCHER_ENGINE_API void engine_take(engine_ctx *ctx, int bus, int transition_kind, int duration_ms);

/* outputs / multiview / display */

/* Replaces the whole sink table: {"outputs":[{"sink":"VCAM1","source":"PGM1","ndi_name":"..."}, ...]}.
 * Sink tokens are VCAM1..VCAM3, HDMI1..HDMI3 and NDI1..NDI3, and the ordinal-less "HDMI" an older
 * runtime-config.json may still carry is read as "HDMI1". The operator builds the table, but only from
 * one webcam, up to three HDMI and up to three NDI sinks, six in all (Switcher.Contracts.OutputCatalog):
 * VCAM2/VCAM3 parse only so tables written before that ceiling keep loading. VCAM1 takes the OBS virtual
 * camera - there is only one, which is the whole reason webcams cap at one - while VCAM2/VCAM3 and
 * NDI1..NDI3 egress as NDI senders. HDMI sinks bind no output here: the App presents them with
 * engine_start_display. An unrecognised token is skipped and a sink that fails to start is reported by
 * engine_get_output_status - neither fails the call. Returns 0 unless the payload itself is unusable. */
SWITCHER_ENGINE_API int engine_apply_outputs(engine_ctx *ctx, const char *outputs_json);

/* --- audio ---------------------------------------------------------------
 * Each program bus owns one libobs audio track: bus 0 -> track 0, bus 1 -> track 1. A source's
 * `mixers` bitmask decides which buses hear it (bit 0 = bus 0, bit 1 = bus 1), which is how
 * AFV/ON/OFF is expressed: the caller recomputes the mask as bus membership changes.
 */
SWITCHER_ENGINE_API void engine_set_source_audio(engine_ctx *ctx, const char *id, int mixers);

/* Which sinks from the last engine_apply_outputs are actually egressing:
 * {"outputs":[{"sink":"VCAM2","source":"PGM2","running":false}, ...]}. An assignment can be accepted
 * and still never start (no NDI runtime, virtual camera held by another app), so this is how the
 * caller learns a program bus has no working output. An HDMI sink owns no output of its own, so it
 * reads as running exactly while a projector started with its token is attached. Pointer owned by the
 * engine, valid until the next call on the same ctx. */
SWITCHER_ENGINE_API const char *engine_get_output_status(engine_ctx *ctx);

/* Active audio render endpoints as JSON ([{"id","name","is_default"}, ...]). The returned pointer is
 * owned by the engine and valid until the next call on the same ctx. */
SWITCHER_ENGINE_API const char *engine_enumerate_audio_devices(engine_ctx *ctx);

/* Routes buses to audio devices: {"outputs":[{"bus":0,"device_id":"..."}, ...]}. A bus may appear more
 * than once to feed several devices; an empty device_id means the system default endpoint. Replaces
 * the whole table. Returns 0 on success. */
SWITCHER_ENGINE_API int engine_apply_audio_outputs(engine_ctx *ctx, const char *assignments_json);
SWITCHER_ENGINE_API int engine_apply_multiview(engine_ctx *ctx, const char *layout_json);
SWITCHER_ENGINE_API int engine_start_display(engine_ctx *ctx, const char *target,
                                             void *hwnd, int display_id);
SWITCHER_ENGINE_API void engine_stop_display(engine_ctx *ctx, const char *target);

/* preview readback + state */
SWITCHER_ENGINE_API void engine_set_tap(engine_ctx *ctx, const char *target, int enabled);
SWITCHER_ENGINE_API void engine_set_frame_cb(engine_ctx *ctx, engine_frame_cb cb, void *user);
SWITCHER_ENGINE_API void engine_set_state_cb(engine_ctx *ctx, engine_state_cb cb, void *user);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* SWITCHER_ENGINE_H */
