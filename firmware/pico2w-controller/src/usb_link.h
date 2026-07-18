#ifndef PICO2W_CONTROLLER_USB_LINK_H
#define PICO2W_CONTROLLER_USB_LINK_H

#include <stdint.h>

#include "ddc_tally.h"

// 押下イベントの送信方式は USB-CDC(シリアル) を採用する。
// HIDはOS標準ドライバで認識できる利点があるがレポート記述子が固定長で、
// 可変長JSONの送出やPC/スマホ双方でのデバッグ(シリアルターミナルでの目視確認)が
// しづらい。本ファームは §4.1 のJSONをそのまま1行送出したいため、CDCを選ぶ。
// CDC自体は TinyUSB (Pico SDKの pico_enable_stdio_usb 経由) を使用する。

void usb_link_init(void);
void usb_link_task(void);

// 押下イベント1件を §4.1 のJSONスキーマで1行送出する。
// USB未接続時は送信をドロップする(バッファリングしない)。メインループの走査
// レイテンシを守るため、再接続後の再送も行わない。
void usb_link_send_button_event(uint8_t button_id, uint64_t timestamp_ms);

// タリー状態変化イベント1件を §4.1 に準拠したJSON形式(event種別 "tally")で1行送出する。
// button_press と同様、USB未接続時はドロップし、バッファリング・再送は行わない。
void usb_link_send_tally_event(tally_state_t state, uint64_t timestamp_ms);

#endif // PICO2W_CONTROLLER_USB_LINK_H
