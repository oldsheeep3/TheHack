# 全体調整計画書(v3): マルチビュー & 出力（HDMI/NDI/フルスクリーン）改訂

- **対象仕様書**: [`docs/specs/multiview-output-revision.md`](../specs/multiview-output-revision.md)（親: [`docs/specs/pc-switcher-app.md`](../specs/pc-switcher-app.md) §2.1/§2.3/§2.4, [`docs/specs/00-system-overview.md`](../specs/00-system-overview.md) §4.2）
- **策定日**: 2026-07-18
- **Epicブランチ**: `feature/epic-multiview-output-v3`
- **最大並列数**: 2（仕様書 §7 「claude: 最大並列 2」に準拠）
- **実装エージェント(agent_cli)**: `sonnet`（全実装タスク） / レビュー・オーケストレーションは Opus (`task-planner2`) が担当
- **アプローチ**: **既存 .NET 実装の再利用・差分改修（REUSE / INCREMENTAL-DIFF / ADDITIVE）**。v2（`agent-A2-001`〜`A2-006`, すべて `status: done`）の成果を土台に、契約・出力型・マルチビューを**後方互換を保って加算拡張**する。ゼロから作り直さない。

> 本計画は v2 計画 [`orchestration-plan-pc-switcher-v2.md`](./orchestration-plan-pc-switcher-v2.md) の続きであり、既存 6 実装プロジェクト（`Switcher.Contracts` / `Switcher.Media` / `Switcher.Web` / `Switcher.Atem` / `Switcher.VirtualCam` / `Switcher.App`）と 7 テストプロジェクトが `.sln` に登録済みであることを前提とする。**本改訂では新規プロジェクトを追加しないため `.sln` は編集しない**（既存テストプロジェクトを加算拡張する）。

---

## 0. 実行環境の前提（全タスク共通）

- `dotnet` SDK は `~/.dotnet` に配置されている。すべてのビルド/テストは以下を前提とする:

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH
```

- ソリューション検証（全タスク共通の受け入れコマンド）:

```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
```

> **v2 の教訓**: `dotnet test HybridSwitcher.sln` が **全7スイート（Contracts/Media/Web/Atem/VirtualCam/Hid/App）を実行**すること。本改訂は新規テストプロジェクトを作らず既存スイートを拡張するため、`.sln` は触らない。既存テストは全グリーンを維持する。

---

## 1. アーキテクチャ方針とタスク分割

改訂の骨子（仕様 §2 の7要件）:

1. **全画面出力のディスプレイ指定＋同一画面警告（続行可）**（要件1）。
2. **操作画面ディスプレイをトレイ右クリック等から変更・永続化**（要件2）。
3. **マルチビューの矩形結合（ドラッグ, region化 / `cells` 後方互換）**（要件3）。
4. **マルチビュー全画面（独立ウィンドウ, HDMI全画面と同等挙動）**（要件4）。
5. **NDI 出力2系統（NDI1/NDI2, PGM1/PGM2, 送出名設定）**（要件5）。
6. **ソース追加3段UI（名前/種別/デバイス列挙）, SRTは接続設定+ATEMヘルパー**（要件6）。
7. **SRTセットアップ手順とATEM設定用ホスト名/ポート表示**（要件7）。

**契約・IF はすべて Phase0（A3-001）で確定**し、以降の並列タスクは自分のプロジェクトディレクトリ内のみを編集する。デバイス列挙・SRT情報は読み取り専用クエリのため、Web↔Media 直接依存を避けて**新IF `IDeviceQueryService` を `Switcher.Contracts/Interfaces` に置く**（Media が実装、Web エンドポイントが利用（テストはフェイク）、App が実体を配線）。

```text
HybridSwitcher.sln（既存 / 加算的に改修・.sln編集なし）
├── src/Switcher.Contracts/    OutputSink/Assignment/Multiview拡張 + 新DTO/IF … A3-001 (Phase0/直列ゲート)
├── src/Switcher.Media/        デバイス列挙 + MVリージョン合成 + SRTホスト算出 … A3-002 (Phase1)
├── src/Switcher.Web/          devices/srt-setup API + バリデータ拡張         … A3-003 (Phase1)
├── src/Switcher.VirtualCam/   NDI出力×2 + MV全画面提示（既存出力型を再利用） … A3-004 (Phase2)
├── src/Switcher.Atem/         ★変更なし
└── src/Switcher.App/          トレイ/UI/警告/ドラッグ結合/ソース3段UI/配線    … A3-005 (Phase3/直列統合)
```

### 共通ファイル競合の回避方針
- **契約・公開IF の変更は A3-001 に集約**。以降のタスクは A3-001 が確定した型/IFに依存するのみ。
- プロジェクト間依存は「各実装プロジェクト → `Switcher.Contracts`」の一方向のみ（Web は Media/VirtualCam の実装を参照しない。IF 経由）。
- **後方互換必須**: `MultiviewLayout` は `cells`(16フラット) 形式を受理し続ける（内部で 1x1 region 正規化）。既存 `OutputSink`(VCAM1/VCAM2/HDMI)・`SourceDefinition`・既存テストを壊さない。

---

## 2. 依存関係と実行スケジュール（フェーズ）

```text
Phase 0 (直列ゲート / 並列1)
  └─ agent-A3-001-contracts-mvout        ← 全タスクの前提（Contracts/IF/DTO 確定）
        │
        ▼
Phase 1 (並列2)
  ├─ agent-A3-002-media-devices-mv       （デバイス列挙 + MVリージョン合成 + SRTホスト算出）
  └─ agent-A3-003-web-devices-validate   （devices/srt-setup API + outputs/multiview バリデータ拡張）
        │
        ▼
Phase 2 (並列1)
  └─ agent-A3-004-output-ndi-mvfull      （NDI出力×2 + MV全画面提示）
        │
        ▼
Phase 3 (直列統合 / 並列1)
  └─ agent-A3-005-app-ui-integration     ← トレイ/ドラッグ結合UI/ソース3段UI/警告/全画面/配線/E2E
```

| フェーズ | タスク | agent_cli | subtreeプレフィックス | 依存 |
| --- | --- | --- | --- | --- |
| 0 | `agent-A3-001-contracts-mvout` | sonnet | `src/Switcher.Contracts/` | なし |
| 1 | `agent-A3-002-media-devices-mv` | sonnet | `src/Switcher.Media/` | A3-001 |
| 1 | `agent-A3-003-web-devices-validate` | sonnet | `src/Switcher.Web/` | A3-001 |
| 2 | `agent-A3-004-output-ndi-mvfull` | sonnet | `src/Switcher.VirtualCam/` | A3-001 |
| 3 | `agent-A3-005-app-ui-integration` | sonnet | `src/Switcher.App/` | A3-002, A3-003, A3-004 |

> `Switcher.Atem` / `Switcher.Hid` は本改訂の対象外（無改変）。A3-004 は Contracts のみに依存し Media/Web と独立のため Phase1 と並走も可能だが、最大並列2を守り Phase2 に単独配置する。

---

## 3. 手動セットアップ手順（Phase0 完了後に1回）

```bash
# Epicブランチ作成（既存 main / v2成果を土台に）
git checkout -b feature/epic-multiview-output-v3

# （Phase0: A3-001 を先に実装し、Contracts拡張 を Epic に確定）

# 各エージェント用 subtree 初期化（Phase1以降の並列タスク用）
git subtree add --prefix=src/Switcher.Media       agent-A3-media-branch
git subtree add --prefix=src/Switcher.Web         agent-A3-web-branch
git subtree add --prefix=src/Switcher.VirtualCam  agent-A3-vcam-branch
git subtree add --prefix=src/Switcher.App         agent-A3-app-branch
```

---

## 4. 一括起動

Phase 順に、未完了タスクを起動する（`status: done` は自動スキップ）。

```bash
/start-all-tasks
# もしくは個別に:
./scripts/manage-screen.sh start agent-A3-001-contracts-mvout
```

- Phase0 が `done` になってから Phase1 を起動する（直列ゲート）。以降も前フェーズ完了を確認してから次フェーズを起動する。

---

## 5. 検証・非機能の共通観点（各タスク共通）

- ビルド/テスト: 上記コマンドで**全7スイートが実行され全グリーン**、警告0（Nullable 有効・`TreatWarningsAsErrors`）。
- **後方互換**: `cells` 形式マルチビュー・既存 sink・既存 `SourceDefinition`・既存テストを壊さない。
- 非機能: 低遅延（映像パス低バッファ）、1 sink/1ソース障害の隔離（NDI/HDMI/VCAM の1系統障害が他を止めない）、入力/状態変更の直列化。
- Windows実行時依存（NDI/DirectShow/MF/D3D）は該当プロジェクト内に閉じ、テストはフェイク注入でヘッドレスCIでもビルド/テストが通る。
- **再利用の徹底**: `HdmiFullscreenOutput` / `Direct3DSwapChainOutput` / `ICursorVisibility` / `OutputRouter` / `CompositorEngine` / `TrayIconService` / `ProjectorWindow` / `MultiviewCellViewModel` 等を拡張し、置換は最小限に。
- レビュー: [`.claude/review-patterns.md`](../../.claude/review-patterns.md) の観点＋仕様適合（親 §4.2 のフィールド名一致）を Opus 親が検証。

## 6. 個別タスク指示書一覧

- [`agent-A3-001-contracts-mvout.md`](./agent-A3-001-contracts-mvout.md)
- [`agent-A3-002-media-devices-mv.md`](./agent-A3-002-media-devices-mv.md)
- [`agent-A3-003-web-devices-validate.md`](./agent-A3-003-web-devices-validate.md)
- [`agent-A3-004-output-ndi-mvfull.md`](./agent-A3-004-output-ndi-mvfull.md)
- [`agent-A3-005-app-ui-integration.md`](./agent-A3-005-app-ui-integration.md)
