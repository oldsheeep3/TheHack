#ifndef SWITCHER_MODULE_MODULE_INDEX_H
#define SWITCHER_MODULE_MODULE_INDEX_H

#include <stdint.h>

// ---- 純粋部 (ch32v003fun非依存, ホストテスト対象: module_index_scale.c) ----

// モジュール番号からI2Cスレーブアドレスを求める (I2C_BASE_ADDR + module_index,
// 親仕様書 §4.5)。module_index は 0..MAX_MODULES-1 を期待するが範囲外でもクランプする。
uint8_t i2c_slave_address_for_module(uint8_t module_index);

// ---- I/O部 (module_index.c) ----

// このモジュールの番号を返す。実基板にはモジュール番号ストラップが無く、モジュールの
// 識別はマスター側の「どのI2Cバス(スロット)に挿さっているか」で行うため、常に
// MODULE_INDEX_FIXED(0) を返す (= 全モジュールが I2C_BASE_ADDR 0x30 で待ち受ける)。
uint8_t get_module_index(void);

#endif // SWITCHER_MODULE_MODULE_INDEX_H
