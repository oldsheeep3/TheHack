#include <stdio.h>

#include "hardware/i2c.h"
#include "pico/stdlib.h"

#include "config.h"

// 各モジュールは後続タスクで実装される。weak属性により未実装時は no-op として動作する。
__attribute__((weak)) void buttons_init(void) {}
__attribute__((weak)) void buttons_task(void) {}
__attribute__((weak)) void usb_link_init(void) {}
__attribute__((weak)) void usb_link_task(void) {}
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
        usb_link_task();
        ddc_tally_task();
        tight_loop_contents();
    }
}
