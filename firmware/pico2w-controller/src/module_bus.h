#ifndef PICO2W_CONTROLLER_MODULE_BUS_H
#define PICO2W_CONTROLLER_MODULE_BUS_H

#include <stdbool.h>
#include <stdint.h>

#include "config.h"

// モジュール1台につき1本引き出されているI2Cバス(スロット)の定義。
// Pico SDK非依存の純粋データ/純粋関数のみ (ホストテスト対象: module_bus.c)。
// 実際のI2C初期化・ピン切替は i2c_modules.c が行う。
typedef struct {
    uint8_t sda_pin;
    uint8_t scl_pin;
} module_bus_t;

// バス1..MODULE_BUS_COUNT の {SDA, SCL} GPIO番号 (添字0がバス1 = モジュール番号0)。
extern const module_bus_t MODULE_BUSES[MODULE_BUS_COUNT];

// RP2040/RP2350 のピンマルチプレクサ上、そのGPIOをI2Cとして使うときのコントローラ番号
// (0 = i2c0, 1 = i2c1) を返す。GPIO番号だけで一意に決まる ((gpio / 2) % 2)。
uint8_t module_bus_i2c_index_for_pin(uint8_t gpio);

// SDA/SCLのペアがRP2040/RP2350のピン割当として成立するか
// (SDAが偶数GPIO・SCLがその次の奇数GPIO・同一コントローラ) を判定する。
bool module_bus_pins_are_valid(uint8_t sda_pin, uint8_t scl_pin);

// バス番号(0基点)のコントローラ番号。範囲外は0を返す。
uint8_t module_bus_i2c_index(uint8_t bus_index);

// ラウンドロビンで次に見るバス番号。1回のメインループで1バスずつしか進めないため
// (i2c_modules_poll)、この順番でスロットを一巡する。範囲外の入力は先頭へ戻す。
uint8_t module_bus_next(uint8_t bus_index);

#endif // PICO2W_CONTROLLER_MODULE_BUS_H
