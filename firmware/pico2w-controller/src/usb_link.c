#include "usb_link.h"

#include <stdio.h>

#include "tusb.h"

#include "config.h"

void usb_link_init(void) {
    // CDCの初期化は pico_enable_stdio_usb(CMakeLists参照) 側で完結しており、
    // ここで追加のTinyUSB初期化は不要。
}

void usb_link_task(void) {
    // stdio_usb は低優先度IRQで tud_task() を回しているため、明示的な
    // ポーリング呼び出しは不要 (メインループをブロックしない)。
}

void usb_link_send_button_event(uint8_t button_id, uint64_t timestamp_ms) {
    if (!tud_cdc_connected()) {
        return;
    }
    printf("{\"event\":\"button_press\",\"data\":{\"controller_id\":\"%s\",\"button_id\":%u,\"timestamp\":%llu}}\n",
           get_controller_id(), (unsigned int)button_id, (unsigned long long)timestamp_ms);
}

static const char *tally_state_to_string(tally_state_t state) {
    switch (state) {
        case TALLY_STATE_PROGRAM:
            return "program";
        case TALLY_STATE_PREVIEW:
            return "preview";
        case TALLY_STATE_OFF:
        default:
            return "off";
    }
}

void usb_link_send_tally_event(tally_state_t state, uint64_t timestamp_ms) {
    if (!tud_cdc_connected()) {
        return;
    }
    printf("{\"event\":\"tally\",\"data\":{\"controller_id\":\"%s\",\"state\":\"%s\",\"timestamp\":%llu}}\n",
           get_controller_id(), tally_state_to_string(state), (unsigned long long)timestamp_ms);
}
