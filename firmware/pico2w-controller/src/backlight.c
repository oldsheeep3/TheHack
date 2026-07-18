// 出力レポート0x02の受領キュー + I2C `0x10 BACKLIGHT` 書込(I2C依存)。
// バイト列のパース自体はI2C/GPIO非依存のbacklight_codec.cに分離してあり
// (ホストテスト対象)、ここではHID受信からI2C書込までを疎結合にするキューイングのみを
// 担う(buttons.c/buttons_debounce.cの分離パターンを踏襲)。

#include "backlight.h"

#include "i2c_modules.h"

#define BACKLIGHT_QUEUE_SIZE 8

static backlight_command_t queue[BACKLIGHT_QUEUE_SIZE];
static uint8_t queue_head;
static uint8_t queue_tail;
static uint8_t queue_count;

void backlight_init(void) {
    queue_head = 0;
    queue_tail = 0;
    queue_count = 0;
}

void backlight_on_output_report(const uint8_t *report, uint16_t len) {
    backlight_command_t cmd;
    if (!backlight_parse_output_report(report, len, &cmd)) {
        return; // 不正なレポートは棄却
    }
    backlight_enqueue_command(&cmd);
}

void backlight_enqueue_command(const backlight_command_t *cmd) {
    if (queue_count >= BACKLIGHT_QUEUE_SIZE) {
        // キュー溢れ: 最古のコマンドを破棄して直近のバックライト指定を優先する
        // (buttons.cのイベントキューと同方針)。
        queue_head = (uint8_t)((queue_head + 1) % BACKLIGHT_QUEUE_SIZE);
        queue_count--;
    }
    queue[queue_tail] = *cmd;
    queue_tail = (uint8_t)((queue_tail + 1) % BACKLIGHT_QUEUE_SIZE);
    queue_count++;
}

void backlight_task(void) {
    if (queue_count == 0) {
        return;
    }

    backlight_command_t cmd = queue[queue_head];
    queue_head = (uint8_t)((queue_head + 1) % BACKLIGHT_QUEUE_SIZE);
    queue_count--;

    // 不通/タイムアウトでもこのコマンドを破棄して次回へ進むだけで、STATE集約ポーリングは
    // 継続する(障害隔離, 親仕様書 §4非機能要件)。
    (void)i2c_modules_write_backlight(cmd.module_index, cmd.rgb);
}
