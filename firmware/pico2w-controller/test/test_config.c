// プレースホルダ・テスト: config.h の定数と get_controller_id() が
// Pico SDK非依存でホスト上でも検証できることを確認する。
// debounce/tally decode ロジックの実装後は、対応するテストをここに追加する。

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "config.h"

int main(void) {
    assert(BUTTON_MATRIX_MAX_BUTTONS == BUTTON_MATRIX_MAX_ROWS * BUTTON_MATRIX_MAX_COLS);
    assert(BUTTON_MATRIX_MAX_BUTTONS <= 16);
    assert(strcmp(get_controller_id(), "main") == 0);

    printf("test_config: OK\n");
    return 0;
}
