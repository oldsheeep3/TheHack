cmake_minimum_required(VERSION 3.13)

# ボードは pico2_w (RP2350 + CYW43 Wi-Fi) 固定。
set(PICO_BOARD pico2_w CACHE STRING "Board type")

include(pico_sdk_import.cmake)

project(pico2w_controller C CXX ASM)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)

# Wi-Fiワイヤレス制御チャネル(P2-003, 親仕様書 §2.5「任意」)。デフォルトOFF: 無線を
# 使わないビルドでCYW43/lwIPの依存・リンクサイズを増やさない。有効化する場合のみ
# -DENABLE_WIRELESS=ON を指定する(README参照)。
option(ENABLE_WIRELESS "Enable CYW43 Wi-Fi wireless control channel (optional, spec §2.5)" OFF)

# デバッグ用 fake モジュール層。ON にすると I2C ポーリング(src/i2c_modules.c)を
# src/i2c_modules_fake.c へ差し替え、モジュールの状態を PC からのデバッグ用 HID
# レポート 0x04 で供給する。実機のスイッチングモジュール(CH32V003)が無い状態で
# SW/VR 集約・HID 経路・バックライト配布を検証するための開発専用ビルド。
# デフォルト OFF: 本番ビルドにはデバッグ用レポートも fake 実装も一切含まれない。
option(ENABLE_FAKE_MODULES "Replace I2C module polling with a PC-driven fake layer (debug only)" OFF)

pico_sdk_init()

add_executable(pico2w_controller
    src/main.c
    src/config.c
    src/board_id.c
    src/state_agg.c
    src/usb_hid.c
    src/backlight.c
    src/backlight_codec.c
    src/settings.c
    src/settings_codec.c
    src/wireless_codec.c
)

target_include_directories(pico2w_controller PRIVATE
    ${CMAKE_CURRENT_LIST_DIR}/include
)

# I2Cポーリングの実装を選ぶ。fake層は i2c_modules.h の契約をそのまま実装するため、
# main.c 側には分岐が一切必要ない。
if(ENABLE_FAKE_MODULES)
    target_sources(pico2w_controller PRIVATE
        src/i2c_modules_fake.c
        src/fake_modules_codec.c
    )
    target_compile_definitions(pico2w_controller PRIVATE FAKE_MODULES)
else()
    target_sources(pico2w_controller PRIVATE src/i2c_modules.c src/module_bus.c)
endif()

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

# CYW43 (Wi-Fi) ワイヤレス制御チャネル(P2-003, §2.5「任意」)。ENABLE_WIRELESS=ON の
# ときのみ src/wireless.c をビルド対象に加え、CYW43/lwIP(threadsafe_background)を
# リンクする。OFF(デフォルト)のビルドでは依存・リンクサイズが一切増えない。
if(ENABLE_WIRELESS)
    target_sources(pico2w_controller PRIVATE src/wireless.c)
    target_compile_definitions(pico2w_controller PRIVATE ENABLE_WIRELESS)
    target_link_libraries(pico2w_controller
        pico_cyw43_arch_lwip_threadsafe_background
    )
endif()

pico_add_extra_outputs(pico2w_controller)
