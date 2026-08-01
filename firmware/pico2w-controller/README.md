# pico2w-controller ファームウェア

Raspberry Pi Pico 2W (`pico2_w`, RP2350 + CYW43) 向けマスターコントローラーファームウェア。

> 親プロジェクト: [`../../README.md`](../../README.md) ／ 仕様書: [`docs/specs/pico2w-controller-firmware.md`](../../docs/specs/pico2w-controller-firmware.md) / 共通プロトコル: [`docs/specs/00-system-overview.md`](../../docs/specs/00-system-overview.md) §4

## 役割

全スイッチングモジュール(CH32V003, スロットごとに専用I2Cバス1本・アドレスは全台`0x30`)を
**I2Cマスター**として定期ポーリングして
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
│   ├── module_bus.h          # スロットごとのI2Cバス定義 (SDA/SCL) の公開API
│   ├── module_bus.c          # バス定義の実体 + GPIO→I2Cコントローラの純粋変換 (Pico SDK非依存, ホストテスト対象)
│   ├── i2c_modules.h         # I2Cポーリング + バックライト書込の公開API
│   ├── i2c_modules.c         # 全バス(スロット)の定期ポーリング + 0x10 BACKLIGHT書込 (ハードウェアI2C依存)
│   ├── fake_modules.h        # デバッグ用fakeモジュール層の公開API (ENABLE_FAKE_MODULESビルドのみ)
│   ├── fake_modules_codec.c  # デバッグ用出力レポート0x04のパース/適用 (Pico SDK非依存, ホストテスト対象)
│   ├── i2c_modules_fake.c    # i2c_modules.c の差し替え: I2Cを使わずRAM上の偽モジュールで応答する
│   ├── backlight.h           # 出力レポート0x02のコマンド型 + パース/キュー/I2C書込API
│   ├── backlight_codec.c     # 出力レポート0x02のパース (Pico SDK非依存, ホストテスト対象)
│   ├── backlight.c           # 受領キュー + I2C `0x10 BACKLIGHT` 書込 (I2C依存)
│   ├── settings.h            # 設定構造体 + シリアライズ/フラッシュ永続化/feature結線API
│   ├── settings_codec.c      # シリアライズ/デシリアライズ/CRC検証/controller_idフォールバック (Pico SDK非依存, ホストテスト対象)
│   ├── settings.c            # フラッシュ読み書き + HIDフィーチャーレポート結線 (hardware/flash依存)
│   ├── usb_hid.h             # HID状態送出・controller_id設定の公開API
│   ├── usb_hid.c             # ベンダー定義HIDデバイス(記述子一式 + 入出力/featureレポート, TinyUSB依存)
│   ├── wireless.h            # ワイヤレス制御チャネルのフレーム定義 + 純粋部/I/O部の公開API
│   ├── wireless_codec.c      # STATE/BACKLIGHTフレームのエンコード/デコード (Pico SDK非依存, ホストテスト対象)
│   └── wireless.c            # CYW43接続(バックオフ再接続)+ UDP送受信 (CYW43/lwIP依存, ENABLE_WIRELESSビルドのみ)
└── test/                    # ホスト(native)ビルド用ユニットテスト (Pico SDK/クロスツールチェーン非依存)
    ├── Makefile
    ├── test_config.c
    ├── test_config_sub.c
    ├── test_module_bus.c      # スロット→SDA/SCL GPIOの割当・重複無し・RP2xxxのI2Cピン規則適合
    ├── test_state_agg.c       # 入力レポートパッキング(module_present/SW/VR位置/seq)・VRデッドバンド判定
    ├── test_backlight.c       # 出力レポート0x02のパース(RGBバイト順・範囲外module_index棄却)
    ├── test_settings.c        # 設定シリアライズ往復・破損検出・controller_idフォールバック
    ├── test_wireless_codec.c  # STATE/BACKLIGHTフレームのエンコード/デコード(state_agg/backlight_codecへの委譲含む)
    └── test_fake_modules.c    # デバッグ用出力レポート0x04のパース・モジュール状態への適用
```

`state_agg_*`/`i2c_modules_*`/`usb_hid_*`/`backlight_*`/`settings_*`/`wireless_*` はすべて実装済み。

## I2C / USB-HID 契約の概要

- **I2C** (`config.h`/`module_bus.c`, 親仕様書 §4.5): Pico=マスター、モジュール=スレーブ。
  実基板 `softswitcher_module_master` は**モジュール1台につき専用のI2Cバスを1本**引き出す
  (`MODULE_BUS_COUNT`=5)。モジュール側にアドレスストラップが無く全台が `MODULE_I2C_ADDR`(0x30)
  で待ち受けるため、**モジュール番号(HIDレポートのスロット位置) = バス番号**となる。

  | バス | Pico実装ピン | GPIO (SDA/SCL) | コントローラ | 経路 |
  | --- | --- | --- | --- | --- |
  | 1 | 26pin / 27pin | 20 / 21 | i2c0 | 1x07コネクタ (数珠つなぎの先頭) |
  | 2 | 31pin / 32pin | 26 / 27 | i2c1 | 2x08コネクタ |
  | 3 | 6pin / 7pin | 4 / 5 | i2c0 | 2x08コネクタ |
  | 4 | 4pin / 5pin | 2 / 3 | i2c1 | 2x08コネクタ |
  | 5 | 1pin / 2pin | 0 / 1 | i2c0 | 2x08コネクタ |

  I2Cコントローラは2基しか無いため、同じコントローラを共有するバス(i2c0: 1,3,5 / i2c1: 2,4)は
  ピン機能を `GPIO_FUNC_I2C` / `GPIO_FUNC_NULL` で付け替えて時分割する。非選択バスのピンは
  内蔵プルアップでHigh(アイドル)に保たれるため、そのバスのモジュールはバスが静止しているだけで
  状態を保持する。外付けプルアップ抵抗が基板・モジュールとも無いため、バスクロック
  `MODULE_I2C_BAUD` の既定は100kHz(外付けプルアップを実装したら引き上げ可)。

  `0x00 STATE`(read 3バイト: SW下位4bit/VR_SRC1/VR_SRC2)がポーリング対象、
  `0x10 BACKLIGHT`(write 12)はバックライト配布で使用、`0xF0 INFO`(read 4)は未使用(契約のみ)。
  1モジュールのタイムアウト/NACKはそのモジュールだけスキップし、他バスのポーリングは
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
  (`backlight.c`)へ委譲する。
- **TinyUSBの引数の形が経路で異なる点に注意**: 制御転送(SET_REPORT)では実際の`report_id`と
  Report ID除去済みのボディが渡るが、割込みOUTエンドポイント経由では`report_id=0`固定で
  Report IDを含んだ生バッファが渡る(`lib/tinyusb/src/class/hid/hid_device.c`)。ホスト
  (Windows)は出力レポートを後者で送るため、`report_id`だけで振り分けると出力レポートが一切
  届かない。`tud_hid_set_report_cb`の冒頭で前者の形へ揃えてから振り分けている。
- ホストは短い出力レポートをデバイスの最大出力レポート長までゼロパディングして送ってくる。
  `0x02`(13バイト)が最大長のため`0x02`自身は影響を受けないが、これより短い出力レポートを
  追加する場合は長さを完全一致で検証してはならない。パース(`backlight_parse_output_report`, `backlight_codec.c`)は
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

### ワイヤレス制御チャネル (任意, §2.5)

USB-HIDが使えない/無線運用時の**代替経路**として、Wi-Fi(CYW43)経由でSW/VR状態送信・
バックライト受領をUSB-HIDと同等のセマンティクスで提供する。**USB直結が主経路**であり、
本チャネルはあくまで代替(併用可、二重送出可)。**ネットワーク未設定時は完全に無効**で、
USB-HIDのみで従来どおり動作する(`wireless_init`が`settings.wifi_ssid`の空チェックで判定)。

- **ビルド切替**: デフォルトでは無効(`ENABLE_WIRELESS` OFF)。無効ビルドでは
  `src/wireless.c`自体がビルド対象に含まれず、CYW43/lwIPの依存・リンクサイズは一切
  増えない(`CMakeLists.txt`)。純粋部(`wireless_codec.c`)のみ常時ビルドされる。
- **フレーム形式**: 1メッセージ = 1 UDPデータグラム = `[type(1)]` + `[payload]`。
  `type`にはHIDレポートIDをそのまま流用し(`WIRELESS_MSG_TYPE_STATE`=`0x01`,
  `WIRELESS_MSG_TYPE_BACKLIGHT`=`0x02`)、`payload`はHID入力/出力レポートと**同一の
  パッキング**を使う。ポートは`WIRELESS_UDP_PORT`(`9200`)。
- **送信** (`wireless_send_state`): 集約状態(`state_agg`, P2-001)を`state_agg_pack`と
  同一パッキングでSTATEフレームへエンコードし送出する。エンコード自体は
  `wireless_codec_build_state_frame`(`wireless_codec.c`)が`state_agg_pack`を呼ぶだけで、
  HID経路と二重実装しない。送信先(PC側)は直近にBACKLIGHTフレームを送ってきた相手を
  記憶して使うため、PCから一度もパケットを受け取っていない場合は送出しない。未接続/
  未確定時はUSB経路同様ドロップする(バッファリング・ブロックしない)。
- **受領** (UDP受信コールバック): BACKLIGHTフレームは`wireless_codec_parse_backlight_frame`
  (type一致・長さ一致を検証後、パース自体は`backlight_parse_output_report`
  (`backlight_codec.c`)へ委譲)で検証し、成功分は`backlight_enqueue_command`
  (`backlight.c`)でUSB出力レポート0x02と**同一の受領キュー**へ積む。以降は
  `backlight_task()`が1回1件ずつI2C `0x10`へ配布する経路を共用するため、分配ロジックを
  二重実装しない。
- **接続・再接続**: `wireless_init`はネットワーク設定済みの場合のみCYW43を初期化し
  STAモードでUDPソケットを確立する。`wireless_task`(メインループから継続呼び出し)は
  非ブロッキングでリンク状態を確認し、未接続なら指数バックオフ(初期1秒、上限30秒)で
  `cyw43_arch_wifi_connect_async`による再接続を試みる。初期化失敗・切断・タイムアウトは
  すべてこのファイル内で吸収し、USB経路・I2C集約ポーリングへは一切波及しない(障害隔離)。
- **セキュリティ**: Wi-Fi資格情報はPCから投入されフラッシュ保存(`settings`, P2-002)
  経由のみで扱い、コード/コミット履歴には一切含めない。

有効化ビルド手順:

```sh
export PICO_SDK_PATH=/path/to/pico-sdk
cmake -B build -DPICO_BOARD=pico2_w -DENABLE_WIRELESS=ON
cmake --build build
```

資格情報はPCから投入する前提(親仕様書 §4.6): HIDフィーチャーレポート
(`HID_REPORT_ID_SETTINGS_FEATURE`)で`wifi_ssid`/`wifi_password`を含む設定を投入・保存後、
Picoを再起動すると`wireless_init`がフラッシュから読み出した資格情報でWi-Fi接続を試みる。
`wifi_ssid`が空のままなら無線は初期化されず、従来どおりUSB-HIDのみで動作する。

代替チャネルの手動確認手順(実機・SDK環境が無いため未検証):

1. 上記手順でWi-Fi資格情報を投入・保存し、Picoを再起動する。
2. PC側から`WIRELESS_UDP_PORT`(`9200`)宛にBACKLIGHTフレーム
   (`[0x02][module_index][RGB×4灯]`)を一度送り、Pico側に送信先を認識させる
   (`wireless_send_state`は直近の受信元にのみ送出するため)。
3. 対象モジュールの4灯が指定色で点灯すること(USB経路のバックライト配布と同様)を確認する。
4. 以降、PCが同ポートでSTATEフレーム(`[0x01][module_present×...]`)を受信できること、
   USB-HID入力レポート0x01と同一のバイト配置であることを確認する。
5. Wi-Fi接続を切断し、指数バックオフで再接続が試みられること、その間もUSB-HID経路の
   SW/VR状態送出が滞留しないことを確認する(障害隔離)。

### デバッグ用 fake モジュール層 (任意, 実機モジュール不要)

実機のスイッチングモジュール(CH32V003)が1台も無い状態で、SW/VR集約・HID経路・バックライト
配布を通しで検証するための開発専用ビルド。`-DENABLE_FAKE_MODULES=ON` を指定すると
`src/i2c_modules.c` の代わりに `src/i2c_modules_fake.c` がリンクされ、I2Cバスの代わりに
PCからのデバッグ用HIDレポートがモジュール状態を供給する。

- **ビルド切替**: デフォルトOFF。OFFのビルドには偽モジュール層もデバッグ用レポートも一切
  含まれず、HID記述子(§4.1)も変化しない。
- **デバッグ用レポート**: 出力`0x04`(`module_index` + `present` + `switches` + VR×2)でPCから
  1モジュール分の状態を注入し、feature `0x05`(`module_index` + 4灯分RGB)で
  `backlight_task()` が最後に「配布」したバックライトを読み出す。
- **入力レポートは`0x01`のまま**にしてある。PC側(`src/Switcher.Hid/Devices/HidSharpDevice.cs`)が
  入力レポートの先頭Report IDを無条件に剥がす実装のため、入力レポートを増やすとこのビルドを
  本番アプリへ繋いだときに状態レポートとして誤解釈されてしまう。そのためバックライトの
  読み出しは入力ではなくfeatureにしている。
- **`main.c` は無改変**。fake層は `i2c_modules.h` の契約をそのまま実装するため、メインループ側に
  分岐が無い。

```sh
cmake -B build-fake -DPICO_BOARD=<board> -DENABLE_FAKE_MODULES=ON
cmake --build build-fake
```

ブラウザからSW/VRを操作するGUIは [`tools/module-simulator`](../../tools/module-simulator/README.md)
にある(検証できる範囲/できない範囲もそちらに記載)。

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

ワイヤレス制御チャネル(任意, §2.5)を有効化する場合は `-DENABLE_WIRELESS=ON` を追加する
(「ワイヤレス制御チャネル」の節参照)。指定しない場合はデフォルトでOFFとなり、
CYW43/lwIPはリンクされない。

## 実機での手動確認手順

`i2c_modules.c` のI2Cポーリング(=実機モジュールとのI2C通信そのもの)は、スイッチングモジュール
(CH32V003)が手元に無いため**未検証**。

一方、`usb_hid.c` のベンダーHID記述子一式・入力レポート`0x01`の集約/送出・出力レポート`0x02`の
バックライト配布経路は、RP2040実機 + [`tools/module-simulator`](../../tools/module-simulator/README.md)
(`-DENABLE_FAKE_MODULES=ON` ビルド)で**確認済み**(下記手順2・3・4・6相当)。この検証で
`tud_hid_set_report_cb` の振り分けが割込みOUT経由の出力レポートを取りこぼす不具合が見つかり、
修正済み(「バックライト配布」の節参照)。

書き込み後は以下の手順で確認する:

1. 仕様書 §5 の結線(モジュールをマスター基板のコネクタへ数珠つなぎで接続する。先頭が1x07の
   バス1、以降2x08経由でバス2..5。上表の `MODULE_BUSES` と一致していることを確認する)を行う。
   モジュール側はアドレス`0x30`固定で、番号設定用のストラップは無い。
2. PCとUSB接続し、OS側のHID一覧(例: Linuxなら `lsusb`/`hidraw`、WindowsならUSBデバイスツリー)で
   ベンダー定義HIDデバイスとして列挙され、シリアル番号文字列が
   `<unique_board_id>-<controller_id>` になっていることを確認する。
3. モジュールを1台のみ接続した状態で入力レポート`0x01`を読み取り、`module_present`ビットマップの
   該当ビットのみが立つこと、SWを押下/VRを回した際に対応バイトが追従すること、`seq`が送出ごとに
   ローテートすることを確認する。次にモジュールを2段目・3段目…へ挿し替え、立つビットが
   スロット位置(バス番号)どおりに移動することを確認する(モジュール番号=バス番号)。
4. モジュールを未接続のまま起動し、ポーリングが詰まらず他の接続済みモジュールの状態送出が継続
   することを確認する(1モジュール不通時の障害隔離)。同一I2Cコントローラを共有するバス
   (i2c0: 1,3,5 / i2c1: 2,4)に同時にモジュールを挿し、両方が並行して更新され続けること
   (時分割のピン切替が破綻していないこと)も確認する。
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

I/O に依存しないロジック(`config.h` の定数、`get_controller_id()`、スロットごとのI2Cバス定義
(`module_bus.c`)、入力レポートパッキング/VRデッドバンド判定、出力レポート`0x02`パース、
設定シリアライズ/破損検出、ワイヤレス
STATE/BACKLIGHTフレームのエンコード/デコード)はホストの `gcc` でネイティブビルド・実行して
テストする。CYW43/lwIP依存のI/O部(`wireless.c`)はこのホストテスト対象外(実機/SDK環境が
必要, 「ワイヤレス制御チャネル」節の手動確認手順を参照)。

```sh
cd firmware/pico2w-controller/test
make        # ビルド + 実行。全テスト green で終了コード0
```
