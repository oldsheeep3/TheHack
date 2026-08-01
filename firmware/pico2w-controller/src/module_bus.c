// module_bus.h の定数実体と、GPIO番号→I2Cコントローラ番号の純粋変換。
// Pico SDK非依存 (ホストの gcc でそのままビルド・テストできる, test/test_module_bus.c)。
//
// RP2040/RP2350 のGPIO機能マルチプレクサでは、I2Cの割当が
//   GPIO 0,1 → i2c0 / 2,3 → i2c1 / 4,5 → i2c0 / 6,7 → i2c1 / ... (4本周期)
//   偶数GPIO = SDA, 奇数GPIO = SCL
// と規則的に決まっているため、バス定義はピン番号だけを持ち、コントローラは算出する。

#include "module_bus.h"

// バス(スロット)はHIDレポートのモジュールスロットへそのまま対応するため、溢れてはならない。
_Static_assert(MODULE_BUS_COUNT <= MAX_MODULES, "MODULE_BUS_COUNT must fit in the HID module slots");

const module_bus_t MODULE_BUSES[MODULE_BUS_COUNT] = MODULE_BUS_PINS_INIT;

uint8_t module_bus_i2c_index_for_pin(uint8_t gpio) {
    return (uint8_t)((gpio / 2u) % 2u);
}

bool module_bus_pins_are_valid(uint8_t sda_pin, uint8_t scl_pin) {
    if ((sda_pin % 2u) != 0u) {
        return false; // SDAは偶数GPIO
    }
    if (scl_pin != (uint8_t)(sda_pin + 1u)) {
        return false; // SCLは直後の奇数GPIO
    }
    return module_bus_i2c_index_for_pin(sda_pin) == module_bus_i2c_index_for_pin(scl_pin);
}

uint8_t module_bus_i2c_index(uint8_t bus_index) {
    if (bus_index >= MODULE_BUS_COUNT) {
        return 0;
    }
    return module_bus_i2c_index_for_pin(MODULE_BUSES[bus_index].sda_pin);
}

uint8_t module_bus_next(uint8_t bus_index) {
    if (bus_index + 1u >= MODULE_BUS_COUNT) {
        return 0;
    }
    return (uint8_t)(bus_index + 1u);
}
