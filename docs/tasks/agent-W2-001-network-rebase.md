---
name: agent-W2-001-network-rebase
status: done
pid: 349647
agent_cli: sonnet
---

# 実装指示書: ネットワーク再定義 & プロトコル型/クライアント拡張（改訂ゲート）

## 概要
既存 `apps/phone-bridge/` の **USB Web Serial 中継を撤去**し、**スマホのネットワーク（Wi-Fi）経由でホストPC（`:8080`）へ接続**する構成へ置換する（仕様 §2.1）。あわせて `protocol/types.ts`・`apiClient.ts` を親仕様書 §4.2/§4.3 に合わせて**拡張**し、後続 W2-002 が消費する新IF（2系統タリー・`SourceDefinition`・program/multiview/outputs/modules/pico-network の各DTO・新エンドポイント）を確定する。本タスクは v2 改訂の前提ゲート。

## 前提条件（依存タスク）
- なし（本タスクが v2 全タスクの前提ゲート）。
- 既存実装（`agent-W-001`〜`W-003`, `done`）を土台とする **増分DIFF**。ゼロ再構築はしない。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `apps/phone-bridge/` 配下（主に `src/protocol/`, 新規 `src/net/`, 旧 `src/bridge/` の撤去, `src/lib/browserSupport.ts` の縮退/削除, `src/App.tsx` のタブ結線, テスト）。

## 実装ステップ
1. **Web Serial 資産の退役**:
   - `src/bridge/serialLink.ts`, `src/bridge/RelayTab.tsx`, `src/bridge/parseControllerLine.ts`, `src/bridge/controllerId.ts` と対応する `__tests__` を削除（PCがHID集約を担うため直接USB中継は不要, 親 §4.1）。
   - `src/lib/browserSupport.ts` の Web Serial/WebUSB 機能検出を削除。ネットワーク接続に固有のブラウザ制約は無いため、当ファイルは削除するか最小限（例: セキュアコンテキスト表示のみ）に縮退させる。
   - `package.json` の `@types/w3c-web-serial` / `@types/w3c-web-usb` は不要になれば依存から除去してよい（残しても害はないが本改訂の意図に合わせ整理を推奨）。
2. **ネットワーク接続レイヤ（新規 `src/net/`）**:
   - PC（`:8080` の HTTP/WebSocket）へネットワーク経由で接続する接続マネージャを実装。接続先ホスト/ポートは**設定可能**（mDNS/手動IP。mDNSはブラウザから直接解決不可な場合は手動ホスト入力＋`localStorage` 保存を第一級とし、mDNSホスト名の手入力を許容する）。
   - `protocol/wsClient.ts` を**流用**して WS 接続・状態購読・指数バックオフ再接続を提供（新規実装しない）。REST 疎通確認（`apiClient` のヘルス/`GET /api/v1/sources` 等）で「PCネットワーク到達性」を可視化。
   - 接続状態（ネットワーク/WebSocket）の可視化と自動再接続（指数バックオフ）を UI へ露出（仕様 §2.1）。**接続先ホスト/ポートの変更時に client の baseUrl/URL を再構成**できるようにする（`ApiClient.setBaseUrl` を活用, WsClient は再生成）。
3. **`src/protocol/types.ts` の拡張**（親 §4.2/§4.3。JSONスネークケース, `any` 不使用, 判別ユニオン/型ガード）:
   - `SourceStatus` は **PascalCase を維持**（`'Connected' | 'Disconnected' | 'Error'`。初版 W-003 レビューで PC側 `Switcher.Contracts` と一致確定済み。**変更しないこと**）。
   - `SourceDefinition`（`POST /api/v1/sources`）: `id`, `name`, `type: "NDI" | "WEBCAM" | "SRT"`, `ndi: { source_name } | null`, `webcam: { device_id, format } | null`, `srt: { url, latency_ms } | null`。`type` に応じた判別ユニオン/型ガードを用意。
   - `SourceInfo`（`GET /api/v1/sources`）: 既存フィールド（`status` 等）を保ちつつ、`SourceDefinition` と紐づく `id` と `type`（NDI/WEBCAM/SRT）を持てるよう拡張。タリーが参照する整数チャンネル/序数（親 §4.3 の `active_*` 要素）との対応関係を明確化する。
   - `ProgramRequest`（`POST /api/v1/program`）: `bus: "PGM1" | "PGM2"`, `layers: Array<{ source_id: string; pip: PipSettings }>`（背面→前面）, `take: boolean`。`PipSettings`/`CropRect` は既存を流用。
   - `MultiviewConfig`（`PUT /api/v1/multiview`）: `cells: MultiviewCell[]`（長さ16）。`MultiviewCell = "PGM1" | "PGM2" | "PVW1" | "PVW2" | \`SRC:${string}\` | "EMPTY"`。
   - `OutputsConfig`（`PUT /api/v1/outputs`）: `outputs: OutputAssignment[]`。`sink: "VCAM1" | "VCAM2" | "HDMI"`, `source: "PGM1" | "PGM2"`, HDMI時 `display_id`/`hide_cursor`/`fullscreen`（判別ユニオン推奨）。
   - `ModulesConfig`（`PUT /api/v1/modules`）: `modules: Array<{ index: number; src1: ModuleSrc; src2: ModuleSrc }>`。`ModuleSrc = { source_id: string; vr_target: string }`（例 `"transition" | "opacity"` 等の文字列）。
   - `PicoNetworkConfig`（`PUT /api/v1/pico/network`, 親 §4.6）: Wi-Fi/BT 資格情報等（`controller_id` 等の保持設定を含めてよい）。
   - `TallyState`（§4.3, **2系統**）: `active_pgm1: number[]`, `active_pgm2: number[]`, `active_pvw1: number[]`, `active_pvw2: number[]`。単系統互換の言及どおり `active_pgm2`/`active_pvw2` は空配列を許容。旧 `active_pgm`/`active_pvw` は本改訂で置換（後続 UI 側の参照も追随）。
   - 各 DTO に対し `is...` 型ガードまたは往復シリアライズ検証を用意（受信検証と回帰テストのため）。
4. **`src/protocol/apiClient.ts` の拡張**（既存 `request` 基盤を流用, 新メソッド追加）:
   - ソース: `getSources()`（既存流用）, `addSource(SourceDefinition)`=`POST /api/v1/sources`, `updateSource(id, SourceDefinition)`=`PUT /api/v1/sources/{id}`, `deleteSource(id)`=`DELETE /api/v1/sources/{id}`。
   - `applyProgram(ProgramRequest)`=`POST /api/v1/program`。
   - `setMultiview(MultiviewConfig)`=`PUT /api/v1/multiview`。
   - `setOutputs(OutputsConfig)`=`PUT /api/v1/outputs`。
   - `setModules(ModulesConfig)`=`PUT /api/v1/modules`。
   - `setPicoNetwork(PicoNetworkConfig)`=`PUT /api/v1/pico/network`。
   - `getTallyState()` は **2系統 `TallyState`** を返すよう更新。
   - `DELETE`/`PUT` に対応するよう `request` のメソッド型を拡張。
5. **`src/App.tsx` のタブ改訂**: 「中継モード」タブを廃し、「**操作/接続モード**」（PCへのネットワーク接続・接続先ホスト設定・接続状態表示。UI中身は最小限でよく、詳細操作は W2-002）と「設定モード」（W2-002 が実装）に置換。設定モードタブは後続がプレースホルダを埋める。
6. テスト: 新DTOの往復シリアライズ/型ガード、`TallyState`(2系統) パース、接続状態遷移（`wsClient` の再接続はモック時計で検証, 既存テストを流用/拡張）。旧 serial 関連テストは削除。
7. `npm ci && npx tsc --noEmit && npm run test && npm run build` を通しコミット。

## 完了条件 / 検証コマンド
- `npm ci && npx tsc --noEmit && npm run test && npm run build` がすべて成功（`tsc --noEmit` エラー0, `npm run test` green, `build` 成功）。
- Web Serial/WebUSB 由来コード（`serialLink`/`RelayTab`/`parseControllerLine`/`controllerId`/`browserSupport` の Web Serial 部）が撤去済み。
- `src/protocol/` の型・クライアントが親仕様書 **§4.2/§4.3** と一致（フィールド名スネークケース, `SourceStatus` は PascalCase, 2系統タリー）。新DTOの往復シリアライズ回帰テストが green。
- 接続先ホスト/ポートの設定・ネットワーク/WS 接続状態の可視化・自動再接続が動作（手動確認手順を README に追記）。

## 技術的な補足 / レビュー観点
- **本タスクの `protocol/` 型・クライアントIFが後続 W2-002 の契約**。PC側（親 §4.2/§4.3）との IF互換を最優先（`.claude/review-patterns.md`「インターフェース互換性」「型」）。
- **`SourceStatus` は PascalCase を維持**（小文字化は初版で発生したIF不一致バグの再発。絶対に戻さない）。
- **並行性/リソース管理**: WS 購読解除・close、接続先変更時のクライアント再構成、コンポーネント unmount 時のクリーンアップ。多重接続防止。
- `any` を避け、`type`/`sink`/`bus`/`cell` は判別可能なユニオン＋型ガードで表現。
- mDNS はブラウザ直接解決が難しいため、**手動ホスト入力＋保存を第一級**にし、mDNSホスト名は手入力で許容する現実的な設計とする。

## 参照
- 仕様: `docs/specs/phone-web-bridge.md` §2.1, §3, §5, §7（改訂 2026-07-18）
- 親仕様書: `docs/specs/00-system-overview.md` §4.1, §4.2, §4.3, §4.6
- 既存資産: `apps/phone-bridge/src/protocol/*`, `src/bridge/*`（退役対象）, `src/lib/browserSupport.ts`
