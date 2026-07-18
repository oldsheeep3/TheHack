#ifndef PICO2W_CONTROLLER_STATE_AGG_H
#define PICO2W_CONTROLLER_STATE_AGG_H

#include <stdbool.h>
#include <stdint.h>

#include "config.h"

// I2Cポーリング(i2c_modules.c)で得られる1モジュール分の状態。
typedef struct {
    bool present;                // 直近のポーリングでACK/応答を得られたか
    uint8_t switches;            // STATE[0] の下位4bit: 各SWのON/OFF
    uint8_t vr[MODULE_VR_COUNT]; // STATE[1], STATE[2]: VR_SRC1, VR_SRC2 (0..255)
} module_state_t;

typedef struct {
    module_state_t modules[MAX_MODULES];
} module_state_array_t;

// モジュール状態配列を入力レポート0x01(親仕様書 §4.1)のバイト列へパッキングする。
// out は最低 HID_REPORT_STATE_IN_LEN バイト必要。seq は呼び出し側が管理する
// 0-255ローテートのフレーム番号(取りこぼし検出用)。
void state_agg_pack(const module_state_array_t *states, uint8_t seq, uint8_t *out);

// VRデッドバンド/間引きの純粋判定。前回送出値(prev)と今回値(next)の差の絶対値が
// VR_DEADBAND_DELTA 以上であれば true (=送出すべき) を返す。
bool state_agg_vr_should_report(uint8_t prev, uint8_t next);

#endif // PICO2W_CONTROLLER_STATE_AGG_H
