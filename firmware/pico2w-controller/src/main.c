#include <stdio.h>

#include "pico/stdlib.h"

#include "buttons.h"
#include "config.h"
#include "ddc_tally.h"
#include "usb_link.h"

int main(void) {
    stdio_init_all();

    buttons_init();
    usb_link_init();
    ddc_tally_init();

    printf("pico2w-controller: controller_id=%s\n", get_controller_id());

    while (true) {
        buttons_task();

        button_press_event_t press;
        while (buttons_pop_event(&press)) {
            usb_link_send_button_event(press.button_id, press.timestamp_ms);
        }

        ddc_tally_task();

        tally_state_event_t tally_event;
        while (ddc_tally_pop_event(&tally_event)) {
            usb_link_send_tally_event(tally_event.state, tally_event.timestamp_ms);
        }

        usb_link_task();
        sleep_ms(BUTTON_SCAN_INTERVAL_MS);
    }
}
