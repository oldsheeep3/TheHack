cmake_minimum_required(VERSION 3.13)

# ボードは pico2_w (RP2350 + CYW43 Wi-Fi) 固定。
set(PICO_BOARD pico2_w CACHE STRING "Board type")

include(pico_sdk_import.cmake)

project(pico2w_controller C CXX ASM)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)

pico_sdk_init()

add_executable(pico2w_controller
    src/main.c
    src/config.c
    src/board_id.c
    src/i2c_modules.c
    src/state_agg.c
    src/usb_hid.c
    src/backlight.c
    src/backlight_codec.c
    src/settings.c
    src/settings_codec.c
)

target_include_directories(pico2w_controller PRIVATE
    ${CMAKE_CURRENT_LIST_DIR}/include
)

# USBはベンダーHID(src/usb_hid.c)専用。固定CDCディスクリプタのpico_enable_stdio_usbとは
# 共存できないため使わず、デバッグ用printf()はUART stdioへ出す。
pico_enable_stdio_usb(pico2w_controller 0)
pico_enable_stdio_uart(pico2w_controller 1)

target_link_libraries(pico2w_controller
    pico_stdlib
    pico_unique_id
    tinyusb_device
    tinyusb_board
    hardware_gpio
    hardware_i2c
    hardware_flash
    hardware_sync
)

# CYW43 (Wi-Fi) は P2-003 で有効化予定。
# 有効化時は target_link_libraries に pico_cyw43_arch_none もしくは
# pico_cyw43_arch_lwip_poll / pico_cyw43_arch_lwip_threadsafe_background を追加する。

pico_add_extra_outputs(pico2w_controller)
