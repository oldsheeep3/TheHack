#include <stdio.h>

#include "pico/stdlib.h"

#include "backlight.h"
#include "config.h"
#include "i2c_modules.h"
#include "settings.h"
#include "state_agg.h"
#include "usb_hid.h"

int main(void) {
    stdio_init_all();

    i2c_modules_init();
    backlight_init();

    // 設定(controller_id/モジュール割付ヒント/ネットワーク資格情報)はUSB列挙より前にロードし、
    // HIDシリアル文字列へ解決済みのcontroller_idを反映する(親仕様書 §4.1/§4.6)。
    settings_t settings;
    settings_load(&settings);
    usb_hid_set_controller_id(settings_resolve_controller_id(&settings));
    usb_hid_init();

    printf("pico2w-controller: controller_id=%s\n", settings_resolve_controller_id(&settings));

    module_state_array_t prev_states = {0};
    uint8_t seq = 0;
    uint64_t last_send_ms = 0;

    while (true) {
        i2c_modules_poll();

        module_state_array_t states;
        i2c_modules_get_state(&states);

        bool should_send = false;
        for (int i = 0; i < MAX_MODULES; i++) {
            if (states.modules[i].present != prev_states.modules[i].present ||
                states.modules[i].switches != prev_states.modules[i].switches) {
                should_send = true;
            }
            for (int v = 0; v < MODULE_VR_COUNT; v++) {
                if (state_agg_vr_should_report(prev_states.modules[i].vr[v], states.modules[i].vr[v])) {
                    should_send = true;
                }
            }
        }

        uint64_t now_ms = time_us_64() / 1000;
        if (should_send || (now_ms - last_send_ms) >= HID_STATE_SEND_INTERVAL_MS) {
            usb_hid_send_state(&states, seq);
            seq = (uint8_t)(seq + 1);
            prev_states = states;
            last_send_ms = now_ms;
        }

        // バックライト書込は1回の呼び出しで最大1件のみ消費するため、STATE集約の
        // レイテンシ(数ms)を大きく阻害しない(§2.3/親仕様書§4非機能要件)。
        backlight_task();
        usb_hid_task();
        sleep_ms(MODULE_POLL_INTERVAL_MS);
    }
}
