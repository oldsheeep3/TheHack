#ifndef SWITCHER_MODULE_MODULE_CONFIG_H
#define SWITCHER_MODULE_MODULE_CONFIG_H

#include <stdint.h>

// このヘッダは ch32v003fun (ch32fun.h) に依存しない (ホストテストからも直接included可能)。
// GPIO/ADCチャネル値は基板設計確定前の暫定プレースホルダ (TODO: PCB確定後に実測ピンへ差し替え)。

// ---- システム定数 (親仕様書 §4.0 / §4.5) ----
#define MAX_MODULES 8
#define I2C_BASE_ADDR 0x30

// ---- I2Cスレーブ レジスタマップ (親仕様書 §4.5。pico2w-controller/include/config.h の
// MODULE_REG_* と値・命名を揃える) ----
#define MODULE_REG_STATE 0x00
#define MODULE_REG_STATE_LEN 3 // [0]SW状態(下位4bit) / [1]VR_SRC1(0..255) / [2]VR_SRC2(0..255)
#define MODULE_REG_BACKLIGHT 0x10
#define MODULE_REG_BACKLIGHT_LEN (BACKLIGHT_LED_COUNT * 3) // 4灯分RGB(write)
#define MODULE_REG_INFO 0xF0
#define MODULE_REG_INFO_LEN 4 // [0..1]fw version / [2]capabilities / [3]HW rev

// ---- SWマトリクス (PGM1×SRC1, PGM1×SRC2, PGM2×SRC1, PGM2×SRC2 の計4SW) ----
#define SW_ROW_COUNT 2 // PGM1, PGM2
#define SW_COL_COUNT 2 // SRC1, SRC2
#define SW_COUNT (SW_ROW_COUNT * SW_COL_COUNT)

// I2C `0x00 STATE` レジスタ [0] の下位4bitのビット位置 (親仕様書 §4.5)。
#define SW_BIT_INDEX(row, col) ((row) * SW_COL_COUNT + (col))
#define SW_BIT_PGM1_SRC1 SW_BIT_INDEX(0, 0)
#define SW_BIT_PGM1_SRC2 SW_BIT_INDEX(0, 1)
#define SW_BIT_PGM2_SRC1 SW_BIT_INDEX(1, 0)
#define SW_BIT_PGM2_SRC2 SW_BIT_INDEX(1, 1)

// 行(PGM1,PGM2)/列(SRC1,SRC2)のGPIOピン番号。
extern const uint8_t SW_ROW_PINS[SW_ROW_COUNT];
extern const uint8_t SW_COL_PINS[SW_COL_COUNT];

// ---- アナログボリューム (VR_SRC1, VR_SRC2) ----
#define VR_COUNT 2
#define VR_SRC1_INDEX 0
#define VR_SRC2_INDEX 1

extern const uint8_t VR_ADC_CHANNELS[VR_COUNT];

// ---- バックライト (SK6812MINI-E ×4, 数珠つなぎ) ----
#define BACKLIGHT_LED_COUNT 4
extern const uint8_t BACKLIGHT_DATA_PIN;

// ---- モジュール番号ストラップ (抵抗ID方式, ADC 1chで0..MAX_MODULES-1を識別) ----
// CH32V003 ADCの分解能 (10bit: 0..1023)。
#define MODULE_STRAP_ADC_RESOLUTION_BITS 10
#define MODULE_STRAP_ADC_MAX ((1u << MODULE_STRAP_ADC_RESOLUTION_BITS) - 1)

extern const uint8_t MODULE_STRAP_ADC_CHANNEL;

#endif // SWITCHER_MODULE_MODULE_CONFIG_H
