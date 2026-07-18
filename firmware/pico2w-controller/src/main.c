#include <stdio.h>

#include "pico/stdlib.h"

#include "config.h"
#include "i2c_modules.h"
#include "state_agg.h"
#include "usb_hid.h"

int main(void) {
    stdio_init_all();

    i2c_modules_init();
    usb_hid_init();

    printf("pico2w-controller: controller_id=%s\n", get_controller_id());

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

        usb_hid_task();
        sleep_ms(MODULE_POLL_INTERVAL_MS);
    }
}
