#ifndef PICO2W_CONTROLLER_BUTTONS_H
#define PICO2W_CONTROLLER_BUTTONS_H

#include <stdbool.h>
#include <stdint.h>

#include "config.h"

// ---- デバウンス状態機械 (GPIO非依存, ホストテスト対象: buttons_debounce.c) ----
//
// 走査周期 BUTTON_SCAN_INTERVAL_MS でサンプリングし、BUTTON_DEBOUNCE_STABLE_SAMPLES
// 回連続で同じ生値が観測されて初めて安定状態とみなす。BUTTON_DEBOUNCE_MS (config.h)
// を走査周期で割った回数がしきい値になる。

#define BUTTON_SCAN_INTERVAL_MS 1
#define BUTTON_DEBOUNCE_STABLE_SAMPLES (BUTTON_DEBOUNCE_MS / BUTTON_SCAN_INTERVAL_MS)

typedef enum {
    BUTTON_EVENT_NONE = 0,
    BUTTON_EVENT_PRESSED,
    BUTTON_EVENT_RELEASED,
} button_event_t;

typedef struct {
    bool stable_pressed;      // 確定済みの安定状態
    bool candidate_pressed;   // 直近の生サンプル値
    uint8_t consecutive_count; // candidate_pressed が連続した回数
} button_debouncer_t;

void button_debouncer_init(button_debouncer_t *db);

// 生入力(raw_pressed)を1サンプル投入する。BUTTON_DEBOUNCE_STABLE_SAMPLES回連続で
// 同じ値が観測され、かつ安定状態と異なる場合にのみ状態を確定させ、遷移イベントを返す。
// 押しっぱなし・チャタリング中は BUTTON_EVENT_NONE を返し続ける(非連打)。
button_event_t button_debouncer_sample(button_debouncer_t *db, bool raw_pressed);

// ---- キーマトリクス走査 & イベントキュー (GPIO依存: buttons.c) ----

typedef struct {
    uint8_t button_id;
    uint64_t timestamp_ms;
} button_press_event_t;

void buttons_init(void);
void buttons_task(void);

// 走査で確定した押下イベントをFIFOから1件取り出す。無ければ false を返す。
bool buttons_pop_event(button_press_event_t *out_event);

#endif // PICO2W_CONTROLLER_BUTTONS_H
