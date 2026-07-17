#include "pico/unique_id.h"

#include "config.h"

void get_unique_board_id(char *buf, size_t buf_len) {
    pico_get_unique_board_id_string(buf, buf_len);
}
