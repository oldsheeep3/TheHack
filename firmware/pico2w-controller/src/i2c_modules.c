// モジュール群のI2Cポーリング(GPIO/ハードウェアI2C依存)。
// 集約結果のバイト列パッキングはPico SDK非依存のstate_agg.cに、バス定義とピン→I2C
// コントローラの対応は同じくPico SDK非依存のmodule_bus.cに分離してあり、ここでは
// 「バスを選び、STATE(0x00)を読み、共有状態配列へ格納する」I/O部分のみを担う。
//
// 実基板はモジュール1台につき専用のI2Cバスを1本持つ(MODULE_BUS_COUNT本)。モジュール側の
// I2Cアドレスは全台 MODULE_I2C_ADDR(0x30) 固定で、モジュール番号 = バス(スロット)番号。
//
// RP2040/RP2350のI2Cコントローラは2基(i2c0/i2c1)しかないため、同じコントローラに
// 割り当たる複数のバス(i2c0: バス1,3,5 / i2c1: バス2,4)はピン機能を付け替えて時分割で使う。
// 非選択バスのピンはGPIO_FUNC_NULL + 内蔵プルアップでHigh(アイドル)に保つため、
// そのバスに繋がったモジュールから見ればバスが静止しているだけで、状態は保持される。

#include "i2c_modules.h"

#include <string.h>

#include "hardware/gpio.h"
#include "hardware/i2c.h"

#include "config.h"
#include "module_bus.h"

// i2c_modules_poll()/i2c_modules_get_state() はメインループの単一コンテキストからのみ
// 呼び出される前提(割込み/別コアからのアクセスなし)のため、排他制御は行わない。
static module_state_array_t shared_states;

// 各I2Cコントローラが現在どのバスへ割り当てられているか(未割当は BUS_NONE)。
#define BUS_NONE 0xFF
#define I2C_CONTROLLER_COUNT 2
static uint8_t active_bus[I2C_CONTROLLER_COUNT];

// i2c_modules_poll() が次に見るバス(ラウンドロビン)。
static uint8_t next_bus;

#ifdef ENABLE_I2C_DIAG
// 実機切り分け用の診断チャネル(デバッグビルド専用, -DENABLE_I2C_DIAG=ON)。
// バスの実測レベルとI2C APIの戻り値を、物理的に存在しない余りスロット
// (MODULE_BUS_COUNT..MAX_MODULES-1) へ載せてHID入力レポート0x01で吸い上げる。
// 本番ビルドには一切含まれない。
#define DIAG_SLOT_LINES (MAX_MODULES - 3) // SDA/SCLの実測レベルとスキップ状況
#define DIAG_SLOT_RESULT (MAX_MODULES - 2) // 直近のwrite/readの戻り値
static uint8_t diag_sda_bits, diag_scl_bits, diag_skip_bits;
static int8_t diag_last_write, diag_last_read;

static void diag_record_lines(uint8_t bus, bool sda, bool scl, bool skipped) {
    uint8_t mask = (uint8_t)(1u << bus);
    diag_sda_bits = (uint8_t)((diag_sda_bits & ~mask) | (sda ? mask : 0));
    diag_scl_bits = (uint8_t)((diag_scl_bits & ~mask) | (scl ? mask : 0));
    diag_skip_bits = (uint8_t)((diag_skip_bits & ~mask) | (skipped ? mask : 0));
}

// 全バス・全7bitアドレス(0x08..0x77)を総当たりし、ACKを返す相手を探す。1回の poll で
// 1アドレスだけ試すのでメインループはブロックしない。「0x30に応答が無い」のが
// アドレス違いなのかバス違いなのか配線違いなのかを、実機だけで切り分けるための診断。
#define DIAG_SLOT_SCAN (MAX_MODULES - 1)
#define DIAG_SCAN_FIRST 0x08
#define DIAG_SCAN_LAST 0x77
static uint8_t diag_scan_addr = DIAG_SCAN_FIRST;
static uint8_t diag_found_bus = 0xFF;
static uint8_t diag_found_addr = 0x00;

static void diag_scan_step(uint8_t bus, i2c_inst_t *i2c) {
    uint8_t probe = MODULE_REG_STATE;
    if (i2c_write_timeout_us(i2c, diag_scan_addr, &probe, 1, false, MODULE_I2C_TIMEOUT_US) == 1) {
        diag_found_bus = bus;
        diag_found_addr = diag_scan_addr;
    }
    if (bus == MODULE_BUS_COUNT - 1) { // 全バスを一巡したら次のアドレスへ
        diag_scan_addr = (diag_scan_addr >= DIAG_SCAN_LAST) ? DIAG_SCAN_FIRST : (uint8_t)(diag_scan_addr + 1);
    }
}

// ---- ソフトウェアI2C(ビットバン)によるアドレスプローブ ----
// ハードウェアI2Cのピン機能割当(GPIO_FUNC_I2C)もコントローラ設定も一切経由せず、
// SIOで直接SDA/SCLを叩いてアドレスフレームを送り、ACKビットを読む。
// 「マスターのI2Cペリフェラルがパッドを駆動できていない」のか「スレーブが応答していない」
// のかを分離するための最後の切り分け。クロックストレッチには対応しない(プローブ専用)。
#define SWI2C_HALF_US 25 // 約20kHz

static void swi2c_release(uint pin) {
    gpio_set_dir(pin, GPIO_IN); // 開放 = プルアップでHigh
}

static void swi2c_low(uint pin) {
    gpio_put(pin, 0);
    gpio_set_dir(pin, GPIO_OUT);
}

static bool swi2c_probe(uint8_t bus_index, uint8_t addr) {
    const module_bus_t *b = &MODULE_BUSES[bus_index];
    gpio_set_function(b->sda_pin, GPIO_FUNC_SIO);
    gpio_set_function(b->scl_pin, GPIO_FUNC_SIO);
    swi2c_release(b->sda_pin);
    swi2c_release(b->scl_pin);
    busy_wait_us(SWI2C_HALF_US);

    swi2c_low(b->sda_pin); // START
    busy_wait_us(SWI2C_HALF_US);
    swi2c_low(b->scl_pin);
    busy_wait_us(SWI2C_HALF_US);

    uint8_t frame = (uint8_t)(addr << 1); // R/W = 0 (write)
    for (int i = 7; i >= 0; i--) {
        if ((frame >> i) & 1u) {
            swi2c_release(b->sda_pin);
        } else {
            swi2c_low(b->sda_pin);
        }
        busy_wait_us(SWI2C_HALF_US);
        swi2c_release(b->scl_pin);
        busy_wait_us(SWI2C_HALF_US * 2);
        swi2c_low(b->scl_pin);
        busy_wait_us(SWI2C_HALF_US);
    }

    swi2c_release(b->sda_pin); // ACKビットはスレーブが駆動する
    busy_wait_us(SWI2C_HALF_US);
    swi2c_release(b->scl_pin);
    busy_wait_us(SWI2C_HALF_US * 2);
    bool ack = !gpio_get(b->sda_pin);
    swi2c_low(b->scl_pin);
    busy_wait_us(SWI2C_HALF_US);

    swi2c_low(b->sda_pin); // STOP
    busy_wait_us(SWI2C_HALF_US);
    swi2c_release(b->scl_pin);
    busy_wait_us(SWI2C_HALF_US);
    swi2c_release(b->sda_pin);
    busy_wait_us(SWI2C_HALF_US * 2);

    gpio_set_function(b->sda_pin, GPIO_FUNC_I2C); // ハードウェアI2Cへ戻す
    gpio_set_function(b->scl_pin, GPIO_FUNC_I2C);
    return ack;
}

// バス0に対するソフトI2Cプローブの結果 (bit0: 0x30, bit1: 0x09)。
static uint8_t diag_swprobe;

static void diag_publish(void) {
    shared_states.modules[DIAG_SLOT_SCAN].present = true;
    shared_states.modules[DIAG_SLOT_SCAN].switches = diag_found_bus;
    shared_states.modules[DIAG_SLOT_SCAN].vr[0] = diag_found_addr;
    shared_states.modules[DIAG_SLOT_SCAN].vr[1] = diag_scan_addr;

    shared_states.modules[DIAG_SLOT_LINES].present = true;
    shared_states.modules[DIAG_SLOT_LINES].switches = diag_sda_bits;
    shared_states.modules[DIAG_SLOT_LINES].vr[0] = diag_scl_bits;
    shared_states.modules[DIAG_SLOT_LINES].vr[1] = diag_skip_bits;

    shared_states.modules[DIAG_SLOT_RESULT].present = true;
    shared_states.modules[DIAG_SLOT_RESULT].switches = diag_swprobe;
    shared_states.modules[DIAG_SLOT_RESULT].vr[0] = (uint8_t)diag_last_write;
    shared_states.modules[DIAG_SLOT_RESULT].vr[1] = (uint8_t)diag_last_read;
}
#endif

static i2c_inst_t *controller_of(uint8_t bus_index) {
    return module_bus_i2c_index(bus_index) == 0 ? i2c0 : i2c1;
}

// SDAホールド時間(SCL立下り後にSDAを保持するサイクル数, clk_sys基準)。
// Pico SDK の i2c_init() は規格最小の約300nsしか設定しないが、モジュール(CH32V003)側は
// これだとアドレスビットを取りこぼす。実機ではビットバン(ホールド25us)なら確実に応答し、
// ハードウェアI2Cだけが応答しない、という差がここに出ていた。余裕をとって約2usにする。
// (clk_sys 125MHz 前提の概算値。バスのHigh期間より短ければ通信自体には影響しない)
#define MODULE_I2C_SDA_HOLD_CYCLES 250

// i2c_init() + このプロジェクト固有の調整。初期化と障害復旧の両方から使う。
static void configure_controller(i2c_inst_t *i2c) {
    i2c_init(i2c, MODULE_I2C_BAUD);
    hw_write_masked(&i2c->hw->sda_hold, MODULE_I2C_SDA_HOLD_CYCLES, I2C_IC_SDA_HOLD_IC_SDA_TX_HOLD_BITS);
}

// bus_index のSDA/SCLを、そのバスが属するコントローラのピンとして有効化する。
// 同じコントローラを共有する別バスが有効な場合は、先にそちらをGPIO_FUNC_NULLへ戻す
// (同一機能を2組のパッドへ同時に割り当てると入力の取り込み元が一意にならないため)。
static void bus_select(uint8_t bus_index) {
    uint8_t controller = module_bus_i2c_index(bus_index);
    if (active_bus[controller] == bus_index) {
        return;
    }

    if (active_bus[controller] != BUS_NONE) {
        const module_bus_t *prev = &MODULE_BUSES[active_bus[controller]];
        gpio_set_function(prev->sda_pin, GPIO_FUNC_NULL);
        gpio_set_function(prev->scl_pin, GPIO_FUNC_NULL);
    }

    const module_bus_t *bus = &MODULE_BUSES[bus_index];
    gpio_set_function(bus->sda_pin, GPIO_FUNC_I2C);
    gpio_set_function(bus->scl_pin, GPIO_FUNC_I2C);
    active_bus[controller] = bus_index;
}

void i2c_modules_init(void) {
    for (int c = 0; c < I2C_CONTROLLER_COUNT; c++) {
        active_bus[c] = BUS_NONE;
    }
    next_bus = 0;
    configure_controller(i2c0);
    configure_controller(i2c1);

    // 全バスのピンを内蔵プルアップ付きのアイドル(非選択)状態にしておく。基板・モジュール
    // とも外付けプルアップを持たないため、このプルアップがバスのHighを保つ唯一の手段。
    for (uint8_t b = 0; b < MODULE_BUS_COUNT; b++) {
        gpio_set_function(MODULE_BUSES[b].sda_pin, GPIO_FUNC_NULL);
        gpio_set_function(MODULE_BUSES[b].scl_pin, GPIO_FUNC_NULL);
        gpio_pull_up(MODULE_BUSES[b].sda_pin);
        gpio_pull_up(MODULE_BUSES[b].scl_pin);
    }

    for (int i = 0; i < MAX_MODULES; i++) {
        shared_states.modules[i].present = false;
        shared_states.modules[i].switches = 0;
        shared_states.modules[i].vr[0] = 0;
        shared_states.modules[i].vr[1] = 0;
    }
}

#ifdef MODULE_I2C_SOFTWARE
// ---- ソフトウェアI2C(ビットバン)マスター ----
// RP2040/RP2350のハードウェアI2Cではモジュール(CH32V003)がアドレスにACKを返さないのに、
// SIOで直接叩くビットバンなら確実に応答する、という実機事象への対処。ハードウェアI2Cの
// 代わりにこちらを使うビルド (-DMODULE_I2C_SOFTWARE=ON)。
// クロックストレッチに対応する(モジュールはSK6812駆動中に割込みを止めるため必須)。
#define SWM_QUARTER_US 12 // 約20kHz
#define SWM_STRETCH_TIMEOUT_US 2000

static void swm_release(uint pin) {
    gpio_set_dir(pin, GPIO_IN);
}

static void swm_low(uint pin) {
    gpio_put(pin, 0);
    gpio_set_dir(pin, GPIO_OUT);
}

// SCLをHighにして、スレーブのクロックストレッチが解けるまで待つ。
static bool swm_scl_release_and_wait(uint scl) {
    swm_release(scl);
    absolute_time_t deadline = make_timeout_time_us(SWM_STRETCH_TIMEOUT_US);
    while (!gpio_get(scl)) {
        if (absolute_time_diff_us(get_absolute_time(), deadline) < 0) {
            return false;
        }
    }
    return true;
}

static bool swm_write_bit(uint sda, uint scl, bool bit_value) {
    if (bit_value) {
        swm_release(sda);
    } else {
        swm_low(sda);
    }
    busy_wait_us(SWM_QUARTER_US);
    if (!swm_scl_release_and_wait(scl)) {
        return false;
    }
    busy_wait_us(SWM_QUARTER_US * 2);
    swm_low(scl);
    busy_wait_us(SWM_QUARTER_US);
    return true;
}

static bool swm_read_bit(uint sda, uint scl, bool *out_bit) {
    swm_release(sda);
    busy_wait_us(SWM_QUARTER_US);
    if (!swm_scl_release_and_wait(scl)) {
        return false;
    }
    busy_wait_us(SWM_QUARTER_US);
    *out_bit = gpio_get(sda);
    busy_wait_us(SWM_QUARTER_US);
    swm_low(scl);
    busy_wait_us(SWM_QUARTER_US);
    return true;
}

static void swm_start(uint sda, uint scl) {
    swm_release(sda);
    swm_release(scl);
    busy_wait_us(SWM_QUARTER_US * 2);
    swm_low(sda);
    busy_wait_us(SWM_QUARTER_US * 2);
    swm_low(scl);
    busy_wait_us(SWM_QUARTER_US);
}

static void swm_stop(uint sda, uint scl) {
    swm_low(sda);
    busy_wait_us(SWM_QUARTER_US);
    swm_release(scl);
    busy_wait_us(SWM_QUARTER_US * 2);
    swm_release(sda);
    busy_wait_us(SWM_QUARTER_US * 2);
}

// 1バイト送出し、スレーブのACKを返す。
static bool swm_write_byte(uint sda, uint scl, uint8_t value, bool *out_ack) {
    for (int i = 7; i >= 0; i--) {
        if (!swm_write_bit(sda, scl, ((value >> i) & 1u) != 0)) {
            return false;
        }
    }
    bool nack = true;
    if (!swm_read_bit(sda, scl, &nack)) {
        return false;
    }
    *out_ack = !nack;
    return true;
}

static bool swm_read_byte(uint sda, uint scl, uint8_t *out_value, bool ack) {
    uint8_t v = 0;
    for (int i = 0; i < 8; i++) {
        bool bit_value = false;
        if (!swm_read_bit(sda, scl, &bit_value)) {
            return false;
        }
        v = (uint8_t)((v << 1) | (bit_value ? 1u : 0u));
    }
    if (!swm_write_bit(sda, scl, !ack)) { // ACK=0 / NACK=1
        return false;
    }
    *out_value = v;
    return true;
}

static void swm_pins_take(const module_bus_t *bus) {
    gpio_set_function(bus->sda_pin, GPIO_FUNC_SIO);
    gpio_set_function(bus->scl_pin, GPIO_FUNC_SIO);
    swm_release(bus->sda_pin);
    swm_release(bus->scl_pin);
}

// レジスタアドレスを書いてから、repeated startでlenバイト読み出す。
static bool swm_read_reg(uint8_t bus_index, uint8_t addr, uint8_t reg, uint8_t *dst, size_t len) {
    const module_bus_t *bus = &MODULE_BUSES[bus_index];
    uint sda = bus->sda_pin, scl = bus->scl_pin;
    swm_pins_take(bus);

    bool ok = false;
    bool ack = false;
    swm_start(sda, scl);
    if (swm_write_byte(sda, scl, (uint8_t)(addr << 1), &ack) && ack &&
        swm_write_byte(sda, scl, reg, &ack) && ack) {
        swm_start(sda, scl); // repeated start
        if (swm_write_byte(sda, scl, (uint8_t)((addr << 1) | 1u), &ack) && ack) {
            ok = true;
            for (size_t i = 0; i < len; i++) {
                if (!swm_read_byte(sda, scl, &dst[i], i + 1 < len)) {
                    ok = false;
                    break;
                }
            }
        }
    }
    swm_stop(sda, scl);
    return ok;
}

static bool swm_write_buf(uint8_t bus_index, uint8_t addr, const uint8_t *src, size_t len) {
    const module_bus_t *bus = &MODULE_BUSES[bus_index];
    uint sda = bus->sda_pin, scl = bus->scl_pin;
    swm_pins_take(bus);

    bool ok = false;
    bool ack = false;
    swm_start(sda, scl);
    if (swm_write_byte(sda, scl, (uint8_t)(addr << 1), &ack) && ack) {
        ok = true;
        for (size_t i = 0; i < len; i++) {
            if (!swm_write_byte(sda, scl, src[i], &ack) || !ack) {
                ok = false;
                break;
            }
        }
    }
    swm_stop(sda, scl);
    return ok;
}
#endif // MODULE_I2C_SOFTWARE

// タイムアウト/中断したトランザクションは、STOPを出せないままバスを掴んだ状態で終わって
// いる可能性がある(レジスタポインタ書込みは nostop=true のため特に)。その状態を次のバスへ
// 持ち越すと、同じコントローラを共有するバス(i2c0: 1,3,5 / i2c1: 2,4)まで巻き込むため、
// 失敗したらコントローラを初期化し直して切り離す(TX FIFOの積み残しもここで消える)。
static void recover_controller(uint8_t bus_index) {
    configure_controller(controller_of(bus_index));
}

// SDA/SCLが両方Highに戻っている(=バスがアイドル)かを、パッドの入力レベルで直接確認する。
// gpio_get() はピン機能がI2Cでもパッドの実レベルを返す。
//
// これはUSB列挙を壊さないための必須ガード: バスがLowに張り付いていると、I2Cコントローラは
// 転送を開始できずTX_EMPTYも上がらないため i2c_write_timeout_us() がタイムアウトで返る
// (=TX FIFOに積んだエントリが残る)。これを繰り返してFIFOが埋まると、SDKの読み出し側に
// ある唯一のタイムアウト無しループ (i2c.c の `while (!i2c_get_write_available(i2c))`) が
// 永久に抜けなくなり、メインループごと停止して tud_task() が回らなくなる。
// 転送を「始める前に」バスの健全性を見て、駄目ならそのバスを今回はスキップする。
static bool bus_is_idle(uint8_t bus_index) {
    const module_bus_t *bus = &MODULE_BUSES[bus_index];
    return gpio_get(bus->sda_pin) && gpio_get(bus->scl_pin);
}

// bus_index (= module_index) のSTATEレジスタを読み、out_state へ格納する。
// ACK無し/タイムアウト/バス異常の場合は false を返す(呼び出し側でそのモジュールをスキップする)。
static bool poll_module(uint8_t bus_index, module_state_t *out_state) {
    bus_select(bus_index);
    bool idle = bus_is_idle(bus_index);
#ifdef ENABLE_I2C_DIAG
    diag_record_lines(bus_index, gpio_get(MODULE_BUSES[bus_index].sda_pin),
                      gpio_get(MODULE_BUSES[bus_index].scl_pin), !idle);
#endif
    if (!idle) {
        return false; // バスがLowに張り付いている: 触らずにスキップする
    }

#ifdef MODULE_I2C_SOFTWARE
    uint8_t sw_buf[MODULE_REG_STATE_LEN];
    if (!swm_read_reg(bus_index, MODULE_I2C_ADDR, MODULE_REG_STATE, sw_buf, MODULE_REG_STATE_LEN)) {
        return false;
    }
    out_state->present = true;
    out_state->switches = (uint8_t)(sw_buf[0] & 0x0Fu);
    out_state->vr[0] = sw_buf[1];
    out_state->vr[1] = sw_buf[2];
    return true;
#else
    i2c_inst_t *i2c = controller_of(bus_index);
#ifdef ENABLE_I2C_DIAG
    if (bus_index == 0) {
        // ハードウェアI2Cを経由しないプローブ。パッド駆動そのものの検証を兼ねる。
        uint8_t probe = 0;
        if (swi2c_probe(0, MODULE_I2C_ADDR)) {
            probe |= 0x01u;
        }
        if (swi2c_probe(0, 0x09)) { // ch32v003fun公式サンプルのアドレス
            probe |= 0x02u;
        }
        diag_swprobe = probe;
    }
    diag_scan_step(bus_index, i2c);
#endif
    uint8_t reg = MODULE_REG_STATE;

    int written = i2c_write_timeout_us(i2c, MODULE_I2C_ADDR, &reg, 1, true, MODULE_I2C_TIMEOUT_US);
#ifdef ENABLE_I2C_DIAG
    if (bus_index == 0) {
        diag_last_write = (int8_t)written;
        diag_last_read = 0;
    }
#endif
    if (written != 1) {
        recover_controller(bus_index);
        return false; // NACK/タイムアウト: モジュール不通
    }

    uint8_t buf[MODULE_REG_STATE_LEN];
    int read = i2c_read_timeout_us(i2c, MODULE_I2C_ADDR, buf, MODULE_REG_STATE_LEN, false, MODULE_I2C_TIMEOUT_US);
#ifdef ENABLE_I2C_DIAG
    if (bus_index == 0) {
        diag_last_read = (int8_t)read;
    }
#endif
    if (read != MODULE_REG_STATE_LEN) {
        recover_controller(bus_index);
        return false;
    }

    out_state->present = true;
    out_state->switches = (uint8_t)(buf[0] & 0x0Fu);
    out_state->vr[0] = buf[1];
    out_state->vr[1] = buf[2];
    return true;
#endif // MODULE_I2C_SOFTWARE
}

void i2c_modules_poll(void) {
    // 1回の呼び出しで1バスだけ進める(ラウンドロビン)。全バスを一気に舐めると、不在バスの
    // タイムアウトが積み上がって1回の呼び出しが長時間ブロックし、メインループが回す
    // tud_task()(USB応答)を圧迫するため。全スロット一巡の周期は
    // MODULE_BUS_COUNT × メインループ周期となり、集約周期の目安(親仕様書 §2.1/§4.5)は満たす。
    uint8_t bus = next_bus;
    next_bus = module_bus_next(next_bus);

    module_state_t state;
    if (poll_module(bus, &state)) {
        shared_states.modules[bus] = state;
    } else {
        // 不通: presentのみ落とす。直前のSW/VR値はそのまま保持するが、presentが
        // falseのためHID側の変化判定(main.c)には影響しない。再接続時は次のポーリング
        // で即座に最新値へ更新される。
        shared_states.modules[bus].present = false;
    }
    // バス数を超えるスロット(MODULE_BUS_COUNT..MAX_MODULES-1)は物理的に存在しないため、
    // 常に不在のまま(初期化時のpresent=falseを維持する)。
#ifdef ENABLE_I2C_DIAG
    diag_publish();
#endif
}

void i2c_modules_get_state(module_state_array_t *out_states) {
    *out_states = shared_states;
}

bool i2c_modules_write_backlight(uint8_t module_index, const uint8_t rgb[MODULE_REG_BACKLIGHT_LEN]) {
    if (module_index >= MODULE_BUS_COUNT) {
        return false; // 存在しないスロット宛の指定は書き込まない
    }

    uint8_t buf[1 + MODULE_REG_BACKLIGHT_LEN];
    buf[0] = MODULE_REG_BACKLIGHT;
    memcpy(&buf[1], rgb, MODULE_REG_BACKLIGHT_LEN);

    // STATEポーリングとバス/コントローラを共有するため、メインループの単一コンテキストからの
    // 逐次呼び出し(backlight_task経由)のみを前提とし、明示的な排他制御は行わない。
    bus_select(module_index);
    if (!bus_is_idle(module_index)) {
        return false;
    }

#ifdef MODULE_I2C_SOFTWARE
    return swm_write_buf(module_index, MODULE_I2C_ADDR, buf, sizeof(buf));
#else
    int written = i2c_write_timeout_us(controller_of(module_index), MODULE_I2C_ADDR, buf, sizeof(buf), false,
                                       MODULE_I2C_TIMEOUT_US);
    if (written != (int)sizeof(buf)) {
        recover_controller(module_index);
        return false;
    }
    return true;
#endif
}
