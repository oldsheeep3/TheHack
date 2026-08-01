// module_config.h の定数実体 (module_config.c) がSW/VR/バックライト/I2Cベースアドレスの
// 契約どおりに提供されていることを検証する。ピン番号は基板 softswitcher_module_sw4
// (CH32V003F4P6, TSSOP-20) の実配線。

#include <assert.h>
#include <stdio.h>

#include "module_config.h"

// ch32fun のピンエンコード (ポート番号<<4 | ピン番号) の実値。ch32fun.h をincludeせずに
// 検証できるよう期待値をリテラルで置く (ch32fun定数との一致は switches.c /
// backlight.c / i2c_slave.c の _Static_assert がクロスビルド時に保証する)。
#define EXPECT_PA1 0x01
#define EXPECT_PC1 0x21
#define EXPECT_PC2 0x22
#define EXPECT_PC4 0x24
#define EXPECT_PD2 0x32
#define EXPECT_PD3 0x33
#define EXPECT_PD4 0x34
#define EXPECT_PD5 0x35

int main(void) {
    assert(MAX_MODULES == 8);
    assert(I2C_BASE_ADDR == 0x30);

    // ピンエンコードが ch32fun (GpioOf: pin>>4 でポート, pin&0xf でビット) と同形式。
    assert(MODULE_PIN(MODULE_PORT_A, 1) == EXPECT_PA1);
    assert(MODULE_PIN(MODULE_PORT_C, 4) == EXPECT_PC4);
    assert(MODULE_PIN(MODULE_PORT_D, 2) == EXPECT_PD2);

    // I2Cスレーブ: 11pin(PC1)=SDA / 12pin(PC2)=SCL (I2C1のシリコン固定ピン)。
    assert(MODULE_I2C_SDA_PIN == EXPECT_PC1);
    assert(MODULE_I2C_SCL_PIN == EXPECT_PC2);

    // SW: 4個の独立入力。SW_1=19pin(PD2) / SW_2=1pin(PD4) / SW_3=20pin(PD3) / SW_4=2pin(PD5)。
    assert(SW_COUNT == 4);
    assert(SW_BIT_SW1 == 0);
    assert(SW_BIT_SW2 == 1);
    assert(SW_BIT_SW3 == 2);
    assert(SW_BIT_SW4 == 3);
    assert(SW_BIT_PGM1_SRC1 == SW_BIT_SW1);
    assert(SW_BIT_PGM1_SRC2 == SW_BIT_SW2);
    assert(SW_BIT_PGM2_SRC1 == SW_BIT_SW3);
    assert(SW_BIT_PGM2_SRC2 == SW_BIT_SW4);
    assert(sizeof(SW_PINS) / sizeof(SW_PINS[0]) == SW_COUNT);
    assert(SW_PINS[SW_BIT_SW1] == EXPECT_PD2);
    assert(SW_PINS[SW_BIT_SW2] == EXPECT_PD4);
    assert(SW_PINS[SW_BIT_SW3] == EXPECT_PD3);
    assert(SW_PINS[SW_BIT_SW4] == EXPECT_PD5);

    // VR: VOL1=5pin(PA1)=ADCチャネル1 / VOL2=6pin(PA2)=ADCチャネル0。
    assert(VR_COUNT == 2);
    assert(sizeof(VR_ADC_CHANNELS) / sizeof(VR_ADC_CHANNELS[0]) == VR_COUNT);
    assert(VR_ADC_CHANNELS[VR_SRC1_INDEX] == 1);
    assert(VR_ADC_CHANNELS[VR_SRC2_INDEX] == 0);

    // バックライト: LED_DATA=14pin(PC4), SK6812MINI-E ×4。
    assert(BACKLIGHT_LED_COUNT == 4);
    assert(BACKLIGHT_DATA_PIN == EXPECT_PC4);
    assert(MODULE_REG_BACKLIGHT_LEN == BACKLIGHT_LED_COUNT * 3);

    // モジュール番号ストラップは実基板に無く、全モジュールが 0x30 固定で待ち受ける。
    assert(MODULE_INDEX_FIXED == 0);

    // ピンの重複割当が無いこと (SW×4 + LED_DATA + I2C×2)。
    const uint8_t all_pins[] = {
        SW_PINS[0], SW_PINS[1], SW_PINS[2], SW_PINS[3],
        BACKLIGHT_DATA_PIN, MODULE_I2C_SDA_PIN, MODULE_I2C_SCL_PIN,
    };
    const unsigned all_pin_count = sizeof(all_pins) / sizeof(all_pins[0]);
    for (unsigned i = 0; i < all_pin_count; i++) {
        for (unsigned j = i + 1; j < all_pin_count; j++) {
            assert(all_pins[i] != all_pins[j]);
        }
    }
    printf("test_module_config: OK\n");
    return 0;
}
