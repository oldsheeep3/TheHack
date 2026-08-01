#ifndef SWITCHER_MODULE_MODULE_CONFIG_H
#define SWITCHER_MODULE_MODULE_CONFIG_H

#include <stdint.h>

// このヘッダは ch32v003fun (ch32fun.h) に依存しない (ホストテストからも直接included可能)。
// GPIO/ADCチャネル値は基板 softswitcher_module_sw4 (CH32V003F4P6, TSSOP-20) の実配線。

// ---- ピン番号エンコード ----
// ch32fun の GpioOf()/funPinMode()/funDigitalWrite() が期待する (ポート番号<<4 | ピン番号)
// 形式。ch32fun.h をincludeせずに同じ値を作れるよう独自に定義する。ch32v003hw.h の
// PA1/PC1/PD2... と同値であることは、ch32fun.h をincludeする側 (switches.c, adc.c,
// backlight.c, i2c_slave.c) の _Static_assert でビルド時に検証する。
#define MODULE_PORT_A 0
#define MODULE_PORT_C 2
#define MODULE_PORT_D 3
#define MODULE_PIN(port, pin) ((uint8_t)(((port) << 4) | (pin)))

// ---- システム定数 (親仕様書 §4.0 / §4.5) ----
#define MAX_MODULES 8
#define I2C_BASE_ADDR 0x30

// ---- I2Cスレーブ (親仕様書 §4.5) ----
// I2C1のSDA/SCLはCH32V003のシリコン固定ピン。基板上は 11pin(PC1)/12pin(PC2)。
#define MODULE_I2C_SDA_PIN MODULE_PIN(MODULE_PORT_C, 1)
#define MODULE_I2C_SCL_PIN MODULE_PIN(MODULE_PORT_C, 2)

// ---- I2Cスレーブ レジスタマップ (親仕様書 §4.5。pico2w-controller/include/config.h の
// MODULE_REG_* と値・命名を揃える) ----
#define MODULE_REG_STATE 0x00
#define MODULE_REG_STATE_LEN 3 // [0]SW状態(下位4bit) / [1]VR_SRC1(0..255) / [2]VR_SRC2(0..255)
#define MODULE_REG_BACKLIGHT 0x10
#define MODULE_REG_BACKLIGHT_LEN (BACKLIGHT_LED_COUNT * 3) // 4灯分RGB(write)
#define MODULE_REG_INFO 0xF0
#define MODULE_REG_INFO_LEN 4 // [0..1]fw version / [2]capabilities / [3]HW rev

// ---- SW (4個の独立入力。基板上のSW_1..SW_4、いずれもGND側へ落とすアクティブLow) ----
// 旧リビジョンは2×2マトリクス走査だったが、実基板は4本を専用GPIOへ直結しているため
// 行選択は行わず、内部プルアップ付き入力として直読みする。
#define SW_COUNT 4

// I2C `0x00 STATE` レジスタ [0] の下位4bitのビット位置 (親仕様書 §4.5)。
// 基板の SW_1..SW_4 を b0..b3 に割り当て、論理名(PGM×SRC)は従来のSTATE契約を維持する。
#define SW_BIT_SW1 0
#define SW_BIT_SW2 1
#define SW_BIT_SW3 2
#define SW_BIT_SW4 3
#define SW_BIT_PGM1_SRC1 SW_BIT_SW1
#define SW_BIT_PGM1_SRC2 SW_BIT_SW2
#define SW_BIT_PGM2_SRC1 SW_BIT_SW3
#define SW_BIT_PGM2_SRC2 SW_BIT_SW4

// SW_1..SW_4 のGPIOピン (基板: 19pin PD2 / 1pin PD4 / 20pin PD3 / 2pin PD5)。
// 配列の添字は上記のビット位置 (SW_BIT_SW1..SW_BIT_SW4) と一致する。
extern const uint8_t SW_PINS[SW_COUNT];

// ---- アナログボリューム (VR_SRC1=基板VOL1, VR_SRC2=基板VOL2) ----
#define VR_COUNT 2
#define VR_SRC1_INDEX 0
#define VR_SRC2_INDEX 1

// VOL1=5pin(PA1)=ADCチャネル1 / VOL2=6pin(PA2)=ADCチャネル0。
// funAnalogRead() はピン番号ではなくアナログチャネル番号を取るため、ここもチャネル番号。
extern const uint8_t VR_ADC_CHANNELS[VR_COUNT];

// ---- バックライト (SK6812MINI-E ×4, 数珠つなぎ) ----
#define BACKLIGHT_LED_COUNT 4
// 基板: 14pin(PC4)。
extern const uint8_t BACKLIGHT_DATA_PIN;

// ---- モジュール番号 ----
// 実基板にはモジュール番号ストラップ(抵抗ID)が無く、ADC入力に使える空きピンも無い
// (14pin PC4 はバックライトのLED_DATA)。モジュールの識別はマスター(Pico)側が
// 「どのI2Cバス(スロット)に挿さっているか」で行うため、スレーブ側は全モジュール共通の
// アドレス I2C_BASE_ADDR(0x30) で待ち受ける (pico2w-controller/include/config.h の
// MODULE_BUS_* と対。親仕様書 §4.5)。
#define MODULE_INDEX_FIXED 0

#endif // SWITCHER_MODULE_MODULE_CONFIG_H
