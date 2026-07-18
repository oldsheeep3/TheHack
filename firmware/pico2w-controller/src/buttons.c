// キーマトリクス走査 (GPIO依存)。デバウンスの状態機械自体は buttons_debounce.c
// (Pico SDK非依存) に分離してあり、ここでは行/列ピンの生値をサンプルとして
// 渡すだけにする。

#include "buttons.h"

#include "hardware/gpio.h"
#include "pico/stdlib.h"

#include "config.h"

static button_debouncer_t debouncers[BUTTON_MATRIX_MAX_BUTTONS];

// 走査(buttons_task)で確定した押下イベントをusb_link側が取り出すまで保持するFIFO。
// 送信側のUSB状態に関わらず走査を止めないよう、キューで疎結合にする。
#define BUTTON_EVENT_QUEUE_SIZE 32
static button_press_event_t event_queue[BUTTON_EVENT_QUEUE_SIZE];
static uint8_t event_queue_head;
static uint8_t event_queue_tail;
static uint8_t event_queue_count;

static void event_queue_push(button_press_event_t event) {
    if (event_queue_count >= BUTTON_EVENT_QUEUE_SIZE) {
        // キュー溢れ: 最古のイベントを破棄して直近を優先する(送信側の詰まりで
        // 走査自体をブロックしないため)。
        event_queue_head = (uint8_t)((event_queue_head + 1) % BUTTON_EVENT_QUEUE_SIZE);
        event_queue_count--;
    }
    event_queue[event_queue_tail] = event;
    event_queue_tail = (uint8_t)((event_queue_tail + 1) % BUTTON_EVENT_QUEUE_SIZE);
    event_queue_count++;
}

bool buttons_pop_event(button_press_event_t *out_event) {
    if (event_queue_count == 0) {
        return false;
    }
    *out_event = event_queue[event_queue_head];
    event_queue_head = (uint8_t)((event_queue_head + 1) % BUTTON_EVENT_QUEUE_SIZE);
    event_queue_count--;
    return true;
}

void buttons_init(void) {
    for (int r = 0; r < BUTTON_MATRIX_MAX_ROWS; r++) {
        gpio_init(BUTTON_ROW_PINS[r]);
        gpio_set_dir(BUTTON_ROW_PINS[r], GPIO_OUT);
        gpio_put(BUTTON_ROW_PINS[r], 1); // アクティブLow走査: 非選択行はHigh
    }
    for (int c = 0; c < BUTTON_MATRIX_MAX_COLS; c++) {
        gpio_init(BUTTON_COL_PINS[c]);
        gpio_set_dir(BUTTON_COL_PINS[c], GPIO_IN);
        gpio_pull_up(BUTTON_COL_PINS[c]);
    }
    for (int i = 0; i < BUTTON_MATRIX_MAX_BUTTONS; i++) {
        button_debouncer_init(&debouncers[i]);
    }
    event_queue_head = 0;
    event_queue_tail = 0;
    event_queue_count = 0;
}

void buttons_task(void) {
    for (int r = 0; r < BUTTON_MATRIX_MAX_ROWS; r++) {
        for (int rr = 0; rr < BUTTON_MATRIX_MAX_ROWS; rr++) {
            gpio_put(BUTTON_ROW_PINS[rr], rr == r ? 0 : 1);
        }
        sleep_us(2); // 行切り替え後、列ピンの信号が安定するのを待つ

        for (int c = 0; c < BUTTON_MATRIX_MAX_COLS; c++) {
            bool raw_pressed = !gpio_get(BUTTON_COL_PINS[c]); // アクティブLow
            int button_id = r * BUTTON_MATRIX_MAX_COLS + c;
            button_event_t event = button_debouncer_sample(&debouncers[button_id], raw_pressed);
            if (event == BUTTON_EVENT_PRESSED) {
                button_press_event_t press = {
                    .button_id = (uint8_t)button_id,
                    .timestamp_ms = time_us_64() / 1000,
                };
                event_queue_push(press);
            }
        }
    }

    for (int rr = 0; rr < BUTTON_MATRIX_MAX_ROWS; rr++) {
        gpio_put(BUTTON_ROW_PINS[rr], 1);
    }
}
