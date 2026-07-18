// 出力レポート0x02(HID_REPORT_ID_BACKLIGHT_OUT)のバイト列パース(I2C/GPIO非依存)。
// Pico SDKに依存しないためホストのgccでそのままビルド・テストできる
// (test/test_backlight.c参照, state_agg.c/buttons_debounce.cの分離パターンを踏襲)。

#include "backlight.h"

bool backlight_parse_output_report(const uint8_t *report, size_t len, backlight_command_t *out_cmd) {
    if (report == NULL || out_cmd == NULL || len != HID_REPORT_BACKLIGHT_OUT_LEN) {
        return false;
    }

    uint8_t module_index = report[0];
    if (module_index >= MAX_MODULES) {
        return false; // 範囲外module_indexは棄却
    }

    out_cmd->module_index = module_index;
    for (int i = 0; i < MODULE_REG_BACKLIGHT_LEN; i++) {
        out_cmd->rgb[i] = report[1 + i]; // バイト順はそのまま保持(GRB変換はモジュール側)
    }
    return true;
}
