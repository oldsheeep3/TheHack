// switcher-engine - native libobs video engine.
//
// Implements native/switcher-engine/include/engine.h over libobs (docs/specs/libobs-engine-migration.md
// §2.1-2.4). ABI + scaffold defined by agent-L-001; the libobs internals are completed by agent-L-002.
//
// Design:
//  - One shared source pool (`sources`): each input opened once as an obs_source_t, referenced by both
//    buses so a single webcam feeds ME1 and ME2 (the property that makes dual-M/E possible in-process).
//  - Two buses, each = { program scene, preview scene, fade transition, program view+video,
//    preview view+video }. The transition renders whichever scene is "program"; TAKE moves program to
//    the staged preview scene and swaps the two roles, touching only that bus (no cross-bus bleed).
//  - Outputs (virtualcam / NDI) bind to a bus's program video_t; HDMI/display renders a target source
//    to an App-provided HWND via obs_display; taps read back a target video_t as throttled BGRA.
//
// BUILD/RUN: libobs links and runs on a Windows + OBS 31.0.x host only (see README.md). This file is not
// part of HybridSwitcher.sln and is not compiled by the managed CI (which injects FakeVideoEngine).

#include "engine.h"

#include <algorithm>
#include <cctype>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <map>
#include <mutex>
#include <string>
#include <vector>

#include <obs.h>
#include <graphics/vec2.h>

namespace {

constexpr int kBusCount = 2;
constexpr uint64_t kTapMinIntervalNs = 33'000'000;  // ~30 fps upper bound on readback (spec step 7)

// A single preview readback tap: a video_t rendered as BGRA and pumped to the frame callback.
struct Tap {
    video_t *video = nullptr;       // borrowed (bus/mv view) or owned via `owned_view`
    obs_view_t *owned_view = nullptr;  // non-null only for lazily-created SRC:<id> taps
    bool connected = false;
    uint64_t last_ns = 0;
    std::string target;
};

// A live output sink (virtualcam / NDI) bound to a bus program video.
struct OutputSink {
    obs_output_t *output = nullptr;
};

// A fullscreen obs_display rendering a target source to an App window.
struct DisplayOut {
    obs_display_t *display = nullptr;
    obs_source_t *source = nullptr;  // borrowed: transition / preview scene / multiview scene source
    uint32_t base_w = 0;
    uint32_t base_h = 0;
};

// Per-tap trampoline arg: the video_output_connect callback identifies its engine + target through this.
struct TapCbArg {
    engine_ctx *ctx;
    std::string target;
};

struct Bus {
    obs_scene_t *program_scene = nullptr;  // currently-live composition
    obs_scene_t *preview_scene = nullptr;  // staged composition (next TAKE target)
    obs_source_t *transition = nullptr;    // fade transition; its dest = program scene source
    obs_view_t *program_view = nullptr;
    obs_view_t *preview_view = nullptr;
    video_t *program_video = nullptr;
    video_t *preview_video = nullptr;
    std::string program_id;  // last single-source id staged/live (for state + convenience)
    std::string preview_id;
};

}  // namespace

struct engine_ctx {
    std::recursive_mutex lock;
    Bus buses[kBusCount];

    std::map<std::string, obs_source_t *> sources;  // shared pool, keyed by contract source id

    // multiview composite (its own scene + view + video)
    obs_scene_t *mv_scene = nullptr;
    obs_view_t *mv_view = nullptr;
    video_t *mv_video = nullptr;

    std::map<std::string, Tap> taps;               // keyed by target token
    std::map<std::string, TapCbArg *> tap_args;    // trampoline args, keyed by target token
    std::map<std::string, OutputSink> outputs;     // keyed by sink token (VCAM1/NDI1/...)
    std::map<std::string, DisplayOut> displays;    // keyed by target token

    engine_frame_cb frame_cb = nullptr;
    void *frame_user = nullptr;
    engine_state_cb state_cb = nullptr;
    void *state_user = nullptr;

    uint32_t canvas_w = 1920;
    uint32_t canvas_h = 1080;

    FILE *log_file = nullptr;
    bool started = false;
};

// ---------------------------------------------------------------------------
// small helpers
// ---------------------------------------------------------------------------
namespace {

std::string to_upper(std::string s) {
    for (char &c : s) c = static_cast<char>(::toupper(static_cast<unsigned char>(c)));
    return s;
}

const char *env_or_null(const char *name) {
    const char *v = std::getenv(name);
    return (v && *v) ? v : nullptr;
}

// Contract source type (UPPERCASE, e.g. "WEBCAM"/"UVC"/"SRT"/"NDI") -> OBS bundled source id.
const char *map_source_type(const std::string &type) {
    const std::string t = to_upper(type);
    if (t == "WEBCAM" || t == "UVC" || t == "DSHOW") return "dshow_input";     // win-dshow
    if (t == "SRT" || t == "MEDIA" || t == "FFMPEG") return "ffmpeg_source";   // obs-ffmpeg (srt:// ok)
    if (t == "NDI") return "ndi_source";                                       // DistroAV
    if (t == "IMAGE") return "image_source";
    if (t == "COLOR") return "color_source";
    if (t == "TEXT") return "text_gdiplus";
    return nullptr;
}

// Translate a contract settings payload (snake_case, WebcamConfig/SrtConfig/NdiConfig or {"url":..})
// into OBS-native source settings for `obs_source_id`. Returns a new obs_data_t (caller releases).
obs_data_t *build_native_settings(const std::string &obs_source_id, const char *settings_json) {
    obs_data_t *in = settings_json ? obs_data_create_from_json(settings_json) : nullptr;
    obs_data_t *out = obs_data_create();

    if (obs_source_id == "dshow_input") {
        const char *dev = in ? obs_data_get_string(in, "device_id") : "";
        if (dev && *dev) obs_data_set_string(out, "video_device_id", dev);
        const char *fmt = in ? obs_data_get_string(in, "format") : "";
        if (fmt && *fmt) obs_data_set_string(out, "resolution", fmt);  // best-effort (host tunes)
    } else if (obs_source_id == "ffmpeg_source") {
        const char *url = in ? obs_data_get_string(in, "url") : "";
        if (url && *url) obs_data_set_string(out, "input", url);
        obs_data_set_bool(out, "is_local_file", false);
        obs_data_set_bool(out, "restart_on_activate", false);
        if (in) {
            long long latency = obs_data_get_int(in, "latency_ms");
            if (latency > 0) obs_data_set_int(out, "reconnect_delay_sec", 1);
        }
    } else if (obs_source_id == "ndi_source") {
        const char *name = in ? obs_data_get_string(in, "source_name") : "";
        if (name && *name) obs_data_set_string(out, "ndi_source_name", name);
    } else if (obs_source_id == "image_source") {
        const char *url = in ? obs_data_get_string(in, "url") : "";
        if (url && *url) obs_data_set_string(out, "file", url);
    } else if (in) {
        // color/text and forward-compat: pass the raw contract data through unchanged.
        obs_data_apply(out, in);
    }

    if (in) obs_data_release(in);
    return out;
}

void set_item_geometry(obs_sceneitem_t *item, uint32_t bw, uint32_t bh,
                       const obs_data_t *pip /* may be null */) {
    struct vec2 pos = {0.0f, 0.0f};
    struct vec2 bounds;
    vec2_set(&bounds, static_cast<float>(bw), static_cast<float>(bh));

    if (pip && obs_data_get_bool(const_cast<obs_data_t *>(pip), "enabled")) {
        pos.x = static_cast<float>(obs_data_get_int(const_cast<obs_data_t *>(pip), "x_position"));
        pos.y = static_cast<float>(obs_data_get_int(const_cast<obs_data_t *>(pip), "y_position"));
        long long w = obs_data_get_int(const_cast<obs_data_t *>(pip), "width");
        long long h = obs_data_get_int(const_cast<obs_data_t *>(pip), "height");
        if (w > 0) bounds.x = static_cast<float>(w);
        if (h > 0) bounds.y = static_cast<float>(h);

        obs_data_t *crop = obs_data_get_obj(const_cast<obs_data_t *>(pip), "crop");
        if (crop) {
            struct obs_sceneitem_crop c = {};
            c.left = static_cast<int>(obs_data_get_int(crop, "left"));
            c.top = static_cast<int>(obs_data_get_int(crop, "top"));
            c.right = static_cast<int>(obs_data_get_int(crop, "right"));
            c.bottom = static_cast<int>(obs_data_get_int(crop, "bottom"));
            obs_sceneitem_set_crop(item, &c);
            obs_data_release(crop);
        }
    }

    // Fit the (shared, arbitrarily-sized) source into the target rectangle.
    obs_sceneitem_set_bounds_type(item, OBS_BOUNDS_SCALE_INNER);
    obs_sceneitem_set_bounds(item, &bounds);
    obs_sceneitem_set_alignment(item, OBS_ALIGN_LEFT | OBS_ALIGN_TOP);
    obs_sceneitem_set_bounds_alignment(item, OBS_ALIGN_LEFT | OBS_ALIGN_TOP);
    obs_sceneitem_set_pos(item, &pos);
    // NOTE(L-002): per-item opacity (PipSettings.opacity) is intentionally not applied here - it would
    // require a filter on the *shared* source, which would bleed across both buses. Deferred until a
    // per-bus source-wrapper exists; geometry/crop/visibility/z-order are honored.
}

// Remove every item currently in `scene`.
bool collect_item_cb(obs_scene_t *, obs_sceneitem_t *item, void *param) {
    auto *vec = static_cast<std::vector<obs_sceneitem_t *> *>(param);
    obs_sceneitem_addref(item);
    vec->push_back(item);
    return true;
}

void clear_scene(obs_scene_t *scene) {
    if (!scene) return;
    std::vector<obs_sceneitem_t *> items;
    obs_scene_enum_items(scene, collect_item_cb, &items);
    for (auto *it : items) {
        obs_sceneitem_remove(it);
        obs_sceneitem_release(it);
    }
}

// Stage a single shared source as a full-frame item on `scene`.
void scene_set_single(engine_ctx *ctx, obs_scene_t *scene, const std::string &source_id) {
    clear_scene(scene);
    auto it = ctx->sources.find(source_id);
    if (it == ctx->sources.end()) return;
    obs_sceneitem_t *item = obs_scene_add(scene, it->second);  // scene item holds its own ref
    if (item) set_item_geometry(item, ctx->canvas_w, ctx->canvas_h, nullptr);
}

// Extract the string values of a top-level JSON array field (e.g. "cells":["PGM1","EMPTY",...]).
// libobs' obs_data arrays only hold objects, not primitive strings, so the legacy `cells` form is
// scanned directly from the JSON text. Returns [] when the field is absent.
std::vector<std::string> parse_string_array(const char *json, const char *field) {
    std::vector<std::string> out;
    if (!json || !field) return out;
    const std::string text = json;
    const std::string key = std::string("\"") + field + "\"";
    size_t k = text.find(key);
    if (k == std::string::npos) return out;
    size_t lb = text.find('[', k);
    if (lb == std::string::npos) return out;
    size_t rb = text.find(']', lb);
    if (rb == std::string::npos) return out;
    size_t i = lb + 1;
    while (i < rb) {
        size_t q1 = text.find('"', i);
        if (q1 == std::string::npos || q1 >= rb) break;
        size_t q2 = text.find('"', q1 + 1);
        if (q2 == std::string::npos || q2 > rb) break;
        out.push_back(text.substr(q1 + 1, q2 - q1 - 1));
        i = q2 + 1;
    }
    return out;
}

int bus_index_from_token(const char *tok) {
    if (!tok) return -1;
    std::string t = to_upper(tok);
    if (t == "PGM1" || t == "PVW1") return 0;
    if (t == "PGM2" || t == "PVW2") return 1;
    return -1;
}

// Resolve a tap/display target token to a rendered video_t (creating a SRC:<id> view on demand).
video_t *resolve_target_video(engine_ctx *ctx, const std::string &target) {
    std::string t = to_upper(target);
    if (t == "PGM1") return ctx->buses[0].program_video;
    if (t == "PGM2") return ctx->buses[1].program_video;
    if (t == "PVW1") return ctx->buses[0].preview_video;
    if (t == "PVW2") return ctx->buses[1].preview_video;
    if (t == "MULTIVIEW") return ctx->mv_video;

    if (target.rfind("SRC:", 0) == 0) {
        const std::string id = target.substr(4);
        auto sit = ctx->sources.find(id);
        if (sit == ctx->sources.end()) return nullptr;
        auto &tap = ctx->taps[target];  // reuse existing owned_view if present
        if (!tap.owned_view) {
            tap.owned_view = obs_view_create();
            obs_view_set_source(tap.owned_view, 0, sit->second);
            tap.video = obs_view_add(tap.owned_view);
            tap.target = target;
        }
        return tap.video;
    }
    return nullptr;
}

obs_source_t *resolve_target_source(engine_ctx *ctx, const std::string &target) {
    std::string t = to_upper(target);
    if (t == "PGM1") return ctx->buses[0].transition;
    if (t == "PGM2") return ctx->buses[1].transition;
    if (t == "PVW1") return obs_scene_get_source(ctx->buses[0].preview_scene);
    if (t == "PVW2") return obs_scene_get_source(ctx->buses[1].preview_scene);
    if (t == "MULTIVIEW") return obs_scene_get_source(ctx->mv_scene);
    if (target.rfind("SRC:", 0) == 0) {
        auto sit = ctx->sources.find(target.substr(4));
        if (sit != ctx->sources.end()) return sit->second;
    }
    return nullptr;
}

void on_raw_video_trampoline(void *param, struct video_data *frame) {
    auto *arg = static_cast<TapCbArg *>(param);
    engine_ctx *ctx = arg->ctx;
    if (!frame || !frame->data[0]) return;

    engine_frame_cb cb;
    void *user;
    uint32_t w, h;
    {
        std::lock_guard<std::recursive_mutex> guard(ctx->lock);
        auto it = ctx->taps.find(arg->target);
        if (it == ctx->taps.end() || !it->second.connected) return;
        if (frame->timestamp - it->second.last_ns < kTapMinIntervalNs && it->second.last_ns != 0) return;
        it->second.last_ns = frame->timestamp;
        cb = ctx->frame_cb;
        user = ctx->frame_user;
        w = ctx->canvas_w;
        h = ctx->canvas_h;
    }
    if (!cb) return;
    cb(user, arg->target.c_str(), frame->data[0], static_cast<int>(w), static_cast<int>(h),
       static_cast<int>(frame->linesize[0]));
}
}  // namespace

// ---------------------------------------------------------------------------
// diagnostics
// ---------------------------------------------------------------------------
namespace {
FILE *g_log_file = nullptr;
void log_handler(int, const char *msg, va_list args, void *) {
    if (!g_log_file) return;
    vfprintf(g_log_file, msg, args);
    fputc('\n', g_log_file);
    fflush(g_log_file);
}
}  // namespace

// ---------------------------------------------------------------------------
// lifecycle
// ---------------------------------------------------------------------------

engine_ctx *engine_startup(const char *options_json) {
    auto *ctx = new engine_ctx();

    // Optional libobs log capture (SWITCHER_ENGINE_LOG=<file>) - invaluable when obs_reset_video fails.
    if (const char *log_path = env_or_null("SWITCHER_ENGINE_LOG")) {
        ctx->log_file = std::fopen(log_path, "w");
        g_log_file = ctx->log_file;
        base_set_log_handler(log_handler, nullptr);
    }

    // Parse options_json via libobs' own JSON (no extra dependency). EngineOptions (Switcher.Contracts)
    // provides canvas_width/height/fps/module_path; the remaining path knobs are read from options_json
    // if present, else from the environment (managed EngineOptions does not carry them yet - see README).
    obs_data_t *opt = options_json ? obs_data_create_from_json(options_json) : obs_data_create();
    if (!opt) opt = obs_data_create();
    obs_data_set_default_int(opt, "canvas_width", 1920);
    obs_data_set_default_int(opt, "canvas_height", 1080);
    obs_data_set_default_int(opt, "fps", 60);
    ctx->canvas_w = static_cast<uint32_t>(obs_data_get_int(opt, "canvas_width"));
    ctx->canvas_h = static_cast<uint32_t>(obs_data_get_int(opt, "canvas_height"));
    const int fps = static_cast<int>(obs_data_get_int(opt, "fps"));

    auto pick = [&](const char *json_key, const char *env_key) -> std::string {
        const char *v = obs_data_get_string(opt, json_key);
        if (v && *v) return v;
        const char *e = env_or_null(env_key);
        return e ? std::string(e) : std::string();
    };
    const std::string data_path = pick("data_path", "SWITCHER_OBS_DATA_PATH");
    const std::string module_bin = pick("module_bin_path", "SWITCHER_OBS_MODULE_BIN");
    const std::string module_data = pick("module_data_path", "SWITCHER_OBS_MODULE_DATA");
    std::string graphics_module = pick("graphics_module", "SWITCHER_OBS_GRAPHICS_MODULE");
    const std::string module_path = obs_data_get_string(opt, "module_path");  // EngineOptions.ModulePath
    if (graphics_module.empty()) graphics_module = "libobs-d3d11";  // d3d11 for shipping; opengl on dev

    if (!obs_startup("en-US", nullptr, nullptr)) {
        obs_data_release(opt);
        delete ctx;
        return nullptr;
    }

    // CORE data path MUST be registered before obs_reset_video, otherwise libobs cannot find its built-in
    // effects (default.effect etc.) and graphics init fails - this was the L-001 scaffold's blocker.
    if (!data_path.empty()) obs_add_data_path(data_path.c_str());

    struct obs_video_info ovi = {};
    ovi.graphics_module = graphics_module.c_str();
    ovi.fps_num = static_cast<uint32_t>(fps > 0 ? fps : 60);
    ovi.fps_den = 1;
    ovi.base_width = ctx->canvas_w;
    ovi.base_height = ctx->canvas_h;
    ovi.output_width = ctx->canvas_w;
    ovi.output_height = ctx->canvas_h;
    ovi.output_format = VIDEO_FORMAT_BGRA;  // BGRA so taps can read back without a color convert
    ovi.colorspace = VIDEO_CS_709;
    ovi.range = VIDEO_RANGE_FULL;
    ovi.scale_type = OBS_SCALE_BICUBIC;
    if (obs_reset_video(&ovi) != OBS_VIDEO_SUCCESS) {
        obs_data_release(opt);
        obs_shutdown();
        if (ctx->log_file) std::fclose(ctx->log_file);
        delete ctx;
        return nullptr;
    }

    struct obs_audio_info oai = {};
    oai.samples_per_sec = 48000;
    oai.speakers = SPEAKERS_STEREO;
    obs_reset_audio(&oai);

    // Bundled source modules (win-dshow / obs-ffmpeg / image-source / text / DistroAV). A missing module
    // must not stop startup - obs_load_all_modules tolerates individual failures.
    if (!module_bin.empty())
        obs_add_module_path(module_bin.c_str(),
                            module_data.empty() ? module_bin.c_str() : module_data.c_str());
    if (!module_path.empty()) obs_add_module_path(module_path.c_str(), module_path.c_str());
    obs_load_all_modules();
    obs_post_load_modules();
    obs_data_release(opt);

    // Multiview composite scene/view/video.
    ctx->mv_scene = obs_scene_create_private("mv");
    ctx->mv_view = obs_view_create();
    obs_view_set_source(ctx->mv_view, 0, obs_scene_get_source(ctx->mv_scene));
    ctx->mv_video = obs_view_add(ctx->mv_view);

    // Two independent program buses, each with a fade transition over a program scene, plus a preview
    // scene/view for PVW readback. The transition (not the raw scene) is the view's channel-0 source so
    // AUTO transitions animate; CUT uses obs_transition_set on the same transition.
    for (auto &bus : ctx->buses) {
        bus.program_scene = obs_scene_create_private("pgm");
        bus.preview_scene = obs_scene_create_private("pvw");

        bus.transition = obs_source_create_private("fade_transition", "me_transition", nullptr);
        obs_transition_set(bus.transition, obs_scene_get_source(bus.program_scene));

        bus.program_view = obs_view_create();
        obs_view_set_source(bus.program_view, 0, bus.transition);
        bus.program_video = obs_view_add(bus.program_view);

        bus.preview_view = obs_view_create();
        obs_view_set_source(bus.preview_view, 0, obs_scene_get_source(bus.preview_scene));
        bus.preview_video = obs_view_add(bus.preview_view);
    }

    ctx->started = true;
    return ctx;
}

void engine_shutdown(engine_ctx *ctx) {
    if (!ctx) return;
    {
        std::lock_guard<std::recursive_mutex> guard(ctx->lock);

        // Stop readback before tearing down views (callbacks reference ctx->taps).
        for (auto &[target, tap] : ctx->taps) {
            if (tap.connected && tap.video) {
                auto ait = ctx->tap_args.find(target);
                if (ait != ctx->tap_args.end()) {
                    video_output_disconnect(tap.video, on_raw_video_trampoline, ait->second);
                }
            }
            if (tap.owned_view) {
                obs_view_set_source(tap.owned_view, 0, nullptr);
                obs_view_remove(tap.owned_view);
                obs_view_destroy(tap.owned_view);
            }
        }
        ctx->taps.clear();
        for (auto &[target, arg] : ctx->tap_args) delete arg;
        ctx->tap_args.clear();

        // Stop + release outputs.
        for (auto &[sink, out] : ctx->outputs) {
            if (out.output) {
                obs_output_stop(out.output);
                obs_output_release(out.output);
            }
        }
        ctx->outputs.clear();

        // Destroy displays.
        for (auto &[target, disp] : ctx->displays) {
            if (disp.display) obs_display_destroy(disp.display);
        }
        ctx->displays.clear();

        // Buses.
        for (auto &bus : ctx->buses) {
            if (bus.program_view) {
                obs_view_set_source(bus.program_view, 0, nullptr);
                obs_view_remove(bus.program_view);
                obs_view_destroy(bus.program_view);
            }
            if (bus.preview_view) {
                obs_view_set_source(bus.preview_view, 0, nullptr);
                obs_view_remove(bus.preview_view);
                obs_view_destroy(bus.preview_view);
            }
            if (bus.transition) {
                obs_transition_clear(bus.transition);
                obs_source_release(bus.transition);
            }
            if (bus.program_scene) obs_scene_release(bus.program_scene);
            if (bus.preview_scene) obs_scene_release(bus.preview_scene);
        }

        // Multiview.
        if (ctx->mv_view) {
            obs_view_set_source(ctx->mv_view, 0, nullptr);
            obs_view_remove(ctx->mv_view);
            obs_view_destroy(ctx->mv_view);
        }
        if (ctx->mv_scene) obs_scene_release(ctx->mv_scene);

        // Shared source pool.
        for (auto &[id, src] : ctx->sources) obs_source_release(src);
        ctx->sources.clear();
    }

    obs_shutdown();
    g_log_file = nullptr;
    if (ctx->log_file) std::fclose(ctx->log_file);
    delete ctx;
}

// ---------------------------------------------------------------------------
// shared source pool
// ---------------------------------------------------------------------------

int engine_add_source(engine_ctx *ctx, const char *id, const char *type, const char *settings_json) {
    if (!ctx || !id || !type) return 1;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);

    const char *obs_id = map_source_type(type);
    if (!obs_id) return 2;  // unknown contract source type

    obs_data_t *settings = build_native_settings(obs_id, settings_json);

    auto it = ctx->sources.find(id);
    if (it != ctx->sources.end()) {
        // Re-adding an existing id updates its settings rather than reopening the device.
        obs_source_update(it->second, settings);
        obs_data_release(settings);
        return 0;
    }

    obs_source_t *src = obs_source_create(obs_id, id, settings, nullptr);
    obs_data_release(settings);
    if (!src) return 3;
    ctx->sources[id] = src;  // pool holds the single reference; scene items add their own
    return 0;
}

int engine_remove_source(engine_ctx *ctx, const char *id) {
    if (!ctx || !id) return 1;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);

    // Drop any SRC:<id> tap that referenced it.
    const std::string tap_key = std::string("SRC:") + id;
    auto tit = ctx->taps.find(tap_key);
    if (tit != ctx->taps.end()) {
        if (tit->second.connected && tit->second.video) {
            auto ait = ctx->tap_args.find(tap_key);
            if (ait != ctx->tap_args.end())
                video_output_disconnect(tit->second.video, on_raw_video_trampoline, ait->second);
        }
        if (tit->second.owned_view) {
            obs_view_set_source(tit->second.owned_view, 0, nullptr);
            obs_view_remove(tit->second.owned_view);
            obs_view_destroy(tit->second.owned_view);
        }
        ctx->taps.erase(tit);
        auto ait = ctx->tap_args.find(tap_key);
        if (ait != ctx->tap_args.end()) { delete ait->second; ctx->tap_args.erase(ait); }
    }

    auto it = ctx->sources.find(id);
    if (it == ctx->sources.end()) return 0;
    obs_source_release(it->second);  // scene items that still reference it keep it alive until removed
    ctx->sources.erase(it);
    return 0;
}

// ---------------------------------------------------------------------------
// dual M/E
// ---------------------------------------------------------------------------

static void emit_state(engine_ctx *ctx) {
    engine_state_cb cb = ctx->state_cb;
    if (!cb) return;
    char buf[512];
    std::snprintf(buf, sizeof(buf),
                  "{\"buses\":["
                  "{\"bus\":0,\"program_id\":\"%s\",\"preview_id\":\"%s\"},"
                  "{\"bus\":1,\"program_id\":\"%s\",\"preview_id\":\"%s\"}]}",
                  ctx->buses[0].program_id.c_str(), ctx->buses[0].preview_id.c_str(),
                  ctx->buses[1].program_id.c_str(), ctx->buses[1].preview_id.c_str());
    cb(ctx->state_user, buf);
}

void engine_set_preview(engine_ctx *ctx, int bus, const char *source_id) {
    if (!ctx || bus < 0 || bus >= kBusCount) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    Bus &b = ctx->buses[bus];
    b.preview_id = source_id ? source_id : "";
    scene_set_single(ctx, b.preview_scene, b.preview_id);
    emit_state(ctx);
}

void engine_set_source_enabled(engine_ctx *ctx, int bus, const char *source_id, int enabled) {
    if (!ctx || bus < 0 || bus >= kBusCount || !source_id) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    Bus &b = ctx->buses[bus];

    // Hot mount/unmount the shared source as an item on the LIVE program scene.
    std::vector<obs_sceneitem_t *> items;
    obs_scene_enum_items(b.program_scene, collect_item_cb, &items);
    obs_sceneitem_t *existing = nullptr;
    auto sit = ctx->sources.find(source_id);
    for (auto *it : items) {
        if (sit != ctx->sources.end() && obs_sceneitem_get_source(it) == sit->second) existing = it;
    }
    if (enabled && !existing && sit != ctx->sources.end()) {
        obs_sceneitem_t *item = obs_scene_add(b.program_scene, sit->second);
        if (item) set_item_geometry(item, ctx->canvas_w, ctx->canvas_h, nullptr);
    } else if (!enabled && existing) {
        obs_sceneitem_remove(existing);
    }
    for (auto *it : items) obs_sceneitem_release(it);
}

int engine_apply_program(engine_ctx *ctx, const char *program_json) {
    if (!ctx || !program_json) return 1;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);

    obs_data_t *req = obs_data_create_from_json(program_json);
    if (!req) return 2;

    const int bus = bus_index_from_token(obs_data_get_string(req, "bus"));
    if (bus < 0) { obs_data_release(req); return 3; }
    Bus &b = ctx->buses[bus];

    // Rebuild the STAGED (preview) scene from layers[], sorted bottom-up by z_order.
    obs_data_array_t *layers = obs_data_get_array(req, "layers");
    clear_scene(b.preview_scene);
    b.preview_id.clear();

    struct LayerRec { std::string id; obs_data_t *pip; long long z; };
    std::vector<LayerRec> recs;
    const size_t n = layers ? obs_data_array_count(layers) : 0;
    for (size_t i = 0; i < n; ++i) {
        obs_data_t *layer = obs_data_array_item(layers, i);
        const char *sid = obs_data_get_string(layer, "source_id");
        obs_data_t *pip = obs_data_get_obj(layer, "pip");
        long long z = pip ? obs_data_get_int(pip, "z_order") : 0;
        recs.push_back({sid ? sid : "", pip, z});
        obs_data_release(layer);
    }
    std::stable_sort(recs.begin(), recs.end(),
                     [](const LayerRec &a, const LayerRec &c) { return a.z < c.z; });
    for (auto &r : recs) {
        auto sit = ctx->sources.find(r.id);
        if (sit != ctx->sources.end()) {
            obs_sceneitem_t *item = obs_scene_add(b.preview_scene, sit->second);
            if (item) set_item_geometry(item, ctx->canvas_w, ctx->canvas_h, r.pip);
            if (b.preview_id.empty()) b.preview_id = r.id;
        }
        if (r.pip) obs_data_release(r.pip);
    }
    if (layers) obs_data_array_release(layers);

    const bool take = obs_data_get_bool(req, "take");
    obs_data_release(req);

    if (take) engine_take(ctx, bus, /*CUT*/ 0, 0);
    emit_state(ctx);
    return 0;
}

void engine_set_pip(engine_ctx *ctx, int bus, const char *source_id, const char *pip_json) {
    if (!ctx || bus < 0 || bus >= kBusCount || !source_id) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    Bus &b = ctx->buses[bus];

    auto sit = ctx->sources.find(source_id);
    if (sit == ctx->sources.end()) return;

    obs_data_t *pip = pip_json ? obs_data_create_from_json(pip_json) : nullptr;

    // Apply to the source's item on the LIVE program scene (create it if the PiP enables a missing item).
    std::vector<obs_sceneitem_t *> items;
    obs_scene_enum_items(b.program_scene, collect_item_cb, &items);
    obs_sceneitem_t *target = nullptr;
    for (auto *it : items) {
        if (obs_sceneitem_get_source(it) == sit->second) target = it;
    }
    if (!target && pip && obs_data_get_bool(pip, "enabled")) {
        target = obs_scene_add(b.program_scene, sit->second);
    }
    if (target) set_item_geometry(target, ctx->canvas_w, ctx->canvas_h, pip);
    for (auto *it : items) obs_sceneitem_release(it);
    if (pip) obs_data_release(pip);
}

void engine_take(engine_ctx *ctx, int bus, int transition_kind, int duration_ms) {
    if (!ctx || bus < 0 || bus >= kBusCount) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    Bus &b = ctx->buses[bus];

    obs_source_t *dest = obs_scene_get_source(b.preview_scene);
    if (transition_kind == 1 /* AUTO */ && duration_ms > 0) {
        obs_transition_start(b.transition, OBS_TRANSITION_MODE_AUTO,
                             static_cast<uint32_t>(duration_ms), dest);
    } else {
        obs_transition_set(b.transition, dest);  // CUT
    }

    // Swap program<->preview roles so the just-taken scene becomes live and staging continues on the
    // other. The transition already points at the new program; re-point the preview view to the new
    // (now-empty) preview scene. Only this bus's transition was touched - ME1/ME2 stay independent.
    std::swap(b.program_scene, b.preview_scene);
    std::swap(b.program_id, b.preview_id);
    obs_view_set_source(b.preview_view, 0, obs_scene_get_source(b.preview_scene));

    emit_state(ctx);
}

// ---------------------------------------------------------------------------
// outputs / multiview / display
// ---------------------------------------------------------------------------

int engine_apply_outputs(engine_ctx *ctx, const char *outputs_json) {
    if (!ctx || !outputs_json) return 1;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);

    obs_data_t *req = obs_data_create_from_json(outputs_json);
    if (!req) return 2;
    obs_data_array_t *arr = obs_data_get_array(req, "outputs");
    const size_t n = arr ? obs_data_array_count(arr) : 0;

    // Rebuild all sinks: stop existing, recreate from the request. One sink failing must not abort the
    // rest (isolation, spec §2.3) - we continue past individual failures and never fail the whole call.
    for (auto &[sink, out] : ctx->outputs) {
        if (out.output) { obs_output_stop(out.output); obs_output_release(out.output); }
    }
    ctx->outputs.clear();

    for (size_t i = 0; i < n; ++i) {
        obs_data_t *a = obs_data_array_item(arr, i);
        const std::string sink = to_upper(obs_data_get_string(a, "sink"));
        const std::string source = to_upper(obs_data_get_string(a, "source"));  // PGM1 / PGM2
        const int bus = (source == "PGM2") ? 1 : 0;
        video_t *video = ctx->buses[bus].program_video;

        if (sink == "HDMI") {
            // HDMI is presented via engine_start_display(target, hwnd, display_id) from the App, which
            // owns the window handle. Nothing to bind here.
            obs_data_release(a);
            continue;
        }

        const char *obs_output_id = nullptr;
        obs_data_t *osettings = obs_data_create();
        if (sink == "VCAM1") {
            // OBS ships a SINGLE virtual-camera output. VCAM1 gets it; VCAM2 is routed to NDI instead
            // (see README "Dual virtual camera"). VCAM2 falling through to ndi keeps both buses egressing.
            obs_output_id = "virtualcam_output";
        } else if (sink == "VCAM2") {
            obs_output_id = "ndi_output";
            const char *nm = obs_data_get_string(a, "ndi_name");
            obs_data_set_string(osettings, "ndi_name", (nm && *nm) ? nm : "SWITCHER VCAM2");
        } else if (sink == "NDI1" || sink == "NDI2") {
            obs_output_id = "ndi_output";
            const char *nm = obs_data_get_string(a, "ndi_name");
            obs_data_set_string(osettings, "ndi_name",
                                (nm && *nm) ? nm : (sink == "NDI1" ? "SWITCHER PGM1" : "SWITCHER PGM2"));
        }

        if (obs_output_id) {
            obs_output_t *output = obs_output_create(obs_output_id, sink.c_str(), osettings, nullptr);
            if (output) {
                obs_output_set_media(output, video, obs_get_audio());
                if (obs_output_start(output)) {
                    ctx->outputs[sink] = OutputSink{output};
                } else {
                    // NDI plugin absent / device busy: release and skip (graceful, no error to caller).
                    obs_output_release(output);
                }
            }
        }
        obs_data_release(osettings);
        obs_data_release(a);
    }
    if (arr) obs_data_array_release(arr);
    obs_data_release(req);
    return 0;
}

int engine_apply_multiview(engine_ctx *ctx, const char *layout_json) {
    if (!ctx || !layout_json) return 1;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);

    obs_data_t *req = obs_data_create_from_json(layout_json);
    if (!req) return 2;

    // Normalize to (rows, cols) + region list. New payloads carry grid+regions; legacy carry a flat
    // 16-cell array laid out over 4 columns (mirrors MultiviewLayoutNormalizer on the managed side).
    int rows = 4, cols = 4;
    obs_data_t *grid = obs_data_get_obj(req, "grid");
    if (grid) {
        rows = static_cast<int>(obs_data_get_int(grid, "rows"));
        cols = static_cast<int>(obs_data_get_int(grid, "cols"));
        obs_data_release(grid);
    }

    struct Region { int row, col, rspan, cspan; std::string content; };
    std::vector<Region> regions;
    obs_data_array_t *regs = obs_data_get_array(req, "regions");
    if (regs) {
        const size_t rn = obs_data_array_count(regs);
        for (size_t i = 0; i < rn; ++i) {
            obs_data_t *r = obs_data_array_item(regs, i);
            regions.push_back({static_cast<int>(obs_data_get_int(r, "row")),
                               static_cast<int>(obs_data_get_int(r, "col")),
                               std::max(1, static_cast<int>(obs_data_get_int(r, "row_span"))),
                               std::max(1, static_cast<int>(obs_data_get_int(r, "col_span"))),
                               obs_data_get_string(r, "content")});
            obs_data_release(r);
        }
        obs_data_array_release(regs);
    } else {
        // Legacy flat `cells` (array of strings) laid out over 4 columns, one 1x1 region per cell.
        const std::vector<std::string> cells = parse_string_array(layout_json, "cells");
        if (!cells.empty()) { cols = 4; rows = static_cast<int>((cells.size() + 3) / 4); }
        for (size_t i = 0; i < cells.size(); ++i) {
            regions.push_back({static_cast<int>(i) / 4, static_cast<int>(i) % 4, 1, 1, cells[i]});
        }
    }
    if (rows <= 0) rows = 4;
    if (cols <= 0) cols = 4;

    // Rebuild the multiview scene: each region maps its content token to a source, placed in its cell.
    clear_scene(ctx->mv_scene);
    const float cell_w = static_cast<float>(ctx->canvas_w) / static_cast<float>(cols);
    const float cell_h = static_cast<float>(ctx->canvas_h) / static_cast<float>(rows);
    for (const auto &rg : regions) {
        if (rg.content.empty() || to_upper(rg.content) == "EMPTY") continue;
        obs_source_t *src = resolve_target_source(ctx, rg.content);
        if (!src) continue;
        obs_sceneitem_t *item = obs_scene_add(ctx->mv_scene, src);
        if (!item) continue;
        struct vec2 pos, bounds;
        vec2_set(&pos, rg.col * cell_w, rg.row * cell_h);
        vec2_set(&bounds, rg.cspan * cell_w, rg.rspan * cell_h);
        obs_sceneitem_set_alignment(item, OBS_ALIGN_LEFT | OBS_ALIGN_TOP);
        obs_sceneitem_set_bounds_alignment(item, OBS_ALIGN_LEFT | OBS_ALIGN_TOP);
        obs_sceneitem_set_bounds_type(item, OBS_BOUNDS_SCALE_INNER);
        obs_sceneitem_set_bounds(item, &bounds);
        obs_sceneitem_set_pos(item, &pos);
    }

    obs_data_release(req);
    return 0;
}

// obs_display draw callback: render `target` fullscreen into the App window.
static void display_draw(void *param, uint32_t cx, uint32_t cy) {
    auto *disp = static_cast<DisplayOut *>(param);
    if (!disp->source || disp->base_w == 0 || disp->base_h == 0) return;

    gs_projection_push();
    gs_viewport_push();
    gs_set_viewport(0, 0, static_cast<int>(cx), static_cast<int>(cy));
    gs_ortho(0.0f, static_cast<float>(disp->base_w), 0.0f, static_cast<float>(disp->base_h),
             -100.0f, 100.0f);
    obs_source_video_render(disp->source);
    gs_viewport_pop();
    gs_projection_pop();
}

int engine_start_display(engine_ctx *ctx, const char *target, void *hwnd, int display_id) {
    if (!ctx || !target || !hwnd) return 1;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);

    obs_source_t *src = resolve_target_source(ctx, target);
    if (!src) return 2;

    // Replace any existing display for this target.
    engine_stop_display(ctx, target);

    DisplayOut &d = ctx->displays[target];
    d.source = src;
    d.base_w = ctx->canvas_w;
    d.base_h = ctx->canvas_h;

    struct gs_init_data gi = {};
#ifdef _WIN32
    gi.window.hwnd = hwnd;
#else
    gi.window.display = hwnd;  // non-Windows dev builds (opengl); shipping target is Windows/d3d11
#endif
    gi.cx = ctx->canvas_w;
    gi.cy = ctx->canvas_h;
    gi.format = GS_BGRA;
    gi.zsformat = GS_ZS_NONE;
    gi.num_backbuffers = 1;
    (void)display_id;  // physical display selection is done App-side by placing the HWND on that monitor

    d.display = obs_display_create(&gi, 0);
    if (!d.display) { ctx->displays.erase(target); return 3; }
    obs_display_add_draw_callback(d.display, display_draw, &d);
    return 0;
}

void engine_stop_display(engine_ctx *ctx, const char *target) {
    if (!ctx || !target) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    auto it = ctx->displays.find(target);
    if (it == ctx->displays.end()) return;
    if (it->second.display) {
        obs_display_remove_draw_callback(it->second.display, display_draw, &it->second);
        obs_display_destroy(it->second.display);
    }
    ctx->displays.erase(it);
}

// ---------------------------------------------------------------------------
// preview readback + state
// ---------------------------------------------------------------------------

void engine_set_tap(engine_ctx *ctx, const char *target, int enabled) {
    if (!ctx || !target) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    const std::string key = target;

    if (enabled) {
        video_t *video = resolve_target_video(ctx, key);
        if (!video) return;
        Tap &tap = ctx->taps[key];
        tap.target = key;
        tap.video = video;
        if (tap.connected) return;

        // One trampoline arg per target, kept alive for the connection's lifetime.
        TapCbArg *arg;
        auto ait = ctx->tap_args.find(key);
        if (ait == ctx->tap_args.end()) {
            arg = new TapCbArg{ctx, key};
            ctx->tap_args[key] = arg;
        } else {
            arg = ait->second;
        }

        struct video_scale_info conv = {};
        conv.format = VIDEO_FORMAT_BGRA;  // already BGRA canvas -> no color conversion
        conv.width = ctx->canvas_w;
        conv.height = ctx->canvas_h;
        conv.range = VIDEO_RANGE_FULL;
        conv.colorspace = VIDEO_CS_709;
        video_output_connect(video, &conv, on_raw_video_trampoline, arg);
        tap.connected = true;
    } else {
        auto it = ctx->taps.find(key);
        if (it == ctx->taps.end() || !it->second.connected) return;
        auto ait = ctx->tap_args.find(key);
        if (ait != ctx->tap_args.end() && it->second.video) {
            video_output_disconnect(it->second.video, on_raw_video_trampoline, ait->second);
        }
        it->second.connected = false;
    }
}

void engine_set_frame_cb(engine_ctx *ctx, engine_frame_cb cb, void *user) {
    if (!ctx) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    ctx->frame_cb = cb;
    ctx->frame_user = user;
}

void engine_set_state_cb(engine_ctx *ctx, engine_state_cb cb, void *user) {
    if (!ctx) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    ctx->state_cb = cb;
    ctx->state_user = user;
}
