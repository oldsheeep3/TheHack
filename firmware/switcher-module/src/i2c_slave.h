#ifndef SWITCHER_MODULE_I2C_SLAVE_H
#define SWITCHER_MODULE_I2C_SLAVE_H

// I2Cスレーブ通信(CH32V003 I2C1, ハードウェア依存)。module_config.h の
// I2C_BASE_ADDR + get_module_index() をスレーブアドレスとして待受け、i2c_regs.c
// (GPIO非依存)のレジスタマップ定義に従い STATE(0x00,read,3B) / BACKLIGHT(0x10,write,12B)
// / INFO(0xF0,read,4B) を提供する(親仕様書 §4.5)。
//
// I2C1_EV_IRQHandler/I2C1_ER_IRQHandler(ISR)はレジスタポインタ確定・1バイト授受・
// 受信バッファ格納のみを行う。SWスキャン/ADC読取/SK6812駆動はメインループ
// (switches_task()/adc_task()/backlight_task())側の責務であり、i2c_slave_task()は
// その結果(switches_get_state()/adc_get_vr())をSTATEスナップショットへ反映し、
// 受領済みBACKLIGHTをbacklight_set_rgb()へ橋渡しする(ISRとの共有はvolatileフラグ/
// ダブルバッファで安全化, 親仕様書§4)。

void i2c_slave_init(void);
void i2c_slave_task(void);

#endif // SWITCHER_MODULE_I2C_SLAVE_H
