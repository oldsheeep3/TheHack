#include <stdio.h>

#include "hardware/i2c.h"
#include "pico/stdlib.h"

#include "buttons.h"
#include "config.h"
#include "usb_link.h"

// ddc_tally は後続タスクで実装される。weak属性により未実装時は no-op として動作する。
__attribute__((weak)) void ddc_tally_init(void) {}
__attribute__((weak)) void ddc_tally_task(void) {}

static void tally_led_init(void) {
    gpio_init(TALLY_LED_RED_PIN);
    gpio_set_dir(TALLY_LED_RED_PIN, GPIO_OUT);
    gpio_init(TALLY_LED_GREEN_PIN);
    gpio_set_dir(TALLY_LED_GREEN_PIN, GPIO_OUT);
}

static void ddc_i2c_init(void) {
    i2c_init(DDC_I2C_PORT, DDC_I2C_BAUDRATE_HZ);
    gpio_set_function(DDC_I2C_SDA_PIN, GPIO_FUNC_I2C);
    gpio_set_function(DDC_I2C_SCL_PIN, GPIO_FUNC_I2C);
}

int main(void) {
    stdio_init_all();
    tally_led_init();
    ddc_i2c_init();

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

        usb_link_task();
        ddc_tally_task();
        sleep_ms(BUTTON_SCAN_INTERVAL_MS);
    }
}
