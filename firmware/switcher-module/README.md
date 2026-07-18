# switcher-module ファームウェア

CH32V003F6P4（RISC-V RV32EC, 48MHz, Flash 16KB / SRAM 2KB）向け、スイッチングモジュール内マイコンファームウェア。SW×4 / VR×2 / SK6812MINI-E×4 をスキャン/駆動し、マスターの Pico 2W と I2C（スレーブ）で通信する。

> 親プロジェクト: [`../../README.md`](../../README.md) ／ 仕様書: [`docs/specs/switcher-module-firmware.md`](../../docs/specs/switcher-module-firmware.md)（親: [`docs/specs/00-system-overview.md`](../../docs/specs/00-system-overview.md) §4.0, §4.5）

## ディレクトリ構成

```
firmware/switcher-module/
├── ch32v003fun/              # submodule (https://github.com/cnlohr/ch32v003fun)
├── Makefile                   # ch32v003fun/ch32fun/ch32fun.mk を使ったクロスビルド定義
├── switcher_module.c          # ch32v003funの規約上必要なエントリポイント (src/main.c をinclude)
├── include/
│   └── module_config.h        # SW/VR/LEDピン・I2Cベースアドレス(0x30)・MAX_MODULES(8)等の定数 (後続タスク共通IF)
├── src/
│   ├── main.c                 # 初期化 + メインループ骨格 (各サブシステムはmodule_hooks経由の空フック)
│   ├── module_config.c        # module_config.h の定数実体 (ch32v003fun非依存)
│   ├── module_hooks.h         # switches/adc/backlight/i2c_slave の init/task 関数宣言 (後続タスクが実装)
│   ├── module_hooks.c         # 上記の weak no-op デフォルト実装
│   ├── module_index.h         # モジュール番号取得 (get_module_index) の公開API
│   ├── module_index.c         # get_module_index() のI/Oプレースホルダ (実ADC結線はM-003)
│   ├── module_index_scale.c   # ADC生値→モジュール番号、モジュール番号→I2Cアドレスの純粋変換 (ホストテスト対象)
│   ├── switches.h / switches.c            # SWマトリクス走査 (GPIO依存, module_hooks strong実装)
│   └── switches_debounce.h / switches_debounce.c
│                                # SWデバウンス純粋状態機械 (GPIO非依存, ホストテスト対象)
└── test/                      # ホスト(native)ビルド用ユニットテスト (ch32v003fun/クロスツールチェーン非依存)
    ├── Makefile
    ├── test_module_config.c
    ├── test_module_index.c
    └── test_switches.c
```

`switches_*` は本タスク(M-002)で実装済み（`switches.c` がGPIO走査、`switches_debounce.c` が純粋デバウンス状態機械）。`adc_*` / `backlight_*` / `i2c_slave_*` の実処理は後続タスク（M-003/M-004）が対応する `src/*.c` を追加し、`module_hooks.h` の関数を strong 定義することで結線される。それまでは `module_hooks.c` の weak no-op が呼ばれる。

## SWマトリクス・デバウンス

4SW（`PGM1×SRC1, PGM1×SRC2, PGM2×SRC1, PGM2×SRC2`）を `switches_task()` が毎ループ走査し、生の4bitサンプル（`module_config.h` の `SW_BIT_INDEX(row, col)` 準拠のビット位置）を `switches_debounce.c` の純粋状態機械へ渡す。各SWビットは独立に `SWITCHES_DEBOUNCE_STABLE_SAMPLES`（5）回連続で同じ生値が観測された時点で確定し、`switches_get_state()` が確定4bit状態を I2C `0x00 STATE` レジスタ[0] の下位4bit形式（上位4bitは0埋め）で返す。実際のI2Cレジスタへの結線は M-004 で行う。

## モジュール番号とI2Cアドレス

- モジュール番号（0..`MAX_MODULES`-1 = 0..7）は、抵抗ID方式でモジュールごとに異なる電圧を作り、1本のADCピン（`MODULE_STRAP_ADC_CHANNEL`）で読み取って `MAX_MODULES` 個のバケットに分類することで決定する（`module_index_from_strap_adc()`、純粋関数、ホストテスト済み）。実機のストラップ配線・抵抗値はHW確定後に決定するため、`get_module_index()`（`src/module_index.c`）は現状 module 0 固定のプレースホルダ実装であり、実ADC結線は M-003（`adc.c`）統合時に行う。
- I2Cスレーブアドレスは `I2C_BASE_ADDR(0x30) + module_index` = `0x30..0x37`（`i2c_slave_address_for_module()`、親仕様書 §4.5）。

## クロスビルド（`ch32v003fun` ツールチェーン必要）

このリポジトリのサンドボックス環境には RISC-V gcc ツールチェーン（`riscv64-unknown-elf-gcc` 等）がインストールされていないため、以下のビルドはこの環境では未検証。実機ビルド環境で確認すること。

```sh
# ch32v003fun (submodule) を取得
git submodule update --init --recursive

# RISC-V gcc ツールチェーンをPATHに用意する (例: xpack riscv-none-elf-gcc)。
# ch32fun.mk はPATH上の riscv64-unknown-elf-gcc / riscv-none-elf-gcc 等を自動検出する。

cd firmware/switcher-module
make        # switcher_module.elf / .bin / .hex を生成 (デフォルトターゲットは flash)
```

書き込み（flash）には `minichlink`（`ch32v003fun/minichlink/`, WCH-LinkE 等のプログラマ経由）を使う:

```sh
cd firmware/switcher-module
make flash  # ビルド + minichlinkでの書き込みを実行
```

## ホストテスト（ch32v003fun/クロスツールチェーン不要, このリポジトリで検証済み）

I/O に依存しない純粋ロジック（`module_config.h` の定数、ADC生値→モジュール番号のバケット変換、モジュール番号→I2Cアドレス変換など）はホストの `gcc` でネイティブビルド・実行してテストする。GPIO/ADC/SPI等のペリフェラルに依存する `.c`（`module_index.c` 等）は対象に含めない。

```sh
cd firmware/switcher-module/test
make        # ビルド + 実行。全テスト green で終了コード0
```

## 参照

- 仕様: [`docs/specs/switcher-module-firmware.md`](../../docs/specs/switcher-module-firmware.md) §1, §3, §5, §6 / [`docs/specs/00-system-overview.md`](../../docs/specs/00-system-overview.md) §4.0, §4.5
- 既存パターン: [`../pico2w-controller/`](../pico2w-controller/)（`test/Makefile`, `include/config.h`, I/O分離）
