#include "config.h"

const uint8_t BUTTON_ROW_PINS[BUTTON_MATRIX_MAX_ROWS] = {6, 7, 8, 9};
const uint8_t BUTTON_COL_PINS[BUTTON_MATRIX_MAX_COLS] = {10, 11, 12, 13};

const char *get_controller_id(void) {
#if CONTROLLER_ROLE == CONTROLLER_ROLE_SUB
    return "sub";
#else
    return "main";
#endif
}
