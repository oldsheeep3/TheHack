#ifndef PICO2W_CONTROLLER_USB_HID_H
#define PICO2W_CONTROLLER_USB_HID_H

#include "state_agg.h"

void usb_hid_init(void);
void usb_hid_task(void);

// 集約済みモジュール状態を入力レポート0x01(HID_REPORT_ID_STATE_IN)として送出する。
// 状態変化時に即時呼び出すことに加え、HID_STATE_SEND_INTERVAL_MSごとの定期送出にも使う
// (取りこぼし対策, 親仕様書 §2.2/§4.1)。USB未接続/未準備時は送出をドロップする
// (バッファリング・ブロックしない)。
void usb_hid_send_state(const module_state_array_t *states, uint8_t seq);

// HIDシリアル文字列に載せるcontroller_idを設定する。設定(settings)が保存済みロールを
// 持つ場合はそれを、未設定ならビルド時 CONTROLLER_ROLE を呼び出し側(main.c)が解決して
// 渡す(親仕様書 §4.1/§4.6)。USB列挙より前(usb_hid_init呼び出し前)に呼ぶこと。
void usb_hid_set_controller_id(const char *controller_id);

#endif // PICO2W_CONTROLLER_USB_HID_H
