# module-simulator

実機のスイッチングモジュール(CH32V003, スロットごとの専用I2Cバス・アドレスは全台`0x30`)が
手元に無い状態で、
`pico2w-controller` の SW/VR 集約・HID 経路・バックライト配布をブラウザから操作して検証する
デバッグ用ツール。

> 親プロジェクト: [`../../README.md`](../../README.md) ／ ファームウェア: [`firmware/pico2w-controller/README.md`](../../firmware/pico2w-controller/README.md) ／ 共通プロトコル: [`docs/specs/00-system-overview.md`](../../docs/specs/00-system-overview.md) §4.1

## 何を検証できるか / できないか

`ENABLE_FAKE_MODULES=ON` でビルドしたファームは、I2C ポーリング(`i2c_modules.c`)を RAM 上の
偽モジュール層(`i2c_modules_fake.c`)へ差し替える。モジュールの状態は I2C バスの代わりに、
このツールが送るデバッグ用 HID 出力レポート `0x04` が供給する。

**検証できる**(実機 Pico を通しで動かす):

- `state_agg_pack` による入力レポート `0x01` のパッキング(`module_present` ビットマップ・SW・VR・`seq`)
- 変化時の即時送出と `HID_STATE_SEND_INTERVAL_MS` ごとの定期送出(`main.c`)
- VR デッドバンド判定(`VR_DEADBAND_DELTA`)による間引き
- `seq` のローテートと欠落検出
- ベンダー定義 HID 記述子一式と列挙
- バックライト配布経路: 出力レポート `0x02` → 受領キュー(`backlight.c`) → `backlight_task()` →
  `i2c_modules_write_backlight()`
- モジュール「不通」時の障害隔離(`present` を落としても他モジュールの送出が継続すること)

**検証できない**(実機モジュールが要る):

- I2C の物理層・リピーテッドスタート・`MODULE_I2C_TIMEOUT_US` のタイムアウト挙動・アドレス
  ストラップ。`i2c_modules.c` 自体がビルド対象から外れるため、このツールでは一切通らない。
- SK6812 の GRB 変換など、モジュール(CH32V003)側の処理。

## 使い方

### 1. デバッグ用ファームを焼く

```sh
cmake -B build-fake -DPICO_BOARD=<board> -DENABLE_FAKE_MODULES=ON
cmake --build build-fake
# build-fake/pico2w_controller.uf2 を BOOTSEL 中の Pico へコピー
```

`ENABLE_FAKE_MODULES` はデフォルト OFF。本番ビルドにはデバッグ用レポート `0x04`/`0x05` も
偽モジュール層も一切含まれない(HID 記述子にも現れない)。

### 2. シミュレータを起動する

```sh
dotnet run --project tools/module-simulator
# module-simulator: open http://127.0.0.1:5199
```

ブラウザで `http://127.0.0.1:5199` を開く。

### 3. 操作する

- **接続**: そのモジュールを「実在する」状態にする。チェックを外すと不通(`present=false`)を
  再現でき、他モジュールの送出が止まらないことを確認できる。
- **SW ボタン**: 押している間だけ ON のモーメンタリ動作。**Shift+クリック**でラッチ(押しっぱなし)に
  なるので、複数 SW の同時押しを作れる。
- **VR スライダー**: 0..255。デッドバンド未満の変化が間引かれる様子は readback 行で確認できる。
- **バックライト**: PC 側(`Switcher.App` など)が出力レポート `0x02` を送ると、Pico が配布した
  4灯分の RGB が色で表示される。
- **readback 行**: Pico から戻ってきた入力レポート `0x01` の内容。こちらが送った内容と食い違うと
  黄色くなる(VR はデッドバンド分の遅れを許容している)。
- **バックライトの「テスト送信」**: 本番の出力レポート `0x02` を `Switcher.App` の代わりに送る。
  アプリを立ち上げずに配布経路だけを叩ける。
- **「BOOTSEL で再起動」**: デバッグ用出力レポート `0x06` で Pico を書き込み待ち状態にする。
  このファームは USB CDC を持たず picotool でのリセットができないため、これが無いと焼き直しの
  たびに物理ボタンの押下が要る。

`GET /api/diag` でデバイスのレポート長と直近の生バイト列(入力レポート・feature `0x05`)が取れる。
レポートが届かないときの切り分け用。

## 設計メモ

- 入力レポートのパースは本番の `HidReportParser`(`src/Switcher.Hid`)をそのまま使う。
  バイトレイアウトを二重実装するとシミュレータだけが正しく見える事故が起きるため。
- バックライトの読み出しは**入力レポートではなく feature レポート `0x05`** にしてある。
  `HidSharpDevice.ReadInputReport()` は先頭の Report ID を無条件に剥がすので、入力レポートを
  2種類に増やすと `ENABLE_FAKE_MODULES` ビルドを本番アプリへ繋いだときに状態レポートとして
  誤解釈されてしまう。feature なら入力レポートは `0x01` の1種類のままで、本番側の挙動は変わらない。
- Windows の HID は出力/feature レポートのバッファ長がデバイスの最大レポート長ちょうどである
  ことを要求するため、短いレポート `0x04`/`0x06` はパディングして書き込んでいる
  (`PicoDebugDevice.cs`)。ファーム側もこれを見越して長さを完全一致で検証していない。
- バックライトの読み出し(feature `0x05`)の失敗は**入力ストリームを落とさない**。
  `ENABLE_FAKE_MODULES` 無しのファームには `0x05` が存在せず GET_FEATURE が正当に失敗するため、
  そこで再接続に入ると状態レポートを取りこぼして seq 欠落として現れてしまう。
- このプロジェクトは `HybridSwitcher.sln` に**含めていない**。製品のソリューションと CI を
  デバッグ専用ツールで汚さないため。ビルド/実行は上記の `dotnet run --project` で行う。
