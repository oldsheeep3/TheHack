# 全体調整計画書: スマホ経由Web設定UI（ネットワーク中継 v2 改訂）

- **対象仕様書**: [`docs/specs/phone-web-bridge.md`](../specs/phone-web-bridge.md)（改訂 2026-07-18, 親: [`docs/specs/00-system-overview.md`](../specs/00-system-overview.md) §4.2/§4.3）
- **策定日**: 2026-07-18
- **Epicブランチ**: `feature/epic-phone-web-bridge-v2`
- **最大並列数**: 1（仕様書 §6 「claude: 最大並列 1」に準拠 → 単一レーンの直列実行）
- **実装エージェント(agent_cli)**: `sonnet`（レビュー/オーケストレーションは Opus `task-planner2`）

> ⚠️ 本計画は既存の `apps/phone-bridge/`（初版 Epic `feature/epic-phone-web-bridge` = タスク `agent-W-001`〜`W-003`, いずれも `done`）を**改訂・差分実装**するものです。ゼロからの再構築ではなく **REUSE / 増分DIFF** を基本方針とします。タスク名は初版と衝突しないよう `agent-W2-*` 名前空間を用います。
> ⚠️ `/start-all-tasks` は `docs/tasks/orchestration-plan.md`（単数）のみを参照します。本コンポーネントは §4 のとおり `./scripts/manage-screen.sh start <タスク名>` で**個別・直列起動**してください。

---

## 1. アーキテクチャ方針とタスク分割（REUSE / 増分DIFF）

既存の単一 Vite + React + TypeScript + Tailwind SPA（`apps/phone-bridge/`）を**改訂**する。本改訂の核は次の2点:

1. **USB Web Serial 中継の廃止 → ネットワーク（Wi-Fi）経由のPC接続へ置換**（仕様 §2.1, 変更履歴 2026-07-18）。Pico 入力はPC側でUSB-HIDとして集約されるため（親 §4.1）、本UIは常にPC経由の状態を購読・操作する。スマホ↔Pico の直接USB中継は行わない。
2. **設定サーフェスの拡張**（仕様 §2.2）: OBS的ソース追加/削除、2系統ME（PGM1/PGM2）のプログラム＋PiP編集、4x4コンフィギュラブル・マルチビュー、出力割当、モジュール割付＋Picoネットワーク設定、2系統タリー表示（親 §4.2/§4.3）。

既存資産の再利用/退役方針:

| 既存ファイル | v2 方針 |
| --- | --- |
| `protocol/types.ts` | **REUSE + EXTEND**。`SourceStatus` は PascalCase（`'Connected'\|'Disconnected'\|'Error'`）を**維持**（初版 W-003 レビューで確定した IF互換）。2系統タリー・`SourceDefinition`(NDI/WEBCAM/SRT)・program/multiview/outputs/modules/pico-network の各DTOを追加。 |
| `protocol/apiClient.ts` | **REUSE + EXTEND**。新エンドポイント（sources CRUD, program, multiview, outputs, modules, pico/network, 2系統tally）を追加。 |
| `protocol/wsClient.ts` | **REUSE**。状態購読・指数バックオフ再接続はそのまま流用。 |
| `bridge/serialLink.ts`, `bridge/RelayTab.tsx`, `bridge/parseControllerLine.ts`, `bridge/controllerId.ts` | **RETIRE**（Web Serial 中継を撤去）。「中継(relay)」概念を**PCへのネットワーク接続/状態**へ置換。PCがHID集約を担うため `parseControllerLine`/`controllerId` は原則退役。 |
| `lib/browserSupport.ts` | **RETIRE/縮退**。Web Serial/WebUSB 機能検出は不要。ネットワーク接続にブラウザ制約はほぼ無いため削除、または「セキュアコンテキスト表示」等の最小限に縮退。 |
| `config/layout.ts`, `config/debounce.ts`, `config/presets.ts`, `config/PipEditor.tsx` | **REUSE**。PiPレイアウト計算・デバウンス・プリセット抽象・PiPエディタ（純粋ロジック）は新UIから流用。 |
| `config/ConfigTab.tsx` | **REUSE + 大幅拡張**。単一チャンネル編集から、ソースCRUD/2系統ME/マルチビュー/出力/モジュールの各セクションへ拡張。 |
| `App.tsx` | **REUSE + 改訂**。タブを「中継モード」→「操作/接続モード」に置換し「設定モード」を拡張。 |

```text
apps/phone-bridge/
├── package.json / vite.config.ts / tsconfig.json（据置）
├── src/
│   ├── main.tsx / App.tsx        タブ（操作/接続モード / 設定モード）
│   ├── protocol/                 §4.2/§4.3 の TS型 + REST/WSクライアント（EXTEND）
│   ├── net/                      PCへのネットワーク接続・発見(mDNS/手動)・状態（W2-001, 旧 bridge/ を置換）
│   └── config/                   ソースCRUD + 2系統ME + マルチビュー + 出力 + モジュール + Pico網設定（W2-002）
└── src/**/__tests__/             純粋ロジックのユニットテスト（vitest, ホスト実行）
```

---

## 2. 依存関係と実行スケジュール（直列）

```text
agent-W2-001-network-rebase        （ゲート: Web Serial 撤去 → ネットワーク接続 + protocol型/クライアント拡張）
        ▼
agent-W2-002-config-ui-v2          （拡張設定UI: ソースCRUD + 2系統ME + マルチビュー + 出力 + モジュール + Pico網設定 + 2系統タリー）
```

| 順 | タスク | agent_cli | subtreeプレフィックス | 依存 |
| --- | --- | --- | --- | --- |
| 1 | `agent-W2-001-network-rebase` | sonnet | `apps/phone-bridge/` | なし（改訂の前提ゲート） |
| 2 | `agent-W2-002-config-ui-v2` | sonnet | `apps/phone-bridge/` | W2-001 |

> 単一レーン・単一ディレクトリのため subtree 分割は不要（Epicブランチ上で直列コミット）。W2-001 が `protocol/` の新型・新クライアントIFを確定し、W2-002 がそれを消費して UI を組む。

---

## 3. コンポーネント間インターフェース整合（重要）

- `src/protocol/` の TS 型・JSONフィールド名は PC側（親仕様書 §4.2/§4.3）と**同一スキーマ**（スネークケース）にする:
  - `SourceDefinition`（`POST /api/v1/sources`, `type: "NDI"|"WEBCAM"|"SRT"`, `ndi`/`webcam`/`srt` の判別ユニオン）
  - `SourceInfo`（`GET /api/v1/sources`。`SourceStatus` は **PascalCase 維持**）
  - `ProgramRequest`（`POST /api/v1/program`, `bus:"PGM1"|"PGM2"`, `layers[].pip`, `take`）
  - `MultiviewConfig`（`PUT /api/v1/multiview`, 16セルの `"PGM1"|"PGM2"|"PVW1"|"PVW2"|"SRC:<id>"|"EMPTY"`）
  - `OutputsConfig`（`PUT /api/v1/outputs`, `sink:"VCAM1"|"VCAM2"|"HDMI"`）
  - `ModulesConfig`（`PUT /api/v1/modules`, `index`/`src1`/`src2`/`vr_target`）
  - `PicoNetworkConfig`（`PUT /api/v1/pico/network`, Wi-Fi/BT 資格情報, 親 §4.6）
  - `TallyState`（§4.3, **2系統**: `active_pgm1`/`active_pgm2`/`active_pvw1`/`active_pvw2`）
- 接続先ポートは 8080（REST/WS）。接続先ホストは mDNS/手動IP で設定可能。
- 既存の `PipSettings`(`x_position` 等)・`CropRect` は §4.2 と一致済みのため**流用**。

---

## 4. 起動手順（直列）

```bash
git checkout -b feature/epic-phone-web-bridge-v2
./scripts/manage-screen.sh start agent-W2-001-network-rebase
# （done後）agent-W2-002-config-ui-v2 を起動
./scripts/manage-screen.sh start agent-W2-002-config-ui-v2
```

---

## 5. 検証方針（各タスク共通）

- Node は利用可能。各タスクの検証は次を通すこと:

```bash
npm ci && npx tsc --noEmit && npm run test && npm run build
```

- 型: `npx tsc --noEmit` エラー0（`strict: true`。`any` を避け判別可能なユニオン/型ガードを使う）。
- テスト: `npm run test`（vitest）。純粋ロジック（レイアウト計算・デバウンス・プリセット・DTO往復シリアライズ・マルチビューセル整形・接続状態遷移）をホストでユニットテスト。回帰テストを追加。
- ビルド: `npm run build`（`tsc -b && vite build`）成功。
- スキーマ適合: 送受信ペイロードが親仕様書 §4.2/§4.3 と一致（フィールド名スネークケース, `SourceStatus` は PascalCase）。
- レビュー観点: [`.claude/review-patterns.md`](../../.claude/review-patterns.md)（型・責務分離・並行性/リソース管理・IF互換）＋仕様適合を Opus 親が検証。

## 6. 個別タスク指示書一覧

- [`agent-W2-001-network-rebase.md`](./agent-W2-001-network-rebase.md)
- [`agent-W2-002-config-ui-v2.md`](./agent-W2-002-config-ui-v2.md)
