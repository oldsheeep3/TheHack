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

static i2c_inst_t *controller_of(uint8_t bus_index) {
    return module_bus_i2c_index(bus_index) == 0 ? i2c0 : i2c1;
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
    i2c_init(i2c0, MODULE_I2C_BAUD);
    i2c_init(i2c1, MODULE_I2C_BAUD);

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

// bus_index (= module_index) のSTATEレジスタを読み、out_state へ格納する。
// ACK無し/タイムアウトの場合は false を返す(呼び出し側でそのモジュールをスキップする)。
static bool poll_module(uint8_t bus_index, module_state_t *out_state) {
    bus_select(bus_index);
    i2c_inst_t *i2c = controller_of(bus_index);
    uint8_t reg = MODULE_REG_STATE;

    int written = i2c_write_timeout_us(i2c, MODULE_I2C_ADDR, &reg, 1, true, MODULE_I2C_TIMEOUT_US);
    if (written != 1) {
        return false; // NACK/タイムアウト: モジュール不通
    }

    uint8_t buf[MODULE_REG_STATE_LEN];
    int read = i2c_read_timeout_us(i2c, MODULE_I2C_ADDR, buf, MODULE_REG_STATE_LEN, false, MODULE_I2C_TIMEOUT_US);
    if (read != MODULE_REG_STATE_LEN) {
        return false;
    }

    out_state->present = true;
    out_state->switches = (uint8_t)(buf[0] & 0x0Fu);
    out_state->vr[0] = buf[1];
    out_state->vr[1] = buf[2];
    return true;
}

void i2c_modules_poll(void) {
    for (uint8_t i = 0; i < MODULE_BUS_COUNT; i++) {
        module_state_t state;
        if (poll_module(i, &state)) {
            shared_states.modules[i] = state;
        } else {
            // 不通: presentのみ落とす。直前のSW/VR値はそのまま保持するが、presentが
            // falseのためHID側の変化判定(main.c)には影響しない。再接続時は次のポーリング
            // で即座に最新値へ更新される。
            shared_states.modules[i].present = false;
        }
    }
    // バス数を超えるスロット(MODULE_BUS_COUNT..MAX_MODULES-1)は物理的に存在しないため、
    // 常に不在のまま(初期化時のpresent=falseを維持する)。
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
    int written = i2c_write_timeout_us(controller_of(module_index), MODULE_I2C_ADDR, buf, sizeof(buf), false,
                                       MODULE_I2C_TIMEOUT_US);
    return written == (int)sizeof(buf);
}
