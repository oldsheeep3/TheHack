---
name: agent-A-004-app-integration
status: planning
pid:
agent_cli: sonnet
---

# 実装指示書: WPFホスト・全モジュール配線・マルチビューUI（Phase 3 / 直列統合）

## 概要
`Switcher.App`（WPF, `net9.0-windows`）を実装し、Media / Web / Atem / VirtualCam の各モジュールをDIで配線して1つの常駐アプリとして起動する。マルチビュー（全入力＋PGM/PVW）UI、ソースプロジェクター（全画面）、コントローラー入力→合成/ATEMへのルーティング、タリー状態の連動を統合する。

## 前提条件（依存タスク）
- Phase1/2 の全タスクが `done`: `agent-A-002-media-engine`, `agent-B-001-web-tally-server`, `agent-A-003-atem-control`, `agent-B-002-virtualcam-output`（および基盤 `agent-A-001`）。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `src/Switcher.App/` のみ。他プロジェクトのIF実装には触れない（配線・呼び出しのみ）。`.sln` へは App のProjectReference追加が必要な場合に限り最小編集可。

## 実装ステップ
1. DIコンテナ（`Microsoft.Extensions.DependencyInjection`）で各実体を登録:
   `IInputSourceManager`(Media), `ICompositorEngine`(Media), `ITallyBroadcaster`(Web), `IAtemController`(Atem), `IVirtualCameraOutput`(VirtualCam), `IControllerInputSink`。
2. **中核ファサード/オーケストレーター**を実装:
   - Web層が呼ぶ中核サービスIF（config適用・sources取得）を Media へ委譲。
   - `IControllerInputSink` の直列キューを消費し、ボタン入力を「合成操作(TAKE/PiP)」または「ATEM中継」へルーティング。
   - PGM/PVW 変化時に `TallyState` を組み立て `ITallyBroadcaster.Publish()`（タリー連動）。
   - 合成フレームを `IVirtualCameraOutput.SubmitFrame()` と物理出力へ供給。
3. 起動時に内蔵Webホスト(8080)・タリー配信・入力デコードを開始し、常駐（トレイ）動作。終了時に全モジュールを確実に停止/Dispose。
4. UI:
   - メインウィンドウにマルチビュー（全入力サムネイル＋PGM/PVW）。ダーク基調。
   - サブオペレーター向けソースプロジェクター（全画面）出力。
   - タリー/接続状態の表示。
5. E2E手動確認手順を README に記載（ダミーソースでの起動、`/api/v1/sources` 応答、WSでのボタン投入→切替、タリーUDP受信確認）。
6. `dotnet build HybridSwitcher.sln` 全体グリーン、`dotnet test` 全プロジェクト green を確認しコミット。

## 完了条件 / 検証コマンド
- `dotnet build HybridSwitcher.sln` 全体エラー0。
- アプリが起動し、DI配線・常駐・終了時クリーンアップが機能する。
- コントローラー入力→合成/ATEM/タリーの一連が結線されている（統合テストまたは手動E2E手順で確認）。

## 技術的な補足 / レビュー観点
- **責務分離**: UI / オーケストレーション / 各モジュールIF の境界を保つ。App は各モジュールの内部実装に依存しない（IF越し）。
- **並行性・競合**: 入力直列キューの単一消費、PGM/PVW状態更新とタリー送出の一貫性（`.claude/review-patterns.md`「並行性」「依存タスクの取り込み」）。
- **リソース**: 全モジュールの起動/停止順序と破棄を保証。
- 各 subtree 由来コードが正しく取り込まれ、IF互換が保たれているかを重点確認。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md` 全体, `docs/specs/00-system-overview.md` §3, §4
