# pico2w-controller ファームウェア

Raspberry Pi Pico 2W (`pico2_w`, RP2350 + CYW43) 向けコントローラー / タリー抽出ファームウェア。
仕様: [`docs/specs/pico2w-controller-firmware.md`](../../docs/specs/pico2w-controller-firmware.md)

## ディレクトリ構成

```
firmware/pico2w-controller/
├── CMakeLists.txt          # Pico SDK ターゲットのビルド定義
├── pico_sdk_import.cmake   # Pico SDK 検出用 (Pico SDK 同梱ファイルのコピー)
├── include/config.h        # ピン/ボタン/LED/I2C 定数, controller_id 契約 (後続タスク共通IF)
├── src/
│   ├── main.c               # 初期化 + メインループ (走査→デバウンス→送信を結線)
│   ├── config.c             # config.h の定数実体・get_controller_id() (Pico SDK非依存)
│   ├── board_id.c           # get_unique_board_id() (pico_unique_id 使用, Pico SDK依存)
│   ├── buttons.h            # デバウンス状態機械 + キーマトリクス走査の公開API
│   ├── buttons_debounce.c   # デバウンス状態機械の実体 (Pico SDK非依存, ホストテスト対象)
│   ├── buttons.c            # キーマトリクス走査・イベントキュー (GPIO依存)
│   ├── usb_link.h           # 押下イベント/タリーイベント送信の公開API
│   ├── usb_link.c           # USB-CDC経由のJSON送信 (TinyUSB依存)
│   ├── ddc_tally.h          # タリーデコード純粋関数 + GPIO結線の公開API
│   ├── ddc_tally_decode.c   # Blackmagicタリーコマンドのデコード (Pico SDK非依存, ホストテスト対象)
│   └── ddc_tally.c          # DDCライン(SCL/SDA)のGPIO割込みパッシブスニッフィング + LED制御 (GPIO依存)
└── test/                    # ホスト(native)ビルド用ユニットテスト (Pico SDK/クロスツールチェーン非依存)
    ├── Makefile
    ├── test_config.c
    ├── test_config_sub.c
    ├── test_buttons.c       # デバウンス状態機械(チャタリング/同時押し/押しっぱなし)
    └── test_ddc_tally.c     # タリーデコード(Program/Preview/Off、対象/非対象カメラ、ブロードキャスト)
```

`buttons_*` / `usb_link_*` / `ddc_tally_*` はすべて実装済み。

## クロスビルド (Pico SDK 必要)

このリポジトリのサンドボックス環境には Pico SDK / `arm-none-eabi` ツールチェーン / `cmake` が
インストールされていないため、以下のビルドはこの環境では未検証。実機ビルド環境で確認すること。

```sh
export PICO_SDK_PATH=/path/to/pico-sdk
cmake -B build -DPICO_BOARD=pico2_w
cmake --build build
# build/pico2w_controller.uf2 が生成される
```

サブ機として書き込む場合は `CONTROLLER_ROLE` をビルド時定義で切り替える:

```sh
cmake -B build -DPICO_BOARD=pico2_w -DCMAKE_C_FLAGS="-DCONTROLLER_ROLE=CONTROLLER_ROLE_SUB"
```

タリー抽出ドングルが監視するカメラ番号は `TALLY_CAMERA_ID` で切り替える(未指定時は1):

```sh
cmake -B build -DPICO_BOARD=pico2_w -DCMAKE_C_FLAGS="-DTALLY_CAMERA_ID=2"
```

## タリー抽出の手動確認手順 (実機)

`ddc_tally.c` のI2Cバス・パッシブスニッフィングと `ddc_tally_decode.c` の
Blackmagicタリーコマンド解釈(カテゴリ/パラメータID)は、この開発環境に実機
(ATEM Mini・カメラ・HDMIブレイクアウト基板)が無いため**未検証**。
`include/config.h` の `DDC_TALLY_I2C_ADDR` および `ddc_tally_decode.c` 冒頭の
`ASSUMED_TALLY_CATEGORY` / `ASSUMED_PARAM_*` はいずれも一次資料未確認の仮定値
(TODO)であり、実機確認後に差し替えが必要になる可能性が高い。書き込み後は以下の
手順で確認する:

1. 仕様書 §5 の結線 (HDMIブレイクアウト基板の 15pin(SCL) / 16pin(SDA) / 17pin(GND)
   を Pico 2W の `DDC_I2C_SCL_PIN` / `DDC_I2C_SDA_PIN` / GND に接続) を行う。
   高速TMDS線には触れないこと。
2. ATEM Mini の PGM/PVW を該当カメラ (`TALLY_CAMERA_ID`) に切り替え、
   `TALLY_LED_RED_PIN`(Program)・`TALLY_LED_GREEN_PIN`(Preview)のLEDが
   状態に応じて点灯/消灯することを目視確認する。
3. PCとUSB接続し、シリアルターミナル(例: `screen`, `minicom`, `picocom`)で
   ボーレートを問わずCDCポートを開き、状態変化のたびに
   `{"event":"tally","data":{"controller_id":"...","state":"program|preview|off","timestamp":...}}`
   が1行出力されることを確認する。
4. 期待通りに動作しない場合、ロジックアナライザでDDCライン(SCL/SDA)を
   キャプチャし、実際のI2Cアドレスとタリーコマンドのバイト構造を確認したうえで
   `DDC_TALLY_I2C_ADDR` / `ASSUMED_TALLY_CATEGORY` / `ASSUMED_PARAM_*` を実測値に
   更新する(呼び出し側のインターフェースは変更不要)。

## ホストテスト (Pico SDK 不要, このリポジトリで検証済み)

I/O に依存しないロジック（`config.h` の定数、`get_controller_id()`、ボタンのデバウンス
状態機械、今後追加される tally decode など）はホストの `gcc` でネイティブビルド・実行してテストする。

```sh
cd firmware/pico2w-controller/test
make        # ビルド + 実行。全テスト green で終了コード0
```
