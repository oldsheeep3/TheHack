// CONTROLLER_ROLE=CONTROLLER_ROLE_SUB でビルドした際の get_controller_id() を検証する。

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "config.h"

int main(void) {
    assert(strcmp(get_controller_id(), "sub") == 0);

    printf("test_config_sub: OK\n");
    return 0;
}
