#ifndef PICO2W_CONTROLLER_WIRELESS_H
#define PICO2W_CONTROLLER_WIRELESS_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "backlight.h"
#include "config.h"
#include "settings.h"
#include "state_agg.h"

// Wi-Fi/BTワイヤレス制御チャネル(親仕様書 §2.5, 任意)。USB-HIDが使えない/無線運用時の
// 代替経路として、SW/VR状態送信(入力レポート0x01相当)・バックライト受領(出力レポート
// 0x02相当)をHIDと同一セマンティクスで提供する。USB直結が主経路、本チャネルは代替であり
// (main.c参照)、ネットワーク未設定時(settings.wifi_ssidが空)は完全に無効化される。

// ---- フレーム形式 (UDP, §2.5「例: WebSocket/UDP over Wi-Fi」) ----
// 1メッセージ = 1 UDPデータグラム = [type(1)] + [payload]。type にはHIDレポートIDを
// そのまま流用する。payloadはHIDレポートのパッキング/パース純粋関数
// (state_agg_pack / backlight_parse_output_report)をそのまま共用し、二重実装しない
// (.claude/review-patterns.md「設計・責務分離」)。
#define WIRELESS_MSG_TYPE_STATE HID_REPORT_ID_STATE_IN
#define WIRELESS_MSG_TYPE_BACKLIGHT HID_REPORT_ID_BACKLIGHT_OUT

#define WIRELESS_FRAME_STATE_LEN (1 + HID_REPORT_STATE_IN_LEN)
#define WIRELESS_FRAME_BACKLIGHT_LEN (1 + HID_REPORT_BACKLIGHT_OUT_LEN)

// 制御チャネルのUDPポート。親仕様書 §4のポート一覧(8080/9000/9999/9910)と衝突しない値。
#define WIRELESS_UDP_PORT 9200

// ---- 純粋部 (CYW43/lwIP非依存, ホストテスト対象: wireless_codec.c) ----

// 集約状態をSTATEフレーム([type]+state_agg_packと同一パッキング)へエンコードする。
// out は最低 WIRELESS_FRAME_STATE_LEN バイト必要。
void wireless_codec_build_state_frame(const module_state_array_t *states, uint8_t seq, uint8_t *out);

// 受信フレームがBACKLIGHTフレーム(type一致+長さ一致)かを検証し、ペイロードを
// backlight_parse_output_report(backlight_codec.c)へ委譲してパースする(二重実装しない)。
bool wireless_codec_parse_backlight_frame(const uint8_t *frame, size_t len, backlight_command_t *out_cmd);

#ifdef ENABLE_WIRELESS

// ---- I/O部 (CYW43/lwIP依存, RP2350依存: wireless.c) ----
// ビルドオプション -DENABLE_WIRELESS でのみコンパイル対象になる(CMakeLists.txt参照)。
// 無効ビルドではリンクサイズ/依存が増えない。

// settings(P2-002)からWi-Fi資格情報を読む。未設定(空SSID)なら初期化せず無効のままとし
// (§2.5「ネットワーク未設定時は無効」)、USB-HIDのみで動作する。設定済みならCYW43初期化・
// Wi-Fi接続(バックオフ付き再接続)・UDPソケットの確立を試みる。
void wireless_init(const settings_t *settings);

// 無線チャネルが有効(資格情報投入済み)かどうか。無効時、main.cは無線送出を一切行わない。
bool wireless_is_enabled(void);

// メインループから継続的に呼び出す。再接続バックオフ・受信ポーリング(バックライト受領)を
// 行う。ブロッキングしないため、無線側の切断/障害がUSB経路・I2C集約ポーリングへ波及しない
// (障害隔離, 親仕様書 §4非機能要件)。
void wireless_task(void);

// 集約状態をSTATEフレームへエンコードし、確立済みチャネルへ送出する。無効/未接続時は
// 何もしない(usb_hid_send_stateと同様、バッファリング・ブロックしない)。
void wireless_send_state(const module_state_array_t *states, uint8_t seq);

#endif // ENABLE_WIRELESS

#endif // PICO2W_CONTROLLER_WIRELESS_H
