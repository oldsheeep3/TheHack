---
name: agent-A3-001-contracts-mvout
status: done
pid: 
agent_cli: sonnet
---

# 実装指示書: 契約拡張 — 出力(NDI)/マルチビュー(region)/デバイス列挙/SRT情報（Phase 0 / 直列ゲート）

## 概要
`Switcher.Contracts` を**加算的に拡張**し、本改訂の全タスクが依存する共有DTO/IFを確定する（仕様 §4.1）。**後方互換必須**（既存の `OutputSink` / `OutputAssignment` / `MultiviewLayout(cells)` / `SourceDefinition` と既存テストを壊さない）。新規プロジェクトは作らず `.sln` は編集しない。

## 前提条件（依存タスク）
- なし（Phase0 直列ゲート）。v2 の `Switcher.Contracts` 資産を土台とする。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.Contracts/`（本体拡張）
- `tests/Switcher.Contracts.Tests/`（ラウンドトリップ/バリデーション用テスト拡張。`.sln` 登録済みのため `.sln` は編集しない）

## 実装ステップ
1. **出力 sink/割当の拡張**（`OutputsRequest.cs`）:
   - `OutputSink` に **`Ndi1`(`"NDI1"`) / `Ndi2`(`"NDI2"`)** を追加（既存 `Vcam1/Vcam2/Hdmi` は順序・名称維持）。
   - `OutputAssignment` に **`string? NdiName`** を追加（NDI sink の送出名。他 sink では null）。既存の positional record 引数の**末尾に追加**し、既存呼び出し/シリアライズ互換を保つ（JSONは `ndi_name`、スネークケースは既存 `ProtocolJsonOptions` に従う）。
2. **マルチビューの region 化**（`MultiviewLayout.cs`, 後方互換）:
   - 新型を追加: `MultiviewGrid(int Rows, int Cols)`、`MultiviewRegion(int Row, int Col, int RowSpan, int ColSpan, string Content)`。
   - `MultiviewLayout` を **`Cells`(既存, nullable 可) と `Grid`(nullable) / `Regions`(nullable) の両表現を持つ**よう拡張する。`cells` のみ指定された旧ペイロードも、`regions` 指定の新ペイロードも受理できる形にする（どちらか一方が入る）。
   - **正規化ヘルパー**を提供: `MultiviewLayout` → `IReadOnlyList<MultiviewRegion>`（`cells` 形式は 4x4・各1x1 の region 列へ、`regions` 形式はそのまま）。App/Web/Media が共通利用できるよう Contracts に置く（例: `MultiviewLayoutNormalizer.ToRegions(layout)`）。`Content` トークンは `PGM1/PGM2/PVW1/PVW2/SRC:<id>/EMPTY`。
   - 既存 `MultiviewLayout(IReadOnlyList<string> Cells)` を使うコードが壊れないよう配慮（コンストラクタ/プロパティ互換）。
3. **デバイス列挙 DTO/IF**（新規ファイル）:
   - `DeviceInfo(string Id, string Name, IReadOnlyList<string>? Formats)` を追加（webcam の解像度/FPS 候補は `Formats`、NDI は null 可）。
   - 列挙対象種別 `DeviceQueryType`（`"WEBCAM"|"NDI"`）を追加（SRT は列挙対象外）。
   - **新IF** `src/Switcher.Contracts/Interfaces/IDeviceQueryService.cs`:
     ```csharp
     public interface IDeviceQueryService
     {
         Task<IReadOnlyList<DeviceInfo>> EnumerateAsync(DeviceQueryType type, CancellationToken ct = default);
         Task<SrtSetupInfo> GetSrtSetupAsync(CancellationToken ct = default);
     }
     ```
     Media が実装（A3-002）、Web エンドポイントが利用（A3-003, テストはフェイク）、App が実体配線（A3-005）。
4. **SRTセットアップ情報 DTO**（新規ファイル, 仕様 §2.7）:
   - `SrtSetupInfo(int ListenerPort, IReadOnlyList<string> HostCandidates, string RecommendedUrl, int RecommendedLatencyMs, string InstructionsText)`。
   - `HostCandidates` は PC の LAN IPv4 候補（複数NIC）。`RecommendedUrl` は例 `srt://192.168.1.50:9000`。
5. **JSON 互換確認**（`ProtocolJsonOptions`/`ProtocolConstants` 既存を利用）: 追加 enum メンバー・record に `JsonStringEnumMemberName` / スネークケースが一貫適用されること。既定 SRT Listener ポートを定数化する場合は `ProtocolConstants` に追記（例 `SrtListenerPort = 9000`）。
6. `Switcher.Contracts.Tests` を拡張: `OutputSink` の NDI1/NDI2 と `ndi_name` のラウンドトリップ、`MultiviewLayout` の `cells`旧形式・`regions`新形式の双方向シリアライズ、`MultiviewLayoutNormalizer.ToRegions` の正規化（cells→16×1x1 / regions→そのまま）、`DeviceInfo`/`SrtSetupInfo` のラウンドトリップ。既存 `ContractsV2RoundTripTests` を維持。

## 完了条件 / 検証コマンド
- 以下が成功（警告0）:
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
  ```
- `OutputSink` に NDI1/NDI2、`OutputAssignment.NdiName` が追加され、旧 `outputs` JSON も受理可能。
- `MultiviewLayout` が `cells` 旧形式と `regions` 新形式の両方をシリアライズ/デシリアライズでき、`ToRegions` 正規化が動作。
- `IDeviceQueryService` / `DeviceInfo` / `SrtSetupInfo` が定義され、下流が参照可能。
- 既存 `Switcher.Contracts.Tests` を含む全スイートがグリーン。

## 技術的な補足 / レビュー観点
- **後方互換が最優先**。positional record への引数追加は末尾のみ。旧 `cells` ペイロードの受理を必ずテストで担保。
- Web↔Media の直接依存を避けるため、デバイス/SRT クエリIFは Contracts に置く（既存 `Interfaces/` の慣習に合わせる）。
- 正規化ロジックは Contracts に一元化し、Web バリデータ・Media 合成・App UI で重複実装しない。
- 検証: `.claude/review-patterns.md`「インターフェース互換性」「型/シリアライズ」「境界」。

## 参照
- 仕様: `docs/specs/multiview-output-revision.md` §4.1/§4.2
- 既存: `src/Switcher.Contracts/`（`OutputsRequest.cs` / `MultiviewLayout.cs` / `SourceDefinition.cs` / `Interfaces/` / `ProtocolJsonOptions.cs` / `ProtocolConstants.cs`）
- 計画: `docs/tasks/orchestration-plan-multiview-output-v3.md`
