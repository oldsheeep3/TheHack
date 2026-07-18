// モジュール群(0x30..0x37)のI2Cポーリング(GPIO/ハードウェアI2C依存)。
// 集約結果のバイト列パッキングはPico SDK非依存のstate_agg.cに分離してあり、
// ここでは「STATE(0x00)を読み、共有状態配列へ格納する」I/O部分のみを担う
// (buttons.c/buttons_debounce.cの分離パターンを踏襲)。

#include "i2c_modules.h"

#include "hardware/gpio.h"
#include "hardware/i2c.h"

#include "config.h"

// i2c_modules_poll()/i2c_modules_get_state() はメインループの単一コンテキストからのみ
// 呼び出される前提(割込み/別コアからのアクセスなし)のため、排他制御は行わない。
static module_state_array_t shared_states;

void i2c_modules_init(void) {
    i2c_init(MODULE_I2C_INSTANCE, MODULE_I2C_BAUD);
    gpio_set_function(MODULE_I2C_SDA_PIN, GPIO_FUNC_I2C);
    gpio_set_function(MODULE_I2C_SCL_PIN, GPIO_FUNC_I2C);
    gpio_pull_up(MODULE_I2C_SDA_PIN);
    gpio_pull_up(MODULE_I2C_SCL_PIN);

    for (int i = 0; i < MAX_MODULES; i++) {
        shared_states.modules[i].present = false;
        shared_states.modules[i].switches = 0;
        shared_states.modules[i].vr[0] = 0;
        shared_states.modules[i].vr[1] = 0;
    }
}

// module_index (0..MAX_MODULES-1) のSTATEレジスタを読み、out_state へ格納する。
// ACK無し/タイムアウトの場合は false を返す(呼び出し側でそのモジュールをスキップする)。
static bool poll_module(uint8_t module_index, module_state_t *out_state) {
    uint8_t addr = (uint8_t)(MODULE_I2C_ADDR_BASE + module_index);
    uint8_t reg = MODULE_REG_STATE;

    int written = i2c_write_timeout_us(MODULE_I2C_INSTANCE, addr, &reg, 1, true, MODULE_I2C_TIMEOUT_US);
    if (written != 1) {
        return false; // NACK/タイムアウト: モジュール不通
    }

    uint8_t buf[MODULE_REG_STATE_LEN];
    int read = i2c_read_timeout_us(MODULE_I2C_INSTANCE, addr, buf, MODULE_REG_STATE_LEN, false, MODULE_I2C_TIMEOUT_US);
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
    for (uint8_t i = 0; i < MAX_MODULES; i++) {
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
}

void i2c_modules_get_state(module_state_array_t *out_states) {
    *out_states = shared_states;
}
