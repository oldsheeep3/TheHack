# pico2w-controller ファームウェア

Raspberry Pi Pico 2W (`pico2_w`, RP2350 + CYW43) 向けマスターコントローラーファームウェア。

> 親プロジェクト: [`../../README.md`](../../README.md) ／ 仕様書: [`docs/specs/pico2w-controller-firmware.md`](../../docs/specs/pico2w-controller-firmware.md) / 共通プロトコル: [`docs/specs/00-system-overview.md`](../../docs/specs/00-system-overview.md) §4

## 役割

全スイッチングモジュール(CH32V003, `0x30`..`0x37`)を **I2Cマスター**として定期ポーリングして
SW/VR状態を集約し、**ベンダー定義USB-HID**の入力レポート`0x01`でPCへ送出する。
物理キーマトリクス走査とHDMI DDCタリー抽出(旧実装)は本タスクで**廃止**し、前者はモジュール
(CH32V003)側へ移管、後者は外部タリーデバイスが担当する。

## ディレクトリ構成

```
firmware/pico2w-controller/
├── CMakeLists.txt          # Pico SDK ターゲットのビルド定義
├── pico_sdk_import.cmake   # Pico SDK 検出用 (Pico SDK 同梱ファイルのコピー)
├── include/
│   ├── config.h             # 定数(MAX_MODULES/I2C/HIDレポート契約等), controller_id契約
│   └── tusb_config.h        # TinyUSB設定 (ベンダーHIDのみ有効化, CDC無効)
├── src/
│   ├── main.c                # 初期化 + メインループ (I2Cポーリング→集約→HID送出を結線)
│   ├── config.c              # get_controller_id() (Pico SDK非依存)
│   ├── board_id.c            # get_unique_board_id() (pico_unique_id使用, Pico SDK依存)
│   ├── state_agg.h           # モジュール状態配列/入力レポートパッキングの公開API
│   ├── state_agg.c           # 入力レポート0x01パッキング + VRデッドバンド判定 (Pico SDK非依存, ホストテスト対象)
│   ├── i2c_modules.h         # I2Cポーリングの公開API
│   ├── i2c_modules.c         # 0x30..0x37の定期ポーリング (ハードウェアI2C依存)
│   ├── usb_hid.h             # HID状態送出の公開API
│   └── usb_hid.c             # ベンダー定義HIDデバイス(記述子一式 + 入力レポート送出, TinyUSB依存)
└── test/                    # ホスト(native)ビルド用ユニットテスト (Pico SDK/クロスツールチェーン非依存)
    ├── Makefile
    ├── test_config.c
    ├── test_config_sub.c
    └── test_state_agg.c     # 入力レポートパッキング(module_present/SW/VR位置/seq)・VRデッドバンド判定
```

`state_agg_*`/`i2c_modules_*`/`usb_hid_*` はすべて実装済み。HID出力レポート`0x02`(バックライト
指定)とfeatureレポートは契約(`config.h`のReport ID/長さ定数)のみ用意してあり、実装は
P2-002(`docs/tasks/agent-P2-002-backlight-settings.md`)で行う。

## I2C / USB-HID 契約の概要

- **I2C** (`config.h`, 親仕様書 §4.5): Pico=マスター、モジュール=スレーブ。アドレス
  `0x30`+モジュール番号(0..7)。`0x00 STATE`(read 3バイト: SW下位4bit/VR_SRC1/VR_SRC2)を
  ポーリング対象、`0x10 BACKLIGHT`(write 12)/`0xF0 INFO`(read 4)は本タスクでは未使用(契約のみ)。
  1モジュールのタイムアウト/NACKはそのモジュールだけスキップし、他モジュールのポーリングは
  継続する(`i2c_modules.c`)。
- **USB-HID** (親仕様書 §4.1): ベンダー定義HID。入力レポート`0x01`(長さ
  `HID_REPORT_STATE_IN_LEN` = `1 + MAX_MODULES + 2*MAX_MODULES + 1`バイト)で
  `module_present`ビットマップ・各モジュールSW状態・各モジュールVR値・`seq`を送出する。
  状態変化時は即時送出、無変化でも`HID_STATE_SEND_INTERVAL_MS`ごとに定期送出する(取りこぼし
  対策)。`controller_id`はHIDシリアル文字列(`get_unique_board_id()` + `get_controller_id()`)で
  提示する。USB未接続/未準備時は送出をドロップする(バッファリング・ブロックしない)。

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

## 実機での手動確認手順

`i2c_modules.c` のI2Cポーリングと `usb_hid.c` のベンダーHID記述子一式は、この開発環境に
実機(Pico 2W・スイッチングモジュール×N)が無いため**未検証**。書き込み後は以下の手順で確認する:

1. 仕様書 §5 の結線(モジュールのI2C `SDA`/`SCL` を Pico 2W の `MODULE_I2C_SDA_PIN`/
   `MODULE_I2C_SCL_PIN` へマルチドロップ接続、アドレスはモジュールのストラップで`0x30`+番号に
   設定)を行う。
2. PCとUSB接続し、OS側のHID一覧(例: Linuxなら `lsusb`/`hidraw`、WindowsならUSBデバイスツリー)で
   ベンダー定義HIDデバイスとして列挙され、シリアル番号文字列が
   `<unique_board_id>-<controller_id>` になっていることを確認する。
3. モジュールを1台のみ接続した状態で入力レポート`0x01`を読み取り、`module_present`ビットマップの
   該当ビットのみが立つこと、SWを押下/VRを回した際に対応バイトが追従すること、`seq`が送出ごとに
   ローテートすることを確認する。
4. モジュールを未接続のまま起動し、ポーリングが詰まらず他の接続済みモジュールの状態送出が継続
   することを確認する(1モジュール不通時の障害隔離)。
5. デバッグ用の初期化ログ(`controller_id=...`)はUART stdio経由(`pico_enable_stdio_uart`)に
   出力される。USBはベンダーHID専用のためCDCシリアルとしては見えない。

## ホストテスト (Pico SDK 不要, このリポジトリで検証済み)

I/O に依存しないロジック(`config.h` の定数、`get_controller_id()`、入力レポートパッキング/
VRデッドバンド判定)はホストの `gcc` でネイティブビルド・実行してテストする。

```sh
cd firmware/pico2w-controller/test
make        # ビルド + 実行。全テスト green で終了コード0
```
