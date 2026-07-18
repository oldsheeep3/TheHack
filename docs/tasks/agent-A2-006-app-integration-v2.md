---
name: agent-A2-006-app-integration-v2
status: done
pid: 555107
agent_cli: sonnet
---

# 実装指示書: アプリ統合 v2（配線・4x4UI・出力・ATEM配線・HID）（Phase 3 / 直列統合）

## 概要
`Switcher.App` を**改訂配線**し、全モジュールを統合する。2系統ME（PGM1/PGM2 + PVW1/PVW2）、**4x4 コンフィギュラブル・マルチビューUI**、出力割当（仮想カメラ×2 + HDMI全画面）、**ATEMルーティング（既存 `Switcher.Atem` を無改変で再利用）**、モジュール割付、**HID入力サービス**の配線、PCホストの設定Web + Pico ネットワーク設定を実装する（仕様 §2.2〜§2.8, 親 §4）。中核 `AppOrchestrator` を2系統・OBS的ソース・HID経路へ拡張し、`ServiceCollectionExtensions`(DI) / `FramePumpService` / `AppHostService` / WPF UI を改修する。**前回イテレーションの教訓に従い、全テストプロジェクトが `.sln` に登録され `dotnet test HybridSwitcher.sln` で全スイート実行されることを最終確認**する。

## 前提条件（依存タスク）
- `agent-A2-002-media-dualme`（2系統ME + OBS的ソース管理）
- `agent-A2-003-web-api-v2`（新API群 + 2系統タリー）
- `agent-A2-004-hid-io`（`Switcher.Hid` 本体）
- `agent-A2-005-output-dualcam-hdmi`（仮想カメラ×2 + HDMI全画面）
- （契約基盤: `agent-A2-001-contracts-v2`）

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.App/`（本体改修）
- `HybridSwitcher.sln`（**テストプロジェクト登録の最終確認・不足があれば追加のみ**）
- `tests/Switcher.App.Tests/`（**新規**: 統合/配線のテストプロジェクトを作成し `.sln` に登録。中核 `AppOrchestrator` の2系統タリー・入力直列化を検証）

## 実装ステップ
1. **DI 配線改修**（`Composition/ServiceCollectionExtensions`）:
   - `InputSourceManager`/`CompositorEngine` を2系統ME版へ配線（A2-002 の新IF/コンストラクタ）。
   - `Switcher.Hid` の `HidInputService`/`HidBacklightService`（`IHidDevice` 実装注入）を登録。
   - `Switcher.VirtualCam` の `DualVirtualCameraOutput`/`OutputRouter`/`HdmiFullscreenOutput` を登録。
   - `WebHost` を新API群のサービスIF（拡張 `ISwitcherConfigService`）で構築（A2-003）。
   - **ATEM は既存 `AtemController`/`ButtonCommandMapping`/`IAtemController` を無改変で再利用**して登録（`Switcher.Atem` は変更しない）。
   - `TallyBroadcaster` を2系統タリーで配線（A2-001 採用設計に従う）。
2. **中核オーケストレータ改修**（`Orchestration/AppOrchestrator`）:
   - 拡張 `ISwitcherConfigService` を実装: sources CRUD / program(バス指定) / multiview / outputs / modules / atem / pico-network を中核状態へ反映（A2-003 のエンドポイントが委譲してくる先）。
   - **HID入力の受け口**: `HidInputService` の正規化イベント（SWエッジ/VR）を購読し、`ModuleMapping`（`PUT /api/v1/modules`）に従って「PGMバス載せ降ろし（`ICompositorEngine` 操作）」または「ATEM中継（`AtemController.SendCommand`/コマンド）」へ変換。既存の `Enqueue(ButtonEvent)`/`CompositeButtonMapping` パターンを2系統・HIDへ一般化。
   - **入力直列化**（メイン/サブ同時操作安全, §5）: 既存 `_stateLock` パターンを維持し、HID経路・Web経路・UI操作すべてを直列化。
   - **2系統タリー算出/配信**: PGM1/PGM2/PVW1/PVW2 の active ソース序数を集計し `TallyStateV2` を `Publish`。UI へも `TallyChanged` で通知。
   - **バックライト算出の配線**: 現在の PGM/PVW 状態 + `ModuleMapping` を `Switcher.Hid` の `BacklightCalculator` に渡し、`HidBacklightService` で送出。
3. **フレームポンプ改修**（`Services/FramePumpService`）: 2系統（PGM1/PGM2）の Program/Preview フレームを取得し、`OutputRouter` の割当に従って VCAM1/VCAM2/HDMI へ分配。4x4マルチビュー用に各セル（PGM1/PGM2/PVW1/PVW2/SRCn）のプレビューフレームも供給。
4. **4x4 コンフィギュラブル・マルチビューUI**（`MainWindow` / `ViewModels`）:
   - 16セルのマトリクス表示。各セルに `PGM1`/`PGM2`/`PVW1`/`PVW2`/`SRC:<id>`/`EMPTY` を自由配置（`PUT /api/v1/multiview` と同一状態を反映/保存）。
   - ラベル・トリム表示・PGM/PVW枠色（赤/緑）。ダーク基調（§5）。
   - ソースドック（追加/削除/並べ替え）・プロパティ（種別別設定）・出力割当・モジュール割付パネル。
5. **全画面プロジェクタ出力**（`ProjectorWindow`）: 指定ディスプレイに PGM1/PGM2 等を全画面表示、**マウスカーソル非表示**。A2-005 の `HdmiFullscreenOutput`/`OutputRouter` と協調（ウィンドウ生成/ディスプレイ配置は本タスク、提示/カーソル制御ロジックは A2-005 の型を利用）。
6. **設定WebのPCホスト + Pico ネットワーク設定**: `WebHost` を App から起動し、`PUT /api/v1/pico/network`（§4.6）で受けた `PicoNetworkConfig` を HID フィーチャーレポート/無線制御チャネル経由で Pico へ投入する配線（実投入は将来拡張枠でも可、配線点と永続化を用意）。設定の永続化（`Configuration/AppConfig`/`AppConfigLoader` 拡張）で「PCが設定の正」を担保。
7. **WSボタン経路の非推奨**: 既存 WS 経路は残置（無線代替時のみ）。既定の制御経路は HID とする配線・コメントを明記。
8. **`.sln` 最終確認**: `Switcher.Hid.Tests`（A2-001登録）に加え、本タスクで作る `Switcher.App.Tests` を `.sln` に登録。`dotnet test HybridSwitcher.sln` の出力で**全テストプロジェクト（Contracts/Media/Web/Atem/VirtualCam/Hid/App）が実行対象に含まれる**ことを確認。
9. **E2E スモーク**: 起動→ソース追加(NDI/WebCam/SRTのいずれかフェイク)→PGM1/PGM2へレイヤ適用→TAKE→タリー配信→出力割当→HID入力(フェイク)でSW反映/バックライト送出、が例外なく通ることを統合テストで確認（実HW/実GStreamer/実HIDは不要、フェイク注入）。

## 完了条件 / 検証コマンド
- 以下が成功（警告0）:
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
  ```
- `dotnet test HybridSwitcher.sln` が **7つのテストプロジェクト（Contracts/Media/Web/Atem/VirtualCam/Hid/App）を実行**（登録漏れゼロ）。
- 2系統ME・4x4マルチビュー・出力割当・ATEM配線・モジュール割付・HID入力/バックライトが DI で解決され、統合スモークが例外なく通る。
- 入力直列化により、Web/HID/UI 同時操作でタリーが不整合にならない（テストで検証）。
- `Switcher.Atem` が無改変であること（差分なし）。

## 技術的な補足 / レビュー観点
- **直列統合タスク**。並列フェーズの成果を統合するだけでなく、DI/中核/UI の整合を最終担保する。既存 `AppOrchestrator` の「`_stateLock` で全変更を直列化」設計を2系統・HIDへ**一般化**して壊さない。
- **ATEMは再利用のみ**: `Switcher.Atem`（`AtemController`/`IAtemController`/`ButtonCommandMapping`/`AtemAction`）を変更しない。Web DTO(`AtemConfig`/`AtemButtonMapping`) → 既存 `AtemCommandMapping`/`ButtonCommandMapping` への変換を App 側で行う。
- **教訓の遵守**: 新規テストプロジェクトの `.sln` 登録漏れは「無検査素通り」を招く。登録を必ず確認する（本タスクの完了条件）。
- WPF/HID/DirectX の実行時依存はテストではフェイクへ差し替え、CI(ヘッドレス)でビルド/テストが通ること。
- 低遅延・障害隔離（1ソース/1モジュール/1出力の障害が全体を止めない）を配線で担保。
- 検証: `.claude/review-patterns.md`「配線/DI」「並行性・直列化」「インターフェース互換性」「テスト網羅」。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md` §2.2〜§2.8, §5 / `docs/specs/00-system-overview.md` §4.1〜§4.6, §5
- 既存: `src/Switcher.App/`（`Composition/ServiceCollectionExtensions` / `Orchestration/AppOrchestrator` / `Services/FramePumpService` / `Services/AppHostService` / `MainWindow` / `ProjectorWindow` / `ViewModels/` / `Configuration/AppConfig`）, `src/Switcher.Atem/`（無改変再利用）
- 前計画: `docs/tasks/agent-A-004-app-integration.md`
- 契約/依存: `docs/tasks/agent-A2-001-contracts-v2.md` 〜 `agent-A2-005-output-dualcam-hdmi.md`
