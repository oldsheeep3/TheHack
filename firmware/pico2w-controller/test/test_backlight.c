// backlight_codec.c (I2C/GPIO非依存の純粋関数) のテスト。
// 出力レポート0x02のRGBパース、範囲外module_indexの棄却、バイト順保持を網羅する。

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "backlight.h"

static void make_valid_report(uint8_t module_index, uint8_t *out) {
    out[0] = module_index;
    for (int i = 0; i < MODULE_REG_BACKLIGHT_LEN; i++) {
        out[1 + i] = (uint8_t)(i + 1); // 1,2,3,...,12
    }
}

static void test_parse_valid_report(void) {
    uint8_t report[HID_REPORT_BACKLIGHT_OUT_LEN];
    make_valid_report(2, report);

    backlight_command_t cmd;
    bool ok = backlight_parse_output_report(report, sizeof(report), &cmd);
    assert(ok);
    assert(cmd.module_index == 2);
    for (int i = 0; i < MODULE_REG_BACKLIGHT_LEN; i++) {
        assert(cmd.rgb[i] == (uint8_t)(i + 1));
    }
}

static void test_rgb_byte_order_is_preserved(void) {
    // SK6812のGRB変換はモジュール側で行うため、Picoはバイト順を変換してはならない。
    uint8_t report[HID_REPORT_BACKLIGHT_OUT_LEN];
    report[0] = 0;
    uint8_t expected[MODULE_REG_BACKLIGHT_LEN] = {0xAA, 0xBB, 0xCC, 0x01, 0x02, 0x03,
                                                    0x10, 0x20, 0x30, 0xFF, 0x00, 0x7F};
    memcpy(&report[1], expected, sizeof(expected));

    backlight_command_t cmd;
    bool ok = backlight_parse_output_report(report, sizeof(report), &cmd);
    assert(ok);
    assert(memcmp(cmd.rgb, expected, sizeof(expected)) == 0);
}

static void test_module_index_boundary_accepted(void) {
    uint8_t report[HID_REPORT_BACKLIGHT_OUT_LEN];
    make_valid_report((uint8_t)(MAX_MODULES - 1), report);

    backlight_command_t cmd;
    bool ok = backlight_parse_output_report(report, sizeof(report), &cmd);
    assert(ok);
    assert(cmd.module_index == MAX_MODULES - 1);
}

static void test_module_index_out_of_range_rejected(void) {
    uint8_t report[HID_REPORT_BACKLIGHT_OUT_LEN];
    make_valid_report((uint8_t)MAX_MODULES, report);

    backlight_command_t cmd;
    bool ok = backlight_parse_output_report(report, sizeof(report), &cmd);
    assert(!ok);
}

static void test_wrong_length_rejected(void) {
    uint8_t report[HID_REPORT_BACKLIGHT_OUT_LEN];
    make_valid_report(0, report);

    backlight_command_t cmd;
    assert(!backlight_parse_output_report(report, sizeof(report) - 1, &cmd));
    assert(!backlight_parse_output_report(report, sizeof(report) + 1, &cmd));
    assert(!backlight_parse_output_report(report, 0, &cmd));
}

int main(void) {
    test_parse_valid_report();
    test_rgb_byte_order_is_preserved();
    test_module_index_boundary_accepted();
    test_module_index_out_of_range_rejected();
    test_wrong_length_rejected();

    printf("test_backlight: OK\n");
    return 0;
}
