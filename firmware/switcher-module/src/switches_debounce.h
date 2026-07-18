#ifndef SWITCHER_MODULE_SWITCHES_DEBOUNCE_H
#define SWITCHER_MODULE_SWITCHES_DEBOUNCE_H

#include <stdint.h>

#include "module_config.h"

// GPIO非依存のSWマトリクス・デバウンス状態機械。Pico SDK/ch32v003funに依存しないため
// ホストのgccでそのままビルド・テストできる (test/test_switches.c 参照)。
//
// 4SW (b0=PGM1×SRC1, b1=PGM1×SRC2, b2=PGM2×SRC1, b3=PGM2×SRC2, module_config.h の
// SW_BIT_INDEX() 準拠) をそれぞれ独立にデバウンスし、確定した安定状態(レベル)を4bitで
// 保持する。イベントではなくレベルを保持するため、押しっぱなし中に同じ生値が来ても
// 確定状態は変化せず(非連打)、走査のたびに冪等に同じ値を返す。

// 連続一致サンプル数のしきい値 (マジックナンバー回避)。走査周期 x この値が
// 実効チャタリング除去時間になる。
#define SWITCHES_DEBOUNCE_STABLE_SAMPLES 5

typedef struct {
    uint8_t stable_state;                  // 確定済み4bit安定状態 (b0..b3, 上位4bitは常に0)
    uint8_t candidate_state;               // 各SWの直近候補値 (ビット位置はstable_stateと同じ)
    uint8_t consecutive_count[SW_COUNT];   // 各SWビットについて候補値と生値が連続一致した回数
} switches_debouncer_t;

void switches_debouncer_init(switches_debouncer_t *db);

// 4bit生サンプル (b0..b3, module_config.h の SW_BIT_INDEX() 準拠) を1回投入する。
// 各SWビットごとに独立して SWITCHES_DEBOUNCE_STABLE_SAMPLES 回連続で同じ生値が
// 観測された時点でそのビットの安定状態を更新する。更新後の確定4bit状態を返す。
uint8_t switches_debouncer_sample(switches_debouncer_t *db, uint8_t raw_state);

// 確定4bit状態を I2C `0x00 STATE` レジスタ[0] の値へ変換する (b0..b3、上位4bitは0埋め)。
uint8_t switches_state_to_register(uint8_t stable_state);

#endif // SWITCHER_MODULE_SWITCHES_DEBOUNCE_H
