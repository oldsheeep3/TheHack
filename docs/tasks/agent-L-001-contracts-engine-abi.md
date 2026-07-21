---
name: agent-L-001-contracts-engine-abi
status: done
pid: 
agent_cli: opus
---

# 実装指示書: IVideoEngine 抽象 + engine.h(C ABI) + Switcher.Engine 骨組み（Phase 0 / 直列ゲート）

## 概要
libobs 移行の**単一の映像エンジン境界**を確定するゲートタスク。以降の全タスクが依存する (1) `IVideoEngine`(C# 抽象), (2) `engine.h`(ネイティブ C ABI), (3) `Switcher.Engine`(P/Invoke 実装 + テスト用 `FakeVideoEngine`) を**同時に**確定し、`.sln` に新規プロジェクトを登録する。**この時点では Media/VirtualCam は削除しない**（App/Web の配線替え完了後に L-005 で削除）。仕様 §2.1/§2.2/§2.3。

## 前提条件（依存タスク）
- なし（Phase0 直列ゲート）。`develop`（main基点）の現状 Contracts を土台とする。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.Contracts/`（`IVideoEngine` と関連 DTO の追加。既存契約は壊さない）
- `src/Switcher.Engine/`（**新規プロジェクト**: P/Invoke 実装 + Fake）
- `tests/Switcher.Engine.Tests/`（**新規**: Fake/契約テスト）
- `native/switcher-engine/include/engine.h`（**C ABI ヘッダのみ**。実装 .c/.cpp は L-002）
- `HybridSwitcher.sln`（新規プロジェクト2つの登録のみ）

## 実装ステップ

1. **`IVideoEngine` 抽象を Contracts に追加** (`src/Switcher.Contracts/Interfaces/IVideoEngine.cs`)。既存 `ICompositorEngine`/`IInputSourceManager`/`IVirtualCameraOutput` の責務を統合した宣言的APIとする。最小面:
   - ライフサイクル: `Task StartAsync(EngineOptions opts, CancellationToken ct)` / `Task StopAsync()`。
   - ソース管理（`IInputSourceManager` 相当）: `Task AddSourceAsync(SourceDefinition src)` / `Task RemoveSourceAsync(string id)` / `Task UpdateSourceAsync(SourceDefinition src)` / `IReadOnlyList<SourceInfo> ListSources()`。
   - 2系統ME（`ICompositorEngine` 相当）: `void SetPreview(Bus bus, string? sourceId)` / `void Take(Bus bus, TransitionKind kind, int durationMs)` / `void SetPip(Bus bus, string sourceId, PipSettings pip)` / `void SetLayer(...)`。`Bus` enum = `Me1`(PGM1/PVW1) / `Me2`(PGM2/PVW2)。
   - 出力（`IVirtualCameraOutput`+`OutputRouter` 相当）: `Task ApplyOutputsAsync(OutputsRequest req)`（既存 `OutputSink` VCAM1/VCAM2/HDMI/NDI1/NDI2 契約をそのまま受ける）。
   - マルチビュー: `Task ApplyMultiviewAsync(MultiviewLayout layout)`。
   - プレビュー読み戻し（App 描画用）: `event Action<VideoTap> OnFrame`（`VideoTap` = 対象識別(`PGM1/PGM2/PVW1/PVW2/MULTIVIEW/SRC:<id>`) + BGRA バッファ + 幅/高さ/stride。スロットル前提）。`void SetTap(string target, bool enabled)`。
   - 状態通知（タリー算出用, `ITallyBroadcaster` の入力）: `event Action<EngineState> OnStateChanged`（各バスの現 PGM/PVW ソースID・接続状態）。
   - HDMI 全画面: `Task StartDisplayOutputAsync(string target, IntPtr hwnd, int displayId)` / `Task StopDisplayOutputAsync(string target)`（ネイティブ `obs_display` にウィンドウを渡す）。
   > 署名の最終形はこのタスクで確定してよいが、**既存 `SourceDefinition`/`SourceInfo`/`OutputsRequest`/`MultiviewLayout`/`PipSettings` DTO を再利用**し、新 DTO は最小限（`EngineOptions`/`Bus`/`TransitionKind`/`VideoTap`/`EngineState`）に留める。

2. **`engine.h`（C ABI）を `native/switcher-engine/include/engine.h` に定義**。`IVideoEngine` と1対1に対応する C 関数群 + コールバック型を宣言（実装は L-002）。仕様 §2.1 の叩き台を踏襲:
   ```c
   typedef struct engine_ctx engine_ctx;
   engine_ctx* engine_startup(const char* options_json);
   void        engine_shutdown(engine_ctx*);
   int  engine_add_source(engine_ctx*, const char* id, const char* type, const char* settings_json);
   int  engine_remove_source(engine_ctx*, const char* id);
   void engine_set_preview(engine_ctx*, int bus, const char* source_id);
   void engine_take(engine_ctx*, int bus, int transition_kind, int duration_ms);
   int  engine_apply_outputs(engine_ctx*, const char* outputs_json);
   int  engine_apply_multiview(engine_ctx*, const char* layout_json);
   int  engine_start_display(engine_ctx*, const char* target, void* hwnd, int display_id);
   void engine_set_tap(engine_ctx*, const char* target, int enabled);
   typedef void (*engine_frame_cb)(void* user, const char* target, const uint8_t* bgra, int w, int h, int stride);
   typedef void (*engine_state_cb)(void* user, const char* state_json);
   void engine_set_frame_cb(engine_ctx*, engine_frame_cb, void* user);
   void engine_set_state_cb(engine_ctx*, engine_state_cb, void* user);
   ```
   - **スレッド/所有権のコメントを明記**（libobs グラフィックススレッド制約・文字列は UTF-8・バッファ所有権はコールバック内のみ有効 等）。

3. **`Switcher.Engine`（新規プロジェクト, `net9.0-windows`）** を作成:
   - `NativeMethods.cs`: `engine.h` に一致する `[DllImport("switcher-engine")]` P/Invoke 宣言。
   - `LibObsVideoEngine : IVideoEngine`: P/Invoke を叩く実体。DTO ↔ JSON 変換は既存 `ProtocolJsonOptions` を再利用。**ネイティブ未配置時は明示的な `PlatformNotSupportedException`/フラグで無効化**し、アプリ起動自体は壊さない設計。
   - `FakeVideoEngine : IVideoEngine`: **ネイティブ非依存の完全インメモリ実装**（状態遷移・イベント発火をシミュレート）。Web/App/CI テストがこれを注入して緑を保つための土台。
   - 参照は `Switcher.Contracts` のみ（GstSharp/Vortice/OBS へは非依存）。

4. **`tests/Switcher.Engine.Tests`（新規）**: `FakeVideoEngine` の状態遷移（SetPreview→Take で PGM が入れ替わる、`OnStateChanged`/`OnFrame` 発火、`ApplyOutputs`/`ApplyMultiview` の受理）を検証。P/Invoke 実体はテストしない（Windows+OBS 実行時, L-002）。

5. **`.sln` 登録**: `src/Switcher.Engine/Switcher.Engine.csproj` と `tests/Switcher.Engine.Tests/*.csproj` を追加（既存プロジェクトは削除しない）。`dotnet test HybridSwitcher.sln` が新スイートを含めて緑。

## 完了条件 / 検証コマンド
```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
```
- `IVideoEngine` と関連 DTO が Contracts に定義され、下流（Web/App/Engine）が参照可能。
- `native/switcher-engine/include/engine.h` が `IVideoEngine` と対応して存在。
- `Switcher.Engine`（`LibObsVideoEngine` + `FakeVideoEngine`）と `Switcher.Engine.Tests` が sln 登録され全スイート緑・警告0。
- 既存 Media/VirtualCam/Web/App/Atem/Hid/Contracts テストは**無改変で緑**（この段階では削除・配線替えをしない）。

## 技術的な補足 / レビュー観点
- **契約は既存 DTO を最大限再利用**。`OutputSink`/`MultiviewLayout` の JSON 契約（スネークケース）を壊さない。
- P/Invoke は `net9.0-windows` に閉じ、`FakeVideoEngine` で CI 緑を担保（Media/VirtualCam の既存方針を踏襲）。
- **GPL 境界**: `Switcher.Engine` は OBS/libobs に直接依存しない（依存は P/Invoke 宣言と `engine.h` 準拠のみ）。
- 検証: `.claude/review-patterns.md`「インターフェース互換性」「型/シリアライズ」「境界」「リソース管理」。

## 参照
- 仕様: `docs/specs/libobs-engine-migration.md` §2.1/§2.2/§2.3
- 既存: `src/Switcher.Contracts/Interfaces/`（`ICompositorEngine`/`IInputSourceManager`/`IVirtualCameraOutput`/`IDeviceQueryService`/`ITallyBroadcaster`）, `ProtocolJsonOptions.cs`, `OutputsRequest.cs`, `MultiviewLayout.cs`, `SourceDefinition.cs`, `PipSettings.cs`
- 計画: `docs/tasks/orchestration-plan-libobs-migration.md`
