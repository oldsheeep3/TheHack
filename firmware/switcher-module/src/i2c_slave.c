// i2c_slave.h のI/O実装。CH32V003 I2C1をスレーブモードで初期化し、レジスタポインタ
// 確定・1バイト授受・受信バッファ格納のみをISR(I2C1_EV_IRQHandler)で行う(親仕様書§4)。
// レジスタ領域の判定・内容生成はGPIO非依存の i2c_regs.c に委譲する。
//
// I2C1のSDA/SCLはCH32V003のシリコン固定ピン(PC1/PC2)であり、SWマトリクス/VR/バック
// ライトのGPIO(module_config.hの暫定プレースホルダ)とは異なりPCB配線選択の余地が
// ないため、ここで直接指定する。

#include "i2c_slave.h"

#include "ch32fun.h"

#include "adc.h"
#include "backlight.h"
#include "i2c_regs.h"
#include "module_config.h"
#include "module_index.h"
#include "switches.h"

#define I2C_SLAVE_SDA_PIN PC1
#define I2C_SLAVE_SCL_PIN PC2

// I2Cロジッククロック(バスクロックより高く設定する必要がある。ch32v003fun例に準拠)。
#define I2C_SLAVE_LOGIC_CLOCK_HZ 2000000
// I2Cバスクロック(親仕様書§4.5: 100k〜400kHz、Fast modeの上限を採用)。
#define I2C_SLAVE_BUS_CLOCK_HZ 400000

// 現在のトランザクションで確定したレジスタアドレスとその領域。ISR内でのみ書き換える。
static volatile uint8_t current_reg;
static volatile bool reg_known;
static volatile i2c_regs_region_t current_region;
// トランザクション先頭からの授受済みバイト数(reg_known確定後のデータバイトのみを数える)。
static volatile uint8_t byte_index;

// STATE/INFO の読み出し用スナップショット。STATEはi2c_slave_task()が毎ループ更新し、
// INFOは起動時に一度だけ生成する(不変)。
static volatile uint8_t state_snapshot[MODULE_REG_STATE_LEN];
static uint8_t info_snapshot[MODULE_REG_INFO_LEN];

// BACKLIGHT書込み用の受信バッファ。current_reg基準のオフセット(0..11)に格納する。
static volatile uint8_t backlight_rx_buf[MODULE_REG_BACKLIGHT_LEN];
static volatile uint8_t backlight_rx_start_offset;
// メインループへの受領完了通知フラグ(ISR->i2c_slave_task間の安全な引き渡し)。
static volatile bool backlight_pending;

void I2C1_EV_IRQHandler(void) __attribute__((interrupt));
void I2C1_ER_IRQHandler(void) __attribute__((interrupt));

void i2c_slave_init(void) {
    reg_known = false;
    current_reg = 0;
    current_region = I2C_REGS_REGION_INVALID;
    byte_index = 0;
    backlight_rx_start_offset = 0;
    backlight_pending = false;
    for (int i = 0; i < MODULE_REG_STATE_LEN; i++) {
        state_snapshot[i] = 0;
    }
    i2c_regs_build_info(info_snapshot);

    funPinMode(I2C_SLAVE_SDA_PIN, GPIO_CFGLR_OUT_10Mhz_AF_OD);
    funPinMode(I2C_SLAVE_SCL_PIN, GPIO_CFGLR_OUT_10Mhz_AF_OD);

    RCC->APB1PCENR |= RCC_APB1Periph_I2C1;
    RCC->APB1PRSTR |= RCC_APB1Periph_I2C1;
    RCC->APB1PRSTR &= ~RCC_APB1Periph_I2C1;

    I2C1->CTLR1 |= I2C_CTLR1_SWRST;
    I2C1->CTLR1 &= ~I2C_CTLR1_SWRST;

    I2C1->CTLR2 |= (FUNCONF_SYSTEM_CORE_CLOCK / I2C_SLAVE_LOGIC_CLOCK_HZ) & I2C_CTLR2_FREQ;
    I2C1->CTLR2 |= I2C_CTLR2_ITBUFEN | I2C_CTLR2_ITEVTEN | I2C_CTLR2_ITERREN;

    NVIC_EnableIRQ(I2C1_EV_IRQn);
    NVIC_SetPriority(I2C1_EV_IRQn, 2 << 4);
    NVIC_EnableIRQ(I2C1_ER_IRQn);
    NVIC_SetPriority(I2C1_ER_IRQn, 2 << 4);

    I2C1->CKCFGR = ((FUNCONF_SYSTEM_CORE_CLOCK / (3 * I2C_SLAVE_BUS_CLOCK_HZ)) & I2C_CKCFGR_CCR) | I2C_CKCFGR_FS;

    uint8_t address = i2c_slave_address_for_module(get_module_index());
    I2C1->OADDR1 = (uint16_t)(address << 1);
    I2C1->OADDR2 = 0;

    I2C1->CTLR1 |= I2C_CTLR1_PE;
    I2C1->CTLR1 |= I2C_CTLR1_ACK;
}

void i2c_slave_task(void) {
    uint8_t new_state[MODULE_REG_STATE_LEN];
    i2c_regs_build_state(switches_get_state(), adc_get_vr(VR_SRC1_INDEX), adc_get_vr(VR_SRC2_INDEX), new_state);

    __disable_irq();
    for (int i = 0; i < MODULE_REG_STATE_LEN; i++) {
        state_snapshot[i] = new_state[i];
    }
    __enable_irq();

    if (backlight_pending) {
        uint8_t rgb[MODULE_REG_BACKLIGHT_LEN];
        __disable_irq();
        for (int i = 0; i < MODULE_REG_BACKLIGHT_LEN; i++) {
            rgb[i] = backlight_rx_buf[i];
        }
        backlight_pending = false;
        __enable_irq();
        backlight_set_rgb(rgb);
    }
}

void I2C1_EV_IRQHandler(void) {
    uint16_t star1 = I2C1->STAR1;
    (void)I2C1->STAR2; // ADDR/STOPFのクリア手順上、STAR2の読み出しを行う。

    if (star1 & I2C_STAR1_ADDR) { // Start (または repeated start)
        reg_known = false;
        byte_index = 0;
    }

    if (star1 & I2C_STAR1_RXNE) { // ホストからの書込みバイトを受信
        uint8_t data = I2C1->DATAR;
        if (!reg_known) {
            current_reg = data;
            current_region = i2c_regs_decode_region(current_reg);
            backlight_rx_start_offset = (uint8_t)(current_reg - MODULE_REG_BACKLIGHT);
            byte_index = 0;
            reg_known = true;
        } else if (current_region == I2C_REGS_REGION_BACKLIGHT) {
            uint8_t offset = (uint8_t)(backlight_rx_start_offset + byte_index);
            if (offset < MODULE_REG_BACKLIGHT_LEN) {
                backlight_rx_buf[offset] = data;
            }
            byte_index++;
        } else {
            // STATE/INFO/不正レジスタへの書込みは無視する(読取専用/未定義, 親仕様書§4.5)。
            byte_index++;
        }
    }

    if (star1 & I2C_STAR1_TXE) { // ホストへの読み出しバイトを送出
        uint8_t out = 0x00;
        if (current_region == I2C_REGS_REGION_STATE && byte_index < MODULE_REG_STATE_LEN) {
            out = state_snapshot[byte_index];
        } else if (current_region == I2C_REGS_REGION_INFO && byte_index < MODULE_REG_INFO_LEN) {
            out = info_snapshot[byte_index];
        }
        I2C1->DATAR = out;
        byte_index++;
    }

    if (star1 & I2C_STAR1_STOPF) {
        I2C1->CTLR1 &= ~(I2C_CTLR1_STOP);
        if (current_region == I2C_REGS_REGION_BACKLIGHT &&
            i2c_regs_backlight_write_is_complete(backlight_rx_start_offset, byte_index)) {
            backlight_pending = true;
        }
        reg_known = false;
    }
}

void I2C1_ER_IRQHandler(void) {
    uint16_t star1 = I2C1->STAR1;

    if (star1 & I2C_STAR1_BERR) {
        I2C1->STAR1 &= ~(I2C_STAR1_BERR);
    }
    if (star1 & I2C_STAR1_ARLO) {
        I2C1->STAR1 &= ~(I2C_STAR1_ARLO);
    }
    if (star1 & I2C_STAR1_AF) {
        I2C1->STAR1 &= ~(I2C_STAR1_AF);
    }
}
