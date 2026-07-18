---
name: agent-A3-005-app-ui-integration
status: done
pid: 
agent_cli: sonnet
---

# 実装指示書: アプリ統合 — トレイ/ドラッグ結合UI/ソース3段UI/警告/全画面/配線（Phase 3 / 直列統合）

## 概要
`Switcher.App` を**改訂配線**し、本改訂の全7要件をUI/挙動として統合する。すなわち (1)全画面出力のディスプレイ指定＋同一画面警告、(2)操作画面ディスプレイをトレイ右クリック等から変更・永続化、(3)マルチビューの矩形結合ドラッグUI、(4)マルチビュー全画面（独立ウィンドウ）、(5)NDI出力割当UI、(6)ソース追加3段UI＋デバイス列挙、(7)SRTセットアップ/ATEMホスト名表示。前フェーズ（A3-002/003/004）の成果を配線し、`IDeviceQueryService` 実体を DI で提供する。

## 前提条件（依存タスク）
- `agent-A3-002-media-devices-mv`（`IDeviceQueryService` 実装 / region 合成 / SRTホスト算出）
- `agent-A3-003-web-devices-validate`（devices/srt-setup API / バリデータ拡張）
- `agent-A3-004-output-ndi-mvfull`（NDI出力×2 / マルチビュー全画面提示）
- （契約基盤: `agent-A3-001-contracts-mvout`）

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.App/`（本体改修）
- `tests/Switcher.App.Tests/`（テスト拡張。`.sln` は編集しない）

## 実装ステップ
1. **DI 配線**（`Composition/ServiceCollectionExtensions`）:
   - `Switcher.Media` の `IDeviceQueryService` 実体を登録し、`WebHost`（A3-003）へ注入。App UI からも同 IF を利用してデバイス/SRT情報を取得。
   - `Switcher.VirtualCam` の NDI 出力型・拡張 `OutputRouter`・マルチビュー全画面提示型を登録。既存 `DualVirtualCameraOutput`/`HdmiFullscreenOutput` 配線を維持。
2. **要件1: 全画面出力のディスプレイ指定＋同一画面警告**:
   - 出力割当パネル（`ViewModels/OutputAssignmentRowViewModel` + UI）に **HDMI のディスプレイ選択ドロップダウン**（`Screen.AllScreens` を列挙、解像度/主モニター表示）を追加。
   - 割当確定時、**出力ディスプレイ == 操作画面ディスプレイ**（§ステップ3の `OperatorDisplayIndex`／メインウィンドウ実表示位置から判定）なら**警告ダイアログ/バナー（続行可）** `[このまま続行]/[キャンセル]` を表示。ブロックしない。
   - `ProjectorWindow` の attach を選択 `DisplayId` に従わせる（既存 `ShowOnConfiguredDisplay`/`OnSourceInitialized` を利用）。
3. **要件2: 操作画面ディスプレイ変更（トレイ右クリック等）**:
   - `AppConfig` に **`OperatorDisplayIndex`** を追加（既定 0）し、`AppConfigLoader`/永続化に反映（既存 `ProjectorDisplayIndex` に倣う）。
   - `Services/TrayIconService` の右クリックメニューに **「操作画面を移動 ▸ Display N」** サブメニュー（`Screen.AllScreens` 動的生成）を追加し、メインウィンドウを選択ディスプレイへ移動＋ `OperatorDisplayIndex` を更新・永続化。メインウィンドウのボタンからも同操作を可能に。
4. **要件3: マルチビューの矩形結合ドラッグUI**:
   - `MultiviewControl`（現状16セルの `MultiviewCellViewModel`）を**ドラッグで矩形範囲選択→結合/解除**できるUIへ拡張。結合状態は `MultiviewRegion`（rowspan/colspan）としてモデル化し、`MultiviewLayout`(regions) を `PUT /api/v1/multiview` 相当で `AppOrchestrator` に反映・保存。
   - `MultiviewCellViewModel` を region 対応（結合セルは対応矩形を1枚で表示、ラベル/枠色 PGM=赤/PVW=緑 を維持）へ拡張。矩形以外の選択は結合不可（ガード）。
   - 旧 `cells` レイアウトの読み込み（`LoadMultiviewLayout`）を維持（後方互換, 正規化は Contracts の `MultiviewLayoutNormalizer`）。
5. **要件4: マルチビュー全画面（独立ウィンドウ）**:
   - `MultiviewFullscreenWindow`（新規）を追加し、A3-004 のマルチビュー全画面提示型でマルチビュー合成を指定ディスプレイへ全画面表示（カーソル非表示・`Esc`で解除）。
   - 起動導線: **トレイ右クリック「マルチビュー全画面 ▸ Display N」**＋メインウィンドウのボタン。全画面先が操作画面ディスプレイと同一なら要件1と同じ**同一画面警告（続行可）**を表示。全画面中もライブ更新を継続。
6. **要件5: NDI出力割当UI**:
   - 出力割当パネルに **NDI1/NDI2 の行**（source=PGM1/PGM2、`ndi_name` 入力、既定 `SWITCHER PGM1`/`SWITCHER PGM2`）を追加し、`PUT /api/v1/outputs`（`OutputRouter.ApplyOutputs`）へ反映。
7. **要件6: ソース追加3段UI＋デバイス列挙**:
   - ソースドックの追加UIを **①名前(自由入力) ②種別(ドロップダウン WEBCAM/NDI/SRT) ③デバイス選択(ドロップダウン)** に再構成。
   - 種別 WEBCAM/NDI では `IDeviceQueryService.EnumerateAsync` の結果をドロップダウン表示（**再スキャン**ボタン、0件時は導線メッセージ）。選択を `SourceDefinition`（`webcam.device_id`/`ndi.source_name`）へマッピングして追加。
   - 種別 SRT ではデバイスドロップダウンの代わりに**接続設定入力（モード Listener/Caller・URL・latency）**＋要件7のヘルパーを表示し、`srt` config を構築。
8. **要件7: SRTセットアップ/ATEMホスト名表示**:
   - SRT選択時（およびヘルプ導線）に `IDeviceQueryService.GetSrtSetupAsync`（or `GET /api/v1/srt/setup`）の `SrtSetupInfo` を表示: コピー可能なホスト候補/推奨URL（例 `srt://192.168.1.50:9000`）、latency 目安、Listener/Caller 手順。
9. **E2E スモーク**（`tests/Switcher.App.Tests`）: 起動→デバイス列挙(フェイク)→ソース3段追加→マルチビュー矩形結合→`PUT /api/v1/multiview`(regions)反映→出力割当(NDI含む)→マルチビュー全画面(フェイク提示)→同一画面警告判定、が例外なく通る統合テスト。既存 `AppOrchestratorTests`/`EndToEndSmokeTests` を維持。実HW/実NDI/実D3D は不要（フェイク注入）。

## 完了条件 / 検証コマンド
- 以下が成功（警告0）:
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
  ```
- `dotnet test HybridSwitcher.sln` が **7スイート（Contracts/Media/Web/Atem/VirtualCam/Hid/App）を実行**し全グリーン。
- 7要件が UI/挙動として配線され、統合スモークが例外なく通る。
- `OperatorDisplayIndex` が永続化・復元され、同一画面警告が正しく判定される（テスト検証）。
- 既存 UI/配線・`cells` 形式マルチビュー・既存 sink が回帰なく動作（後方互換）。

## 技術的な補足 / レビュー観点
- **直列統合タスク**。前フェーズ成果の配線と、UI/DI/中核状態の整合を最終担保する。既存 `AppOrchestrator` の `_stateLock` 直列化を壊さず、マルチビュー/出力/ソース変更をすべて直列化。
- **再利用最優先**: `TrayIconService`/`ProjectorWindow`/`MultiviewCellViewModel`/`OutputAssignmentRowViewModel` を拡張し、全画面提示は A3-004 の型を利用（二重実装しない）。
- 同一画面警告は判定ロジック（`OperatorDisplayIndex` vs 出力 `DisplayId`）をテスト可能な形（UIから分離）に切り出す。
- WPF/DirectX/NDI の実行時依存はテストではフェイクへ差し替え、ヘッドレスCIでビルド/テストが通ること。
- 障害隔離（1ソース/1出力の障害が全体を止めない）を配線で担保。
- 検証: `.claude/review-patterns.md`「配線/DI」「並行性・直列化」「インターフェース互換性」「テスト網羅」。

## 参照
- 仕様: `docs/specs/multiview-output-revision.md` §2.1〜§2.7, §4.3, §6
- 契約/依存: `docs/tasks/agent-A3-001-contracts-mvout.md` 〜 `agent-A3-004-output-ndi-mvfull.md`
- 既存: `src/Switcher.App/`（`Composition/ServiceCollectionExtensions` / `Orchestration/AppOrchestrator` / `Services/TrayIconService` / `Services/FramePumpService` / `MainWindow.xaml(.cs)` / `ProjectorWindow.xaml(.cs)` / `ViewModels/MultiviewCellViewModel` / `ViewModels/OutputAssignmentRowViewModel` / `ViewModels/SourceTileViewModel` / `Configuration/AppConfig`(`AppConfigLoader`)）
- 前計画: `docs/tasks/agent-A2-006-app-integration-v2.md`
