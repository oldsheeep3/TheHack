// プレースホルダ・テスト: config.h の定数と get_controller_id() が
// Pico SDK非依存でホスト上でも検証できることを確認する。

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "config.h"

int main(void) {
    assert(MAX_MODULES == 8);
    assert(MODULE_SWITCH_COUNT == 4);
    assert(MODULE_VR_COUNT == 2);
    assert(MODULE_I2C_ADDR_BASE == 0x30);
    assert(HID_REPORT_STATE_IN_LEN == 1 + MAX_MODULES + 2 * MAX_MODULES + 1);
    assert(HID_REPORT_BACKLIGHT_OUT_LEN == 1 + 12);
    assert(strcmp(get_controller_id(), "main") == 0);

    printf("test_config: OK\n");
    return 0;
}
