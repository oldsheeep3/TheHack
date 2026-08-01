#ifndef PICO2W_CONTROLLER_FAKE_MODULES_H
#define PICO2W_CONTROLLER_FAKE_MODULES_H

// デバッグ用 fake モジュール層 (FAKE_MODULES ビルドのみ)。
//
// 実機のスイッチングモジュール(CH32V003)が1台も無い状態で、SW/VR集約(state_agg)・
// 変化検出/定期送出(main.c)・HID記述子/レポート送出(usb_hid.c)・バックライト配布
// (backlight.c)を通しで検証するための開発専用の差し替え層。ENABLE_FAKE_MODULES=ON の
// ビルドでは i2c_modules.c の代わりに i2c_modules_fake.c がリンクされ、I2Cバスの代わりに
// PCからのデバッグ用HIDレポート0x04が「モジュールの状態」を供給する。
//
// backlight_codec.c/state_agg.c と同じく、純粋部(パース/適用)とI/O部(HID送出・状態保持)を
// 分離してある。純粋部のみ fake_modules_codec.c に置き、ホストテスト対象とする。

#include <stdbool.h>
#include <stdint.h>

#include "state_agg.h"

// デバッグ用出力レポート0x04をパースした1モジュール分の指定内容。
typedef struct {
    uint8_t module_index;
    bool present; // false にすると「不通のモジュール」を再現できる(障害隔離の確認用)
    uint8_t switches;
    uint8_t vr[MODULE_VR_COUNT];
} fake_module_command_t;

// ---- 純粋部 (Pico SDK非依存, ホストテスト対象: fake_modules_codec.c) ----

// デバッグ用出力レポート0x04のボディをパースする。長さ不一致・範囲外 module_index は
// 棄却して false を返す(backlight_parse_output_report と同じ方針)。switches は
// 下位4bit(MODULE_SWITCH_COUNT分)のみ採用し、上位bitは切り捨てる。
bool fake_modules_parse_debug_report(const uint8_t *buffer, uint16_t bufsize, fake_module_command_t *out_cmd);

// パース済みコマンドをモジュール状態配列へ適用する。範囲外 module_index は無視する。
void fake_modules_apply(const fake_module_command_t *cmd, module_state_array_t *states);

// ---- I/O部 (TinyUSB依存: i2c_modules_fake.c) ----

// usb_hid.c の SET_REPORT コールバックから呼ばれる。パース成功時のみ内部の状態配列へ
// 適用する。
void fake_modules_on_debug_report(const uint8_t *buffer, uint16_t bufsize);

// usb_hid.c の GET_REPORT コールバックから呼ばれる(settings_build_feature_report と同じ形)。
// 直近に backlight_task() から「配布された」バックライト(module_index + 4灯分RGB)を
// シリアライズして返し、書き込んだバイト数を返す。まだ一度も配布されていない場合は
// module_index に FAKE_BACKLIGHT_MODULE_NONE を入れて返す。reqlen が足りない場合は0を返す。
uint16_t fake_modules_build_backlight_feature_report(uint8_t *buffer, uint16_t reqlen);

// usb_hid.c の SET_REPORT (出力レポート0x06) から呼ばれる。マジックバイトが一致した場合のみ
// BOOTSEL再起動を予約する。実際の再起動は次の i2c_modules_poll() が行う(USBコールバックの
// 中でチップをリセットせず、ホストへの応答を返しきってから落とすため)。
void fake_modules_on_reboot_report(const uint8_t *buffer, uint16_t bufsize);

#endif // PICO2W_CONTROLLER_FAKE_MODULES_H
