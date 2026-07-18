// get_module_index() のI/O実装プレースホルダ。実ADC(MODULE_STRAP_ADC_CHANNEL)による
// ストラップ読取は M-003 (adc.c) 統合時に配線する。それまでは module 0 固定で動作する
// (I2Cアドレスは i2c_slave_address_for_module() 経由で I2C_BASE_ADDR=0x30 になる)。

#include "module_index.h"

uint8_t get_module_index(void) {
    return module_index_from_strap_adc(0);
}
