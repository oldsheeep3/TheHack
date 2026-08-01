// stub/ch32fun.h の実体。GPIO/ADCの読み書きを配列へ記録するだけの、副作用の無いモック。

#include "ch32fun.h"

#include <string.h>

stub_gpio_t stub_gpio;

void stub_gpio_reset(void) {
    memset(&stub_gpio, 0, sizeof(stub_gpio));
    // 未接続ピンの既定値は High (SWは内部プルアップ + アクティブLowのため、未押下相当)。
    for (int i = 0; i < STUB_PIN_COUNT; i++) {
        stub_gpio.level[i] = FUN_HIGH;
    }
}

void funPinMode(uint8_t pin, uint8_t mode) {
    stub_gpio.mode[pin & (STUB_PIN_COUNT - 1)] = mode;
    stub_gpio.mode_set[pin & (STUB_PIN_COUNT - 1)] = 1;
}

void funDigitalWrite(uint8_t pin, uint8_t value) {
    stub_gpio.level[pin & (STUB_PIN_COUNT - 1)] = value;
    stub_gpio.write_count[pin & (STUB_PIN_COUNT - 1)]++;
}

int funDigitalRead(uint8_t pin) {
    return stub_gpio.level[pin & (STUB_PIN_COUNT - 1)] ? 1 : 0;
}

void funAnalogInit(void) {
    stub_gpio.adc_init_count++;
}

int funAnalogRead(int channel) {
    if (channel < 0 || channel >= STUB_ADC_CHANNEL_COUNT) {
        return 0;
    }
    stub_gpio.adc_read_count[channel]++;
    return (int)stub_gpio.adc[channel];
}

void Delay_Us(uint32_t us) {
    stub_gpio.delay_us_total += us;
}
