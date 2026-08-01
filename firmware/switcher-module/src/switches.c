// switches.h のI/O実装。SW_PINS(SW_1..SW_4)を内部プルアップ付き入力として直読みし、
// Low(=GNDへ落ちている)を押下として扱う。生サンプルは switches_debounce.c (GPIO非依存)
// にそのまま渡し、確定状態のみをここで保持する。
// 実基板 (softswitcher_module_sw4) は4SWを専用GPIOへ直結しているため、旧リビジョンの
// ような行選択によるマトリクス走査は行わない。

#include "switches.h"

#include "ch32fun.h"

#include "module_config.h"
#include "switches_debounce.h"

// module_config.h のピンエンコード (ch32fun非依存) が ch32fun のピン定数と一致することを
// ビルド時に検証する (実配線: SW_1=19pin PD2 / SW_2=1pin PD4 / SW_3=20pin PD3 / SW_4=2pin PD5)。
_Static_assert(MODULE_PIN(MODULE_PORT_D, 2) == PD2, "MODULE_PIN encoding must match ch32fun PD2");
_Static_assert(MODULE_PIN(MODULE_PORT_D, 3) == PD3, "MODULE_PIN encoding must match ch32fun PD3");
_Static_assert(MODULE_PIN(MODULE_PORT_D, 4) == PD4, "MODULE_PIN encoding must match ch32fun PD4");
_Static_assert(MODULE_PIN(MODULE_PORT_D, 5) == PD5, "MODULE_PIN encoding must match ch32fun PD5");
_Static_assert(MODULE_PIN(MODULE_PORT_A, 1) == PA1, "MODULE_PIN encoding must match ch32fun PA1");
_Static_assert(MODULE_PIN(MODULE_PORT_C, 1) == PC1, "MODULE_PIN encoding must match ch32fun PC1");

static switches_debouncer_t debouncer;
static uint8_t current_state;

static uint8_t read_raw(void) {
    uint8_t raw = 0;

    for (int i = 0; i < SW_COUNT; i++) {
        if (!funDigitalRead(SW_PINS[i])) { // アクティブLow
            raw |= (uint8_t)(1u << i);
        }
    }
    return raw;
}

void switches_init(void) {
    for (int i = 0; i < SW_COUNT; i++) {
        funPinMode(SW_PINS[i], GPIO_CFGLR_IN_PUPD);
        funDigitalWrite(SW_PINS[i], FUN_HIGH); // プルアップを選択
    }

    switches_debouncer_init(&debouncer);
    current_state = 0;
}

void switches_task(void) {
    uint8_t raw = read_raw();
    current_state = switches_state_to_register(switches_debouncer_sample(&debouncer, raw));
}

uint8_t switches_get_state(void) {
    return current_state;
}
