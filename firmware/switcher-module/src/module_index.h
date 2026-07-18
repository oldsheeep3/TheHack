#ifndef SWITCHER_MODULE_MODULE_INDEX_H
#define SWITCHER_MODULE_MODULE_INDEX_H

#include <stdint.h>

// ---- 純粋部 (ch32v003fun非依存, ホストテスト対象: module_index_scale.c) ----

// MODULE_STRAP_ADC_CHANNEL の生ADC値 (0..MODULE_STRAP_ADC_MAX) を、抵抗ID方式で
// 0..MAX_MODULES-1 のモジュール番号へバケット変換する。範囲外の入力はクランプする。
uint8_t module_index_from_strap_adc(uint16_t adc_raw);

// モジュール番号からI2Cスレーブアドレスを求める (I2C_BASE_ADDR + module_index,
// 親仕様書 §4.5)。module_index は 0..MAX_MODULES-1 を期待するが範囲外でもクランプする。
uint8_t i2c_slave_address_for_module(uint8_t module_index);

// ---- I/O部 (ch32v003fun依存: module_index.c) ----

// このモジュールのストラップ/抵抗IDを実ADCで読み取り、module_index_from_strap_adc()で
// 変換した値を返す。実ADC結線は M-003 (adc.c) 統合時に行う前提のプレースホルダ実装。
uint8_t get_module_index(void);

#endif // SWITCHER_MODULE_MODULE_INDEX_H
