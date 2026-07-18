#include "ch32fun.h"

#include "module_hooks.h"

// 初期化 + メインループ骨格。SW/ADC/バックライト/I2Cスレーブの実処理は各後続タスクが
// module_hooks.h のフック関数を strong 定義することで結線される (このタスクでは
// weak no-op が呼ばれる)。SRAM 2KB制約のため静的バッファは持たない。
int main(void) {
    SystemInit();
    funGpioInitAll();

    switches_init();
    adc_init();
    backlight_init();
    i2c_slave_init();

    while (1) {
        switches_task();
        adc_task();
        backlight_task();
        i2c_slave_task();
    }
}
