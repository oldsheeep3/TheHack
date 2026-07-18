// ddc_tally_decode.c (GPIO/Pico SDK非依存) のタリーデコード純粋関数テスト。
// 既知バイト列サンプルで Program/Preview/Off、対象/非対象カメラID、
// ブロードキャスト、壊れたフレームを網羅する。
//
// ここで使うバイト列は ddc_tally_decode.c 冒頭コメントに記載した「仮定の」
// フレーム構造 ([0]dest_camera_id [1]command_length [2]command_category
// [3]parameter) に基づく自己整合的なテストであり、実機のATEM/カメラから
// 採取した値ではない(実機構造は未確認、同ファイルのTODO参照)。実機確認後に
// 仮定値が更新された場合は、このテストの定数も合わせて更新すること。

#include <assert.h>
#include <stdio.h>

#include "ddc_tally.h"

#define TARGET_CAMERA_ID 1
#define OTHER_CAMERA_ID 2
#define BROADCAST_CAMERA_ID 0

// ddc_tally_decode.c 内の仮定値と一致させる (ヘッダには露出していないため複製)。
#define CATEGORY_TALLY 0x0A
#define CATEGORY_UNKNOWN 0x0B
#define PARAM_PROGRAM 0x00
#define PARAM_PREVIEW 0x01
#define PARAM_OFF 0x02

static void test_program_for_target_camera(void) {
    uint8_t bytes[] = {TARGET_CAMERA_ID, 2, CATEGORY_TALLY, PARAM_PROGRAM};
    tally_decode_result_t result = ddc_tally_decode(bytes, sizeof(bytes), TARGET_CAMERA_ID);
    assert(result.matched);
    assert(result.state == TALLY_STATE_PROGRAM);
}

static void test_preview_for_target_camera(void) {
    uint8_t bytes[] = {TARGET_CAMERA_ID, 2, CATEGORY_TALLY, PARAM_PREVIEW};
    tally_decode_result_t result = ddc_tally_decode(bytes, sizeof(bytes), TARGET_CAMERA_ID);
    assert(result.matched);
    assert(result.state == TALLY_STATE_PREVIEW);
}

static void test_off_for_target_camera(void) {
    uint8_t bytes[] = {TARGET_CAMERA_ID, 2, CATEGORY_TALLY, PARAM_OFF};
    tally_decode_result_t result = ddc_tally_decode(bytes, sizeof(bytes), TARGET_CAMERA_ID);
    assert(result.matched);
    assert(result.state == TALLY_STATE_OFF);
}

static void test_broadcast_matches_any_camera(void) {
    uint8_t bytes[] = {BROADCAST_CAMERA_ID, 2, CATEGORY_TALLY, PARAM_PROGRAM};
    tally_decode_result_t result = ddc_tally_decode(bytes, sizeof(bytes), OTHER_CAMERA_ID);
    assert(result.matched);
    assert(result.state == TALLY_STATE_PROGRAM);
}

static void test_non_target_camera_is_ignored(void) {
    uint8_t bytes[] = {OTHER_CAMERA_ID, 2, CATEGORY_TALLY, PARAM_PROGRAM};
    tally_decode_result_t result = ddc_tally_decode(bytes, sizeof(bytes), TARGET_CAMERA_ID);
    assert(!result.matched);
}

static void test_unknown_category_is_ignored(void) {
    uint8_t bytes[] = {TARGET_CAMERA_ID, 2, CATEGORY_UNKNOWN, PARAM_PROGRAM};
    tally_decode_result_t result = ddc_tally_decode(bytes, sizeof(bytes), TARGET_CAMERA_ID);
    assert(!result.matched);
}

static void test_too_short_frame_is_ignored(void) {
    uint8_t bytes[] = {TARGET_CAMERA_ID, 2, CATEGORY_TALLY}; // parameterバイトが無い
    tally_decode_result_t result = ddc_tally_decode(bytes, sizeof(bytes), TARGET_CAMERA_ID);
    assert(!result.matched);

    tally_decode_result_t empty_result = ddc_tally_decode(NULL, 0, TARGET_CAMERA_ID);
    assert(!empty_result.matched);
}

static void test_inconsistent_command_length_is_ignored(void) {
    // command_length(10) が実際の受信バイト数を超えており、壊れた/未完のフレーム。
    uint8_t bytes[] = {TARGET_CAMERA_ID, 10, CATEGORY_TALLY, PARAM_PROGRAM};
    tally_decode_result_t result = ddc_tally_decode(bytes, sizeof(bytes), TARGET_CAMERA_ID);
    assert(!result.matched);
}

int main(void) {
    test_program_for_target_camera();
    test_preview_for_target_camera();
    test_off_for_target_camera();
    test_broadcast_matches_any_camera();
    test_non_target_camera_is_ignored();
    test_unknown_category_is_ignored();
    test_too_short_frame_is_ignored();
    test_inconsistent_command_length_is_ignored();

    printf("test_ddc_tally: OK\n");
    return 0;
}
