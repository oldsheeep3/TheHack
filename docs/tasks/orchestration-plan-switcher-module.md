# 全体調整計画書: スイッチングモジュール内マイコン ファームウェア（CH32V003）

- **対象仕様書**: [`docs/specs/switcher-module-firmware.md`](../specs/switcher-module-firmware.md)（親: [`docs/specs/00-system-overview.md`](../specs/00-system-overview.md)）
- **策定日**: 2026-07-18
- **Epicブランチ**: `feature/epic-switcher-module`
- **最大並列数**: 1（仕様書 §7 「claude: 最大並列 1」に準拠 → 単一レーンの直列実行）
- **実装エージェント(agent_cli)**: `sonnet`（レビュー/オーケストレーションは Opus `task-planner2`）

> ⚠️ **本計画は PC常駐アプリ計画（`orchestration-plan.md`）・Pico 2W計画（`orchestration-plan-pico2w-firmware.md`）とは別Epic・別コンポーネント**です。タスク名は `agent-M-*` で名前空間を分離しており、PC側 `agent-A/B-*`・Pico側 `agent-P-*`・スマホ側 `agent-W-*` と衝突しません。
> ⚠️ **`/start-all-tasks` は `docs/tasks/orchestration-plan.md`（単数）のみを参照**します。本コンポーネントは下記 §4 のとおり `./scripts/manage-screen.sh start <タスク名>` で**個別・直列起動**してください（最大並列1のため直列実行が要件そのもの。Pico 2W計画と同じ運用）。

---

## 1. スコープと除外（重要）

| 項目 | 扱い |
| --- | --- |
| §2.1 スイッチ入力スキャン + デバウンス（純粋状態機械） | ✅ 本イテレーションで実装（M-002） |
| §2.2 アナログVR×2 ADC読取 + スケーリング（純粋） | ✅ 本イテレーションで実装（M-003） |
| §2.3 SK6812×4 バックライト駆動 + RGB→GRBフレーム（純粋） | ✅ 本イテレーションで実装（M-003） |
| §2.4 I2Cスレーブ + レジスタマップ（`0x00`/`0x10`/`0xF0`） | ✅ 本イテレーションで実装（M-004） |
| ストラップ/抵抗IDによる `get_module_index()` | ✅ M-001 で土台、実機ストラップ配線はHW依存 |

---

## 2. アーキテクチャ方針とタスク分割

単一ファームウェアバイナリ（`firmware/switcher-module/`）を、`ch32v003fun`（軽量・オープン、仕様書 §3 推奨）で構築する。最大並列1のため**直列フェーズ**で1タスクずつ積み上げる。

**設計の要**は既存 `firmware/pico2w-controller/` で実証済みの分離パターンの踏襲である:

- **I/O（GPIO/ADC/SPI/I2C ペリフェラル依存）と純粋ロジックを別ファイルに分離**する。
- 純粋ロジック（`switches_debounce.c` / `adc_scale.c` / `sk6812_frame.c` / レジスタ読み書きの純粋部）は **ホストの native gcc（`-Werror`）でユニットテスト可能**にする（`test/` に最小 Makefile）。
- CH32V003 は **SRAM 2KB** の厳しい制約があるため、バッファ・スタックを最小化し、割込みハンドラは短く保つ（スキャン/ADC/LED更新はメインループ、割込みとの共有はフラグ/リングで安全化）。

```text
firmware/switcher-module/
├── ch32v003fun/            (submodule or vendored build glue)
├── CMakeLists.txt / Makefile  ch32v003fun ビルド定義
├── include/module_config.h    SW/VR/LEDピン・I2Cベースアドレス(0x30)・get_module_index()
├── src/main.c                  初期化 + メインループ（各モジュール統合）
├── src/switches.{c,h}          SWマトリクス走査(I/O) + switches_debounce.c（純粋）
├── src/adc.{c,h}               VR読取(I/O) + adc_scale.c（純粋: 0..255スケール）
├── src/backlight.{c,h}         SK6812駆動(I/O) + sk6812_frame.c（純粋: RGB→GRB）
├── src/i2c_slave.{c,h}         I2Cスレーブ + レジスタマップ(0x00/0x10/0xF0)
└── test/                       ホスト(native)ユニットテスト
```

### タスク境界の考え方
- **M-001 のビルド基盤・`module_config.h`（ピン/アドレス/`get_module_index()`）・ディレクトリ構成・ホストテスト土台が後続タスクの契約**。以降のタスクはこの土台へ追記する。
- 純粋ロジックの関数シグネチャは各担当タスクで確定させ、I2Cレジスタの意味（親仕様書 §4.5）とバイト配置を厳守する。

---

## 3. 依存関係と実行スケジュール（直列）

```text
agent-M-001-module-scaffold          （ゲート: ビルド基盤・pin/config・module番号・ホストテスト土台）
        ▼
agent-M-002-switch-input             （SW×4走査 + 純粋デバウンス状態機械 → STATE下位4bit）
        ▼
agent-M-003-analog-backlight         （VR×2 ADC + 純粋スケール / SK6812×4駆動 + 純粋GRBフレーム）
        ▼
agent-M-004-i2c-slave-integration    （I2Cスレーブ + レジスタマップ結線 + メインループ統合）
```

| 順 | フェーズ | タスク | agent_cli | subtreeプレフィックス | 依存 |
| --- | --- | --- | --- | --- | --- |
| 1 | 0 (直列ゲート) | `agent-M-001-module-scaffold` | sonnet | `firmware/switcher-module/` | なし |
| 2 | 1 | `agent-M-002-switch-input` | sonnet | `firmware/switcher-module/` | M-001 |
| 3 | 2 | `agent-M-003-analog-backlight` | sonnet | `firmware/switcher-module/` | M-001 |
| 4 | 3 (直列統合) | `agent-M-004-i2c-slave-integration` | sonnet | `firmware/switcher-module/` | M-001, M-002, M-003 |

> 単一レーン・単一ディレクトリのため subtree 分割は不要（Epicブランチ上で直列コミット）。各タスクは前タスク完了(`done`)を前提に同ディレクトリへ追記する。M-004 は M-002/M-003 の成果（SW状態・VR値・バックライト出力）を I2C レジスタへ結線する統合ゲート。

---

## 4. 起動手順（直列）

```bash
git checkout -b feature/epic-switcher-module

# 直列に1つずつ起動し、done を確認してから次へ
./scripts/manage-screen.sh start agent-M-001-module-scaffold
# （done後）
./scripts/manage-screen.sh start agent-M-002-switch-input
# （done後）
./scripts/manage-screen.sh start agent-M-003-analog-backlight
# （done後）
./scripts/manage-screen.sh start agent-M-004-i2c-slave-integration
```

- `/start-all-tasks` は `orchestration-plan.md`（単数）のみを対象とするため、本コンポーネントには使用しない。上記のとおり個別・直列で起動する。

---

## 5. 検証方針（各タスク共通）

- **ホストテスト（最優先）**: I/O非依存の純粋ロジック（デバウンス状態機械 / ADCスケーリング / RGB→GRB・SK6812フレーム組み立て / レジスタ読み書きの純粋部）を `test/` で native ビルド（`gcc -std=c11 -Wall -Wextra -Werror`）してユニットテスト。既存 `firmware/pico2w-controller/test/Makefile` と同方式で、GPIO/ペリフェラル依存の `.c` は含めず純粋部のみを対象にする。
- **クロスビルド**: `ch32v003fun` のツールチェーン（RISC-V gcc）を用いたビルド手順を README に明記。SDK/ツールチェーン導入環境ではビルド確認（可能なら `.bin`/`.elf` 生成）。**未導入環境では最低限ホストテストを green** にする（仕様書 §5）。
- **I2Cレジスタ適合**: `0x00 STATE`(read,3B) / `0x10 BACKLIGHT`(write,12B) / `0xF0 INFO`(read,4B) のバイト配置・方向・意味が親仕様書 §4.5 と一致すること（純粋部のホストテストで検証）。
- **メモリ制約**: SRAM 2KB を意識し、バッファ/スタックの肥大を避ける（レビュー観点）。
- レビュー観点: [`.claude/review-patterns.md`](../../.claude/review-patterns.md)（設計・責務分離 / 並行性・割込み / リソース管理 / 命名・境界）＋仕様適合を Opus 親が検証。

## 6. 個別タスク指示書一覧

- [`agent-M-001-module-scaffold.md`](./agent-M-001-module-scaffold.md)
- [`agent-M-002-switch-input.md`](./agent-M-002-switch-input.md)
- [`agent-M-003-analog-backlight.md`](./agent-M-003-analog-backlight.md)
- [`agent-M-004-i2c-slave-integration.md`](./agent-M-004-i2c-slave-integration.md)
