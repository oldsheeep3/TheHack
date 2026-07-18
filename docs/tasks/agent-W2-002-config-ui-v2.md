---
name: agent-W2-002-config-ui-v2
status: done
pid: 376905
agent_cli: sonnet
---

# 実装指示書: 拡張スイッチャー設定UI（設定モード v2）

## 概要
W2-001 が確定した新プロトコル型・クライアントを用い、設定モードUIを親仕様書 §4.2 の全設定サーフェスへ拡張する（仕様 §2.2）。OBS的なソース追加/削除/並べ替え、**2系統ME（PGM1/PGM2）のプログラム構成＋PiPレイアウト編集**、**4x4 コンフィギュラブル・マルチビュー**割当編集、**出力割当**（仮想カメラ×2/HDMI）、**モジュール割付＋Picoネットワーク設定**、**2系統タリー**（PGM1/PGM2/PVW1/PVW2）表示を備える。既存の純粋ロジック（`layout`/`debounce`/`presets`）と `PipEditor` を再利用する。

## 前提条件（依存タスク）
- `agent-W2-001-network-rebase` が `done`（新 `protocol/` 型・クライアント・ネットワーク接続・タブ土台が確定）。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `apps/phone-bridge/`（主に `src/config/`, `src/App.tsx` の設定モードタブ結線, テスト）。

## 実装ステップ
1. **ソース管理（OBS的 追加/削除/並べ替え）**:
   - `apiClient.getSources()` で `SourceInfo[]` を取得し、名前・種別（NDI/WEBCAM/SRT）・ステータス（アイコン＋色）を一覧表示。
   - `SourceDefinition` を入力するフォームで `addSource`（`POST`）/`updateSource`（`PUT`）/`deleteSource`（`DELETE`）を実行。種別に応じ `ndi.source_name` / `webcam.device_id`+`format` / `srt.url`+`latency_ms` を出し分け（判別ユニオン）。
   - 並べ替え（表示順）UI を提供（PC側が順序を持たない場合はクライアント表示順として `localStorage` 保持でよい）。
2. **2系統ME プログラム＋PiP編集**:
   - PGM1/PGM2 を切替え、各バスの合成レイヤ（`layers[]`, 背面→前面）を編集。レイヤごとに `source_id` と `PipSettings`（位置/サイズ/クロップ/不透明度/Zオーダー）を **既存 `PipEditor` + `layout.ts`（純粋関数）** で視覚編集。
   - 変更を `apiClient.applyProgram({ bus, layers, take })`（`POST /api/v1/program`）でリアルタイム送信。ドラッグ中の過剰送信は **既存 `debounce`** で抑制。
   - `take` ボタンで当該バスの PVW→PGM 切替を送出（`take: true`）。
3. **4x4 マルチビュー割当編集**:
   - 16セルのグリッドUIで各セルに `"PGM1"|"PGM2"|"PVW1"|"PVW2"|"SRC:<id>"|"EMPTY"` を割当。`apiClient.setMultiview({ cells })`（`PUT /api/v1/multiview`）で送信。
   - **セル配列の生成/検証（長さ16, `SRC:<id>` 整形）を純粋関数に分離**しユニットテスト可能に。
4. **出力割当**:
   - 仮想カメラ×2（VCAM1/VCAM2）/HDMI へ PGM1/PGM2 を割当。HDMI は `display_id`/`hide_cursor`/`fullscreen` を編集。`apiClient.setOutputs({ outputs })`（`PUT /api/v1/outputs`）。
5. **モジュール割付 + Picoネットワーク設定**:
   - 物理モジュールの `src1`/`src2` を論理ソース（`source_id`）へ紐付け、VR割当先（`vr_target`, 例 transition/opacity）を指定。`apiClient.setModules({ modules })`（`PUT /api/v1/modules`）。モジュール数は親 §4.0 `MAX_MODULES`=8 を上限とする。
   - Pico の Wi-Fi/BT ネットワーク設定フォーム → `apiClient.setPicoNetwork(...)`（`PUT /api/v1/pico/network`, 親 §4.6）。
6. **2系統タリー表示**:
   - `apiClient.getTallyState()`（2系統 `TallyState`）を WS もしくはポーリングで受け、`active_pgm1`/`active_pgm2`（赤系）・`active_pvw1`/`active_pvw2`（緑系）をソース一覧/マルチビューに反映。系統（PGM1/PGM2）を判別できるバッジにする。
7. **シーンプリセット**: 既存 `presets.ts`（`PresetStore` 抽象）を**流用**し、拡張後の設定（2系統プログラム/レイアウト集合）の保存/読込に対応（まず `localStorage`, 将来PC側APIへ差し替え可能な抽象を維持）。
8. UI/UX: 大きめタップターゲット、低輝度ダーク配色、状態はアイコン＋色で即判別（仕様 §5）。「操作モード」と「設定モード」のタブ切替は W2-001 の土台に結線。
9. テスト（vitest, ホスト実行）: マルチビューセル整形/検証、`SourceDefinition` 判別、program送信ペイロード整形、既存の layout計算/デバウンス/プリセット往復の回帰。APIクライアントはモック。
10. `npm ci && npx tsc --noEmit && npm run test && npm run build` を通しコミット。

## 完了条件 / 検証コマンド
- `npm ci && npx tsc --noEmit && npm run test && npm run build` がすべて成功（`tsc --noEmit` エラー0, `npm run test` green, `build` 成功）。
- 送受信ペイロード（sources CRUD / program / multiview / outputs / modules / pico-network / 2系統tally）が親仕様書 **§4.2/§4.3** と一致（フィールド名スネークケース, `SourceStatus` は PascalCase, `type` は `NDI`/`WEBCAM`/`SRT`）。
- ソースCRUD・2系統ME編集・マルチビュー割当・出力割当・モジュール割付・Pico網設定・2系統タリー表示が動作（手動確認手順を README に追記）。
- 追加した純粋ロジック（マルチビューセル整形等）に回帰テストがある。

## 技術的な補足 / レビュー観点
- **IF互換**: 送受信スキーマは W2-001 の `protocol/` 型経由に限定し、PC側（§4.2/§4.3）と一致（`.claude/review-patterns.md`「インターフェース互換性」）。UI内で独自の別スキーマを作らない。
- **責務分離**: 表示（React）と計算ロジック（純粋関数: レイアウト計算・マルチビューセル整形・プリセット）を分離してテスト可能に。既存 `layout.ts`/`debounce.ts`/`presets.ts`/`PipEditor.tsx` を再利用し重複実装を避ける。
- **パフォーマンス**: ドラッグ/スライダー中の `program`/config 送信はデバウンス/スロットル。不要な再レンダリングを避ける。
- **2系統の取り違え防止**: PGM1/PGM2・PVW1/PVW2 を UI とペイロードの両方で明確に区別（親 §4.3 の `active_pgm1/2`・`active_pvw1/2`）。

## 参照
- 仕様: `docs/specs/phone-web-bridge.md` §2.2, §5（改訂 2026-07-18）
- 親仕様書: `docs/specs/00-system-overview.md` §4.0, §4.2, §4.3, §4.6
- 既存資産（再利用）: `apps/phone-bridge/src/config/{layout,debounce,presets,PipEditor,ConfigTab}.*`
- 前タスク: `docs/tasks/agent-W2-001-network-rebase.md`
