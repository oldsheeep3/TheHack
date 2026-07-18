---
name: agent-M-002-switch-input
status: done
pid: 363450
agent_cli: sonnet
---

# 実装指示書: スイッチ入力スキャン & デバウンス（純粋状態機械）

## 概要
4スイッチ（`PGM1×SRC1, PGM1×SRC2, PGM2×SRC1, PGM2×SRC2`）のマトリクスを常時スキャンし、チャタリング防止（デバウンス）を行う。デバウンスは **I/O から分離した純粋状態機械**（`switches_debounce.c`）として実装し、ホスト（native gcc）でユニットテスト可能にする。確定した安定状態を I2C `0x00 STATE` レジスタの**下位4bit**（`b0=PGM1×SRC1, b1=PGM1×SRC2, b2=PGM2×SRC1, b3=PGM2×SRC2`）に反映できる形で保持する。

## 前提条件（依存タスク）
- `agent-M-001-module-scaffold` が `done`（ピン定義・`module_config.h`・ホストテスト土台が確定）。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `firmware/switcher-module/`（主に `src/switches.{c,h}`, `src/switches_debounce.{c,h}`, `src/main.c` のフック結線, `test/`）。

## 実装ステップ
1. `src/switches.{c,h}`（I/O層）:
   - `module_config.h` のピンを用いた 4スイッチ・マトリクス走査（GPIO読取）。
   - 走査で得た生サンプルを純粋デバウンス層へ渡し、確定状態を取得する薄いラッパ。
2. `src/switches_debounce.{c,h}`（**純粋状態機械 / GPIO非依存**）:
   - 入力サンプル列（4bit 生値の時系列）→ 安定押下/離し状態（4bit）へ変換する状態機械。
   - しきい値・連続一致サンプル数は定数化（マジックナンバー回避）。
   - チャタリング（bounce）→ 単一の安定遷移、押しっぱなし → 非連打（状態は保持、再イベント化しない）を保証。
   - CH32V003 SRAM 2KB を意識し状態は最小（各SW数バイト程度）に。
3. **STATEマッピング**: 確定4bit を `b0..b3` の順で I2C STATE `[0]` の下位4bitへ反映できる純粋関数/アクセサを用意（上位4bitは 0 埋め）。実際のレジスタ結線は M-004 で行うため、本タスクは「確定状態を提供する」ところまで。
4. `src/main.c`: 走査→デバウンス→状態更新をメインループに結線（SW→STATE反映が数ms以内になる走査周期）。
5. ホストテスト(`test/`): `switches_debounce.c` のみを対象に（`switches.c` は GPIO依存のため含めない）、以下を網羅:
   - 連続チャタリング入力 → 単一の安定遷移。
   - 押しっぱなし → 非連打（状態保持、追加イベントなし）。
   - 複数SW同時押し/独立トグル。
   - 4bit→STATE下位4bitのビット順（`b0=PGM1×SRC1 … b3=PGM2×SRC2`）が正しいこと。
   - Makefile に `test_switches`（`../src/switches_debounce.c` を対象）を追加。
6. ホストテストを green にし、クロスビルド（環境があれば）を確認してコミット。

## 完了条件 / 検証コマンド
- `test/` のデバウンス・ビットマッピングのユニットテストが green（native gcc `-Werror`, `cd firmware/switcher-module/test && make`）。
- クロスビルドが成功（`ch32v003fun` 環境がある場合）。README にクロスビルド手順が反映済み。
- 確定SW状態が I2C `0x00 STATE` の下位4bit配置（親仕様書 §4.5, `b0..b3`）に一致する形で提供される。

## 技術的な補足 / レビュー観点
- **チャタリング**: しきい値・サンプル数を定数化。押しっぱなし・同時押しの扱いを明確化。
- **I/O とロジック分離**: デバウンスはホストテスト対象（`switches.c` の GPIO は含めない）。`.claude/review-patterns.md`「設計・責務分離」「並行性」「テスト」「命名」。
- **境界**: 割込みを使う場合はハンドラを短く保ち、メインループとの共有はフラグ/リングで安全化（本タスクはポーリング走査を基本としてよい）。

## 参照
- 仕様: `docs/specs/switcher-module-firmware.md` §2.1, §5 / `docs/specs/00-system-overview.md` §4.5
- 既存パターン: `firmware/pico2w-controller/src/buttons_debounce.c`, `test/test_buttons.c`
