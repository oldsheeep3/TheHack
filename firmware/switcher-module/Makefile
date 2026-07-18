# CH32V003F6P4 (RV32EC, 48MHz, Flash 16KB / SRAM 2KB) 向けクロスビルド定義。
# ch32v003fun (submodule) の共通ビルドルールを利用する。RISC-V gcc ツールチェーンが
# PATH上に必要 (README.md参照)。このリポジトリのサンドボックス環境には未導入のため
# 本Makefileでのビルドはこの環境では未検証。

all: flash

# switcher_module.c (このディレクトリ直下, ch32v003fun の規約で $(TARGET).c が必須) は
# src/main.c を include するだけの薄いエントリポイント。実体はすべて src/ 配下に置く。
TARGET := switcher_module
ADDITIONAL_C_FILES := src/module_config.c src/module_hooks.c src/module_index.c src/module_index_scale.c src/switches.c src/switches_debounce.c src/adc.c src/adc_scale.c src/backlight.c src/sk6812_frame.c src/i2c_regs.c src/i2c_slave.c
EXTRA_CFLAGS += -Iinclude -Isrc

TARGET_MCU ?= CH32V003

include ch32v003fun/ch32fun/ch32fun.mk

flash: cv_flash
clean: cv_clean
