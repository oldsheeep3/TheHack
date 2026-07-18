#ifndef SWITCHER_MODULE_BACKLIGHT_H
#define SWITCHER_MODULE_BACKLIGHT_H

#include <stdint.h>

#include "module_config.h"

// ---- SK6812×4 バックライト駆動(GPIOビットバン依存) ----
//
// I2C `0x10 BACKLIGHT`(12B, 親仕様書§4.5)で受領したRGBを sk6812_frame.c (I/O非依存)
// でGRBフレームへ変換し、BACKLIGHT_DATA_PIN 1本へ800kHzのタイミングでビットバン出力する
// 薄いラッパ。RGB→GRB変換自体はsk6812_frame.hを参照。

void backlight_init(void);
void backlight_task(void);

// 次回のbacklight_task()で送出するRGB(4灯×3バイト, R,G,Bの順)を設定する。
// I2C `0x10 BACKLIGHT` レジスタからの実結線は M-004 で行う。未受領時(初期状態)は
// 全灯消灯(0,0,0)を送出する。
void backlight_set_rgb(const uint8_t rgb[BACKLIGHT_LED_COUNT * 3]);

#endif // SWITCHER_MODULE_BACKLIGHT_H
