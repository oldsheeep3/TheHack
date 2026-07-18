// i2c_regs.c (GPIO非依存) のレジスタ領域判定・STATE/INFOバイト生成・BACKLIGHT受領完了
// 判定を検証する(親仕様書§4.5: 0x00 STATE read 3B / 0x10 BACKLIGHT write 12B / 0xF0 INFO
// read 4B)。

#include <assert.h>
#include <stdio.h>

#include "i2c_regs.h"
#include "module_config.h"

// 0x00 STATE(read, 3B)の領域判定。3バイト分(0x00,0x01,0x02)がSTATE、直後の0x03は
// BACKLIGHT(0x10)手前の未定義ギャップとしてINVALIDになることを検証する。
static void test_decode_region_state(void) {
    assert(i2c_regs_decode_region(0x00) == I2C_REGS_REGION_STATE);
    assert(i2c_regs_decode_region(0x01) == I2C_REGS_REGION_STATE);
    assert(i2c_regs_decode_region(0x02) == I2C_REGS_REGION_STATE);
    assert(i2c_regs_decode_region(0x03) == I2C_REGS_REGION_INVALID);
}

// 0x10 BACKLIGHT(write, 12B)の領域判定。0x10..0x1B(12バイト分)がBACKLIGHT、直後の
// 0x1CはINVALIDになることを検証する。
static void test_decode_region_backlight(void) {
    assert(i2c_regs_decode_region(0x10) == I2C_REGS_REGION_BACKLIGHT);
    assert(i2c_regs_decode_region(0x1B) == I2C_REGS_REGION_BACKLIGHT);
    assert(i2c_regs_decode_region(0x1C) == I2C_REGS_REGION_INVALID);
}

// 0xF0 INFO(read, 4B)の領域判定。0xF0..0xF3がINFO、直後の0xF4・末尾0xFFはINVALIDに
// なることを検証する。
static void test_decode_region_info(void) {
    assert(i2c_regs_decode_region(0xF0) == I2C_REGS_REGION_INFO);
    assert(i2c_regs_decode_region(0xF3) == I2C_REGS_REGION_INFO);
    assert(i2c_regs_decode_region(0xF4) == I2C_REGS_REGION_INVALID);
    assert(i2c_regs_decode_region(0xFF) == I2C_REGS_REGION_INVALID);
}

// 書込み可否はBACKLIGHTのみtrue(STATE/INFOはread専用, INVALIDは対象外)。
static void test_region_is_writable(void) {
    assert(i2c_regs_region_is_writable(I2C_REGS_REGION_STATE) == false);
    assert(i2c_regs_region_is_writable(I2C_REGS_REGION_BACKLIGHT) == true);
    assert(i2c_regs_region_is_writable(I2C_REGS_REGION_INFO) == false);
    assert(i2c_regs_region_is_writable(I2C_REGS_REGION_INVALID) == false);
}

// STATE 3バイト生成: [0]はSW状態の下位4bitのみ(上位ビットは切り捨て) / [1][2]はVR値そのまま。
static void test_build_state(void) {
    uint8_t out[MODULE_REG_STATE_LEN];

    i2c_regs_build_state(0xFF, 0x12, 0x34, out);
    assert(out[0] == 0x0F);
    assert(out[1] == 0x12);
    assert(out[2] == 0x34);

    i2c_regs_build_state((uint8_t)(1u << SW_BIT_PGM2_SRC2), 0, 255, out);
    assert(out[0] == (1u << SW_BIT_PGM2_SRC2));
    assert(out[1] == 0);
    assert(out[2] == 255);
}

// INFO 4バイト生成: [0..1]fw version / [2]capabilities / [3]HW rev が定数どおりであること。
static void test_build_info(void) {
    uint8_t out[MODULE_REG_INFO_LEN];

    i2c_regs_build_info(out);
    assert(out[0] == I2C_REGS_FW_VERSION_MAJOR);
    assert(out[1] == I2C_REGS_FW_VERSION_MINOR);
    assert(out[2] == I2C_REGS_CAPABILITIES);
    assert(out[3] == I2C_REGS_HW_REV);
}

// BACKLIGHT受領完了判定: レジスタ先頭(offset0)から過不足なく12バイト受領した場合のみ
// 完了とみなす。途中書込み・長さ不足・不一致は完了扱いしない(親仕様書§4.5)。
static void test_backlight_write_is_complete(void) {
    assert(i2c_regs_backlight_write_is_complete(0, MODULE_REG_BACKLIGHT_LEN) == true);
    assert(i2c_regs_backlight_write_is_complete(0, MODULE_REG_BACKLIGHT_LEN - 1) == false);
    assert(i2c_regs_backlight_write_is_complete(0, MODULE_REG_BACKLIGHT_LEN + 1) == false);
    assert(i2c_regs_backlight_write_is_complete(1, MODULE_REG_BACKLIGHT_LEN) == false);
}

int main(void) {
    test_decode_region_state();
    test_decode_region_backlight();
    test_decode_region_info();
    test_region_is_writable();
    test_build_state();
    test_build_info();
    test_backlight_write_is_complete();

    printf("test_i2c_regs: OK\n");
    return 0;
}
