// module_index_from_strap_adc() / i2c_slave_address_for_module() の純粋変換ロジックを検証する
// (get_module_index()のプレースホルダテスト, 完了条件: ホストテストgreen)。

#include <assert.h>
#include <stdio.h>

#include "module_config.h"
#include "module_index.h"

int main(void) {
    // 生ADC値0 → モジュール0 (先頭バケット)
    assert(module_index_from_strap_adc(0) == 0);

    // 生ADC値最大 → モジュール MAX_MODULES-1 (末尾バケット)
    assert(module_index_from_strap_adc(MODULE_STRAP_ADC_MAX) == MAX_MODULES - 1);

    // 中間値: 1024/8=128刻みでバケットが変わる (10bit ADC, MAX_MODULES=8想定)
    assert(module_index_from_strap_adc(127) == 0);
    assert(module_index_from_strap_adc(128) == 1);
    assert(module_index_from_strap_adc(512) == 4);

    // 範囲外の入力(ADC分解能を超える値)はクランプされ、未定義動作にならない
    assert(module_index_from_strap_adc(0xFFFF) == MAX_MODULES - 1);

    // I2Cアドレス = I2C_BASE_ADDR + module_index (親仕様書 §4.5)
    assert(i2c_slave_address_for_module(0) == I2C_BASE_ADDR);
    assert(i2c_slave_address_for_module(7) == 0x37);
    assert(i2c_slave_address_for_module(MAX_MODULES - 1) == I2C_BASE_ADDR + MAX_MODULES - 1);

    // 範囲外のmodule_indexもクランプされる
    assert(i2c_slave_address_for_module(255) == I2C_BASE_ADDR + MAX_MODULES - 1);

    printf("test_module_index: OK\n");
    return 0;
}
