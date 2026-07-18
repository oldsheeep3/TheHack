#include "adc_scale.h"

static uint16_t clamp_raw(uint16_t raw) {
    return raw > ADC_SCALE_RAW_MAX ? ADC_SCALE_RAW_MAX : raw;
}

uint8_t adc_scale_raw_to_u8(uint16_t raw) {
    uint16_t clamped = clamp_raw(raw);
    return (uint8_t)(((uint32_t)clamped * 255u) / ADC_SCALE_RAW_MAX);
}

void adc_scale_init(adc_scale_state_t *state) {
    for (int i = 0; i < ADC_SCALE_WINDOW_SIZE; i++) {
        state->window[i] = 0;
    }
    state->index = 0;
    state->primed = 0;
    state->last_output = 0;
}

uint8_t adc_scale_update(adc_scale_state_t *state, uint16_t raw_adc) {
    uint16_t clamped = clamp_raw(raw_adc);
    uint8_t is_first_sample = (uint8_t)(!state->primed);

    if (is_first_sample) {
        for (int i = 0; i < ADC_SCALE_WINDOW_SIZE; i++) {
            state->window[i] = clamped;
        }
        state->primed = 1;
    } else {
        state->window[state->index] = clamped;
        state->index = (uint8_t)((state->index + 1) % ADC_SCALE_WINDOW_SIZE);
    }

    uint32_t sum = 0;
    for (int i = 0; i < ADC_SCALE_WINDOW_SIZE; i++) {
        sum += state->window[i];
    }
    uint8_t scaled = adc_scale_raw_to_u8((uint16_t)(sum / ADC_SCALE_WINDOW_SIZE));

    if (is_first_sample) {
        state->last_output = scaled;
    } else {
        int16_t diff = (int16_t)scaled - (int16_t)state->last_output;
        if (diff < 0) {
            diff = (int16_t)(-diff);
        }
        if (diff >= ADC_SCALE_DEADBAND) {
            state->last_output = scaled;
        }
    }
    return state->last_output;
}
