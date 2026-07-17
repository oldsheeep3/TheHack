---
name: agent-P-004-wifi-tally-receive
status: planning
pid:
agent_cli: sonnet
---

# 実装指示書: Wi-Fi経由タリー受信 → LED（オプション）

## 概要
（仕様 §2.4「検討」項目）Pico 2W の Wi-Fi(CYW43 + lwIP)で、PC常駐アプリが送出するタリーUDPブロードキャスト（親仕様書 §4.3, `255.255.255.255:9999`）を受信し、自チャンネルの PGM/PVW 状態で LED を制御する。将来的な「スマホを介さない直接接続」経路の足がかり。

## 前提条件（依存タスク）
- `agent-P-001-firmware-scaffold` が `done`。
- 望ましくは `agent-P-003-ddc-tally-extraction` 完了後（LED制御を共用できる）。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `firmware/pico2w-controller/`（主に `src/wifi_tally.{c,h}`, `CMakeLists.txt` の CYW43 有効化, `src/main.c` 結線, `test/`）。

## 実装ステップ
1. `CMakeLists.txt` で CYW43/lwIP を有効化（`pico_cyw43_arch_lwip_threadsafe_background` 等）。Wi-Fi SSID/PASS は `config.h`（またはビルド時定義）で設定可能に。シークレットを履歴に混入させない（例: `config.local.h` を gitignore、テンプレ提供）。
2. `src/wifi_tally.{c,h}`:
   - Wi-Fi接続・再接続（バックオフ）。UDP `9999` を待受。
   - `TallyState`(§4.3, `active_pgm`/`active_pvw`) JSON を**純粋パース関数**でデコード（自ch含有→Red/Green/Off）。ホストテスト可能に。
   - タイムアウト（一定時間未受信）でフェイルセーフ表示（点滅等）。
3. LED制御は P-003 の LED 抽象を共用（重複実装しない）。
4. `src/main.c` に結線（DDC由来タリーとWi-Fi由来タリーの優先順位/統合方針をコメントで明確化）。
5. ホストテスト(`test/`): タリーJSONパース（自ch=PGM/PVW/対象外、欠損フィールド）を検証。
6. クロスビルド（可能なら）／ホストテストを通しコミット。

## 完了条件 / 検証コマンド
- `test/` のタリーJSONパース・ユニットテストが green。
- クロスビルドが成功（環境がある場合、CYW43有効ビルド）。
- 受信→LED反映、未受信時フェイルセーフが動作（手動確認手順をREADMEに追記）。

## 技術的な補足 / レビュー観点
- **オプション扱い**: §2.4 は「検討」項目。優先度は P-002/P-003 より低い。実装しない選択も可（その場合は本タスクを `done` にせずスキップ理由を記録）。
- **セキュリティ**: Wi-Fi認証情報をコード/履歴に混入させない（`.claude/review-patterns.md`「セキュリティ」）。
- **リソース/並行性**: lwIP コールバックと本ループの共有、ソケット/タイマー破棄、再接続競合に注意。
- 親仕様書 §4.3 のフィールド名（`active_pgm`/`active_pvw`）に厳密一致。

## 参照
- 仕様: `docs/specs/pico2w-controller-firmware.md` §2.4, §3 / `docs/specs/00-system-overview.md` §4.3
