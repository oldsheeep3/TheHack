// ベンダー定義USB-HIDデバイス(TinyUSB依存)。
//
// 本タスクでは入力レポート0x01(親仕様書 §4.1, HID_REPORT_ID_STATE_IN)のみを実装する。
// 出力レポート0x02(HID_REPORT_ID_BACKLIGHT_OUT, バックライト指定)とfeatureレポートは
// P2-002で追加する。config.hに契約(report ID/長さ)は用意済みのため、P2-002では
// 下記 desc_hid_report にOutputアイテムを追記し、EPNUM_HID_OUTを持つ
// TUD_HID_INOUT_DESCRIPTOR へ切り替え、tud_hid_set_report_cb を実装すればよい
// (呼び出し側の公開APIはこのファイル内で完結するよう設計している)。
//
// pico_enable_stdio_usb(CMakeLists参照)は使わず、本ファイルで独自のUSBデバイス記述子
// (デバイス/コンフィグ/文字列/HIDレポート)を定義する。stdio_usbは固定のCDC記述子
// しか提供せずベンダーHIDと共存できないため。デバッグ用printf()はUART側
// (pico_enable_stdio_uart)へ出す。

#include "usb_hid.h"

#include <stdio.h>
#include <string.h>

#include "tusb.h"

#include "config.h"

// TinyUSBがOSSプロジェクトのテスト用途に公開している共有VID/PID。
// 量産時は独自のUSB VID/PIDを取得して差し替えること(TODO, 実機検証未了)。
#define USB_VID 0xCafe
#define USB_PID 0x4011
#define USB_BCD 0x0200

// ---- デバイスディスクリプタ ----

static tusb_desc_device_t const desc_device = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = USB_BCD,
    .bDeviceClass = 0x00,
    .bDeviceSubClass = 0x00,
    .bDeviceProtocol = 0x00,
    .bMaxPacketSize0 = CFG_TUD_ENDPOINT0_SIZE,
    .idVendor = USB_VID,
    .idProduct = USB_PID,
    .bcdDevice = 0x0100,
    .iManufacturer = 0x01,
    .iProduct = 0x02,
    .iSerialNumber = 0x03,
    .bNumConfigurations = 0x01,
};

uint8_t const *tud_descriptor_device_cb(void) {
    return (uint8_t const *)&desc_device;
}

// ---- HIDレポートディスクリプタ ----
// ベンダー定義(Usage Page 0xFF00)。入力レポート0x01のみ(親仕様書 §4.1)。
static uint8_t const desc_hid_report[] = {
    0x06, 0x00, 0xFF,              // Usage Page (Vendor Defined 0xFF00)
    0x09, 0x01,                    // Usage (0x01)
    0xA1, 0x01,                    // Collection (Application)
    0x85, HID_REPORT_ID_STATE_IN,  //   Report ID (1)
    0x09, 0x02,                    //   Usage (0x02)
    0x15, 0x00,                    //   Logical Minimum (0)
    0x26, 0xFF, 0x00,               //   Logical Maximum (255)
    0x75, 0x08,                    //   Report Size (8)
    0x95, HID_REPORT_STATE_IN_LEN, //   Report Count
    0x81, 0x02,                    //   Input (Data,Var,Abs)
    0xC0,                          // End Collection
};

uint8_t const *tud_hid_descriptor_report_cb(uint8_t instance) {
    (void)instance;
    return desc_hid_report;
}

// ---- コンフィグディスクリプタ (HIDインターフェース1個, IN方向のみ) ----

enum {
    ITF_NUM_HID = 0,
    ITF_NUM_TOTAL,
};

#define EPNUM_HID_IN 0x81
#define CONFIG_TOTAL_LEN (TUD_CONFIG_DESC_LEN + TUD_HID_DESC_LEN)

static uint8_t const desc_configuration[] = {
    TUD_CONFIG_DESCRIPTOR(1, ITF_NUM_TOTAL, 0, CONFIG_TOTAL_LEN, 0x00, 100),
    TUD_HID_DESCRIPTOR(ITF_NUM_HID, 0, HID_ITF_PROTOCOL_NONE, sizeof(desc_hid_report), EPNUM_HID_IN,
                        CFG_TUD_HID_EP_BUFSIZE, 5),
};

uint8_t const *tud_descriptor_configuration_cb(uint8_t index) {
    (void)index;
    return desc_configuration;
}

// ---- 文字列ディスクリプタ ----
// controller_id(main/sub)はシリアル番号文字列(index 3)で提示する
// (get_unique_board_id() + get_controller_id(), 親仕様書 §4.1)。

static char const *const string_desc_arr[] = {
    NULL,                // 0: 言語ID (別途処理)
    "NxTEND-THE-HACK",   // 1: Manufacturer
    "pico2w-controller", // 2: Product
    NULL,                // 3: Serial (動的生成)
};

static uint16_t desc_str_buf[32];

uint16_t const *tud_descriptor_string_cb(uint8_t index, uint16_t langid) {
    (void)langid;

    if (index == 0) {
        desc_str_buf[0] = (uint16_t)((TUSB_DESC_STRING << 8) | (2 + 2));
        desc_str_buf[1] = 0x0409; // English (US)
        return desc_str_buf;
    }

    char serial_buf[UNIQUE_BOARD_ID_STRING_BUF_SIZE + 8];
    char const *str;

    if (index == 3) {
        char board_id[UNIQUE_BOARD_ID_STRING_BUF_SIZE];
        get_unique_board_id(board_id, sizeof(board_id));
        snprintf(serial_buf, sizeof(serial_buf), "%s-%s", board_id, get_controller_id());
        str = serial_buf;
    } else if (index < (sizeof(string_desc_arr) / sizeof(string_desc_arr[0]))) {
        str = string_desc_arr[index];
    } else {
        return NULL;
    }

    if (str == NULL) {
        return NULL;
    }

    size_t str_len = strlen(str);
    size_t max_chars = (sizeof(desc_str_buf) / sizeof(desc_str_buf[0])) - 1;
    if (str_len > max_chars) {
        str_len = max_chars;
    }

    for (size_t i = 0; i < str_len; i++) {
        desc_str_buf[1 + i] = (uint16_t)str[i];
    }
    desc_str_buf[0] = (uint16_t)((TUSB_DESC_STRING << 8) | (2 + 2 * str_len));

    return desc_str_buf;
}

// ---- TinyUSB HIDクラスコールバック ----

// GET_REPORT: feature/inレポートの明示取得要求。本タスクでは未使用(0を返す)。
uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type, uint8_t *buffer,
                                uint16_t reqlen) {
    (void)instance;
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)reqlen;
    return 0;
}

// SET_REPORT: 出力レポート0x02(バックライト)はP2-002で実装する。現状は無視する。
void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
                            uint8_t const *buffer, uint16_t bufsize) {
    (void)instance;
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)bufsize;
}

// ---- 公開API ----

void usb_hid_init(void) {
    tusb_init();
}

void usb_hid_task(void) {
    tud_task();
}

void usb_hid_send_state(const module_state_array_t *states, uint8_t seq) {
    if (!tud_hid_ready()) {
        return; // USB未接続/未準備: ドロップする(バッファリング・ブロックしない)
    }
    uint8_t report[HID_REPORT_STATE_IN_LEN];
    state_agg_pack(states, seq, report);
    tud_hid_report(HID_REPORT_ID_STATE_IN, report, sizeof(report));
}
