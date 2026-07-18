#ifndef PICO2W_CONTROLLER_DDC_TALLY_H
#define PICO2W_CONTROLLER_DDC_TALLY_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

// ---- タリー状態 (デコード純粋関数の出力 / LED制御の入力) ----
typedef enum {
    TALLY_STATE_OFF = 0,     // 消灯
    TALLY_STATE_PROGRAM,     // 赤 (オンエア)
    TALLY_STATE_PREVIEW,     // 緑 (プレビュー/サブ)
} tally_state_t;

typedef struct {
    bool matched;         // camera_id 宛(またはbroadcast)のタリーコマンドとして解釈できたか
    tally_state_t state;  // matched == true の場合のみ有効
} tally_decode_result_t;

// ---- コマンドデコード (Pico SDK非依存, ホストテスト対象: ddc_tally_decode.c) ----
//
// bytes/len: I2Cバスの1トランザクション(START〜STOP)で捕捉したバイト列から、
//            アドレスバイト(7bitアドレス+R/W)を除いたコマンド部分。
// camera_id: 自カメラID (config.h の TALLY_CAMERA_ID)。
//
// プロトコル構造は docs/specs/pico2w-controller-firmware.md §2.3 の通り未確定であり、
// 本実装は Blackmagic Design が公開する "SDI/HDMI Camera Control Protocol" に見られる
// 一般的なパケット構造(宛先device ID + コマンド長 + カテゴリ/パラメータ + ペイロード)を
// 土台にした **仮定** である。実機のDDCラインをキャプチャして確認できていないため、
// 詳細は ddc_tally_decode.c 冒頭のコメントおよび TODO を参照。
// 実機確認後は ddc_tally_decode.c の内部実装のみを差し替えれば、呼び出し側
// (ddc_tally.c / テスト) は変更不要になるよう設計している。
tally_decode_result_t ddc_tally_decode(const uint8_t *bytes, size_t len, uint8_t camera_id);

// ---- ハードウェア結線 (GPIO割込みベースのI2Cバス・パッシブスニッフィング,
//       Pico SDK依存: ddc_tally.c) ----

void ddc_tally_init(void);

// 割込みで捕捉済みのフレームをデコードし、タリー状態が変化していればLEDを更新して
// イベントキューに積む。メインループから継続的に呼び出すこと(ノンブロッキング)。
void ddc_tally_task(void);

typedef struct {
    tally_state_t state;
    uint64_t timestamp_ms;
} tally_state_event_t;

// 状態変化イベントを1件取り出す。無ければ false を返す
// (buttons_pop_event と同じFIFO+ドロップ最古方式、詳細は ddc_tally.c 参照)。
bool ddc_tally_pop_event(tally_state_event_t *out_event);

#endif // PICO2W_CONTROLLER_DDC_TALLY_H
