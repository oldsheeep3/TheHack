# 全体調整計画書: 映像エンジンの libobs 移行

- **対象仕様書**: [`docs/specs/libobs-engine-migration.md`](../specs/libobs-engine-migration.md)（親: [`docs/specs/pc-switcher-app.md`](../specs/pc-switcher-app.md) §2/§3, [`docs/specs/00-system-overview.md`](../specs/00-system-overview.md) §4）
- **策定日**: 2026-07-21
- **ベース/統合ブランチ**: `develop`（`main` 基点で新設済み。以降の開発の基点）
- **Epicブランチ**: `feature/epic-libobs-migration`（`develop` から作成。各タスクPRの統合先）
- **最大並列数**: 2（仕様書 §8 / 親 §6「claude: 最大並列 2」に準拠）
- **実装エージェント(agent_cli)**: 映像エンジン中核（契約設計・ネイティブ libobs）は `opus`、その他の .NET 配線・削除は `sonnet`。レビュー/オーケストレーションは Opus (`task-planner2`) が担当。
- **アプローチ**: **REPLACE-ENGINE / REWIRE-TO-ABSTRACTION / DELETE-DEAD-CODE**。映像エンジン（デコード・合成・出力）を GStreamer+DirectX 自作実装から **libobs** へ置換する。**新抽象 `IVideoEngine`（Contracts）を単一の映像エンジン境界**とし、`Switcher.Web`/`Switcher.App` はこの抽象にのみ依存させる。実体は新設 `Switcher.Engine`(C#, P/Invoke) が新設ネイティブ `switcher-engine`(C/C++, libobs) を駆動する。最後に `Switcher.Media` / `Switcher.VirtualCam` と外部依存（GstSharp/Vortice）を**削除**する。

> **維持する外部契約**（壊さない）: WebAPI/WebSocket（8080, スネークケースJSON）、タリーUDP（9999, 2系統）、HIDレポート、設定JSON、`OutputSink`/`MultiviewLayout`/`SourceDefinition` 契約。`Switcher.Atem`/`Switcher.Hid`/`Switcher.Contracts`(拡張のみ)/`Switcher.Web`(配線替え)/`Switcher.App`(配線替え) は**維持**。

---

## 0. 実行環境の前提（全タスク共通）

### .NET（マネージド側 / CI 対象）
```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
```
- **ヘッドレスCIの鉄則**: マネージド側は **libobs/OBS 非搭載でもビルド/テストが緑**であること。`Switcher.Engine` の実 P/Invoke 実装は Windows+OBS 実行時のみ有効化し、**テストはフェイク `IVideoEngine` 注入**で行う（`Switcher.Media`/`VirtualCam` が持っていた「Windows依存はプロジェクト内に閉じ、テストはフェイク」方針を踏襲）。

### ネイティブ（`switcher-engine` / Windows + OBS のみ）
- 対象 OBS バージョンの **libobs ヘッダ + import lib** にリンク（CMake）。バージョンは L-002 で固定し README に明記。**dotnet の sln には含めない**（別ビルド）。

---

## 1. アーキテクチャ方針とタスク分割

### 1.1 新しい映像エンジン境界

```text
[ Switcher.App (WPF) ]  [ Switcher.Web (Kestrel) ]   ← どちらも IVideoEngine にのみ依存
            \                    /
             ▼                  ▼
        Switcher.Contracts: IVideoEngine (新抽象) / 関連DTO
                     ▲                         ▲
        （実体）     │                         │（テスト）
             Switcher.Engine (C#)         FakeVideoEngine (テスト用)
                     │  P/Invoke (engine.h C ABI)
                     ▼
        native/switcher-engine (C/C++, libobs, GPLv2)
                     │  obs_view×2 / bundled modules / outputs
                     ▼
                  libobs (OBS core)
```

- **`IVideoEngine`** が置換する既存抽象: `ICompositorEngine`（2系統ME/PiP/TAKE）・`IInputSourceManager`/`IFrameSource`（ソース管理）・`IVirtualCameraOutput`＋`OutputRouter`（出力）。これらは移行完了後に**削除**する。
- **維持する抽象**: `IControllerInputSink`（HID）・`ITallyBroadcaster`（タリー）・`IAtemController`（ATEM）・`IDeviceQueryService`（デバイス列挙, 実体を `Switcher.Engine` が libobs 列挙で提供）。
- **プレビュー描画**: App の WPF プレビュー/マルチビューは、エンジンから **スロットル済み CPU BGRA フレーム読み戻しコールバック**を受けて `WriteableBitmap` に描画（既存 `FrameBitmapWriter` を再利用）。**HDMI 全画面**は App のフルスクリーンウィンドウの HWND を渡し、ネイティブ `obs_display` が直接描画（読み戻しなし）。

### 1.2 タスク一覧

| Phase | タスク | agent_cli | subtreeプレフィックス | 依存 |
| --- | --- | --- | --- | --- |
| 0 | `agent-L-001-contracts-engine-abi` | opus | `src/Switcher.Contracts/` + `src/Switcher.Engine/`(新) + `.sln` | なし |
| 1 | `agent-L-002-native-libobs-engine` | opus | `native/switcher-engine/`(新) | L-001 |
| 1 | `agent-L-003-web-engine-rewire` | sonnet | `src/Switcher.Web/` | L-001 |
| 2 | `agent-L-004-app-engine-integration` | sonnet | `src/Switcher.App/` | L-001, L-003 |
| 3 | `agent-L-005-cleanup-delete-media-vcam` | sonnet | ルート（削除・sln・csproj・Contracts掃除） | L-003, L-004 |

> L-001（ゲート）で **`IVideoEngine`(C#) と `engine.h`(C ABI) を同時確定**する。これがネイティブ(L-002)とマネージド配線(L-003/004)の共有契約になる。L-002 はネイティブのみで .NET 側と非競合のため L-003 と Phase1 並走可（最大並列2）。

---

## 2. 依存関係と実行スケジュール（フェーズ）

```text
Phase 0 (直列ゲート / 並列1)
  └─ agent-L-001-contracts-engine-abi     ← IVideoEngine + engine.h + Switcher.Engine骨組み(P/Invoke+Fake) + sln登録
        │
        ▼
Phase 1 (並列2)
  ├─ agent-L-002-native-libobs-engine     （libobs: 起動/モジュール読込/obs_view×2/ソース/出力/MV/状態CB）
  └─ agent-L-003-web-engine-rewire        （Web エンドポイントを IVideoEngine 経由へ配線替え）
        │
        ▼
Phase 2 (直列統合 / 並列1)
  └─ agent-L-004-app-engine-integration   （App DI を Switcher.Engine へ / HID結線 / プレビュー・MV・HDMI提示 / Media・VirtualCam参照除去）
        │
        ▼
Phase 3 (直列 / 並列1)
  └─ agent-L-005-cleanup-delete-media-vcam （Media/VirtualCam＋両テスト削除 / GstSharp・Vortice除去 / sln整理 / Contracts不要IF削除 / 最終グリーン）
```

- **各フェーズは前フェーズが `done` になってから起動**（直列ゲート厳守）。特に L-005（削除）は L-003/L-004 で Media/VirtualCam への参照が完全に消えたことを前提とする。

---

## 3. 手動セットアップ手順（Phase0 完了後に1回）

```bash
# Epicブランチ作成（develop を土台に）
git checkout develop
git checkout -b feature/epic-libobs-migration

# （Phase0: L-001 を先に実装し、Contracts の IVideoEngine / engine.h / Switcher.Engine骨組み を Epic に確定）

# 各エージェント用 subtree 初期化（Phase1以降の並列タスク用）
git subtree add --prefix=native/switcher-engine  agent-L-native-branch
git subtree add --prefix=src/Switcher.Web        agent-L-web-branch
git subtree add --prefix=src/Switcher.App        agent-L-app-branch
```

> L-005 はルート横断（ディレクトリ削除・`.sln`・`.csproj`・Contracts 掃除）のため subtree 分離せず、Epic 上で直列実行する。

---

## 4. 一括起動

```bash
/start-all-tasks
# もしくは個別に:
./scripts/manage-screen.sh start agent-L-001-contracts-engine-abi
```
- Phase0 が `done` になってから Phase1（L-002 + L-003 並列）を起動。以降も前フェーズ完了を確認して次を起動。

---

## 5. 検証・非機能の共通観点（各タスク共通）

- **マネージドのビルド/テスト**: `dotnet build/test HybridSwitcher.sln` が**全スイート緑・警告0**（Nullable 有効・`TreatWarningsAsErrors`）。`Switcher.Engine` を含む新テストも sln 登録。
- **ヘッドレス互換**: libobs/OBS 非搭載でもマネージド側は緑（フェイク `IVideoEngine`）。ネイティブ `switcher-engine` の実行テストは Windows+OBS 限定でよい。
- **契約後方互換**: WebAPI（スネークケース）・タリーペイロード・HIDレポート・`OutputSink`/`MultiviewLayout`(region+cells)/`SourceDefinition` を壊さない。既存 Web/Contracts/Hid/Atem/App テストは緑を維持。
- **耐障害・低遅延**: 1ソース/1 sink 障害の隔離、映像パス低バッファ（親 §4）。
- **GPLv2 境界**: libobs リンクは `native/switcher-engine`（GPL）に閉じ、マネージド側は `IVideoEngine` 抽象と P/Invoke 宣言のみ。
- **削除の安全性**: L-005 の削除前に、対象への参照（`ProjectReference`・`using`・DI 登録）がゼロであることを grep で確認してから削除。
- **レビュー**: [`.claude/review-patterns.md`](../../.claude/review-patterns.md) の観点＋仕様適合（親 §4.2 のフィールド名一致、`IVideoEngine` 抽象の一貫性）を Opus 親が検証。

## 6. 個別タスク指示書一覧

- [`agent-L-001-contracts-engine-abi.md`](./agent-L-001-contracts-engine-abi.md)
- [`agent-L-002-native-libobs-engine.md`](./agent-L-002-native-libobs-engine.md)
- [`agent-L-003-web-engine-rewire.md`](./agent-L-003-web-engine-rewire.md)
- [`agent-L-004-app-engine-integration.md`](./agent-L-004-app-engine-integration.md)
- [`agent-L-005-cleanup-delete-media-vcam.md`](./agent-L-005-cleanup-delete-media-vcam.md)

## 7. リスク・技術検証（仕様 §9 対応）
- **仮想カメラ2系統化**: OBS 標準仮想カメラは1系統。L-002 で「第2仮想カメラ提供 or 一方を NDI/HDMI 振替」を検証・記録。
- **OBS バンドルモジュール同梱範囲**（`win-dshow`/`obs-ffmpeg`/`image-source`/`text`/DistroAV）と読込パスの確定（L-002）。
- **リンク対象 OBS バージョン固定**と ABI 差異（L-002, README）。
- **P/Invoke 境界のスレッド安全性**（libobs グラフィックススレッド制約をネイティブ層に閉じる, L-001/L-002）。
