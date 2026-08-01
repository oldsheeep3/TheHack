// module_config.h の定数実体。ch32v003fun (Pico SDK相当) 非依存の純粋データのみを置く
// (ホストテストから直接ビルド・参照可能, test/test_module_config.c 参照)。
// 値は基板 softswitcher_module_sw4 (CH32V003F4P6, TSSOP-20) の実配線。

#include "module_config.h"

// SW_1(19pin PD2), SW_2(1pin PD4), SW_3(20pin PD3), SW_4(2pin PD5)。
const uint8_t SW_PINS[SW_COUNT] = {
    MODULE_PIN(MODULE_PORT_D, 2),
    MODULE_PIN(MODULE_PORT_D, 4),
    MODULE_PIN(MODULE_PORT_D, 3),
    MODULE_PIN(MODULE_PORT_D, 5),
};

// VOL1 = 5pin(PA1) = ADCチャネル1、VOL2 = 6pin(PA2) = ADCチャネル0。
const uint8_t VR_ADC_CHANNELS[VR_COUNT] = {1, 0};

// LED_DATA = 14pin(PC4)。
const uint8_t BACKLIGHT_DATA_PIN = MODULE_PIN(MODULE_PORT_C, 4);
