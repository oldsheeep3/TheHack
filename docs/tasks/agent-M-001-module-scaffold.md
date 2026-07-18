---
name: agent-M-001-module-scaffold
status: done
pid: 345953
agent_cli: sonnet
---

# 実装指示書: モジュールファーム基盤 & ビルドスキャフォールド（直列ゲート）

## 概要
`ch32v003fun`（仕様書 §3 推奨）による CH32V003F6P4 スイッチングモジュール・ファームウェアのビルド基盤を構築する。ピン/設定ヘッダ（SW×4 / VR×2 / SK6812×4 / I2Cベースアドレス `0x30` / モジュール番号ストラップ）、`main.c` の初期化・メインループ骨格、そして I/O から分離した純粋ロジックをホストテストするための土台（`test/`）を用意する。既存 `firmware/pico2w-controller/` で実証済みの「I/O とロジックの分離 / native gcc ホストテスト」パターンを踏襲する。

## 前提条件（依存タスク）
- なし（本タスクが全タスクの前提・直列ゲート）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `firmware/switcher-module/` 配下（新規）。

## 実装ステップ
1. **ビルド基盤**: `ch32v003fun` を submodule もしくは vendored build glue として取り込み、`CMakeLists.txt`（または `Makefile`）で CH32V003F6P4（RV32EC, 48MHz, Flash 16KB / SRAM 2KB）向けにビルドできる定義を用意する。ペリフェラル（I2Cスレーブ / ADC / SPI or 厳密タイミングGPIO / GPIO）は後続タスクで有効化する前提で骨格のみ。
2. `include/module_config.h` を作成し、ハード定義を集約（マジックナンバーを避け全て定数化）:
   - **SWマトリクスピン**（`PGM1×SRC1, PGM1×SRC2, PGM2×SRC1, PGM2×SRC2` の行/列）。
   - **VRピン**（`VR_SRC1`, `VR_SRC2` の ADC チャネル）。
   - **SK6812データピン**（4灯チェーン, SPI or ビットバン用）。
   - **I2Cベースアドレス** `#define I2C_BASE_ADDR 0x30`、`#define MAX_MODULES 8`（親仕様書 §4.0）。
   - **モジュール番号取得** `get_module_index()`: GPIOストラップ/抵抗IDから 0..`MAX_MODULES`-1 を返すユーティリティ（実機ストラップ配線はHW依存のため、読取方式を関数として用意し、テスト可能な純粋部（strap生値→index）は分離してよい）。I2Cスレーブアドレスは `I2C_BASE_ADDR + get_module_index()` = `0x30..0x37`。
3. `src/main.c`: 初期化（クロック/GPIO）とメインループの骨格。各モジュール（switches/adc/backlight/i2c_slave）は後続タスクで追加される前提の空フック（宣言のみ or weak）を置く。SRAM 2KB を意識し静的バッファを最小化。
4. **ホストテスト土台**: `test/` に native ビルド用の最小 Makefile を用意（`firmware/pico2w-controller/test/Makefile` に倣い `CC ?= gcc`, `CFLAGS ?= -std=c11 -Wall -Wextra -Werror -I../include -I../src`）。GPIO/ペリフェラル依存の `.c` は含めず、後続タスクの純粋部（`switches_debounce.c` 等）を直接共有して単体テストする構成。プレースホルダテスト（例: `get_module_index()` の純粋部）を1つ green にしておく。
5. README（`firmware/switcher-module/README.md`）にビルド手順を記載: `ch32v003fun` ツールチェーン（RISC-V gcc）でのクロスビルド手順、ホストテスト実行手順（`cd test && make`）、書き込み（flash）手順（`minichlink` 等）。ツールチェーン未導入環境の前提を明記。
6. クロスビルド（環境があれば）で `.bin`/`.elf` 生成を確認、無ければホストテスト土台のビルドが通ることを確認しコミット。

## 完了条件 / 検証コマンド
- `test/` の native ビルドが成功し、プレースホルダテストが green（`cd firmware/switcher-module/test && make`）。
- クロスビルド手順が README に明記され、`ch32v003fun` 導入環境がある場合はビルド確認（可能なら `.bin`/`.elf` 生成）。
- `module_config.h` に SW/VR/LEDピン・`I2C_BASE_ADDR 0x30`・`MAX_MODULES 8`・`get_module_index()` が定数化/提供され、I2Cアドレスが `0x30 + module_index` となる方式が明示されている（親仕様書 §4.0/§4.5 準拠）。

## 技術的な補足 / レビュー観点
- **本タスクのピン定義・IF・ディレクトリ構成・ホストテスト方式が後続タスクの契約**。命名・ヘッダ構成を安定させる。
- I/O とロジックを分離し、ロジックをホストでテスト可能にする方針を土台から徹底（`.claude/review-patterns.md`「設計・責務分離」「テスト」）。
- SRAM 2KB 制約: バッファ・スタックを最小化する方針を土台に反映。
- ツールチェーン未導入環境の前提を README に明記（仕様書 §5）。

## 参照
- 仕様: `docs/specs/switcher-module-firmware.md` §1, §3, §5, §6 / `docs/specs/00-system-overview.md` §4.0, §4.5
- 既存パターン: `firmware/pico2w-controller/`（`test/Makefile`, `include/config.h`, I/O分離）
