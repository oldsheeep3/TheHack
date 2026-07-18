#ifndef PICO2W_CONTROLLER_CONFIG_H
#define PICO2W_CONTROLLER_CONFIG_H

#include <stddef.h>
#include <stdint.h>

// ---- システム定数 (親仕様書 00-system-overview.md §4.0 が単一のソース) ----
#define MAX_MODULES 8
#define MODULE_SWITCH_COUNT 4 // (pgm1, pgm2) × (src1, src2)
#define MODULE_VR_COUNT 2     // src1, src2

// ---- I2C (モジュール集約用マスター, 親仕様書 §4.5) ----
#define MODULE_I2C_INSTANCE i2c0
#define MODULE_I2C_SDA_PIN 4
#define MODULE_I2C_SCL_PIN 5
#define MODULE_I2C_BAUD 400000 // 100k~400kHz

// モジュールI2Cアドレス: ベース 0x30 + モジュール番号(0..MAX_MODULES-1) = 0x30..0x37
#define MODULE_I2C_ADDR_BASE 0x30

// モジュール内レジスタマップ(親仕様書 §4.5)
#define MODULE_REG_STATE 0x00
#define MODULE_REG_STATE_LEN 3 // [0]SW状態(下位4bit) / [1]VR_SRC1(0..255) / [2]VR_SRC2(0..255)
#define MODULE_REG_BACKLIGHT 0x10
#define MODULE_REG_BACKLIGHT_LEN 12 // 4灯分RGB(write, P2-002で実装)
#define MODULE_REG_INFO 0xF0
#define MODULE_REG_INFO_LEN 4 // [0..1]fw version / [2]capabilities / [3]HW rev(契約のみ, 未使用)

// 1モジュールあたりのI2C read/writeタイムアウト。不通/未接続のモジュールがあっても
// 集約全体を止めないよう、タイムアウトしたモジュールはそのポーリング回だけスキップする。
#define MODULE_I2C_TIMEOUT_US 2000

// 全モジュール一巡のポーリング周期の目安(1kHz, 親仕様書 §2.1/§4.5)。
#define MODULE_POLL_INTERVAL_MS 1

// ---- USB-HID レポート (親仕様書 §4.1) ----
#define HID_REPORT_ID_STATE_IN 0x01          // Pico→PC: 集約状態
#define HID_REPORT_ID_BACKLIGHT_OUT 0x02     // PC→Pico: バックライト指定(P2-002で実装)
#define HID_REPORT_ID_SETTINGS_FEATURE 0x03  // PC<->Pico: 設定投入/読出(feature, P2-002で実装, §4.6)

// 入力レポート0x01のバイト長 = module_present(1) + SW×MAX_MODULES + VR×2×MAX_MODULES + seq(1)
#define HID_REPORT_STATE_IN_LEN (1 + MAX_MODULES + 2 * MAX_MODULES + 1)

// 出力レポート0x02のバイト長 = module_index(1) + RGB×4灯(12バイト)
#define HID_REPORT_BACKLIGHT_OUT_LEN (1 + 12)

// 状態変化が無くても定期送出する周期(取りこぼし対策, 親仕様書 §2.2/§4.1)。
#define HID_STATE_SEND_INTERVAL_MS 20

// ---- VRデッドバンド ----
// 前回送出値との差の絶対値がこの値未満のVR変化は送出対象外とする(0..255スケール)。
#define VR_DEADBAND_DELTA 3

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
