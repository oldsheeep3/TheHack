---
name: agent-M-003-analog-backlight
status: doing
pid: 374718
agent_cli: sonnet
---

# 実装指示書: アナログVR読取（ADC）& バックライト駆動（SK6812×4）

## 概要
2つのアナログボリューム（`VR_SRC1`, `VR_SRC2`）を CH32V003 の ADC で読み取り、ノイズ抑制（移動平均/デッドバンド）のうえ 8bit（0..255）へスケールする。あわせて SK6812MINI-E ×4 のバックライトチェーンを **800kHz・GRB順**で駆動する。スケーリング（`adc_scale.c`）と RGB→GRB フレーム組み立て（`sk6812_frame.c`）は **I/O から分離した純粋関数**として実装し、ホスト（native gcc）でユニットテスト可能にする。

## 前提条件（依存タスク）
- `agent-M-001-module-scaffold` が `done`（ピン定義・`module_config.h`・ホストテスト土台が確定）。
- （並びとしては M-002 の後に直列起動するが、コード依存はない。共通土台は M-001。）

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `firmware/switcher-module/`（主に `src/adc.{c,h}`, `src/adc_scale.{c,h}`, `src/backlight.{c,h}`, `src/sk6812_frame.{c,h}`, `src/main.c` のフック結線, `test/`）。

## 実装ステップ
### A. アナログVR（ADC）
1. `src/adc.{c,h}`（I/O層）: `module_config.h` の ADC チャネルで `VR_SRC1`/`VR_SRC2` を読み取る。
2. `src/adc_scale.{c,h}`（**純粋 / ペリフェラル非依存**）:
   - ADC生値 → 8bit（0..255）へのスケーリング。
   - ノイズ抑制（移動平均 and/or デッドバンド）を純粋関数として実装（状態は最小）。
   - VR は適度な間引き/デッドバンドで更新（微小変動でのバタつき抑制）。
3. スケール済み値を I2C `0x00 STATE` の `[1]`=`VR_SRC1`, `[2]`=`VR_SRC2` に反映できる形で提供（実結線は M-004）。

### B. バックライト（SK6812×4）
4. `src/backlight.{c,h}`（I/O層）: 4灯チェーンを **800kHz GRB** で駆動。CH32V003 の SPI もしくは厳密タイミングのビットバンで実装し、フレーム末尾にリセット（>80us Low）で確定。**割込み禁止区間を最小化**してチラつきを防ぐ（仕様書 §4）。
5. `src/sk6812_frame.{c,h}`（**純粋 / ペリフェラル非依存**）:
   - 4灯分の RGB（12バイト, 親仕様書 §4.5 `0x10 BACKLIGHT` 受領形式）→ SK6812 の **GRB順**フレーム（送出バイト列）への変換・組み立て。
   - 色はPCが算出する前提で、本ファームは受領色を忠実に変換・出力するのみ（加工しない）。
6. `src/main.c`: ADC読取→スケール、および（M-004結線後に）受領バックライト→フレーム→駆動をメインループに結線するフックを用意。

### C. ホストテスト
7. `test/` に純粋部のみを対象に追加（`adc.c`/`backlight.c` の I/O は含めない）:
   - `test_adc_scale`（`../src/adc_scale.c`）: 生値→0..255の境界（0/中央/最大クランプ）、移動平均、デッドバンド挙動。
   - `test_sk6812_frame`（`../src/sk6812_frame.c`）: RGB→GRB のバイト順、4灯×3バイト=12バイト入力の各灯マッピング、出力フレーム長。
   - Makefile に上記テストターゲットを追加。

## 完了条件 / 検証コマンド
- `test/` の ADCスケール・SK6812フレームのユニットテストが green（native gcc `-Werror`, `cd firmware/switcher-module/test && make`）。
- クロスビルドが成功（`ch32v003fun` 環境がある場合）。README にクロスビルド手順が反映済み。
- VR値が `0x00 STATE` `[1]`/`[2]`（0..255）へ、バックライトが `0x10 BACKLIGHT`（12B, GRB変換）へ、親仕様書 §4.5 の配置・順序どおりに提供/変換される。

## 技術的な補足 / レビュー観点
- **タイミング**: SK6812 は 800kHz GRB。ビットバン時は割込み禁止区間を最小化（1灯単位で区切る等）。SPI利用時はビットレート/エンコード方式をコメントで明示。
- **純粋分離**: スケーリングと GRB フレーム組み立ては純粋関数（ホストテスト対象）。I/O層（ADC/SPI/GPIO）は含めない。`.claude/review-patterns.md`「設計・責務分離」「テスト」「リソース管理」。
- **メモリ**: SK6812 フレームバッファは 4灯×3バイト規模。移動平均バッファも含め SRAM 2KB を圧迫しないサイズに。
- **境界**: バックライト未受領時（初期状態）の出力色・消灯方針を明記。

## 参照
- 仕様: `docs/specs/switcher-module-firmware.md` §2.2, §2.3, §4, §5 / `docs/specs/00-system-overview.md` §4.5
- 既存パターン: `firmware/pico2w-controller/`（純粋部の分離・`test/Makefile`）
