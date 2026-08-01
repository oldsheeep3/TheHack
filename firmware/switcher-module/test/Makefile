# ネイティブ(ホスト)ビルド用の最小テストランナー。ch32v003fun/RISC-Vクロスツールチェーン
# 非依存。I/O非依存ロジック(switches_debounce/adc_scale/sk6812_frame等)を後続タスクで
# 追加する際は、対応する src/*.c をコピーせず src/ or include/ から直接共有し
# (module_index_scale.c 参照)、このMakefileにテスト対象を追加していく。
#
# GPIO/ADCに触る src/*.c (switches.c, adc.c) は stub/ch32fun.h (ホスト用スタブ) を
# -Istub で本物より先に見つけさせることでホスト上でもリンクできる。基板配線に直結した
# 「どのピン/チャネルをどう読むか」を回帰テストするための仕組み。

CC ?= gcc
CFLAGS ?= -std=c11 -Wall -Wextra -Werror -I../include -I../src
STUB_CFLAGS := -Istub

TESTS := test_module_config test_module_index test_switches test_switches_io \
	test_adc_scale test_adc_io test_sk6812_frame test_i2c_regs

.PHONY: all test clean
all: test

test: $(TESTS)
	@for t in $(TESTS); do \
		echo "Running $$t"; \
		./$$t || exit 1; \
	done

test_module_config: test_module_config.c ../src/module_config.c
	$(CC) $(CFLAGS) -o $@ $^

# module_index.c (get_module_index()) は定数を返すだけだが、I/O部として分離したまま
# 純粋部 (module_index_scale.c) を対象にする。
test_module_index: test_module_index.c ../src/module_index_scale.c
	$(CC) $(CFLAGS) -o $@ $^

# switches.c (GPIO依存のSW直読み) は含めず、純粋部の switches_debounce.c のみを対象にする。
test_switches: test_switches.c ../src/switches_debounce.c
	$(CC) $(CFLAGS) -o $@ $^

# switches.c 本体を stub/ch32fun.h 経由でホストビルドし、ピン割当・アクティブLow解釈を検証する。
test_switches_io: test_switches_io.c ../src/switches.c ../src/switches_debounce.c ../src/module_config.c stub/ch32fun_stub.c
	$(CC) $(CFLAGS) $(STUB_CFLAGS) -o $@ $^

# adc.c (ADC依存のVR読取) は含めず、純粋部の adc_scale.c のみを対象にする。
test_adc_scale: test_adc_scale.c ../src/adc_scale.c
	$(CC) $(CFLAGS) -o $@ $^

# adc.c 本体を stub/ch32fun.h 経由でホストビルドし、VR→ADCチャネルの割当を検証する。
test_adc_io: test_adc_io.c ../src/adc.c ../src/adc_scale.c ../src/module_config.c stub/ch32fun_stub.c
	$(CC) $(CFLAGS) $(STUB_CFLAGS) -o $@ $^

# backlight.c (GPIOビットバン依存のSK6812駆動) は含めず、純粋部の sk6812_frame.c のみを対象にする。
test_sk6812_frame: test_sk6812_frame.c ../src/sk6812_frame.c
	$(CC) $(CFLAGS) -o $@ $^

# i2c_slave.c (I2C1ペリフェラル/ISR依存) は含めず、純粋部の i2c_regs.c のみを対象にする。
test_i2c_regs: test_i2c_regs.c ../src/i2c_regs.c
	$(CC) $(CFLAGS) -o $@ $^

clean:
	rm -f $(TESTS)
