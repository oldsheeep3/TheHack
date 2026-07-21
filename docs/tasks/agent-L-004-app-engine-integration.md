---
name: agent-L-004-app-engine-integration
status: done
pid: 
agent_cli: sonnet
---

# 実装指示書: Switcher.App を Switcher.Engine へ統合（Phase 2 / 直列統合）

## 概要
App（WPF 合成ルート）の DI を `Switcher.Media`/`Switcher.VirtualCam` の実体から **`Switcher.Engine`(`LibObsVideoEngine`) へ切り替え**、HID 入力→エンジン、プレビュー/マルチビュー/HDMI 全画面の描画を **`IVideoEngine` の読み戻し/`obs_display` 提示**へ移行する。App の `ProjectReference` から **Media/VirtualCam を除去**（削除自体は L-005、ここでは参照とDI登録を外す）。仕様 §2.2〜§2.5。

## 前提条件（依存タスク）
- **L-001 完了**（`IVideoEngine`/`Switcher.Engine`）。
- **L-003 完了**（Web が `IVideoEngine` 経由。App は Web ホストへ同一 `IVideoEngine` 実体を注入する）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.App/`
- `tests/Switcher.App.Tests/`

## 実装ステップ
1. **DI 合成の切替**（`Composition/ServiceCollectionExtensions.cs`, `Orchestration/AppOrchestrator.cs`）:
   - `IVideoEngine` の実体として `Switcher.Engine.LibObsVideoEngine` を単一インスタンス登録し、**App と 内蔵 Web ホストで同一実体を共有**（L-003 の Web はこの実体を受ける）。
   - `IDeviceQueryService` の実体を `Switcher.Engine`（libobs 列挙）に配線。
   - `IControllerInputSink`(HID) / `ITallyBroadcaster` / `IAtemController` の登録は**維持**。
   - `Switcher.Media`/`Switcher.VirtualCam` 由来の実体登録（`CompositorEngine`/`InputSourceManager`/`OutputRouter`/`VirtualCameraOutput`）を除去。
2. **`ProjectReference` の除去**（`Switcher.App.csproj`）: `Switcher.Media` / `Switcher.VirtualCam` への参照を削除し、`Switcher.Engine` を追加。ビルドが通ること（残存 using を掃除）。
3. **HID→エンジン結線**（`MainWindow.xaml.cs` 等の `IInputSourceManager` 依存箇所）: コントローラー入力を `(bus, action, target)` に正規化し `IVideoEngine.SetPreview/Take/SetPip` を呼ぶ。バックライト算出は `OnStateChanged`（PGM/PVW 状態）を入力に既存ロジックを維持。
4. **プレビュー/マルチビュー描画**（`Rendering/FrameBitmapWriter.cs`, `MainWindow`, `MultiviewFullscreenWindow.xaml.cs`, `ProjectorWindow.xaml.cs`, `ViewModels/SourceTileViewModel.cs`）:
   - 旧 `IFrameSource`/`FrameData`/D3D 経路を **`IVideoEngine.OnFrame`(BGRA 読み戻し) → `WriteableBitmap`** へ置換（`FrameBitmapWriter` を再利用）。表示中のタイルのみ `SetTap(target,true)`。
   - **HDMI/物理ディスプレイ全画面**は、フルスクリーンウィンドウの HWND を `StartDisplayOutputAsync(target, hwnd, displayId)` に渡し `obs_display` 直描画（カーソル非表示・`Esc` 解除は App 側の既存挙動を維持）。同一画面警告・操作画面移動・トレイメニュー等の UI 挙動は**現状維持**。
5. **タリー配線維持**: `OnStateChanged` → 既存 `ITallyBroadcaster`（9999 UDP, 2系統）へ。ペイロード契約は不変。
6. **テスト**: `Switcher.App.Tests` を `FakeVideoEngine` 注入で更新（HID→エンジン呼び出しの検証、状態→バックライト/タリー算出、Media/VirtualCam 非依存）。ヘッドレスで緑。

## 完了条件 / 検証コマンド
```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
```
- App が `Switcher.Media`/`Switcher.VirtualCam` を **`ProjectReference` せず**、`IVideoEngine`(=`Switcher.Engine`) 経由で動作。
- HID 入力→ME操作、状態→バックライト/タリー、プレビュー/MV/HDMI 提示が `IVideoEngine` 経由に移行。
- App と内蔵 Web ホストが同一 `IVideoEngine` 実体を共有。
- 全スイート緑・警告0（ヘッドレスは Fake 注入）。

## 技術的な補足 / レビュー観点
- **UI/UX・外部契約は不変**（マルチビュー/トレイ/全画面/警告/ダーク基調・タリー・HID）。描画元がエンジンへ変わるのみ。
- この段階で **App からの Media/VirtualCam 参照はゼロ**にする（L-005 の削除の前提）。`grep -rn "Switcher.Media\|Switcher.VirtualCam" src/Switcher.App` が空になること。
- 検証: `.claude/review-patterns.md`「リソース管理（WriteableBitmap/tap の有効化制御）」「境界」「UI スレッド」。

## 参照
- 仕様: `docs/specs/libobs-engine-migration.md` §2.2〜§2.5, §7
- 既存: `src/Switcher.App/`（`Composition/ServiceCollectionExtensions.cs`/`Orchestration/AppOrchestrator.cs`/`MainWindow.xaml.cs`/`Rendering/FrameBitmapWriter.cs`/`MultiviewFullscreenWindow.xaml.cs`/`ProjectorWindow.xaml.cs`）
- 計画: `docs/tasks/orchestration-plan-libobs-migration.md`
