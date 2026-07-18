# ネイティブ(ホスト)ビルド用の最小テストランナー。
# Pico SDK/クロスツールチェーン非依存。I/O非依存ロジック(state_agg等)を
# 後続タスクで追加する際は、対応する src/*.c をコピーせず src/ or include/ から直接
# 共有し(state_agg.c参照)、このMakefileにテスト対象を追加していく。

CC ?= gcc
CFLAGS ?= -std=c11 -Wall -Wextra -Werror -I../include -I../src

TESTS := test_config test_config_sub test_state_agg test_backlight test_settings test_wireless_codec

.PHONY: all test clean
all: test

test: $(TESTS)
	@for t in $(TESTS); do \
		echo "Running $$t"; \
		./$$t || exit 1; \
	done

test_config: test_config.c ../src/config.c
	$(CC) $(CFLAGS) -o $@ $^

# CONTROLLER_ROLE=CONTROLLER_ROLE_SUB でのビルドを別バイナリとして検証する
# (#if は enum ではなく #define 定数で判定する必要があることの回帰テスト)。
test_config_sub: test_config_sub.c ../src/config.c
	$(CC) $(CFLAGS) -DCONTROLLER_ROLE=CONTROLLER_ROLE_SUB -o $@ $^

# state_agg.c (I2C/USB非依存の純粋関数) のみを対象にする。i2c_modules.c/usb_hid.cは
# ハードウェアI2C/TinyUSB依存のためホストテスト対象外。
test_state_agg: test_state_agg.c ../src/state_agg.c
	$(CC) $(CFLAGS) -o $@ $^

# backlight_codec.c (I2C/GPIO非依存の純粋関数) のみを対象にする。backlight.c(キュー+I2C書込)
# はi2c_modules.hのハードウェア依存のためホストテスト対象外。
test_backlight: test_backlight.c ../src/backlight_codec.c
	$(CC) $(CFLAGS) -o $@ $^

# settings_codec.c (フラッシュ/HID非依存の純粋関数) + config.c(get_controller_idフォールバック
# 先)を対象にする。settings.c(フラッシュ消去/書込)はhardware/flash依存のためホストテスト対象外。
test_settings: test_settings.c ../src/settings_codec.c ../src/config.c
	$(CC) $(CFLAGS) -o $@ $^

# wireless_codec.c (CYW43/lwIP非依存の純粋関数) のみを対象にする。state_agg_pack/
# backlight_parse_output_reportへ委譲する箇所を検証するため、その実装(state_agg.c/
# backlight_codec.c)もリンクする。wireless.c(CYW43/lwIP依存のI/O部)はホストテスト対象外。
test_wireless_codec: test_wireless_codec.c ../src/wireless_codec.c ../src/state_agg.c ../src/backlight_codec.c
	$(CC) $(CFLAGS) -o $@ $^

clean:
	rm -f $(TESTS)
