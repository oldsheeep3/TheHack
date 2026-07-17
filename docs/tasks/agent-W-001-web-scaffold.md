---
name: agent-W-001-web-scaffold
status: planning
pid:
agent_cli: sonnet
---

# 実装指示書: Webアプリ基盤 & プロトコル型/クライアント（直列ゲート）

## 概要
Vite + React + TypeScript + Tailwind の SPA 土台を構築し、親仕様書 §4 に一致する TS 型定義と REST/WebSocket クライアントを整備する。「中継モード / 設定モード」を切り替えるダーク基調のタブ骨格を用意する。

## 前提条件（依存タスク）
- なし（本タスクが全タスクの前提）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `apps/phone-bridge/` 配下。

## 実装ステップ
1. Vite (`react-ts` テンプレート) で `apps/phone-bridge/` を初期化。`tsconfig` は `strict: true`。ESLint/Prettier を導入。
2. Tailwind CSS 導入（ダークモード標準）。低輝度・高コントラストの配色トークンを定義。
3. `src/protocol/types.ts`: 親仕様書 §4 の型を定義（PC側 `Switcher.Contracts` と同一スキーマ、JSONはスネークケース）:
   - `ButtonEvent`, `WsEnvelope`（§4.1）, `SourceInfo`, `SourceProtocol`, `SourceStatus`, `PipSettings`, `CropRect`, `ConfigChangeRequest`（§4.2）, `TallyState`（§4.3）。
4. `src/protocol/apiClient.ts`: REST クライアント（`GET /api/v1/sources`, `POST /api/v1/config`）。ベースURL/ポート(8080)は設定可能に。
5. `src/protocol/wsClient.ts`: WebSocket クライアント（接続/自動再接続/送受信）。接続状態を購読できる薄い状態管理を提供。
6. `src/App.tsx`: 「中継モード」「設定モード」タブの骨格（中身は後続 W-002/W-003 が実装するプレースホルダ）。大きめのタップターゲット。
7. `npm run build` / `tsc --noEmit` / `npm run test`（プレースホルダのvitest）が通ることを確認しコミット。

## 完了条件 / 検証コマンド
- `npm ci && npm run build` 成功。
- `tsc --noEmit` エラー0。
- `src/protocol/` の型が親仕様書 §4 と一致（フィールド名スネークケース）。往復シリアライズの簡易テストが green。

## 技術的な補足 / レビュー観点
- **本タスクの `protocol/` 型・クライアントIFが後続の契約**。PC側(§4)とのIF互換を最優先（`.claude/review-patterns.md`「インターフェース互換性」「型」）。
- `any` を避け、判別可能なユニオン/型ガードを用いる。
- ブラウザ制約（Web Serial/WebUSB は HTTPS/localhost・Chromium系）を後続で表示するための土台（機能検出ユーティリティ）を用意しておくと望ましい。

## 参照
- 仕様: `docs/specs/phone-web-bridge.md` §3, §5 / `docs/specs/00-system-overview.md` §4
