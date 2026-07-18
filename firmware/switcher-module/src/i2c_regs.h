#ifndef SWITCHER_MODULE_I2C_REGS_H
#define SWITCHER_MODULE_I2C_REGS_H

#include <stdbool.h>
#include <stdint.h>

#include "module_config.h"

// I2Cスレーブのレジスタマップ純粋部(ch32v003fun/GPIO非依存)。ISR/メインループが
// 参照する「レジスタアドレスがどの領域か」の判定と、各レジスタのバイト内容生成を
// ここに集約し、ホストテスト可能にする(親仕様書 §4.5, 00-system-overview.md §4.5)。

// INFO(0xF0) の各バイト値。実機HW rev未確定のため [3] はプレースホルダ (TODO: PCB確定後に差し替え)。
#define I2C_REGS_FW_VERSION_MAJOR 0x00
#define I2C_REGS_FW_VERSION_MINOR 0x01
#define I2C_REGS_CAPABILITIES 0x00 // 追加capabilitiesビットは未定義(将来拡張用に0)
#define I2C_REGS_HW_REV 0x00

typedef enum {
    I2C_REGS_REGION_STATE,
    I2C_REGS_REGION_BACKLIGHT,
    I2C_REGS_REGION_INFO,
    I2C_REGS_REGION_INVALID,
} i2c_regs_region_t;

// レジスタアドレス(I2Cの最初の書込みバイト)がどの領域に属するかを判定する。
// 領域内の任意のオフセット(例: 0x11)も同じ領域として扱う。未定義アドレスは
// I2C_REGS_REGION_INVALID を返す(親仕様書§4.5「不正レジスタは無視」に対応)。
i2c_regs_region_t i2c_regs_decode_region(uint8_t reg_addr);

// 領域がホストからの書込みを受け付けるか(現状 BACKLIGHT のみ true)。
bool i2c_regs_region_is_writable(i2c_regs_region_t region);

// STATE(0x00, read, 3B)の内容を生成する:
// [0]=SW状態下位4bit(上位4bitは呼出側で0埋め済みを期待) / [1]=VR_SRC1(0..255) / [2]=VR_SRC2(0..255)。
void i2c_regs_build_state(uint8_t sw_state, uint8_t vr_src1, uint8_t vr_src2,
                           uint8_t out[MODULE_REG_STATE_LEN]);

// INFO(0xF0, read, 4B)の内容を生成する: [0..1]=fw version / [2]=capabilities / [3]=HW rev。
void i2c_regs_build_info(uint8_t out[MODULE_REG_INFO_LEN]);

// BACKLIGHT(0x10, write, 12B)への書込みが、レジスタ先頭(start_offset=0, レジスタ
// アドレス0x10そのもの)から received_len バイト過不足なく(=12バイトちょうど)行われた
// (=完全な1フレーム分)かどうかを判定する。ISRはSTOP時にこれで受領完了フラグを立てる
// かどうかを決める(親仕様書§4.5, 途中書込み/長さ不足は無視して反映しない)。
bool i2c_regs_backlight_write_is_complete(uint8_t start_offset, uint8_t received_len);

#endif // SWITCHER_MODULE_I2C_REGS_H
