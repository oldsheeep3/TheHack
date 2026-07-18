---
name: agent-P2-003-wireless-optional
status: done
pid: 416159
agent_cli: sonnet
---

# 実装指示書: Wi-Fi/BT ワイヤレス制御チャネル（オプション / USB-HIDの代替経路）

## 概要
（仕様 §2.5「任意」項目）Pico 2W の **CYW43（Wi-Fi/BT）** を用い、PCから投入したネットワーク資格情報（P2-002 の設定永続化, 親仕様書 §4.6）で接続を確立し、**USB-HID が使えない/無線運用時の代替制御チャネル**を提供する。集約した SW/VR 状態の送信（HID入力`0x01`相当）とバックライト指定の受領（HID出力`0x02`相当）を同等に行う。**ネットワーク未設定時は無効**（USB-HIDのみで動作）。

## 前提条件（依存タスク）
- `agent-P2-001-refactor-i2c-hid` が `done`（I2C集約・状態パッキング `state_agg` が確定）。
- 望ましくは `agent-P2-002-backlight-settings` 完了後（ネットワーク資格情報の設定永続化・バックライト分配 `backlight` を共用できる）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `firmware/pico2w-controller/`（主に `src/wireless.{c,h}`, `CMakeLists.txt` の CYW43/lwIP 有効化, `src/main.c` 結線, `test/`）。

## 実装ステップ
1. **`CMakeLists.txt`**: CYW43/lwIP を有効化（`pico_cyw43_arch_lwip_threadsafe_background` 等。P2-001 のコメントに従い `target_link_libraries` へ追加）。無線を使わないビルドでリンクサイズ/依存が増えすぎないよう、有効化はビルドオプション（例 `-DENABLE_WIRELESS`）で切替可能にすることを検討。
2. **`src/wireless.{c,h}`（新規, CYW43/lwIP依存）**:
   - `settings`（P2-002）から資格情報を読み、**未設定なら初期化せず無効**（USB-HIDのみで動作, §2.5）。
   - Wi-Fi（またはBT）接続・再接続（バックオフ）。制御チャネルを確立（例: WebSocket/UDP over Wi-Fi, §2.5）。
   - **送信**: `state_agg`（P2-001）で作った集約状態（SW/VR/`module_present`/`seq`）を、HID入力`0x01`と**同一のセマンティクス**でチャネルへ送出（パッキング純粋関数を共用し二重実装しない）。
   - **受領**: バックライト指定を受け取り、`backlight`（P2-002）の分配経路へ渡して I2C `0x10` へ配布（分配ロジックを共用）。
   - タイムアウト/切断時のフェイルセーフ（無線側停止時にUSB経路へ影響を出さない）。
3. **`src/main.c` 結線**: USB-HID と無線チャネルの**優先順位/併用方針**をコメントで明確化（USB直結を主経路、無線は代替。二重送出/競合の扱い）。無線未設定時は完全に無効。
4. **ホストテスト（拡張）**: チャネルのメッセージ・エンコード/デコードに純粋部があれば `test/` に追加（例 `test_wireless_codec.c`）。`state_agg`/`backlight` の純粋関数を共用する箇所は既存テストでカバーされることを確認（重複実装しない）。
5. **README 差分**: 無線有効化ビルド手順、資格情報のPC投入前提（§4.6）、代替チャネルの手動確認手順、未設定時はUSB-HIDのみで動作する旨を追記。
6. クロスビルド（環境があれば CYW43 有効ビルド）／ホストテストを通してコミット。

## 完了条件 / 検証コマンド
- `cd firmware/pico2w-controller/test && make` が **green**（追加した純粋部テストを含む。native gcc `-Werror`）。
- ネットワーク**未設定時**は無線が初期化されず、USB-HID のみで従来どおり動作する。
- 設定投入時（§4.6）、Wi-Fi/BT で接続し、SW/VR状態送信・バックライト受領が USB-HID と同等セマンティクスで機能する（実機手動確認手順をREADMEに明記）。
- `cmake -B build -DPICO_BOARD=pico2_w [-DENABLE_WIRELESS=ON]` のクロスビルドが成功（SDK環境がある場合）。

## 技術的な補足 / レビュー観点
- **オプション扱い**: §2.5 は「任意」。優先度は P2-001/P2-002 より低い。実装しない選択も可（その場合は本タスクを `done` にせずスキップ理由を記録）。
- **共用/重複回避**: 送信の状態パッキングは `state_agg`（P2-001）、受領のバックライト分配は `backlight`（P2-002）を共用し、無線用に二重実装しない（`.claude/review-patterns.md`「設計・責務分離」）。
- **セキュリティ**: 資格情報をコード/コミット履歴に混入させない。設定は P2-002 のフラッシュ保存経由のみ（`.claude/review-patterns.md`「セキュリティ」）。
- **並行性/リソース**: lwIP コールバックとメインループの共有状態、ソケット/タイマー破棄、再接続競合、無線側障害がUSB経路・I2C集約を巻き込まないこと（障害隔離, 非機能 §4）。

## 参照
- 仕様: `docs/specs/pico2w-controller-firmware.md` §2.5, §3, §4 / `docs/specs/00-system-overview.md` §4.1, §4.6
- 前タスク: `docs/tasks/agent-P2-001-refactor-i2c-hid.md`, `docs/tasks/agent-P2-002-backlight-settings.md`
- レビュー基準: `.claude/review-patterns.md`
