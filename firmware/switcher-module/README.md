# switcher-module ファームウェア

CH32V003F6P4（RISC-V RV32EC, 48MHz, Flash 16KB / SRAM 2KB）向け、スイッチングモジュール内マイコンファームウェア。SW×4 / VR×2 / SK6812MINI-E×4 をスキャン/駆動し、マスターの Pico 2W と I2C（スレーブ）で通信する。

> 親プロジェクト: [`../../README.md`](../../README.md) ／ 仕様書: [`docs/specs/switcher-module-firmware.md`](../../docs/specs/switcher-module-firmware.md)（親: [`docs/specs/00-system-overview.md`](../../docs/specs/00-system-overview.md) §4.0, §4.5）

## ピンアサイン（基板 `softswitcher_module_sw4`, CH32V003F4P6 TSSOP-20）

`include/module_config.h` が単一のソース。ピン番号は ch32fun と同じ「ポート番号<<4 | ピン番号」
エンコード（`MODULE_PIN()`）で持ち、ch32fun の `PD2` 等と一致することは各 `.c` の
`_Static_assert` がクロスビルド時に検証する。

| 信号 | ピン | 定数 | 備考 |
| --- | --- | --- | --- |
| `SDA` / `SCL` | 11pin `PC1` / 12pin `PC2` | `MODULE_I2C_SDA_PIN` / `MODULE_I2C_SCL_PIN` | I2C1のシリコン固定ピン |
| `LED_DATA` | 14pin `PC4` | `BACKLIGHT_DATA_PIN` | SK6812MINI-E ×4 チェーン |
| `SW1` | 19pin `PD2` | `SW_PINS[SW_BIT_SW1]` | b0 = `PGM1×SRC1` |
| `SW2` | 1pin `PD4` | `SW_PINS[SW_BIT_SW2]` | b1 = `PGM1×SRC2` |
| `SW3` | 20pin `PD3` | `SW_PINS[SW_BIT_SW3]` | b2 = `PGM2×SRC1` |
| `SW4` | 2pin `PD5` | `SW_PINS[SW_BIT_SW4]` | b3 = `PGM2×SRC2` |
| `VOL1` | 5pin `PA1` | `VR_ADC_CHANNELS[VR_SRC1_INDEX]` = ADCチャネル**1** | VR_SRC1 |
| `VOL2` | 6pin `PA2` | `VR_ADC_CHANNELS[VR_SRC2_INDEX]` = ADCチャネル**0** | VR_SRC2 |

SWはいずれも内部プルアップ付き入力で、押下でGNDへ落ちるアクティブLow。`funAnalogRead()` は
ピン番号ではなくアナログチャネル番号を取るため、VRの定数はチャネル番号であることに注意
（`PA1`=ch1 / `PA2`=ch0 と番号が逆順になる）。

## ディレクトリ構成

```
firmware/switcher-module/
├── ch32v003fun/              # submodule (https://github.com/cnlohr/ch32v003fun)
├── Makefile                   # ch32v003fun/ch32fun/ch32fun.mk を使ったクロスビルド定義
├── switcher_module.c          # ch32v003funの規約上必要なエントリポイント (src/main.c をinclude)
├── include/
│   └── module_config.h        # SW/VR/LEDピン・I2Cベースアドレス(0x30)・MAX_MODULES(8)等の定数 (後続タスク共通IF)
├── src/
│   ├── main.c                 # 初期化 + メインループ骨格 (各サブシステムはmodule_hooks経由の空フック)
│   ├── module_config.c        # module_config.h の定数実体 (ch32v003fun非依存)
│   ├── module_hooks.h         # switches/adc/backlight/i2c_slave の init/task 関数宣言 (後続タスクが実装)
│   ├── module_hooks.c         # 上記の weak no-op デフォルト実装
│   ├── module_index.h         # モジュール番号取得 (get_module_index) の公開API
│   ├── module_index.c         # get_module_index() (実基板はストラップ無しのため固定値)
│   ├── module_index_scale.c   # モジュール番号→I2Cアドレスの純粋変換 (ホストテスト対象)
│   ├── switches.h / switches.c            # SW×4 の直読み (GPIO依存, module_hooks strong実装)
│   ├── switches_debounce.h / switches_debounce.c
│   │                            # SWデバウンス純粋状態機械 (GPIO非依存, ホストテスト対象)
│   ├── adc.h / adc.c            # VR_SRC1/VR_SRC2 読取 (ADC依存, module_hooks strong実装)
│   ├── adc_scale.h / adc_scale.c
│   │                            # ADC生値→0..255スケーリング(移動平均+デッドバンド)純粋関数 (ホストテスト対象)
│   ├── backlight.h / backlight.c
│   │                            # SK6812×4 800kHz GRBビットバン駆動 (GPIO依存, module_hooks strong実装)
│   ├── sk6812_frame.h / sk6812_frame.c
│   │                            # RGB→GRBフレーム組み立て純粋関数 (GPIO非依存, ホストテスト対象)
│   ├── i2c_regs.h / i2c_regs.c
│   │                            # I2Cレジスタマップの純粋部(領域判定・STATE/INFOバイト生成・
│   │                            # BACKLIGHT受領完了判定, ch32v003fun非依存, ホストテスト対象)
│   └── i2c_slave.h / i2c_slave.c
│                                # I2C1スレーブ初期化 + ISR(レジスタポインタ確定・1バイト授受・
│                                # 受信バッファ格納のみ) (I2C1依存, module_hooks strong実装)
└── test/                      # ホスト(native)ビルド用ユニットテスト (ch32v003fun/クロスツールチェーン非依存)
    ├── Makefile
    ├── stub/ch32fun.h         # ch32fun のホスト用スタブ (GPIO/ADCを配列へ記録するだけのモック)
    ├── stub/ch32fun_stub.c
    ├── test_module_config.c   # ピン定数・重複割当の検証
    ├── test_module_index.c
    ├── test_switches.c        # デバウンス状態機械 (純粋部)
    ├── test_switches_io.c     # switches.c 本体: ピン→ビット割当・アクティブLow・直読み (stub経由)
    ├── test_adc_scale.c
    ├── test_adc_io.c          # adc.c 本体: VR→ADCチャネル割当 (stub経由)
    ├── test_sk6812_frame.c
    └── test_i2c_regs.c
```

`switches_*` は M-002 で実装済み（`switches.c` がGPIO直読み、`switches_debounce.c` が純粋デバウンス状態機械）。`adc_*` / `backlight_*` は M-003 で実装済み（`adc.c` がVR_SRC1/VR_SRC2のADC走査、`adc_scale.c` が純粋スケーリング、`backlight.c` がSK6812のビットバン駆動、`sk6812_frame.c` が純粋なRGB→GRBフレーム組み立て）。`i2c_slave_*` / `i2c_regs_*` は本タスク(M-004)で実装済み（`i2c_regs.c` がレジスタ領域判定・バイト内容生成の純粋部、`i2c_slave.c` がI2C1スレーブ初期化とISR）。これにより `module_hooks.h` の全関数が strong 定義され、`module_hooks.c` の weak no-op は呼ばれなくなる。

## SW読取・デバウンス

4SW（`SW1..SW4` = `PGM1×SRC1, PGM1×SRC2, PGM2×SRC1, PGM2×SRC2`）はいずれも専用GPIOへ直結されているため、`switches_task()` は行選択を行わず内部プルアップ入力を毎ループ直読みする（Low = 押下）。生の4bitサンプル（`module_config.h` の `SW_BIT_SW1..SW_BIT_SW4` 準拠のビット位置）を `switches_debounce.c` の純粋状態機械へ渡す。各SWビットは独立に `SWITCHES_DEBOUNCE_STABLE_SAMPLES`（5）回連続で同じ生値が観測された時点で確定し、`switches_get_state()` が確定4bit状態を I2C `0x00 STATE` レジスタ[0] の下位4bit形式（上位4bitは0埋め）で返す。

## アナログVR読取（ADC）

`adc_task()` が毎ループ `VR_ADC_CHANNELS`（`VR_SRC1`=VOL1=ch1, `VR_SRC2`=VOL2=ch0）を `funAnalogRead()` で読み取り、生値をADC非依存の `adc_scale.c` へ渡す。`adc_scale_update()` は幅 `ADC_SCALE_WINDOW_SIZE`（4）の移動平均でノイズを抑え、さらに `ADC_SCALE_DEADBAND`（2）未満の微小変動は出力を更新しない（バタつき抑制）。初回サンプルは移動平均バッファ全体をそのサンプルで充填し、デッドバンド判定なしで即時反映する。`adc_get_vr()` が直近のスケール済み値（0..255）を返し、I2C `0x00 STATE` レジスタ`[1]`/`[2]` への実結線は M-004 で行う。

## バックライト駆動（SK6812MINI-E×4）

`backlight_set_rgb()` で受け取ったRGB（4灯×3バイト、親仕様書§4.5 `0x10 BACKLIGHT` 受領形式）は、GPIO非依存の `sk6812_frame_from_rgb()` によりSK6812送出順（GRB）フレームへ変換され、`backlight_task()` が `BACKLIGHT_DATA_PIN` へ800kHzのタイミングでビットバン出力する。色の加工（補正/ガンマ等）は行わず受領値を忠実に変換・出力するのみ。フレーム末尾は80us以上のLowでリセット（フレーム確定）する。ビットバンの割込み禁止区間は1灯（3バイト）単位に区切り最小化している（仕様書§4）。ビット単位のタイミング（NOPループ回数）はdatasheet基準値へのベストエフォートであり、`module_config.h` の暫定ピン値と同様にPCB確定後・実機オシロでの再調整を要する。バックライト未受領時（初期状態）は全灯消灯（0,0,0）を送出する。I2C `0x10 BACKLIGHT` レジスタからの実結線は M-004 で行う。

## モジュール番号とI2Cアドレス

- 実基板にはモジュール番号ストラップ（抵抗ID）が無く、ADCに回せる空きピンも無い（14pin `PC4` はバックライトの `LED_DATA`）。そのため `get_module_index()`（`src/module_index.c`）は常に `MODULE_INDEX_FIXED`（0）を返し、**全モジュールが `0x30` 固定で待ち受ける**。
- モジュールの識別はマスター（Pico）側が行う。マスター基板はモジュール1台につき専用のI2Cバスを1本引き出しており（数珠つなぎのn番目 = バスn）、**どのバスで応答したかがそのままモジュール番号（HIDレポートのスロット位置）**になる（`../pico2w-controller/src/module_bus.c` の `MODULE_BUSES`、親仕様書 §4.5）。
- I2Cスレーブアドレスの計算式 `I2C_BASE_ADDR(0x30) + module_index`（`i2c_slave_address_for_module()`）は、将来モジュール側にアドレスストラップを載せて1バスに複数台ぶら下げる構成へ戻す場合に備えて残してある。`i2c_slave_init()`（`src/i2c_slave.c`）が起動時に `get_module_index()` の結果からアドレスを算出し `I2C1->OADDR1` に設定する。

## I2Cレジスタマップ（親仕様書 §4.5, `src/i2c_regs.c`/`src/i2c_slave.c`）

Pico 2W（マスター）から見た、モジュール（スレーブ, アドレス `0x30` 固定）のレジスタ一覧。レジスタポインタ方式（先頭バイトでレジスタアドレスを指定し、以降を連続read/writeする一般的なI2Cデバイス方式）に従う。

| アドレス | 方向 | 長さ | 内容 |
| --- | --- | --- | --- |
| `0x00` STATE | read | 3B | `[0]` SW状態(下位4bit, `SW_BIT_SW1..SW_BIT_SW4`準拠, 上位4bitは0埋め) / `[1]` VR_SRC1(0..255) / `[2]` VR_SRC2(0..255) |
| `0x10` BACKLIGHT | write | 12B | 4灯分RGB（1灯3バイト×4, 受領順そのまま）。モジュール側で SK6812 GRB へ変換（`sk6812_frame_from_rgb()`）して駆動する |
| `0xF0` INFO | read | 4B | `[0..1]` firmware version(major/minor) / `[2]` capabilities（未定義ビットは0） / `[3]` module HW rev |

- STATEは `i2c_slave_task()` が毎ループ `switches_get_state()`/`adc_get_vr()` から再構築してスナップショットへ反映する（ISRは読み出し時にこのスナップショットをそのまま返すのみ）。
- BACKLIGHTはレジスタ先頭(`0x10`)から過不足なく12バイト受領した場合のみ（`i2c_regs_backlight_write_is_complete()`）受領完了フラグを立て、`i2c_slave_task()` がメインループ側で `backlight_set_rgb()` へ反映する。途中書込み・長さ不足の場合は無視し、直前の状態を維持する。
- 上記3領域以外のレジスタアドレスへの書込み、およびSTATE/INFO（read専用）への書込みは無視する（NACKはせず、データを保持しないだけ）。範囲外read（未定義領域からの読み出し）は `0x00` を返す。
- I2C ISR（`I2C1_EV_IRQHandler`）はレジスタポインタ確定・1バイト授受・BACKLIGHT受信バッファへの格納のみを行い、SWスキャン/ADC読取/SK6812駆動は行わない（メインループの `switches_task()`/`adc_task()`/`backlight_task()` が担う）。ISRとメインループ間の共有（STATEスナップショット・BACKLIGHT受信バッファ・受領完了フラグ）は `__disable_irq()`/`__enable_irq()` で保護した volatile 変数で安全化している。

## クロスビルド（`ch32v003fun` ツールチェーン必要）

このリポジトリのサンドボックス環境には RISC-V gcc ツールチェーン（`riscv64-unknown-elf-gcc` 等）がインストールされていないため、以下のビルドはこの環境では未検証。実機ビルド環境で確認すること。

```sh
# ch32v003fun (submodule) を取得
git submodule update --init --recursive

# RISC-V gcc ツールチェーンをPATHに用意する (例: xpack riscv-none-elf-gcc)。
# ch32fun.mk はPATH上の riscv64-unknown-elf-gcc / riscv-none-elf-gcc 等を自動検出する。

cd firmware/switcher-module
make        # switcher_module.elf / .bin / .hex を生成 (デフォルトターゲットは flash)
```

書き込み（flash）には `minichlink`（`ch32v003fun/minichlink/`, WCH-LinkE 等のプログラマ経由）を使う:

```sh
cd firmware/switcher-module
make flash  # ビルド + minichlinkでの書き込みを実行
```

## ホストテスト（ch32v003fun/クロスツールチェーン不要, このリポジトリで検証済み）

I/O に依存しない純粋ロジック（`module_config.h` の定数、デバウンス状態機械、ADCスケーリング、RGB→GRB変換、I2Cレジスタマップの純粋部、モジュール番号→I2Cアドレス変換）に加えて、**基板配線に直結する `switches.c` / `adc.c` 本体**もホストの `gcc` でネイティブビルド・実行してテストする。後者は `test/stub/ch32fun.h`（GPIO/ADCの読み書きを配列へ記録するだけのモック）を `-Istub` で本物より先に見つけさせることで、`src/*.c` を一切変更せずにリンクしている（ピン→ビット位置の割当・アクティブLow解釈・VR→ADCチャネルの割当が、基板のピンアサインとずれたら失敗する）。

厳密なタイミングやペリフェラルレジスタを直接叩く `.c`（`backlight.c` のビットバン、`i2c_slave.c` のI2C1 ISR）はスタブでは意味のある検証にならないため対象外で、従来どおり純粋部（`sk6812_frame.c` / `i2c_regs.c`）のみをテストする。

```sh
cd firmware/switcher-module/test
make        # ビルド + 実行。全テスト green で終了コード0
```

## 実機I2C確認手順

`i2c_slave.c` のI2C1スレーブ動作（ISR/レジスタマップ）は、この開発環境に実機（CH32V003・I2Cマスター）が無いため**未検証**。書き込み後は以下の手順で確認する:

1. モジュールをマスター基板のコネクタへ挿す（I2C `SDA`/`SCL` = 11pin `PC1` / 12pin `PC2` が、そのスロットに割り当たったバスへ繋がる）。汎用I2Cアダプタで直接叩く場合も同じ2本を使う。**基板・モジュールとも外付けプルアップ抵抗を持たない**ため、マスター側の内蔵プルアップ（`i2c_modules_init()`）か、アダプタ側のプルアップが有効であることを確認する。
2. アドレス応答確認: `i2c-tools`（Linux）等でバススキャンし、`0x30` にモジュールが応答することを確認する（モジュール番号によらず全台 `0x30`。どのスロットかはマスター側のバスで決まる）。

   ```sh
   i2cdetect -y <bus番号>
   ```

3. STATE(`0x00`, read 3B)確認: SWを1つ押下・VRを回しながら読み出し、`[0]`（SW下位4bit）が押下中のSWビットのみ立つこと、`[1]`/`[2]`（VR_SRC1/VR_SRC2）がVR操作に追従して0..255で変化することを確認する。

   ```sh
   i2cget -y <bus番号> 0x30 0x00 b   # レジスタ0x00から連続読出しできない簡易ツールの場合は
                                       # i2ctransfer 等でwrite(0x00)+read(3)の1トランザクションにする
   ```

4. BACKLIGHT(`0x10`, write 12B)確認: 4灯分RGB（12バイト、途中で分割しない1トランザクション）を書き込み、4灯すべてが指定色・指定順（送出順どおり）で点灯することを目視確認する。12バイト未満/レジスタ先頭以外からの書込みでは反映されない（無視される）ことも確認する。

   ```sh
   i2cset -y <bus番号> 0x30 0x10 <R0> <G0> <B0> ... <R3> <G3> <B3> i
   ```

5. INFO(`0xF0`, read 4B)確認: `[0..1]`=firmware version（`I2C_REGS_FW_VERSION_MAJOR`/`MINOR`）、`[2]`=capabilities（現状0）、`[3]`=HW rev（現状0, プレースホルダ）と一致することを確認する。
6. SWのビット位置確認: `SW1`〜`SW4`（19pin `PD2` / 1pin `PD4` / 20pin `PD3` / 2pin `PD5`）を1つずつ押し、`STATE[0]` の b0〜b3 が順に立つことを確認する（`test_switches_io.c` がホスト上で検証している対応が実機でも成立していることの確認）。同様にVOL1/VOL2を片方ずつ回し、`[1]`/`[2]` が独立に変化することを確認する。
7. 複数モジュール（最大 `MODULE_BUS_COUNT`=5 台）を数珠つなぎにした状態で、1台を抜く/そのバスを切断しても他モジュールのSTATE読み出しが継続すること（マスター側 `i2c_modules.c` のタイムアウト/スキップにより1モジュール不通が他へ波及しないこと）を確認する。

## 参照

- 仕様: [`docs/specs/switcher-module-firmware.md`](../../docs/specs/switcher-module-firmware.md) §1, §3, §5, §6 / [`docs/specs/00-system-overview.md`](../../docs/specs/00-system-overview.md) §4.0, §4.5
- 既存パターン: [`../pico2w-controller/`](../pico2w-controller/)（`test/Makefile`, `include/config.h`, I/O分離）
