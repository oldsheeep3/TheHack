// HDMI DDCライン(SCL/SDA)のパッシブスニッフィング (GPIO依存)。
//
// ハードウェアI2Cペリフェラルはスレーブモードでも「自アドレス宛の通信にのみACKし
// 応答する」動作しかできず、ATEM↔カメラ間(我々のアドレス宛ではない)通信の生バイト列を
// 傍受することができない。そのため、DDC_I2C_SDA_PIN/DDC_I2C_SCL_PIN はハードウェア
// I2Cペリフェラルにマッピングせず、プレーンGPIO入力+エッジ割込みでSCL/SDAの2線を
// 直接観測し、I2Cバスプロトコルをソフトウェアで再構成する(バスへは一切書き込まない=
// パッシブ、ATEM↔カメラ間通信を阻害しない)。
//
// I2Cバスの基本ルール:
//   - START条件: SCL=High の間に SDA が High→Low に変化。
//   - STOP条件 : SCL=High の間に SDA が Low→High に変化。
//   - データビット: SCLの立ち上がりエッジでSDAの値をサンプルする
//                   (マスタはSCL=Lowの間だけSDAを変化させるため、SCL=Highの間は
//                   安定していることが保証される)。1バイト=8データビット+ACK/NACK
//                   の計9クロック。
//
// 割込みハンドラ(ISR)ではビット/バイトの再構成のみを行い、1トランザクション
// (START〜STOP)が完了した時点でリングバッファ(frame_queue)に積む。デコード
// (ddc_tally_decode)や状態比較・LED更新・USB通知キュー投入はメインループ側
// (ddc_tally_task)で行い、ISRは短く保つ。frame_queue はISR(生産者)とメインループ
// (消費者)の単一生産者/単一消費者キューで、消費者側の読み出しのみ
// save_and_disable_interrupts/restore_interruptsで保護する
// (生産者=ISRはメインループに横取りされないため、生産側に追加の保護は不要)。

#include "ddc_tally.h"

#include "hardware/gpio.h"
#include "hardware/sync.h"
#include "pico/stdlib.h"

#include "config.h"

// ---- I2Cバス捕捉: 1トランザクション分のフレームバッファ ----

#define DDC_FRAME_MAX_BYTES 16 // アドレスバイト含む。タリーコマンドは数バイト程度と想定
#define DDC_FRAME_QUEUE_SIZE 8

typedef struct {
    uint8_t bytes[DDC_FRAME_MAX_BYTES];
    uint8_t len;
} ddc_raw_frame_t;

static ddc_raw_frame_t frame_queue[DDC_FRAME_QUEUE_SIZE];
static volatile uint8_t frame_queue_head;
static volatile uint8_t frame_queue_tail;
static volatile uint8_t frame_queue_count;

// ISR内でのみ触る捕捉中の作業状態 (割込みは再入しないため保護不要)。
static volatile bool capturing;
static uint8_t capture_bytes[DDC_FRAME_MAX_BYTES];
static uint8_t capture_len;
static uint16_t current_bits; // 蓄積中のビット (9ビットで1バイト+ACK)
static uint8_t bit_count;

static void frame_queue_push_from_isr(const uint8_t *bytes, uint8_t len) {
    if (frame_queue_count >= DDC_FRAME_QUEUE_SIZE) {
        // 溢れた場合は最古のフレームを破棄して直近を優先する
        // (メインループの遅延でISR側がブロックしないようにするため)。
        frame_queue_head = (uint8_t)((frame_queue_head + 1) % DDC_FRAME_QUEUE_SIZE);
        frame_queue_count--;
    }
    ddc_raw_frame_t *slot = &frame_queue[frame_queue_tail];
    uint8_t copy_len = len > DDC_FRAME_MAX_BYTES ? DDC_FRAME_MAX_BYTES : len;
    for (uint8_t i = 0; i < copy_len; i++) {
        slot->bytes[i] = bytes[i];
    }
    slot->len = copy_len;
    frame_queue_tail = (uint8_t)((frame_queue_tail + 1) % DDC_FRAME_QUEUE_SIZE);
    frame_queue_count++;
}

static bool frame_queue_pop(ddc_raw_frame_t *out_frame) {
    uint32_t saved_irq = save_and_disable_interrupts();
    bool has_item = frame_queue_count > 0;
    if (has_item) {
        *out_frame = frame_queue[frame_queue_head];
        frame_queue_head = (uint8_t)((frame_queue_head + 1) % DDC_FRAME_QUEUE_SIZE);
        frame_queue_count--;
    }
    restore_interrupts(saved_irq);
    return has_item;
}

static void ddc_gpio_irq_handler(uint gpio, uint32_t events) {
    bool scl_high = gpio_get(DDC_I2C_SCL_PIN);
    bool sda_now = gpio_get(DDC_I2C_SDA_PIN);

    if (gpio == DDC_I2C_SDA_PIN && scl_high) {
        // SCL=Highの間のSDA変化 = START/STOP条件 (データビットではない)。
        if (events & GPIO_IRQ_EDGE_FALL) {
            // START (リピートSTART含む): 新しいトランザクションとして捕捉を開始。
            capturing = true;
            capture_len = 0;
            current_bits = 0;
            bit_count = 0;
        } else if (events & GPIO_IRQ_EDGE_RISE) {
            // STOP: 捕捉済みバイト列をキューへ渡して確定。
            if (capturing && capture_len > 0) {
                frame_queue_push_from_isr(capture_bytes, capture_len);
            }
            capturing = false;
        }
        return;
    }

    if (gpio == DDC_I2C_SCL_PIN && (events & GPIO_IRQ_EDGE_RISE)) {
        if (!capturing) {
            return;
        }
        current_bits = (uint16_t)((current_bits << 1) | (sda_now ? 1u : 0u));
        bit_count++;
        if (bit_count == 9) { // 8データビット + ACK/NACKビット
            if (capture_len < DDC_FRAME_MAX_BYTES) {
                capture_bytes[capture_len++] = (uint8_t)(current_bits >> 1); // ACKビットを除く
            }
            current_bits = 0;
            bit_count = 0;
        }
    }
}

// ---- タリーLED ----

static void tally_led_apply(tally_state_t state) {
    gpio_put(TALLY_LED_RED_PIN, state == TALLY_STATE_PROGRAM);
    gpio_put(TALLY_LED_GREEN_PIN, state == TALLY_STATE_PREVIEW);
}

// ---- 状態変化イベントキュー (buttons.c の event_queue と同じ方式) ----

#define TALLY_EVENT_QUEUE_SIZE 8
static tally_state_event_t tally_event_queue[TALLY_EVENT_QUEUE_SIZE];
static uint8_t tally_event_queue_head;
static uint8_t tally_event_queue_tail;
static uint8_t tally_event_queue_count;

static void tally_event_queue_push(tally_state_event_t event) {
    if (tally_event_queue_count >= TALLY_EVENT_QUEUE_SIZE) {
        tally_event_queue_head = (uint8_t)((tally_event_queue_head + 1) % TALLY_EVENT_QUEUE_SIZE);
        tally_event_queue_count--;
    }
    tally_event_queue[tally_event_queue_tail] = event;
    tally_event_queue_tail = (uint8_t)((tally_event_queue_tail + 1) % TALLY_EVENT_QUEUE_SIZE);
    tally_event_queue_count++;
}

bool ddc_tally_pop_event(tally_state_event_t *out_event) {
    if (tally_event_queue_count == 0) {
        return false;
    }
    *out_event = tally_event_queue[tally_event_queue_head];
    tally_event_queue_head = (uint8_t)((tally_event_queue_head + 1) % TALLY_EVENT_QUEUE_SIZE);
    tally_event_queue_count--;
    return true;
}

static tally_state_t current_state = TALLY_STATE_OFF;

void ddc_tally_init(void) {
    gpio_init(TALLY_LED_RED_PIN);
    gpio_set_dir(TALLY_LED_RED_PIN, GPIO_OUT);
    gpio_init(TALLY_LED_GREEN_PIN);
    gpio_set_dir(TALLY_LED_GREEN_PIN, GPIO_OUT);
    tally_led_apply(TALLY_STATE_OFF);

    // SCL/SDAはバス上の他デバイス(ATEM/カメラ)が既にプルアップしている前提のため、
    // Pico側では入力のみとし追加のプルは行わない(バスに影響を与えないため)。
    gpio_init(DDC_I2C_SDA_PIN);
    gpio_set_dir(DDC_I2C_SDA_PIN, GPIO_IN);
    gpio_init(DDC_I2C_SCL_PIN);
    gpio_set_dir(DDC_I2C_SCL_PIN, GPIO_IN);

    frame_queue_head = 0;
    frame_queue_tail = 0;
    frame_queue_count = 0;
    capturing = false;
    capture_len = 0;

    gpio_set_irq_enabled_with_callback(DDC_I2C_SDA_PIN, GPIO_IRQ_EDGE_RISE | GPIO_IRQ_EDGE_FALL, true,
                                        &ddc_gpio_irq_handler);
    gpio_set_irq_enabled(DDC_I2C_SCL_PIN, GPIO_IRQ_EDGE_RISE, true);
}

void ddc_tally_task(void) {
    ddc_raw_frame_t frame;
    while (frame_queue_pop(&frame)) {
        if (frame.len < 1) {
            continue;
        }
        // 1バイト目 = 7bitアドレス + R/Wビット。DDC_TALLY_I2C_ADDR宛以外
        // (EDID読み出し(0x50)等)のノイズをここで除外する。
        uint8_t addr7 = (uint8_t)(frame.bytes[0] >> 1);
        if (addr7 != DDC_TALLY_I2C_ADDR) {
            continue;
        }

        tally_decode_result_t result = ddc_tally_decode(frame.bytes + 1, (size_t)(frame.len - 1), TALLY_CAMERA_ID);
        if (!result.matched) {
            continue;
        }

        if (result.state != current_state) {
            current_state = result.state;
            tally_led_apply(current_state);
            tally_state_event_t event = {
                .state = current_state,
                .timestamp_ms = time_us_64() / 1000,
            };
            tally_event_queue_push(event);
        }
    }
}
