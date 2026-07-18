// sk6812_frame.c (GPIO非依存) のRGB→GRBバイト順変換・各灯マッピング・フレーム長を検証する。

#include <assert.h>
#include <stdio.h>
#include <string.h>

#include "module_config.h"
#include "sk6812_frame.h"

// 単灯分のRGB→GRBバイト順変換 (R,G,B → G,R,B) を検証する。
static void test_single_led_byte_order(void) {
    uint8_t rgb[SK6812_RGB_BYTES] = {0};
    uint8_t frame[SK6812_FRAME_BYTES] = {0};

    rgb[0] = 0x11; // R
    rgb[1] = 0x22; // G
    rgb[2] = 0x33; // B

    sk6812_frame_from_rgb(rgb, frame);

    assert(frame[0] == 0x22); // G
    assert(frame[1] == 0x11); // R
    assert(frame[2] == 0x33); // B
}

// 4灯分, 各灯が独立に正しくマッピングされることを検証する(灯間の混線がない)。
static void test_four_leds_independent_mapping(void) {
    uint8_t rgb[SK6812_RGB_BYTES] = {
        0x01, 0x02, 0x03, // led0: R,G,B
        0x11, 0x12, 0x13, // led1
        0x21, 0x22, 0x23, // led2
        0x31, 0x32, 0x33, // led3
    };
    uint8_t frame[SK6812_FRAME_BYTES] = {0};

    sk6812_frame_from_rgb(rgb, frame);

    const uint8_t expected[SK6812_FRAME_BYTES] = {
        0x02, 0x01, 0x03, // led0: G,R,B
        0x12, 0x11, 0x13, // led1
        0x22, 0x21, 0x23, // led2
        0x32, 0x31, 0x33, // led3
    };
    assert(memcmp(frame, expected, sizeof(expected)) == 0);
}

// 入出力とも 4灯×3バイト=12バイトであることを検証する。
static void test_frame_length(void) {
    assert(SK6812_RGB_BYTES == BACKLIGHT_LED_COUNT * 3);
    assert(SK6812_FRAME_BYTES == BACKLIGHT_LED_COUNT * 3);
    assert(SK6812_RGB_BYTES == 12);
    assert(SK6812_FRAME_BYTES == 12);
}

int main(void) {
    test_single_led_byte_order();
    test_four_leds_independent_mapping();
    test_frame_length();

    printf("test_sk6812_frame: OK\n");
    return 0;
}
