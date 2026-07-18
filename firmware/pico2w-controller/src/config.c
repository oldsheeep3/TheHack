#include "config.h"

const char *get_controller_id(void) {
#if CONTROLLER_ROLE == CONTROLLER_ROLE_SUB
    return "sub";
#else
    return "main";
#endif
}
