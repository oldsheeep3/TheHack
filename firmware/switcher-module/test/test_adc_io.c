// adc.c (ADC依存のVR読取) を stub/ch32fun.h 経由でホスト上で検証する。
// 実基板 (softswitcher_module_sw4) の配線 VOL1=5pin(PA1)=ADCチャネル1 /
// VOL2=6pin(PA2)=ADCチャネル0 に対して、VR_SRC1/VR_SRC2 が正しいチャネルから読まれて
// いるか (取り違え・入れ替わりの回帰検出) を確認する。

#include <assert.h>
#include <stdio.h>

#include "ch32fun.h" // test/stub/ch32fun.h (ホストスタブ)

#include "adc.h"
#include "adc_scale.h"
#include "module_config.h"

#define VOL1_ADC_CHANNEL 1 // PA1
#define VOL2_ADC_CHANNEL 0 // PA2

static void test_channel_assignment(void) {
    assert(VR_COUNT == 2);
    assert(VR_ADC_CHANNELS[VR_SRC1_INDEX] == VOL1_ADC_CHANNEL);
    assert(VR_ADC_CHANNELS[VR_SRC2_INDEX] == VOL2_ADC_CHANNEL);
    assert(VR_ADC_CHANNELS[VR_SRC1_INDEX] != VR_ADC_CHANNELS[VR_SRC2_INDEX]);
}

// adc_task() は VR_SRC1 を VOL1 のチャネル、VR_SRC2 を VOL2 のチャネルから読む。
// 両者を大きく異なる値にしておくことで、入れ替わりが起きれば必ず失敗する。
static void test_reads_expected_channels(void) {
    stub_gpio_reset();
    adc_init();
    assert(stub_gpio.adc_init_count == 1);

    stub_gpio.adc[VOL1_ADC_CHANNEL] = ADC_SCALE_RAW_MAX; // VOL1 全開
    stub_gpio.adc[VOL2_ADC_CHANNEL] = 0;                 // VOL2 全閉
    adc_task();

    assert(stub_gpio.adc_read_count[VOL1_ADC_CHANNEL] == 1);
    assert(stub_gpio.adc_read_count[VOL2_ADC_CHANNEL] == 1);
    // 初回サンプルは移動平均を充填して即時反映される (adc_scale.c)。
    assert(adc_get_vr(VR_SRC1_INDEX) == 255);
    assert(adc_get_vr(VR_SRC2_INDEX) == 0);

    // 逆向きにしても追従する (デッドバンドを超える変化)。
    stub_gpio.adc[VOL1_ADC_CHANNEL] = 0;
    stub_gpio.adc[VOL2_ADC_CHANNEL] = ADC_SCALE_RAW_MAX;
    for (int i = 0; i < ADC_SCALE_WINDOW_SIZE; i++) {
        adc_task();
    }
    assert(adc_get_vr(VR_SRC1_INDEX) == 0);
    assert(adc_get_vr(VR_SRC2_INDEX) == 255);
}

// SW/バックライト/I2Cのピンに割り当てたアナログチャネルを踏んでいない。
// (旧リビジョンのモジュール番号ストラップは ADCチャネル2 = PC4 を使っていたが、
//  実基板では PC4 がバックライトの LED_DATA。)
static void test_does_not_touch_other_channels(void) {
    stub_gpio_reset();
    adc_init();
    adc_task();

    for (int ch = 0; ch < STUB_ADC_CHANNEL_COUNT; ch++) {
        if (ch == VOL1_ADC_CHANNEL || ch == VOL2_ADC_CHANNEL) {
            continue;
        }
        assert(stub_gpio.adc_read_count[ch] == 0);
    }
}

static void test_out_of_range_index(void) {
    stub_gpio_reset();
    adc_init();
    adc_task();
    assert(adc_get_vr(VR_COUNT) == 0);
    assert(adc_get_vr(255) == 0);
}

int main(void) {
    test_channel_assignment();
    test_reads_expected_channels();
    test_does_not_touch_other_channels();
    test_out_of_range_index();

    printf("test_adc_io: OK\n");
    return 0;
}
