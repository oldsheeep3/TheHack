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

# モジュールI2Cのバスクロック(Hz)の上書き。既定は include/config.h の 100kHz。
# 基板に外付けプルアップが無く内蔵プルアップに頼る配線のため、立ち上がり不足を切り分ける
# 目的で -DMODULE_I2C_BAUD=20000 のように下げてビルドできるようにしてある。
set(MODULE_I2C_BAUD "" CACHE STRING "Override the module I2C bus clock in Hz (default: see config.h)")

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

# stdio UART の既定ピンは GPIO0/1 だが、実基板ではそこがモジュールI2Cのバス5
# (config.h の MODULE_BUS_PINS_INIT) に割り当たっているため、基板上で未接続の
# GPIO12/13 (UART0の別ピン割当) へ移す。移さないと stdio_init_all() と
# i2c_modules_init() が同じパッドを奪い合う。
#
# あわせて stdio UART の「受信文字あり」コールバック(= UART RX 割込み)を無効化する。
# このファームはUARTから入力を受け取らない一方、RXピンが未接続(フローティング)や
# 他機能に奪われた状態だとフレーミング/ブレーク検出で割込みが上がり続け、メインループ
# (tud_task によるUSB応答)を圧迫し得るため。
# 実機切り分け用のI2C診断チャネル(デバッグ専用)。ONにすると、各バスのSDA/SCLの実測レベルと
# I2C API の戻り値を、物理的に存在しない余りスロットへ載せてHID入力レポート0x01で読み出せる。
# モジュールとのI2Cをソフトウェア(ビットバン)で行う。**既定ON。**
# 実機(RP2040 + CH32V003モジュール)では、ハードウェアI2Cブロックを使うとモジュールが
# アドレスにACKを返さない(原因未特定)。同じピン・同じ速度でもSIOで直接叩くビットバンなら
# 100%応答することを実機で確認したため、動作する方を既定とする(src/i2c_modules.c 参照)。
# ハードウェアI2C版のコードは残してあるので、原因が判明したら -DMODULE_I2C_SOFTWARE=OFF で戻せる。
option(MODULE_I2C_SOFTWARE "Use a bit-banged I2C master instead of the hardware I2C blocks" ON)
if(MODULE_I2C_SOFTWARE)
    target_compile_definitions(pico2w_controller PRIVATE MODULE_I2C_SOFTWARE)
endif()

option(ENABLE_I2C_DIAG "Publish per-bus I2C line levels / API results in spare HID slots (debug only)" OFF)
if(ENABLE_I2C_DIAG)
    target_compile_definitions(pico2w_controller PRIVATE ENABLE_I2C_DIAG)
endif()

if(MODULE_I2C_BAUD)
    target_compile_definitions(pico2w_controller PRIVATE MODULE_I2C_BAUD=${MODULE_I2C_BAUD})
endif()

# 1トランザクションのタイムアウト(us)。MODULE_I2C_BAUD を下げたときは合わせて伸ばす。
set(MODULE_I2C_TIMEOUT_US "" CACHE STRING "Override the per-transaction I2C timeout in us (default: see config.h)")
if(MODULE_I2C_TIMEOUT_US)
    target_compile_definitions(pico2w_controller PRIVATE MODULE_I2C_TIMEOUT_US=${MODULE_I2C_TIMEOUT_US})
endif()

target_compile_definitions(pico2w_controller PRIVATE
    PICO_DEFAULT_UART=0
    PICO_DEFAULT_UART_TX_PIN=12
    PICO_DEFAULT_UART_RX_PIN=13
    PICO_STDIO_UART_SUPPORT_CHARS_AVAILABLE_CALLBACK=0
)

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
