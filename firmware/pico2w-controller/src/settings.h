#ifndef PICO2W_CONTROLLER_SETTINGS_H
#define PICO2W_CONTROLLER_SETTINGS_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "config.h"

// ---- 設定データ (PC投入, 親仕様書 §4.6) ----
//
// HIDフィーチャーレポート(HID_REPORT_ID_SETTINGS_FEATURE)はUSB Full-Speedの制御転送で
// やり取りされ、TinyUSBの制御バッファ(CFG_TUD_ENDPOINT0_SIZE=64バイト)に収まる必要が
// あるため、SETTINGS_SERIALIZED_LEN は64バイト未満に収める設計とする。SSID/パスワードは
// その制約内で以下の長さに切り詰める(より長い資格情報が必要な場合は複数レポートへの
// 分割プロトコルへの拡張が必要, 本タスクでは非対応)。
#define SETTINGS_WIFI_SSID_LEN 20     // NUL込み(19文字まで)
#define SETTINGS_WIFI_PASSWORD_LEN 20 // NUL込み(19文字まで)

#define SETTINGS_CONTROLLER_ROLE_UNSET 0xFFu
#define SETTINGS_MODULE_HINT_UNSET 0xFFu

typedef struct {
    uint8_t controller_role; // CONTROLLER_ROLE_MAIN/SUB、またはSETTINGS_CONTROLLER_ROLE_UNSET
    uint8_t module_hints[MAX_MODULES]; // モジュール割付ヒント(意味論はPC側で定義。SETTINGS_MODULE_HINT_UNSET=未設定)
    char wifi_ssid[SETTINGS_WIFI_SSID_LEN];
    char wifi_password[SETTINGS_WIFI_PASSWORD_LEN];
} settings_t;

// magic(4) + version(1) + controller_role(1) + module_hints(MAX_MODULES) +
// wifi_ssid + wifi_password + crc32(4) のシリアライズ長。
#define SETTINGS_SERIALIZED_LEN \
    (4 + 1 + 1 + MAX_MODULES + SETTINGS_WIFI_SSID_LEN + SETTINGS_WIFI_PASSWORD_LEN + 4)

// ---- 純粋部 (フラッシュ/HID非依存, ホストテスト対象: settings_codec.c) ----

// 全フィールド未設定のデフォルト設定を返す。
void settings_set_defaults(settings_t *settings);

// settings をマジック+バージョン+CRC32付きのバイト列へシリアライズする。
// out は最低 SETTINGS_SERIALIZED_LEN バイト必要。
void settings_serialize(const settings_t *settings, uint8_t *out);

// シリアライズ済みバイト列を検証(マジック/バージョン/CRC32/NUL終端)する。妥当な場合のみ
// out_settings へデシリアライズして true を返す。破損・未初期化・長さ不一致・旧版は
// false を返す(呼び出し側はデフォルト設定にフォールバックすること)。
bool settings_deserialize(const uint8_t *data, size_t len, settings_t *out_settings);

// controller_id文字列("main"/"sub")を返す。settings->controller_role が設定済みなら
// それを優先し、未設定(SETTINGS_CONTROLLER_ROLE_UNSET)ならビルド時 CONTROLLER_ROLE
// (get_controller_id(), config.h)にフォールバックする(親仕様書 §4.6)。
const char *settings_resolve_controller_id(const settings_t *settings);

// ---- I/O部 (フラッシュ永続化 + HIDフィーチャーレポート結線, RP2350依存: settings.c) ----

// フラッシュから設定を読み出す。未初期化/破損時はデフォルト設定を返す。
void settings_load(settings_t *out_settings);

// 設定をフラッシュへ保存する(セクタ消去+書込)。
void settings_save(const settings_t *settings);

// HIDフィーチャーレポートのSET_REPORT受領。検証(settings_deserialize)に成功した場合
// のみフラッシュへ保存する(不正なデータで上書きしない)。
void settings_on_feature_report(const uint8_t *data, uint16_t len);

// HIDフィーチャーレポートのGET_REPORT応答。フラッシュの現在設定をシリアライズして
// out へ書き込み、書き込んだバイト数を返す(out_capacity不足時は0)。
uint16_t settings_build_feature_report(uint8_t *out, uint16_t out_capacity);

#endif // PICO2W_CONTROLLER_SETTINGS_H
