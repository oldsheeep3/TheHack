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

/* target identifiers used by taps/displays: "PGM1" "PGM2" "PVW1" "PVW2" "MULTIVIEW" "SRC:<id>". */
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

/* dual M/E */
SWITCHER_ENGINE_API void engine_set_preview(engine_ctx *ctx, int bus, const char *source_id);
SWITCHER_ENGINE_API void engine_set_source_enabled(engine_ctx *ctx, int bus,
                                                   const char *source_id, int enabled);
SWITCHER_ENGINE_API int engine_apply_program(engine_ctx *ctx, const char *program_json);
SWITCHER_ENGINE_API void engine_set_pip(engine_ctx *ctx, int bus,
                                        const char *source_id, const char *pip_json);
SWITCHER_ENGINE_API void engine_take(engine_ctx *ctx, int bus, int transition_kind, int duration_ms);

/* outputs / multiview / display */
SWITCHER_ENGINE_API int engine_apply_outputs(engine_ctx *ctx, const char *outputs_json);
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
