// デバッグ用 fake モジュール層のI/O部 (FAKE_MODULES ビルドのみ, i2c_modules.c の差し替え)。
//
// i2c_modules.h の契約をそのまま実装するが、I2Cバスは一切触らない。モジュールの状態は
// PCからのデバッグ用出力レポート0x04 (fake_modules_on_debug_report) が供給し、
// バックライト配布は実機I2C書込の代わりに内部へ記録して featureレポート0x05
// (fake_modules_build_backlight_feature_report) でPCから読み出せるようにする。
//
// main.c から見た振る舞いは i2c_modules.c と同一のため、メインループ側には
// FAKE_MODULES の分岐が一切必要ない。

#include "i2c_modules.h"

#include <string.h>

#include "pico/bootrom.h"
#include "pico/stdlib.h"

#include "config.h"
#include "fake_modules.h"

// i2c_modules.c と同じく、メインループの単一コンテキストからのみ触られる前提で排他制御は
// 行わない。TinyUSBのSET_REPORT/GET_REPORTコールバック(fake_modules_* の呼び出し元)も
// main.c が回す tud_task() から呼ばれるため、同一コンテキストとなる。
static module_state_array_t shared_states;

// backlight_task() が最後に配布したバックライト。PCはfeatureレポート0x05で読み出す。
static uint8_t last_backlight_module = FAKE_BACKLIGHT_MODULE_NONE;
static uint8_t last_backlight_rgb[MODULE_REG_BACKLIGHT_LEN];

// 出力レポート0x06によるBOOTSEL再起動の予約。
static bool reboot_requested;

void i2c_modules_init(void) {
    for (int i = 0; i < MAX_MODULES; i++) {
        shared_states.modules[i].present = false;
        shared_states.modules[i].switches = 0;
        for (int v = 0; v < MODULE_VR_COUNT; v++) {
            shared_states.modules[i].vr[v] = 0;
        }
    }
    last_backlight_module = FAKE_BACKLIGHT_MODULE_NONE;
    memset(last_backlight_rgb, 0, sizeof(last_backlight_rgb));
}

void i2c_modules_poll(void) {
    // ポーリングすべきI2Cバスが無い。モジュール状態はPCからのデバッグ用出力レポート0x04が
    // 供給するため、通常は行うことが無い(main.c は i2c_modules.c と同じ呼び出し順のまま)。
    if (reboot_requested) {
        // ホストがSET_REPORTの完了を受け取れるよう少し待ってから落とす。reset_usb_boot は
        // 戻らない(そのままBOOTSELで再起動する)。
        sleep_ms(50);
        reset_usb_boot(0, 0);
    }
}

void i2c_modules_get_state(module_state_array_t *out_states) {
    *out_states = shared_states;
}

bool i2c_modules_write_backlight(uint8_t module_index, const uint8_t rgb[MODULE_REG_BACKLIGHT_LEN]) {
    if (module_index >= MAX_MODULES) {
        return false; // i2c_modules.c と同じく範囲外は書込を行わず false
    }

    // 実機I2C書込の代わりに配布内容を記録する。PCはfeatureレポート0x05で読み出して、
    // 出力レポート0x02 → 受領キュー → backlight_task() の配布経路が動いたことを確認できる。
    last_backlight_module = module_index;
    memcpy(last_backlight_rgb, rgb, MODULE_REG_BACKLIGHT_LEN);
    return true;
}

void fake_modules_on_debug_report(const uint8_t *buffer, uint16_t bufsize) {
    fake_module_command_t cmd;
    if (!fake_modules_parse_debug_report(buffer, bufsize, &cmd)) {
        return; // 長さ不一致/範囲外 module_index は棄却する
    }
    fake_modules_apply(&cmd, &shared_states);
}

void fake_modules_on_reboot_report(const uint8_t *buffer, uint16_t bufsize) {
    // 0x04 と同じくホストのゼロパディングを許容するため長さは「以上」で見る。
    if (buffer == NULL || bufsize < HID_REPORT_FAKE_REBOOT_OUT_LEN) {
        return;
    }
    if (buffer[0] != FAKE_REBOOT_MAGIC) {
        return; // マジック不一致は無視(誤爆防止)
    }
    reboot_requested = true;
}

uint16_t fake_modules_build_backlight_feature_report(uint8_t *buffer, uint16_t reqlen) {
    if (buffer == NULL || reqlen < HID_REPORT_FAKE_BACKLIGHT_FEATURE_LEN) {
        return 0;
    }
    buffer[0] = last_backlight_module;
    memcpy(&buffer[1], last_backlight_rgb, MODULE_REG_BACKLIGHT_LEN);
    return HID_REPORT_FAKE_BACKLIGHT_FEATURE_LEN;
}
