---
name: agent-P2-002-backlight-settings
status: done
pid: 379068
agent_cli: sonnet
---

# 実装指示書: HID出力レポート0x02→I2Cバックライト配布 + 設定フラッシュ永続化

## 概要
P2-001 が確立した USB-HID / I2C 基盤に、(1) **HID出力レポート `0x02`**（`module_index` + 4灯RGB, 親仕様書 §4.1）を受領して対応モジュールの I2C `0x10 BACKLIGHT`（12バイト）へ配布する経路と、(2) **設定の不揮発（フラッシュ）永続化**（`controller_id`・モジュール割付ヒント・ネットワーク資格情報等）を、**PC経由（HIDフィーチャーレポート）**で投入・保存する経路（親仕様書 §4.6）を追加する。純粋部（RGB分配・設定シリアライズ）はホストテストする。

## 前提条件（依存タスク）
- `agent-P2-001-refactor-i2c-hid` が `done`（I2Cマスター基盤・`usb_hid.{c,h}`・`config.h` の `MAX_MODULES`/レジスタ/レポート定数・ホストテスト土台が確定）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `firmware/pico2w-controller/`（主に `src/backlight.{c,h}`, `src/settings.{c,h}`, `src/usb_hid.{c,h}` への出力/feature追加, `src/i2c_modules.{c,h}` への write追加, `src/main.c` 結線, `include/config.h`, `test/`）。

## 実装ステップ
1. **`src/usb_hid.{c,h}` 拡張**: HIDディスクリプタに**出力レポート `0x02`**（`module_index`(1) + 4灯RGB(12) = 13バイト, §4.1）を追加。TinyUSB の出力/SET_REPORT コールバックで受領し、後段キューへ渡す。受領で**メインループ/HID受信をブロックしない**（受領キューを設ける, §2.3）。
2. **`src/backlight.{c,h}`（新規, 純粋部+I/O部を分離）**:
   - 純粋部（ホストテスト対象）: 出力レポート`0x02`のバイト列 → `module_index` と 4×RGB（各3バイト）へのパース/検証（範囲外`module_index`の棄却、RGBバイト順の保持）。SK6812 の GRB 変換は**モジュール側で行う**ため Pico は RGB 順のまま渡す（§4.1/§4.5 注記）。
   - I/O部: パース済みRGBを I2C `0x10 BACKLIGHT`（12バイト write）で対象アドレス `0x30 + module_index` へ書き込む。
3. **`src/i2c_modules.{c,h}` に write 経路追加**: `0x10 BACKLIGHT` への 12バイト write を提供。STATE ポーリングと同一バス上で衝突しないようメインループでの逐次処理（キュー消費）にする。書込タイムアウト/不通時はスキップ（集約を止めない）。
4. **`src/settings.{c,h}`（新規, 純粋部+I/O部を分離）**:
   - 純粋部（ホストテスト対象）: 設定構造体（`controller_id`, モジュール割付ヒント配列, ネットワーク資格情報=SSID/PASS等）の**シリアライズ/デシリアライズ**とバリデーション、破損/未初期化検出（マジック/バージョン/CRC等）。
   - I/O部: RP2350 フラッシュへの保存/読出（`hardware/flash` によるセクタ消去+書込。ラストセクタ運用等、実装方針をコメントで明記）。
   - 投入経路: **HIDフィーチャーレポート**で PC から設定を受領し、検証→フラッシュ保存（設定の正はPC, §4.6）。`get_controller_id()` は保存設定を優先し、未設定時はビルド時 `CONTROLLER_ROLE` にフォールバックする方針を明記。
5. **`src/main.c` 結線**: 出力`0x02`受領→キュー→I2C配布、起動時の設定ロード、feature受領→保存を結線。バックライト書込がSTATE集約レイテンシ（数ms）を阻害しないことを確認。
6. **ホストテスト（拡張）**: `test/test_backlight.c`（RGBパース・範囲外`module_index`棄却・バイト順）と `test/test_settings.c`（シリアライズ往復・破損検出・controller_idフォールバック）を追加し `test/Makefile` の `TESTS` に登録（純粋部のみ対象, フラッシュ/USB依存は含めない）。
7. **README 差分**: 出力`0x02`→I2C `0x10` 配布フロー、フィーチャーレポートによる設定投入・フラッシュ永続化、実機手動確認手順（PCからRGB送出→対象モジュール点灯、設定投入→再起動後保持）を追記。
8. クロスビルド（環境があれば）／ホストテストを通してコミット。

## 完了条件 / 検証コマンド
- `cd firmware/pico2w-controller/test && make` が **green**（`test_backlight`, `test_settings` を含む。native gcc `-Werror`）。
- 出力レポート `0x02` のレイアウト（`module_index` + 4×RGB=12）と I2C `0x10 BACKLIGHT`（12バイト write）が親仕様書 §4.1/§4.5 に一致。
- 設定投入がHIDフィーチャーレポート経由で機能し、フラッシュへ永続化・再起動後に復元される（純粋部はテスト、I/O部は手動確認手順をREADMEに明記）。
- `cmake -B build -DPICO_BOARD=pico2_w && cmake --build build` が成功（SDK環境がある場合、`.uf2` 生成）。

## 技術的な補足 / レビュー観点
- **並行性/境界**: 出力受領キューと STATE ポーリングの同一I2Cバス共有を直列化し、HID受信・集約送出をブロックしない（`.claude/review-patterns.md`「並行性」「境界」）。
- **セキュリティ**: ネットワーク資格情報をコード/コミット履歴に混入させない。フラッシュ保存のみとし、ログ/シリアルへ平文出力しない（`.claude/review-patterns.md`「セキュリティ」）。
- **フラッシュ耐久性/整合性**: セクタ消去中の割込み/実行位置（XIP）に注意。マジック+バージョン+CRCで破損/旧版を安全に扱う。
- **REUSE**: 純粋部（backlight/settings シリアライズ）を必ずホストテスト側に分離（P2-001 の `state_agg.c` と同方針）。

## 参照
- 仕様: `docs/specs/pico2w-controller-firmware.md` §2.3, §2.4 / `docs/specs/00-system-overview.md` §4.1, §4.5, §4.6
- 前タスク: `docs/tasks/agent-P2-001-refactor-i2c-hid.md`
- レビュー基準: `.claude/review-patterns.md`
