// adc_scale.c (ADC非依存) の境界値・移動平均・デッドバンド挙動を検証する。

#include <assert.h>
#include <stdio.h>

#include "adc_scale.h"

// 単純スケーリング(移動平均/デッドバンドを介さない)の境界値: 0/中央/最大/範囲外クランプ。
static void test_raw_to_u8_boundaries(void) {
    assert(adc_scale_raw_to_u8(0) == 0);
    assert(adc_scale_raw_to_u8(ADC_SCALE_RAW_MAX) == 255);
    assert(adc_scale_raw_to_u8(ADC_SCALE_RAW_MAX / 2) == 127); // 511*255/1023 = 127 (整数除算)

    // ADC分解能を超える範囲外の入力はクランプされ、最大値と同じ結果になる。
    assert(adc_scale_raw_to_u8(0xFFFF) == 255);
}

// 初回サンプルはウィンドウ全体を即時充填し、デッドバンド判定なしで反映される。
static void test_first_sample_is_immediate(void) {
    adc_scale_state_t state;
    adc_scale_init(&state);

    uint8_t out = adc_scale_update(&state, ADC_SCALE_RAW_MAX);
    assert(out == 255);
}

// 移動平均: ウィンドウ幅ぶんのサンプルが入るまで、平均値は投入したサンプルの平均になる。
static void test_moving_average(void) {
    adc_scale_state_t state;
    adc_scale_init(&state);

    // 初回で0充填(出力0)。
    uint8_t out = adc_scale_update(&state, 0);
    assert(out == 0);

    // ADC_SCALE_WINDOW_SIZE(4)回, 最大値を投入すると平均が最大値に収束し255になる。
    for (int i = 0; i < ADC_SCALE_WINDOW_SIZE; i++) {
        out = adc_scale_update(&state, ADC_SCALE_RAW_MAX);
    }
    assert(out == 255);
}

// デッドバンド: 移動平均後の差がADC_SCALE_DEADBAND未満なら出力は更新されない。
static void test_deadband_suppresses_small_change(void) {
    adc_scale_state_t state;
    adc_scale_init(&state);

    // 初回で raw=512 相当の値に安定させる(出力を固定するため十分な回数投入)。
    uint8_t baseline = 0;
    for (int i = 0; i < ADC_SCALE_WINDOW_SIZE + 1; i++) {
        baseline = adc_scale_update(&state, 512);
    }

    // 1サンプルだけウィンドウ内の1要素が変化する程度の微小変動 (+1) では、
    // 移動平均後の差がデッドバンド未満に留まり出力は変化しない。
    uint8_t out = adc_scale_update(&state, 513);
    assert(out == baseline);

    // 十分に大きな変化 (最大値) を連続投入すれば、いずれデッドバンドを超えて更新される。
    uint8_t changed = baseline;
    for (int i = 0; i < ADC_SCALE_WINDOW_SIZE; i++) {
        changed = adc_scale_update(&state, ADC_SCALE_RAW_MAX);
    }
    assert(changed > baseline);
}

int main(void) {
    test_raw_to_u8_boundaries();
    test_first_sample_is_immediate();
    test_moving_average();
    test_deadband_suppresses_small_change();

    printf("test_adc_scale: OK\n");
    return 0;
}
