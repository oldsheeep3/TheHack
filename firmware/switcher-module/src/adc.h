#ifndef SWITCHER_MODULE_ADC_H
#define SWITCHER_MODULE_ADC_H

#include <stdint.h>

// ---- アナログVR読取(ADC依存) ----
//
// module_config.h の VR_ADC_CHANNELS を使ってVR_SRC1/VR_SRC2を読み取り、生値を
// adc_scale.c (ADC非依存) へ渡してノイズ抑制済み0..255値を得る薄いラッパ。
// スケーリング/移動平均/デッドバンド自体はadc_scale.hを参照。

void adc_init(void);
void adc_task(void);

// 直近でスケーリングした VR_SRC1(0)/VR_SRC2(1) の値(0..255)を返す
// (module_config.h の VR_SRC1_INDEX/VR_SRC2_INDEX を渡す)。範囲外は0を返す。
// I2C `0x00 STATE` レジスタ[1]/[2] への実結線は M-004 で行う。
uint8_t adc_get_vr(uint8_t vr_index);

#endif // SWITCHER_MODULE_ADC_H
