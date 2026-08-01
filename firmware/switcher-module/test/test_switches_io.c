// switches.c (GPIO依存のSW直読み) を stub/ch32fun.h 経由でホスト上で検証する。
// 実基板 (softswitcher_module_sw4) の配線 SW_1=19pin(PD2) / SW_2=1pin(PD4) /
// SW_3=20pin(PD3) / SW_4=2pin(PD5) と、STATEレジスタのビット位置の対応が崩れていないか、
// およびアクティブLow・内部プルアップ入力としての設定を回帰テストする。

#include <assert.h>
#include <stdio.h>

#include "ch32fun.h" // test/stub/ch32fun.h (ホストスタブ)

#include "module_config.h"
#include "switches.h"
#include "switches_debounce.h"

// SW_PINS の並び (= STATEレジスタのビット順) に対応する期待ピン。
static const uint8_t EXPECTED_PINS[SW_COUNT] = {PD2, PD4, PD3, PD5};

// 生レベルを count 回サンプリングして確定状態を得る。
static uint8_t sample_times(int count) {
    for (int i = 0; i < count; i++) {
        switches_task();
    }
    return switches_get_state();
}

// SW_PINS が実基板の配線どおりで、ビット位置と1対1に対応している。
static void test_pin_assignment(void) {
    for (int i = 0; i < SW_COUNT; i++) {
        assert(SW_PINS[i] == EXPECTED_PINS[i]);
    }
    assert(SW_PINS[SW_BIT_SW1] == PD2);
    assert(SW_PINS[SW_BIT_SW2] == PD4);
    assert(SW_PINS[SW_BIT_SW3] == PD3);
    assert(SW_PINS[SW_BIT_SW4] == PD5);

    // 同じピンを2つのSWへ割り当てていない。
    for (int i = 0; i < SW_COUNT; i++) {
        for (int j = i + 1; j < SW_COUNT; j++) {
            assert(SW_PINS[i] != SW_PINS[j]);
        }
    }
}

// switches_init() は4本すべてを内部プルアップ入力に設定する (出力にしない = マトリクスの
// 行駆動を行わない)。
static void test_init_configures_pullup_inputs(void) {
    stub_gpio_reset();
    switches_init();

    for (int i = 0; i < SW_COUNT; i++) {
        uint8_t pin = SW_PINS[i];
        assert(stub_gpio.mode_set[pin]);
        assert(stub_gpio.mode[pin] == GPIO_CFGLR_IN_PUPD);
        assert(stub_gpio.level[pin] == FUN_HIGH); // プルアップ選択のためHighを書く
    }

    // SW以外のピンには一切触らない。
    assert(!stub_gpio.mode_set[PC1]);
    assert(!stub_gpio.mode_set[PC4]);
    assert(!stub_gpio.mode_set[PA1]);
}

// 押下(Low)はビットが立ち、開放(High)はビットが落ちる(アクティブLow)。
static void test_active_low_per_switch(void) {
    for (int i = 0; i < SW_COUNT; i++) {
        stub_gpio_reset();
        switches_init();

        // 全ピンHigh = 全SW開放。
        assert(sample_times(SWITCHES_DEBOUNCE_STABLE_SAMPLES) == 0);

        stub_gpio.level[SW_PINS[i]] = FUN_LOW;
        uint8_t state = sample_times(SWITCHES_DEBOUNCE_STABLE_SAMPLES);
        assert(state == (uint8_t)(1u << i));
        assert((state & 0xF0u) == 0);

        stub_gpio.level[SW_PINS[i]] = FUN_HIGH;
        assert(sample_times(SWITCHES_DEBOUNCE_STABLE_SAMPLES) == 0);
    }
}

// 論理名 (PGM×SRC) と物理ピンの対応。STATE契約 (親仕様書 §4.5) の回帰テスト。
static void test_logical_bit_mapping(void) {
    stub_gpio_reset();
    switches_init();

    stub_gpio.level[PD2] = FUN_LOW; // SW_1 = PGM1×SRC1
    stub_gpio.level[PD3] = FUN_LOW; // SW_3 = PGM2×SRC1
    uint8_t state = sample_times(SWITCHES_DEBOUNCE_STABLE_SAMPLES);
    assert(state == (uint8_t)((1u << SW_BIT_PGM1_SRC1) | (1u << SW_BIT_PGM2_SRC1)));

    // PD4(SW_2)/PD5(SW_4) は押していないのでビットは立たない。
    assert((state & (uint8_t)(1u << SW_BIT_PGM1_SRC2)) == 0);
    assert((state & (uint8_t)(1u << SW_BIT_PGM2_SRC2)) == 0);
}

// 走査ごとにピンへ書き込まない (直読みのみ)。マトリクス走査へ戻ると、非選択行の駆動で
// ここが増えるため回帰検出になる。
static void test_task_does_not_drive_pins(void) {
    stub_gpio_reset();
    switches_init();

    uint8_t writes_after_init[SW_COUNT];
    for (int i = 0; i < SW_COUNT; i++) {
        writes_after_init[i] = stub_gpio.write_count[SW_PINS[i]];
    }

    sample_times(10);

    for (int i = 0; i < SW_COUNT; i++) {
        assert(stub_gpio.write_count[SW_PINS[i]] == writes_after_init[i]);
    }
    assert(stub_gpio.delay_us_total == 0); // 行切替の整定待ちも不要
}

int main(void) {
    test_pin_assignment();
    test_init_configures_pullup_inputs();
    test_active_low_per_switch();
    test_logical_bit_mapping();
    test_task_does_not_drive_pins();

    printf("test_switches_io: OK\n");
    return 0;
}
