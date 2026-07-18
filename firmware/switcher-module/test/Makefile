# ネイティブ(ホスト)ビルド用の最小テストランナー。ch32v003fun/RISC-Vクロスツールチェーン
# 非依存。I/O非依存ロジック(switches_debounce/adc_scale/sk6812_frame等)を後続タスクで
# 追加する際は、対応する src/*.c をコピーせず src/ or include/ から直接共有し
# (module_index_scale.c 参照)、このMakefileにテスト対象を追加していく。

CC ?= gcc
CFLAGS ?= -std=c11 -Wall -Wextra -Werror -I../include -I../src

TESTS := test_module_config test_module_index test_switches test_adc_scale test_sk6812_frame

.PHONY: all test clean
all: test

test: $(TESTS)
	@for t in $(TESTS); do \
		echo "Running $$t"; \
		./$$t || exit 1; \
	done

test_module_config: test_module_config.c ../src/module_config.c
	$(CC) $(CFLAGS) -o $@ $^

# module_index.c (get_module_index() のI/Oプレースホルダ) は ch32v003fun非依存だが、
# 実ADC結線後(M-003)にPico SDK相当の依存が入る可能性があるため、純粋部
# (module_index_scale.c) だけを対象にする。
test_module_index: test_module_index.c ../src/module_index_scale.c
	$(CC) $(CFLAGS) -o $@ $^

# switches.c (GPIO依存のSWマトリクス走査) は含めず、純粋部の switches_debounce.c のみを対象にする。
test_switches: test_switches.c ../src/switches_debounce.c
	$(CC) $(CFLAGS) -o $@ $^

# adc.c (ADC依存のVR読取) は含めず、純粋部の adc_scale.c のみを対象にする。
test_adc_scale: test_adc_scale.c ../src/adc_scale.c
	$(CC) $(CFLAGS) -o $@ $^

# backlight.c (GPIOビットバン依存のSK6812駆動) は含めず、純粋部の sk6812_frame.c のみを対象にする。
test_sk6812_frame: test_sk6812_frame.c ../src/sk6812_frame.c
	$(CC) $(CFLAGS) -o $@ $^

clean:
	rm -f $(TESTS)
