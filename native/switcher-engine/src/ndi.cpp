#include "ndi.h"

#include <obs.h>
#include <util/platform.h>

#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#ifdef SWITCHER_HAS_NDI

// The dynamic-load header declares the whole API as a struct of function pointers plus a loader, so we
// never link against Processing.NDI.Lib.x64.lib and the DLL stays optional at runtime.
#include <Processing.NDI.Lib.h>

#ifdef _WIN32
#include <windows.h>
#endif

namespace {

const NDIlib_v5 *g_ndi = nullptr;
bool g_load_attempted = false;
bool g_registered = false;

NDIlib_find_instance_t g_find = nullptr;
std::mutex g_find_lock;

#ifdef _WIN32
// The runtime installer sets NDI_RUNTIME_DIR_V6 (V5 on older installs); fall back to the documented
// default install location so a machine that only has the SDK still works.
std::string locate_ndi_library() {
    const char *vars[] = {"NDI_RUNTIME_DIR_V6", "NDI_RUNTIME_DIR_V5", "NDI_RUNTIME_DIR_V4"};
    for (const char *var : vars) {
        const char *dir = std::getenv(var);
        if (dir && *dir) return std::string(dir) + "\\" + NDILIB_LIBRARY_NAME;
    }
    return NDILIB_LIBRARY_NAME;  // let the loader search PATH
}

const NDIlib_v5 *load_ndi_runtime() {
    const std::string path = locate_ndi_library();
    HMODULE lib = LoadLibraryA(path.c_str());
    if (!lib) {
        blog(LOG_INFO, "switcher-engine: NDI runtime not found ('%s'); NDI sources unavailable",
             path.c_str());
        return nullptr;
    }

    using load_fn = const NDIlib_v5 *(*)(void);
    auto loader = reinterpret_cast<load_fn>(
        reinterpret_cast<void *>(GetProcAddress(lib, "NDIlib_v5_load")));
    if (!loader) {
        blog(LOG_WARNING, "switcher-engine: '%s' has no NDIlib_v5_load entry point", path.c_str());
        return nullptr;
    }

    const NDIlib_v5 *ndi = loader();
    if (!ndi || !ndi->initialize()) {
        blog(LOG_WARNING, "switcher-engine: NDIlib initialize() failed (unsupported CPU?)");
        return nullptr;
    }

    blog(LOG_INFO, "switcher-engine: NDI runtime loaded from '%s'", path.c_str());
    return ndi;
}
#else
const NDIlib_v5 *load_ndi_runtime() { return nullptr; }
#endif

const NDIlib_v5 *ndi() {
    if (!g_load_attempted) {
        g_load_attempted = true;
        g_ndi = load_ndi_runtime();
    }
    return g_ndi;
}

// ---------------------------------------------------------------------------
// receiver source
// ---------------------------------------------------------------------------

struct NdiSource {
    obs_source_t *source = nullptr;
    std::mutex lock;
    std::string name;          // guarded by lock; the receive thread copies it per (re)connect
    std::atomic<bool> running{false};
    std::thread thread;
};

// We ask the receiver for NDIlib_recv_color_format_BGRX_BGRA, so the sender (or the SDK) does any
// conversion and this stays a two-case mapping onto packed RGB formats obs takes as-is - no colour
// matrix, no planar handling.
bool to_obs_format(NDIlib_FourCC_video_type_e fourcc, video_format *out) {
    switch (fourcc) {
    case NDIlib_FourCC_video_type_BGRA: *out = VIDEO_FORMAT_BGRA; return true;
    case NDIlib_FourCC_video_type_BGRX: *out = VIDEO_FORMAT_BGRX; return true;
    default: return false;
    }
}

// JSON string escaper, local to this file so the NDI path has no dependency on engine.cpp.
void append_json_string(const char *s, std::string &out) {
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
                out.push_back(static_cast<char>(*p));  // pass UTF-8 through
            }
        }
    }
    out.push_back('"');
}

void receive_loop(NdiSource *self) {
    const NDIlib_v5 *lib = ndi();
    if (!lib) return;

    NDIlib_recv_instance_t recv = nullptr;
    std::string connected_to;

    while (self->running.load(std::memory_order_acquire)) {
        std::string wanted;
        {
            std::lock_guard<std::mutex> guard(self->lock);
            wanted = self->name;
        }

        // (Re)connect whenever the configured source name changes, and keep retrying while it is empty
        // or not yet on the network - an NDI sender that appears later must just start working.
        if (!recv || wanted != connected_to) {
            if (recv) {
                lib->recv_destroy(recv);
                recv = nullptr;
            }

            if (wanted.empty()) {
                std::this_thread::sleep_for(std::chrono::milliseconds(250));
                continue;
            }

            NDIlib_source_t src = {};
            src.p_ndi_name = wanted.c_str();

            NDIlib_recv_create_v3_t settings = {};
            settings.source_to_connect_to = src;
            settings.color_format = NDIlib_recv_color_format_BGRX_BGRA;
            settings.bandwidth = NDIlib_recv_bandwidth_highest;
            settings.allow_video_fields = false;
            settings.p_ndi_recv_name = "Switcher";

            recv = lib->recv_create_v3(&settings);
            connected_to = wanted;
            if (!recv) {
                std::this_thread::sleep_for(std::chrono::milliseconds(500));
                continue;
            }
        }

        NDIlib_video_frame_v2_t video = {};
        // A bounded wait keeps this loop responsive to stop/reconfigure without spinning.
        const NDIlib_frame_type_e type = lib->recv_capture_v2(recv, &video, nullptr, nullptr, 100);
        if (type != NDIlib_frame_type_video) {
            continue;
        }

        video_format format;
        if (video.p_data && video.xres > 0 && video.yres > 0 && to_obs_format(video.FourCC, &format)) {
            struct obs_source_frame frame = {};
            frame.width = static_cast<uint32_t>(video.xres);
            frame.height = static_cast<uint32_t>(video.yres);
            frame.format = format;
            frame.data[0] = video.p_data;
            frame.linesize[0] = static_cast<uint32_t>(
                video.line_stride_in_bytes > 0 ? video.line_stride_in_bytes : video.xres * 4);
            frame.timestamp = os_gettime_ns();
            frame.full_range = true;  // packed RGB from NDI is full-range; no colour matrix needed

            // obs copies the pixels into its own async frame queue, so the NDI buffer can be freed
            // immediately afterwards.
            obs_source_output_video(self->source, &frame);
        }

        lib->recv_free_video_v2(recv, &video);
    }

    if (recv) lib->recv_destroy(recv);
}

void start_thread(NdiSource *self) {
    self->running.store(true, std::memory_order_release);
    self->thread = std::thread(receive_loop, self);
}

void stop_thread(NdiSource *self) {
    self->running.store(false, std::memory_order_release);
    if (self->thread.joinable()) self->thread.join();
}

const char *src_get_name(void *) { return "NDI source"; }

void src_update(void *data, obs_data_t *settings) {
    auto *self = static_cast<NdiSource *>(data);
    const char *name = obs_data_get_string(settings, "ndi_name");
    std::lock_guard<std::mutex> guard(self->lock);
    self->name = name ? name : "";
}

void *src_create(obs_data_t *settings, obs_source_t *source) {
    auto *self = new NdiSource();
    self->source = source;
    src_update(self, settings);
    start_thread(self);
    return self;
}

void src_destroy(void *data) {
    auto *self = static_cast<NdiSource *>(data);
    stop_thread(self);
    delete self;
}

void src_defaults(obs_data_t *settings) { obs_data_set_default_string(settings, "ndi_name", ""); }

// ---------------------------------------------------------------------------
// sender output
// ---------------------------------------------------------------------------

struct NdiOutput {
    obs_output_t *output = nullptr;
    NDIlib_send_instance_t send = nullptr;
    std::string name;
    uint32_t width = 0;
    uint32_t height = 0;
    int fps_num = 60;
    int fps_den = 1;

    // Audio: NDI takes planar float in ONE allocation addressed by a channel stride, while obs hands
    // over a separate pointer per plane. Repacking needs a scratch buffer, owned here and only ever
    // touched by the raw-audio callback (which cannot overlap itself or outlive end_data_capture).
    int sample_rate = 48000;
    int channels = 2;
    std::vector<float> audio_scratch;
};

const char *out_get_name(void *) { return "NDI output"; }

void out_update(void *data, obs_data_t *settings) {
    auto *self = static_cast<NdiOutput *>(data);
    const char *name = obs_data_get_string(settings, "ndi_name");
    self->name = name && *name ? name : "Switcher";
}

void *out_create(obs_data_t *settings, obs_output_t *output) {
    auto *self = new NdiOutput();
    self->output = output;
    out_update(self, settings);
    return self;
}

void out_destroy(void *data) { delete static_cast<NdiOutput *>(data); }

bool out_start(void *data) {
    auto *self = static_cast<NdiOutput *>(data);
    const NDIlib_v5 *lib = ndi();
    if (!lib) return false;

    video_t *video = obs_output_video(self->output);
    if (!video) return false;

    self->width = video_output_get_width(video);
    self->height = video_output_get_height(video);
    self->fps_num = static_cast<int>(video_output_get_frame_rate(video) * 1000.0);
    self->fps_den = 1000;
    if (self->fps_num <= 0) { self->fps_num = 60; self->fps_den = 1; }

    NDIlib_send_create_t create = {};
    create.p_ndi_name = self->name.c_str();
    create.clock_video = true;   // pace sending to the frame rate so receivers get an even cadence
    create.clock_audio = false;

    self->send = lib->send_create(&create);
    if (!self->send) {
        blog(LOG_WARNING, "switcher-engine: NDIlib_send_create failed for '%s'", self->name.c_str());
        return false;
    }

    // Ask obs for BGRA regardless of the canvas format, so raw_video never has to convert here.
    struct video_scale_info conversion = {};
    conversion.format = VIDEO_FORMAT_BGRA;
    conversion.width = self->width;
    conversion.height = self->height;
    conversion.range = VIDEO_RANGE_FULL;
    conversion.colorspace = VIDEO_CS_709;
    obs_output_set_video_conversion(self->output, &conversion);

    // Stereo planar float is what NDI's FLTp expects, and a program bus mix is stereo. Which track we
    // are fed is chosen by the engine with obs_output_set_mixer.
    if (audio_t *audio = obs_output_audio(self->output)) {
        self->sample_rate = static_cast<int>(audio_output_get_sample_rate(audio));
    }
    self->channels = 2;

    struct audio_convert_info audio_conversion = {};
    audio_conversion.samples_per_sec = static_cast<uint32_t>(self->sample_rate);
    audio_conversion.format = AUDIO_FORMAT_FLOAT_PLANAR;
    audio_conversion.speakers = SPEAKERS_STEREO;
    obs_output_set_audio_conversion(self->output, &audio_conversion);

    if (!obs_output_begin_data_capture(self->output, 0)) {
        lib->send_destroy(self->send);
        self->send = nullptr;
        return false;
    }

    blog(LOG_INFO, "switcher-engine: NDI output '%s' started (%ux%u)", self->name.c_str(),
         self->width, self->height);
    return true;
}

void out_stop(void *data, uint64_t) {
    auto *self = static_cast<NdiOutput *>(data);
    obs_output_end_data_capture(self->output);

    // end_data_capture has returned, so no further raw_video callback can be in flight.
    if (self->send) {
        const NDIlib_v5 *lib = ndi();
        if (lib) lib->send_destroy(self->send);
        self->send = nullptr;
    }
}

void out_raw_video(void *data, struct video_data *frame) {
    auto *self = static_cast<NdiOutput *>(data);
    const NDIlib_v5 *lib = g_ndi;
    if (!lib || !self->send || !frame || !frame->data[0]) return;

    NDIlib_video_frame_v2_t video = {};
    video.xres = static_cast<int>(self->width);
    video.yres = static_cast<int>(self->height);
    video.FourCC = NDIlib_FourCC_video_type_BGRA;
    video.frame_rate_N = self->fps_num;
    video.frame_rate_D = self->fps_den;
    video.picture_aspect_ratio = 0.0f;  // square pixels
    video.frame_format_type = NDIlib_frame_format_type_progressive;
    video.timecode = NDIlib_send_timecode_synthesize;
    video.p_data = frame->data[0];
    video.line_stride_in_bytes = static_cast<int>(frame->linesize[0]);

    // Synchronous send: the obs frame buffer is only valid for the duration of this callback, and the
    // async variant requires the caller's buffer to stay alive until the *next* async send.
    lib->send_send_video_v2(self->send, &video);
}

void out_raw_audio(void *data, struct audio_data *frames) {
    auto *self = static_cast<NdiOutput *>(data);
    const NDIlib_v5 *lib = g_ndi;
    if (!lib || !self->send || !frames || frames->frames == 0 || !frames->data[0]) return;

    const size_t count = frames->frames;
    self->audio_scratch.resize(count * static_cast<size_t>(self->channels));

    for (int ch = 0; ch < self->channels; ++ch) {
        float *dst = self->audio_scratch.data() + static_cast<size_t>(ch) * count;
        // A mono mix only fills plane 0; duplicate it so both NDI channels carry the programme
        // rather than one channel playing silence.
        const auto *src = reinterpret_cast<const float *>(frames->data[ch] ? frames->data[ch] : frames->data[0]);
        std::memcpy(dst, src, count * sizeof(float));
    }

    NDIlib_audio_frame_v3_t audio = {};
    audio.sample_rate = self->sample_rate;
    audio.no_channels = self->channels;
    audio.no_samples = static_cast<int>(count);
    audio.timecode = NDIlib_send_timecode_synthesize;
    audio.FourCC = NDIlib_FourCC_audio_type_FLTP;
    audio.p_data = reinterpret_cast<uint8_t *>(self->audio_scratch.data());
    audio.channel_stride_in_bytes = static_cast<int>(count * sizeof(float));

    lib->send_send_audio_v3(self->send, &audio);
}

void out_defaults(obs_data_t *settings) { obs_data_set_default_string(settings, "ndi_name", "Switcher"); }

obs_output_info make_output_info() {
    obs_output_info info = {};
    info.id = "switcher_ndi_output";
    info.flags = OBS_OUTPUT_VIDEO | OBS_OUTPUT_AUDIO;
    info.get_name = out_get_name;
    info.create = out_create;
    info.destroy = out_destroy;
    info.start = out_start;
    info.stop = out_stop;
    info.raw_video = out_raw_video;
    info.raw_audio = out_raw_audio;
    info.update = out_update;
    info.get_defaults = out_defaults;
    return info;
}

obs_source_info make_source_info() {
    obs_source_info info = {};
    info.id = "switcher_ndi";
    info.type = OBS_SOURCE_TYPE_INPUT;
    info.output_flags = OBS_SOURCE_ASYNC_VIDEO | OBS_SOURCE_DO_NOT_DUPLICATE;
    info.get_name = src_get_name;
    info.create = src_create;
    info.destroy = src_destroy;
    info.update = src_update;
    info.get_defaults = src_defaults;
    info.icon_type = OBS_ICON_TYPE_CAMERA;
    return info;
}

}  // namespace

bool ndi_register_source(void) {
    if (g_registered) return true;
    if (!ndi()) return false;

    static obs_source_info source_info = make_source_info();
    obs_register_source(&source_info);

    static obs_output_info output_info = make_output_info();
    obs_register_output(&output_info);

    g_registered = true;
    blog(LOG_INFO, "switcher-engine: registered native NDI source ('%s') and output ('%s')",
         source_info.id, output_info.id);
    return true;
}

bool ndi_is_available(void) { return g_registered; }

const char *ndi_source_id(void) { return g_registered ? "switcher_ndi" : nullptr; }

const char *ndi_output_id(void) { return g_registered ? "switcher_ndi_output" : nullptr; }

void ndi_append_sources_json(std::string &out, bool &first) {
    const NDIlib_v5 *lib = ndi();
    if (!lib) return;

    std::lock_guard<std::mutex> guard(g_find_lock);
    if (!g_find) {
        NDIlib_find_create_t create = {};
        create.show_local_sources = true;
        g_find = lib->find_create_v2(&create);
        if (!g_find) return;

        // The finder needs a moment on first use before anything has been discovered; later calls read
        // whatever the background discovery has accumulated since.
        lib->find_wait_for_sources(g_find, 1000);
    } else {
        lib->find_wait_for_sources(g_find, 100);
    }

    uint32_t count = 0;
    const NDIlib_source_t *sources = lib->find_get_current_sources(g_find, &count);
    for (uint32_t i = 0; i < count; ++i) {
        const char *name = sources[i].p_ndi_name;
        if (!name || !*name) continue;

        if (!first) out += ",";
        first = false;

        // The NDI name is both the identifier and the label: it is what recv_create_v3 connects by.
        out += "{\"id\":";
        append_json_string(name, out);
        out += ",\"name\":";
        append_json_string(name, out);
        out += ",\"formats\":null}";
    }
}

void ndi_shutdown(void) {
    const NDIlib_v5 *lib = g_ndi;
    if (!lib) return;

    {
        std::lock_guard<std::mutex> guard(g_find_lock);
        if (g_find) {
            lib->find_destroy(g_find);
            g_find = nullptr;
        }
    }

    lib->destroy();
    g_ndi = nullptr;
    g_load_attempted = false;
    g_registered = false;
}

#else  // !SWITCHER_HAS_NDI

// Built without the NDI SDK headers: every entry point reports "no NDI" so the engine still compiles
// and the app falls back to DistroAV's ndi_source if that plugin is installed.
bool ndi_register_source(void) { return false; }
bool ndi_is_available(void) { return false; }
const char *ndi_source_id(void) { return nullptr; }
const char *ndi_output_id(void) { return nullptr; }
void ndi_append_sources_json(std::string &, bool &) {}
void ndi_shutdown(void) {}

#endif
