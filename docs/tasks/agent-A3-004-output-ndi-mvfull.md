---
name: agent-A3-004-output-ndi-mvfull
status: done
pid: 
agent_cli: sonnet
---

# 実装指示書: 出力 — NDI送出×2 & マルチビュー全画面提示（Phase 2）

## 概要
`Switcher.VirtualCam` を**加算的に拡張**し、(a) **NDI 出力×2（NDI1/NDI2, PGM1/PGM2, 送出名設定）** と (b) **マルチビュー全画面提示**（独立ウィンドウ用の提示ロジック）を提供する（仕様 §2.4/§2.5）。既存 `OutputRouter` / `HdmiFullscreenOutput` / `Direct3DSwapChainOutput` / `ISwapChainOutput` / `ICursorVisibility` を**最大限再利用**する。ウィンドウ生成/ディスプレイ配置は App(A3-005) の責務、本タスクは**提示ロジック＋カーソル制御＋ルーティング**まで。

## 前提条件（依存タスク）
- `agent-A3-001-contracts-mvout`（`OutputSink`(NDI1/NDI2)/`OutputAssignment.NdiName` 確定）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.VirtualCam/`（本体拡張）
- `tests/Switcher.VirtualCam.Tests/`（テスト拡張。`.sln` は編集しない）

## 実装ステップ
1. **NDI 出力型**（例 `Ndi/NdiOutput` + IF `INdiOutput`）:
   - 指定した送出名（`ndi_name`）で NDI Sender を開き、フレームを送出する型を追加。**プラットフォーム抽象化**: 実 SDK 呼び出しは内部プロバイダ（例 `INdiSenderDevice`）へ委譲し、**テストはフェイク注入**で Open/Send/Close を検証（Windows/NDI SDK 依存をテストから排除）。
   - NDI SDK 未検出時は**送出を無効化**（no-op で開始せず、状態を保持）。例外で他 sink を巻き込まない。
   - 遅延Open・解像度変化時の再オープン等、既存 `VirtualCameraOutput` の耐障害パターンに倣う。
2. **OutputRouter への NDI ルーティング追加**（`OutputRouter.cs`）:
   - `ApplyOutputs(OutputsRequest)` を拡張し、**NDI1/NDI2 sink → PGM1/PGM2 source** の割当を保持。既定 **PGM1→NDI1, PGM2→NDI2**、`NdiName` 既定は `SWITCHER PGM1` / `SWITCHER PGM2`。
   - フレーム分配（`RouteFrame`）で、対応 PGM フレームを NDI 出力へ供給。既存 VCAM/HDMI 経路と**不要コピーを避けて**併存（NV12変換はVCAM用、HDMI/NDIは別経路）。
   - **1 sink 障害が他 sink を止めない**隔離を維持。
3. **マルチビュー全画面提示**（既存 HDMI 全画面型の再利用）:
   - マルチビュー合成フレームを指定ディスプレイへ全画面提示する経路を用意する。**新規の重複実装を避け**、既存 `HdmiFullscreenOutput`(`ISwapChainOutput`+`ICursorVisibility`) を **マルチビュー用にも利用可能**にする（例: マルチビュー用インスタンスを別途生成できるよう、ファクトリ/命名を整理）。カーソル非表示・`display_id`・fullscreen フラグの反映は HDMI と同一。
   - マルチビュー全画面は**出力割当(sink)とは別系統**（`OutputRouter` の VCAM/HDMI/NDI 割当には含めない）。提示先ウィンドウのHWND attach/配置は App(A3-005) が行い、本タスクは提示・カーソル制御ロジックを提供。
4. `Switcher.VirtualCam.Tests` を拡張: NDI出力2系統が独立に Open/Send/Close（フェイク `INdiSenderDevice` 2台, 送出名反映, SDK未検出でno-op隔離）、`OutputRouter` の NDI 割当解決（既定/変更/不正 source）、マルチビュー全画面提示が `ISwapChainOutput`/`ICursorVisibility` フェイクでカーソル非表示・display_id・fullscreen を反映。既存 `DualVirtualCameraOutputTests`/`OutputRouterTests`/`Nv12FrameConverterTests`/`VirtualCameraOutputTests` を維持。

## 完了条件 / 検証コマンド
- 以下が成功（警告0）:
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
  ```
- NDI1/NDI2 が別送出名で独立に動作し、`OutputRouter` が PGM→NDI 割当を解決（既定 PGM1→NDI1, PGM2→NDI2）。NDI SDK 未検出でも他 sink を止めない。
- マルチビュー全画面提示が既存 HDMI 全画面型の再利用で実現し、カーソル非表示/display_id/fullscreen を反映。
- 既存 `Switcher.VirtualCam.Tests` を含む全スイートがグリーン。

## 技術的な補足 / レビュー観点
- **再利用最優先**: HDMI全画面（`HdmiFullscreenOutput`/`Direct3DSwapChainOutput`/`ICursorVisibility`）をマルチビュー全画面へ流用し、二重実装しない。責務境界（提示ロジック＝本タスク / ウィンドウ生成・配置＝A3-005）を本ファイルに明記。
- **障害隔離・低遅延**: 1 NDI/1 HDMI/1 VCAM の障害が他 sink・合成を止めない。フレーム分配の不要コピー削減。
- Windows/NDI SDK 依存はプロジェクト内に閉じ、テストはフェイク注入で OS 非依存に。
- 検証: `.claude/review-patterns.md`「リソース管理（Open/Close/Dispose）」「並行性」「境界（複数sink/SDK未検出）」。

## 参照
- 仕様: `docs/specs/multiview-output-revision.md` §2.4/§2.5, §4.3
- 契約: `docs/tasks/agent-A3-001-contracts-mvout.md`
- 既存: `src/Switcher.VirtualCam/`（`OutputRouter.cs` / `DualVirtualCameraOutput.cs` / `VirtualCameraOutput.cs` / `Display/HdmiFullscreenOutput.cs` / `Display/Direct3DSwapChainOutput.cs` / `Display/ISwapChainOutput.cs` / `Display/ICursorVisibility.cs` / `FrameConversion/Nv12FrameConverter.cs`）
