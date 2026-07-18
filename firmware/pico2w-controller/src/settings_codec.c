// 設定構造体のシリアライズ/デシリアライズ + バリデーション(フラッシュ/HID非依存)。
// Pico SDKに依存しないためホストのgccでそのままビルド・テストできる
// (test/test_settings.c参照, state_agg.c/buttons_debounce.cの分離パターンを踏襲)。

#include "settings.h"

#include <string.h>

#include "config.h"

#define SETTINGS_FORMAT_VERSION 1

static const uint8_t SETTINGS_MAGIC[4] = {'P', '2', 'S', 'T'};

// CRC-32/ISO-HDLC (poly 0xEDB88320, init/final xor 0xFFFFFFFF)。設定は数十バイト・
// 低頻度更新のみのためテーブル化はせずビット単位で計算する。
static uint32_t settings_crc32(const uint8_t *data, size_t len) {
    uint32_t crc = 0xFFFFFFFFu;
    for (size_t i = 0; i < len; i++) {
        crc ^= data[i];
        for (int bit = 0; bit < 8; bit++) {
            uint32_t mask = (uint32_t)(-(int32_t)(crc & 1u));
            crc = (crc >> 1) ^ (0xEDB88320u & mask);
        }
    }
    return crc ^ 0xFFFFFFFFu;
}

void settings_set_defaults(settings_t *settings) {
    settings->controller_role = SETTINGS_CONTROLLER_ROLE_UNSET;
    for (int i = 0; i < MAX_MODULES; i++) {
        settings->module_hints[i] = SETTINGS_MODULE_HINT_UNSET;
    }
    memset(settings->wifi_ssid, 0, sizeof(settings->wifi_ssid));
    memset(settings->wifi_password, 0, sizeof(settings->wifi_password));
}

void settings_serialize(const settings_t *settings, uint8_t *out) {
    size_t offset = 0;

    memcpy(&out[offset], SETTINGS_MAGIC, sizeof(SETTINGS_MAGIC));
    offset += sizeof(SETTINGS_MAGIC);

    out[offset++] = SETTINGS_FORMAT_VERSION;
    out[offset++] = settings->controller_role;

    memcpy(&out[offset], settings->module_hints, MAX_MODULES);
    offset += MAX_MODULES;

    memcpy(&out[offset], settings->wifi_ssid, SETTINGS_WIFI_SSID_LEN);
    offset += SETTINGS_WIFI_SSID_LEN;

    memcpy(&out[offset], settings->wifi_password, SETTINGS_WIFI_PASSWORD_LEN);
    offset += SETTINGS_WIFI_PASSWORD_LEN;

    uint32_t crc = settings_crc32(out, offset);
    out[offset++] = (uint8_t)(crc & 0xFFu);
    out[offset++] = (uint8_t)((crc >> 8) & 0xFFu);
    out[offset++] = (uint8_t)((crc >> 16) & 0xFFu);
    out[offset++] = (uint8_t)((crc >> 24) & 0xFFu);
}

bool settings_deserialize(const uint8_t *data, size_t len, settings_t *out_settings) {
    if (data == NULL || out_settings == NULL || len != SETTINGS_SERIALIZED_LEN) {
        return false;
    }

    if (memcmp(data, SETTINGS_MAGIC, sizeof(SETTINGS_MAGIC)) != 0) {
        return false; // マジック不一致: 未初期化/破損したフラッシュ領域
    }

    size_t offset = sizeof(SETTINGS_MAGIC);
    if (data[offset++] != SETTINGS_FORMAT_VERSION) {
        return false; // 旧版/未知バージョン
    }

    uint32_t stored_crc = (uint32_t)data[SETTINGS_SERIALIZED_LEN - 4] |
                           ((uint32_t)data[SETTINGS_SERIALIZED_LEN - 3] << 8) |
                           ((uint32_t)data[SETTINGS_SERIALIZED_LEN - 2] << 16) |
                           ((uint32_t)data[SETTINGS_SERIALIZED_LEN - 1] << 24);
    uint32_t computed_crc = settings_crc32(data, SETTINGS_SERIALIZED_LEN - 4);
    if (stored_crc != computed_crc) {
        return false; // CRC不一致: 破損データ
    }

    uint8_t controller_role = data[offset++];
    if (controller_role != SETTINGS_CONTROLLER_ROLE_UNSET && controller_role != CONTROLLER_ROLE_MAIN &&
        controller_role != CONTROLLER_ROLE_SUB) {
        return false;
    }

    const uint8_t *module_hints = &data[offset];
    offset += MAX_MODULES;

    const uint8_t *ssid_bytes = &data[offset];
    if (memchr(ssid_bytes, '\0', SETTINGS_WIFI_SSID_LEN) == NULL) {
        return false; // NUL終端されていない不正な文字列
    }
    offset += SETTINGS_WIFI_SSID_LEN;

    const uint8_t *password_bytes = &data[offset];
    if (memchr(password_bytes, '\0', SETTINGS_WIFI_PASSWORD_LEN) == NULL) {
        return false;
    }
    offset += SETTINGS_WIFI_PASSWORD_LEN;

    out_settings->controller_role = controller_role;
    memcpy(out_settings->module_hints, module_hints, MAX_MODULES);
    memcpy(out_settings->wifi_ssid, ssid_bytes, SETTINGS_WIFI_SSID_LEN);
    memcpy(out_settings->wifi_password, password_bytes, SETTINGS_WIFI_PASSWORD_LEN);
    return true;
}

const char *settings_resolve_controller_id(const settings_t *settings) {
    if (settings->controller_role == CONTROLLER_ROLE_MAIN) {
        return "main";
    }
    if (settings->controller_role == CONTROLLER_ROLE_SUB) {
        return "sub";
    }
    return get_controller_id(); // 未設定時はビルド時 CONTROLLER_ROLE(config.c)へフォールバック
}
