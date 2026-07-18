// switches_debounce.h のI/O非依存な状態機械実装。CH32V003 SRAM 2KB制約を意識し、
// 状態は switches_debouncer_t (SW数バイト程度) に収めている。

#include "switches_debounce.h"

void switches_debouncer_init(switches_debouncer_t *db) {
    db->stable_state = 0;
    db->candidate_state = 0;
    for (int i = 0; i < SW_COUNT; i++) {
        db->consecutive_count[i] = 0;
    }
}

uint8_t switches_debouncer_sample(switches_debouncer_t *db, uint8_t raw_state) {
    for (int i = 0; i < SW_COUNT; i++) {
        uint8_t mask = (uint8_t)(1u << i);
        uint8_t raw_bit = (uint8_t)((raw_state & mask) != 0);
        uint8_t candidate_bit = (uint8_t)((db->candidate_state & mask) != 0);

        if (raw_bit == candidate_bit) {
            if (db->consecutive_count[i] < SWITCHES_DEBOUNCE_STABLE_SAMPLES) {
                db->consecutive_count[i]++;
            }
        } else {
            db->candidate_state = (uint8_t)(raw_bit ? (db->candidate_state | mask) : (db->candidate_state & ~mask));
            db->consecutive_count[i] = 1;
        }

        if (db->consecutive_count[i] >= SWITCHES_DEBOUNCE_STABLE_SAMPLES) {
            db->stable_state = (uint8_t)(raw_bit ? (db->stable_state | mask) : (db->stable_state & ~mask));
        }
    }
    return db->stable_state;
}

uint8_t switches_state_to_register(uint8_t stable_state) {
    return (uint8_t)(stable_state & 0x0Fu);
}
