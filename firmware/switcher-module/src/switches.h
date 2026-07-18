#ifndef SWITCHER_MODULE_SWITCHES_H
#define SWITCHER_MODULE_SWITCHES_H

#include <stdint.h>

// ---- SWマトリクス走査 (GPIO依存) ----
//
// module_config.h の SW_ROW_PINS/SW_COL_PINS を使って4SWマトリクスを走査し、生サンプルを
// switches_debounce.c (GPIO非依存) へ渡して確定状態を得る薄いラッパ。デバウンスの
// 状態機械自体はswitches_debounce.hを参照。

void switches_init(void);
void switches_task(void);

// 直近で確定したSW状態を I2C `0x00 STATE` レジスタ[0] の形式で返す
// (b0=PGM1×SRC1, b1=PGM1×SRC2, b2=PGM2×SRC1, b3=PGM2×SRC2、上位4bitは0)。
// 実際のI2Cレジスタへの結線は M-004 で行う。
uint8_t switches_get_state(void);

#endif // SWITCHER_MODULE_SWITCHES_H
