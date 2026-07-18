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
│   ├── main.c                # 初期化 + メインループ (I2Cポーリング→集約→HID送出、出力/feature結線)
│   ├── config.c              # get_controller_id() (Pico SDK非依存)
│   ├── board_id.c            # get_unique_board_id() (pico_unique_id使用, Pico SDK依存)
│   ├── state_agg.h           # モジュール状態配列/入力レポートパッキングの公開API
│   ├── state_agg.c           # 入力レポート0x01パッキング + VRデッドバンド判定 (Pico SDK非依存, ホストテスト対象)
│   ├── i2c_modules.h         # I2Cポーリング + バックライト書込の公開API
│   ├── i2c_modules.c         # 0x30..0x37の定期ポーリング + 0x10 BACKLIGHT書込 (ハードウェアI2C依存)
│   ├── backlight.h           # 出力レポート0x02のコマンド型 + パース/キュー/I2C書込API
│   ├── backlight_codec.c     # 出力レポート0x02のパース (Pico SDK非依存, ホストテスト対象)
│   ├── backlight.c           # 受領キュー + I2C `0x10 BACKLIGHT` 書込 (I2C依存)
│   ├── settings.h            # 設定構造体 + シリアライズ/フラッシュ永続化/feature結線API
│   ├── settings_codec.c      # シリアライズ/デシリアライズ/CRC検証/controller_idフォールバック (Pico SDK非依存, ホストテスト対象)
│   ├── settings.c            # フラッシュ読み書き + HIDフィーチャーレポート結線 (hardware/flash依存)
│   ├── usb_hid.h             # HID状態送出・controller_id設定の公開API
│   └── usb_hid.c             # ベンダー定義HIDデバイス(記述子一式 + 入出力/featureレポート, TinyUSB依存)
└── test/                    # ホスト(native)ビルド用ユニットテスト (Pico SDK/クロスツールチェーン非依存)
    ├── Makefile
    ├── test_config.c
    ├── test_config_sub.c
    ├── test_state_agg.c     # 入力レポートパッキング(module_present/SW/VR位置/seq)・VRデッドバンド判定
    ├── test_backlight.c     # 出力レポート0x02のパース(RGBバイト順・範囲外module_index棄却)
    └── test_settings.c      # 設定シリアライズ往復・破損検出・controller_idフォールバック
```

`state_agg_*`/`i2c_modules_*`/`usb_hid_*`/`backlight_*`/`settings_*` はすべて実装済み。

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
  対策)。`controller_id`はHIDシリアル文字列(`get_unique_board_id()` + 設定解決済みの
  controller_id)で提示する。USB未接続/未準備時は送出をドロップする(バッファリング・
  ブロックしない)。

### バックライト配布 (出力レポート`0x02` → I2C `0x10`, §2.3/§4.1/§4.5)

- PCが出力レポート`0x02`(`module_index`(1) + 4灯分RGB(12) = `HID_REPORT_BACKLIGHT_OUT_LEN`
  バイト)を送出すると、`usb_hid.c`の`tud_hid_set_report_cb`が`backlight_on_output_report()`
  (`backlight.c`)へ委譲する。パース(`backlight_parse_output_report`, `backlight_codec.c`)は
  範囲外`module_index`・長さ不一致を棄却し、RGBバイト順はそのまま保持する(SK6812の
  GRB変換はモジュール側で実施)。
- パース成功分は受領キュー(`backlight.c`, 8件FIFO, 溢れ時は最古を破棄)へ積むのみで
  I2C書込は行わないため、HID受信・メインループをブロックしない。
- メインループが毎周回`backlight_task()`を呼び、キューから最大1件を取り出して
  `i2c_modules_write_backlight()`(`0x10 BACKLIGHT`, 12バイトwrite)でI2C書込する。
  1回の呼び出しで消費するのは最大1件のため、STATEポーリング(1kHz目安)のレイテンシを
  大きく阻害しない。対象モジュール不通/タイムアウト時はそのコマンドを破棄して継続する
  (集約全体を止めない)。

### 設定投入・フラッシュ永続化 (HIDフィーチャーレポート, §2.4/§4.6)

- 設定の正はPC側にあり、`controller_id`(main/sub)・モジュール割付ヒント・Wi-Fi/BT資格情報
  (SSID/パスワード)をHIDフィーチャーレポート(`HID_REPORT_ID_SETTINGS_FEATURE`)でPicoへ
  投入する。`settings.h`の`settings_t`にまとめ、`settings_codec.c`(Pico SDK非依存)で
  マジック(`P2ST`)+バージョン+CRC32を付与してシリアライズ/検証する。
- SET_REPORT受領(`settings_on_feature_report`, `settings.c`)は検証成功時のみ
  `settings_save()`でフラッシュ(末尾セクタ, `hardware/flash`のセクタ消去+書込)へ保存する。
  不正なデータは無視し、既存のフラッシュ内容を上書きしない。
- GET_REPORTでは`settings_build_feature_report()`が現在フラッシュに保存されている設定を
  シリアライズして返し、PC側が投入内容を確認できる。
- `controller_id`は`settings_resolve_controller_id()`が解決する: 設定の`controller_role`が
  設定済み(`CONTROLLER_ROLE_MAIN`/`SUB`)ならそれを優先し、未設定
  (`SETTINGS_CONTROLLER_ROLE_UNSET`)ならビルド時`CONTROLLER_ROLE`(`get_controller_id()`)へ
  フォールバックする。起動時に一度ロードし、HIDシリアル文字列(`usb_hid_set_controller_id()`)
  へ反映する。
- **HIDフィーチャーレポート1件のサイズ制約**: USB Full-Speed制御転送はTinyUSBの制御バッファ
  (`CFG_TUD_ENDPOINT0_SIZE`=64バイト)に収まる必要があるため、`SETTINGS_SERIALIZED_LEN`
  (現状58バイト)は64バイト未満に設計してある。この制約によりSSID/パスワードは
  `SETTINGS_WIFI_SSID_LEN`/`SETTINGS_WIFI_PASSWORD_LEN`(各20バイト, NUL込みで19文字まで)に
  切り詰めている。より長い資格情報が必要になった場合は複数レポートへ分割するプロトコル
  拡張が必要(本タスクでは非対応)。
- **セキュリティ**: Wi-Fi/BT資格情報はフラッシュ保存のみとし、ログ/シリアル(UART stdio)へ
  平文出力しない。コード/コミット履歴にも資格情報を含めない。

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
6. **バックライト配布**: PCからHID出力レポート`0x02`(`module_index` + 4灯分RGB)を送出し、
   対象モジュールの4灯が指定色で点灯すること、他モジュールに影響しないことを確認する。
   複数モジュールへ連続送出した際もSW/VR状態送出(入力レポート`0x01`)が滞留しないことを
   確認する(受領キュー+1回1件消費の効果)。
7. **設定投入・フラッシュ永続化**: PCからHIDフィーチャーレポート(`HID_REPORT_ID_SETTINGS_FEATURE`)
   で`controller_id`(main/sub)・モジュール割付ヒント・Wi-Fi/BT資格情報を投入し、GET_REPORTで
   読み戻して投入内容と一致することを確認する。その後Picoを再起動し、HIDシリアル文字列の
   `controller_id`部分が投入した値を保持していること(フラッシュからの復元)を確認する。
   不正な内容(未検証のツールで意図的に壊したレポート等)を送っても既存の設定が上書きされない
   ことも確認する。

## ホストテスト (Pico SDK 不要, このリポジトリで検証済み)

I/O に依存しないロジック(`config.h` の定数、`get_controller_id()`、入力レポートパッキング/
VRデッドバンド判定、出力レポート`0x02`パース、設定シリアライズ/破損検出)はホストの `gcc` で
ネイティブビルド・実行してテストする。

```sh
cd firmware/pico2w-controller/test
make        # ビルド + 実行。全テスト green で終了コード0
```
