// settings_codec.c (フラッシュ/HID非依存の純粋関数) のテスト。
// シリアライズ往復、破損検出(マジック/バージョン/CRC/NUL終端)、controller_idフォールバック
// を網羅する。

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "settings.h"

static void test_defaults_roundtrip(void) {
    settings_t settings;
    settings_set_defaults(&settings);

    uint8_t buf[SETTINGS_SERIALIZED_LEN];
    settings_serialize(&settings, buf);

    settings_t restored;
    bool ok = settings_deserialize(buf, sizeof(buf), &restored);
    assert(ok);
    assert(restored.controller_role == SETTINGS_CONTROLLER_ROLE_UNSET);
    for (int i = 0; i < MAX_MODULES; i++) {
        assert(restored.module_hints[i] == SETTINGS_MODULE_HINT_UNSET);
    }
    assert(restored.wifi_ssid[0] == '\0');
    assert(restored.wifi_password[0] == '\0');
}

static void test_populated_roundtrip(void) {
    settings_t settings;
    settings_set_defaults(&settings);
    settings.controller_role = CONTROLLER_ROLE_SUB;
    for (int i = 0; i < MAX_MODULES; i++) {
        settings.module_hints[i] = (uint8_t)i;
    }
    strncpy(settings.wifi_ssid, "studio-net", sizeof(settings.wifi_ssid) - 1);
    strncpy(settings.wifi_password, "hunter2-ish", sizeof(settings.wifi_password) - 1);

    uint8_t buf[SETTINGS_SERIALIZED_LEN];
    settings_serialize(&settings, buf);

    settings_t restored;
    bool ok = settings_deserialize(buf, sizeof(buf), &restored);
    assert(ok);
    assert(restored.controller_role == CONTROLLER_ROLE_SUB);
    for (int i = 0; i < MAX_MODULES; i++) {
        assert(restored.module_hints[i] == (uint8_t)i);
    }
    assert(strcmp(restored.wifi_ssid, "studio-net") == 0);
    assert(strcmp(restored.wifi_password, "hunter2-ish") == 0);
}

static void test_wrong_length_rejected(void) {
    settings_t settings;
    settings_set_defaults(&settings);
    uint8_t buf[SETTINGS_SERIALIZED_LEN];
    settings_serialize(&settings, buf);

    settings_t restored;
    assert(!settings_deserialize(buf, sizeof(buf) - 1, &restored));
    assert(!settings_deserialize(buf, 0, &restored));
}

static void test_corrupted_magic_rejected(void) {
    settings_t settings;
    settings_set_defaults(&settings);
    uint8_t buf[SETTINGS_SERIALIZED_LEN];
    settings_serialize(&settings, buf);
    buf[0] ^= 0xFFu; // マジックの先頭バイトを破壊

    settings_t restored;
    assert(!settings_deserialize(buf, sizeof(buf), &restored));
}

static void test_wrong_version_rejected(void) {
    settings_t settings;
    settings_set_defaults(&settings);
    uint8_t buf[SETTINGS_SERIALIZED_LEN];
    settings_serialize(&settings, buf);
    buf[4] = 0xEFu; // 未知バージョンへ書き換え(CRCは検証前に弾かれるため無視されてよい)

    settings_t restored;
    assert(!settings_deserialize(buf, sizeof(buf), &restored));
}

static void test_corrupted_crc_rejected(void) {
    settings_t settings;
    settings_set_defaults(&settings);
    settings.controller_role = CONTROLLER_ROLE_MAIN;
    uint8_t buf[SETTINGS_SERIALIZED_LEN];
    settings_serialize(&settings, buf);
    buf[6] ^= 0x01u; // module_hints先頭バイトを破壊(CRCは更新しない)

    settings_t restored;
    assert(!settings_deserialize(buf, sizeof(buf), &restored));
}

static void test_ssid_not_null_terminated_rejected(void) {
    settings_t settings;
    settings_set_defaults(&settings);
    memset(settings.wifi_ssid, 'A', sizeof(settings.wifi_ssid)); // 全バイトを非NULで埋める

    uint8_t buf[SETTINGS_SERIALIZED_LEN];
    settings_serialize(&settings, buf);

    settings_t restored;
    assert(!settings_deserialize(buf, sizeof(buf), &restored));
}

static void test_resolve_controller_id_prefers_settings(void) {
    settings_t settings;
    settings_set_defaults(&settings);

    settings.controller_role = CONTROLLER_ROLE_MAIN;
    assert(strcmp(settings_resolve_controller_id(&settings), "main") == 0);

    settings.controller_role = CONTROLLER_ROLE_SUB;
    assert(strcmp(settings_resolve_controller_id(&settings), "sub") == 0);
}

static void test_resolve_controller_id_falls_back_when_unset(void) {
    settings_t settings;
    settings_set_defaults(&settings);
    assert(settings.controller_role == SETTINGS_CONTROLLER_ROLE_UNSET);

    // ビルド時 CONTROLLER_ROLE (デフォルトはMAIN, config.h参照) へフォールバックする。
    assert(strcmp(settings_resolve_controller_id(&settings), get_controller_id()) == 0);
}

int main(void) {
    test_defaults_roundtrip();
    test_populated_roundtrip();
    test_wrong_length_rejected();
    test_corrupted_magic_rejected();
    test_wrong_version_rejected();
    test_corrupted_crc_rejected();
    test_ssid_not_null_terminated_rejected();
    test_resolve_controller_id_prefers_settings();
    test_resolve_controller_id_falls_back_when_unset();

    printf("test_settings: OK\n");
    return 0;
}
