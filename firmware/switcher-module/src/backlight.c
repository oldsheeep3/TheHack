// backlight.h のI/O実装。BACKLIGHT_DATA_PIN 1本へ、SK6812MINI-E×4チェーンを800kHz・
// GRB順のタイミングでビットバン駆動する。フレーム組み立て(RGB→GRB)はGPIO非依存の
// sk6812_frame.cに委譲し、ここでは変換済みフレームの送出のみを行う。
//
// タイミングはNOPループでの概算(48MHz動作, 1サイクル≈20.8ns)。目標値(datasheet基準:
// T0H=300ns/T0L=900ns, T1H=600ns/T1L=600ns)に対するベストエフォートであり、
// module_config.hの暫定ピン値と同様にPCB確定後・実機オシロでの再調整を要する。

#include "backlight.h"

#include "ch32fun.h"

#include "sk6812_frame.h"

#define SK6812_NOP_T0H 6
#define SK6812_NOP_T0L 34
#define SK6812_NOP_T1H 24
#define SK6812_NOP_T1L 16

// SK6812のフレーム確定に必要な最小Low時間(>80us, 仕様書§2.3)。
#define SK6812_RESET_LOW_US 80

static uint8_t pending_frame[SK6812_FRAME_BYTES];

static inline void delay_nops(uint32_t count) {
    for (uint32_t i = 0; i < count; i++) {
        asm volatile("nop");
    }
}

static void send_bit(uint8_t bit_value) {
    if (bit_value) {
        funDigitalWrite(BACKLIGHT_DATA_PIN, FUN_HIGH);
        delay_nops(SK6812_NOP_T1H);
        funDigitalWrite(BACKLIGHT_DATA_PIN, FUN_LOW);
        delay_nops(SK6812_NOP_T1L);
    } else {
        funDigitalWrite(BACKLIGHT_DATA_PIN, FUN_HIGH);
        delay_nops(SK6812_NOP_T0H);
        funDigitalWrite(BACKLIGHT_DATA_PIN, FUN_LOW);
        delay_nops(SK6812_NOP_T0L);
    }
}

static void send_byte(uint8_t byte_value) {
    for (int bit = 7; bit >= 0; bit--) {
        send_bit((uint8_t)((byte_value >> bit) & 0x01u));
    }
}

void backlight_init(void) {
    funPinMode(BACKLIGHT_DATA_PIN, GPIO_CFGLR_OUT_10Mhz_PP);
    funDigitalWrite(BACKLIGHT_DATA_PIN, FUN_LOW);
    for (int i = 0; i < SK6812_FRAME_BYTES; i++) {
        pending_frame[i] = 0; // 未受領時(初期状態)は全灯消灯
    }
}

void backlight_set_rgb(const uint8_t rgb[BACKLIGHT_LED_COUNT * 3]) {
    sk6812_frame_from_rgb(rgb, pending_frame);
}

void backlight_task(void) {
    // 割込み禁止区間を1灯(3バイト)単位に区切り、最小化する(仕様書§4)。
    for (int led = 0; led < BACKLIGHT_LED_COUNT; led++) {
        __disable_irq();
        for (int b = 0; b < 3; b++) {
            send_byte(pending_frame[led * 3 + b]);
        }
        __enable_irq();
    }
    Delay_Us(SK6812_RESET_LOW_US);
}
