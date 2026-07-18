---
name: agent-P2-001-refactor-i2c-hid
status: done
pid: 349511
agent_cli: sonnet
---

# 実装指示書: 役割縮小リファクタ（DDC/ボタン撤去）+ I2C集約 + USB-HID入力レポート0x01

## 概要
既存 `firmware/pico2w-controller/`（実装済み）を改訂仕様に合わせて**差分改修**する。(1) 廃止機能（**物理キーマトリクス走査**と **DDCタリー抽出**）を関連ファイル・ビルド定義・テストごと**削除**し、(2) Pico を **I2Cマスター**として各モジュール `0x30`..`0x37` を定期ポーリングして `0x00 STATE`（SW状態/VR×2）を集約し、(3) 集約結果を**ベンダー定義 USB-HID の入力レポート `0x01`**（親仕様書 §4.1）でPCへ送出する。既存スキャフォールド（ボード=`pico2_w`, TinyUSB, `get_controller_id()`/Unique ID, ホストテスト土台）は**流用**し、集約とレポートパッキングの純粋ロジックはホストテストする。

> 本タスクが **HID/I2C の契約（レポート長・レジスタ・公開ヘッダIF）を確定**させるゲート。後続 P2-002/P2-003 はこの契約に追記する。

## 前提条件（依存タスク）
- なし（既存スキャフォールド `firmware/pico2w-controller/` を前提に差分改修する）。
- 前提として流用する既存物: `CMakeLists.txt`, `include/config.h`, `src/main.c`, `src/config.c`（`get_controller_id()`）, `src/board_id.c`（`get_unique_board_id()`）, `test/`（native gcc `-Werror` 土台）。

## 対象ディレクトリ（このタスクで作成/編集/削除してよい範囲）
- `firmware/pico2w-controller/` 配下のみ。

## 実装ステップ
1. **削除（RETIRE）**: 以下を削除し、参照も撤去する（ビルドが壊れないこと）。
   - `src/buttons.{c,h}`, `src/buttons_debounce.c`, `test/test_buttons.c`（キーマトリクスは CH32V003 側へ移管）。
   - `src/ddc_tally.{c,h}`, `src/ddc_tally_decode.c`, `test/test_ddc_tally.c`（DDCタリー抽出は廃止）。
   - `src/usb_link.{c,h}`（CDC JSON。本タスクの `src/usb_hid.{c,h}` へ置換）。
   - `CMakeLists.txt` の `add_executable` ソース一覧、`test/Makefile` の `TESTS`/ルール、`include/config.h` のボタン行列/DDC/タリーLED/カメラ定数、`src/main.c` の該当結線を撤去。
2. **config.h 差分（REUSE）**: `get_controller_id()`/Unique ID 契約は維持しつつ、新定数を集約:
   - `MAX_MODULES 8`（親仕様書 §4.0。HIDレポート長・アドレス空間の単一ソース）。SW数=4/モジュール, VR数=2/モジュール も定数化。
   - I2C マスターピン（ハードウェアI2C: 例 `MODULE_I2C_SDA_PIN`/`MODULE_I2C_SCL_PIN`, `MODULE_I2C_INSTANCE`, `MODULE_I2C_BAUD` 100k〜400kHz）。
   - モジュールI2Cアドレス: ベース `MODULE_I2C_ADDR_BASE 0x30`（`0x30 + n`）。
   - I2Cレジスタ（§4.5）: `MODULE_REG_STATE 0x00`(read 3), `MODULE_REG_BACKLIGHT 0x10`(write 12), `MODULE_REG_INFO 0xF0`(read 4)。
   - HIDレポートID: `HID_REPORT_ID_STATE_IN 0x01`, `HID_REPORT_ID_BACKLIGHT_OUT 0x02`。入力レポート長は §4.1 に従い算出（`1 + MAX_MODULES + 2*MAX_MODULES + 1`）を定数化。
   - ポーリング/集約周期（1kHz目安）、VRデッドバンド閾値・間引きレートを定数化（マジックナンバー回避）。
3. **`src/i2c_modules.{c,h}`（新規, GPIO/I2C依存）**: `0x30`..`0x37` を定期ポーリング。
   - 各アドレスへ `0x00 STATE` を read（3バイト: SW(4bit)/VR_SRC1/VR_SRC2）。ACK有無で `module_present` ビットマップを構築。
   - 未接続/不通は**タイムアウトしてスキップ**（1モジュールの不通が集約全体を止めない, 非機能 §4）。SDK I2C のタイムアウトAPIを使用。
   - 取得結果を「モジュール状態配列（present/SW/VR×2）」の共有構造体へ格納する公開IFを提供。
4. **`src/state_agg.{c,h}`（新規, I/O非依存=ホストテスト対象）**: モジュール状態配列 → **入力レポート `0x01` バイト列**へパッキングする純粋関数を実装。
   - レイアウト（親仕様書 §4.1）: `[0]`=`module_present`ビットマップ / `[1..MAX_MODULES]`=各SW状態(下位4bit) / 続く`2*MAX_MODULES`=各VR(SRC1,SRC2 各0..255) / 末尾1=`seq`(0-255ローテート)。
   - VRデッドバンド/間引き適用の純粋判定（前回値との差分で「送出すべきか」を返す）も分離して実装。
5. **`src/usb_hid.{c,h}`（新規, TinyUSB依存）**: ベンダー定義 HID デバイスとして列挙。
   - HIDレポートディスクリプタに入力レポート`0x01`（長=§4.1算出値）を定義（出力`0x02`・feature は P2-002 で追加。ここでは拡張余地をコメントで明示）。
   - **状態変化時に即時送出＋一定レートで定期送出**（取りこぼし対策, §2.2）。USB未接続時の送出方針（ドロップ/ブロックしない）を明記。
   - `controller_id`（main/sub）は HIDシリアル文字列（`get_unique_board_id()`＋`get_controller_id()`）で提示。
6. **`src/main.c` 結線（REUSE）**: 旧「走査→CDC送信」を「I2Cポーリング→集約→HID `0x01` 送出」へ差し替え。集約周期1kHz目安、SW状態→HID送出レイテンシ数ms以内（§4/非機能）。
7. **ホストテスト（REUSE/拡張）**: `test/test_state_agg.c` を追加し、`test/Makefile` の `TESTS` に登録（`state_agg.c` のみを対象, I2C/USB依存は含めない）。全モジュール接続/一部欠損の`module_present`、SW/VRのバイト位置、`seq`ローテート、VRデッドバンド判定を網羅。旧 `test_buttons`/`test_ddc_tally` は削除。
8. **README 差分**: ディレクトリ構成表を更新（削除ファイルの記述除去・新ファイル追記）、HID/I2C の概要とビルド/ホストテスト手順、実機での手動確認手順（モジュール接続時の `module_present`／SW/VR反映確認）を記載。
9. クロスビルド（環境があれば `.uf2` 生成）確認、無ければホストテスト green を確認してコミット。

## 完了条件 / 検証コマンド
- `cd firmware/pico2w-controller/test && make` が **green**（`test_state_agg` を含む。native gcc `-Werror`）。
- 削除対象（buttons matrix / ddc_tally / usb_link）とその参照が完全に撤去され、`CMakeLists.txt`・`test/Makefile`・`main.c`・`config.h` にビルドを壊す残骸が無い。
- 入力レポート `0x01` のバイトレイアウトが親仕様書 §4.1（`module_present`/SW/VR/`seq`）に一致。I2C STATE(`0x00`) の 3バイト解釈が §4.5 に一致。
- `cmake -B build -DPICO_BOARD=pico2_w && cmake --build build` が成功（SDK環境がある場合、`.uf2` 生成）。無い場合はクロスビルド手順を README に明記。

## 技術的な補足 / レビュー観点
- **REUSE強調**: ゼロから書かず、既存スキャフォールドと「純粋部 vs I/O依存部の分離」パターン（旧 `buttons_debounce.c`／`ddc_tally_decode.c`）を踏襲する。純粋部（`state_agg.c`）を必ずホストテスト側に置く。
- **障害隔離**: 1モジュールのI2C不通が全体を止めないタイムアウト/スキップを明確化（`.claude/review-patterns.md`「境界」「並行性」）。
- **契約の安定性**: 本タスクのヘッダIF・レポート長・レジスタ定数が後続の契約。命名・レイアウトを安定させる。
- **境界/リソース**: USB切断中の送出ドロップ方針、I2Cバスエラー時のリカバリ、共有状態構造体の更新/読出の一貫性（メインループ単一コンテキスト前提か割込みかを明記）。

## 参照
- 仕様: `docs/specs/pico2w-controller-firmware.md` §1, §2.1, §2.2, §7 / `docs/specs/00-system-overview.md` §4.0, §4.1, §4.5
- 既存: `firmware/pico2w-controller/`（`CMakeLists.txt`, `include/config.h`, `src/main.c`, `src/config.c`, `src/board_id.c`, `test/`）
- レビュー基準: `.claude/review-patterns.md`
