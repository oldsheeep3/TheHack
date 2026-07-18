#ifndef SWITCHER_MODULE_ADC_SCALE_H
#define SWITCHER_MODULE_ADC_SCALE_H

#include <stdint.h>

// VR_SRC1/VR_SRC2 の生ADC値(0..ADC_SCALE_RAW_MAX)を、移動平均によるノイズ抑制と
// デッドバンドによるバタつき抑制を経て 0..255 にスケーリングする純粋関数。
// ch32v003fun/ADCペリフェラルに依存しないため、ホストのgccでそのままビルド・
// テストできる (test/test_adc_scale.c 参照)。

// CH32V003 ADCの分解能 (10bit: 0..1023)。module_config.h の
// MODULE_STRAP_ADC_MAX と同じ物理ADCの分解能だが、VRスケーリングの純粋部を
// module_config.h(GPIO/ADCチャネル定義)に依存させないためここで独立定義する。
#define ADC_SCALE_RAW_BITS 10
#define ADC_SCALE_RAW_MAX ((1u << ADC_SCALE_RAW_BITS) - 1)

// 移動平均のウィンドウ幅。VR_SRC1/VR_SRC2 それぞれ独立した状態(adc_scale_state_t)で保持する。
#define ADC_SCALE_WINDOW_SIZE 4

// 出力(0..255)換算での更新しきい値。移動平均後の値がこの差に達しない限り
// last_outputを更新しない(微小変動でのバタつき抑制)。
#define ADC_SCALE_DEADBAND 2

typedef struct {
    uint16_t window[ADC_SCALE_WINDOW_SIZE];
    uint8_t index;
    uint8_t primed;
    uint8_t last_output;
} adc_scale_state_t;

void adc_scale_init(adc_scale_state_t *state);

// 生ADC値(範囲外はクランプ)を1サンプル投入し、移動平均→デッドバンドを経た
// 0..255スケール値を返す(内部状態を更新する)。初回呼び出しはウィンドウ全体を
// そのサンプルで充填し、デッドバンド判定なしで即時反映する。
uint8_t adc_scale_update(adc_scale_state_t *state, uint16_t raw_adc);

// 移動平均・デッドバンドを介さない単純な0..255への線形スケーリング(範囲外はクランプ)。
uint8_t adc_scale_raw_to_u8(uint16_t raw);

#endif // SWITCHER_MODULE_ADC_SCALE_H
