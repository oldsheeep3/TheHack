---
name: agent-A3-002-media-devices-mv
status: done
pid: 
agent_cli: sonnet
---

# 実装指示書: メディア — デバイス列挙 / マルチビュー region 合成 / SRTホスト算出（Phase 1）

## 概要
`Switcher.Media` を**加算的に拡張**し、(a) Webcam/NDI の**デバイス列挙**（`IDeviceQueryService` 実装）、(b) マルチビューの**region（矩形結合）合成**対応、(c) **SRT Listener のホスト/ポート算出**を提供する（仕様 §2.3/§2.6/§2.7）。既存 `CompositorEngine` / `InputSourceManager` / `PipelineDescriptorFactory` を**再利用・拡張**する。

## 前提条件（依存タスク）
- `agent-A3-001-contracts-mvout`（`IDeviceQueryService` / `DeviceInfo` / `SrtSetupInfo` / `MultiviewRegion` / `MultiviewLayoutNormalizer` が確定）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.Media/`（本体拡張）
- `tests/Switcher.Media.Tests/`（テスト拡張。`.sln` は編集しない）

## 実装ステップ
1. **デバイス列挙サービス**（`IDeviceQueryService` 実装, 例 `Devices/DeviceQueryService`）:
   - **WEBCAM**: OSキャプチャデバイスを列挙（DirectShow/Media Foundation。既存 GStreamer/DirectShow 依存の範囲内）。`DeviceInfo.Id`＝デバイスID、`Name`＝表示名、`Formats`＝可能なら `"1920x1080@30"` 等の候補。
   - **NDI**: NDI find でネットワーク上の NDI ソース名を列挙し `DeviceInfo`（`Id`=`Name`=NDIソース名, `Formats`=null）。NDI SDK 未検出時は**空配列**を返す（例外を投げない。導線メッセージは Web/App が表示）。
   - **プラットフォーム抽象化**: 実列挙は OS 依存の内部プロバイダ（例 `IWebcamDeviceProvider` / `INdiSourceProvider`）に委譲し、**テストではフェイク注入**で列挙結果を検証できるようにする（Windows実行時依存をテストから排除）。
   - `GetSrtSetupAsync`: §ステップ3参照。
2. **マルチビュー region 合成**（`CompositorEngine` 周辺を拡張）:
   - マルチビュー用プレビュー合成を `MultiviewLayoutNormalizer.ToRegions(layout)` の各 `MultiviewRegion`（`Row/Col/RowSpan/ColSpan/Content`）に基づき、**結合セルは対応する矩形領域に1枚を拡大配置**するよう一般化する（従来の16均等セル配置を region 駆動へ）。
   - `Content` 種別（`PGM1/PGM2/PVW1/PVW2/SRC:<id>/EMPTY`）ごとにソースフレームを解決。`EMPTY` は黒。枠色/ラベル用メタ（PGM=赤/PVW=緑）は既存のマルチビュー供給経路に合わせて保持（描画は App）。
   - 既存の 4x4 `cells` 経路は正規化により**そのまま動作**すること（後方互換）。
3. **SRT Listener ホスト/ポート算出**（`GetSrtSetupAsync`）:
   - PC の LAN IPv4 アドレス候補を列挙（ループバック/リンクローカル除外、複数NIC対応）。
   - `SrtSetupInfo` を組み立て: `ListenerPort`＝既定 SRT Listener ポート（`ProtocolConstants` 由来, 既定 9000）、`HostCandidates`＝IPv4候補、`RecommendedUrl`＝先頭候補で `srt://<ip>:<port>`、`RecommendedLatencyMs`＝40、`InstructionsText`＝Listener/Caller の使い分けと ATEM 側入力手順の短文。
   - IP 列挙も抽象化し（例 `ILocalAddressProvider`）、テストでフェイク注入して決定的に検証。
4. `Switcher.Media.Tests` を拡張: デバイス列挙（webcam/ndi フェイクプロバイダで正常/空/エラー隔離）、region 合成の配置解決（2x2結合が対応矩形へ拡大される・`cells`旧形式が16×1x1として同一結果になる後方互換）、SRTホスト算出（フェイクIPプロバイダで `HostCandidates`/`RecommendedUrl` を検証）。既存 `CompositorEngine`/`InputSourceManager`/`PipLayoutCalculator` 系テストを維持。

## 完了条件 / 検証コマンド
- 以下が成功（警告0）:
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
  ```
- `IDeviceQueryService` 実装が webcam/ndi 列挙と SRT情報取得を提供（NDI SDK 未検出でも例外なく空配列）。
- マルチビュー合成が region（矩形結合）を反映し、旧 `cells` 形式も同一挙動（後方互換）。
- `SrtSetupInfo` が LAN IP/ポート/推奨URL/手順を返す。
- 既存 `Switcher.Media.Tests` を含む全スイートがグリーン。

## 技術的な補足 / レビュー観点
- **OS依存の隔離**: 実デバイス列挙・IP列挙はプロバイダIFへ委譲し、`Switcher.Media.Tests` はフェイクで OS 非依存に。Windows実行時依存はプロジェクト内に閉じる。
- **障害隔離**: NDI SDK 未導入・デバイス0件・列挙失敗は空配列/明示状態で返し、合成や他機能を止めない。
- **再利用最優先**: `CompositorEngine` を置き換えず region 対応へ拡張。正規化は Contracts の `MultiviewLayoutNormalizer` を使い重複実装しない。
- 低遅延: マルチビュー合成は既存のGPU/供給経路方針を踏襲。
- 検証: `.claude/review-patterns.md`「リソース管理」「並行性」「境界（結合矩形/0件/複数NIC）」。

## 参照
- 仕様: `docs/specs/multiview-output-revision.md` §2.3/§2.6/§2.7, §4.3
- 契約: `docs/tasks/agent-A3-001-contracts-mvout.md`
- 既存: `src/Switcher.Media/`（`CompositorEngine.cs` / `InputSourceManager.cs` / `GStreamer/PipelineDescriptorFactory.cs`）
