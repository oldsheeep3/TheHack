// module_config.h の定数実体。ch32v003fun (Pico SDK相当) 非依存の純粋データのみを置く
// (ホストテストから直接ビルド・参照可能, test/test_module_config.c 参照)。

#include "module_config.h"

const uint8_t SW_ROW_PINS[SW_ROW_COUNT] = {0, 1}; // PGM1, PGM2
const uint8_t SW_COL_PINS[SW_COL_COUNT] = {2, 3}; // SRC1, SRC2

const uint8_t VR_ADC_CHANNELS[VR_COUNT] = {0, 1}; // VR_SRC1, VR_SRC2

const uint8_t BACKLIGHT_DATA_PIN = 4;

const uint8_t MODULE_STRAP_ADC_CHANNEL = 2;
