// fake_modules_codec.c (デバッグ用 fake モジュール層の純粋部) のテスト。
// デバッグ用出力レポート0x04のパース(長さ不一致/範囲外module_index/SW下位4bit)と、
// モジュール状態配列への適用(present=false時にSW/VRを保持すること)を網羅する。
//
// このテストは FAKE_MODULES ビルドのみで意味を持つ定数を使うため、Makefile 側で
// -DFAKE_MODULES を付けてビルドする。

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "fake_modules.h"

static void make_valid_report(uint8_t module_index, uint8_t present, uint8_t switches, uint8_t vr1, uint8_t vr2,
                              uint8_t *out) {
    out[0] = module_index;
    out[1] = present;
    out[2] = switches;
    out[3] = vr1;
    out[4] = vr2;
}

static void test_parse_valid_report(void) {
    uint8_t report[HID_REPORT_FAKE_MODULE_OUT_LEN];
    make_valid_report(3, 1, 0x0A, 0x40, 0xC0, report);

    fake_module_command_t cmd;
    assert(fake_modules_parse_debug_report(report, sizeof(report), &cmd));
    assert(cmd.module_index == 3);
    assert(cmd.present);
    assert(cmd.switches == 0x0A);
    assert(cmd.vr[0] == 0x40);
    assert(cmd.vr[1] == 0xC0);
}

static void test_switches_masked_to_low_nibble(void) {
    // STATE(0x00)[0] と同じく下位4bitのみが有効。上位bitは切り捨てる。
    uint8_t report[HID_REPORT_FAKE_MODULE_OUT_LEN];
    make_valid_report(0, 1, 0xFF, 0, 0, report);

    fake_module_command_t cmd;
    assert(fake_modules_parse_debug_report(report, sizeof(report), &cmd));
    assert(cmd.switches == 0x0F);
}

static void test_present_zero_parsed_as_false(void) {
    uint8_t report[HID_REPORT_FAKE_MODULE_OUT_LEN];
    make_valid_report(0, 0, 0x0F, 0x11, 0x22, report);

    fake_module_command_t cmd;
    assert(fake_modules_parse_debug_report(report, sizeof(report), &cmd));
    assert(!cmd.present);
}

static void test_module_index_boundary_accepted(void) {
    uint8_t report[HID_REPORT_FAKE_MODULE_OUT_LEN];
    make_valid_report((uint8_t)(MAX_MODULES - 1), 1, 0, 0, 0, report);

    fake_module_command_t cmd;
    assert(fake_modules_parse_debug_report(report, sizeof(report), &cmd));
    assert(cmd.module_index == MAX_MODULES - 1);
}

static void test_module_index_out_of_range_rejected(void) {
    uint8_t report[HID_REPORT_FAKE_MODULE_OUT_LEN];
    make_valid_report((uint8_t)MAX_MODULES, 1, 0, 0, 0, report);

    fake_module_command_t cmd;
    assert(!fake_modules_parse_debug_report(report, sizeof(report), &cmd));
}

static void test_short_report_rejected(void) {
    uint8_t report[HID_REPORT_FAKE_MODULE_OUT_LEN];
    make_valid_report(0, 1, 0, 0, 0, report);

    fake_module_command_t cmd;
    assert(!fake_modules_parse_debug_report(report, sizeof(report) - 1, &cmd));
    assert(!fake_modules_parse_debug_report(report, 0, &cmd));
}

static void test_host_padded_report_accepted(void) {
    // Windowsは短い出力レポートをデバイスの最大出力レポート長(=0x02の13バイト)まで
    // ゼロパディングして送ってくる。余剰バイトごと受理できなければ0x04が一切届かない。
    uint8_t padded[HID_REPORT_BACKLIGHT_OUT_LEN];
    memset(padded, 0, sizeof(padded));
    make_valid_report(5, 1, 0x09, 0x77, 0x88, padded);

    fake_module_command_t cmd;
    assert(fake_modules_parse_debug_report(padded, sizeof(padded), &cmd));
    assert(cmd.module_index == 5);
    assert(cmd.present);
    assert(cmd.switches == 0x09);
    assert(cmd.vr[0] == 0x77);
    assert(cmd.vr[1] == 0x88);
}

static void test_apply_sets_state(void) {
    module_state_array_t states;
    memset(&states, 0, sizeof(states));

    fake_module_command_t cmd = {.module_index = 2, .present = true, .switches = 0x05, .vr = {0x33, 0x77}};
    fake_modules_apply(&cmd, &states);

    assert(states.modules[2].present);
    assert(states.modules[2].switches == 0x05);
    assert(states.modules[2].vr[0] == 0x33);
    assert(states.modules[2].vr[1] == 0x77);

    // 他モジュールは触らない
    assert(!states.modules[0].present);
    assert(states.modules[3].switches == 0);
}

static void test_apply_absent_keeps_last_sw_vr(void) {
    // 実機の i2c_modules_poll() は不通時に present のみ落とし、直前のSW/VR値は保持する。
    // fake 層もその挙動へ揃える。
    module_state_array_t states;
    memset(&states, 0, sizeof(states));

    fake_module_command_t on = {.module_index = 1, .present = true, .switches = 0x0C, .vr = {0x10, 0x20}};
    fake_modules_apply(&on, &states);

    fake_module_command_t off = {.module_index = 1, .present = false, .switches = 0x00, .vr = {0x00, 0x00}};
    fake_modules_apply(&off, &states);

    assert(!states.modules[1].present);
    assert(states.modules[1].switches == 0x0C);
    assert(states.modules[1].vr[0] == 0x10);
    assert(states.modules[1].vr[1] == 0x20);
}

static void test_apply_out_of_range_ignored(void) {
    module_state_array_t states;
    memset(&states, 0, sizeof(states));

    fake_module_command_t cmd = {.module_index = MAX_MODULES, .present = true, .switches = 0x0F, .vr = {1, 2}};
    fake_modules_apply(&cmd, &states);

    for (int i = 0; i < MAX_MODULES; i++) {
        assert(!states.modules[i].present);
    }
}

int main(void) {
    test_parse_valid_report();
    test_switches_masked_to_low_nibble();
    test_present_zero_parsed_as_false();
    test_module_index_boundary_accepted();
    test_module_index_out_of_range_rejected();
    test_short_report_rejected();
    test_host_padded_report_accepted();
    test_apply_sets_state();
    test_apply_absent_keeps_last_sw_vr();
    test_apply_out_of_range_ignored();

    printf("test_fake_modules: OK\n");
    return 0;
}
