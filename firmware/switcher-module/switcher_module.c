// ch32v003fun のビルド規約 ($(TARGET).c が Makefile と同階層に必要) を満たすための
// 薄いエントリポイント。実体は src/main.c に置く (pico2w-controller の src/ レイアウトに
// 倣うため; ディレクトリ構成は後続タスクの契約, docs/specs/switcher-module-firmware.md §6)。
#include "src/main.c"
