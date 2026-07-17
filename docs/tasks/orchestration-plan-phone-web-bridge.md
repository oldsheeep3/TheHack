# 全体調整計画書: スマホ経由Webブリッジ & 設定WebUI

- **対象仕様書**: [`docs/specs/phone-web-bridge.md`](../specs/phone-web-bridge.md)（親: [`docs/specs/00-system-overview.md`](../specs/00-system-overview.md)）
- **策定日**: 2026-07-17
- **Epicブランチ**: `feature/epic-phone-web-bridge`
- **最大並列数**: 1（仕様書 §6 「claude: 最大並列 1」に準拠 → 単一レーンの直列実行）
- **実装エージェント(agent_cli)**: `sonnet`（レビュー/オーケストレーションは Opus `task-planner2`）

> ⚠️ 本計画は他コンポーネント計画（`orchestration-plan.md` = PC / `orchestration-plan-pico2w-firmware.md` = ファーム）とは**別Epic**です。タスク名は `agent-W-*` で名前空間を分離しています。
> ⚠️ `/start-all-tasks` は `docs/tasks/orchestration-plan.md`（単数）のみを参照します。本コンポーネントは §4 のとおり `manage-screen.sh start <タスク名>` で**個別・直列起動**してください。

---

## 1. アーキテクチャ方針とタスク分割

単一の Vite + React + TypeScript SPA（`apps/phone-bridge/`）。最大並列1のため**直列フェーズ**で積み上げる。PC常駐アプリ（`Switcher.Web`, タスク `agent-B-001`）の REST/WS 契約（親仕様書 §4）と**型・JSONフィールド名を一致**させることが最重要。

```text
apps/phone-bridge/
├── package.json / vite.config.ts / tsconfig.json / tailwind.config.js
├── src/
│   ├── main.tsx / App.tsx        タブ（中継モード / 設定モード）・ダークUI土台
│   ├── protocol/                 親仕様書§4のTS型 + REST/WSクライアント（共有）
│   ├── bridge/                   Web Serial/WebUSB → WebSocket 中継（W-002）
│   └── config/                   ソース一覧 + PiPレイアウト編集 + プリセット（W-003）
└── src/**/__tests__/             純粋ロジックのユニットテスト（vitest）
```

---

## 2. 依存関係と実行スケジュール（直列）

```text
agent-W-001-web-scaffold           （ゲート: Vite/React/TS/Tailwind + protocol型 + REST/WSクライアント + タブ土台）
        ▼
agent-W-002-usb-bridge             （Web Serial/WebUSB 読取 → WebSocket 中継）
        ▼
agent-W-003-config-ui              （ソース一覧 + PiPレイアウト編集 + プリセット + タリー表示）
```

| 順 | タスク | agent_cli | subtreeプレフィックス | 依存 |
| --- | --- | --- | --- | --- |
| 1 | `agent-W-001-web-scaffold` | sonnet | `apps/phone-bridge/` | なし |
| 2 | `agent-W-002-usb-bridge` | sonnet | `apps/phone-bridge/` | W-001 |
| 3 | `agent-W-003-config-ui` | sonnet | `apps/phone-bridge/` | W-001 |

> 単一レーン・単一ディレクトリのため subtree 分割は不要（Epicブランチ上で直列コミット）。

---

## 3. コンポーネント間インターフェース整合（重要）

- `src/protocol/` の TS 型は PC側 `Switcher.Contracts`（`agent-A-001`）／`Switcher.Web`（`agent-B-001`）と**同一スキーマ**にする:
  - `ButtonEvent` / `WsEnvelope`（§4.1, WS送信）
  - `SourceInfo`（§ `GET /api/v1/sources` レスポンス）
  - `PipSettings` / `ConfigChangeRequest`（§4.2, `POST /api/v1/config`）
  - `TallyState`（§4.3, `active_pgm`/`active_pvw` 表示用）
- 接続先ポートは 8080（REST/WS）。JSONフィールド名はスネークケースで一致させる。

---

## 4. 起動手順（直列）

```bash
git checkout -b feature/epic-phone-web-bridge
./scripts/manage-screen.sh start agent-W-001-web-scaffold
# （done後）agent-W-002 → agent-W-003 と直列に
```

---

## 5. 検証方針（各タスク共通）

- ビルド: `npm ci && npm run build`（Vite）。
- 型: `tsc --noEmit`（`strict: true`。`any` を避ける）。
- Lint: `npm run lint`（ESLint 導入時）。
- テスト: `npm run test`（vitest）。純粋ロジック（イベント整形・レイアウト計算・JSONパース）をユニットテスト。
- レビュー観点: [`.claude/review-patterns.md`](../../.claude/review-patterns.md)（型・責務分離・並行性・IF互換）＋仕様適合を Opus 親が検証。

## 6. 個別タスク指示書一覧

- [`agent-W-001-web-scaffold.md`](./agent-W-001-web-scaffold.md)
- [`agent-W-002-usb-bridge.md`](./agent-W-002-usb-bridge.md)
- [`agent-W-003-config-ui.md`](./agent-W-003-config-ui.md)
