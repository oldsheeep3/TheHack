---
name: agent-L-005-cleanup-delete-media-vcam
status: planning
pid: 
agent_cli: sonnet
---

# 実装指示書: Media/VirtualCam 削除と依存整理（Phase 3 / 直列・最終）

## 概要
libobs 移行の総仕上げ。参照が消えた `Switcher.Media` / `Switcher.VirtualCam` と両テストを**削除**し、`.sln`・`.csproj`・NuGet 依存（GstSharp/Vortice）・不要になった Contracts 抽象を整理する。仕様 §3。**削除前に参照ゼロを grep で必ず確認**する。

## 前提条件（依存タスク）
- **L-003 完了**（Web が Media/VirtualCam 非依存）。
- **L-004 完了**（App が Media/VirtualCam を `ProjectReference` せず、`IVideoEngine` 経由）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- ルート横断（削除・整理のため）: `HybridSwitcher.sln`, `src/Switcher.Media/`(削除), `src/Switcher.VirtualCam/`(削除), `tests/Switcher.Media.Tests/`(削除), `tests/Switcher.VirtualCam.Tests/`(削除), `src/Switcher.Contracts/Interfaces/`(不要IF削除), `README.md`/`src/*/README.md`(記述整理)。

## 実装ステップ
1. **参照ゼロの確認（削除の前提。空でなければ中止して差し戻し）**:
   ```bash
   grep -rn "Switcher.Media\|Switcher.VirtualCam" src --include=*.cs --include=*.csproj | grep -v "/obj/\|/bin/"
   grep -rn "ICompositorEngine\|IVirtualCameraOutput\|IInputSourceManager\|IFrameSource" src --include=*.cs | grep -v "Switcher.Media\|Switcher.VirtualCam\|/obj/\|/bin/"
   ```
   - 前者は空、後者は Contracts の定義ファイル以外に残っていないこと。
2. **プロジェクト削除**: `src/Switcher.Media/`・`src/Switcher.VirtualCam/`・`tests/Switcher.Media.Tests/`・`tests/Switcher.VirtualCam.Tests/` をディレクトリごと削除。
3. **`.sln` 整理**: 上記4プロジェクトのエントリ（`Project(...)` 行と `GlobalSection` の構成マッピング）を除去。`dotnet build HybridSwitcher.sln` が壊れないこと。
4. **不要 Contracts 抽象の削除**: `IVideoEngine` に統合され参照されなくなった `Interfaces/ICompositorEngine.cs` / `Interfaces/IVirtualCameraOutput.cs` / `Interfaces/IInputSourceManager.cs`（＋ Media 側の `IFrameSource` は Media 削除で消える）を削除。`IDeviceQueryService`/`ITallyBroadcaster`/`IControllerInputSink`/`IAtemController` は**維持**。
5. **NuGet/依存の掃除**: 削除済みプロジェクト固有だった `GstSharp` / `Vortice.*` 参照が他に残っていないか確認（`grep -rn "GstSharp\|Vortice" src/*/*.csproj`）。残骸・GStreamer ランタイム解決/DLL リゾルバ/ランタイムパス関連の記述・スクリプトがあれば除去。
6. **ドキュメント整理**: ルート `README.md` のコンポーネント表・.NET プロジェクト構成から Media/VirtualCam を除き、`Switcher.Engine`(+`native/switcher-engine`) を追記。技術スタック行を libobs へ更新（親仕様 §3 の注記に整合）。削除したプロジェクトの `README.md` 参照リンクを除去。
7. **最終検証**: 全スイート緑・警告0。テスト数がゼロ落ちしていない（Media/VirtualCam の**削除ぶんを除き**、他スイートは維持）ことを確認。

## 完了条件 / 検証コマンド
```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
```
- `src/Switcher.Media` / `src/Switcher.VirtualCam` と両テストが**存在しない**。
- `.sln` から4プロジェクトが除去され、ビルド/テストが緑・警告0。
- `GstSharp`/`Vortice.*` 参照が残っていない。
- 不要 Contracts IF（`ICompositorEngine`/`IVirtualCameraOutput`/`IInputSourceManager`）が削除され、残り（Engine/Web/App/Atem/Hid/Contracts）スイートは緑。
- `README.md` が新構成（`Switcher.Engine` + `native/switcher-engine`）を反映。

## 技術的な補足 / レビュー観点
- **削除は参照ゼロ確認後のみ**。1件でも残れば削除せず差し戻す（安全第一）。
- テストの「消えてよい緑」と「消してはいけない緑」を区別（Media/VirtualCam スイートの消滅は想定内、他スイートの減少は不可）。
- 検証: `.claude/review-patterns.md`「デッドコード削除の安全性」「ビルド構成整合」「ドキュメント整合」。

## 参照
- 仕様: `docs/specs/libobs-engine-migration.md` §3
- 計画: `docs/tasks/orchestration-plan-libobs-migration.md`
