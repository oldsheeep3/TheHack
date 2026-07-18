// switches.h のI/O実装。SW_ROW_PINS(PGM1,PGM2)をアクティブLowで1本ずつ走査し、
// SW_COL_PINS(SRC1,SRC2、プルアップ入力)のLowで押下を検出する。生サンプルは
// switches_debounce.c (GPIO非依存) にそのまま渡し、確定状態のみをここで保持する。
// SW_ROW_PINS/SW_COL_PINS の値は基板設計確定前の暫定プレースホルダ (module_config.c)。

#include "switches.h"

#include "ch32fun.h"

#include "module_config.h"
#include "switches_debounce.h"

// 行切り替え後、列ピンの信号が安定するのを待つ時間。
#define SWITCHES_ROW_SETTLE_US 2

static switches_debouncer_t debouncer;
static uint8_t current_state;

static uint8_t read_raw_matrix(void) {
    uint8_t raw = 0;

    for (int row = 0; row < SW_ROW_COUNT; row++) {
        for (int rr = 0; rr < SW_ROW_COUNT; rr++) {
            funDigitalWrite(SW_ROW_PINS[rr], rr == row ? FUN_LOW : FUN_HIGH);
        }
        Delay_Us(SWITCHES_ROW_SETTLE_US);

        for (int col = 0; col < SW_COL_COUNT; col++) {
            if (!funDigitalRead(SW_COL_PINS[col])) { // アクティブLow
                raw |= (uint8_t)(1u << SW_BIT_INDEX(row, col));
            }
        }
    }

    for (int rr = 0; rr < SW_ROW_COUNT; rr++) {
        funDigitalWrite(SW_ROW_PINS[rr], FUN_HIGH);
    }
    return raw;
}

void switches_init(void) {
    for (int row = 0; row < SW_ROW_COUNT; row++) {
        funPinMode(SW_ROW_PINS[row], GPIO_CFGLR_OUT_10Mhz_PP);
        funDigitalWrite(SW_ROW_PINS[row], FUN_HIGH); // 非選択行はHigh
    }
    for (int col = 0; col < SW_COL_COUNT; col++) {
        funPinMode(SW_COL_PINS[col], GPIO_CFGLR_IN_PUPD);
        funDigitalWrite(SW_COL_PINS[col], FUN_HIGH); // プルアップを選択
    }

    switches_debouncer_init(&debouncer);
    current_state = 0;
}

void switches_task(void) {
    uint8_t raw = read_raw_matrix();
    current_state = switches_state_to_register(switches_debouncer_sample(&debouncer, raw));
}

uint8_t switches_get_state(void) {
    return current_state;
}
