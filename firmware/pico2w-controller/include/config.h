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
#define DDC_I2C_PORT i2c0
#define DDC_I2C_SDA_PIN 4
#define DDC_I2C_SCL_PIN 5
#define DDC_I2C_BAUDRATE_HZ (100 * 1000)

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
