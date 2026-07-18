// adc.h のI/O実装。VR_ADC_CHANNELS(VR_SRC1,VR_SRC2)をfunAnalogReadで走査し、生値を
// adc_scale.c (ADC非依存) にそのまま渡し、スケーリング済み値のみをここで保持する。

#include "adc.h"

#include "ch32fun.h"

#include "adc_scale.h"
#include "module_config.h"

static adc_scale_state_t vr_state[VR_COUNT];
static uint8_t vr_scaled[VR_COUNT];

void adc_init(void) {
    funAnalogInit();
    for (int i = 0; i < VR_COUNT; i++) {
        adc_scale_init(&vr_state[i]);
        vr_scaled[i] = 0;
    }
}

void adc_task(void) {
    for (int i = 0; i < VR_COUNT; i++) {
        uint16_t raw = (uint16_t)funAnalogRead(VR_ADC_CHANNELS[i]);
        vr_scaled[i] = adc_scale_update(&vr_state[i], raw);
    }
}

uint8_t adc_get_vr(uint8_t vr_index) {
    if (vr_index >= VR_COUNT) {
        return 0;
    }
    return vr_scaled[vr_index];
}
