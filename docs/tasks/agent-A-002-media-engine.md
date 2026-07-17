---
name: agent-A-002-media-engine
status: planning
pid:
agent_cli: sonnet
---

# 実装指示書: 映像入力マネージャー & GPU/PiP合成エンジン（Phase 1）

## 概要
GStreamer(GstSharp)で UVC / NDI / SRT を最大10ch以上、スレッド分離でノンブロッキング・デコードする入力マネージャーと、デコードフレームをGPUテクスチャで合成する PGM/PVW 合成（PiP）エンジンを実装する。`Switcher.Contracts` の `IInputSourceManager` / `ICompositorEngine` を実装する。

## 前提条件（依存タスク）
- `agent-A-001-foundation-contracts` が `done`（`Switcher.Contracts` のIF/DTOが確定）。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `src/Switcher.Media/` のみ。`.sln` や `Switcher.Contracts` は編集しない（IF追加が必要なら Opus 親にエスカレーション）。

## 実装ステップ
1. GstSharp を導入（NuGet: `GstSharp`）。GStreamer ランタイム前提を README/コメントに明記。
2. `InputSource`（1系統）を実装:
   - プロトコル別パイプライン生成: UVC=`ksvideosrc`/`mfvideosrc`、NDI=`ndisrc`（NDI plugin）、SRT=`srtsrc`（Listener, `srt://:9000?mode=listener`, `latency` 20–50ms）。
   - デコードは各系統で独立スレッド。バッファは最小（`sync=false`/低`latency`）で低遅延化。
   - 最新フレームを GPU 供給用にダブルバッファ保持。切断/エラーを検知し `SourceStatus` を更新、自動再接続（バックオフ付き）。
3. `InputSourceManager : IInputSourceManager` を実装:
   - ソースの動的な追加/削除、`GetSources()`（`SourceInfo` 一覧）、状態変化 `event`。
   - 10ch以上の同時運用でも1系統の障害が他へ波及しない設計（例外隔離・独立ライフサイクル）。
4. `CompositorEngine : ICompositorEngine` を実装:
   - DirectX 11（推奨）でデコードフレームをGPUテクスチャ化し、座標/サイズ/Zオーダー/不透明度/クロップ（`PipSettings`）に従い合成。
   - PGM / PVW の2系統を保持。`Take()` で PVW→PGM 切替。`ApplyLayout()` はミリ秒オーダーで反映。
   - 合成結果フレームを外部（仮想カメラ/UI）へ渡す取得API（`ICompositorEngine` 準拠）。
5. 単体テスト: レイアウト計算（座標/クロップ/Zオーダー順序）、状態遷移（接続/切断/再接続）、ソース追加削除の冪等性。GStreamer実機依存部分は抽象化しモック可能にする。
6. `dotnet build`・`dotnet test` を通しコミット。

## 完了条件 / 検証コマンド
- `dotnet build src/Switcher.Media/Switcher.Media.csproj` がエラー0。
- `dotnet test`（レイアウト計算・状態遷移のユニットテスト）green。
- `IInputSourceManager` / `ICompositorEngine` を仕様通り実装（シグネチャ互換）。

## 技術的な補足 / レビュー観点
- **並行性**: 各系統スレッドの共有状態は排他制御。最新フレーム参照はロック最小のダブルバッファ/`Interlocked`等で。
- **リソース管理**: パイプライン/テクスチャ/スレッドの Dispose・停止パスを必ず用意（`.claude/review-patterns.md`「リソース管理」）。
- **耐障害**: 例外を握り潰さず該当系統のみ Error 化。全体停止を招かないこと。
- GStreamer/NDI プラグインが無い環境ではビルドは通るが実行時に要ランタイム、の旨を明記。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md` §2.1, §2.2, §4
