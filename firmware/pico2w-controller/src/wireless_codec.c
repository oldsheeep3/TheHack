// ワイヤレス制御チャネルのメッセージエンコード/デコード(CYW43/lwIP非依存)。
// STATEフレームのパッキングはstate_agg_pack(state_agg.c)、BACKLIGHTフレームの
// パースはbacklight_parse_output_report(backlight_codec.c)へそのまま委譲し、
// HID入力/出力レポートと二重実装しない(.claude/review-patterns.md「設計・責務分離」)。
// Pico SDKに依存しないためホストのgccでそのままビルド・テストできる
// (test/test_wireless_codec.c参照)。

#include "wireless.h"

void wireless_codec_build_state_frame(const module_state_array_t *states, uint8_t seq, uint8_t *out) {
    out[0] = WIRELESS_MSG_TYPE_STATE;
    state_agg_pack(states, seq, &out[1]); // 入力レポート0x01と同一パッキング(state_agg.c共用)
}

bool wireless_codec_parse_backlight_frame(const uint8_t *frame, size_t len, backlight_command_t *out_cmd) {
    if (frame == NULL || len != WIRELESS_FRAME_BACKLIGHT_LEN) {
        return false;
    }
    if (frame[0] != WIRELESS_MSG_TYPE_BACKLIGHT) {
        return false; // type不一致(STATEフレーム等)は棄却
    }
    // ペイロードは出力レポート0x02と同一形式のため、パース自体はbacklight_codec.cへ委譲する。
    return backlight_parse_output_report(&frame[1], len - 1, out_cmd);
}
