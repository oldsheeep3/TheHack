// wireless_codec.c (CYW43/lwIP非依存の純粋関数) のテスト。
// STATEフレームがstate_agg_packと同一パッキングであること、BACKLIGHTフレームの
// type/長さ検証とbacklight_parse_output_reportへの委譲(範囲外module_index棄却含む)を
// 網羅する。CYW43/lwIP依存のI/O部(wireless.c)はホストテスト対象外(README参照)。

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "wireless.h"

static module_state_array_t make_all_present(void) {
    module_state_array_t states;
    memset(&states, 0, sizeof(states));
    for (int i = 0; i < MAX_MODULES; i++) {
        states.modules[i].present = true;
        states.modules[i].switches = (uint8_t)(i & 0x0F);
        states.modules[i].vr[0] = (uint8_t)(i * 10);
        states.modules[i].vr[1] = (uint8_t)(255 - i * 10);
    }
    return states;
}

static void test_state_frame_has_type_prefix(void) {
    module_state_array_t states = make_all_present();
    uint8_t frame[WIRELESS_FRAME_STATE_LEN];
    wireless_codec_build_state_frame(&states, 7, frame);
    assert(frame[0] == WIRELESS_MSG_TYPE_STATE);
}

static void test_state_frame_payload_matches_state_agg_pack(void) {
    // 入力レポート0x01と同一セマンティクス(HID経路と二重実装していないことの回帰確認)。
    module_state_array_t states = make_all_present();
    uint8_t frame[WIRELESS_FRAME_STATE_LEN];
    wireless_codec_build_state_frame(&states, 42, frame);

    uint8_t expected_payload[HID_REPORT_STATE_IN_LEN];
    state_agg_pack(&states, 42, expected_payload);

    assert(memcmp(&frame[1], expected_payload, HID_REPORT_STATE_IN_LEN) == 0);
}

static void make_valid_backlight_frame(uint8_t module_index, uint8_t *out) {
    out[0] = WIRELESS_MSG_TYPE_BACKLIGHT;
    out[1] = module_index;
    for (int i = 0; i < MODULE_REG_BACKLIGHT_LEN; i++) {
        out[2 + i] = (uint8_t)(i + 1);
    }
}

static void test_backlight_frame_parses_valid_frame(void) {
    uint8_t frame[WIRELESS_FRAME_BACKLIGHT_LEN];
    make_valid_backlight_frame(3, frame);

    backlight_command_t cmd;
    bool ok = wireless_codec_parse_backlight_frame(frame, sizeof(frame), &cmd);
    assert(ok);
    assert(cmd.module_index == 3);
    for (int i = 0; i < MODULE_REG_BACKLIGHT_LEN; i++) {
        assert(cmd.rgb[i] == (uint8_t)(i + 1));
    }
}

static void test_backlight_frame_rejects_wrong_type(void) {
    uint8_t frame[WIRELESS_FRAME_BACKLIGHT_LEN];
    make_valid_backlight_frame(0, frame);
    frame[0] = WIRELESS_MSG_TYPE_STATE; // typeがSTATEフレームのもの

    backlight_command_t cmd;
    assert(!wireless_codec_parse_backlight_frame(frame, sizeof(frame), &cmd));
}

static void test_backlight_frame_rejects_wrong_length(void) {
    uint8_t frame[WIRELESS_FRAME_BACKLIGHT_LEN];
    make_valid_backlight_frame(0, frame);

    backlight_command_t cmd;
    assert(!wireless_codec_parse_backlight_frame(frame, sizeof(frame) - 1, &cmd));
    assert(!wireless_codec_parse_backlight_frame(frame, sizeof(frame) + 1, &cmd));
    assert(!wireless_codec_parse_backlight_frame(frame, 0, &cmd));
}

static void test_backlight_frame_delegates_module_index_validation(void) {
    // backlight_parse_output_reportへの委譲により、範囲外module_indexも棄却されること
    // (二重実装せずbacklight_codec.cのバリデーションをそのまま共用していることの確認)。
    uint8_t frame[WIRELESS_FRAME_BACKLIGHT_LEN];
    make_valid_backlight_frame((uint8_t)MAX_MODULES, frame);

    backlight_command_t cmd;
    assert(!wireless_codec_parse_backlight_frame(frame, sizeof(frame), &cmd));
}

int main(void) {
    test_state_frame_has_type_prefix();
    test_state_frame_payload_matches_state_agg_pack();
    test_backlight_frame_parses_valid_frame();
    test_backlight_frame_rejects_wrong_type();
    test_backlight_frame_rejects_wrong_length();
    test_backlight_frame_delegates_module_index_validation();

    printf("test_wireless_codec: OK\n");
    return 0;
}
