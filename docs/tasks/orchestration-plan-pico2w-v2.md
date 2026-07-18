# 全体調整計画書: Pico 2W マスターコントローラー ファームウェア（改訂 v2 / I2C集約 + USB-HID）

- **対象仕様書**: [`docs/specs/pico2w-controller-firmware.md`](../specs/pico2w-controller-firmware.md)（改訂: 2026-07-18／親: [`docs/specs/00-system-overview.md`](../specs/00-system-overview.md) §4.1/§4.5/§4.6）
- **策定日**: 2026-07-18
- **Epicブランチ**: `feature/epic-pico2w-v2`
- **最大並列数**: 1（仕様書 §6 「claude: 最大並列 1」に準拠 → 単一レーンの直列実行）
- **実装エージェント(agent_cli)**: `sonnet`（レビュー/オーケストレーションは Opus `task-planner2`）
- **アプローチ**: 既存 `firmware/pico2w-controller/`（実装済み）に対する **REUSE / 増分差分（INCREMENTAL-DIFF）**。ゼロから作り直さず、スキャフォールド（ボード=`pico2_w`, TinyUSB, Unique Board ID, ホストテスト土台）を流用しつつ、役割縮小に合わせて**削除**と**置換**を行う。

> ⚠️ **本計画は既存 Pico計画（`orchestration-plan-pico2w-firmware.md`, Epic `feature/epic-pico2w-firmware`）の後継改訂**です。旧計画の `agent-P-*`（P-001〜P-004）はスキャフォールド以外が本改訂で無効化されるため、新タスクは名前空間を分けた **`agent-P2-*`** で管理します。PC側の `agent-A/B-*`・Web側 `agent-W-*` とも衝突しません。
> ⚠️ **`/start-all-tasks` は `docs/tasks/orchestration-plan.md`（単数）のみを参照**します。本コンポーネントは下記 §4 のとおり `./scripts/manage-screen.sh start <タスク名>` で**個別・直列起動**してください（最大並列1のため直列実行が要件そのもの）。

---

## 1. 仕様改訂サマリと差分方針（重要）

親仕様書 §2.1 / §7・個別仕様書 §7 の改訂により、Pico 2W の役割が**縮小**された。既存コードに対する扱いは以下のとおり。

| 対象（既存コード） | 改訂後の扱い | 根拠 |
| --- | --- | --- |
| `src/buttons.{c,h}`, `src/buttons_debounce.c` + `test/test_buttons.c` | ⛔ **削除（RETIRE）**。物理キーマトリクス走査は CH32V003 モジュール側へ移管（Picoはローカルマトリクスを走査しない）。 | 個別 §7 / 親 §2.1 |
| `src/ddc_tally.{c,h}`, `src/ddc_tally_decode.c` + `test/test_ddc_tally.c` | ⛔ **削除（RETIRE）**。DDCタリー抽出は廃止（タリーは外部デバイス `wireless-tally.md` が担当）。 | 個別 §1/§7 / 親 §2.1/§7 |
| `src/usb_link.{c,h}`（CDC JSON行送出） | 🔁 **置換（REPLACE）**。ベンダー定義 **USB-HID**（入力レポート `0x01`＝集約状態、出力レポート `0x02`＝バックライト）へ。構造・パターンは流用。 | 個別 §2.2/§2.3 / 親 §4.1 |
| `CMakeLists.txt`, `include/config.h`, `src/main.c`, `src/board_id.c`, `src/config.c` | ✅ **流用（REUSE）**。ボード/TinyUSB/Unique ID/`get_controller_id()`/ホストテスト土台は維持。ピン・ソース一覧・ループ結線を差分更新。 | 個別 §3 |
| `test/`（native gcc `-Werror` 土台） | ✅ **流用・拡張（REUSE）**。純粋ロジック（レポートパッキング/集約/バックライト分配/設定シリアライズ）を新テストで検証。button/ddc テストは削除。 | 個別 §3/§5 |
| 新規: I2Cマスター（`0x30`..`0x37` ポーリング） | ➕ **新規追加**。STATE(`0x00`)読取・`module_present`・タイムアウト/スキップ。 | 親 §4.5 |
| §2.5 Wi-Fi/BT ワイヤレス制御チャネル | ➕ 最終フェーズで**オプション実装**（P2-003）。未設定時は無効。 | 個別 §2.5 / 親 §4.6 |

---

## 2. アーキテクチャ方針とタスク分割

単一ファームウェアバイナリ（`firmware/pico2w-controller/`）を、Pico SDK(C/C++) で構築する。最大並列1のため**直列フェーズ**で1タスクずつ積み上げる。ロジック純粋部（HIDレポートのパッキング・モジュール状態集約・バックライト分配・設定シリアライズ）は**I/Oから分離**して**ホスト(native gcc)でユニットテスト可能**にする方針を、既存スキャフォールドの `buttons_debounce.c`／`ddc_tally_decode.c` と同じ「純粋部 vs GPIO/USB依存部の分離」パターンで踏襲する。

```text
firmware/pico2w-controller/            （★=REUSE, ○=新規/置換, ⛔=削除）
├── CMakeLists.txt            ★ ボード=pico2_w, TinyUSB。ソース一覧を差分更新（CYW43はP2-003で有効化）
├── pico_sdk_import.cmake     ★ 流用
├── include/config.h          ★ 差分: MAX_MODULES/I2Cピン・アドレス/HIDレポート定数を追加、button/DDC定義を撤去
├── src/main.c                ★ 差分: 走査→送信の結線を「I2Cポーリング→HID集約」へ差し替え
├── src/config.c              ★ 流用: get_controller_id() 維持（ピン配列はI2C系へ更新）
├── src/board_id.c            ★ 流用: get_unique_board_id()（HIDシリアル/controller_id 提示に使用）
├── src/i2c_modules.{c,h}     ○ [P2-001] I2Cマスター: 0x30..0x37 ポーリング, STATE読取, present/timeout
├── src/state_agg.{c,h}       ○ [P2-001] 純粋: モジュール状態→入力レポート0x01 パッキング（ホストテスト対象）
├── src/usb_hid.{c,h}         ○ [P2-001/002] TinyUSBベンダーHID: 入力0x01送出 / 出力0x02受領 / feature設定
├── src/backlight.{c,h}       ○ [P2-002] 純粋: 出力0x02→module別RGB分配 + I2C 0x10 書込キュー（純粋部テスト）
├── src/settings.{c,h}        ○ [P2-002] 設定シリアライズ/フラッシュ永続化（純粋部=シリアライズをテスト）
├── src/wireless.{c,h}        ○ [P2-003] (opt) CYW43+lwIP 代替制御チャネル（未設定時は無効）
├── src/buttons.{c,h}         ⛔ 削除（モジュール側へ移管）
├── src/buttons_debounce.c    ⛔ 削除
├── src/ddc_tally.{c,h}       ⛔ 削除（DDCタリー廃止）
├── src/ddc_tally_decode.c    ⛔ 削除
├── src/usb_link.{c,h}        ⛔ 置換（usb_hid.{c,h} へ）
└── test/                     ★ 流用・拡張: test_state_agg / test_backlight / test_settings 追加, test_buttons/test_ddc_tally 削除
```

---

## 3. 依存関係と実行スケジュール（直列）

```text
agent-P2-001-refactor-i2c-hid        （ゲート: 削除整理 + I2C集約 + HID入力レポート0x01）
        ▼
agent-P2-002-backlight-settings      （HID出力0x02→I2C配布 + 設定フラッシュ永続化/feature）
        ▼
agent-P2-003-wireless-optional  [opt]（CYW43 Wi-Fi/BT 代替制御チャネル, 未設定時無効）
```

| 順 | タスク | agent_cli | subtreeプレフィックス | 依存 |
| --- | --- | --- | --- | --- |
| 1 | `agent-P2-001-refactor-i2c-hid` | sonnet | `firmware/pico2w-controller/` | なし（既存スキャフォールドを前提に差分） |
| 2 | `agent-P2-002-backlight-settings` | sonnet | `firmware/pico2w-controller/` | P2-001 |
| 3 | `agent-P2-003-wireless-optional` | sonnet | `firmware/pico2w-controller/` | P2-001（opt, P2-002完了後が望ましい） |

> 単一レーン・単一ディレクトリのため subtree 分割は不要（Epicブランチ上で直列コミット）。各タスクは前タスク完了(`done`)を前提に同ディレクトリへ差分を積む。P2-001 が **HID/I2C の契約（レポート長・レジスタ・ヘッダIF）を確定**させ、後続はその契約に追記する。

---

## 4. 起動手順（直列）

```bash
git checkout -b feature/epic-pico2w-v2

# 直列に1つずつ起動し、done を確認してから次へ
./scripts/manage-screen.sh start agent-P2-001-refactor-i2c-hid
# （done後）
./scripts/manage-screen.sh start agent-P2-002-backlight-settings
# （done後, 任意）
./scripts/manage-screen.sh start agent-P2-003-wireless-optional
```

---

## 5. 検証方針（各タスク共通）

- **ホストテスト（主検証）**: I/O非依存の純粋ロジック（入力レポート0x01のパッキング・モジュール集約・`module_present`ビットマップ・`seq`ローテート・出力0x02のRGB分配・設定シリアライズ）は既存 `test/`（native gcc, `-std=c11 -Wall -Wextra -Werror`）で**必ず green**にする。GPIO/I2C/USB依存の実体は含めない（`buttons_debounce.c` 方式を踏襲）。
- **クロスビルド**: `PICO_SDK_PATH` を設定し `cmake -B build -DPICO_BOARD=pico2_w && cmake --build build` が成功すること（環境があれば `.uf2` 生成）。SDK/ARMツールチェーン非導入環境では最低限ホストテストを green にし、クロスビルド手順を README に明記（実機ビルドは要環境）。
- **プロトコル適合**: HID 入力`0x01`/出力`0x02` は親仕様書 §4.1 のオフセット・長さに、I2C STATE(`0x00`)/BACKLIGHT(`0x10`)/INFO(`0xF0`) は §4.5 のレジスタマップに厳密一致させる。設定投入は §4.6（HIDフィーチャーレポート/ワイヤレス制御チャネル）に準拠。
- **削除の完全性**: 撤去対象（buttons matrix, ddc_tally）は `CMakeLists.txt`・`test/Makefile`・`main.c`・`config.h` の参照も含めて残骸を残さない（ビルドが壊れないこと）。
- レビュー観点: [`.claude/review-patterns.md`](../../.claude/review-patterns.md)（リソース管理・並行性・命名・境界・設計/責務分離）＋仕様適合を Opus 親が検証。

## 6. 個別タスク指示書一覧

- [`agent-P2-001-refactor-i2c-hid.md`](./agent-P2-001-refactor-i2c-hid.md)
- [`agent-P2-002-backlight-settings.md`](./agent-P2-002-backlight-settings.md)
- [`agent-P2-003-wireless-optional.md`](./agent-P2-003-wireless-optional.md)（オプション）
