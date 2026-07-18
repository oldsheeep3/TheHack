#include "i2c_regs.h"

i2c_regs_region_t i2c_regs_decode_region(uint8_t reg_addr) {
    // MODULE_REG_STATE は 0 のため下限比較は不要(reg_addr は uint8_t で常に >= 0)。
    if (reg_addr < (uint8_t)(MODULE_REG_STATE + MODULE_REG_STATE_LEN)) {
        return I2C_REGS_REGION_STATE;
    }
    if (reg_addr >= MODULE_REG_BACKLIGHT && reg_addr < (uint8_t)(MODULE_REG_BACKLIGHT + MODULE_REG_BACKLIGHT_LEN)) {
        return I2C_REGS_REGION_BACKLIGHT;
    }
    if (reg_addr >= MODULE_REG_INFO && reg_addr < (uint8_t)(MODULE_REG_INFO + MODULE_REG_INFO_LEN)) {
        return I2C_REGS_REGION_INFO;
    }
    return I2C_REGS_REGION_INVALID;
}

bool i2c_regs_region_is_writable(i2c_regs_region_t region) {
    return region == I2C_REGS_REGION_BACKLIGHT;
}

void i2c_regs_build_state(uint8_t sw_state, uint8_t vr_src1, uint8_t vr_src2,
                           uint8_t out[MODULE_REG_STATE_LEN]) {
    out[0] = (uint8_t)(sw_state & 0x0Fu);
    out[1] = vr_src1;
    out[2] = vr_src2;
}

void i2c_regs_build_info(uint8_t out[MODULE_REG_INFO_LEN]) {
    out[0] = I2C_REGS_FW_VERSION_MAJOR;
    out[1] = I2C_REGS_FW_VERSION_MINOR;
    out[2] = I2C_REGS_CAPABILITIES;
    out[3] = I2C_REGS_HW_REV;
}

bool i2c_regs_backlight_write_is_complete(uint8_t start_offset, uint8_t received_len) {
    return start_offset == 0 && received_len == MODULE_REG_BACKLIGHT_LEN;
}
