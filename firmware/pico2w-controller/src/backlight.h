#ifndef PICO2W_CONTROLLER_BACKLIGHT_H
#define PICO2W_CONTROLLER_BACKLIGHT_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "config.h"

// 出力レポート0x02(HID_REPORT_ID_BACKLIGHT_OUT)を1件分パースした結果。
// RGBは受領したバイト順のまま保持する(SK6812のGRB変換はモジュール側で行うため, §4.1/§4.5)。
typedef struct {
    uint8_t module_index;
    uint8_t rgb[MODULE_REG_BACKLIGHT_LEN]; // 4灯 × 3バイト(RGB)
} backlight_command_t;

// ---- 純粋部 (I2C/GPIO非依存, ホストテスト対象: backlight_codec.c) ----

// 出力レポート0x02の生バイト列をパースする。長さが HID_REPORT_BACKLIGHT_OUT_LEN と一致し、
// module_index が MAX_MODULES 未満の場合のみ true を返し out_cmd へ格納する。
// 長さ不一致・範囲外 module_index は棄却(false)する。
bool backlight_parse_output_report(const uint8_t *report, size_t len, backlight_command_t *out_cmd);

// ---- I/O部 (受領キュー + I2C書込, I2C依存: backlight.c) ----

void backlight_init(void);

// HID出力レポート0x02のSET_REPORT受領コールバック(usb_hid.cから直接呼ばれる)。
// パースのみ行いキューへ積む。I2C書込は行わないため、HID受信・メインループを
// ブロックしない(親仕様書 §2.3)。
void backlight_on_output_report(const uint8_t *report, uint16_t len);

// 既にパース済みのバックライトコマンドをキューへ積む。backlight_on_output_reportと同じ
// キュー(溢れ時は最古を破棄)を共有するため、USB出力レポート0x02経由以外の受領元
// (無線チャネル, wireless.c)もこれを使えば分配ロジック(キュー+backlight_task)を
// 二重実装せずに済む(.claude/review-patterns.md「設計・責務分離」)。
void backlight_enqueue_command(const backlight_command_t *cmd);

// キューから最大1件を取り出し、対応モジュールのI2C `0x10 BACKLIGHT` へ書き込む。
// メインループから継続的に呼び出すこと。1回の呼び出しで消費するのは最大1件のため、
// STATEポーリングのレイテンシを大きく阻害しない。不通/タイムアウト時はそのコマンドを
// 破棄して次回へ進む(集約全体を止めない)。
void backlight_task(void);

#endif // PICO2W_CONTROLLER_BACKLIGHT_H
