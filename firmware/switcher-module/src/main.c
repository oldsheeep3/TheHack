#include "ch32fun.h"

#include "module_hooks.h"

// 初期化 + メインループ。SW/ADC/バックライト/I2Cスレーブの実処理は module_hooks.h の
// フック関数として各サブシステム(switches.c/adc.c/backlight.c/i2c_slave.c)が
// strong 定義することで結線される。ループ順は「走査→デバウンス(switches_task) /
// ADC→スケール(adc_task) → I2C STATE更新・BACKLIGHT受領反映(i2c_slave_task) →
// SK6812駆動(backlight_task)」(親仕様書§2.4/§4.5)。i2c_slave_taskをbacklight_taskの
// 直前に置くことで、同一ループ内で受領したBACKLIGHTを最短で駆動に反映する。
#ifdef WIRING_TEST
// 配線確認モード (src/wiring_test.c, `make WIRING_TEST=1`)。通常ビルドには含まれない。
void wiring_test_run(void);
#endif

int main(void) {
    SystemInit();
    funGpioInitAll();

#ifdef WIRING_TEST
    wiring_test_run(); // 戻らない: I2C/SW/LEDの通常動作は行わない
#endif

    switches_init();
    adc_init();
    backlight_init();
    i2c_slave_init();

    while (1) {
        switches_task();
        adc_task();
        i2c_slave_task();
        backlight_task();
    }
}
