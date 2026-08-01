// get_module_index() のI/O実装。実基板 (softswitcher_module_sw4) にはモジュール番号
// ストラップ(抵抗ID)が無く、ADCに使える空きピンも無い (14pin PC4 はバックライトの
// LED_DATA)。モジュールの識別はマスター(Pico)側が「どのI2Cバス(スロット)に挿さって
// いるか」で行うため、スレーブ側は常に MODULE_INDEX_FIXED を返し、全モジュールが
// I2C_BASE_ADDR(0x30) 固定で待ち受ける (親仕様書 §4.5)。
// 将来のリビジョンでストラップが載り、1バスに複数モジュールをぶら下げる構成に戻す場合は、
// ここで読み取った番号を返せば i2c_slave_address_for_module() がアドレスへ変換する。

#include "module_index.h"

#include "module_config.h"

uint8_t get_module_index(void) {
    return MODULE_INDEX_FIXED;
}
