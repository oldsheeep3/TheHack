---
name: agent-P-001-firmware-scaffold
status: planning
pid:
agent_cli: sonnet
---

# 実装指示書: ファームウェア基盤 & ビルドスキャフォールド（直列ゲート）

## 概要
Pico SDK(C/C++) による Pico 2W ファームウェアのビルド基盤を構築する。ボード `pico2_w`、TinyUSB・CYW43(将来)有効化、ピン/設定ヘッダ、controller識別（Unique Board ID / 設定）、ホストテスト用の分離レイヤの土台を用意する。

## 前提条件（依存タスク）
- なし（本タスクが全タスクの前提）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `firmware/pico2w-controller/` 配下。

## 実装ステップ
1. `CMakeLists.txt` と `pico_sdk_import.cmake` を作成。`set(PICO_BOARD pico2_w)`、`pico_enable_stdio_usb`、TinyUSB 有効化。CYW43 は将来（P-004）で有効化する前提でコメント。
2. `include/config.h` を作成し、ハード定義を集約:
   - ボタン行列ピン（行/列, 最大16ボタン想定）、タリーLEDピン（赤/緑）、I2C(DDC)ピン（例 GP4=SDA/GP5=SCL）。マジックナンバーを避け全て定数化。
   - `CONTROLLER_ID` の決定方式: RP2350 Unique Board ID 読み出しユーティリティ、または設定値でメイン/サブを区別できるようにする（`get_controller_id()` を用意）。
3. `src/main.c`: 初期化（stdio/USB/GPIO）とメインループの骨格。各モジュール（buttons/usb_link/ddc_tally）は後続タスクで追加される前提の空フック（宣言のみ or `__attribute__((weak))`）を置く。
4. **ホストテスト土台**: `test/` に native ビルド用の最小 CMake/Make を用意し、I/O非依存ロジックを単体テストできる構成にする（後続の debounce/tally decode 用）。
5. README（`firmware/pico2w-controller/README.md`）にビルド手順（`PICO_SDK_PATH`, `cmake -B build -DPICO_BOARD=pico2_w`, ホストテスト実行）を記載。
6. クロスビルド（環境があれば）で `.uf2` 生成を確認、無ければホストテスト土台のビルドを確認しコミット。

## 完了条件 / 検証コマンド
- `cmake -B build -DPICO_BOARD=pico2_w && cmake --build build` が成功（SDK環境がある場合、`.uf2` 生成）。
- `test/` の native ビルドが成功（プレースホルダテストが green）。
- `config.h` にピン/ボタン/LED/I2C/controller識別が定数化され、`get_controller_id()` が提供される。

## 技術的な補足 / レビュー観点
- **本タスクのピン定義・IF・ディレクトリ構成が後続タスクの契約**。命名・ヘッダ構成を安定させる。
- I/O とロジックを分離し、ロジックをホストでテスト可能にする方針を土台から徹底（`.claude/review-patterns.md`「設計・責務分離」「テスト」）。
- SDK/ツールチェーン未導入環境の前提を README に明記。

## 参照
- 仕様: `docs/specs/pico2w-controller-firmware.md` §1, §3, §5
