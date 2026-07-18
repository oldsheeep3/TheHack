---
name: agent-P-002-button-usb-input
status: doing
pid: 303216
agent_cli: sonnet
---

# 実装指示書: 物理キー入力スキャン & USB送信

## 概要
キーマトリクス（最大16ボタン）をチャタリング防止しつつ常時走査し、押下キーIDを USB(HID または CDC)経由で接続先（PC/スマホ）へ送信する。イベント形式は親仕様書 §4.1 の `ButtonEvent`（`controller_id` 付与）に準拠。

## 前提条件（依存タスク）
- `agent-P-001-firmware-scaffold` が `done`（ピン定義・USB基盤・controller識別・ホストテスト土台が確定）。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `firmware/pico2w-controller/`（主に `src/buttons.{c,h}`, `src/usb_link.{c,h}`, `src/main.c` のフック結線, `test/`）。

## 実装ステップ
1. `src/buttons.{c,h}`:
   - キーマトリクス走査（`config.h` のピンを使用）。
   - **デバウンスを純粋状態機械として実装**（GPIO読取と分離）: 入力サンプル列→安定押下/離しイベントを出力。ホストテスト可能な関数シグネチャにする。
   - 押下エッジで `button_id` を確定。
2. `src/usb_link.{c,h}`:
   - TinyUSB を用い、押下イベントを送信。方式は CDC(シリアル)で §4.1 の JSON（`{"event":"button_press","data":{"controller_id":...,"button_id":...,"timestamp":...}}`）を1行送出を基本とする（HID併用可なら方式選定理由をコメント）。
   - `controller_id` は `get_controller_id()`（P-001）から取得。`timestamp` はデバイス起動からの `time_us_64()` ベース（ms）で付与し、単調増加を保証。
3. `src/main.c`: 走査→デバウンス→送信をメインループに結線。送信レイテンシ数ms以内を満たす走査周期に設定。
4. ホストテスト(`test/`): デバウンス状態機械（連続チャタリング入力→単一イベント、同時押し、押しっぱなし非連打）を網羅。
5. クロスビルド（可能なら）／ホストテストを通しコミット。

## 完了条件 / 検証コマンド
- `test/` のデバウンス・イベント生成ユニットテストが green。
- クロスビルドが成功（環境がある場合）。
- 送信JSONが親仕様書 §4.1 のスキーマ（`event`/`controller_id`/`button_id`/`timestamp`）に一致。

## 技術的な補足 / レビュー観点
- **チャタリング**: しきい値・サンプル数を定数化（マジックナンバー回避）。同時押し・押しっぱなしの扱いを明確化。
- **境界**: USB切断中の送信ドロップ/バッファ方針を明記。ブロッキングでメインループを止めない。
- I/O とロジック分離（デバウンスはホストテスト対象）。`.claude/review-patterns.md`「並行性」「テスト」「命名」。

## 参照
- 仕様: `docs/specs/pico2w-controller-firmware.md` §2.1, §4 / `docs/specs/00-system-overview.md` §4.1
