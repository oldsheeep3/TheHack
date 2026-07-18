---
name: agent-A2-005-output-dualcam-hdmi
status: reviewing
pid: 414520
agent_cli: sonnet
---

# 実装指示書: 出力 — 仮想カメラ×2 & HDMI全画面（Phase 2）

## 概要
`Switcher.VirtualCam` を**加算的に拡張**し、**仮想カメラ×2**（既定 PGM1→VCAM1 / PGM2→VCAM2）と、**HDMI/物理ディスプレイへの全画面出力（マウスカーソル非表示）** を提供する（仕様 §2.4, 親 §4.2 `PUT /api/v1/outputs`）。割当は出力API（A2-003 が受け付け、A2-006 が配線）で動的変更できる。既存 `VirtualCameraOutput` / `Nv12FrameConverter` / `SharedMemoryVirtualCameraDevice` / `Display/Direct3DSwapChainOutput` を**再利用**し、多重化（2系統）とHDMIフルスクリーン制御を加算する。

## 前提条件（依存タスク）
- `agent-A2-001-contracts-v2`（`OutputSink`(VCAM1/VCAM2/HDMI) / `OutputSource`(PGM1/PGM2) / `OutputAssignment`(display_id/hide_cursor/fullscreen) / `OutputsRequest` が確定）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.VirtualCam/`（本体拡張）
- `tests/Switcher.VirtualCam.Tests/`（テスト拡張。`.sln` 登録済みのため `.sln` は編集しない）

## 実装ステップ
1. **仮想カメラ×2**:
   - 既存 `VirtualCameraOutput`（1台・`IVirtualCameraDevice` 注入・NV12変換・遅延Open）を土台に、**2インスタンス**を管理する `DualVirtualCameraOutput`（または `VirtualCameraOutput` を id/sink 指定で複数生成する管理型）を追加。VCAM1/VCAM2 を個別デバイスとして開く。
   - `SharedMemoryVirtualCameraDevice` を**デバイス名/共有メモリ名でパラメータ化**し、2台が別デバイスとして共存できるようにする（既定名の衝突回避）。
   - 既存 `IVirtualCameraOutput`（`Start`/`SubmitFrame`/`Stop`）は互換維持。多重化は上位の管理型で行う。
2. **HDMI全画面出力**:
   - 既存 `Display/Direct3DSwapChainOutput` / `ISwapChainOutput` を**再利用**し、指定 `display_id` のディスプレイへ全画面（fullscreen）でフレーム提示する `HdmiFullscreenOutput` を追加。
   - **マウスカーソル非表示**（`hide_cursor`）: 全画面出力ウィンドウ上でカーソルを隠す（Windows: `Cursor.Hide()` 相当 or ウィンドウスタイル）。実ウィンドウ生成/配置は App(WPF) と協調する場合があるため、**カーソル非表示・全画面のポリシー適用点をこのプロジェクトの出力型に持たせつつ**、WPFウィンドウ生成が必要な部分は A2-006 の `ProjectorWindow` 側と責務分担する（本タスクはスワップチェーン提示＋カーソル制御ロジックまで）。
3. **出力割当の適用**:
   - `OutputRouter`（`ApplyOutputs(OutputsRequest)`）を提供: 各 `OutputAssignment` に従い、どの PGM フレーム（PGM1/PGM2）をどの sink（VCAM1/VCAM2/HDMI+display_id）へ流すかのルーティングを保持。フレーム供給元（`ICompositorEngine.GetProgramFrame(bus)`）との接続は A2-006 が配線（ここではルーティング表と提示先の管理まで）。
   - 既定割当（PGM1→VCAM1, PGM2→VCAM2）を提供。
4. **フレーム分配の効率**: 同一フレームを複数 sink へ渡す際の不要コピー回避（NV12変換はカメラsink用、HDMIはスワップチェーン提示用に別経路）。既存 `Nv12FrameConverter.GetRequiredBufferSize`/`ConvertBgraToNv12` を再利用。
5. `Switcher.VirtualCam.Tests` を拡張: 2台の仮想カメラが独立にOpen/Push/Close されること（フェイク `IVirtualCameraDevice` を2台注入）、`OutputRouter` の割当解決（既定/変更/不正）、HDMI提示ロジックのカーソル非表示・全画面フラグの反映（`ISwapChainOutput` フェイク）。既存 `Nv12FrameConverterTests` / `VirtualCameraOutputTests` を維持。

## 完了条件 / 検証コマンド
- 以下が成功（警告0）:
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
  ```
- 仮想カメラ2台が別デバイスとして共存し、既定 PGM1→VCAM1 / PGM2→VCAM2 が割当可能。
- HDMI全画面出力型がカーソル非表示・全画面・`display_id` 指定を反映（テストでフラグ検証）。
- `OutputRouter.ApplyOutputs` が §4.2 の割当を解決し、既存 `Switcher.VirtualCam.Tests` が全グリーン。

## 技術的な補足 / レビュー観点
- **再利用最優先**: `VirtualCameraOutput`（遅延Open・NV12変換・resolution再オープン）と `Direct3DSwapChainOutput`/`ISwapChainOutput` をそのまま活かす。共有メモリ名/デバイス名の**パラメータ化**が2台共存の要。
- 責務分担: WPFウィンドウ（`ProjectorWindow`）の生成・ディスプレイ配置は A2-006。本タスクは「提示ロジック＋カーソル非表示ポリシー＋ルーティング表」まで。境界を本ファイルに明記し二重実装を避ける。
- 低遅延: フレーム分配の不要コピー削減。sink 障害（1カメラ）で他 sink を止めない。
- Windows依存（DirectShow/MF/D3D）は既存同様プロジェクト内に閉じ、テストはフェイク注入でOS非依存に。
- 検証: `.claude/review-patterns.md`「リソース管理（Open/Close/Dispose）」「並行性」「境界（解像度/複数sink）」。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md` §2.3(全画面プロジェクタ), §2.4 / `docs/specs/00-system-overview.md` §4.2（`PUT /api/v1/outputs`）
- 既存: `src/Switcher.VirtualCam/`（`VirtualCameraOutput` / `FrameConversion/Nv12FrameConverter` / `Devices/SharedMemoryVirtualCameraDevice` / `Display/Direct3DSwapChainOutput` / `Display/ISwapChainOutput`）
- 契約: `docs/tasks/agent-A2-001-contracts-v2.md`
