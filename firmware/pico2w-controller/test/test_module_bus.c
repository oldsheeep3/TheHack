// module_bus.c (Pico SDK非依存) のバス定義とピン→I2Cコントローラ変換を検証する。
// 実基板 softswitcher_module_master の配線 (Pico の実装ピン番号) をそのまま期待値に置き、
// 配線とファームのバス定義がずれた場合に必ず失敗するようにする。

#include <assert.h>
#include <stdbool.h>
#include <stdio.h>

#include "config.h"
#include "module_bus.h"

// 基板の SDA_n / SCL_n が繋がる Pico の GPIO 番号 (括弧内は基板上の実装ピン番号)。
// バス1: 26pin(GPIO20)/27pin(GPIO21)  ← 1x07コネクタ (数珠つなぎの先頭モジュール)
// バス2: 31pin(GPIO26)/32pin(GPIO27)  ┐
// バス3: 6pin(GPIO4)  /7pin(GPIO5)    │ 2x08コネクタ (2段目以降へ順に渡される)
// バス4: 4pin(GPIO2)  /5pin(GPIO3)    │
// バス5: 1pin(GPIO0)  /2pin(GPIO1)    ┘
static const uint8_t EXPECTED_SDA[MODULE_BUS_COUNT] = {20, 26, 4, 2, 0};
static const uint8_t EXPECTED_SCL[MODULE_BUS_COUNT] = {21, 27, 5, 3, 1};

static void test_bus_count(void) {
    assert(MODULE_BUS_COUNT == 5);
    // スロットはHIDレポートのモジュールスロット(MAX_MODULES)に収まる必要がある。
    assert(MODULE_BUS_COUNT <= MAX_MODULES);
    assert(MODULE_I2C_ADDR == 0x30);
}

static void test_pin_assignment(void) {
    for (int b = 0; b < MODULE_BUS_COUNT; b++) {
        assert(MODULE_BUSES[b].sda_pin == EXPECTED_SDA[b]);
        assert(MODULE_BUSES[b].scl_pin == EXPECTED_SCL[b]);
    }
}

// 同じGPIOを2本のバスへ割り当てていない。
static void test_no_duplicate_pins(void) {
    for (int i = 0; i < MODULE_BUS_COUNT; i++) {
        for (int j = i + 1; j < MODULE_BUS_COUNT; j++) {
            assert(MODULE_BUSES[i].sda_pin != MODULE_BUSES[j].sda_pin);
            assert(MODULE_BUSES[i].scl_pin != MODULE_BUSES[j].scl_pin);
            assert(MODULE_BUSES[i].sda_pin != MODULE_BUSES[j].scl_pin);
            assert(MODULE_BUSES[i].scl_pin != MODULE_BUSES[j].sda_pin);
        }
    }
}

// RP2040/RP2350のピンマルチプレクサ規則 (偶数=SDA / 奇数=SCL, 4本周期でi2c0/i2c1) に
// 全バスが適合している。適合していないピンをバス定義に書くとI2Cとして機能しない。
static void test_pins_are_valid_for_rp2xxx(void) {
    for (int b = 0; b < MODULE_BUS_COUNT; b++) {
        assert(module_bus_pins_are_valid(MODULE_BUSES[b].sda_pin, MODULE_BUSES[b].scl_pin));
    }

    // 規則から外れる組み合わせは弾く。
    assert(!module_bus_pins_are_valid(21, 20)); // SDA/SCL逆
    assert(!module_bus_pins_are_valid(20, 22)); // 連続していない
    assert(!module_bus_pins_are_valid(4, 7));   // ペアが跨っている
}

static void test_i2c_index_mapping(void) {
    // GPIO 0,1→i2c0 / 2,3→i2c1 / 4,5→i2c0 ... の4本周期。
    assert(module_bus_i2c_index_for_pin(0) == 0);
    assert(module_bus_i2c_index_for_pin(1) == 0);
    assert(module_bus_i2c_index_for_pin(2) == 1);
    assert(module_bus_i2c_index_for_pin(3) == 1);
    assert(module_bus_i2c_index_for_pin(4) == 0);
    assert(module_bus_i2c_index_for_pin(20) == 0);
    assert(module_bus_i2c_index_for_pin(21) == 0);
    assert(module_bus_i2c_index_for_pin(26) == 1);
    assert(module_bus_i2c_index_for_pin(27) == 1);

    // 実配線: i2c0 = バス1,3,5 / i2c1 = バス2,4 (2コントローラを時分割で共有する)。
    const uint8_t expected_controller[MODULE_BUS_COUNT] = {0, 1, 0, 1, 0};
    for (int b = 0; b < MODULE_BUS_COUNT; b++) {
        assert(module_bus_i2c_index((uint8_t)b) == expected_controller[b]);
        // SDAとSCLは必ず同じコントローラに属する。
        assert(module_bus_i2c_index_for_pin(MODULE_BUSES[b].sda_pin) ==
               module_bus_i2c_index_for_pin(MODULE_BUSES[b].scl_pin));
    }

    // 範囲外のバス番号でも未定義動作にならない。
    assert(module_bus_i2c_index(MODULE_BUS_COUNT) == 0);
    assert(module_bus_i2c_index(255) == 0);
}

// ラウンドロビン: 1回の呼び出しで1バスずつ進み、最後のバスの次は先頭へ戻る
// (i2c_modules_poll が1呼び出し1バスで全スロットを一巡するための順序)。
static void test_round_robin(void) {
    uint8_t bus = 0;
    for (int i = 0; i < MODULE_BUS_COUNT - 1; i++) {
        uint8_t next = module_bus_next(bus);
        assert(next == bus + 1);
        bus = next;
    }
    assert(bus == MODULE_BUS_COUNT - 1);
    assert(module_bus_next(bus) == 0); // 一巡して先頭へ

    // 全バスをちょうど1回ずつ通る。
    bool seen[MODULE_BUS_COUNT] = {false};
    uint8_t cur = 0;
    for (int i = 0; i < MODULE_BUS_COUNT; i++) {
        assert(!seen[cur]);
        seen[cur] = true;
        cur = module_bus_next(cur);
    }
    assert(cur == 0);
    for (int i = 0; i < MODULE_BUS_COUNT; i++) {
        assert(seen[i]);
    }

    // 範囲外の入力でも先頭へ戻るだけで、配列外を指さない。
    assert(module_bus_next(MODULE_BUS_COUNT) == 0);
    assert(module_bus_next(255) == 0);
}

int main(void) {
    test_bus_count();
    test_pin_assignment();
    test_no_duplicate_pins();
    test_pins_are_valid_for_rp2xxx();
    test_i2c_index_mapping();
    test_round_robin();

    printf("test_module_bus: OK\n");
    return 0;
}
