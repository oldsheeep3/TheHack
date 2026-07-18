#ifndef SWITCHER_MODULE_SK6812_FRAME_H
#define SWITCHER_MODULE_SK6812_FRAME_H

#include <stdint.h>

#include "module_config.h"

// I2C `0x10 BACKLIGHT`(12B, 4灯×RGB, 親仕様書§4.5)で受領したRGBバイト列を、
// SK6812MINI-Eの送出順(GRB)フレームへ変換する純粋関数。ch32v003fun/GPIO非依存で
// ホストテスト可能 (test/test_sk6812_frame.c 参照)。色はPCが算出する前提で、
// 本ファームは受領値を忠実にバイト順のみ変換する(補正/ガンマ等の加工はしない)。

#define SK6812_RGB_BYTES (BACKLIGHT_LED_COUNT * 3)   // 入力: 4灯×RGB = 12バイト
#define SK6812_FRAME_BYTES (BACKLIGHT_LED_COUNT * 3) // 出力: 4灯×GRB = 12バイト(バイト数は同じ、順序のみ変わる)

// rgb: 4灯分, 各灯 R,G,B の順で12バイト (親仕様書§4.5 `0x10 BACKLIGHT`受領形式)。
// frame_out: 4灯分, 各灯 G,R,B の順で12バイト (SK6812送出順)。rgbとframe_outに
// 同じバッファは渡さないこと。
void sk6812_frame_from_rgb(const uint8_t rgb[SK6812_RGB_BYTES], uint8_t frame_out[SK6812_FRAME_BYTES]);

#endif // SWITCHER_MODULE_SK6812_FRAME_H
