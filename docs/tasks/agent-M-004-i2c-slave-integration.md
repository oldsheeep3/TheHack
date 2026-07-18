---
name: agent-M-004-i2c-slave-integration
status: done
pid: 401836
agent_cli: sonnet
---

# 実装指示書: I2Cスレーブ & レジスタマップ統合（直列統合ゲート）

## 概要
CH32V003 を I2Cスレーブとして動作させ、親仕様書 §4.5 のレジスタマップを実装する。アドレスは **ベース `0x30` + モジュール番号**（`get_module_index()`, `0x30..0x37`）。M-002 の SW確定状態・M-003 の VR値/バックライト変換を各レジスタへ結線し、メインループへ統合する。I2C割込みは短く保ち、スキャン/ADC/LED更新はメインループで行う（割込みとの共有はフラグ/リングで安全化）。

## 前提条件（依存タスク）
- `agent-M-001-module-scaffold` が `done`（ビルド基盤・`module_config.h`・I2Cベースアドレス・`get_module_index()`）。
- `agent-M-002-switch-input` が `done`（SW確定状態 → STATE下位4bit）。
- `agent-M-003-analog-backlight` が `done`（VRスケール → STATE `[1]`/`[2]`、バックライト RGB→GRB 変換・駆動）。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `firmware/switcher-module/`（主に `src/i2c_slave.{c,h}`, `src/main.c` の統合結線, `test/`）。

## 実装ステップ
1. `src/i2c_slave.{c,h}`:
   - CH32V003 の I2Cスレーブ初期化。スレーブアドレス = `I2C_BASE_ADDR (0x30) + get_module_index()`（`0x30..0x37`）。
   - **レジスタマップ**（親仕様書 §4.5）を実装:
     - `0x00 STATE`（**read, 3B**）: `[0]`=SW状態（下位4bit, `b0..b3`）/ `[1]`=`VR_SRC1`(0..255) / `[2]`=`VR_SRC2`(0..255)。
     - `0x10 BACKLIGHT`（**write, 12B**）: 4灯分 RGB。受領後にモジュール側で SK6812 GRB へ変換して駆動（M-003 の `sk6812_frame.c` を利用）。
     - `0xF0 INFO`（**read, 4B**）: `[0..1]`=firmware version / `[2]`=capabilities / `[3]`=module HW rev。定数として定義。
   - **レジスタ読み書きの純粋部を分離**: 現在の状態スナップショット（SW/VR）→ STATE 3バイト生成、および受領 12バイト → バックライト RGB バッファ確定、を純粋関数化してホストテスト可能にする（`i2c_regs.c` 等）。
2. **割込み/メインループ規律**:
   - I2C ISR は「レジスタポインタ確定・1バイト授受・受信バッファ格納」など最小限に留める。
   - SWスキャン・ADC読取・SK6812駆動はメインループで実施。ISR とメインループ間の共有は volatile フラグ/ダブルバッファ/リングで安全化（`0x10` 受領完了フラグを立て、メインループで反映）。
   - SK6812 駆動中の割込み禁止区間を最小化（M-003 方針を踏襲）。
3. `src/main.c`: 初期化で I2Cスレーブ・switches・adc・backlight を起動し、メインループで「走査→デバウンス→STATE更新 / ADC→スケール→STATE更新 / バックライト受領→フレーム→駆動」を統合。
4. ホストテスト(`test/`): レジスタ純粋部を対象に追加:
   - `0x00 STATE` 3バイト生成: SW下位4bit＋VR2バイトの配置が §4.5 と一致。
   - `0x10 BACKLIGHT` 12バイト受領 → 4灯 RGB 分解 → GRBフレーム（M-003 と整合）。
   - `0xF0 INFO` 4バイトの内容（version/capabilities/HW rev）。
   - Makefile にテストターゲットを追加。
5. ホストテストを green にし、クロスビルド（環境があれば）を確認してコミット。README にレジスタマップ・アドレス決定方式・実機I2C確認手順を追記。

## 完了条件 / 検証コマンド
- `test/` の全ホストテストが green（native gcc `-Werror`, `cd firmware/switcher-module/test && make`）。
- クロスビルドが README に手順明記され、`ch32v003fun` 導入環境がある場合はビルド確認（可能なら `.bin`/`.elf` 生成）。
- **I2Cレジスタ挙動が親仕様書 §4.5 と一致**: `0x00`(read,3B)/`0x10`(write,12B)/`0xF0`(read,4B) のバイト配置・方向・意味、スレーブアドレス `0x30 + module_index`。
- I2C ISR が短く、スキャン/ADC/LED はメインループで処理される構成になっている（レビューで確認）。

## 技術的な補足 / レビュー観点
- **並行性**: ISR とメインループの共有データは volatile / ダブルバッファ / フラグで保護。書込み(`0x10`)の途中受信と反映のアトミック性を担保。
- **障害隔離**: 1モジュールのI2C不通が他へ波及しない前提（Pico側でタイムアウト・スキップ）。スレーブ側は不正レジスタ/長さのNACK/無視方針を明記。
- **メモリ**: STATE/バックライト/受信バッファは固定小サイズ。SRAM 2KB を圧迫しない。
- `.claude/review-patterns.md`「並行性」「リソース管理」「境界」「インターフェース互換性」。

## 参照
- 仕様: `docs/specs/switcher-module-firmware.md` §2.4, §4, §5 / `docs/specs/00-system-overview.md` §4.0, §4.5
- 既存パターン: `firmware/pico2w-controller/`（純粋部分離・`test/Makefile`・ISR/メインループ分離）
