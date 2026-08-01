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
//    The sink table is not a fixed set: the operator adds one webcam and up to three HDMI / NDI sinks
//    (VCAM1, HDMI1..HDMI3, NDI1..NDI3), so sink tokens are parsed into kind + ordinal rather than
//    matched one by one - see parse_sink_token and engine_apply_outputs. VCAM2/VCAM3 are still parsed
//    for tables written by builds that predate the one-webcam limit.
//
// BUILD/RUN: libobs links and runs on a Windows + OBS 31.0.x host only (see README.md). This file is not
// part of HybridSwitcher.sln and is not compiled by the managed CI (which injects FakeVideoEngine).

#include "engine.h"

#include "audio_out.h"
#include "ndi.h"

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cmath>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

#include <obs.h>
#include <obs-audio-controls.h>
#include <graphics/vec2.h>
#include <graphics/vec4.h>
#include <util/platform.h>

namespace {

constexpr int kBusCount = 2;
// Highest ordinal a sink token may carry: VCAM1..3 / HDMI1..3 / NDI1..3. This is a parsing bound, not
// the operator-facing ceiling - the managed side caps webcams at one (OutputCatalog.MaxWebcamSinks) and
// everything else at three, but VCAM2/VCAM3 must keep parsing so tables written by older builds still
// load and reach the NDI fallback below.
constexpr int kMaxSinksPerKind = 3;
constexpr uint64_t kTapMinIntervalNs = 33'000'000;  // ~30 fps upper bound on readback (spec step 7)

// A single preview readback tap. The target's source is rendered off-screen into a texrender each frame,
// copied to a staging surface, and mapped one frame later to hand BGRA pixels to the managed frame
// callback. This uses only public libobs graphics APIs (obs_add_main_render_callback + gs_texrender +
// gs_stagesurface), because obs_view/video_output_connect never marks a custom view's mix raw-active
// (start_raw_video, which does, is not exported), so a bare view tap yields no frames.
struct Tap {
    std::string target;
    obs_source_t *source = nullptr;   // resolved at enable; bus transition/scene, or the pooled SRC source
    bool use_source_size = false;     // SRC:<id> reads back at the source's own size; buses use the canvas
    bool inc_showing = false;         // we called obs_source_inc_showing (async devices need it to capture)
    gs_texrender_t *texrender = nullptr;
    gs_stagesurf_t *stage = nullptr;
    uint32_t sw = 0, sh = 0;          // current stage dimensions
    bool have_staged = false;         // a frame is staged, ready to map on the next render tick
    uint64_t last_ns = 0;             // throttle clock (graphics-thread only)
};

// A live output sink (virtualcam / NDI) bound to a bus program video.
struct OutputSink {
    obs_output_t *output = nullptr;
    int bus = 0;  // which program bus this sink carries, for engine_get_output_status
};

// A fullscreen obs_display rendering a target source to an App window.
struct DisplayOut {
    obs_display_t *display = nullptr;
    obs_source_t *source = nullptr;  // borrowed: transition / preview scene / multiview scene source
    uint32_t base_w = 0;
    uint32_t base_h = 0;
    engine_ctx *ctx = nullptr;       // for the multiview overlay; null for plain PGM/PVW displays
    bool overlay = false;            // draw the multiview cell frames + audio meters over the composite
};

// Per-source audio level, fed by an obs_volmeter on the audio thread and read by the multiview overlay
// on the graphics thread. Held through a shared_ptr so a published overlay cell can outlive the pooled
// source it was bound to: engine_remove_source only stops the volmeter, and the frozen level then ages
// out on its own (kMeterStaleNs) instead of leaving a stuck bar on screen.
struct Meter {
    obs_volmeter_t *volmeter = nullptr;  // owned; stopped in stop_meter. Engine threads only.
    std::atomic<float> magnitude{-INFINITY};  // dBFS, loudest channel
    std::atomic<float> peak{-INFINITY};
    std::atomic<uint64_t> updated_ns{0};
};
using MeterRef = std::shared_ptr<Meter>;

// obs_volmeter_add_callback target. Runs on the audio thread; only stores into the atomics above.
void meter_levels_cb(void *param, const float magnitude[MAX_AUDIO_CHANNELS],
                     const float peak[MAX_AUDIO_CHANNELS], const float input_peak[MAX_AUDIO_CHANNELS]) {
    auto *m = static_cast<Meter *>(param);
    (void)input_peak;  // the bar shows the post-fader level, matching what the source sends on
    float mag = -INFINITY, pk = -INFINITY;
    for (int ch = 0; ch < MAX_AUDIO_CHANNELS; ++ch) {
        if (magnitude[ch] > mag) mag = magnitude[ch];
        if (peak[ch] > pk) pk = peak[ch];
    }
    m->magnitude.store(mag, std::memory_order_relaxed);
    m->peak.store(pk, std::memory_order_relaxed);
    m->updated_ns.store(os_gettime_ns(), std::memory_order_release);
}

// One multiview region, in canvas pixels, as the overlay needs it: a frame to draw and (optionally) an
// audio level to draw beside it.
struct MvCell {
    float x = 0.0f, y = 0.0f, w = 0.0f, h = 0.0f;
    bool occupied = false;  // false for EMPTY/unresolvable regions - framed dim, no meter
    MeterRef meter;         // SRC:<id> regions bind their pooled source's meter directly
    int bus_meter = -1;     // PGM/PVW regions index MvOverlay::bus_meters instead (the bus restaffs)
};

// Overlay state shared between the engine threads (which publish) and the graphics thread (which draws).
// Its own small mutex, never held across a libobs call, so the graphics thread can never be blocked by a
// thread that is itself waiting on the graphics thread (which is what taking ctx->lock here would risk).
struct MvOverlay {
    std::mutex lock;
    std::vector<MvCell> cells;
    MeterRef bus_meters[kBusCount * 2];  // [bus * 2 + (0 = program, 1 = preview)]
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
    std::map<std::string, MeterRef> meters;         // audio level per pooled source id (audio sources only)

    // multiview composite (its own scene + view + video)
    obs_scene_t *mv_scene = nullptr;
    obs_view_t *mv_view = nullptr;
    video_t *mv_video = nullptr;
    MvOverlay overlay;  // cell frames + meters drawn over the MULTIVIEW display

    std::map<std::string, Tap> taps;               // keyed by target token; mutated only under obs graphics
    std::map<std::string, OutputSink> outputs;     // keyed by canonical sink token (VCAM1/HDMI2/NDI3/...)
    std::map<std::string, DisplayOut> displays;    // keyed by target token (PGM1/PVW2/MULTIVIEW/HDMI2/SRC:<id>)
    bool render_cb_added = false;                  // whether taps_render is registered on the main render

    std::atomic<engine_frame_cb> frame_cb{nullptr};  // read lock-free by the main-render tap callback
    std::atomic<void *> frame_user{nullptr};
    engine_state_cb state_cb = nullptr;
    void *state_user = nullptr;

    uint32_t canvas_w = 1920;
    uint32_t canvas_h = 1080;

    FILE *log_file = nullptr;
    bool started = false;

    std::string last_enum_json;  // backing store for engine_enumerate_devices' returned pointer
    std::string last_audio_devices_json;
    std::string last_output_status_json;
    std::string last_multiview_json;  // last applied layout, re-applied after a TAKE swaps PVW scenes
};

// Forward declaration: engine_take re-applies the cached multiview layout, because a region whose
// content is PVW1/PVW2 holds a scene item pointing at the preview scene the TAKE just swapped out.
//
// NOTE(tally frames): drawing the red/green tally outline into the *native* multiview was tried by
// rebuilding this scene (with a color_source behind each tallied region) on every bus change. Doing
// scene teardown/rebuild plus private-source create/destroy at bus-change frequency wedges the libobs
// graphics thread - every tap stops delivering frames and all previews freeze, reproducibly, within a
// dozen takes. The operator window frames its own multiview cells instead (MultiviewTally); giving the
// composited multiview output the same frames needs a design that never mutates the scene graph on the
// hot path (retained per-region colour sources whose settings/visibility are updated in place).
// The cell frames / audio meters this file *does* draw take the other road: they are painted by
// display_draw straight over the composite (draw_mv_overlay), so the scene graph is never touched at
// all. Tally colouring could ride on the same path - it only needs the on-air flag per cell.
static int apply_multiview_locked(engine_ctx *ctx, const char *layout_json);

// Rebinds MvOverlay::bus_meters after a bus's program/preview source changes. Must hold ctx->lock.
static void sync_bus_meters_locked(engine_ctx *ctx);

// Re-applies the cached layout. No-op before the first engine_apply_multiview. Must hold ctx->lock.
static void refresh_multiview_locked(engine_ctx *ctx) {
    if (ctx->last_multiview_json.empty()) return;
    const std::string layout = ctx->last_multiview_json;  // copy: apply_multiview_locked rewrites it
    apply_multiview_locked(ctx, layout.c_str());
}

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
    // NDI: prefer switcher-engine's own receiver (built on the NDI SDK, no plugin required); fall back
    // to DistroAV's ndi_source when this build has no NDI support but that plugin happens to be present.
    if (t == "NDI") return ndi_is_available() ? ndi_source_id() : "ndi_source";
    if (t == "IMAGE") return "image_source";
    if (t == "HTML" || t == "BROWSER") return "browser_source";                // obs-browser (CEF)
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

        // WebcamConfig.Format is "<W>x<H>" or "<W>x<H>@<FPS>". win-dshow only honors `resolution` /
        // `frame_interval` when res_type is Custom (1) - with the default Preferred (0) it opens the
        // device's *first* advertised media type, which on many UVC cameras is an 8 fps MJPEG/YUY2 mode
        // (observed: BUFFALO BSWHD06M opening at 1280x720@8). Setting res_type explicitly is what makes
        // the requested mode take effect. frame_interval is in 100 ns units; 0 = FPS_HIGHEST.
        const char *fmt = in ? obs_data_get_string(in, "format") : "";
        if (fmt && *fmt) {
            std::string spec = fmt;
            long long interval = 0;  // FPS_HIGHEST
            const size_t at = spec.find('@');
            if (at != std::string::npos) {
                const double fps = std::atof(spec.c_str() + at + 1);
                if (fps > 0.0) interval = static_cast<long long>(10'000'000.0 / fps + 0.5);
                spec = spec.substr(0, at);
            }
            obs_data_set_int(out, "res_type", 1);  // ResType_Custom
            obs_data_set_string(out, "resolution", spec.c_str());
            obs_data_set_int(out, "frame_interval", interval);
        } else {
            obs_data_set_int(out, "res_type", 0);  // ResType_Preferred (device default)
        }
    } else if (obs_source_id == "ffmpeg_source") {
        const char *url = in ? obs_data_get_string(in, "url") : "";
        std::string input = (url && *url) ? url : "";
        const long long latency_ms = in ? obs_data_get_int(in, "latency_ms") : 0;

        // SrtConfig.latency_ms can only reach libsrt through the URL query: ffmpeg_source has no latency
        // setting of its own. FFmpeg's srt protocol takes `latency` in MICROseconds, so the operator's
        // 20-50 ms (multiview-output-revision.md §2.7) becomes 20000-50000 here. Previously the value was
        // read and dropped, leaving libsrt's 120 ms default in place. An explicit latency= already in the
        // URL is the operator's own choice and is left alone.
        if (latency_ms > 0 && input.rfind("srt://", 0) == 0 && input.find("latency=") == std::string::npos) {
            input += (input.find('?') == std::string::npos) ? '?' : '&';
            input += "latency=" + std::to_string(latency_ms * 1000);
        }

        if (!input.empty()) obs_data_set_string(out, "input", input.c_str());
        obs_data_set_bool(out, "is_local_file", false);
        obs_data_set_bool(out, "restart_on_activate", false);
        if (latency_ms > 0) obs_data_set_int(out, "reconnect_delay_sec", 1);
    } else if (obs_source_id == "ndi_source" || obs_source_id == "switcher_ndi") {
        // NdiConfig.source_name is the full NDI name ("MACHINE (Source)"), which is what both our own
        // receiver and DistroAV connect by; the two plugins just spell the setting key differently.
        const char *name = in ? obs_data_get_string(in, "source_name") : "";
        if (name && *name) {
            obs_data_set_string(out, obs_source_id == "switcher_ndi" ? "ndi_name" : "ndi_source_name", name);
        }
    } else if (obs_source_id == "image_source") {
        // ImageConfig.file_path; "url" is accepted too for the legacy {"url":..} payload.
        const char *file = in ? obs_data_get_string(in, "file_path") : "";
        if (!file || !*file) file = in ? obs_data_get_string(in, "url") : "";
        if (file && *file) obs_data_set_string(out, "file", file);
        obs_data_set_bool(out, "unload", false);
    } else if (obs_source_id == "browser_source") {
        // HtmlConfig -> obs-browser settings. A local .html is passed through `local_file`, not `url`;
        // obs-browser only reads `local_file` when `is_local_file` is set (and vice versa).
        const bool is_local = in && obs_data_get_bool(in, "is_local_file");
        const char *url = in ? obs_data_get_string(in, "url") : "";
        obs_data_set_bool(out, "is_local_file", is_local);
        if (url && *url) obs_data_set_string(out, is_local ? "local_file" : "url", url);

        const long long w = in ? obs_data_get_int(in, "width") : 0;
        const long long h = in ? obs_data_get_int(in, "height") : 0;
        obs_data_set_int(out, "width", w > 0 ? w : 1920);
        obs_data_set_int(out, "height", h > 0 ? h : 1080);

        const long long fps = in ? obs_data_get_int(in, "fps") : 0;
        if (fps > 0) {
            obs_data_set_bool(out, "fps_custom", true);
            obs_data_set_int(out, "fps", fps);
        }

        const char *css = in ? obs_data_get_string(in, "css") : "";
        if (css && *css) obs_data_set_string(out, "css", css);

        // Keep the page alive when it is not on a bus: the switcher taps every source for its preview
        // tile, so shutting the browser down while "not visible" would blank that tile.
        obs_data_set_bool(out, "shutdown", false);
        obs_data_set_bool(out, "restart_when_active", false);
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

// The kind half of an output sink token. The output table is built by the operator (one webcam, up to
// three HDMI and three NDI, six in all), so nothing here may assume a fixed set of sinks.
enum class SinkKind { None, Vcam, Hdmi, Ndi };

// A parsed sink token: a kind plus its 1-based ordinal within that kind.
struct SinkToken {
    SinkKind kind = SinkKind::None;
    int ordinal = 0;
};

// "VCAM2" -> {Vcam,2}, "HDMI3" -> {Hdmi,3}. An ordinal-less token means the first sink of its kind, which
// is how the bare "HDMI" an old runtime-config.json (single-display era) still carries reads as HDMI1.
// Anything else - a kind this build does not know, an ordinal past kMaxSinksPerKind, junk - comes back as
// {None,0} so the caller can skip that one row instead of failing the whole table. VCAM2/VCAM3 still parse
// even though the operator can no longer add them: an older build's table has to load, not be rejected.
SinkToken parse_sink_token(const std::string &token) {
    const std::string t = to_upper(token);
    SinkToken out;
    size_t prefix = 0;
    if (t.rfind("VCAM", 0) == 0) { out.kind = SinkKind::Vcam; prefix = 4; }
    else if (t.rfind("HDMI", 0) == 0) { out.kind = SinkKind::Hdmi; prefix = 4; }
    else if (t.rfind("NDI", 0) == 0) { out.kind = SinkKind::Ndi; prefix = 3; }
    else return {};

    const std::string tail = t.substr(prefix);
    if (tail.empty()) {
        out.ordinal = 1;  // legacy "HDMI"
    } else if (tail.size() == 1 && tail[0] >= '1' && tail[0] <= static_cast<char>('0' + kMaxSinksPerKind)) {
        out.ordinal = tail[0] - '0';
    } else {
        return {};
    }
    return out;
}

// Canonical wire token for a parsed sink ("HDMI" normalises to "HDMI1"), matching OutputCatalog.TokenOf
// on the managed side. This is the key ctx->outputs uses, so one sink is one entry however it was spelled.
std::string sink_token_text(SinkToken sink) {
    switch (sink.kind) {
        case SinkKind::Vcam: return "VCAM" + std::to_string(sink.ordinal);
        case SinkKind::Hdmi: return "HDMI" + std::to_string(sink.ordinal);
        case SinkKind::Ndi:  return "NDI" + std::to_string(sink.ordinal);
        default: return std::string();
    }
}

// Sender name a sink that egresses over NDI announces itself as when the assignment names none. NDI<n>
// is named for the bus it was created to carry ("SWITCHER PGM<n>", matching OutputDefaults on the managed
// side); a VCAM<n> riding the NDI fallback keeps its own name so a receiver can tell the two apart.
std::string default_ndi_name(SinkToken sink) {
    return (sink.kind == SinkKind::Vcam ? "SWITCHER VCAM" : "SWITCHER PGM") + std::to_string(sink.ordinal);
}

// Which bus a sink carries, from the last applied output table. Falls back to bus 0 when the sink is not
// in the table - the App can open a projector for a sink before (or without) engine_apply_outputs, and a
// display showing PGM1 beats a display showing nothing.
int sink_bus_or_default(engine_ctx *ctx, const std::string &canonical_token) {
    auto it = ctx->outputs.find(canonical_token);
    return (it != ctx->outputs.end()) ? it->second.bus : 0;
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

    // HDMI<n> renders the program of whichever bus that sink carries. The App addresses each HDMI
    // projector by its sink token rather than by "PGM1"/"PGM2" precisely so two HDMI sinks on the *same*
    // bus still land on two distinct ctx->displays keys - keying by bus would let the second projector
    // replace the first.
    const SinkToken sink = parse_sink_token(t);
    if (sink.kind == SinkKind::Hdmi) {
        return ctx->buses[sink_bus_or_default(ctx, sink_token_text(sink))].transition;
    }
    return nullptr;
}

// ctx->displays key for a target token: sink tokens are canonicalised so a projector opened with the
// legacy "HDMI" addresses the same record as the output table's "HDMI1" (which is what lets
// engine_get_output_status find it); every other target is keyed exactly as the caller spelled it.
std::string display_key(const std::string &target) {
    const SinkToken sink = parse_sink_token(target);
    return sink.kind == SinkKind::None ? target : sink_token_text(sink);
}

// Re-point everything that caches a *scene object* for one bus after engine_take swapped
// program_scene/preview_scene. Taps and obs_display draw callbacks resolved their obs_source_t once, at
// enable time, so without this the PVW1/PVW2 readback (multiview cells, UI preview) and any PVW display
// keep rendering the scene that TAKE just promoted to program - i.e. PVW shows PGM's pixels.
// Mutating them inside obs_enter_graphics parks the graphics thread, so taps_render / display_draw can
// never observe a half-updated pointer. Must be called while holding ctx->lock.
void rebind_bus_targets_locked(engine_ctx *ctx, int bus) {
    if (bus < 0 || bus >= kBusCount) return;
    const std::string pvw_key = (bus == 0) ? "PVW1" : "PVW2";
    const std::string pgm_key = (bus == 0) ? "PGM1" : "PGM2";
    obs_source_t *pvw_src = obs_scene_get_source(ctx->buses[bus].preview_scene);
    obs_source_t *pgm_src = ctx->buses[bus].transition;  // unchanged by TAKE, but kept in sync for clarity

    obs_enter_graphics();
    auto tap_it = ctx->taps.find(pvw_key);
    if (tap_it != ctx->taps.end() && tap_it->second.source != pvw_src) {
        tap_it->second.source = pvw_src;
        tap_it->second.have_staged = false;  // the staged surface belongs to the old scene
    }
    auto disp_it = ctx->displays.find(pvw_key);
    if (disp_it != ctx->displays.end()) disp_it->second.source = pvw_src;
    disp_it = ctx->displays.find(pgm_key);
    if (disp_it != ctx->displays.end()) disp_it->second.source = pgm_src;
    obs_leave_graphics();
}

// Release a tap's GPU objects. MUST run inside an obs graphics context (obs_enter_graphics / the render
// thread), which serializes with taps_render so the objects are never freed mid-use.
void tap_free_gpu(Tap &t) {
    if (t.stage) { gs_stagesurface_destroy(t.stage); t.stage = nullptr; }
    if (t.texrender) { gs_texrender_destroy(t.texrender); t.texrender = nullptr; }
    t.have_staged = false;
    t.sw = t.sh = 0;
}

// obs_add_main_render_callback: runs every frame on the graphics thread inside a live gs context (from
// render_main_texture). For each active tap it maps the surface staged last tick (1-frame latency avoids
// a GPU stall), then renders the target source off-screen and stages it for next tick. ctx->taps is only
// mutated under obs_enter_graphics, which parks the graphics thread, so iterating here needs no extra lock.
void taps_render(void *param, uint32_t, uint32_t) {
    auto *ctx = static_cast<engine_ctx *>(param);
    engine_frame_cb cb = ctx->frame_cb.load(std::memory_order_acquire);
    void *user = ctx->frame_user.load(std::memory_order_acquire);
    const uint64_t now = os_gettime_ns();

    for (auto &[key, t] : ctx->taps) {
        if (!t.source) continue;

        // 1) Emit the frame staged on the previous tick.
        if (t.have_staged && t.stage && cb) {
            uint8_t *data = nullptr;
            uint32_t linesize = 0;
            if (gs_stagesurface_map(t.stage, &data, &linesize)) {
                cb(user, t.target.c_str(), data, static_cast<int>(t.sw), static_cast<int>(t.sh),
                   static_cast<int>(linesize));
                gs_stagesurface_unmap(t.stage);
            }
            t.have_staged = false;
        }

        // 2) Throttle the (re)staging to ~kTapMinIntervalNs.
        if (t.last_ns != 0 && now - t.last_ns < kTapMinIntervalNs) continue;

        uint32_t w = ctx->canvas_w, h = ctx->canvas_h;
        if (t.use_source_size) {
            w = obs_source_get_width(t.source);
            h = obs_source_get_height(t.source);
            if (w == 0 || h == 0) continue;  // async source hasn't delivered a frame yet
        }

        if (!t.texrender) t.texrender = gs_texrender_create(GS_BGRA, GS_ZS_NONE);
        if (!t.stage || t.sw != w || t.sh != h) {
            if (t.stage) gs_stagesurface_destroy(t.stage);
            t.stage = gs_stagesurface_create(w, h, GS_BGRA);
            t.sw = w;
            t.sh = h;
            t.have_staged = false;
        }
        if (!t.texrender || !t.stage) continue;

        gs_texrender_reset(t.texrender);
        if (gs_texrender_begin(t.texrender, w, h)) {
            struct vec4 clear;
            vec4_zero(&clear);
            gs_clear(GS_CLEAR_COLOR, &clear, 0.0f, 0);
            gs_ortho(0.0f, static_cast<float>(w), 0.0f, static_cast<float>(h), -100.0f, 100.0f);
            obs_source_video_render(t.source);
            gs_texrender_end(t.texrender);
            gs_stage_texture(t.stage, gs_texrender_get_texture(t.texrender));
            t.have_staged = true;
            t.last_ns = now;
        }
    }
}

// ---------------------------------------------------------------------------
// per-source audio meters (feed the multiview overlay bars)
// ---------------------------------------------------------------------------

// Give a pooled source its own volmeter, if it carries audio at all (scenes - i.e. MIX sources - do not
// push audio through obs_source_output_audio, so they never report a level and get no bar). Idempotent.
// Must be called while holding ctx->lock.
void attach_meter_locked(engine_ctx *ctx, const std::string &id, obs_source_t *src) {
    if (!src || ctx->meters.count(id)) return;
    if ((obs_source_get_output_flags(src) & OBS_SOURCE_AUDIO) == 0) return;

    obs_volmeter_t *vm = obs_volmeter_create(OBS_FADER_LOG);
    if (!vm) return;
    auto m = std::make_shared<Meter>();
    m->volmeter = vm;
    obs_volmeter_add_callback(vm, meter_levels_cb, m.get());
    if (!obs_volmeter_attach_source(vm, src)) {
        obs_volmeter_remove_callback(vm, meter_levels_cb, m.get());
        obs_volmeter_destroy(vm);
        return;
    }
    ctx->meters[id] = std::move(m);
}

// Stop feeding a meter. obs_volmeter_remove_callback takes the volmeter's callback mutex, so once it
// returns meter_levels_cb is neither running nor callable and the Meter can safely outlive us in an
// already-published overlay cell.
void stop_meter(const MeterRef &m) {
    if (!m || !m->volmeter) return;
    obs_volmeter_remove_callback(m->volmeter, meter_levels_cb, m.get());
    obs_volmeter_destroy(m->volmeter);  // detaches the source
    m->volmeter = nullptr;
}

// Must be called while holding ctx->lock. Returns null for ids with no audio (or no such source).
MeterRef find_meter_locked(engine_ctx *ctx, const std::string &id) {
    if (id.empty()) return nullptr;
    auto it = ctx->meters.find(id);
    return it == ctx->meters.end() ? nullptr : it->second;
}

// ---------------------------------------------------------------------------
// multiview overlay drawing (graphics thread)
// ---------------------------------------------------------------------------

constexpr float kMeterFloorDb = -60.0f;   // bottom of the bar
constexpr float kMeterWarnDb = -20.0f;    // green -> yellow
constexpr float kMeterHotDb = -9.0f;      // yellow -> red
constexpr uint64_t kMeterStaleNs = 250'000'000;  // no audio for this long reads as silence

// gs_effect_set_color takes 0xAABBGGRR (vec4_from_rgba reads the little-endian bytes as R,G,B,A).
constexpr uint32_t rgba(uint8_t r, uint8_t g, uint8_t b, uint8_t a) {
    return static_cast<uint32_t>(r) | (static_cast<uint32_t>(g) << 8) |
           (static_cast<uint32_t>(b) << 16) | (static_cast<uint32_t>(a) << 24);
}

constexpr uint32_t kFrameColor = rgba(0xC8, 0xC8, 0xC8, 0xFF);       // occupied cell frame
constexpr uint32_t kFrameEmptyColor = rgba(0x50, 0x50, 0x50, 0xFF);  // empty cell frame
constexpr uint32_t kMeterTrackColor = rgba(0x0A, 0x0A, 0x0A, 0xB4);
constexpr uint32_t kMeterOkColor = rgba(0x3C, 0xC8, 0x50, 0xFF);
constexpr uint32_t kMeterWarnColor = rgba(0xE0, 0xC0, 0x30, 0xFF);
constexpr uint32_t kMeterHotColor = rgba(0xE0, 0x3C, 0x32, 0xFF);
constexpr uint32_t kMeterPeakColor = rgba(0xFF, 0xFF, 0xFF, 0xE0);

// Fill an axis-aligned rectangle in canvas coordinates (y grows downward, matching display_draw's ortho).
void fill_rect(float x, float y, float w, float h, uint32_t color) {
    if (w < 1.0f || h < 1.0f) return;
    gs_effect_t *solid = obs_get_base_effect(OBS_EFFECT_SOLID);
    if (!solid) return;
    gs_eparam_t *param = gs_effect_get_param_by_name(solid, "color");
    gs_effect_set_color(param, color);
    gs_matrix_push();
    gs_matrix_identity();
    gs_matrix_translate3f(x, y, 0.0f);
    while (gs_effect_loop(solid, "Solid")) {
        gs_draw_sprite(nullptr, 0, static_cast<uint32_t>(w), static_cast<uint32_t>(h));
    }
    gs_matrix_pop();
}

// Four bars just inside the cell rect, so neighbouring frames sit side by side instead of overlapping.
void stroke_rect(float x, float y, float w, float h, float t, uint32_t color) {
    if (w <= 2.0f * t || h <= 2.0f * t) return;
    fill_rect(x, y, w, t, color);              // top
    fill_rect(x, y + h - t, w, t, color);      // bottom
    fill_rect(x, y + t, t, h - 2.0f * t, color);              // left
    fill_rect(x + w - t, y + t, t, h - 2.0f * t, color);      // right
}

// dBFS -> 0..1 of the bar's height. -inf / NaN land on 0.
float db_to_frac(float db) {
    if (!(db > kMeterFloorDb)) return 0.0f;
    if (db > 0.0f) db = 0.0f;
    return (db - kMeterFloorDb) / -kMeterFloorDb;
}

// A vertical level bar down the right-hand inside edge of a cell: dark track, level painted bottom-up in
// green/yellow/red bands, plus a thin peak line.
void draw_meter(const Meter *meter, float x, float y, float w, float h, float thickness) {
    if (!meter || w < 2.0f || h < 4.0f) return;

    // A source that has never delivered a sample (a video-only webcam, say) gets no bar at all - an
    // always-empty track next to every camera would be noise, not information.
    const uint64_t updated = meter->updated_ns.load(std::memory_order_acquire);
    if (updated == 0) return;

    float mag = meter->magnitude.load(std::memory_order_relaxed);
    float peak = meter->peak.load(std::memory_order_relaxed);
    if (os_gettime_ns() - updated > kMeterStaleNs) {
        mag = -INFINITY;  // device gone / stream stalled: show an empty track rather than a frozen bar
        peak = -INFINITY;
    }

    fill_rect(x, y, w, h, kMeterTrackColor);

    const struct { float top_db; uint32_t color; } bands[] = {
        {kMeterWarnDb, kMeterOkColor}, {kMeterHotDb, kMeterWarnColor}, {0.0f, kMeterHotColor}};
    float lo = kMeterFloorDb;
    for (const auto &band : bands) {
        const float hi = std::min(mag, band.top_db);
        if (hi > lo) {
            const float y0 = y + h - db_to_frac(hi) * h;
            const float y1 = y + h - db_to_frac(lo) * h;
            fill_rect(x, y0, w, y1 - y0, band.color);
        }
        lo = band.top_db;
        if (mag <= lo) break;
    }

    const float peak_frac = db_to_frac(peak);
    if (peak_frac > 0.0f) {
        const float py = y + h - peak_frac * h;
        fill_rect(x, std::min(py, y + h - thickness), w, thickness, kMeterPeakColor);
    }
}

// Draws the cell frames + meters over the already-composited multiview. Runs on the graphics thread from
// display_draw, inside the same viewport/ortho the composite was rendered with (canvas coordinates).
void draw_mv_overlay(engine_ctx *ctx, float thickness) {
    gs_blend_state_push();
    gs_reset_blend_state();  // the track/peak colours are alpha-blended; don't inherit the caller's state

    std::lock_guard<std::mutex> guard(ctx->overlay.lock);
    for (const auto &cell : ctx->overlay.cells) {
        if (cell.w < 2.0f * thickness || cell.h < 2.0f * thickness) continue;
        stroke_rect(cell.x, cell.y, cell.w, cell.h, thickness,
                    cell.occupied ? kFrameColor : kFrameEmptyColor);
        if (!cell.occupied) continue;

        // SRC cells carry their meter; PGM/PVW cells read whatever source that bus is staffed with now.
        const Meter *meter = cell.meter.get();
        if (!meter && cell.bus_meter >= 0) meter = ctx->overlay.bus_meters[cell.bus_meter].get();
        if (!meter) continue;

        const float pad = thickness * 2.0f;
        const float bar_w = std::max(6.0f, cell.w * 0.02f);
        const float bar_x = cell.x + cell.w - thickness - pad - bar_w;
        const float bar_y = cell.y + thickness + pad;
        const float bar_h = cell.h - 2.0f * (thickness + pad);
        draw_meter(meter, bar_x, bar_y, bar_w, bar_h, thickness);
    }

    gs_blend_state_pop();
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
    // provides canvas_width/height/fps/module_path plus the path knobs (data_path/module_bin_path/
    // module_data_path/graphics_module); each is read from options_json if present, else from the
    // environment (Switcher.App fills them in from the located OBS install - see README).
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
    // libobs' obs_find_data_file() concatenates the registered data path and the file name WITHOUT
    // inserting a separator (see check_path in obs-internal.h: dstr_copy(path); dstr_cat(file)). Its
    // own built-in paths always end in '/', so a data path we register MUST end in a separator too, or
    // "<dir>/libobsdefault.effect" is searched and graphics init fails. Normalize to guarantee one.
    auto with_trailing_sep = [](std::string p) -> std::string {
        if (!p.empty() && p.back() != '/' && p.back() != '\\') p.push_back('/');
        return p;
    };
    const std::string data_path = with_trailing_sep(pick("data_path", "SWITCHER_OBS_DATA_PATH"));
    const std::string module_bin = pick("module_bin_path", "SWITCHER_OBS_MODULE_BIN");
    const std::string module_data = pick("module_data_path", "SWITCHER_OBS_MODULE_DATA");
    std::string graphics_module = pick("graphics_module", "SWITCHER_OBS_GRAPHICS_MODULE");
    const std::string module_path = obs_data_get_string(opt, "module_path");  // EngineOptions.ModulePath
    if (graphics_module.empty()) graphics_module = "libobs-d3d11";  // d3d11 for shipping; opengl on dev

    if (!obs_startup("en-US", nullptr, nullptr)) {
        obs_data_release(opt);
        base_set_log_handler(nullptr, nullptr);
        g_log_file = nullptr;
        if (ctx->log_file) std::fclose(ctx->log_file);
        delete ctx;
        return nullptr;
    }

    // CORE data path MUST be registered before obs_reset_video, otherwise libobs cannot find its built-in
    // effects (default.effect etc.) and graphics init fails - this was the L-001 scaffold's blocker.
    if (!data_path.empty()) obs_add_data_path(data_path.c_str());
    blog(LOG_INFO, "switcher-engine: data_path='%s' graphics_module='%s'", data_path.c_str(), graphics_module.c_str());

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
    const int reset_rc = obs_reset_video(&ovi);
    if (reset_rc != OBS_VIDEO_SUCCESS) {
        blog(LOG_ERROR, "switcher-engine: obs_reset_video failed (code %d)", reset_rc);
        obs_data_release(opt);
        obs_shutdown();
        base_set_log_handler(nullptr, nullptr);
        g_log_file = nullptr;
        if (ctx->log_file) std::fclose(ctx->log_file);
        delete ctx;
        return nullptr;
    }

    struct obs_audio_info oai = {};
    oai.samples_per_sec = 48000;
    oai.speakers = SPEAKERS_STEREO;
    obs_reset_audio(&oai);

    // Load ONLY the capture/source/transition modules the switcher needs, by name. An installed OBS's
    // obs-plugins folder also contains Qt/frontend plugins (frontend-tools, aja-output-ui,
    // decklink-output-ui, obs-websocket, obs-browser...) whose module_load constructs Qt widgets; loaded
    // via obs_load_all_modules() in a host process with no QApplication they abort the whole process
    // ("Must construct a QApplication before a QWidget"). Opening a curated allow-list of headless-safe
    // plugins keeps sources working (webcam/SRT/media/transitions) without ever touching the UI plugins.
    // obs-transitions is required: the per-bus M/E TAKE uses the "fade_transition" source it registers.
    const char *user_plugin_root = env_or_null("APPDATA");
    static const char *kSourceModules[] = {
        "win-dshow", "win-capture", "win-wasapi", "obs-ffmpeg", "image-source",
        "obs-text", "text-freetype2", "obs-transitions", "obs-filters", "vlc-video",
        "obs-x264", "obs-outputs", "rtmp-services",
        // obs-browser (HTML sources). Headless-safe on Windows: ENABLE_BROWSER_QT_LOOP is a macOS-only
        // build flag, so module_load only registers the source type and calls obs_frontend_add_event_callback
        // (a documented no-op without a frontend). CEF itself starts lazily on the plugin's own manager
        // thread when the first browser_source is created - no QApplication is ever needed.
        "obs-browser",
        "distroav", "obs-ndi",  // NDI (DistroAV) - optional; absent in a stock OBS install
    };
    if (!module_bin.empty()) {
        const std::string bin = with_trailing_sep(module_bin);
        const std::string mdata = with_trailing_sep(module_data.empty() ? module_bin : module_data);
        int loaded = 0;
        for (const char *name : kSourceModules) {
            obs_module_t *mod = nullptr;
            std::string dll = bin + name + ".dll";
            std::string ddir = mdata + name;
            bool ok = obs_open_module(&mod, dll.c_str(), ddir.c_str()) == MODULE_SUCCESS && obs_init_module(mod);

            // Third-party plugins (notably DistroAV/obs-ndi) often install per-user rather than into the
            // OBS program folder, at %APPDATA%\obs-studio\plugins\<name>\bin\64bit\<name>.dll with data
            // alongside. Try that layout too before declaring the plugin absent.
            if (!ok && user_plugin_root) {
                mod = nullptr;
                dll = std::string(user_plugin_root) + "\\obs-studio\\plugins\\" + name + "\\bin\\64bit\\" + name + ".dll";
                ddir = std::string(user_plugin_root) + "\\obs-studio\\plugins\\" + name + "\\data";
                ok = obs_open_module(&mod, dll.c_str(), ddir.c_str()) == MODULE_SUCCESS && obs_init_module(mod);
            }

            if (ok) ++loaded;
        }
        blog(LOG_INFO, "switcher-engine: loaded %d/%d source modules from '%s'",
             loaded, static_cast<int>(sizeof(kSourceModules) / sizeof(kSourceModules[0])), bin.c_str());
    }
    // A caller-supplied custom module dir (EngineOptions.ModulePath) is trusted to hold only non-UI
    // source plugins, so it is still loaded wholesale.
    if (!module_path.empty()) {
        obs_add_module_path(module_path.c_str(), module_path.c_str());
        obs_load_all_modules();
    }
    obs_post_load_modules();
    obs_data_release(opt);

    // Native NDI receiver. Registered after the modules so that if DistroAV *is* installed both source
    // types exist and map_source_type can prefer ours.
    ndi_register_source();

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

    // Phase 1: stop readback. Remove the main-render callback first (it blocks until any in-flight
    // taps_render iteration finishes and prevents further calls), then free each tap's GPU objects inside
    // a graphics context and dec_showing the sources we showed. Sources themselves are released below,
    // after the taps that reference them are gone.
    {
        std::lock_guard<std::recursive_mutex> guard(ctx->lock);
        if (ctx->render_cb_added) {
            obs_remove_main_render_callback(taps_render, ctx);
            ctx->render_cb_added = false;
        }
        obs_enter_graphics();
        for (auto &[target, tap] : ctx->taps) tap_free_gpu(tap);
        obs_leave_graphics();
        for (auto &[target, tap] : ctx->taps)
            if (tap.inc_showing && tap.source) obs_source_dec_showing(tap.source);
        ctx->taps.clear();
    }

    {
        std::lock_guard<std::recursive_mutex> guard(ctx->lock);

        // Stop + release outputs.
        for (auto &[sink, out] : ctx->outputs) {
            if (out.output) {
                obs_output_stop(out.output);
                obs_output_release(out.output);
            }
        }
        ctx->outputs.clear();

        // Destroy displays. Their draw callbacks are the only reader of the overlay, so the meters and
        // the published cells can be torn down right after (obs_display_destroy parks the graphics
        // thread until any in-flight display_draw has returned).
        for (auto &[target, disp] : ctx->displays) {
            if (disp.display) obs_display_destroy(disp.display);
        }
        ctx->displays.clear();

        for (auto &[id, meter] : ctx->meters) stop_meter(meter);
        ctx->meters.clear();
        {
            std::lock_guard<std::mutex> overlay_guard(ctx->overlay.lock);
            ctx->overlay.cells.clear();
            for (auto &m : ctx->overlay.bus_meters) m.reset();
        }

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

    audio_out_shutdown();  // stops render threads + raw-audio callbacks before the audio subsystem goes
    obs_shutdown();  // destroys any remaining sources, so NDI receivers are stopped before we unload it
    ndi_shutdown();
    g_log_file = nullptr;
    if (ctx->log_file) std::fclose(ctx->log_file);
    delete ctx;
}

// ---------------------------------------------------------------------------
// shared source pool
// ---------------------------------------------------------------------------

// Build (or rebuild) a MIX source's scene from its layer list. A mix is a private obs_scene, so the
// rest of the engine needs no special case: a scene *is* an obs_source, which means a mix can be mounted
// on a bus, tapped for preview, or dropped into a multiview cell exactly like a camera.
//
// Members are looked up in the shared source pool, so a source inside a mix is the same instance that is
// usable on its own elsewhere - libobs opens each device once and every scene item just references it.
static void build_mix_scene_locked(engine_ctx *ctx, const std::string &id, obs_scene_t *scene,
                                   const char *settings_json) {
    clear_scene(scene);

    obs_data_t *cfg = settings_json ? obs_data_create_from_json(settings_json) : nullptr;
    if (!cfg) return;

    uint32_t canvas_w = static_cast<uint32_t>(obs_data_get_int(cfg, "canvas_width"));
    uint32_t canvas_h = static_cast<uint32_t>(obs_data_get_int(cfg, "canvas_height"));
    if (canvas_w == 0) canvas_w = ctx->canvas_w;
    if (canvas_h == 0) canvas_h = ctx->canvas_h;

    struct LayerRec { std::string id; long long x, y, w, h, z; obs_data_t *crop; };
    std::vector<LayerRec> layers;

    obs_data_array_t *arr = obs_data_get_array(cfg, "layers");
    const size_t n = arr ? obs_data_array_count(arr) : 0;
    for (size_t i = 0; i < n; ++i) {
        obs_data_t *l = obs_data_array_item(arr, i);
        const char *sid = obs_data_get_string(l, "source_id");
        layers.push_back({sid ? sid : "",
                          obs_data_get_int(l, "x_position"), obs_data_get_int(l, "y_position"),
                          obs_data_get_int(l, "width"), obs_data_get_int(l, "height"),
                          obs_data_get_int(l, "z_order"),
                          obs_data_get_obj(l, "crop")});
        obs_data_release(l);
    }
    if (arr) obs_data_array_release(arr);

    std::stable_sort(layers.begin(), layers.end(),
                     [](const LayerRec &a, const LayerRec &b) { return a.z < b.z; });

    for (auto &layer : layers) {
        // Skip self-reference outright; libobs rejects deeper cycles itself (obs_scene_add returns null
        // when adding a source would make it its own descendant).
        if (layer.id.empty() || layer.id == id) {
            if (layer.crop) obs_data_release(layer.crop);
            continue;
        }

        auto sit = ctx->sources.find(layer.id);
        if (sit == ctx->sources.end()) {
            if (layer.crop) obs_data_release(layer.crop);
            continue;
        }

        obs_sceneitem_t *item = obs_scene_add(scene, sit->second);
        if (!item) {
            if (layer.crop) obs_data_release(layer.crop);
            continue;
        }

        if (layer.crop) {
            struct obs_sceneitem_crop c = {};
            c.left = static_cast<int>(obs_data_get_int(layer.crop, "left"));
            c.top = static_cast<int>(obs_data_get_int(layer.crop, "top"));
            c.right = static_cast<int>(obs_data_get_int(layer.crop, "right"));
            c.bottom = static_cast<int>(obs_data_get_int(layer.crop, "bottom"));
            obs_sceneitem_set_crop(item, &c);
            obs_data_release(layer.crop);
        }

        struct vec2 pos, bounds;
        vec2_set(&pos, static_cast<float>(layer.x), static_cast<float>(layer.y));
        vec2_set(&bounds,
                 static_cast<float>(layer.w > 0 ? layer.w : canvas_w),
                 static_cast<float>(layer.h > 0 ? layer.h : canvas_h));
        obs_sceneitem_set_alignment(item, OBS_ALIGN_LEFT | OBS_ALIGN_TOP);
        obs_sceneitem_set_bounds_alignment(item, OBS_ALIGN_LEFT | OBS_ALIGN_TOP);
        obs_sceneitem_set_bounds_type(item, OBS_BOUNDS_SCALE_INNER);
        obs_sceneitem_set_bounds(item, &bounds);
        obs_sceneitem_set_pos(item, &pos);
    }

    obs_data_release(cfg);
}

int engine_add_source(engine_ctx *ctx, const char *id, const char *type, const char *settings_json) {
    if (!ctx || !id || !type) return 1;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);

    // MIX has no backing obs source type: it *is* a scene of other pooled sources.
    if (to_upper(type) == "MIX") {
        auto it = ctx->sources.find(id);
        obs_scene_t *scene = it != ctx->sources.end() ? obs_scene_from_source(it->second) : nullptr;

        if (it != ctx->sources.end() && !scene) return 4;  // id already taken by a non-mix source

        if (!scene) {
            scene = obs_scene_create_private(id);
            if (!scene) return 3;
            // The scene object owns the reference the pool hands back on removal; obs_scene_get_source
            // borrows, and releasing the source releases the scene.
            ctx->sources[id] = obs_scene_get_source(scene);
        }

        build_mix_scene_locked(ctx, id, scene, settings_json);
        return 0;
    }

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

    // Silent until the App assigns a mask (engine_set_source_audio): adding a source must never put
    // audio on air by itself.
    obs_source_set_audio_mixers(src, 0);

    ctx->sources[id] = src;  // pool holds the single reference; scene items add their own

    // Meter the source regardless of its mixer mask: the multiview bar shows what the source is sending,
    // which is exactly what the operator needs to see *before* routing it to a bus.
    attach_meter_locked(ctx, id, src);
    sync_bus_meters_locked(ctx);
    return 0;
}

int engine_remove_source(engine_ctx *ctx, const char *id) {
    if (!ctx || !id) return 1;

    // Tear down the SRC:<id> readback tap first (frees its GPU objects + dec_showing under a graphics
    // context, so taps_render can no longer touch this source), then release the pooled source.
    const std::string tap_key = std::string("SRC:") + id;
    engine_set_tap(ctx, tap_key.c_str(), 0);

    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    auto mit = ctx->meters.find(id);
    if (mit != ctx->meters.end()) {
        stop_meter(mit->second);  // before the release below, so the volmeter never outlives its source
        ctx->meters.erase(mit);
    }
    auto it = ctx->sources.find(id);
    if (it != ctx->sources.end()) {
        obs_source_release(it->second);  // scene items that still reference it keep it alive until removed
        ctx->sources.erase(it);
    }
    sync_bus_meters_locked(ctx);
    return 0;
}

// Append a JSON-quoted, escaped copy of the UTF-8 string s to out.
static void json_escape_append(const char *s, std::string &out) {
    out.push_back('"');
    for (const unsigned char *p = reinterpret_cast<const unsigned char *>(s ? s : ""); *p; ++p) {
        switch (*p) {
        case '"': out += "\\\""; break;
        case '\\': out += "\\\\"; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default:
            if (*p < 0x20) {
                char buf[8];
                std::snprintf(buf, sizeof(buf), "\\u%04x", *p);
                out += buf;
            } else {
                out.push_back(static_cast<char>(*p));  // pass UTF-8 bytes through
            }
        }
    }
    out.push_back('"');
}

const char *engine_enumerate_devices(engine_ctx *ctx, const char *kind) {
    if (!ctx) return "[]";
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);

    const std::string k = kind ? to_upper(kind) : std::string();
    const char *source_id = nullptr;
    const char *prop_name = nullptr;
    if (k == "WEBCAM" || k == "UVC" || k == "DSHOW") {
        source_id = "dshow_input";
        prop_name = "video_device_id";
    } else if (k == "NDI") {
        // Our own receiver discovers senders through the NDI SDK's finder rather than through an obs
        // property list, so this path bypasses obs entirely when native NDI is available.
        if (ndi_is_available()) {
            std::string out = "[";
            bool first = true;
            ndi_append_sources_json(out, first);
            out += "]";
            ctx->last_enum_json = std::move(out);
            blog(LOG_INFO, "switcher-engine: enumerate_devices(NDI) -> %s", ctx->last_enum_json.c_str());
            return ctx->last_enum_json.c_str();
        }

        source_id = "ndi_source";        // DistroAV; absent -> obs_get_source_properties returns null
        prop_name = "ndi_source_name";
    } else {
        ctx->last_enum_json = "[]";
        return ctx->last_enum_json.c_str();
    }

    std::string out = "[";
    // Type-level property enumeration: dshow_input / ndi_source populate their device list in
    // get_properties() without needing a live instance, so no device is opened just to list them.
    obs_properties_t *props = obs_get_source_properties(source_id);
    if (props) {
        obs_property_t *p = obs_properties_get(props, prop_name);
        if (p && obs_property_get_type(p) == OBS_PROPERTY_LIST) {
            const size_t n = obs_property_list_item_count(p);
            bool first = true;
            for (size_t i = 0; i < n; ++i) {
                const char *name = obs_property_list_item_name(p, i);
                const char *id = obs_property_list_item_string(p, i);
                if (!id || !*id) continue;  // skip the empty "select a device" placeholder row
                if (!first) out += ",";
                first = false;
                out += "{\"id\":";
                json_escape_append(id, out);
                out += ",\"name\":";
                json_escape_append(name && *name ? name : id, out);
                out += ",\"formats\":null}";  // per-device format probing is a follow-up; default res works
            }
        }
        obs_properties_destroy(props);
    }
    out += "]";
    ctx->last_enum_json = std::move(out);
    blog(LOG_INFO, "switcher-engine: enumerate_devices(%s) -> %s", k.c_str(), ctx->last_enum_json.c_str());
    return ctx->last_enum_json.c_str();
}

// ---------------------------------------------------------------------------
// dual M/E
// ---------------------------------------------------------------------------

static void emit_state(engine_ctx *ctx) {
    engine_state_cb cb = ctx->state_cb;
    if (!cb) return;
    // Build with obs_data so source ids are JSON-escaped and the payload is variable-length: a raw
    // snprintf into a fixed buffer would corrupt state_json for ids containing " / \ / control chars or
    // longer than the buffer, breaking the managed OnStateChanged parse (③).
    obs_data_t *root = obs_data_create();
    obs_data_array_t *buses = obs_data_array_create();
    for (int i = 0; i < kBusCount; ++i) {
        obs_data_t *b = obs_data_create();
        obs_data_set_int(b, "bus", i);
        obs_data_set_string(b, "program_id", ctx->buses[i].program_id.c_str());
        obs_data_set_string(b, "preview_id", ctx->buses[i].preview_id.c_str());
        obs_data_array_push_back(buses, b);
        obs_data_release(b);
    }
    obs_data_set_array(root, "buses", buses);
    const char *json = obs_data_get_json(root);  // owned by root, valid until release (i.e. during cb)
    if (json) cb(ctx->state_user, json);
    obs_data_array_release(buses);
    obs_data_release(root);
}

// A PGM/PVW multiview cell shows a bus, and which source that bus carries changes without the layout
// ever changing - so the cell's meter binding is kept here rather than in the published cell list.
// program_id/preview_id are the bus' primary source; a multi-layer composition meters its first layer.
static void sync_bus_meters_locked(engine_ctx *ctx) {
    MeterRef bound[kBusCount * 2];
    for (int b = 0; b < kBusCount; ++b) {
        bound[b * 2 + 0] = find_meter_locked(ctx, ctx->buses[b].program_id);
        bound[b * 2 + 1] = find_meter_locked(ctx, ctx->buses[b].preview_id);
    }
    std::lock_guard<std::mutex> guard(ctx->overlay.lock);
    for (int i = 0; i < kBusCount * 2; ++i) ctx->overlay.bus_meters[i] = std::move(bound[i]);
}

void engine_set_preview(engine_ctx *ctx, int bus, const char *source_id) {
    if (!ctx || bus < 0 || bus >= kBusCount) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    Bus &b = ctx->buses[bus];
    b.preview_id = source_id ? source_id : "";
    scene_set_single(ctx, b.preview_scene, b.preview_id);
    sync_bus_meters_locked(ctx);
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
    sync_bus_meters_locked(ctx);
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
    // other (the new preview scene holds what was live, matching hardware-switcher PGM/PVW swap). The
    // transition already points at the new program; re-point the preview view to the new preview scene.
    // Only this bus's transition was touched - ME1/ME2 stay independent.
    std::swap(b.program_scene, b.preview_scene);
    std::swap(b.program_id, b.preview_id);
    obs_view_set_source(b.preview_view, 0, obs_scene_get_source(b.preview_scene));

    // Everything else that cached the old preview scene object must follow the swap: readback taps,
    // obs_displays, and any multiview region whose content token is PVW1/PVW2.
    rebind_bus_targets_locked(ctx, bus);
    refresh_multiview_locked(ctx);
    sync_bus_meters_locked(ctx);

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
        const std::string raw_sink = to_upper(obs_data_get_string(a, "sink"));
        const SinkToken sink_id = parse_sink_token(raw_sink);
        const std::string sink = sink_token_text(sink_id);  // canonical: a legacy "HDMI" becomes "HDMI1"
        const std::string source = to_upper(obs_data_get_string(a, "source"));  // PGM1 / PGM2
        const int bus = (source == "PGM2") ? 1 : 0;
        video_t *video = ctx->buses[bus].program_video;

        if (sink_id.kind == SinkKind::None) {
            // A sink token this build cannot place (a kind added by a newer App, an ordinal past
            // kMaxSinksPerKind, a hand-edited config): drop the row and keep building the rest of the
            // table - one bad sink must never cost the operator the sinks that are fine (spec §2.3).
            blog(LOG_WARNING, "switcher-engine: unknown output sink '%s', skipped", raw_sink.c_str());
            obs_data_release(a);
            continue;
        }

        if (sink_id.kind == SinkKind::Hdmi) {
            // HDMI is presented via engine_start_display("HDMI<n>", hwnd, display_id) from the App, which
            // owns the window handle. Nothing to bind here - record the assignment so the status query
            // can report which bus it carries, and let the display's own presence say whether it runs.
            ctx->outputs[sink] = OutputSink{nullptr, bus};
            obs_data_release(a);
            continue;
        }

        // Native NDI sender (SDK-backed, no plugin); DistroAV's ndi_output only if this build has no
        // NDI support and that plugin is installed.
        const char *ndi_out = ndi_is_available() ? ndi_output_id() : "ndi_output";

        const char *obs_output_id = nullptr;
        obs_data_t *osettings = obs_data_create();
        if (sink_id.kind == SinkKind::Vcam && sink_id.ordinal == 1) {
            // OBS ships a SINGLE virtual-camera output, and only one process can hold the camera driver.
            // That is why the operator gets exactly one webcam sink (OutputCatalog.MaxWebcamSinks): VCAM1
            // gets the camera, and no second webcam sink can be added to want one. VCAM2/VCAM3 fall
            // through to an NDI sender (see README "Multiple virtual cameras") purely so a table written
            // by an older build still egresses somewhere instead of going silent.
            obs_output_id = "virtualcam_output";
        } else {
            // Everything left (a legacy VCAM2/VCAM3 on the fallback, NDI1..NDI3) egresses as an NDI sender.
            obs_output_id = ndi_out;
            const char *nm = obs_data_get_string(a, "ndi_name");
            const std::string fallback = default_ndi_name(sink_id);
            obs_data_set_string(osettings, "ndi_name", (nm && *nm) ? nm : fallback.c_str());
        }

        if (obs_output_id) {
            obs_output_t *output = obs_output_create(obs_output_id, sink.c_str(), osettings, nullptr);
            if (output) {
                obs_output_set_media(output, video, obs_get_audio());

                // Bus n's audio lives on track n (see engine_set_source_audio), so an output carrying
                // PGM2's picture has to carry PGM2's mix and not track 0's.
                obs_output_set_mixer(output, static_cast<size_t>(bus));

                if (obs_output_start(output)) {
                    ctx->outputs[sink] = OutputSink{output, bus};
                } else {
                    // Device busy / NDI runtime missing: release and skip (graceful, no error to the
                    // caller). engine_get_output_status is how the app finds out it never started.
                    blog(LOG_WARNING, "switcher-engine: output '%s' (%s) failed to start",
                         sink.c_str(), obs_output_id);
                    obs_output_release(output);
                }
            } else {
                blog(LOG_WARNING, "switcher-engine: output '%s' could not be created ('%s' unavailable)",
                     sink.c_str(), obs_output_id);
            }
        }
        obs_data_release(osettings);
        obs_data_release(a);
    }
    if (arr) obs_data_array_release(arr);
    obs_data_release(req);

    // An HDMI projector already on screen cached the transition of the bus its sink carried when the
    // window opened, so re-routing that sink here (HDMI2: PGM1 -> PGM2) has to move the picture with it.
    // Same reasoning as rebind_bus_targets_locked after a TAKE, and the same discipline: the pointer is
    // swapped inside obs_enter_graphics, which parks the graphics thread so display_draw can never see a
    // half-updated DisplayOut.
    obs_enter_graphics();
    for (auto &[key, disp] : ctx->displays) {
        if (parse_sink_token(key).kind != SinkKind::Hdmi) continue;
        obs_source_t *src = resolve_target_source(ctx, key);
        if (src) disp.source = src;
    }
    obs_leave_graphics();
    return 0;
}

int engine_apply_multiview(engine_ctx *ctx, const char *layout_json) {
    if (!ctx || !layout_json) return 1;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    return apply_multiview_locked(ctx, layout_json);
}

// Must be called while holding ctx->lock. Caches the layout so engine_take can re-apply it (see
// rebind_bus_targets_locked): multiview regions whose content is PVW1/PVW2 hold a scene item pointing at
// the pre-TAKE preview scene, which the swap turns into the program scene.
static int apply_multiview_locked(engine_ctx *ctx, const char *layout_json) {
    ctx->last_multiview_json = layout_json;

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
    // The same pass collects the overlay cells (frame rect + which meter the bar reads), which are handed
    // to the graphics thread at the end - drawing them is what makes each cell's extent visible on a
    // full-screen multiview, where an unlit or letterboxed feed is otherwise indistinguishable from its
    // neighbour's black.
    clear_scene(ctx->mv_scene);
    const float cell_w = static_cast<float>(ctx->canvas_w) / static_cast<float>(cols);
    const float cell_h = static_cast<float>(ctx->canvas_h) / static_cast<float>(rows);
    std::vector<MvCell> cells;
    cells.reserve(regions.size());
    for (const auto &rg : regions) {
        MvCell cell;
        cell.x = rg.col * cell_w;
        cell.y = rg.row * cell_h;
        cell.w = rg.cspan * cell_w;
        cell.h = rg.rspan * cell_h;

        const std::string token = to_upper(rg.content);
        obs_source_t *src = rg.content.empty() || token == "EMPTY" ? nullptr
                                                                   : resolve_target_source(ctx, rg.content);
        if (src) {
            cell.occupied = true;
            if (rg.content.rfind("SRC:", 0) == 0) {
                cell.meter = find_meter_locked(ctx, rg.content.substr(4));
            } else if (const int bus = bus_index_from_token(token.c_str()); bus >= 0) {
                cell.bus_meter = bus * 2 + (token.rfind("PVW", 0) == 0 ? 1 : 0);
            }
        }
        cells.push_back(std::move(cell));

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

    {
        std::lock_guard<std::mutex> guard(ctx->overlay.lock);
        ctx->overlay.cells = std::move(cells);
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
    if (disp->overlay && disp->ctx) {
        // Scale the line weight with the canvas so the frames stay visible at 4K and don't swamp a
        // 720p multiview: ~3 px at 1080p.
        const float thickness = std::max(2.0f, std::floor(static_cast<float>(disp->base_h) / 360.0f));
        draw_mv_overlay(disp->ctx, thickness);
    }
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

    // Sink targets are keyed canonically ("HDMI" -> "HDMI1"): engine_get_output_status decides whether an
    // HDMI sink is running by looking its own token up in this map, so the two spellings must not diverge.
    const std::string key = display_key(target);
    DisplayOut &d = ctx->displays[key];
    d.source = src;
    d.base_w = ctx->canvas_w;
    d.base_h = ctx->canvas_h;
    d.ctx = ctx;
    d.overlay = to_upper(target) == "MULTIVIEW";  // cell frames + audio bars, full-screen multiview only

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
    if (!d.display) { ctx->displays.erase(key); return 3; }
    obs_display_add_draw_callback(d.display, display_draw, &d);
    return 0;
}

void engine_stop_display(engine_ctx *ctx, const char *target) {
    if (!ctx || !target) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    auto it = ctx->displays.find(display_key(target));
    if (it == ctx->displays.end()) return;
    if (it->second.display) {
        obs_display_remove_draw_callback(it->second.display, display_draw, &it->second);
        obs_display_destroy(it->second.display);
    }
    ctx->displays.erase(it);
}

// ---------------------------------------------------------------------------
// audio
// ---------------------------------------------------------------------------

void engine_set_source_audio(engine_ctx *ctx, const char *id, int mixers) {
    if (!ctx || !id) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);

    auto it = ctx->sources.find(id);
    if (it == ctx->sources.end()) return;

    // Bus n owns libobs audio track n, so the mask the App computes from AFV/ON/OFF maps straight onto
    // obs' mixer bits - there is no audio-follows-video concept in libobs to fight with.
    obs_source_set_audio_mixers(it->second, static_cast<uint32_t>(mixers));
}

const char *engine_get_output_status(engine_ctx *ctx) {
    if (!ctx) return "[]";
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);

    // Which sinks are actually egressing, per bus. An assignment can be accepted and still never run -
    // NDI with no runtime installed, a virtual camera another app already holds - and the operator has
    // no way to see that from the routing table alone.
    obs_data_array_t *arr = obs_data_array_create();
    for (const auto &[sink, out] : ctx->outputs) {
        // An HDMI sink binds no obs_output - the App presents it - so "running" for it means a projector
        // window is currently attached to *that* sink. Keyed by the sink token, not by the bus: two HDMI
        // sinks can carry the same bus, and closing one of their windows must only darken that one.
        const bool running = parse_sink_token(sink).kind == SinkKind::Hdmi
            ? ctx->displays.count(sink) > 0
            : out.output && obs_output_active(out.output);

        obs_data_t *item = obs_data_create();
        obs_data_set_string(item, "sink", sink.c_str());
        obs_data_set_string(item, "source", out.bus == 1 ? "PGM2" : "PGM1");
        obs_data_set_bool(item, "running", running);
        obs_data_array_push_back(arr, item);
        obs_data_release(item);
    }

    obs_data_t *root = obs_data_create();
    obs_data_set_array(root, "outputs", arr);
    const char *json = obs_data_get_json(root);
    ctx->last_output_status_json = json ? json : "{\"outputs\":[]}";
    obs_data_array_release(arr);
    obs_data_release(root);
    return ctx->last_output_status_json.c_str();
}

const char *engine_enumerate_audio_devices(engine_ctx *ctx) {
    if (!ctx) return "[]";
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    ctx->last_audio_devices_json = audio_out_enumerate_devices();
    return ctx->last_audio_devices_json.c_str();
}

int engine_apply_audio_outputs(engine_ctx *ctx, const char *assignments_json) {
    if (!ctx || !assignments_json) return 1;

    // Deliberately NOT under ctx->lock: opening a WASAPI endpoint can block for tens of milliseconds,
    // and audio routing shares no state with the video graph.
    return audio_out_apply(assignments_json);
}

// ---------------------------------------------------------------------------
// preview readback + state
// ---------------------------------------------------------------------------

void engine_set_tap(engine_ctx *ctx, const char *target, int enabled) {
    if (!ctx || !target) return;
    const std::string key = target;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);

    if (enabled) {
        obs_source_t *src = resolve_target_source(ctx, key);
        if (!src) return;
        const bool is_src = key.rfind("SRC:", 0) == 0;
        bool do_inc = false;

        // Mutate ctx->taps + register the render callback under obs graphics, which parks the graphics
        // thread so taps_render can't be iterating concurrently.
        obs_enter_graphics();
        Tap &t = ctx->taps[key];
        if (!t.source) {  // fresh tap
            t.target = key;
            t.source = src;
            t.use_source_size = is_src;
            t.last_ns = 0;
            if (is_src) { t.inc_showing = true; do_inc = true; }
            if (!ctx->render_cb_added) {
                obs_add_main_render_callback(taps_render, ctx);
                ctx->render_cb_added = true;
            }
        }
        obs_leave_graphics();

        // inc_showing outside the graphics context (its show handler may itself enter graphics). Async
        // capture sources (dshow webcam, ndi_source) only open their device / push frames while showing.
        if (do_inc) obs_source_inc_showing(src);
    } else {
        obs_source_t *shown = nullptr;
        obs_enter_graphics();
        auto it = ctx->taps.find(key);
        if (it != ctx->taps.end()) {
            if (it->second.inc_showing) shown = it->second.source;
            tap_free_gpu(it->second);
            ctx->taps.erase(it);
        }
        obs_leave_graphics();
        if (shown) obs_source_dec_showing(shown);
    }
}

void engine_set_frame_cb(engine_ctx *ctx, engine_frame_cb cb, void *user) {
    if (!ctx) return;
    // Stored atomically so the main-render tap callback reads a consistent cb/user without ctx->lock.
    ctx->frame_user.store(user, std::memory_order_release);
    ctx->frame_cb.store(cb, std::memory_order_release);
}

void engine_set_state_cb(engine_ctx *ctx, engine_state_cb cb, void *user) {
    if (!ctx) return;
    std::lock_guard<std::recursive_mutex> guard(ctx->lock);
    ctx->state_cb = cb;
    ctx->state_user = user;
}
