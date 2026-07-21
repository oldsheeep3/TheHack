// switcher-engine - native libobs video engine.
//
// Implements native/switcher-engine/include/engine.h over libobs (docs/specs/libobs-engine-migration.md
// §2.1-2.4). Structure defined by agent-L-001; the libobs internals marked `TODO(L-002)` are completed
// by agent-L-002 on a Windows + OBS host (that is the only environment where libobs links/runs).
//
// Design:
//  - One shared source pool (`sources`): each input opened once as an obs_source_t, referenced by both
//    buses so a single webcam feeds ME1 and ME2 (the property that makes dual-M/E possible in-process).
//  - Two buses, each = a transition source + an obs_view + the video_t obtained from obs_view_add().
//    Outputs (virtualcam / NDI / display) attach to a bus's video_t.

#include "engine.h"

#include <map>
#include <mutex>
#include <string>

#include <obs.h>

namespace {

struct Bus {
    obs_source_t *transition = nullptr;  // current PGM = transition output
    obs_view_t *view = nullptr;
    video_t *video = nullptr;
    obs_source_t *preview_scene = nullptr;  // staged PVW target
};

}  // namespace

struct engine_ctx {
    std::mutex lock;
    Bus buses[2];
    std::map<std::string, obs_source_t *> sources;  // shared pool, keyed by contract source id
    engine_frame_cb frame_cb = nullptr;
    void *frame_user = nullptr;
    engine_state_cb state_cb = nullptr;
    void *state_user = nullptr;
    bool started = false;
};

// ---------------------------------------------------------------------------
// lifecycle
// ---------------------------------------------------------------------------

engine_ctx *engine_startup(const char *options_json) {
    auto *ctx = new engine_ctx();

    // TODO(L-002): parse options_json (canvas_width/height, fps, module_path) via a JSON lib.
    if (!obs_startup("en-US", nullptr, nullptr)) {
        delete ctx;
        return nullptr;
    }

    struct obs_video_info ovi = {};
    ovi.graphics_module = "libobs-d3d11";   // TODO(L-002): opengl fallback for non-Windows dev
    ovi.fps_num = 60;
    ovi.fps_den = 1;
    ovi.base_width = 1920;
    ovi.base_height = 1080;
    ovi.output_width = 1920;
    ovi.output_height = 1080;
    ovi.output_format = VIDEO_FORMAT_BGRA;
    ovi.colorspace = VIDEO_CS_709;
    ovi.range = VIDEO_RANGE_FULL;
    if (obs_reset_video(&ovi) != OBS_VIDEO_SUCCESS) {
        obs_shutdown();
        delete ctx;
        return nullptr;
    }

    struct obs_audio_info oai = {};
    oai.samples_per_sec = 48000;
    oai.speakers = SPEAKERS_STEREO;
    obs_reset_audio(&oai);

    // Load OBS bundled modules (win-dshow / obs-ffmpeg / image-source / text / DistroAV).
    // TODO(L-002): obs_add_module_path(module_bin, module_data) from options_json before loading.
    obs_load_all_modules();
    obs_post_load_modules();

    // Two independent program buses, each its own view + video pipeline.
    for (auto &bus : ctx->buses) {
        bus.view = obs_view_create();
        // TODO(L-002): create a real transition (e.g. "cut_transition"/"fade_transition") and set it
        // as the view's channel-0 source; obs_view_add() to get the bus video_t.
        // bus.transition = obs_source_create_private("cut_transition", "pgm", nullptr);
        // obs_view_set_source(bus.view, 0, bus.transition);
        // bus.video = obs_view_add(bus.view);
    }

    ctx->started = true;
    return ctx;
}

void engine_shutdown(engine_ctx *ctx) {
    if (!ctx) return;
    {
        std::lock_guard<std::mutex> guard(ctx->lock);
        for (auto &[id, src] : ctx->sources) {
            obs_source_release(src);
        }
        ctx->sources.clear();
        for (auto &bus : ctx->buses) {
            if (bus.view) {
                obs_view_set_source(bus.view, 0, nullptr);
                obs_view_remove(bus.view);
                obs_view_destroy(bus.view);
            }
            if (bus.transition) obs_source_release(bus.transition);
            if (bus.preview_scene) obs_source_release(bus.preview_scene);
        }
    }
    obs_shutdown();
    delete ctx;
}

// ---------------------------------------------------------------------------
// shared source pool
// ---------------------------------------------------------------------------

int engine_add_source(engine_ctx *ctx, const char *id, const char *type, const char *settings_json) {
    if (!ctx || !id || !type) return 1;
    std::lock_guard<std::mutex> guard(ctx->lock);

    // TODO(L-002): map contract `type` -> OBS source id (WEBCAM->dshow_input, SRT->ffmpeg_source with
    // srt:// url, NDI->DistroAV source, IMAGE/COLOR/TEXT->standard) and build obs_data from settings_json.
    // Open once; re-adding an existing id updates its settings rather than reopening the device.
    (void)settings_json;
    auto it = ctx->sources.find(id);
    if (it != ctx->sources.end()) {
        return 0;  // TODO(L-002): obs_source_update with new settings
    }
    // obs_source_t *src = obs_source_create(obs_source_id, id, settings, nullptr);
    // ctx->sources[id] = src;
    return 0;
}

int engine_remove_source(engine_ctx *ctx, const char *id) {
    if (!ctx || !id) return 1;
    std::lock_guard<std::mutex> guard(ctx->lock);
    auto it = ctx->sources.find(id);
    if (it == ctx->sources.end()) return 0;
    obs_source_release(it->second);
    ctx->sources.erase(it);
    return 0;
}

// ---------------------------------------------------------------------------
// dual M/E
// ---------------------------------------------------------------------------

void engine_set_preview(engine_ctx *ctx, int bus, const char *source_id) {
    if (!ctx || bus < 0 || bus > 1) return;
    // TODO(L-002): stage `source_id`'s scene as the bus's PVW target (used by the next TAKE).
    (void)source_id;
}

void engine_set_source_enabled(engine_ctx *ctx, int bus, const char *source_id, int enabled) {
    if (!ctx || bus < 0 || bus > 1 || !source_id) return;
    // TODO(L-002): add/remove the shared source as a scene item on the bus's PGM scene (hot mount).
    (void)enabled;
}

int engine_apply_program(engine_ctx *ctx, const char *program_json) {
    if (!ctx || !program_json) return 1;
    // TODO(L-002): parse ProgramRequest (bus, layers[sourceId+pip], take); rebuild the bus scene's
    // items to match `layers`, then TAKE if requested.
    return 0;
}

void engine_set_pip(engine_ctx *ctx, int bus, const char *source_id, const char *pip_json) {
    if (!ctx || bus < 0 || bus > 1 || !source_id) return;
    // TODO(L-002): apply x/y/width/height/opacity/zorder/crop to the source's scene item on `bus`.
    (void)pip_json;
}

void engine_take(engine_ctx *ctx, int bus, int transition_kind, int duration_ms) {
    if (!ctx || bus < 0 || bus > 1) return;
    // TODO(L-002): transition_kind 0=CUT (obs_transition_set) / 1=AUTO (obs_transition_start with
    // duration_ms). Must affect only `bus` - never the other bus's transition.
    (void)transition_kind;
    (void)duration_ms;
}

// ---------------------------------------------------------------------------
// outputs / multiview / display
// ---------------------------------------------------------------------------

int engine_apply_outputs(engine_ctx *ctx, const char *outputs_json) {
    if (!ctx || !outputs_json) return 1;
    // TODO(L-002): parse OutputsRequest; for each sink (VCAM1/2, HDMI, NDI1/2) create/reconfigure the
    // matching obs_output bound to the source bus's video_t. 1 sink failure must not stop the others.
    // NOTE(L-002): OBS ships a single virtual-camera output; document the 2nd-VCAM decision here
    // (2nd device vs. route one bus to NDI/HDMI) - spec §9.
    return 0;
}

int engine_apply_multiview(engine_ctx *ctx, const char *layout_json) {
    if (!ctx || !layout_json) return 1;
    // TODO(L-002): render PGM1/PGM2/PVW1/PVW2/SRC:<id>/EMPTY into a 4x4 (region-aware) composite for
    // the MULTIVIEW tap / display.
    return 0;
}

int engine_start_display(engine_ctx *ctx, const char *target, void *hwnd, int display_id) {
    if (!ctx || !target || !hwnd) return 1;
    // TODO(L-002): create an obs_display on `hwnd` that renders `target`'s view fullscreen.
    (void)display_id;
    return 0;
}

void engine_stop_display(engine_ctx *ctx, const char *target) {
    if (!ctx || !target) return;
    // TODO(L-002): destroy the obs_display for `target`.
}

// ---------------------------------------------------------------------------
// preview readback + state
// ---------------------------------------------------------------------------

void engine_set_tap(engine_ctx *ctx, const char *target, int enabled) {
    if (!ctx || !target) return;
    // TODO(L-002): enable/disable throttled BGRA readback of `target` -> frame_cb (<=30fps).
    (void)enabled;
}

void engine_set_frame_cb(engine_ctx *ctx, engine_frame_cb cb, void *user) {
    if (!ctx) return;
    std::lock_guard<std::mutex> guard(ctx->lock);
    ctx->frame_cb = cb;
    ctx->frame_user = user;
}

void engine_set_state_cb(engine_ctx *ctx, engine_state_cb cb, void *user) {
    if (!ctx) return;
    std::lock_guard<std::mutex> guard(ctx->lock);
    ctx->state_cb = cb;
    ctx->state_user = user;
}
