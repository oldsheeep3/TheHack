#ifndef SWITCHER_MODULE_MODULE_HOOKS_H
#define SWITCHER_MODULE_MODULE_HOOKS_H

// 各サブシステムの初期化/メインループタスク関数。実体は後続タスクで追加される
// switches.c (M-002) / adc.c, backlight.c (M-003) / i2c_slave.c (M-004) が提供する
// strong定義がリンク時に優先される。未実装のサブシステムは module_hooks.c の
// weakデフォルト(no-op)がリンクされる。

void switches_init(void);
void switches_task(void);

void adc_init(void);
void adc_task(void);

void backlight_init(void);
void backlight_task(void);

void i2c_slave_init(void);
void i2c_slave_task(void);

#endif // SWITCHER_MODULE_MODULE_HOOKS_H
