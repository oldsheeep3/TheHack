// 実機ブリングアップ用の診断モード (WIRING_TEST ビルドのみ, 通常ビルドには一切含まれない)。
//
// I2Cが応答しないときの切り分け用。I2Cバスの2本(SDA=PC1 / SCL=PC2)を「信号線」ではなく
// 「1bitの出力チャネル」として使い、モジュール内部の状態をマスター側へ伝える。
// マスター(pico2w-controller)を -DENABLE_I2C_DIAG=ON でビルドしておくと、全バスのSDA/SCLの
// 実測レベルがHIDレポートに出るため、モジュールがどちらの線をLowにしたかを読み取れる。
//
// ビルド:
//   make WIRING_TEST=1 switcher_module.bin   (配線チェック: 既定)
//   make WIRING_TEST=2 switcher_module.bin   (I2Cペリフェラルのレジスタ読み戻し)

#ifdef WIRING_TEST

#include "ch32fun.h"

#include "i2c_slave.h"
#include "module_config.h"
#include "module_index.h"

#define WIRING_TEST_PHASE_MS 2000

static void release_both(void) {
    funPinMode(MODULE_I2C_SDA_PIN, GPIO_CFGLR_IN_FLOAT);
    funPinMode(MODULE_I2C_SCL_PIN, GPIO_CFGLR_IN_FLOAT);
}

static void drive_low(uint8_t pin) {
    funPinMode(pin, GPIO_CFGLR_OUT_10Mhz_OD);
    funDigitalWrite(pin, FUN_LOW);
}

#if WIRING_TEST == 4

// SDA と SCL が途中で入れ替わっていないかを判別する。WIRING_TEST=1 は両フェーズとも
// 同じ長さだったため、交差していても観測結果が同一で区別できなかった。
// ここでは長さを変えて非対称にする:
//   SDA(PC1) を **1秒** Low → 開放2秒 → SCL(PC2) を **5秒** Low → 開放2秒
// マスター側で「短くLowになる線 = モジュールのSDA」「長くLowになる線 = モジュールのSCL」。
// 短い方がマスターのSCL_nに現れたら、どこかで交差している。
void wiring_test_run(void) {
    for (;;) {
        release_both();
        Delay_Ms(2000);

        drive_low(MODULE_I2C_SDA_PIN); // 短い = SDA
        Delay_Ms(1000);

        release_both();
        Delay_Ms(2000);

        drive_low(MODULE_I2C_SCL_PIN); // 長い = SCL
        Delay_Ms(5000);
    }
}

#elif WIRING_TEST == 3

// I2Cペリフェラルの出力が本当に PC1/PC2 のパッドへ届いているかを見る。
// ピンをAF_ODにしてI2C1を有効化したうえで、**マスターとしてSTART条件を生成**する。
// START生成はSDAをLowへ引く動作なので、パッドへ結線されていればマスター(Pico)側の
// 診断でそのバスのSDAがLowに張り付いて見える。
//
//   マスター側で SDA が Low になる → ペリフェラル出力はパッドへ届いている
//                                     (=AF結線はOK。原因はスレーブ動作固有)
//   両方Highのまま                  → **ペリフェラルがパッドを駆動できていない**
//                                     (=AF結線/ピン設定が効いていない。これが根本原因)
void wiring_test_run(void) {
    i2c_slave_init(); // ピンをAF_ODにし、I2C1にクロック供給+PE
    I2C1->CTLR1 |= (uint16_t)I2C_CTLR1_START;
    for (;;) {
        // START生成後はそのまま放置し、ライン状態をマスター側から観測させる。
    }
}

#elif WIRING_TEST == 2

// I2Cスレーブを通常どおり初期化し、**レジスタが実際に効いているか**を読み戻して報告する。
// APB1のペリフェラルクロックが入っていなければ書き込みは無視され、読み戻しは0になる。
//
// 報告の読み方 (マスター側の診断で、そのバスのSDA/SCLレベルを見る):
//   フェーズA (2秒): 両方Highのまま        … 区切り
//   フェーズB (2秒): SDAがLow → PE(ペリフェラル有効)ビットが立っている
//                    SCLがLow → OADDR1(スレーブアドレス)が期待値どおり書けている
//                    両方Low   → どちらもOK (=設定は効いている。原因はさらに別)
//                    両方High  → **レジスタが1つも効いていない**(クロック未供給等)
void wiring_test_run(void) {
    i2c_slave_init();

    const bool pe_ok = (I2C1->CTLR1 & I2C_CTLR1_PE) != 0;
    const uint16_t expected_oaddr = (uint16_t)(i2c_slave_address_for_module(get_module_index()) << 1);
    const bool addr_ok = (I2C1->OADDR1 & 0x00FEu) == (expected_oaddr & 0x00FEu);

    // 読み戻しは済んだので、ペリフェラルを止めてピンを素のGPIOとして使う。
    I2C1->CTLR1 &= ~(uint16_t)I2C_CTLR1_PE;

    for (;;) {
        release_both();
        Delay_Ms(WIRING_TEST_PHASE_MS);

        if (pe_ok) {
            drive_low(MODULE_I2C_SDA_PIN);
        }
        if (addr_ok) {
            drive_low(MODULE_I2C_SCL_PIN);
        }
        Delay_Ms(WIRING_TEST_PHASE_MS);
    }
}

#else

// 配線チェック: I2Cペリフェラルを一切使わず、SDA/SCLを順番にLowへ引くだけ。
//   バス n の SDA と SCL が交互にLowになる → 配線正常
//   どの線も落ちない                       → 未接続 (断線/半田不良)
void wiring_test_run(void) {
    for (;;) {
        release_both();
        Delay_Ms(WIRING_TEST_PHASE_MS);

        drive_low(MODULE_I2C_SDA_PIN);
        Delay_Ms(WIRING_TEST_PHASE_MS);

        release_both();
        Delay_Ms(WIRING_TEST_PHASE_MS);

        drive_low(MODULE_I2C_SCL_PIN);
        Delay_Ms(WIRING_TEST_PHASE_MS);
    }
}

#endif // WIRING_TEST == 2

#endif // WIRING_TEST
