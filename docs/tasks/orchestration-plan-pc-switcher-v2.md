# 全体調整計画書(v2): PC常駐アプリ 大幅改訂（2系統ME / OBS的ソース / HID / 4x4マルチビュー）

- **対象仕様書**: [`docs/specs/pc-switcher-app.md`](../specs/pc-switcher-app.md)（改訂版 / 親: [`docs/specs/00-system-overview.md`](../specs/00-system-overview.md) §4）
- **策定日**: 2026-07-18
- **Epicブランチ**: `feature/epic-pc-switcher-v2`
- **最大並列数**: 2（仕様書 §6 「claude: 最大並列 2」に準拠）
- **実装エージェント(agent_cli)**: `sonnet`（全実装タスク） / レビュー・オーケストレーションは Opus (`task-planner2`) が担当
- **アプローチ**: **既存 .NET 実装の再利用・差分改修（REUSE / INCREMENTAL-DIFF）**。ゼロから作り直さない。既存 `HybridSwitcher.sln` の各プロジェクトを **加算的（additive）** に拡張し、可能な限り後方互換を保つ。

> 本計画は初版計画 [`orchestration-plan.md`](./orchestration-plan.md) の続きであり、既に `status: done` になっている初版タスク（A-001〜A-004, B-001, B-002）で実装済みの資産を土台とする。既存ソリューションには 6 実装プロジェクト（`Switcher.Contracts` / `Switcher.Media` / `Switcher.Web` / `Switcher.Atem` / `Switcher.VirtualCam` / `Switcher.App`）と 5 テストプロジェクトが登録済み。

---

## 0. 実行環境の前提（全タスク共通）

- `dotnet` SDK は `~/.dotnet` に配置されている。**すべてのビルド/テストコマンドは以下の環境変数を前提**とする:

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH
```

- ソリューション検証（全タスク共通の受け入れコマンド）:

```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
```

> **前回イテレーションの教訓**: `dotnet test HybridSwitcher.sln` が **全テストスイートを実行する**よう、新規テストプロジェクトは必ず `.sln` に登録すること（登録漏れがあると CI で無検査のまま素通りする）。新規プロジェクトの `.sln` 登録は原則 **Phase0（A2-001）** または **Phase3統合（A2-006）** で行い、並列フェーズのタスクは `.sln` を編集しない。

---

## 1. アーキテクチャ方針とタスク分割

改訂の骨子（仕様 §7 / 親 §4）:

1. **OBS的ソース自由追加**（NDI / WebCam(UVC) / SRT）: `SourceInfo` 中心のチャネル固定モデルから、`id` ベースで追加/削除/並べ替えできる `SourceDefinition` モデルへ拡張（後方互換維持）。
2. **2系統ME（PGM1/PGM2 + PVW1/PVW2）**: 単一 PGM/PVW の `CompositorEngine` を **バス指定（PGM1/PGM2）** の合成へ拡張。
3. **4x4 コンフィギュラブル・マルチビュー**（16セル）。
4. **出力: 仮想カメラ×2 + HDMI全画面（カーソル非表示）**、`PUT /api/v1/outputs` で割当。
5. **USB-HID 入力受信 + バックライト算出**（親 §4.1, §4.5）— 新規プロジェクト `Switcher.Hid`。
6. **タリー2系統化**（`active_pgm1/2`, `active_pvw1/2`、親 §4.3）。
7. **ATEM遠隔制御は維持**（§2.8）。既存 `Switcher.Atem` は **変更せず再利用**し、`PUT /api/v1/atem` マッピングへ配線するのみ。
8. WebSocketボタン経路は HID 経路に置換（supersede）。既存WSは互換のため残置 or 非推奨化（仕様に従う）。

.NET ソリューション `HybridSwitcher.sln` を **1タスク＝1プロジェクト（＝1ディレクトリ）** の粒度で差分改修し、各並列タスクが別ディレクトリで作業することで subtree 境界の衝突を回避する。

```text
HybridSwitcher.sln（既存 / 加算的に改修）
├── src/Switcher.Contracts/    共有DTO/IF/定数を拡張          … A2-001 (Phase0/直列ゲート)  ★.sln編集可
├── src/Switcher.Media/        2系統ME + OBS的ソース管理へ拡張 … A2-002 (Phase1)
├── src/Switcher.Web/          新API群 + 2系統タリー           … A2-003 (Phase1)
├── src/Switcher.Hid/          【新規】HID入出力 + バックライト算出 … A2-004 (Phase2)  ★A2-001で.sln登録
├── src/Switcher.VirtualCam/   仮想カメラ×2 + HDMI全画面出力   … A2-005 (Phase2)
├── src/Switcher.Atem/         ★変更なし（そのまま再利用）
└── src/Switcher.App/          配線・4x4UI・出力・ATEM配線・HID … A2-006 (Phase3/直列統合) ★.sln編集可
```

### 共通ファイル競合の回避方針
- **`.sln` への新規プロジェクト登録は Phase0 (A2-001) に集約**する。A2-001 は `Switcher.Hid`（＋そのテスト `Switcher.Hid.Tests`）の**空スタブ**を作成して `.sln` に登録し、Contracts を確定させる。以降の並列タスクは**自分のプロジェクトディレクトリ内のみ**を編集し `.sln` に触れない。
- プロジェクト間の依存は「各実装プロジェクト → `Switcher.Contracts`」の一方向のみ。改訂で追加する公開IF/DTOは A2-001 で確定させ、以降変更しない（変更が必要な場合は Opus 親が調整）。
- **後方互換方針**: 既存の公開型（`SourceInfo` / `TallyState` / `PipSettings` / 各IF）はできる限り**壊さず加算**する。破壊が避けられない箇所（例: `ICompositorEngine` へのバス引数追加）は A2-001 でIFを確定し、下流タスクの改修範囲を明示する。

---

## 2. 依存関係と実行スケジュール（フェーズ）

```text
Phase 0 (直列ゲート / 並列1)
  └─ agent-A2-001-contracts-v2            ← 全タスクの前提（Contracts拡張 + Hidスタブ.sln登録）
        │
        ▼
Phase 1 (並列2)
  ├─ agent-A2-002-media-dualme            （2系統ME + OBS的ソース管理）
  └─ agent-A2-003-web-api-v2              （新API群 + 2系統タリー）
        │
        ▼
Phase 2 (並列2)
  ├─ agent-A2-004-hid-io                  （新規 Switcher.Hid: HID入出力 + バックライト算出）
  └─ agent-A2-005-output-dualcam-hdmi     （仮想カメラ×2 + HDMI全画面）
        │
        ▼
Phase 3 (直列統合 / 並列1)
  └─ agent-A2-006-app-integration-v2      ← 全モジュール配線・4x4UI・E2E・.sln最終確認
```

| フェーズ | タスク | agent_cli | subtreeプレフィックス | 依存 |
| --- | --- | --- | --- | --- |
| 0 | `agent-A2-001-contracts-v2` | sonnet | `src/Switcher.Contracts/`（＋ `.sln` / `src/Switcher.Hid/` スタブ登録） | なし |
| 1 | `agent-A2-002-media-dualme` | sonnet | `src/Switcher.Media/` | A2-001 |
| 1 | `agent-A2-003-web-api-v2` | sonnet | `src/Switcher.Web/` | A2-001 |
| 2 | `agent-A2-004-hid-io` | sonnet | `src/Switcher.Hid/` | A2-001 |
| 2 | `agent-A2-005-output-dualcam-hdmi` | sonnet | `src/Switcher.VirtualCam/` | A2-001 |
| 3 | `agent-A2-006-app-integration-v2` | sonnet | `src/Switcher.App/`（＋ `.sln` 最終確認） | A2-002, A2-003, A2-004, A2-005 |

> `Switcher.Atem` は改訂対象外（§2.8 維持）。既存 `IAtemController` / `AtemController` / `ButtonCommandMapping` をそのまま再利用し、A2-003（`PUT /api/v1/atem` の受け口）と A2-006（配線）から利用する。**A2 タスクとして単独の Atem 改修タスクは設けない。**

---

## 3. 手動セットアップ手順（Phase0 完了後に1回）

```bash
# Epicブランチ作成（既存 main / 前回Epic成果を土台に）
git checkout -b feature/epic-pc-switcher-v2

# （Phase0: A2-001 を先に実装し、Contracts拡張 と Switcher.Hid スタブ + .sln登録 を Epic に確定）

# 各エージェント用 subtree 初期化（Phase1以降の並列タスク用）
git subtree add --prefix=src/Switcher.Media       agent-A2-media-branch
git subtree add --prefix=src/Switcher.Web         agent-A2-web-branch
git subtree add --prefix=src/Switcher.Hid         agent-A2-hid-branch
git subtree add --prefix=src/Switcher.VirtualCam  agent-A2-vcam-branch
git subtree add --prefix=src/Switcher.App         agent-A2-app-branch
```

---

## 4. 一括起動

Phase 順に、未完了タスクを起動する（`status: done` は自動スキップ）。

```bash
/start-all-tasks
# もしくは個別に:
./scripts/manage-screen.sh start agent-A2-001-contracts-v2
```

- Phase0 が `done` になってから Phase1 を起動すること（直列ゲート）。以降も前フェーズ完了を確認してから次フェーズを起動する。

---

## 5. 検証・非機能の共通観点（各タスク共通）

- ビルド: `DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln`
- テスト: `DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test HybridSwitcher.sln`（**全スイートが実行されること**を確認。新規テストは `.sln` 登録必須）
- 静的検査: Nullable 有効・`TreatWarningsAsErrors`。警告0を維持。
- 非機能: 低遅延（映像パスの低バッファ）、1系統/1モジュール障害の隔離、コントローラー入力（HID）の直列化（競合回避、メイン/サブ同時操作安全）。
- **再利用の徹底**: 既存クラス（`InputSourceManager` / `CompositorEngine` / `PipLayoutCalculator` / `PipelineDescriptorFactory` / `TallyBroadcaster` / `VirtualCameraOutput` / `AtemController` 等）を極力**拡張**し、置き換えは最小限に。
- レビュー: [`.claude/review-patterns.md`](../../.claude/review-patterns.md) の観点＋仕様適合（親 §4 のフィールド名一致）を Opus 親が検証。

## 6. 個別タスク指示書一覧

- [`agent-A2-001-contracts-v2.md`](./agent-A2-001-contracts-v2.md)
- [`agent-A2-002-media-dualme.md`](./agent-A2-002-media-dualme.md)
- [`agent-A2-003-web-api-v2.md`](./agent-A2-003-web-api-v2.md)
- [`agent-A2-004-hid-io.md`](./agent-A2-004-hid-io.md)
- [`agent-A2-005-output-dualcam-hdmi.md`](./agent-A2-005-output-dualcam-hdmi.md)
- [`agent-A2-006-app-integration-v2.md`](./agent-A2-006-app-integration-v2.md)
