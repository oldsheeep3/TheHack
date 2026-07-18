---
name: agent-W-002-usb-bridge
status: done
pid: 300915
agent_cli: sonnet
---

# 実装指示書: USB→Network ブリッジ（中継モード）

## 概要
スマホにUSB-OTG接続された Pico 2W を Web Serial API / WebUSB API で認識し、送られてくるボタンイベントを検知して、メインPC常駐アプリへ WebSocket 経由で転送する（親仕様書 §4.1, `controller_id` 付与）。接続状態の可視化と自動再接続を備える。

## 前提条件（依存タスク）
- `agent-W-001-web-scaffold` が `done`（protocol型・WS/RESTクライアント・タブ土台が確定）。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `apps/phone-bridge/`（主に `src/bridge/`, `src/App.tsx` の中継モードタブ結線, テスト）。

## 実装ステップ
1. `src/bridge/serialLink.ts`: Web Serial API（優先）でPico 2Wに接続し受信ストリームを読む。機能検出し、非対応時はWebUSBフォールバックまたは明示エラー表示。ユーザー操作（ボタン押下）でポート選択を起動（ブラウザのジェスチャ要件）。
2. **受信データのパースを純粋関数に分離**（`parseControllerLine` 等）: Pico 2W(P-002)がCDCで送る §4.1 JSON行を `ButtonEvent` へ変換。壊れた/部分行のバッファリング（改行区切り）を扱う。
3. 転送: パース結果を `wsClient`（W-001）で PC(8080) へ送信。`controller_id` は未設定なら UI設定値（main/sub）を付与。
4. UI（中継モードタブ）:
   - USB接続/切断ボタン、`controller_id` 選択（main/sub）。
   - USB接続状態・WebSocket接続状態をアイコン＋色で表示。
   - 直近イベントのログ表示（デバッグ用）。
5. **自動再接続**: USB/WS 双方の切断検知→バックオフ再接続。状態を UI に反映。
6. テスト: `parseControllerLine`（正常/分割/破損/複数行連結）、`controller_id` 付与、再接続状態遷移（クライアントをモック）。
7. `npm run build` / `tsc --noEmit` / `npm run test` を通しコミット。

## 完了条件 / 検証コマンド
- `tsc --noEmit` エラー0、`npm run test` green（パース・付与・再接続ロジック）。
- 送信WSペイロードが親仕様書 §4.1（`event`/`controller_id`/`button_id`/`timestamp`）に一致。
- 接続状態の可視化・自動再接続が動作（手動確認手順をREADMEに追記）。

## 技術的な補足 / レビュー観点
- **並行性/リソース**: Serial/WSストリームの購読解除・close、多重接続防止、コンポーネント unmount 時のクリーンアップ（`.claude/review-patterns.md`「リソース管理」「並行性」）。
- **ブラウザ制約**: HTTPS/localhost・Chromium系・ユーザージェスチャ要件を UI で明示。
- 低遅延（ボタン→PC 数ms〜十数ms）を阻害しないよう、パース/転送は同期的・軽量に。

## 参照
- 仕様: `docs/specs/phone-web-bridge.md` §2.1, §4 / `docs/specs/00-system-overview.md` §4.1
