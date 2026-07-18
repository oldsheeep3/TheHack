#ifndef PICO2W_CONTROLLER_I2C_MODULES_H
#define PICO2W_CONTROLLER_I2C_MODULES_H

#include "state_agg.h"

// I2Cハードウェア初期化(SDA/SCLのGPIO割当・プルアップ・ボーレート設定)。
void i2c_modules_init(void);

// 0x30..0x37 の全モジュールへ STATE(0x00) を1回ずつポーリングし、共有状態配列を
// 更新する。1モジュールのタイムアウト/NACKはそのモジュールのみ present=false として
// スキップし、他モジュールのポーリングは継続する(障害隔離, 親仕様書 §4/非機能要件)。
// メインループの単一コンテキストから継続的に呼び出すこと。
void i2c_modules_poll(void);

// 直近のポーリング結果のスナップショットを取得する。
void i2c_modules_get_state(module_state_array_t *out_states);

#endif // PICO2W_CONTROLLER_I2C_MODULES_H
