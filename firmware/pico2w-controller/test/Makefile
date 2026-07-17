# ネイティブ(ホスト)ビルド用の最小テストランナー。
# Pico SDK/クロスツールチェーン非依存。I/O非依存ロジック(debounce/tally decode等)を
# 後続タスクで追加する際は、対応する src/*.c をコピーせず include/ 経由で共有し、
# このMakefileにテスト対象を追加していく。

CC ?= gcc
CFLAGS ?= -std=c11 -Wall -Wextra -Werror -I../include

TESTS := test_config test_config_sub

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

clean:
	rm -f $(TESTS)
