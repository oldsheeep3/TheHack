// module_config.h の定数実体 (module_config.c) がSW/VR/バックライト/I2Cベースアドレスの
// 契約どおりに提供されていることを検証する。

#include <assert.h>
#include <stdio.h>

#include "module_config.h"

int main(void) {
    assert(MAX_MODULES == 8);
    assert(I2C_BASE_ADDR == 0x30);

    assert(SW_COUNT == 4);
    assert(SW_BIT_PGM1_SRC1 == 0);
    assert(SW_BIT_PGM1_SRC2 == 1);
    assert(SW_BIT_PGM2_SRC1 == 2);
    assert(SW_BIT_PGM2_SRC2 == 3);
    assert(sizeof(SW_ROW_PINS) / sizeof(SW_ROW_PINS[0]) == SW_ROW_COUNT);
    assert(sizeof(SW_COL_PINS) / sizeof(SW_COL_PINS[0]) == SW_COL_COUNT);

    assert(VR_COUNT == 2);
    assert(sizeof(VR_ADC_CHANNELS) / sizeof(VR_ADC_CHANNELS[0]) == VR_COUNT);

    assert(BACKLIGHT_LED_COUNT == 4);

    assert(MODULE_STRAP_ADC_MAX == 1023);

    printf("test_module_config: OK\n");
    return 0;
}
