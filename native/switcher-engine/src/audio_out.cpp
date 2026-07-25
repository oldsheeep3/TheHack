#include "audio_out.h"

#include <obs.h>

#include <atomic>
#include <cstdio>
#include <cstring>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

#ifdef _WIN32

#include <windows.h>
#include <audioclient.h>
#include <mmdeviceapi.h>
#include <functiondiscoverykeys_devpkey.h>

namespace {

constexpr size_t kBusCount = 2;

// How much audio the ring holds. Big enough to absorb obs' burst cadence and a late render wake-up,
// small enough that the added latency stays inaudible for live monitoring (~100 ms at 48 kHz).
constexpr size_t kRingMilliseconds = 100;

std::string wide_to_utf8(const wchar_t *w) {
    if (!w) return {};
    const int len = WideCharToMultiByte(CP_UTF8, 0, w, -1, nullptr, 0, nullptr, nullptr);
    if (len <= 1) return {};
    std::string out(static_cast<size_t>(len - 1), '\0');
    WideCharToMultiByte(CP_UTF8, 0, w, -1, out.data(), len, nullptr, nullptr);
    return out;
}

std::wstring utf8_to_wide(const std::string &s) {
    if (s.empty()) return {};
    const int len = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, nullptr, 0);
    if (len <= 1) return {};
    std::wstring out(static_cast<size_t>(len - 1), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, out.data(), len);
    return out;
}

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
                out.push_back(static_cast<char>(*p));
            }
        }
    }
    out.push_back('"');
}

// COM is initialised per thread. The render thread and any thread that enumerates need it; using
// apartment-agnostic MTA keeps this simple and matches what the audio APIs expect here.
struct ComScope {
    bool owned = false;
    ComScope() {
        const HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        owned = SUCCEEDED(hr);
    }
    ~ComScope() {
        if (owned) CoUninitialize();
    }
};

// ---------------------------------------------------------------------------
// one sink = one obs audio track rendered to one endpoint
// ---------------------------------------------------------------------------

struct AudioSink {
    size_t mix_idx = 0;
    std::string device_id;      // empty = system default

    // The ring always holds STEREO, because that is what we ask obs for. A program bus mix is stereo,
    // and requesting the endpoint's own layout would mean depending on libobs supporting whatever a
    // device reports — an 8-channel headset, say. The render loop up-mixes into the device's channel
    // count when it fills the WASAPI buffer. (Reading the ring at the device's channel count is exactly
    // the buffer overrun that crashed the app the first time this was written.)
    static constexpr uint32_t kRingChannels = 2;

    uint32_t device_channels = 2;
    uint32_t sample_rate = 48000;

    std::atomic<bool> running{false};
    std::thread thread;

    // Interleaved float ring, written by obs' audio thread and read by the render thread.
    std::mutex lock;
    std::vector<float> ring;
    size_t capacity_frames = 0;
    size_t write_frame = 0;
    size_t read_frame = 0;
    size_t buffered_frames = 0;

    void reset_ring(uint32_t rate, uint32_t device_chans) {
        std::lock_guard<std::mutex> guard(lock);
        device_channels = device_chans;
        sample_rate = rate;
        capacity_frames = static_cast<size_t>(rate) * kRingMilliseconds / 1000;
        ring.assign(capacity_frames * kRingChannels, 0.0f);
        write_frame = read_frame = buffered_frames = 0;
    }

    void push(const float *samples, size_t frames) {
        std::lock_guard<std::mutex> guard(lock);
        if (capacity_frames == 0) return;

        for (size_t i = 0; i < frames; ++i) {
            // Overrun: drop the oldest frame rather than the newest, so a stalled device recovers to
            // *current* audio instead of replaying a backlog.
            if (buffered_frames == capacity_frames) {
                read_frame = (read_frame + 1) % capacity_frames;
                --buffered_frames;
            }

            std::memcpy(&ring[write_frame * kRingChannels], &samples[i * kRingChannels],
                        kRingChannels * sizeof(float));
            write_frame = (write_frame + 1) % capacity_frames;
            ++buffered_frames;
        }
    }

    // Fills `out` (device layout, interleaved) with up to `frames`. Anything not available is written
    // as silence, which is the correct behaviour for a live feed — never stretch or repeat. The stereo
    // ring goes to the first two channels and the rest are zeroed, so a surround endpoint plays the
    // program mix on its front pair rather than nothing.
    size_t pull(float *out, size_t frames) {
        std::lock_guard<std::mutex> guard(lock);
        std::memset(out, 0, frames * device_channels * sizeof(float));

        size_t written = 0;
        while (written < frames && buffered_frames > 0) {
            const float *src = &ring[read_frame * kRingChannels];
            float *dst = &out[written * device_channels];

            dst[0] = src[0];
            if (device_channels >= 2) {
                dst[1] = src[1];
            }

            read_frame = (read_frame + 1) % capacity_frames;
            --buffered_frames;
            ++written;
        }

        return written;
    }
};

std::vector<std::unique_ptr<AudioSink>> g_sinks;
std::mutex g_sinks_lock;

// obs audio thread -> ring. Kept to a memcpy: this runs on the audio pipeline and must not block.
void on_raw_audio(void *param, size_t mix_idx, struct audio_data *data) {
    (void)mix_idx;
    auto *sink = static_cast<AudioSink *>(param);
    if (!data || !data->data[0] || data->frames == 0) return;
    sink->push(reinterpret_cast<const float *>(data->data[0]), data->frames);
}

IMMDevice *open_endpoint(IMMDeviceEnumerator *enumerator, const std::string &device_id) {
    IMMDevice *device = nullptr;
    if (device_id.empty()) {
        enumerator->GetDefaultAudioEndpoint(eRender, eConsole, &device);
    } else {
        enumerator->GetDevice(utf8_to_wide(device_id).c_str(), &device);
    }
    return device;
}

void render_loop(AudioSink *sink) {
    ComScope com;

    IMMDeviceEnumerator *enumerator = nullptr;
    if (FAILED(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL,
                                __uuidof(IMMDeviceEnumerator), reinterpret_cast<void **>(&enumerator)))) {
        blog(LOG_WARNING, "switcher-engine: audio out - no device enumerator");
        return;
    }

    IMMDevice *device = open_endpoint(enumerator, sink->device_id);
    enumerator->Release();
    if (!device) {
        blog(LOG_WARNING, "switcher-engine: audio out - endpoint '%s' not found",
             sink->device_id.empty() ? "(default)" : sink->device_id.c_str());
        return;
    }

    IAudioClient *client = nullptr;
    WAVEFORMATEX *mix_format = nullptr;
    IAudioRenderClient *render = nullptr;
    HANDLE ready = nullptr;
    bool started = false;

    do {
        if (FAILED(device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr,
                                    reinterpret_cast<void **>(&client)))) break;
        if (FAILED(client->GetMixFormat(&mix_format)) || !mix_format) break;

        // Shared mode renders in the endpoint's own mix format; obs resamples/remixes for us via the
        // audio_convert_info we register with, so we only have to accept float here.
        const bool is_float =
            mix_format->wFormatTag == WAVE_FORMAT_IEEE_FLOAT ||
            (mix_format->wFormatTag == WAVE_FORMAT_EXTENSIBLE &&
             reinterpret_cast<WAVEFORMATEXTENSIBLE *>(mix_format)->SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
        if (!is_float) {
            blog(LOG_WARNING, "switcher-engine: audio out - endpoint mix format is not float; skipping");
            break;
        }

        sink->reset_ring(mix_format->nSamplesPerSec, mix_format->nChannels);

        ready = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!ready) break;

        // 200 000 * 100 ns = 20 ms buffer; event-driven so we wake exactly when the device wants data.
        if (FAILED(client->Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                                      200000, 0, mix_format, nullptr))) break;
        if (FAILED(client->SetEventHandle(ready))) break;

        UINT32 buffer_frames = 0;
        if (FAILED(client->GetBufferSize(&buffer_frames))) break;
        if (FAILED(client->GetService(__uuidof(IAudioRenderClient), reinterpret_cast<void **>(&render)))) break;

        // Register for this bus's PCM only once the endpoint is ready, so no audio is buffered into a
        // sink that turns out to be unusable.
        struct audio_convert_info conversion = {};
        conversion.samples_per_sec = mix_format->nSamplesPerSec;
        conversion.format = AUDIO_FORMAT_FLOAT;                 // interleaved
        conversion.speakers = SPEAKERS_STEREO;                  // always 2 ch into the ring
        obs_add_raw_audio_callback(sink->mix_idx, &conversion, on_raw_audio, sink);

        if (FAILED(client->Start())) {
            obs_remove_raw_audio_callback(sink->mix_idx, on_raw_audio, sink);
            break;
        }
        started = true;

        blog(LOG_INFO, "switcher-engine: audio out - track %d -> '%s' (%u Hz, %u ch)",
             static_cast<int>(sink->mix_idx),
             sink->device_id.empty() ? "(default)" : sink->device_id.c_str(),
             mix_format->nSamplesPerSec, mix_format->nChannels);

        std::vector<float> scratch(static_cast<size_t>(buffer_frames) * mix_format->nChannels);

        while (sink->running.load(std::memory_order_acquire)) {
            if (WaitForSingleObject(ready, 200) != WAIT_OBJECT_0) continue;

            UINT32 padding = 0;
            if (FAILED(client->GetCurrentPadding(&padding))) break;

            const UINT32 available = buffer_frames - padding;
            if (available == 0) continue;

            BYTE *buffer = nullptr;
            if (FAILED(render->GetBuffer(available, &buffer))) break;
            sink->pull(scratch.data(), available);
            std::memcpy(buffer, scratch.data(),
                        static_cast<size_t>(available) * mix_format->nChannels * sizeof(float));
            render->ReleaseBuffer(available, 0);
        }
    } while (false);

    if (started) {
        obs_remove_raw_audio_callback(sink->mix_idx, on_raw_audio, sink);
        client->Stop();
    }
    if (render) render->Release();
    if (ready) CloseHandle(ready);
    if (mix_format) CoTaskMemFree(mix_format);
    if (client) client->Release();
    device->Release();
}

void stop_all_locked() {
    for (auto &sink : g_sinks) {
        sink->running.store(false, std::memory_order_release);
        if (sink->thread.joinable()) sink->thread.join();
    }
    g_sinks.clear();
}

}  // namespace

std::string audio_out_enumerate_devices(void) {
    ComScope com;

    IMMDeviceEnumerator *enumerator = nullptr;
    if (FAILED(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL,
                                __uuidof(IMMDeviceEnumerator), reinterpret_cast<void **>(&enumerator)))) {
        return "[]";
    }

    std::string default_id;
    IMMDevice *default_device = nullptr;
    if (SUCCEEDED(enumerator->GetDefaultAudioEndpoint(eRender, eConsole, &default_device)) && default_device) {
        LPWSTR id = nullptr;
        if (SUCCEEDED(default_device->GetId(&id))) {
            default_id = wide_to_utf8(id);
            CoTaskMemFree(id);
        }
        default_device->Release();
    }

    IMMDeviceCollection *collection = nullptr;
    std::string out = "[";
    if (SUCCEEDED(enumerator->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, &collection)) && collection) {
        UINT count = 0;
        collection->GetCount(&count);
        bool first = true;

        for (UINT i = 0; i < count; ++i) {
            IMMDevice *device = nullptr;
            if (FAILED(collection->Item(i, &device)) || !device) continue;

            LPWSTR id = nullptr;
            std::string device_id;
            if (SUCCEEDED(device->GetId(&id))) {
                device_id = wide_to_utf8(id);
                CoTaskMemFree(id);
            }

            std::string name = device_id;
            IPropertyStore *props = nullptr;
            if (SUCCEEDED(device->OpenPropertyStore(STGM_READ, &props)) && props) {
                PROPVARIANT value;
                PropVariantInit(&value);
                if (SUCCEEDED(props->GetValue(PKEY_Device_FriendlyName, &value)) && value.vt == VT_LPWSTR) {
                    name = wide_to_utf8(value.pwszVal);
                }
                PropVariantClear(&value);
                props->Release();
            }

            if (!device_id.empty()) {
                if (!first) out += ",";
                first = false;
                out += "{\"id\":";
                append_json_string(device_id.c_str(), out);
                out += ",\"name\":";
                append_json_string(name.c_str(), out);
                out += device_id == default_id ? ",\"is_default\":true}" : ",\"is_default\":false}";
            }

            device->Release();
        }
        collection->Release();
    }
    out += "]";

    enumerator->Release();
    return out;
}

int audio_out_apply(const char *assignments_json) {
    if (!assignments_json) return 1;

    obs_data_t *root = obs_data_create_from_json(assignments_json);
    if (!root) return 2;

    struct Wanted { size_t mix_idx; std::string device_id; };
    std::vector<Wanted> wanted;

    obs_data_array_t *arr = obs_data_get_array(root, "outputs");
    const size_t n = arr ? obs_data_array_count(arr) : 0;
    for (size_t i = 0; i < n; ++i) {
        obs_data_t *item = obs_data_array_item(arr, i);

        // ProgramBus crosses the ABI as its contract spelling ("PGM1"/"PGM2"), like every other bus
        // token in this engine; a bare index is accepted too so the field can be driven directly.
        size_t bus = kBusCount;
        const char *bus_token = obs_data_get_string(item, "bus");
        if (bus_token && *bus_token) {
            if (_stricmp(bus_token, "PGM1") == 0) bus = 0;
            else if (_stricmp(bus_token, "PGM2") == 0) bus = 1;
        } else {
            const long long index = obs_data_get_int(item, "bus");
            if (index >= 0 && static_cast<size_t>(index) < kBusCount) bus = static_cast<size_t>(index);
        }

        const char *device = obs_data_get_string(item, "device_id");
        if (bus < kBusCount) {
            wanted.push_back({bus, device ? device : ""});
        }

        obs_data_release(item);
    }
    if (arr) obs_data_array_release(arr);
    obs_data_release(root);

    // Rebuild wholesale. Restarting a sink costs a device open, which only happens when the operator
    // changes routing - not on the audio path - so the simplicity is worth more than the churn.
    std::lock_guard<std::mutex> guard(g_sinks_lock);
    stop_all_locked();

    for (const auto &entry : wanted) {
        auto sink = std::make_unique<AudioSink>();
        sink->mix_idx = entry.mix_idx;
        sink->device_id = entry.device_id;
        sink->running.store(true, std::memory_order_release);

        AudioSink *raw = sink.get();
        sink->thread = std::thread(render_loop, raw);
        g_sinks.push_back(std::move(sink));
    }

    return 0;
}

void audio_out_shutdown(void) {
    std::lock_guard<std::mutex> guard(g_sinks_lock);
    stop_all_locked();
}

#else  // !_WIN32

std::string audio_out_enumerate_devices(void) { return "[]"; }
int audio_out_apply(const char *) { return 0; }
void audio_out_shutdown(void) {}

#endif
