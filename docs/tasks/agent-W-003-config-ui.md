---
name: agent-W-003-config-ui
status: planning
pid:
agent_cli: sonnet
---

# 実装指示書: スイッチャー設定UI（設定モード）

## 概要
メインPCのWebAPIから入力ソース一覧を取得して表示し、各チャンネルのPiPレイアウトをスライダー/ドラッグで視覚的にリアルタイム変更する。シーンプリセットの保存・読み込み、タリー状態（PGM/PVW）表示を備える。

## 前提条件（依存タスク）
- `agent-W-001-web-scaffold` が `done`（protocol型・REST/WSクライアント・タブ土台が確定）。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `apps/phone-bridge/`（主に `src/config/`, `src/App.tsx` の設定モードタブ結線, テスト）。

## 実装ステップ
1. ソース一覧: `apiClient.getSources()`（`GET /api/v1/sources`）で `SourceInfo[]` を取得し、名前・プロトコル・ステータスを一覧表示（ダークUI・状態はアイコン＋色）。ポーリングまたはWS購読で更新。
2. PiPレイアウト編集:
   - 選択チャンネルの `PipSettings`（位置/サイズ/クロップ/不透明度/Zオーダー/背景）をスライダー＋ドラッグ操作で編集。
   - 変更を `apiClient.postConfig(ConfigChangeRequest)`（`POST /api/v1/config`）でリアルタイム送信（デバウンス/スロットルで過剰送信を抑制）。
   - **レイアウト計算（ドラッグ座標→PipSettings、境界クランプ）を純粋関数に分離**してユニットテスト可能に。
3. シーンプリセット: 複数チャンネルのレイアウト集合を保存/読み込み（まず `localStorage`。将来PC側APIがあれば差し替え可能な抽象に）。
4. タリー表示: `TallyState`(§4.3) を WS もしくはポーリングで受け、PGM(赤)/PVW(緑) をチャンネル一覧に反映。
5. UI/UX: 大きめタップターゲット、低輝度配色、状態即判別（仕様 §5）。
6. テスト: レイアウト計算（座標変換・クランプ）、送信デバウンス、プリセット保存/読込の往復。APIクライアントはモック。
7. `npm run build` / `tsc --noEmit` / `npm run test` を通しコミット。

## 完了条件 / 検証コマンド
- `tsc --noEmit` エラー0、`npm run test` green（レイアウト計算・デバウンス・プリセット）。
- 送信ペイロードが親仕様書 §4.2（`ConfigChangeRequest`/`PipSettings`）に一致。
- ソース一覧・レイアウト編集・プリセット・タリー表示が動作（手動確認手順をREADMEに追記）。

## 技術的な補足 / レビュー観点
- **IF互換**: 送受信スキーマは W-001 の `protocol/` 型経由に限定し、PC側(§4.2/§4.3)と一致（`.claude/review-patterns.md`「インターフェース互換性」）。
- **パフォーマンス**: ドラッグ中の送信はデバウンス/スロットル。不要な再レンダリングを避ける。
- **責務分離**: 表示（React）と計算ロジック（純粋関数）を分離してテスト可能に。

## 参照
- 仕様: `docs/specs/phone-web-bridge.md` §2.2, §5 / `docs/specs/00-system-overview.md` §4.2, §4.3
