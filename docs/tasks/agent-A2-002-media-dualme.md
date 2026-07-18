---
name: agent-A2-002-media-dualme
status: doing
pid: 369701
agent_cli: sonnet
---

# 実装指示書: 2系統ME合成 & OBS的ソース管理（Phase 1）

## 概要
`Switcher.Media` を**加算的に拡張**し、単一 PGM/PVW だった `CompositorEngine` を **2系統独立ME（PGM1/PGM2、各々に PVW1/PVW2 の計4レンダーターゲット）** へ拡張する（仕様 §2.2, 親 §4.2 `POST /api/v1/program`）。あわせて `InputSourceManager` を **OBSライクなソース管理**（`id` ベースの NDI/WebCam/SRT を自由に追加/削除/並べ替え/複製）へ拡張する（§2.1）。既存の `PipLayoutCalculator` / `PipelineDescriptorFactory`（`GStreamer/`）/ `DirectX11Compositor` / `InputSource` は**再利用**し、置き換えは最小限にする。

## 前提条件（依存タスク）
- `agent-A2-001-contracts-v2`（`SourceDefinition` / `SourceType` / `ProgramBus` / `ProgramLayer` / `ProgramRequest` / 拡張 `ICompositorEngine` / 拡張 `IInputSourceManager` 等が確定していること）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.Media/`（本体拡張）
- `tests/Switcher.Media.Tests/`（テスト拡張。`.sln` 登録済みのため `.sln` は編集しない）

## 実装ステップ
1. **OBS的ソース管理へ拡張**（`InputSourceManager`）:
   - 既存の整数 `channel` キーモデルを土台に、`SourceDefinition.Id`（string）で識別する add/remove/reorder/duplicate を提供。内部で `id`⇔`channel`（序数）を対応付け、§4.3 タリー用の整数序数を安定的に払い出す。
   - `AddSource(SourceDefinition)` / `RemoveSource(string id)` / `Reorder(...)` / `Duplicate(string id)` を追加。既存 `AddSource(int, SourceProtocol, string?)` は**互換のため残置** or 内部委譲に。
   - `SourceType`（NDI/WEBCAM/SRT）と種別別設定（`NdiConfig`/`WebcamConfig`/`SrtConfig`）を `PipelineDescriptorFactory` / `GstPipelineFactory` のパイプライン記述へマッピング。NDIはSDK前提、SRTは Listener(9000)/Caller と `latency_ms` を反映。
   - 既存の「1系統の切断が他へ波及しない」スレッド分離・ノンブロッキング設計（`InputSource` 毎スレッド）を維持。
2. **2系統ME合成へ拡張**（`CompositorEngine`）:
   - PGM1/PGM2 それぞれに独立した Program/Preview シーン（計4レンダーターゲット）を保持。既存の `_previewScene`/`_programScene`（参照スワップTAKE）を**バス単位に一般化**。
   - `ICompositorEngine`（A2-001で拡張済み）を実装: `ApplyProgram(ProgramRequest)`（バス指定でレイヤ・PiP適用）、`Take(ProgramBus bus)`（当該バスの PVW→PGM 参照スワップ）、`GetProgramFrame(ProgramBus)` / `GetPreviewFrame(ProgramBus)`。
   - 既存 `ApplyPipSettings`/`Take()`/`GetProgramFrame()`/`GetPreviewFrame()` は互換シム（PGM1既定）として残すか、A2-001 が定めた新IFへ集約（採用方針は A2-001 の記述に従う）。
   - **同一ソースを両バスに載せられる**こと（レイヤは `source_id` 参照、`TryGetFrame` は id/序数で共有取得）。
   - `PipLayoutCalculator.BuildScene` を**そのまま再利用**（PiP座標・Zオーダー・不透明度・クロップ計算）。GPU合成は `IGpuCompositor`/`DirectX11Compositor` を再利用し、レンダーターゲットをバス×(PGM/PVW)で4面持つ。
3. **物理モジュールSWマトリクス反映の受け口**（§2.2）: `pgm{1,2}×src{1,2}` の載せ降ろしをホット操作できるよう、バス×ソースのトグルをエンジンAPIとして提供（実際のHID→操作変換は A2-006 が担当。ここでは操作プリミティブのみ）。
4. **シーンプリセット保存/読み込み**（§2.2）のためのシーン状態のシリアライズ可能な取得/適用APIを用意（永続化そのものは App 層でもよい。最小限、現在のバス状態を DTO で出し入れできること）。
5. `Switcher.Media.Tests` を拡張: 2系統独立性（PGM1のTAKEがPGM2に影響しない）、同一ソースの両バス載せ、id ベース add/remove/reorder、`PipLayoutCalculator` の既存テスト維持。GStreamer/DirectX 実体なしで純粋部分をテスト可能に（既存の `Fakes.cs` / 内部コンストラクタ注入パターンを踏襲）。

## 完了条件 / 検証コマンド
- 以下が成功（警告0）:
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
  ```
- 2系統ME（PGM1/PGM2 + PVW1/PVW2）が独立に合成・TAKEでき、片系統の操作が他系統に波及しないことをテストで確認。
- `SourceDefinition` ベースの add/remove/reorder/duplicate が動作し、§4.3 用の整数序数が安定的に払い出される。
- 既存 `Switcher.Media.Tests` が破壊されず全グリーン。

## 技術的な補足 / レビュー観点
- **再利用最優先**: `PipLayoutCalculator` / `PipelineDescriptorFactory` / `DirectX11Compositor` / `InputSource` を極力そのまま使う。バス次元の追加は「既存の1系統ロジックを配列/辞書化」する形が理想。
- 低遅延: 映像パスは低バッファ維持。TAKEは参照スワップ（サブms）。レイアウト変更はミリ秒反映。
- 障害隔離: 1ソース切断が全体を止めない既存設計を崩さない。ロック粒度に注意（シーン更新の `_sceneLock` をバス単位で保持）。
- スレッド安全: `ConcurrentDictionary` とロックの併用箇所を整理し、id↔序数マップの一貫性を担保。
- 検証: `.claude/review-patterns.md`「並行性」「インターフェース互換性」「命名・可読性」。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md` §2.1, §2.2 / `docs/specs/00-system-overview.md` §4.2（`POST /api/v1/program`）, §4.3
- 既存: `src/Switcher.Media/`（`CompositorEngine` / `InputSourceManager` / `InputSource` / `Compositing/PipLayoutCalculator` / `Compositing/DirectX11Compositor` / `GStreamer/PipelineDescriptorFactory`）
- 契約: `docs/tasks/agent-A2-001-contracts-v2.md`

## 採用設計メモ（実装時追記）

- **`ICompositorEngine` / `IInputSourceManager` の拡張は見送った**: 指示書は「`ICompositorEngine`（A2-001で拡張済み）を実装」と前提していたが、実際には A2-001 は `SourceDefinition` / `ProgramRequest` / `ProgramBus` / `ProgramLayer` 等のDTOのみを追加し、両インターフェース自体（`src/Switcher.Contracts/Interfaces/*.cs`）は拡張していない（A2-001の採用設計メモにある `TallyStateV2` と同様の「本タスクの編集範囲外への波及を避けるための見送り」）。
  - 理由: 本タスクの対象ディレクトリは `src/Switcher.Media/` と `tests/Switcher.Media.Tests/` のみで `src/Switcher.Contracts/` は編集範囲外。両インターフェースへ新メンバーを追加すると、それを参照する `src/Switcher.App/` / `src/Switcher.Web/`（編集範囲外）側の追従実装が必要になり、`dotnet build` が壊れる。
  - 対応: `CompositorEngine` / `InputSourceManager` は既存インターフェース実装をそのまま維持しつつ、加算的な public メンバーとして2系統ME / OBS的ソース管理APIを実装した:
    - `CompositorEngine.ApplyProgram(ProgramRequest)` / `Take(ProgramBus)` / `GetProgramFrame(ProgramBus)` / `GetPreviewFrame(ProgramBus)` / `SetSourceEnabled(ProgramBus, string, bool)`（pgm×srcホットトグル primitive） / `GetSceneSnapshot()` / `ApplySceneSnapshot(SceneSnapshot)`（シーンプリセット用DTO、`Switcher.Media.SceneSnapshot.cs` に新規定義。`Switcher.Contracts` を触れないため本体は `Switcher.Media` 名前空間に配置）。
    - `InputSourceManager.AddSource(SourceDefinition)` / `RemoveSource(string id)` / `Reorder(IReadOnlyList<string> orderedIds)` / `Duplicate(string id)`。既存 `AddSource(int, SourceProtocol, string?)` は互換のため無変更で残置（内部委譲はしていない: id⇔channel対応表と衝突しないよう `_sources` の占有チャンネルのみを共通の真実源として使う設計にした）。
    - `IFrameSource`（`Switcher.Media` 内部専用インターフェース、Contracts外）に `TryResolveChannel(string sourceId, out int channel)` を追加し、`ProgramLayer.SourceId` → 既存の `channel` ベース `PipLayoutCalculator`/`CompositedLayer`/`IGpuCompositor` へのブリッジとした。
  - 下流影響: `POST /api/v1/program` 等のWeb APIエンドポイントで2系統ME/OBS的ソース管理を公開するには、`ICompositorEngine` / `IInputSourceManager` 自体の拡張（またはインターフェースを介さず具象クラス `CompositorEngine` / `InputSourceManager` を直接参照する形へのDI変更）が別途必要。`src/Switcher.Web/` を編集範囲に持つ後続タスク（A2-003想定）が対応すること。
- **チャンネル割当**: id ベースの `AddSource`/`Duplicate` は `_sources` に空いている最小のチャンネル番号を自動採番する（`_addLock` で排他）。同一 id への再 `AddSource` は既存チャンネルを再利用し、`RemoveSource`後の再追加でない限り番号は変わらない（§4.3 タリー安定性）。
- **並び順**: `SourceInfo.Order` を新設せず既存の optional フィールド（A2-001が追加済み）を利用。`GetSources()` は `Order ?? Channel` でソートするため、レガシー `AddSource(int,...)` 呼び出し（Order未設定）は従来通り channel 順になり後方互換。

---

## レビュー指摘（要修正 / fixing）— 2026-07-18 by Opus 親

2系統ME合成（`CompositorEngine`: バス単位ロック・`ApplyProgram`/`Take(bus)`/`SetSourceEnabled`/シーンスナップショット・後方互換シム）と OBS的ソース管理は設計良好で、フルビルドもクリーン。ただし**フレーキー（非決定的）に落ちるテストが1件**あり、これは実バグを示している。

### 指摘1（必須）: `Reorder` の並び順が非決定的（データ競合）
- テスト `tests/Switcher.Media.Tests/InputSourceManagerTests.Reorder_ChangesListOrderWithoutChangingChannels` が **約20%の確率で失敗**（`Expected ["src-b","src-a"]` / `Actual ["src-a","src-b"]`）。10回中数回再現。
- 原因: `InputSource._info` を **2箇所が非アトミックな read-modify-write（`current with { ... }` → `Volatile.Write`）で更新**している:
  1. `Reorder`→`UpdateOrder(order)` が `Order` を設定。
  2. `InputSource` のバックグラウンド実行スレッド（`RunLoop`/`SetStatus`：接続・再接続・状態通知）が `Status`/`Resolution` を設定。
  これらがロックなしで同じ `_info` を上書きし合うため、バックグラウンドスレッドが `Reorder` の設定した `Order` を**取りこぼす**ことがある。結果、両ソースの `Order` が同値（または null）に化け、`GetSources()` の `OrderBy(Order ?? Channel)` がタイになり、`ConcurrentDictionary` の列挙順で非決定的に並ぶ。
- **対応（`src/Switcher.Media/` 内）**: `_info` の更新を競合しないようにする。いずれかの方針で:
  - `InputSource` の `_info` 更新（`UpdateOrder` と `SetStatus`）を**単一ロックで直列化**する、または
  - `Order` を racy な `_info` とは別に**独立した並び順フィールド/ストア**（例: `InputSourceManager` 側に `id→order` を保持し `GetSources()` でそれを参照）として持たせ、`_info` の read-modify-write 競合から切り離す。
  - いずれの場合も `OrderBy` のキーが**必ず一意で安定**になるよう、タイ時の決定的な二次キー（例: `Channel`）も添える。
- **検証**: `Reorder_ChangesListOrderWithoutChangingChannels` を含む `dotnet test tests/Switcher.Media.Tests/...` を**連続10回**実行して全て green（フレーク解消）。フルソリューションビルド警告0を維持。修正後コミットして `reviewing` へ。

（参考: 合成エンジンのバス単位ロック実装は良好。今回はソース並び順の `_info` 競合のみ是正すればよい。）

### 対応結果（2026-07-18）

- `InputSource` に `_infoLock` を新設し、`UpdateOrder`/`SetStatus` の `_info` read-modify-write（読み取り→`with`→`Volatile.Write`）を単一ロックで直列化。両メソッドとも `StatusChanged` 発火はロック外（イベントハンドラ内で再入した場合のデッドロックを避けるため）。
- `InputSourceManager.GetSources()` の `OrderBy(info => info.Order ?? info.Channel)` に `ThenBy(info => info.Channel)` を追加し、同値時も `Channel` で決定的に安定ソートされるようにした。
- 検証: `Reorder_ChangesListOrderWithoutChangingChannels` を単体で **15回連続実行**して全て green。`dotnet build HybridSwitcher.sln`（警告0）/ `dotnet test HybridSwitcher.sln` も全グリーン（Switcher.Media.Tests: 48/48）。
