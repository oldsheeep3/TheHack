// ADC生値→モジュール番号、モジュール番号→I2Cアドレスの純粋変換ロジック。
// ch32v003fun/GPIO/ADCペリフェラルに依存しないため、ホストの gcc でそのまま
// ビルド・テストできる (test/test_module_index.c 参照)。

#include "module_index.h"

#include "module_config.h"

uint8_t module_index_from_strap_adc(uint16_t adc_raw) {
    uint32_t clamped = adc_raw > MODULE_STRAP_ADC_MAX ? MODULE_STRAP_ADC_MAX : adc_raw;
    uint32_t bucket = (clamped * (uint32_t)MAX_MODULES) / ((uint32_t)MODULE_STRAP_ADC_MAX + 1);
    return (uint8_t)(bucket >= MAX_MODULES ? MAX_MODULES - 1 : bucket);
}

uint8_t i2c_slave_address_for_module(uint8_t module_index) {
    uint8_t clamped = module_index >= MAX_MODULES ? MAX_MODULES - 1 : module_index;
    return (uint8_t)(I2C_BASE_ADDR + clamped);
}
