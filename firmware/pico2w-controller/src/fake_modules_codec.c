// デバッグ用 fake モジュールの純粋部 (Pico SDK非依存, ホストテスト対象)。
// backlight_codec.c と同じく、パース/適用のみを担いI/O(HID送出・状態保持)は
// i2c_modules_fake.c 側が持つ。

#include "fake_modules.h"

bool fake_modules_parse_debug_report(const uint8_t *buffer, uint16_t bufsize, fake_module_command_t *out_cmd) {
    if (buffer == NULL || out_cmd == NULL) {
        return false;
    }
    // backlight_parse_output_report と違い長さ「以上」で受ける。出力レポートが複数ある場合、
    // ホスト(Windows)は短いレポートをデバイスの最大出力レポート長までゼロパディングして
    // 送ってくるため、完全一致で弾くとこのレポートが一切届かなくなる。0x02(13バイト)が
    // 最大長なので、0x04(5バイト)は常にパディングされて到着する。余剰バイトは無視する。
    if (bufsize < HID_REPORT_FAKE_MODULE_OUT_LEN) {
        return false;
    }

    uint8_t module_index = buffer[0];
    if (module_index >= MAX_MODULES) {
        return false;
    }

    out_cmd->module_index = module_index;
    out_cmd->present = (buffer[1] != 0);
    // SW状態はSTATE(0x00)[0]と同じく下位4bitのみが有効(config.h §4.5)。
    out_cmd->switches = (uint8_t)(buffer[2] & 0x0Fu);
    for (int v = 0; v < MODULE_VR_COUNT; v++) {
        out_cmd->vr[v] = buffer[3 + v];
    }
    return true;
}

void fake_modules_apply(const fake_module_command_t *cmd, module_state_array_t *states) {
    if (cmd == NULL || states == NULL) {
        return;
    }
    if (cmd->module_index >= MAX_MODULES) {
        return;
    }

    module_state_t *target = &states->modules[cmd->module_index];
    target->present = cmd->present;
    // present=false(不通)でも直前のSW/VR値は保持する。実機の i2c_modules_poll() が
    // 不通時に present のみ落とす挙動(i2c_modules.c)と揃える。
    if (cmd->present) {
        target->switches = cmd->switches;
        for (int v = 0; v < MODULE_VR_COUNT; v++) {
            target->vr[v] = cmd->vr[v];
        }
    }
}
