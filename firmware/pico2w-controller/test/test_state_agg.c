// state_agg.c (I2C/USB非依存の純粋関数) のテスト。
// module_present ビットマップ(全接続/一部欠損)、SW/VRのバイト位置、seqローテート、
// VRデッドバンド判定を網羅する。

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "state_agg.h"

static module_state_array_t make_all_present(void) {
    module_state_array_t states;
    memset(&states, 0, sizeof(states));
    for (int i = 0; i < MAX_MODULES; i++) {
        states.modules[i].present = true;
        states.modules[i].switches = (uint8_t)(i & 0x0F);
        states.modules[i].vr[0] = (uint8_t)(i * 10);
        states.modules[i].vr[1] = (uint8_t)(255 - i * 10);
    }
    return states;
}

static void test_module_present_bitmap_all_connected(void) {
    module_state_array_t states = make_all_present();
    uint8_t out[HID_REPORT_STATE_IN_LEN];
    state_agg_pack(&states, 0, out);
    assert(out[0] == 0xFF);
}

static void test_module_present_bitmap_partial(void) {
    module_state_array_t states = make_all_present();
    states.modules[1].present = false;
    states.modules[5].present = false;
    uint8_t out[HID_REPORT_STATE_IN_LEN];
    state_agg_pack(&states, 0, out);
    uint8_t expected = (uint8_t)(0xFF & ~(1u << 1) & ~(1u << 5));
    assert(out[0] == expected);
}

static void test_switch_byte_positions(void) {
    module_state_array_t states = make_all_present();
    uint8_t out[HID_REPORT_STATE_IN_LEN];
    state_agg_pack(&states, 0, out);
    for (int i = 0; i < MAX_MODULES; i++) {
        assert(out[1 + i] == (uint8_t)(i & 0x0F));
    }
}

static void test_switch_byte_masks_to_4bit(void) {
    module_state_array_t states = make_all_present();
    states.modules[0].switches = 0xFF; // 上位ビットが立っていてもマスクされること
    uint8_t out[HID_REPORT_STATE_IN_LEN];
    state_agg_pack(&states, 0, out);
    assert(out[1] == 0x0F);
}

static void test_vr_byte_positions(void) {
    module_state_array_t states = make_all_present();
    uint8_t out[HID_REPORT_STATE_IN_LEN];
    state_agg_pack(&states, 0, out);
    int vr_offset = 1 + MAX_MODULES;
    for (int i = 0; i < MAX_MODULES; i++) {
        assert(out[vr_offset + i * 2] == (uint8_t)(i * 10));
        assert(out[vr_offset + i * 2 + 1] == (uint8_t)(255 - i * 10));
    }
}

static void test_seq_rotates_into_last_byte(void) {
    module_state_array_t states = make_all_present();
    uint8_t out[HID_REPORT_STATE_IN_LEN];

    state_agg_pack(&states, 0, out);
    assert(out[HID_REPORT_STATE_IN_LEN - 1] == 0);

    state_agg_pack(&states, 255, out);
    assert(out[HID_REPORT_STATE_IN_LEN - 1] == 255);

    state_agg_pack(&states, 42, out);
    assert(out[HID_REPORT_STATE_IN_LEN - 1] == 42);
}

static void test_vr_deadband_below_threshold_is_ignored(void) {
    assert(!state_agg_vr_should_report(100, 100));
    assert(!state_agg_vr_should_report(100, (uint8_t)(100 + (VR_DEADBAND_DELTA - 1))));
    assert(!state_agg_vr_should_report(100, (uint8_t)(100 - (VR_DEADBAND_DELTA - 1))));
}

static void test_vr_deadband_at_or_above_threshold_reports(void) {
    assert(state_agg_vr_should_report(100, (uint8_t)(100 + VR_DEADBAND_DELTA)));
    assert(state_agg_vr_should_report(100, (uint8_t)(100 - VR_DEADBAND_DELTA)));
}

int main(void) {
    test_module_present_bitmap_all_connected();
    test_module_present_bitmap_partial();
    test_switch_byte_positions();
    test_switch_byte_masks_to_4bit();
    test_vr_byte_positions();
    test_seq_rotates_into_last_byte();
    test_vr_deadband_below_threshold_is_ignored();
    test_vr_deadband_at_or_above_threshold_reports();

    printf("test_state_agg: OK\n");
    return 0;
}
