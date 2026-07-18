---
name: agent-P-003-ddc-tally-extraction
status: done
pid: 310048
agent_cli: sonnet
---

# 実装指示書: HDMI DDCスニッフィング & タリー抽出

## 概要
HDMIの低速制御線（15:SCL / 16:SDA / 17:GND）にパッシブ接続し、ATEM Mini↔カメラ間のDDC/I2Cパケットを盗聴。Blackmagic Camera Control Protocol をデコードして自カメラ宛のタリーON/OFF(Red/Green)を検知し、GPIO LED点灯＋USB経由で状態通知する。

## 前提条件（依存タスク）
- `agent-P-001-firmware-scaffold` が `done`（I2C/LEDピン定義・USB基盤・ホストテスト土台が確定）。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `firmware/pico2w-controller/`（主に `src/ddc_tally.{c,h}`, `src/main.c` の結線, `test/`）。

## 実装ステップ
1. `src/ddc_tally.{c,h}`:
   - Pico のハードウェアI2C（`config.h` の DDC ピン）を**スレーブ/モニタ的にパッシブ受信**する構成でバイト列を収集（バスへ能動的に書き込まない＝ATEM↔カメラ通信を阻害しない）。
   - **Blackmagicタリーデコードを純粋関数として実装**（受信バイト列→対象カメラID→タリー状態 Red/Green/Off）。プロトコル構造・アドレスの根拠をコメントに記載（不明点は仮定を明記しTODO化）。
   - 自カメラID（`config.h` または `controller_id` 連動）に一致するタリーのみ反映。
2. LED制御: Red=Program / Green=Preview/Sub / 消灯=Off を GPIO で点灯。
3. USB通知: タリー状態変化時に USB(CDC)経由で状態を送出（PC側が受け取れる形式。§4系のイベントに準拠、種別を `tally` 等で区別）。
4. `src/main.c` に結線。I2C受信はメインループ/割込みでノンブロッキングに。
5. ホストテスト(`test/`): タリーデコード純粋関数を、既知バイト列サンプル（Program/Preview/Off、対象/非対象カメラID）で検証。
6. クロスビルド（可能なら）／ホストテストを通しコミット。

## 完了条件 / 検証コマンド
- `test/` のタリーデコード・ユニットテストが green（対象/非対象カメラ、Red/Green/Off）。
- クロスビルドが成功（環境がある場合）。
- LEDおよびUSB通知が状態に応じて更新される（手動確認手順をREADMEに追記）。

## 技術的な補足 / レビュー観点
- **非干渉の保証**: バスへ能動送信しない（パッシブ）。高速TMDS線には触れない前提を守る（仕様 §2.3, §5）。
- **プロトコル不確実性**: Blackmagicの正確なフレーム構造が未確定な部分は仮定・出典・TODOを明記し、デコード関数を差し替え可能に設計。
- **並行性/割込み**: I2C受信バッファと本ループの共有はロック/リングバッファで安全に。`.claude/review-patterns.md`「並行性」「リソース管理」。

## 参照
- 仕様: `docs/specs/pico2w-controller-firmware.md` §2.3, §5
