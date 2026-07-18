# ネイティブ(ホスト)ビルド用の最小テストランナー。
# Pico SDK/クロスツールチェーン非依存。I/O非依存ロジック(debounce/tally decode等)を
# 後続タスクで追加する際は、対応する src/*.c をコピーせず src/ or include/ から直接
# 共有し(buttons_debounce.c 参照)、このMakefileにテスト対象を追加していく。

CC ?= gcc
CFLAGS ?= -std=c11 -Wall -Wextra -Werror -I../include -I../src

TESTS := test_config test_config_sub test_buttons test_ddc_tally

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

# buttons.c (GPIO依存) は含めず、Pico SDK非依存の buttons_debounce.c のみを対象にする。
test_buttons: test_buttons.c ../src/buttons_debounce.c
	$(CC) $(CFLAGS) -o $@ $^

# ddc_tally.c (GPIO/割込み依存) は含めず、Pico SDK非依存の ddc_tally_decode.c のみを対象にする。
test_ddc_tally: test_ddc_tally.c ../src/ddc_tally_decode.c
	$(CC) $(CFLAGS) -o $@ $^

clean:
	rm -f $(TESTS)
