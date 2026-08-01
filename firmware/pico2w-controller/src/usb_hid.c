// ベンダー定義USB-HIDデバイス(TinyUSB依存)。
//
// 入力レポート0x01(親仕様書 §4.1, HID_REPORT_ID_STATE_IN)に加え、出力レポート0x02
// (HID_REPORT_ID_BACKLIGHT_OUT, バックライト指定, §2.3)とfeatureレポート
// (HID_REPORT_ID_SETTINGS_FEATURE, 設定投入/読出, §4.6)を実装する。出力/feature
// レポートの受信自体はこのファイルで受け、実処理(パース・キュー投入・フラッシュ保存)は
// backlight.c/settings.cへ委譲する。
//
// pico_enable_stdio_usb(CMakeLists参照)は使わず、本ファイルで独自のUSBデバイス記述子
// (デバイス/コンフィグ/文字列/HIDレポート)を定義する。stdio_usbは固定のCDC記述子
// しか提供せずベンダーHIDと共存できないため。デバッグ用printf()はUART側
// (pico_enable_stdio_uart)へ出す。

#include "usb_hid.h"

#include <stdio.h>
#include <string.h>

#include "tusb.h"

#include "backlight.h"
#include "config.h"
#include "settings.h"

#ifdef FAKE_MODULES
#include "fake_modules.h"
#endif

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
// ベンダー定義(Usage Page 0xFF00)。入力レポート0x01・出力レポート0x02・featureレポート
// (設定)の3つのReport IDを同一コレクション内に持つ(親仕様書 §4.1/§4.6)。
static uint8_t const desc_hid_report[] = {
    0x06, 0x00, 0xFF, // Usage Page (Vendor Defined 0xFF00)
    0x09, 0x01,       // Usage (0x01)
    0xA1, 0x01,       // Collection (Application)

    0x85, HID_REPORT_ID_STATE_IN,  //   Report ID (1)
    0x09, 0x02,                    //   Usage (0x02)
    0x15, 0x00,                    //   Logical Minimum (0)
    0x26, 0xFF, 0x00,               //   Logical Maximum (255)
    0x75, 0x08,                    //   Report Size (8)
    0x95, HID_REPORT_STATE_IN_LEN, //   Report Count
    0x81, 0x02,                    //   Input (Data,Var,Abs)

    0x85, HID_REPORT_ID_BACKLIGHT_OUT,  //   Report ID (2)
    0x09, 0x03,                         //   Usage (0x03)
    0x15, 0x00,                         //   Logical Minimum (0)
    0x26, 0xFF, 0x00,                    //   Logical Maximum (255)
    0x75, 0x08,                         //   Report Size (8)
    0x95, HID_REPORT_BACKLIGHT_OUT_LEN, //   Report Count
    0x91, 0x02,                         //   Output (Data,Var,Abs)

    0x85, HID_REPORT_ID_SETTINGS_FEATURE, //   Report ID (3)
    0x09, 0x04,                           //   Usage (0x04)
    0x15, 0x00,                           //   Logical Minimum (0)
    0x26, 0xFF, 0x00,                      //   Logical Maximum (255)
    0x75, 0x08,                           //   Report Size (8)
    0x95, SETTINGS_SERIALIZED_LEN,        //   Report Count
    0xB1, 0x02,                           //   Feature (Data,Var,Abs)

#ifdef FAKE_MODULES
    // デバッグ用(FAKE_MODULESビルドのみ)。本番ビルドの記述子には現れないため、PC側から見た
    // レポート契約(§4.1)は変わらない。
    0x85, HID_REPORT_ID_FAKE_MODULE_OUT,  //   Report ID (4): PC→Pico 偽モジュール状態の注入
    0x09, 0x05,                           //   Usage (0x05)
    0x15, 0x00,                           //   Logical Minimum (0)
    0x26, 0xFF, 0x00,                      //   Logical Maximum (255)
    0x75, 0x08,                           //   Report Size (8)
    0x95, HID_REPORT_FAKE_MODULE_OUT_LEN, //   Report Count
    0x91, 0x02,                           //   Output (Data,Var,Abs)

    0x85, HID_REPORT_ID_FAKE_REBOOT_OUT,  //   Report ID (6): PC→Pico BOOTSELへ再起動
    0x09, 0x07,                           //   Usage (0x07)
    0x15, 0x00,                           //   Logical Minimum (0)
    0x26, 0xFF, 0x00,                      //   Logical Maximum (255)
    0x75, 0x08,                           //   Report Size (8)
    0x95, HID_REPORT_FAKE_REBOOT_OUT_LEN, //   Report Count
    0x91, 0x02,                           //   Output (Data,Var,Abs)

    // 入力レポートは0x01の1種類に保つため、配布バックライトの読出はfeatureにする
    // (理由はconfig.hのHID_REPORT_FAKE_BACKLIGHT_FEATURE_LENのコメント参照)。
    0x85, HID_REPORT_ID_FAKE_BACKLIGHT_FEATURE,  //   Report ID (5): PC←Pico 配布バックライトの読出
    0x09, 0x06,                                  //   Usage (0x06)
    0x15, 0x00,                                  //   Logical Minimum (0)
    0x26, 0xFF, 0x00,                             //   Logical Maximum (255)
    0x75, 0x08,                                  //   Report Size (8)
    0x95, HID_REPORT_FAKE_BACKLIGHT_FEATURE_LEN, //   Report Count
    0xB1, 0x02,                                  //   Feature (Data,Var,Abs)
#endif

    0xC0, // End Collection
};

uint8_t const *tud_hid_descriptor_report_cb(uint8_t instance) {
    (void)instance;
    return desc_hid_report;
}

// ---- コンフィグディスクリプタ (HIDインターフェース1個, IN+OUT方向) ----
// Featureレポート(設定)はコントロール転送(EP0)経由のため専用エンドポイントは不要。
// 出力レポート0x02(バックライト)用にOUTエンドポイントを追加し、TUD_HID_INOUT_DESCRIPTOR
// (TinyUSB提供マクロ)へ切り替える。クロスビルド環境が無いため、実機ビルド時にマクロ名/
// TUD_HID_INOUT_DESC_LENが使用中のTinyUSBバージョンで提供されていることを確認すること。

enum {
    ITF_NUM_HID = 0,
    ITF_NUM_TOTAL,
};

#define EPNUM_HID_OUT 0x01
#define EPNUM_HID_IN 0x81
#define CONFIG_TOTAL_LEN (TUD_CONFIG_DESC_LEN + TUD_HID_INOUT_DESC_LEN)

static uint8_t const desc_configuration[] = {
    TUD_CONFIG_DESCRIPTOR(1, ITF_NUM_TOTAL, 0, CONFIG_TOTAL_LEN, 0x00, 100),
    TUD_HID_INOUT_DESCRIPTOR(ITF_NUM_HID, 0, HID_ITF_PROTOCOL_NONE, sizeof(desc_hid_report), EPNUM_HID_OUT,
                              EPNUM_HID_IN, CFG_TUD_HID_EP_BUFSIZE, 5),
};

uint8_t const *tud_descriptor_configuration_cb(uint8_t index) {
    (void)index;
    return desc_configuration;
}

// ---- 文字列ディスクリプタ ----
// controller_id(main/sub)はシリアル番号文字列(index 3)で提示する
// (get_unique_board_id() + usb_hid_set_controller_id()で設定された値, 親仕様書 §4.1/§4.6)。

static char const *const string_desc_arr[] = {
    NULL,                // 0: 言語ID (別途処理)
    "NxTEND-THE-HACK",   // 1: Manufacturer
    "pico2w-controller", // 2: Product
    NULL,                // 3: Serial (動的生成)
};

static uint16_t desc_str_buf[32];

// HIDシリアル文字列に載せるcontroller_id。デフォルトはビルド時ロールで、main.cが
// 設定(settings)から解決した値をusb_hid_set_controller_id()経由で上書きする(§4.6)。
#define USB_HID_CONTROLLER_ID_MAX_LEN 8
static char current_controller_id[USB_HID_CONTROLLER_ID_MAX_LEN] = "main";

void usb_hid_set_controller_id(const char *controller_id) {
    snprintf(current_controller_id, sizeof(current_controller_id), "%s", controller_id);
}

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
        snprintf(serial_buf, sizeof(serial_buf), "%s-%s", board_id, current_controller_id);
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

// GET_REPORT: 設定feature(HID_REPORT_ID_SETTINGS_FEATURE)の読出のみ対応する。
// PCがフラッシュ保存済みの設定を確認・再投入判断するために使う(§4.6)。
uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type, uint8_t *buffer,
                                uint16_t reqlen) {
    (void)instance;
    if (report_type == HID_REPORT_TYPE_FEATURE && report_id == HID_REPORT_ID_SETTINGS_FEATURE) {
        return settings_build_feature_report(buffer, reqlen);
    }
#ifdef FAKE_MODULES
    if (report_type == HID_REPORT_TYPE_FEATURE && report_id == HID_REPORT_ID_FAKE_BACKLIGHT_FEATURE) {
        return fake_modules_build_backlight_feature_report(buffer, reqlen);
    }
#endif
    return 0;
}

// SET_REPORT: 出力レポート0x02(バックライト, §2.3)とfeature(設定, §4.6)を受領する。
// いずれもパース/検証のみここから委譲し、I2C書込・フラッシュ書込は呼び出し先
// (backlight.c/settings.c)が担う。
void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id, hid_report_type_t report_type,
                            uint8_t const *buffer, uint16_t bufsize) {
    (void)instance;

    // TinyUSBは呼び出し元によって引数の形が違う(lib/tinyusb/src/class/hid/hid_device.c):
    //   - 制御転送(SET_REPORT): 実際のreport_id + Report IDを除去済みのボディ
    //   - 割込みOUTエンドポイント: report_id=0 固定 + Report IDを含んだ生バッファ
    // ホスト(Windows)は出力レポートを後者で送ってくるため、report_idだけで振り分けると
    // 出力レポートが一切届かない。ここで前者の形へ揃えてから振り分ける。
    if (report_id == 0 && bufsize >= 1) {
        report_id = buffer[0];
        buffer++;
        bufsize--;
    }

    if (report_type == HID_REPORT_TYPE_OUTPUT && report_id == HID_REPORT_ID_BACKLIGHT_OUT) {
        backlight_on_output_report(buffer, bufsize);
    } else if (report_type == HID_REPORT_TYPE_FEATURE && report_id == HID_REPORT_ID_SETTINGS_FEATURE) {
        settings_on_feature_report(buffer, bufsize);
    }
#ifdef FAKE_MODULES
    else if (report_type == HID_REPORT_TYPE_OUTPUT && report_id == HID_REPORT_ID_FAKE_MODULE_OUT) {
        fake_modules_on_debug_report(buffer, bufsize);
    } else if (report_type == HID_REPORT_TYPE_OUTPUT && report_id == HID_REPORT_ID_FAKE_REBOOT_OUT) {
        fake_modules_on_reboot_report(buffer, bufsize);
    }
#endif
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

