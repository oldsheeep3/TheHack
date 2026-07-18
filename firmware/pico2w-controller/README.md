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
│   ├── usb_link.h           # 押下イベント送信の公開API
│   └── usb_link.c           # USB-CDC経由のJSON送信 (TinyUSB依存)
└── test/                    # ホスト(native)ビルド用ユニットテスト (Pico SDK/クロスツールチェーン非依存)
    ├── Makefile
    ├── test_config.c
    ├── test_config_sub.c
    └── test_buttons.c       # デバウンス状態機械(チャタリング/同時押し/押しっぱなし)
```

`ddc_tally_*` は後続タスクで実装されるモジュールのフックで、`main.c` では
`__attribute__((weak))` 宣言のみを置いている（未実装時は no-op）。
`buttons_*` / `usb_link_*` は本タスクで実装済み。

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

## ホストテスト (Pico SDK 不要, このリポジトリで検証済み)

I/O に依存しないロジック（`config.h` の定数、`get_controller_id()`、ボタンのデバウンス
状態機械、今後追加される tally decode など）はホストの `gcc` でネイティブビルド・実行してテストする。

```sh
cd firmware/pico2w-controller/test
make        # ビルド + 実行。全テスト green で終了コード0
```
