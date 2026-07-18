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

#endif // PICO2W_CONTROLLER_USB_HID_H
