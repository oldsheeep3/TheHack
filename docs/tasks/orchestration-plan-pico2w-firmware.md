# 全体調整計画書: Pico 2W コントローラー & タリー抽出ファームウェア

- **対象仕様書**: [`docs/specs/pico2w-controller-firmware.md`](../specs/pico2w-controller-firmware.md)（親: [`docs/specs/00-system-overview.md`](../specs/00-system-overview.md)）
- **策定日**: 2026-07-17
- **Epicブランチ**: `feature/epic-pico2w-firmware`
- **最大並列数**: 1（仕様書 §6 「claude: 最大並列 1」に準拠 → 単一レーンの直列実行）
- **実装エージェント(agent_cli)**: `sonnet`（レビュー/オーケストレーションは Opus `task-planner2`）

> ⚠️ **本計画は PC常駐アプリ計画（`orchestration-plan.md`）とは別Epic・別コンポーネント**です。タスク名は `agent-P-*` で名前空間を分離しており、PC側の `agent-A/B-*` と衝突しません。
> ⚠️ **`/start-all-tasks` は `docs/tasks/orchestration-plan.md`（単数）のみを参照**します。本コンポーネントは下記 §4 のとおり `manage-screen.sh start <タスク名>` で**個別・直列起動**してください（最大並列1のため直列実行が要件そのもの）。

---

## 1. スコープと除外（重要）

| 項目 | 扱い |
| --- | --- |
| §2.1 物理キー入力スキャン + USB送信 | ✅ 本イテレーションで実装 |
| §2.3 HDMI DDCタリー抽出 + LED + USB通知 | ✅ 本イテレーションで実装 |
| §2.4 Wi-Fi経由タリー受信/接続（検討） | ➕ 最終フェーズで**オプション実装**（P-004） |
| §2.2 モジュール内マイコン統括（内部バス） | ⛔ **TBD/後日**につき本計画から除外。仕様確定後に `agent-P-005-*` を追加予定 |

---

## 2. アーキテクチャ方針とタスク分割

単一ファームウェアバイナリ（`firmware/pico2w-controller/`）を、Pico SDK(C/C++) で構築する。最大並列1のため**直列フェーズ**で1タスクずつ積み上げる。ロジック純粋部（デバウンス・タリーデコード等）は**ホストでユニットテスト可能**なように I/O から分離する。

```text
firmware/pico2w-controller/
├── CMakeLists.txt            ボード=pico2_w, TinyUSB/CYW43 有効
├── pico_sdk_import.cmake
├── include/config.h          ピン定義・ボタン数・controller_id 設定
├── src/main.c                メインループ（各モジュール統合）
├── src/buttons.{c,h}         キーマトリクス走査+デバウンス（純粋ロジック分離）
├── src/usb_link.{c,h}        TinyUSB HID/CDC 送信（§4.1 ButtonEvent）
├── src/ddc_tally.{c,h}       I2Cスニッフ + Blackmagicタリーデコード + LED
├── src/wifi_tally.{c,h}      (opt) CYW43+lwIP でタリーUDP受信
└── test/                     ホスト側ユニットテスト（デバウンス/デコード）
```

---

## 3. 依存関係と実行スケジュール（直列）

```text
agent-P-001-firmware-scaffold        （ゲート: ビルド基盤・pin/config・controller識別）
        ▼
agent-P-002-button-usb-input         （キー走査 + USB HID/CDC 送信）
        ▼
agent-P-003-ddc-tally-extraction     （I2Cスニッフ + タリーデコード + LED + USB通知）
        ▼
agent-P-004-wifi-tally-receive  [opt]（Wi-Fiタリー受信 → LED）
```

| 順 | タスク | agent_cli | subtreeプレフィックス | 依存 |
| --- | --- | --- | --- | --- |
| 1 | `agent-P-001-firmware-scaffold` | sonnet | `firmware/pico2w-controller/` | なし |
| 2 | `agent-P-002-button-usb-input` | sonnet | `firmware/pico2w-controller/` | P-001 |
| 3 | `agent-P-003-ddc-tally-extraction` | sonnet | `firmware/pico2w-controller/` | P-001 |
| 4 | `agent-P-004-wifi-tally-receive` | sonnet | `firmware/pico2w-controller/` | P-001（opt） |

> 単一レーン・単一ディレクトリのため subtree 分割は不要（Epicブランチ上で直列コミット）。各タスクは前タスク完了(`done`)を前提に同ディレクトリへ追記する。

---

## 4. 起動手順（直列）

```bash
git checkout -b feature/epic-pico2w-firmware

# 直列に1つずつ起動し、done を確認してから次へ
./scripts/manage-screen.sh start agent-P-001-firmware-scaffold
# （done後）
./scripts/manage-screen.sh start agent-P-002-button-usb-input
# … 以降 P-003, P-004 と続ける
```

---

## 5. 検証方針（各タスク共通）

- **クロスビルド**: `PICO_SDK_PATH` を設定し `cmake -B build -DPICO_BOARD=pico2_w && cmake --build build` が成功すること（`.uf2` 生成）。
- **ホストテスト**: I/O非依存の純粋ロジック（デバウンス状態機械・Blackmagicタリーデコード）は `test/` で native ビルドしてユニットテスト。
- **ツールチェーン非導入環境**: SDK/ARMツールチェーンが無い場合は最低限ホストテストを green にし、クロスビルド手順を README に明記（実機ビルドは要環境）。
- レビュー観点: [`.claude/review-patterns.md`](../../.claude/review-patterns.md)（リソース管理・並行性・命名・境界）＋仕様適合を Opus 親が検証。

## 6. 個別タスク指示書一覧

- [`agent-P-001-firmware-scaffold.md`](./agent-P-001-firmware-scaffold.md)
- [`agent-P-002-button-usb-input.md`](./agent-P-002-button-usb-input.md)
- [`agent-P-003-ddc-tally-extraction.md`](./agent-P-003-ddc-tally-extraction.md)
- [`agent-P-004-wifi-tally-receive.md`](./agent-P-004-wifi-tally-receive.md)（オプション）
