---
name: agent-B-002-virtualcam-output
status: doing
pid: 196104
agent_cli: sonnet
---

# 実装指示書: 仮想カメラ出力 & 物理ディスプレイ出力（Phase 2）

## 概要
合成結果（PGM）を仮想カメラ（DirectShowフィルタ, OBS Virtual Camera相当）としてOSに認識させる出力と、HDMI/Type-C等の物理ディスプレイへのフルスクリーン出力を実装する。`Switcher.Contracts` の `IVirtualCameraOutput` を実装する。

## 前提条件（依存タスク）
- `agent-A-001-foundation-contracts` が `done`。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `src/Switcher.VirtualCam/` のみ。`.sln` / `Switcher.Contracts` は編集しない。

## 実装ステップ
1. 仮想カメラ方式を選定・実装（Windows前提）:
   - 第一候補: OBS Virtual Camera のDirectShowフィルタDLLに準拠した方式、または既存OSSラッパーの利用。導入手順・レジストリ登録をREADME/コメントに明記。
   - `VirtualCameraOutput : IVirtualCameraOutput` を実装: `Start()` / `SubmitFrame(frame)` / `Stop()`。合成エンジンから受け取ったフレームを仮想デバイスへ供給。
2. 物理出力: 合成結果をフルスクリーン表示するための出力サーフェス提供（`ISwapChainOutput` 等の薄いIFを本プロジェクト内に定義し、App統合が特定ディスプレイに割当）。
3. フレーム形式変換（合成エンジンのGPUテクスチャ/フレーム → 仮想カメラが要求する形式）を実装。ゼロコピー/低遅延を志向。
4. テスト: フレームフォーマット変換のユニットテスト、Start/Stopのライフサイクル・二重開始/停止の冪等性。DirectShow実機依存部分は抽象化しモック可能に。
5. `dotnet build`・`dotnet test` を通しコミット。

## 完了条件 / 検証コマンド
- `dotnet build src/Switcher.VirtualCam/Switcher.VirtualCam.csproj` エラー0。
- `dotnet test` green。
- `IVirtualCameraOutput` を仕様通り実装。

## 技術的な補足 / レビュー観点
- **リソース管理**: デバイス/サーフェス/フレームバッファの確保・解放、Start/Stopの再入対応（`.claude/review-patterns.md`）。
- レジストリ登録・管理者権限の要否など環境前提を明記。導入は将来拡張の録画/配信と競合しない設計に。
- フレーム変換のコピー回数・型を明確化（マジックナンバー回避）。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md` §2.3, §3
