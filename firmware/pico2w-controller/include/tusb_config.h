#ifndef PICO2W_CONTROLLER_TUSB_CONFIG_H
#define PICO2W_CONTROLLER_TUSB_CONFIG_H

#ifdef __cplusplus
extern "C" {
#endif

#define CFG_TUSB_RHPORT0_MODE (OPT_MODE_DEVICE | OPT_MODE_FULL_SPEED)

#ifndef CFG_TUSB_OS
#define CFG_TUSB_OS OPT_OS_PICO
#endif

#define CFG_TUD_ENABLED 1
#define CFG_TUD_ENDPOINT0_SIZE 64

// ベンダー定義HID(src/usb_hid.c)のみ有効化。CDC(旧usb_link.c)は本タスクで廃止した。
#define CFG_TUD_CDC 0
#define CFG_TUD_MSC 0
#define CFG_TUD_MIDI 0
#define CFG_TUD_VENDOR 0
#define CFG_TUD_HID 1

// 入力レポート(report ID 1バイト + HID_REPORT_STATE_IN_LEN)がFS割込みEPの
// 最大パケットサイズ(64)に収まるため64固定。
#define CFG_TUD_HID_EP_BUFSIZE 64

#ifdef __cplusplus
}
#endif

#endif // PICO2W_CONTROLLER_TUSB_CONFIG_H
