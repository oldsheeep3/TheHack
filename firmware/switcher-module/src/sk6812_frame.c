#include "sk6812_frame.h"

void sk6812_frame_from_rgb(const uint8_t rgb[SK6812_RGB_BYTES], uint8_t frame_out[SK6812_FRAME_BYTES]) {
    for (int led = 0; led < BACKLIGHT_LED_COUNT; led++) {
        uint8_t r = rgb[led * 3 + 0];
        uint8_t g = rgb[led * 3 + 1];
        uint8_t b = rgb[led * 3 + 2];
        frame_out[led * 3 + 0] = g;
        frame_out[led * 3 + 1] = r;
        frame_out[led * 3 + 2] = b;
    }
}
