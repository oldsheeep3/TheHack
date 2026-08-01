#ifndef SWITCHER_MODULE_SWITCHES_H
#define SWITCHER_MODULE_SWITCHES_H

#include <stdint.h>

// ---- SW直読み (GPIO依存) ----
//
// module_config.h の SW_PINS(SW_1..SW_4)をプルアップ入力として読み、生サンプルを
// switches_debounce.c (GPIO非依存) へ渡して確定状態を得る薄いラッパ。デバウンスの
// 状態機械自体はswitches_debounce.hを参照。

void switches_init(void);
void switches_task(void);

// 直近で確定したSW状態を I2C `0x00 STATE` レジスタ[0] の形式で返す
// (b0=SW_1(PGM1×SRC1), b1=SW_2(PGM1×SRC2), b2=SW_3(PGM2×SRC1), b3=SW_4(PGM2×SRC2)、
// 上位4bitは0)。
uint8_t switches_get_state(void);

#endif // SWITCHER_MODULE_SWITCHES_H
