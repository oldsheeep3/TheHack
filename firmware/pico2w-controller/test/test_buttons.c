// buttons_debounce.c (GPIO非依存) のデバウンス状態機械テスト。
// チャタリング入力→単一イベント、同時押し、押しっぱなし非連打を網羅する。

#include <assert.h>
#include <stdio.h>

#include "buttons.h"

static void feed(button_debouncer_t *db, const bool *samples, int count,
                  const button_event_t *expected_events) {
    for (int i = 0; i < count; i++) {
        button_event_t event = button_debouncer_sample(db, samples[i]);
        assert(event == expected_events[i]);
    }
}

// チャタリング入力(短時間のON/OFF往復)は無視され、しきい値回数連続で
// 安定した時点で1回だけ BUTTON_EVENT_PRESSED が発生することを確認する。
static void test_chattering_produces_single_press_event(void) {
    button_debouncer_t db;
    button_debouncer_init(&db);

    // BUTTON_DEBOUNCE_STABLE_SAMPLES == 5 前提: 末尾5個の true が生サンプルとして
    // 連続した時点(先頭のチャタリング区間中の true も連続にカウントされ得る)で
    // 初めて確定し、以降は押しっぱなしでも再発火しない。
    const bool samples[] = {
        true, false, true, false, true, // チャタリング (安定しない)
        true, true, true, true,         // ここまでで true が5回連続し確定
        true, true, true, true,         // 押しっぱなし継続 (再発火しない)
    };
    const button_event_t expected[] = {
        BUTTON_EVENT_NONE, BUTTON_EVENT_NONE, BUTTON_EVENT_NONE,
        BUTTON_EVENT_NONE, BUTTON_EVENT_NONE,
        BUTTON_EVENT_NONE, BUTTON_EVENT_NONE, BUTTON_EVENT_NONE,
        BUTTON_EVENT_PRESSED,
        BUTTON_EVENT_NONE, BUTTON_EVENT_NONE, BUTTON_EVENT_NONE, BUTTON_EVENT_NONE,
    };

    feed(&db, samples, sizeof(samples) / sizeof(samples[0]), expected);
}

// 押しっぱなし状態が続く間は、確定イベント後に再度 PRESSED が連打されない
// (安定状態と同じ値が来てもイベントは発火しない)。
static void test_held_down_does_not_repeat(void) {
    button_debouncer_t db;
    button_debouncer_init(&db);

    for (int i = 0; i < BUTTON_DEBOUNCE_STABLE_SAMPLES; i++) {
        button_event_t event = button_debouncer_sample(&db, true);
        if (i == BUTTON_DEBOUNCE_STABLE_SAMPLES - 1) {
            assert(event == BUTTON_EVENT_PRESSED);
        } else {
            assert(event == BUTTON_EVENT_NONE);
        }
    }

    for (int i = 0; i < 50; i++) {
        assert(button_debouncer_sample(&db, true) == BUTTON_EVENT_NONE);
    }

    // 離した場合は BUTTON_DEBOUNCE_STABLE_SAMPLES 回連続で1回だけ RELEASED。
    for (int i = 0; i < BUTTON_DEBOUNCE_STABLE_SAMPLES; i++) {
        button_event_t event = button_debouncer_sample(&db, false);
        if (i == BUTTON_DEBOUNCE_STABLE_SAMPLES - 1) {
            assert(event == BUTTON_EVENT_RELEASED);
        } else {
            assert(event == BUTTON_EVENT_NONE);
        }
    }
}

// 同時押し: 複数ボタンの状態機械は互いに独立しており、片方の状態が
// もう片方の確定タイミングに影響しないことを確認する。
static void test_simultaneous_presses_are_independent(void) {
    button_debouncer_t db_a, db_b;
    button_debouncer_init(&db_a);
    button_debouncer_init(&db_b);

    button_event_t last_a = BUTTON_EVENT_NONE;
    button_event_t last_b = BUTTON_EVENT_NONE;
    for (int i = 0; i < BUTTON_DEBOUNCE_STABLE_SAMPLES; i++) {
        last_a = button_debouncer_sample(&db_a, true);
        last_b = button_debouncer_sample(&db_b, true);
    }

    assert(last_a == BUTTON_EVENT_PRESSED);
    assert(last_b == BUTTON_EVENT_PRESSED);
}

int main(void) {
    test_chattering_produces_single_press_event();
    test_held_down_does_not_repeat();
    test_simultaneous_presses_are_independent();

    printf("test_buttons: OK\n");
    return 0;
}
