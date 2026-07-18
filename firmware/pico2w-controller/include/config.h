#ifndef PICO2W_CONTROLLER_CONFIG_H
#define PICO2W_CONTROLLER_CONFIG_H

#include <stddef.h>
#include <stdint.h>

// ---- ボタンマトリクス (最大16ボタン想定) ----
#define BUTTON_MATRIX_MAX_ROWS 4
#define BUTTON_MATRIX_MAX_COLS 4
#define BUTTON_MATRIX_MAX_BUTTONS (BUTTON_MATRIX_MAX_ROWS * BUTTON_MATRIX_MAX_COLS)
#define BUTTON_DEBOUNCE_MS 5

extern const uint8_t BUTTON_ROW_PINS[BUTTON_MATRIX_MAX_ROWS];
extern const uint8_t BUTTON_COL_PINS[BUTTON_MATRIX_MAX_COLS];

// ---- タリーLED (赤/緑) ----
#define TALLY_LED_RED_PIN 14
#define TALLY_LED_GREEN_PIN 15

// ---- I2C (HDMI DDCライン スニッフィング用) ----
// 注意: ハードウェアI2Cペリフェラルは「自アドレス宛の通信にのみACKし応答する」
// 仕組みのため、ATEM↔カメラ間(我々のアドレス宛ではない)通信を正しく傍受できない。
// そのため ddc_tally.c では DDC_I2C_SDA_PIN/DDC_I2C_SCL_PIN をハードウェアI2Cに
// マッピングせず、プレーンGPIO入力+エッジ割込みでバスを直接モニタする
// (ビットバンによるパッシブスニッフィング、バスへは一切書き込まない)。
#define DDC_I2C_SDA_PIN 4
#define DDC_I2C_SCL_PIN 5

// ---- タリー対象カメラ / DDCタリー用I2Cアドレス ----
// TALLY_CAMERA_ID: このドングルが監視するカメラの番号(1始まり, 0はブロードキャスト
// 予約のため使用しない)。ビルド時定義 (-DTALLY_CAMERA_ID=N) で切り替える。
#ifndef TALLY_CAMERA_ID
#define TALLY_CAMERA_ID 1
#endif

// DDC_TALLY_I2C_ADDR: ATEM→カメラ間のタリー通知に使われると仮定する7bit I2Cアドレス。
// 一次資料で未確認の仮定値 (TODO: 実機のDDCラインをロジックアナライザ等でキャプチャし
// 確定させること)。EDID読み出し(0x50)以外に観測されるアドレスに差し替える想定。
#ifndef DDC_TALLY_I2C_ADDR
#define DDC_TALLY_I2C_ADDR 0x6E
#endif

// ---- コントローラー識別 ----
// メイン/サブの区別はビルド時定義 (-DCONTROLLER_ROLE=CONTROLLER_ROLE_SUB) で切り替える。
// プリプロセッサの #if で判定するため、enum ではなく #define で定義する。
#define CONTROLLER_ROLE_MAIN 0
#define CONTROLLER_ROLE_SUB 1

#ifndef CONTROLLER_ROLE
#define CONTROLLER_ROLE CONTROLLER_ROLE_MAIN
#endif

// RP2350 (フラッシュ) Unique Board ID を16進文字列で取得する。
// buf は最低 UNIQUE_BOARD_ID_STRING_BUF_SIZE バイト必要。
#define UNIQUE_BOARD_ID_STRING_BUF_SIZE 17
void get_unique_board_id(char *buf, size_t buf_len);

// 親仕様書 §4.1 の controller_id ("main"/"sub") を返す。
const char *get_controller_id(void);

#endif // PICO2W_CONTROLLER_CONFIG_H
