// module_hooks.h の weak デフォルト実装 (no-op)。対応する src/*.c が後続タスクで
// 追加され strong 定義を提供すると、リンカはそちらを優先する。

#include "module_hooks.h"

__attribute__((weak)) void switches_init(void) {}
__attribute__((weak)) void switches_task(void) {}

__attribute__((weak)) void adc_init(void) {}
__attribute__((weak)) void adc_task(void) {}

__attribute__((weak)) void backlight_init(void) {}
__attribute__((weak)) void backlight_task(void) {}

__attribute__((weak)) void i2c_slave_init(void) {}
__attribute__((weak)) void i2c_slave_task(void) {}
