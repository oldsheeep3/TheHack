#ifndef PICO2W_CONTROLLER_CONFIG_H
#define PICO2W_CONTROLLER_CONFIG_H

#include <stddef.h>
#include <stdint.h>

// ---- システム定数 (親仕様書 00-system-overview.md §4.0 が単一のソース) ----
#define MAX_MODULES 8
#define MODULE_SWITCH_COUNT 4 // (pgm1, pgm2) × (src1, src2)
#define MODULE_VR_COUNT 2     // src1, src2

// ---- I2C (モジュール集約用マスター, 親仕様書 §4.5) ----
//
// 実基板 softswitcher_module_master は、モジュール1台につき専用のI2Cバスを1本引き出す
// (= MODULE_BUS_COUNT 本のスロット)。モジュールは数珠つなぎで、マスターに最も近い1台が
// バス1(1x07コネクタ)を使い、残りのバス2..5は2x08コネクタでそのまま次段へ渡される。
// 各モジュールは受け取った束の先頭ペアを自分のI2Cとして消費し、残りを1ペアずつずらして
// 次段へ渡す。つまり「数珠つなぎのn番目のモジュール = バスn」。
//
// モジュール側(CH32V003)にはアドレスストラップが無く全台が 0x30 で待ち受けるため、
// モジュール番号(HIDレポートのスロット位置)は「どのバスで応答したか」で決まる。
#define MODULE_BUS_COUNT 5
#define MODULE_I2C_ADDR 0x30

// バス1..5の {SDA, SCL} GPIO番号 (Pico実装ピン: 26/27pin, 31/32pin, 6/7pin, 4/5pin, 1/2pin)。
// RP2040/RP2350ではI2Cコントローラ(i2c0/i2c1)がGPIO番号から一意に決まるため、
// インスタンスは持たず module_bus.c が算出する。5本のバスを i2c0(バス1,3,5) と
// i2c1(バス2,4) の2コントローラで時分割して使う。
#define MODULE_BUS_PINS_INIT     \
    {                            \
        {20, 21}, /* バス1 */    \
        {26, 27}, /* バス2 */    \
        {4, 5},   /* バス3 */    \
        {2, 3},   /* バス4 */    \
        {0, 1},   /* バス5 */    \
    }

// I2Cバスクロック。仕様上は100k〜400kHzだが、マスター基板・モジュール基板とも外付けの
// プルアップ抵抗を持たずRP2040/RP2350の内蔵プルアップ(約50〜80kΩ)に頼る配線のため、
// 立ち上がりに余裕のある100kHzを既定とする(外付けプルアップを実装したら引き上げ可)。
//
// 内蔵プルアップだけだと立ち上がりが数µsかかり、100kHzでもACKの読み取りに間に合わない
// ことがある。切り分け用に -DMODULE_I2C_BAUD=<Hz> でビルド時に上書きできるようにしてある。
#ifndef MODULE_I2C_BAUD
#define MODULE_I2C_BAUD 100000
#endif

// モジュール内レジスタマップ(親仕様書 §4.5)
#define MODULE_REG_STATE 0x00
#define MODULE_REG_STATE_LEN 3 // [0]SW状態(下位4bit) / [1]VR_SRC1(0..255) / [2]VR_SRC2(0..255)
#define MODULE_REG_BACKLIGHT 0x10
#define MODULE_REG_BACKLIGHT_LEN 12 // 4灯分RGB(write, P2-002で実装)
#define MODULE_REG_INFO 0xF0
#define MODULE_REG_INFO_LEN 4 // [0..1]fw version / [2]capabilities / [3]HW rev(契約のみ, 未使用)

// 1モジュールあたりのI2C read/writeタイムアウト。不通/未接続のモジュールがあっても
// 集約全体を止めないよう、タイムアウトしたモジュールはそのポーリング回だけスキップする。
//
// バスクロックを下げると1トランザクションの所要時間が伸びるため、MODULE_I2C_BAUD を
// 下げる場合はこちらも合わせて伸ばすこと(1バイト書込 ≒ 18ビット / バスクロック)。
// 切り分け用に -DMODULE_I2C_TIMEOUT_US=<us> でビルド時に上書きできる。
#ifndef MODULE_I2C_TIMEOUT_US
#define MODULE_I2C_TIMEOUT_US 2000
#endif

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

// ---- デバッグ用 fake モジュール (FAKE_MODULES ビルドのみ) ----
// 実機のスイッチングモジュール(CH32V003)が手元に無い状態でSW/VR集約・HID経路を検証する
// ための開発専用チャネル。-DENABLE_FAKE_MODULES=ON のビルドでのみ定義され、本番ビルドの
// HID記述子・レポート契約(§4.1)には一切現れない。
#ifdef FAKE_MODULES
#define HID_REPORT_ID_FAKE_MODULE_OUT 0x04        // PC→Pico: 偽モジュールのpresent/SW/VRを注入
#define HID_REPORT_ID_FAKE_BACKLIGHT_FEATURE 0x05 // PC←Pico: 配布されたバックライトの読出

// 出力レポート0x04のバイト長 = module_index(1) + present(1) + switches(1) + VR×MODULE_VR_COUNT
#define HID_REPORT_FAKE_MODULE_OUT_LEN (3 + MODULE_VR_COUNT)

// featureレポート0x05のバイト長 = module_index(1) + 4灯分RGB(12)
// 入力レポートではなくfeatureにしているのは、入力レポートを0x01の1種類に保つため。
// PC側のHID実装(src/Switcher.Hid/Devices/HidSharpDevice.cs)は入力レポートの先頭
// Report IDを無条件に剥がすので、入力レポートを増やすとFAKE_MODULESビルドを本番アプリへ
// 繋いだときに状態レポートとして誤解釈されてしまう。
#define HID_REPORT_FAKE_BACKLIGHT_FEATURE_LEN (1 + MODULE_REG_BACKLIGHT_LEN)

// 未だ一度もバックライトが配布されていないことを示すmodule_indexのセンチネル。
#define FAKE_BACKLIGHT_MODULE_NONE 0xFF

// PC→Pico: BOOTSEL(USBマスストレージ書込モード)へ再起動する。このファームはCDCを持たず
// picotool経由のリセットができないため、デバッグビルドの焼き直しごとに物理ボタンの押下が
// 必要になる。それを避けるための開発専用コマンド。
#define HID_REPORT_ID_FAKE_REBOOT_OUT 0x06
#define HID_REPORT_FAKE_REBOOT_OUT_LEN 1
// 誤爆防止のマジックバイト。この値でなければ無視する。
#define FAKE_REBOOT_MAGIC 0xB7
#endif

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
