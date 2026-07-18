// switches_debounce.c (GPIO非依存) のデバウンス状態機械テスト。
// チャタリング入力→単一の安定遷移、押しっぱなし非連打、複数SW独立性、
// STATEレジスタへのビット順マッピングを網羅する。

#include <assert.h>
#include <stdio.h>

#include "module_config.h"
#include "switches_debounce.h"

// チャタリング入力(短時間のON/OFF往復)は無視され、しきい値回数連続で
// 安定した時点で1回だけ確定状態が変化することを確認する。
static void test_chattering_produces_single_stable_transition(void) {
    switches_debouncer_t db;
    switches_debouncer_init(&db);

    const uint8_t bit = (uint8_t)(1u << SW_BIT_PGM1_SRC1);

    // SWITCHES_DEBOUNCE_STABLE_SAMPLES == 5 前提: チャタリング区間の後、
    // bit が5回連続した時点で初めて確定する。
    const uint8_t samples[] = {
        bit, 0, bit, 0, bit, // チャタリング (安定しない)
        bit, bit, bit, bit,  // ここまでで bit が5回連続し確定
        bit, bit, bit, bit,  // 押しっぱなし継続 (再度確定しても値は変わらない)
    };
    uint8_t prev_state = 0;
    int transitions = 0;
    for (unsigned i = 0; i < sizeof(samples) / sizeof(samples[0]); i++) {
        uint8_t state = switches_debouncer_sample(&db, samples[i]);
        if (state != prev_state) {
            transitions++;
            assert(i == 8); // 5回連続一致した直後(先頭のチャタリング分も加算)
            assert(state == bit);
        }
        prev_state = state;
    }
    assert(transitions == 1);
}

// 押しっぱなし状態が続く間は、確定後に同じ生値を投入し続けても
// 確定状態が変化しない(非連打, 冪等)ことを確認する。
static void test_held_down_does_not_repeat(void) {
    switches_debouncer_t db;
    switches_debouncer_init(&db);

    const uint8_t bit = (uint8_t)(1u << SW_BIT_PGM2_SRC2);

    uint8_t state = 0;
    for (int i = 0; i < SWITCHES_DEBOUNCE_STABLE_SAMPLES; i++) {
        state = switches_debouncer_sample(&db, bit);
    }
    assert(state == bit);

    for (int i = 0; i < 50; i++) {
        assert(switches_debouncer_sample(&db, bit) == bit);
    }

    // 離した場合もしきい値回数連続で確定し、以降0を維持する(非連打)。
    for (int i = 0; i < SWITCHES_DEBOUNCE_STABLE_SAMPLES; i++) {
        state = switches_debouncer_sample(&db, 0);
    }
    assert(state == 0);
    for (int i = 0; i < 50; i++) {
        assert(switches_debouncer_sample(&db, 0) == 0);
    }
}

// 同時押し/独立トグル: 4SWそれぞれが他のSWの状態に影響されず独立に
// デバウンス・確定することを確認する。
static void test_simultaneous_and_independent_toggles(void) {
    switches_debouncer_t db;
    switches_debouncer_init(&db);

    const uint8_t bit_a = (uint8_t)(1u << SW_BIT_PGM1_SRC1);
    const uint8_t bit_b = (uint8_t)(1u << SW_BIT_PGM2_SRC1);

    // PGM1×SRC1 と PGM2×SRC1 を同時に押す。
    uint8_t state = 0;
    for (int i = 0; i < SWITCHES_DEBOUNCE_STABLE_SAMPLES; i++) {
        state = switches_debouncer_sample(&db, (uint8_t)(bit_a | bit_b));
    }
    assert(state == (uint8_t)(bit_a | bit_b));

    // PGM1×SRC1 のみ離す。PGM2×SRC1 は押されたまま変化しない。
    for (int i = 0; i < SWITCHES_DEBOUNCE_STABLE_SAMPLES; i++) {
        state = switches_debouncer_sample(&db, bit_b);
    }
    assert(state == bit_b);
}

// 確定4bitのビット順 (b0=PGM1×SRC1, b1=PGM1×SRC2, b2=PGM2×SRC1, b3=PGM2×SRC2) が
// module_config.h の定義通りに STATE レジスタへ反映され、上位4bitは0埋めされる。
static void test_state_register_bit_mapping(void) {
    assert(SW_BIT_PGM1_SRC1 == 0);
    assert(SW_BIT_PGM1_SRC2 == 1);
    assert(SW_BIT_PGM2_SRC1 == 2);
    assert(SW_BIT_PGM2_SRC2 == 3);

    switches_debouncer_t db;
    switches_debouncer_init(&db);

    uint8_t raw = (uint8_t)((1u << SW_BIT_PGM1_SRC2) | (1u << SW_BIT_PGM2_SRC2));
    uint8_t state = 0;
    for (int i = 0; i < SWITCHES_DEBOUNCE_STABLE_SAMPLES; i++) {
        state = switches_debouncer_sample(&db, raw);
    }

    uint8_t reg = switches_state_to_register(state);
    assert(reg == (uint8_t)((1u << 1) | (1u << 3)));
    assert((reg & 0xF0u) == 0); // 上位4bitは0埋め

    // 上位bitが混入した入力でも、レジスタ化時にマスクされる。
    assert(switches_state_to_register(0xFFu) == 0x0Fu);
}

int main(void) {
    test_chattering_produces_single_stable_transition();
    test_held_down_does_not_repeat();
    test_simultaneous_and_independent_toggles();
    test_state_register_bit_mapping();

    printf("test_switches: OK\n");
    return 0;
}
