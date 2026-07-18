---
name: agent-W-003-config-ui
status: doing
pid: 325150
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

---

## レビュー指摘（要修正 / fixing）— 2026-07-18 by Opus 親

レイアウト計算・デバウンス・プリセット・UIの責務分離は良好で、`tsc`/`test`/`build` も通っている。ただし**PC側の実配信スキーマとの型不一致（IF互換違反）が1点**あり、これを修正すること。

### 指摘1（必須）: `SourceStatus` の値ケースが PC 側と不一致
- `src/protocol/types.ts` の `SourceStatus` は小文字 `'connected' | 'disconnected' | 'error'` だが、**PC側 `Switcher.Contracts` は PascalCase で配信する**。実測とPC側テストの両方で確定済み:
  - 実シリアライズ: `{"channel":1,"name":"cam","protocol":"UVC","resolution":"1920x1080","status":"Connected"}`
  - PC側テスト `tests/Switcher.Web.Tests/WebApiTests.cs:93` が `Assert.Equal("Connected", ... status ...)` を検証。
- このため `GET /api/v1/sources` の実応答（`status:"Connected"`）に対し、`ConfigTab.tsx` の `STATUS_LABEL["Connected"]` / `STATUS_COLOR["Connected"]` が **undefined** になり、ステータスのラベル空表示・`bg-undefined` でUIが壊れる。
- 参考: `protocol` は PC側が `[JsonStringEnumMemberName("UVC")]` で大文字固定 → 現状の TS `'UVC'|'NDI'|'SRT'` で**一致済み（変更不要）**。不一致は `SourceStatus` のみ。
- **対応**（`apps/phone-bridge/` 内。IF互換修正のため `src/protocol/types.ts` の編集可）:
  1. `src/protocol/types.ts`: `SourceStatus` を `'Connected' | 'Disconnected' | 'Error'` に変更。`SOURCE_STATUSES` 配列と `isSourceStatus` ガードも同値へ更新。
  2. `src/config/ConfigTab.tsx`: `STATUS_LABEL` / `STATUS_COLOR` のキーを `Connected` / `Disconnected` / `Error` に変更（ラベル文言・色は現状踏襲でよい）。
  3. 上記に依存するテストがあれば追随。可能なら `status:"Connected"` を含む `SourceInfo` を用いた回帰テストを1件追加。
- **検証**: `npx tsc --noEmit` エラー0、`npm run test` green、`npm run build` 成功。修正後コミットして `reviewing` へ。

（参考: 送信系 §4.1/§4.2、`PipSettings`(`x_position` 等)、`TallyState`(`active_pgm`/`active_pvw`) は一致確認済み。上記の受信 `status` ケースのみ是正すればよい。）
