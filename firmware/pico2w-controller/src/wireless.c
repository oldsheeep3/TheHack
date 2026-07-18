// Wi-Fiワイヤレス制御チャネルのI/O部(CYW43/lwIP依存, RP2350依存)。
// フレームのエンコード/デコード自体はwireless_codec.c(Pico SDK非依存)に分離してあり、
// ここではCYW43初期化・Wi-Fi接続(バックオフ再接続)・UDPソケットの送受信結線のみを担う
// (backlight.c/settings.cの分離パターンを踏襲)。
//
// -DENABLE_WIRELESS が定義されたビルドでのみコンパイル対象になる(CMakeLists.txt参照)。
// 無効ビルドではこのファイル自体がターゲットに含まれないため、CYW43/lwIPへの依存も
// リンクサイズも一切増えない。
//
// 障害隔離: 無線側の初期化失敗・切断・タイムアウトはこのファイル内で吸収し、USB経路
// (usb_hid.c)・I2C集約(i2c_modules.c)には一切影響を出さない(親仕様書 §4非機能要件)。

#ifdef ENABLE_WIRELESS

#include "wireless.h"

#include <string.h>

#include "pico/cyw43_arch.h"
#include "pico/time.h"

#include "lwip/pbuf.h"
#include "lwip/udp.h"

// 再接続バックオフ(切断/認証失敗のたびに倍加し、上限で頭打ちにする)。
#define WIRELESS_RECONNECT_BACKOFF_MIN_MS 1000u
#define WIRELESS_RECONNECT_BACKOFF_MAX_MS 30000u

typedef enum {
    WIRELESS_STATE_DISABLED = 0, // 未設定、または初期化失敗
    WIRELESS_STATE_CONNECTING,
    WIRELESS_STATE_CONNECTED,
} wireless_state_t;

static wireless_state_t s_state = WIRELESS_STATE_DISABLED;
static settings_t s_settings;
static struct udp_pcb *s_pcb;

// 直近にBACKLIGHTフレームを送ってきた相手(PC側)をSTATE送信先として記憶する。
// 未受信のうちはSTATE送信を行わない(誰に送ればよいか不明なため)。
static ip_addr_t s_peer_addr;
static u16_t s_peer_port;
static bool s_peer_known;

static uint32_t s_backoff_ms;
static uint64_t s_next_connect_attempt_ms;

static uint64_t wireless_now_ms(void) {
    return time_us_64() / 1000;
}

static void wireless_udp_recv_cb(void *arg, struct udp_pcb *pcb, struct pbuf *p, const ip_addr_t *addr,
                                  u16_t port) {
    (void)arg;
    (void)pcb;
    if (p == NULL) {
        return;
    }

    s_peer_addr = *addr;
    s_peer_port = port;
    s_peer_known = true;

    if (p->tot_len == WIRELESS_FRAME_BACKLIGHT_LEN) {
        uint8_t frame[WIRELESS_FRAME_BACKLIGHT_LEN];
        pbuf_copy_partial(p, frame, sizeof(frame), 0);

        backlight_command_t cmd;
        if (wireless_codec_parse_backlight_frame(frame, sizeof(frame), &cmd)) {
            // 受領キュー+I2C書込(backlight_task)をUSB出力レポート0x02経路と共用する
            // (二重実装しない, .claude/review-patterns.md「設計・責務分離」)。
            backlight_enqueue_command(&cmd);
        }
        // type不一致・パース失敗(範囲外module_index等)の場合は黙って棄却する
        // (backlight_on_output_reportの不正レポート棄却と同方針)。
    }

    pbuf_free(p);
}

void wireless_init(const settings_t *settings) {
    s_settings = *settings;

    if (s_settings.wifi_ssid[0] == '\0') {
        // ネットワーク未設定: 無線を初期化せず無効のままとする(§2.5, USB-HIDのみで動作)。
        s_state = WIRELESS_STATE_DISABLED;
        return;
    }

    if (cyw43_arch_init() != 0) {
        // 初期化失敗もUSB経路には影響を出さず、無線を無効のまま扱う。
        s_state = WIRELESS_STATE_DISABLED;
        return;
    }
    cyw43_arch_enable_sta_mode();

    s_pcb = udp_new();
    if (s_pcb == NULL) {
        cyw43_arch_deinit();
        s_state = WIRELESS_STATE_DISABLED;
        return;
    }

    cyw43_arch_lwip_begin();
    udp_bind(s_pcb, IP_ANY_TYPE, WIRELESS_UDP_PORT);
    udp_recv(s_pcb, wireless_udp_recv_cb, NULL);
    cyw43_arch_lwip_end();

    s_peer_known = false;
    s_backoff_ms = WIRELESS_RECONNECT_BACKOFF_MIN_MS;
    s_next_connect_attempt_ms = 0;
    s_state = WIRELESS_STATE_CONNECTING;
}

bool wireless_is_enabled(void) {
    return s_state != WIRELESS_STATE_DISABLED;
}

void wireless_task(void) {
    if (s_state == WIRELESS_STATE_DISABLED) {
        return;
    }

    if (cyw43_wifi_link_status(&cyw43_state, CYW43_ITF_STA) == CYW43_LINK_UP) {
        s_state = WIRELESS_STATE_CONNECTED;
        s_backoff_ms = WIRELESS_RECONNECT_BACKOFF_MIN_MS; // 接続確立でバックオフをリセット
        return;
    }

    s_state = WIRELESS_STATE_CONNECTING;

    uint64_t now = wireless_now_ms();
    if (now < s_next_connect_attempt_ms) {
        return; // バックオフ待機中(ブロックしない, メインループを止めない)
    }

    // 非同期接続要求。結果はcyw43_wifi_link_status経由で次回以降のtask呼び出しで確認する
    // ため、ここではブロックしない。
    cyw43_arch_wifi_connect_async(s_settings.wifi_ssid, s_settings.wifi_password, CYW43_AUTH_WPA2_AES_PSK);

    s_next_connect_attempt_ms = now + s_backoff_ms;
    if (s_backoff_ms < WIRELESS_RECONNECT_BACKOFF_MAX_MS) {
        s_backoff_ms *= 2;
        if (s_backoff_ms > WIRELESS_RECONNECT_BACKOFF_MAX_MS) {
            s_backoff_ms = WIRELESS_RECONNECT_BACKOFF_MAX_MS;
        }
    }
}

void wireless_send_state(const module_state_array_t *states, uint8_t seq) {
    if (s_state != WIRELESS_STATE_CONNECTED || !s_peer_known) {
        // 未接続、または送信先(PC側)が未確定: usb_hid_send_stateと同様にドロップする
        // (バッファリング・ブロックしない)。
        return;
    }

    uint8_t frame[WIRELESS_FRAME_STATE_LEN];
    wireless_codec_build_state_frame(states, seq, frame);

    struct pbuf *p = pbuf_alloc(PBUF_TRANSPORT, sizeof(frame), PBUF_RAM);
    if (p == NULL) {
        return; // 確保失敗時もドロップするのみ(次回送出で回復)
    }
    memcpy(p->payload, frame, sizeof(frame));

    cyw43_arch_lwip_begin();
    udp_sendto(s_pcb, p, &s_peer_addr, s_peer_port);
    cyw43_arch_lwip_end();

    pbuf_free(p);
}

#endif // ENABLE_WIRELESS
