#ifndef PICO2W_CONTROLLER_I2C_MODULES_H
#define PICO2W_CONTROLLER_I2C_MODULES_H

#include "state_agg.h"

// I2Cハードウェア初期化(全バスのSDA/SCLのGPIO割当・プルアップ・ボーレート設定)。
void i2c_modules_init(void);

// 全スロット(MODULE_BUS_COUNT本のI2Cバス、いずれもアドレスは MODULE_I2C_ADDR)へ
// STATE(0x00) を1回ずつポーリングし、共有状態配列を更新する。1モジュールのタイムアウト/
// NACKはそのモジュールのみ present=false としてスキップし、他モジュールのポーリングは
// 継続する(障害隔離, 親仕様書 §4/非機能要件)。
// メインループの単一コンテキストから継続的に呼び出すこと。
void i2c_modules_poll(void);

// 直近のポーリング結果のスナップショットを取得する。
void i2c_modules_get_state(module_state_array_t *out_states);

// module_index (= スロット/バス番号 0..MODULE_BUS_COUNT-1) の BACKLIGHT(0x10) レジスタへ
// 4灯分RGB(12バイト)を書き込む。ACK無し/タイムアウト/範囲外module_indexの場合は書込を行わず false を返す
// (呼び出し側はSTATEポーリングを止めずスキップすること)。
bool i2c_modules_write_backlight(uint8_t module_index, const uint8_t rgb[MODULE_REG_BACKLIGHT_LEN]);

#endif // PICO2W_CONTROLLER_I2C_MODULES_H
