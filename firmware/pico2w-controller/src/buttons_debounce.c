// GPIO非依存のデバウンス状態機械。Pico SDKに依存しないためホストの gcc で
// そのままビルド・テストできる (test/test_buttons.c 参照)。

#include "buttons.h"

void button_debouncer_init(button_debouncer_t *db) {
    db->stable_pressed = false;
    db->candidate_pressed = false;
    db->consecutive_count = 0;
}

button_event_t button_debouncer_sample(button_debouncer_t *db, bool raw_pressed) {
    if (raw_pressed == db->candidate_pressed) {
        if (db->consecutive_count < BUTTON_DEBOUNCE_STABLE_SAMPLES) {
            db->consecutive_count++;
        }
    } else {
        db->candidate_pressed = raw_pressed;
        db->consecutive_count = 1;
    }

    if (db->consecutive_count >= BUTTON_DEBOUNCE_STABLE_SAMPLES &&
        db->candidate_pressed != db->stable_pressed) {
        db->stable_pressed = db->candidate_pressed;
        return db->stable_pressed ? BUTTON_EVENT_PRESSED : BUTTON_EVENT_RELEASED;
    }
    return BUTTON_EVENT_NONE;
}
