// Blackmagicタリーコマンドのデコード (Pico SDK非依存)。GPIO/割込みに依存する
// バス捕捉ロジック(ddc_tally.c)とは独立させ、ホストの gcc でそのままビルド・
// テストできるようにしてある (test/test_ddc_tally.c 参照)。buttons.c /
// buttons_debounce.c の分離パターンに倣う。
//
// ---- プロトコル前提 (未確認・要実機検証) ----
// ATEM Mini ↔ カメラ間で HDMI DDCライン上に流れる Blackmagic Camera Control
// Protocol の正確なフレーム構造は、この実装時点で一次資料から確認できていない
// (docs/specs/pico2w-controller-firmware.md §2.3 「プロトコル不確実性」参照)。
// 本デコーダは、Blackmagic Design が公開している "SDI/HDMI Camera Control
// Protocol" に見られる一般的な構造(宛先device ID + コマンド長 + カテゴリ/
// パラメータ + ペイロード)を土台にした仮定の実装であり、以下は TODO:
//
//   [0] dest_camera_id   宛先カメラ番号。0 = 全カメラ向けブロードキャストと仮定。
//   [1] command_length   [2]以降に続くコマンド部分のバイト数と仮定。
//   [2] command_category タリー相当のコマンドカテゴリと仮定する値
//                         (ASSUMED_TALLY_CATEGORY)。実機のカテゴリIDは未確認。
//   [3] parameter         Program/Preview/Off の種別と仮定する値。実機の
//                         パラメータIDは未確認。
//   [4..] payload         本デコードでは未使用。
//
// TODO: 実機のDDCラインをロジックアナライザ等でキャプチャし、上記の仮定値
// (カテゴリ/パラメータID)を実測値に差し替えること。呼び出し側のインターフェース
// (ddc_tally_decode の入出力)は変えずに済むよう設計している。

#include "ddc_tally.h"

#define ASSUMED_TALLY_CATEGORY 0x0Au // TODO: 実機確認が取れていない仮定値
#define ASSUMED_PARAM_PROGRAM 0x00u
#define ASSUMED_PARAM_PREVIEW 0x01u
#define ASSUMED_PARAM_OFF 0x02u

#define BROADCAST_CAMERA_ID 0u

#define HEADER_LEN 4u // dest_camera_id, command_length, command_category, parameter

tally_decode_result_t ddc_tally_decode(const uint8_t *bytes, size_t len, uint8_t camera_id) {
    tally_decode_result_t result = {.matched = false, .state = TALLY_STATE_OFF};

    if (bytes == NULL || len < HEADER_LEN) {
        return result; // ヘッダに満たない短すぎるフレームは無視
    }

    uint8_t dest_camera_id = bytes[0];
    uint8_t command_length = bytes[1];
    uint8_t command_category = bytes[2];
    uint8_t parameter = bytes[3];

    if (dest_camera_id != BROADCAST_CAMERA_ID && dest_camera_id != camera_id) {
        return result; // 他カメラ宛
    }

    // 申告されたcommand_lengthが実際に受信できたバイト数を超える場合は
    // 壊れた/未完のフレームとみなして無視する。
    if ((size_t)command_length + 2u > len) {
        return result;
    }

    if (command_category != ASSUMED_TALLY_CATEGORY) {
        return result; // タリー以外のコマンドカテゴリ(未対応)
    }

    result.matched = true;
    switch (parameter) {
        case ASSUMED_PARAM_PROGRAM:
            result.state = TALLY_STATE_PROGRAM;
            break;
        case ASSUMED_PARAM_PREVIEW:
            result.state = TALLY_STATE_PREVIEW;
            break;
        case ASSUMED_PARAM_OFF:
        default:
            result.state = TALLY_STATE_OFF;
            break;
    }
    return result;
}
