# 全体調整計画書: PC常駐アプリ（映像エンジン & 制御ハブ）

- **対象仕様書**: [`docs/specs/pc-switcher-app.md`](../specs/pc-switcher-app.md)（親: [`docs/specs/00-system-overview.md`](../specs/00-system-overview.md)）
- **策定日**: 2026-07-17
- **Epicブランチ**: `feature/epic-pc-switcher-app`
- **最大並列数**: 2（仕様書 §6 「claude: 最大並列 2」に準拠）
- **実装エージェント(agent_cli)**: `sonnet`（全実装タスク） / レビュー・オーケストレーションは Opus (`task-planner2`) が担当

---

## 1. アーキテクチャ方針とタスク分割

.NET ソリューション `HybridSwitcher.sln` を **1タスク＝1プロジェクト（＝1ディレクトリ）** に分割し、各並列タスクが別ディレクトリで作業することで subtree 境界の衝突を回避する。

```text
HybridSwitcher.sln
├── src/Switcher.Contracts/    共有モデル・IF・プロトコル定義   … A-001 (Phase0/直列ゲート)
├── src/Switcher.Media/        GStreamer入力 + GPU/PiP合成      … A-002 (Phase1)
├── src/Switcher.Web/          Kestrel WebAPI/WS + UDPタリー    … B-001 (Phase1)
├── src/Switcher.Atem/         ATEM遠隔制御クライアント          … A-003 (Phase2)
├── src/Switcher.VirtualCam/   DirectShow仮想カメラ出力          … B-002 (Phase2)
└── src/Switcher.App/          WPFホスト・配線・マルチビューUI  … A-004 (Phase3/直列統合)
```

### 共通ファイル競合の回避方針
- **`.sln` は Phase0 で全プロジェクトのスタブごと一括登録**しておく。以降の並列タスクは**自分のプロジェクトディレクトリ内のみ**を編集し、`.sln` に触れない。
- プロジェクト間の依存は「各実装プロジェクト → `Switcher.Contracts`」の一方向のみ。`Contracts` の公開IF/DTOは A-001 で確定させ、以降変更しない（変更が必要な場合は Opus 親が調整）。

---

## 2. 依存関係と実行スケジュール（フェーズ）

```text
Phase 0 (直列ゲート / 並列1)
  └─ agent-A-001-foundation-contracts     ← 全タスクの前提
        │
        ▼
Phase 1 (並列2)
  ├─ agent-A-002-media-engine
  └─ agent-B-001-web-tally-server
        │
        ▼
Phase 2 (並列2)
  ├─ agent-A-003-atem-control
  └─ agent-B-002-virtualcam-output
        │
        ▼
Phase 3 (直列統合 / 並列1)
  └─ agent-A-004-app-integration          ← 全モジュールの配線・E2E
```

| フェーズ | タスク | agent_cli | subtreeプレフィックス | 依存 |
| --- | --- | --- | --- | --- |
| 0 | `agent-A-001-foundation-contracts` | sonnet | （Epic直下: `src/Switcher.Contracts/` + `.sln`） | なし |
| 1 | `agent-A-002-media-engine` | sonnet | `src/Switcher.Media/` | A-001 |
| 1 | `agent-B-001-web-tally-server` | sonnet | `src/Switcher.Web/` | A-001 |
| 2 | `agent-A-003-atem-control` | sonnet | `src/Switcher.Atem/` | A-001 |
| 2 | `agent-B-002-virtualcam-output` | sonnet | `src/Switcher.VirtualCam/` | A-001 |
| 3 | `agent-A-004-app-integration` | sonnet | `src/Switcher.App/` | A-002, A-003, A-004前提の全て |

> Phase0 の `Contracts` は共有基盤のため subtree に隔離せず Epic ブランチ直下で確定させる（README §3「Epicブランチ作成と subtree 初期化」の直前工程に相当）。

---

## 3. 手動セットアップ手順（Phase0 完了後に1回）

```bash
# Epicブランチ作成
git checkout -b feature/epic-pc-switcher-app

# （Phase0: A-001 を先に実装し、Contracts と .sln スタブを Epic に確定）

# 各エージェント用 subtree 初期化（Phase1以降の並列タスク用）
git subtree add --prefix=src/Switcher.Media       agent-A-media-branch
git subtree add --prefix=src/Switcher.Web         agent-B-web-branch
git subtree add --prefix=src/Switcher.Atem        agent-A-atem-branch
git subtree add --prefix=src/Switcher.VirtualCam  agent-B-virtualcam-branch
git subtree add --prefix=src/Switcher.App         agent-A-app-branch
```

---

## 4. 一括起動

Phase 順に、未完了タスクを起動する（`status: done` は自動スキップ）。

```bash
/start-all-tasks
# もしくは個別に:
./scripts/manage-screen.sh start agent-A-001-foundation-contracts
```

- Phase0 が `done` になってから Phase1 を起動すること（直列ゲート）。以降も前フェーズ完了を確認してから次フェーズを起動する。

---

## 5. 検証・非機能の共通観点（各タスク共通）

- ビルド: `dotnet build HybridSwitcher.sln`（各タスクは自プロジェクトの `dotnet build` / `dotnet test` を通す）
- 静的検査: `dotnet format --verify-no-changes`（導入時）。Nullable 有効・`any` 相当の緩い型を避ける。
- 非機能: 低遅延（映像パスの低バッファ）、1系統障害の隔離、コントローラー入力の直列化（競合回避）。
- レビュー: [`.claude/review-patterns.md`](../../.claude/review-patterns.md) の観点＋仕様適合を Opus 親が検証。

## 6. 個別タスク指示書一覧

- [`agent-A-001-foundation-contracts.md`](./agent-A-001-foundation-contracts.md)
- [`agent-A-002-media-engine.md`](./agent-A-002-media-engine.md)
- [`agent-B-001-web-tally-server.md`](./agent-B-001-web-tally-server.md)
- [`agent-A-003-atem-control.md`](./agent-A-003-atem-control.md)
- [`agent-B-002-virtualcam-output.md`](./agent-B-002-virtualcam-output.md)
- [`agent-A-004-app-integration.md`](./agent-A-004-app-integration.md)
