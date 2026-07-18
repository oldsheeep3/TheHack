// モジュール状態配列 → 入力レポート0x01バイト列のパッキング(Pico SDK非依存)。
// I/Oに依存しない純粋関数のみを置き、ホストのgccでそのままビルド・テストできる
// (test/test_state_agg.c参照)。buttons_debounce.c/ddc_tally_decode.cで採用していた
// 「純粋部 vs I/O依存部の分離」パターンを踏襲する。

#include "state_agg.h"

void state_agg_pack(const module_state_array_t *states, uint8_t seq, uint8_t *out) {
    uint8_t present_bitmap = 0;
    for (int i = 0; i < MAX_MODULES; i++) {
        if (states->modules[i].present) {
            present_bitmap = (uint8_t)(present_bitmap | (1u << i));
        }
    }
    out[0] = present_bitmap;

    for (int i = 0; i < MAX_MODULES; i++) {
        out[1 + i] = (uint8_t)(states->modules[i].switches & 0x0Fu);
    }

    int vr_offset = 1 + MAX_MODULES;
    for (int i = 0; i < MAX_MODULES; i++) {
        out[vr_offset + i * 2] = states->modules[i].vr[0];
        out[vr_offset + i * 2 + 1] = states->modules[i].vr[1];
    }

    out[HID_REPORT_STATE_IN_LEN - 1] = seq;
}

bool state_agg_vr_should_report(uint8_t prev, uint8_t next) {
    int delta = (int)next - (int)prev;
    if (delta < 0) {
        delta = -delta;
    }
    return delta >= VR_DEADBAND_DELTA;
}
