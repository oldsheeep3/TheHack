// i2c_slave_address_for_module() の純粋変換ロジックを検証する。
// 実基板にはモジュール番号ストラップが無く get_module_index() は MODULE_INDEX_FIXED(0)
// 固定のため、実効アドレスは I2C_BASE_ADDR(0x30) となる (マスター側はモジュールが挿さって
// いるI2Cバス=スロットでモジュールを識別する。pico2w-controller/include/config.h の
// MODULE_BUS_* を参照)。

#include <assert.h>
#include <stdio.h>

#include "module_config.h"
#include "module_index.h"

int main(void) {
    // I2Cアドレス = I2C_BASE_ADDR + module_index (親仕様書 §4.5)
    assert(i2c_slave_address_for_module(0) == I2C_BASE_ADDR);
    assert(i2c_slave_address_for_module(7) == 0x37);
    assert(i2c_slave_address_for_module(MAX_MODULES - 1) == I2C_BASE_ADDR + MAX_MODULES - 1);

    // 範囲外のmodule_indexもクランプされる
    assert(i2c_slave_address_for_module(255) == I2C_BASE_ADDR + MAX_MODULES - 1);

    // 実基板構成 (ストラップ無し) での実効アドレス。マスターは全バスでこのアドレスを叩く。
    assert(i2c_slave_address_for_module(MODULE_INDEX_FIXED) == 0x30);

    printf("test_module_index: OK\n");
    return 0;
}
