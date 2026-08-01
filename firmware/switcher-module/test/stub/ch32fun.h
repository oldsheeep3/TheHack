#ifndef SWITCHER_MODULE_TEST_STUB_CH32FUN_H
#define SWITCHER_MODULE_TEST_STUB_CH32FUN_H

// ch32v003fun (ch32fun.h) のホストテスト用スタブ。
//
// switches.c / adc.c は「どのピン/チャネルを、どのモードで、どう解釈して読むか」という
// 基板配線に直結したロジックを持つが、これまでは ch32fun.h に依存するためホストテストの
// 対象外だった。テスト側の -I でこのスタブを本物より先に見つけさせることで、src/*.c を
// 一切変更せずにホスト上でリンクし、ピン→ビット位置の割当やアクティブLow解釈を検証する
// (test/test_switches_io.c, test/test_adc_io.c)。
//
// 提供するのは検証に必要な関数/定数だけで、レジスタ定義やタイミングは模倣しない。
// ペリフェラルを直接叩くファイル (backlight.c のビットバン, i2c_slave.c のI2C1 ISR) は
// このスタブの対象外 (従来どおり純粋部の sk6812_frame.c / i2c_regs.c のみをテストする)。

#include <stdint.h>

#define FUN_LOW 0
#define FUN_HIGH 1

// funPinMode() に渡すモード値。実物 (ch32v003hw.h の GPIO_CFGLR_*) と同じ値にしておき、
// テスト側は「入力プルアップに設定されたか」を確認できるようにする。
#define GPIO_CFGLR_IN_ANALOG 0x0
#define GPIO_CFGLR_IN_FLOAT 0x4
#define GPIO_CFGLR_IN_PUPD 0x8
#define GPIO_CFGLR_OUT_10Mhz_PP 0x1
#define GPIO_CFGLR_OUT_10Mhz_OD 0x5
#define GPIO_CFGLR_OUT_10Mhz_AF_PP 0x9
#define GPIO_CFGLR_OUT_10Mhz_AF_OD 0xD

// ピン番号エンコード (ポート番号<<4 | ピン番号)。module_config.h の MODULE_PIN() と同形式。
#define PA1 0x01
#define PA2 0x02
#define PC0 0x20
#define PC1 0x21
#define PC2 0x22
#define PC4 0x24
#define PD0 0x30
#define PD2 0x32
#define PD3 0x33
#define PD4 0x34
#define PD5 0x35
#define PD6 0x36

#define STUB_PIN_COUNT 0x40
#define STUB_ADC_CHANNEL_COUNT 8

// スタブが記録するピン/ADCの状態 (テスト本体から直接読み書きする)。
typedef struct {
    uint8_t mode[STUB_PIN_COUNT];         // 最後に funPinMode() で設定されたモード
    uint8_t mode_set[STUB_PIN_COUNT];     // funPinMode() が呼ばれたか
    uint8_t level[STUB_PIN_COUNT];        // funDigitalRead() が返す値 / funDigitalWrite() の書込値
    uint8_t write_count[STUB_PIN_COUNT];  // funDigitalWrite() の呼び出し回数
    uint16_t adc[STUB_ADC_CHANNEL_COUNT]; // funAnalogRead() が返す値
    uint8_t adc_read_count[STUB_ADC_CHANNEL_COUNT];
    uint8_t adc_init_count;
    uint32_t delay_us_total;
} stub_gpio_t;

extern stub_gpio_t stub_gpio;

void stub_gpio_reset(void);

void funPinMode(uint8_t pin, uint8_t mode);
void funDigitalWrite(uint8_t pin, uint8_t value);
int funDigitalRead(uint8_t pin);
void funAnalogInit(void);
int funAnalogRead(int channel);
void Delay_Us(uint32_t us);

#endif // SWITCHER_MODULE_TEST_STUB_CH32FUN_H
