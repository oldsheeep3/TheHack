# Switcher — 技術仕様（詳細）

最終更新: 2026-07-25 / 対象コミット: `develop`

本書は現時点で **実装済み** の内容を記述する。未実装の構想は「9. 未実装・ロードマップ」にのみ記載し、
本文には混ぜない。

---

## 1. 全体像

Switcher は Windows 上で動作する 2系統 M/E のライブ映像スイッチャーである。映像処理は libobs（OBS
Studio のコアライブラリ）に一本化し、その上に独自の制御レイヤー・UI・外部連携を載せている。

```
┌──────────────────────────────────────────────────────────────────────┐
│ Switcher.App (WPF / .NET 9)                                          │
│   オペレーターコンソール · MIX エディタ · マルチビュー設定             │
│   AppOrchestrator … PGM/PVW 状態の唯一の権威                          │
└───────┬───────────────┬───────────────┬───────────────┬──────────────┘
        │               │               │               │
   Switcher.Web    Switcher.Hid    Switcher.Atem   Switcher.Engine
   REST + WS       USB-HID         ATEM 制御       IVideoEngine
   (Kestrel)       (Pico 2W)       (UDP 9910)          │
                                                       │ P/Invoke
                                              ┌────────▼─────────┐
                                              │ switcher-engine  │
                                              │ (C++ / libobs)   │
                                              │  + NDI SDK       │
                                              └──────────────────┘
```

### 1.1 プロジェクト構成

| プロジェクト | 役割 |
|---|---|
| `Switcher.Contracts` | 全モジュール共有の DTO・列挙・`IVideoEngine` などのインターフェース。UI 非依存 |
| `Switcher.Engine` | `IVideoEngine` の実装。`LibObsVideoEngine`（P/Invoke）と `FakeVideoEngine`（テスト用） |
| `Switcher.App` | WPF アプリ本体。DI 合成ルート、UI、`AppOrchestrator` |
| `Switcher.Web` | 埋め込み Kestrel。REST API + コントローラー WebSocket + 各種バリデータ |
| `Switcher.Hid` | Pico 2W との USB-HID 入出力、スイッチのエッジ検出、バックライト算出 |
| `Switcher.Atem` | ATEM Mini への UDP 制御（ボタン→コマンド写像） |
| `native/switcher-engine` | C++。libobs と NDI SDK を直接叩くネイティブエンジン |

---

## 2. 映像エンジン（native/switcher-engine）

### 2.1 構造

- **共有ソースプール** `std::map<std::string, obs_source_t*>`。入力は **1回だけ** 開かれ、両バス・
  マルチビュー・MIX から同一インスタンスを参照する。これが「同じカメラを PGM1 と PGM2 と MIX に同時に
  出せる」ことを可能にしている（デバイスの二重オープンは不可能なので、単一プロセス構成が前提）。
- **バス × 2**。各バスは `{ program_scene, preview_scene, transition(fade), program_view/video,
  preview_view/video }` を持つ。トランジションが「現在の program シーン」を描画し、TAKE は
  program/preview の役割を入れ替える。他バスには一切波及しない。
- **マルチビュー**は専用の private シーン + view + video。
- **出力**は bus の `program_video` に `obs_output` をバインドする。

### 2.2 起動シーケンス（`engine_startup`）

1. 任意でログハンドラを設定（`SWITCHER_ENGINE_LOG=<file>`）
2. `obs_startup` → **`obs_add_data_path` は `obs_reset_video` より前に必須**（libobs 内蔵エフェクトの
   解決に使われるため。ここを外すと graphics 初期化が失敗する）
3. `obs_reset_video`（BGRA / VIDEO_CS_709 / FULL range / 既定 1920x1080@60）
4. `obs_reset_audio`（48kHz stereo）。トラック **0 = PGM1 / 1 = PGM2**（2.11 参照）
5. **プラグインを名前指定で個別ロード**。`obs_load_all_modules()` は使わない ── OBS の plugins には
   Qt 依存のフロントエンドプラグイン（frontend-tools, obs-websocket, aja-output-ui 等）が同居しており、
   QApplication のないホストでロードすると `Must construct a QApplication before a QWidget` で
   プロセスごと落ちるため。許可リストは `win-dshow / win-capture / win-wasapi / obs-ffmpeg /
   image-source / obs-text / text-freetype2 / obs-transitions / obs-filters / vlc-video / obs-x264 /
   obs-outputs / rtmp-services / obs-browser / distroav / obs-ndi`
   - システム配置（`<obs>/obs-plugins/64bit`）→ 見つからなければユーザー配置
     （`%APPDATA%/obs-studio/plugins/<name>/bin/64bit`）の順に試行
6. `obs_post_load_modules()`
7. **NDI 登録**（`ndi_register_source()`。2.6 参照）
8. マルチビュー用シーン／ビュー、バス×2 の生成

### 2.3 ソース種別（`map_source_type`）

| 契約種別 | libobs ソース | 備考 |
|---|---|---|
| `WEBCAM` / `UVC` | `dshow_input` | 2.4 参照 |
| `SRT` / `MEDIA` | `ffmpeg_source` | `srt://` URL 可 |
| `NDI` | `switcher_ndi`（自前） | DistroAV があればそちらにフォールバック |
| `IMAGE` | `image_source` | |
| `HTML` | `browser_source` | obs-browser（CEF）。2.5 参照 |
| `MIX` | （private scene） | 2.7 参照 |

### 2.4 Webcam のキャプチャモード

win-dshow は `res_type` が **Custom(1)** のときだけ `resolution` / `frame_interval` を尊重する。既定の
Preferred(0) ではデバイスが最初に申告するメディアタイプで開かれ、多くの UVC カメラでこれは極端に低い
フレームレートになる（実測: BUFFALO BSWHD06M が **1280x720@8fps**）。

`WebcamConfig.Format` は `"<W>x<H>"` または `"<W>x<H>@<FPS>"` を受け取り、ネイティブ側で
`res_type=1` / `resolution` / `frame_interval`（100ns 単位、0 = FPS_HIGHEST）に変換する。未指定なら
明示的に Preferred を設定する。

### 2.5 HTML ソース

obs-browser を使用。Windows ビルドでは `ENABLE_BROWSER_QT_LOOP` が **定義されない**（macOS 限定の
ビルドフラグ）ため、`obs_module_load` はソース型の登録と `obs_frontend_add_event_callback`（フロント
エンド不在では no-op）を行うだけで、CEF はプラグイン自身のマネージャースレッドで遅延起動する。
QApplication は不要。検証環境: OBS 32.0.4 / CEF 127。

`shutdown=false` を設定してオフエア中もページを動かし続ける（プレビュータイルが黒くならないため）。

### 2.6 NDI（DistroAV 非依存）

`src/ndi.cpp`。**NDI SDK を直接利用**し、OBS 用 NDI プラグイン（DistroAV）を必要としない。

- **必要なのはヘッダのみで、それもリポジトリに同梱している**（`third_party/ndi/include`）。SDK の
  `Include` 配下の各ヘッダは「このファイルに限り MIT」と明記されており、MIT は GPL 互換なので GPL の
  成果物と一緒に配布できる。おかげで **NDI SDK を入れずにソースからビルドできる**（GPL の「受け取った
  ソースで再構築できること」を満たすため）。別の SDK を使うなら `-DNDI_INCLUDE_DIR=<path>`。
- **ランタイムはユーザーが入れる。** `Processing.NDI.DynamicLoad.h` の `NDIlib_v5_load` を
  `LoadLibrary` で解決する（NDI Tools / NDI Runtime が設定する `NDI_RUNTIME_DIR_V6/V5/V4` から探索）。
  NDI のない PC でも同じバイナリが動き、その場合は全エントリポイントが「利用不可」を返す。ヘッダが
  見つからないビルドでは `SWITCHER_HAS_NDI` が定義されず、`ndi.cpp` 全体がスタブになる。
- **入力**: `obs_register_source("switcher_ndi")`。非同期ビデオソース。ソース1つにつき受信スレッド1本で
  `recv_capture_v2` → `obs_source_output_video`。受信フォーマットは
  `NDIlib_recv_color_format_BGRX_BGRA` を要求するため色変換コードが不要。送信側が不在／後から出現した
  場合はリトライし続ける。
- **出力**: `obs_register_output("switcher_ndi_output")`。`obs_output_begin_data_capture` +
  `raw_video` で obs からフレームを受け取り、`send_send_video_v2`（同期版）で送出。obs のフレーム
  バッファはコールバック中しか有効でないため、非同期版ではなく同期版を使う。
  `obs_output_set_video_conversion` で BGRA を要求している。
- **出力の音声**: `OBS_OUTPUT_VIDEO | OBS_OUTPUT_AUDIO` で登録し、`raw_audio` から
  `send_send_audio_v3` へ送る。`obs_output_set_audio_conversion` で **stereo / planar float** を要求し、
  どのトラックを受け取るかは engine 側の `obs_output_set_mixer(output, bus)` が決める（2.11）。
  NDI の FLTp は「1つの確保域 + チャンネルストライド」を要求するのに対し obs はプレーンごとに別ポインタ
  を渡すため、コールバック内でスクラッチバッファへ詰め直してから送出する。
- **探索**: `engine_enumerate_devices("NDI")` は obs のプロパティ一覧ではなく SDK の finder を使う。
  ソースの `id` は NDI 名そのもの（`MACHINE (Source)`）で、これが接続キーになる。

### 2.7 MIX ソース

`engine_add_source(type="MIX")` は private な `obs_scene` を生成し、共有ソースプールに **mix の id で
登録**する。シーン自体が `obs_source` であるため、mix は他の特別扱いを一切必要とせず、バスへの搭載・
プレビュータップ・マルチビューセル・モジュール割当のすべてでカメラと同じように扱える。

レイヤーは同じプールから解決されるので、あるソースが mix の内部と単体のバスに同時に存在できる。
自己参照は明示的にスキップし、より深い循環は libobs 側が拒否する（`obs_scene_add` が null を返す）。

### 2.8 プレビュー読み出し（タップ）

`engine_set_tap(target, true)` で対象のピクセルをマネージド側のフレームコールバックへ流す。

`obs_view` + `video_output_connect` は **使わない**。カスタム view のミックスは raw-active として
マークされず（それを行う `start_raw_video` は非公開）、そのままでは1フレームも届かないため。代わりに
単一の `obs_add_main_render_callback` が、各タップの対象ソースを `gs_texrender` にオフスクリーン描画 →
`gs_stagesurface` にステージ → **1フレーム後**にマップして BGRA をコールバックへ渡す（GPU ストールの
回避）。`ctx->taps` の変更は必ず `obs_enter_graphics()` 下で行い、グラフィックススレッドを停止させる
ことで追加のロックを不要にしている。

非同期キャプチャソース（dshow / NDI）は "showing" でないと取り込みを行わないため、`SRC:` タップは
`obs_source_inc_showing` を呼ぶ（破棄時に対で `dec_showing`）。

### 2.9 TAKE とシーン入れ替え

`engine_take` はバスの `program_scene` / `preview_scene` ポインタを入れ替える。**一度だけ解決した
`obs_source_t*` を保持しているものはすべて追随させる必要がある**：

- `rebind_bus_targets_locked` が当該バスの `PVW*` タップと `PVW*`/`PGM*` の `obs_display` を張り直す
- `ctx->last_multiview_json` にキャッシュしたレイアウトを再適用し、`PVW1`/`PVW2` を割り当てた
  マルチビュー領域を更新する

これを行わないと、TAKE 直後の PVW 読み出しが「たった今 PGM に昇格したシーン」を映す。

### 2.10 出力

sink は固定の5系統ではなく、オペレーターが編集する**可変テーブル**。種別（Webcam / HDMI / NDI）＋序数で
`VCAM1` / `HDMI1`〜`HDMI3` / `NDI1`〜`NDI3` を表し、上限は **Webcam 1・HDMI 3・NDI 3・合計6**
（`OutputCatalog.MaxWebcamSinks` / `MaxSinksPerKind` / `MaxTotal`、判定は `MaxOf(kind)`）。初期状態は
`VCAM1`＋`HDMI1` の2出力で、そこから追加する。
序数なしの旧トークン（`"VCAM"` / `"HDMI"` / `"NDI"`）は各種別の1番目として読むため、既存の
`runtime-config.json` はそのまま動く。`VCAM2` / `VCAM3` もトークンとしては読める（旧ビルドの設定が
壊れずに読み込めるようにするため）が、オペレーターが追加することはできない。

| Sink | 実体 |
|---|---|
| `VCAM1` | `virtualcam_output`（OBS の仮想カメラは1つだけ。だから Webcam は1本まで） |
| `VCAM2` / `VCAM3`（旧ビルド互換） | NDI 送出 `SWITCHER VCAM2` / `SWITCHER VCAM3`。新規に追加はできない |
| `NDI1`〜`NDI3` | NDI 送出（既定の送出名は `SWITCHER PGM1`〜`SWITCHER PGM3`） |
| `HDMI1`〜`HDMI3` | `engine_start_display(target, hwnd, display_id)`。HWND はアプリ側が所有 |

表示の対象トークンは `PGM1`/`PGM2` ではなく **sink トークンそのもの**（`HDMI1`…）。同じバスに割り当てた
2つの HDMI sink が、それぞれ独立した全画面ウィンドウを持てるようにするため。

各出力は `obs_output_set_media(output, program_video, obs_get_audio())` に加えて
`obs_output_set_mixer(output, bus)` を受ける。これがないと PGM2 の絵を運ぶ出力が PGM1 の音を運ぶ。

**ルーティングの制約**: PGM1 / PGM2 は **それぞれ最低1つの出力**を持たなければならない
（`OutputRules.MissingBuses`。Web バリデータと `AppOrchestrator.ApplyOutputsAsync` の両方で強制）。
2系統 M/E は2系統を同時に出すためのものなので、どこにも出ていない PGM は設定ではなく結線ミスとして扱う。
既定が `VCAM1` 1本ではなく `VCAM1`＋`HDMI1` なのも、この規則を満たす最小構成だから。

**割当と実際に出ているかは別**: 個々の sink の失敗は全体を止めない（他の sink を巻き添えにしないため）
ので、割当表を見ても「NDI ランタイムが無くて NDI1 が起動しなかった」ことは分からない。
`engine_get_output_status` が sink ごとの `running` を返し、`OutputRules.BusesWithoutRunningOutput` が
「割当はあるのに1つも動いていないバス」を判定する。オペレーター画面は Apply 時に警告を出し、起動時の
復元ではログに残す。HDMI は全画面ウィンドウが開いて初めて running になる。

### 2.11 音声

映像ソースは音声ソースでもあるため、入力の追加・削除は映像側と共通のまま扱う（別に音声入力を作らない）。

- **バス n の音声 = libobs のトラック n**。`obs_source_set_audio_mixers(src, mask)` のビットが、その
  ソースがどのバスで聞こえるかを表す。ソース作成直後は `0`（無音）で開始する。
- モード（`SourceAudioMode`）は **AFV / ON / OFF**。マスクの算出はマネージド側（3.2）が持つ。libobs に
  AFV の概念はない。
- **出力デバイス**: `src/audio_out.cpp`。libobs のモニタリングはプロセスで1デバイスしか持てないため、
  自前の WASAPI レンダラを持つ。`obs_add_raw_audio_callback(track, …)` で受けたバスの PCM をリングに
  積み、共有モードのイベント駆動レンダースレッドがエンドポイントへ書く。
  - **1つのバスを複数デバイスへ**出せる（各デバイスが独立したシンク＝独立したコールバック登録）。
  - リングは **常にステレオ**で持つ。デバイスのチャンネル数で読むと、8ch ヘッドセットに 2ch を流した
    ときにバッファを踏み越えてプロセスごと落ちる（実際に起きた）。デバイスのレイアウトへの
    アップミックスは書き出し時に行い、余ったチャンネルは 0 で埋める。
  - 供給が間に合わないときは**無音**を書く（引き伸ばしや再生はしない）。あふれたときは**最古**を捨てる
    （復帰後に遅れた音を再生しないため）。
  - `engine_apply_audio_outputs` は **`ctx->lock` を取らない**。デバイスのオープンはエンジンの状態に
    触れないうえ、グラフィックス／オーディオのコールバックを待たせる理由がない。

---

## 3. マネージド層

### 3.1 `IVideoEngine`

映像に関する唯一の境界。ライフサイクル（`StartAsync`/`StopAsync`）、ソース管理、2系統 M/E、
プレビュータップ、マルチビュー、出力を1つの面にまとめている。実装は `LibObsVideoEngine`（P/Invoke）と
`FakeVideoEngine`（ネイティブ非依存。ヘッドレステストと非 Windows 環境用）。

イベント: `SourceStatusChanged` / `SourceRemoved`。

### 3.2 `AppOrchestrator`

**PGM/PVW に影響するすべての変更が通る単一の関門**。HID・Web・UI の3経路が競合してタリーや
バックライトが不整合になることを防ぐため、全変更を `_stateLock` で直列化する。

- 影のバス状態（`_programSourceIds` / `_previewSourceIds`）を自前で保持する。libobs には「バス X に
  どのソース id が載っているか」を問い合わせる API がないため。
- `TakeAsync(bus, durationMs)` はネイティブのシーン入れ替えを影の状態にも反映する
  （program ↔ preview を交換）。`durationMs` 0 = CUT、>0 = AUTO フェード。
- `HandleModulePresence(mask)` は Pico の `module_present` ビットマップとモジュール一覧を突き合わせる。
- ソース定義（`SourceDefinition`）を id ごとに保持し、再接続・MIX 編集・永続化に使う。

#### MIX のタリー伝播

`ExpandMixMembersLocked` は、バスに載っているソース id 集合を **MIX の中身まで展開** する。合成ソースが
オンエアなら、その中の全ソースがオンエアである ── タリー（UI・UDP・Pico の LED）も AFV も同じ集合を見る。

#### 音声ポリシー（AFV）

`RefreshSourceAudioLocked` が全ソースのミキサーマスクを算出し `SetSourceAudioMixers` で反映する。

| モード | マスク |
|---|---|
| `ON` | PGM1 \| PGM2（絵が出ていなくても聞こえる。BGM や司会マイク） |
| `AFV` | そのソースが載っているバスのビットのみ（MIX 経由を含む） |
| `OFF` | 0 |

呼び出し点は「マスクが変わりうる瞬間」すべて ── バス状態の再計算（`RecomputeAndPublishV2Locked`）、
ソースの追加、ソースの更新（モード変更）、起動時の復元。ここを1つ落とすと、追加直後の `ON` が無音のまま、
あるいは `AFV` が最初のバス操作まで鳴りっぱなしになる。

#### タリー色

`ApplyTallyColorsAsync` で PGM/PVW × バス1/2 の4色を設定・永続化する。UI は `DynamicResource` 経由で、
Pico のモジュール LED は `ConfiguredBacklightPolicy` 経由で **同じ色** を使う。

### 3.3 ロック順序（デッドロック回避）

`LibObsVideoEngine` はネイティブの `ctx->lock` と競合しうるマネージドロックを**フレーム経路に置かない**。

ネイティブ側は `ctx->lock` を保持したまま `obs_enter_graphics()`（＝グラフィックススレッドの停止）を
呼ぶ（`engine_set_tap` / `rebind_bus_targets_locked`）。一方フレームコールバックは**グラフィックス
スレッド上**で `MarkSourceLive` を通る。ここで両者が同じロックを触ると次の循環待ちが成立する:

```
T1: デバイス列挙   … マネージドロック保持 → ctx->lock 待ち（NDI finder で最大1秒）
T2: TAKE/ソース追加 … ctx->lock 保持     → グラフィックス停止待ち
T3: グラフィックス  … 停止できない        → マネージドロック待ち（T1 が保持）
```

対策は2つ:

- ソース登録簿は `ConcurrentDictionary`。グラフィックススレッドはロックを一切取らない。
- ポインタを返すネイティブ照会（`engine_enumerate_devices` / `_audio_devices` / `_get_output_status`。
  返り値はエンジン所有のバッファで次回呼び出しで上書きされる）は**専用の `_nativeQueryGate`** で直列化し、
  コピーだけをロック内で行う。このロックはグラフィックススレッドが触らないので循環が閉じない。

### 3.4 ログと未捕捉例外

本アプリの主要なエラー戦略は「警告を出して続行」なので、読めるログが無いと設計が意味を失う。

- `FileLoggerProvider` が `%LOCALAPPDATA%\Switcher\logs\switcher-<date>.log` に出力（14日で自動削除）。
  書き込みは専用スレッドで直列化し、**呼び出し元（グラフィックススレッド・HID 読み取りスレッド）を
  ディスクで待たせない**。キューが溢れたら行を捨てる。ログ自体は決して例外を投げない。
- `App.OnStartup` で `DispatcherUnhandledException`（記録して続行 — 本番中に消えるのが最悪）、
  `AppDomain.UnhandledException` と `TaskScheduler.UnobservedTaskException`（終了は防げないので記録のみ）
  を登録する。
- `HidInputService` の読み取りループは各ハンドラ呼び出しを捕捉し、`HandlerFailed` で通知する。
  バックグラウンドスレッドから例外が抜けるとプロセスが終了するため。

### 3.5 フレームポンプ

`FramePumpService` が 30fps でバス／ソースのフレームを読み、トークン（`PGM1`/`PVW2`/`SRC:<id>` …）
ごとにキャッシュして `Tick` を発火する。UI プレビュー専用（出力はエンジン側が持つ）。

**UI スレッドのコストが支配的**であることに注意。`FrameBitmapWriter` はフレームバッファを pin して
`WriteableBitmap.WritePixels(IntPtr)` に渡す（`ToArray()` は 1080p で 1回 8MB のコピーになる）。
さらに `PreviewBitmapCache` がトークンごとに **1 tick 1回だけ** 変換し、開いている全ウィンドウで
共有する。`Tick` の購読はオペレーターウィンドウ1つだけで、そこから他ウィンドウへ配る。

### 3.6 永続化

`%LOCALAPPDATA%\Switcher\runtime-config.json` に保存され、起動時に復元される。配布物のディレクトリ
（`C:\Program Files\...`）は標準ユーザーが書けないため、実行ファイルの隣には**置かない**。旧バージョンが
実行ファイル隣に書いたファイルは初回起動時に自動で移行する（`RuntimeConfigMigration`）。

書き込みは **temp → `File.Move(overwrite)`** で、直前の版を `.bak` に残す。インプレース書き込みだと
書き込み中のクラッシュ／電源断で切り詰められたファイルが残り、次回起動が無言でデフォルトに戻る
（＝ソース・レイアウト・出力・音声ルーティングの全喪失）。読み出しは本体が壊れていれば `.bak` を試す。

保存は **例外を投げない**。永続化の失敗で落ちてはならないのに加え、保存はその変更を起こしたスレッド
（HID 読み取りスレッドを含む）で走るため、そこから例外が抜けるとプロセスごと終了する。

- `sources`（`SourceDefinition` 一式）
- `multiview_grid` / `multiview_regions`（+ 後方互換の `multiview_cells`）
- `output_assignments`
- `audio_outputs`（バス→デバイス）
- `tally_colors`
- `module_mappings`
- `atem_config` / `pico_network`

復元は `AppHostService.StartAsync` 内、エンジン起動直後・`MainWindow` 構築前に行う。**通常ソースを
すべて生成してから MIX を生成**する（ネイティブ側は既存のメンバーしか結線できないため）。

バスは意図的に空で起動する（起動しただけで映像が本線に乗るのを避けるため）。

出力ルーティングの復元だけは例外的に**修復**する: どちらかの PGM に出力がない設定（このルールが入る前に
書かれたもの）は既定のルーティングへ戻して保存し直す。片方のバスが死んだまま立ち上がるよりよい。

---

## 4. オペレーターコンソール（UI）

デザイン方針は「Studio Dock」。OBS Studio の形を借り、既存ユーザーの学習コストを下げる。

```
メニュー
ツールバー（+ Add source / New mix / Multiview settings / full-screen / projector / タリー / ATEM）
┌────────────────────────────────┬──────────────┐
│ PVW ┃ PGM （選択中 M/E）        │ マルチビュー  │
│ トランジションバー               │ （表示専用）  │
├────────┬────────┬───────┬──────┴──────────────┤
│ Sources│ Buses  │Outputs│ Controller           │
└────────┴────────┴───────┴──────────────────────┘
```

- **ダーク固定**。オペレーターは本線モニターの横に座るため、明るい UI は映像より眩しくなる。
- **赤=PGM / 緑=PVW はタリーの意味だけに予約**。操作可能な要素のアクセントは青。
- **キーボード**: `1`–`9` ステージ / `Space` CUT / `A` AUTO / `Esc` プレビュークリア /
  `Tab` M/E 切替。テキスト入力にフォーカスがあるときは無効。
- **マルチビュー**はセルの内容がオンエア中なら赤枠、ステージ中なら緑枠（`SRC:` セルも含む）。編集は
  別ウィンドウ（`MultiviewSettingsWindow`）。グリッドは **4×4〜6×6** で行・列を独立に選べる。
- **MIX エディタ**は実寸キャンバス上でドラッグ移動・角でリサイズ、レイヤーはライブサムネイル。
- **音声**は Sources ドックの各行に AFV/ON/OFF、Outputs ドックにバス→デバイスの割当（Rescan で
  再列挙）。タリー色は Outputs のバス名にもそのまま出る。

---

## 5. コントローラー（Pico 2W / USB-HID）

- 入力レポート `0x01`: `module_present`(1) + SW×MAX_MODULES + VR×2×MAX_MODULES + `seq`(1)
- 出力レポート `0x02`: `module_index`(1) + 4×RGB
- `module_present` のビット n がモジュール n の接続を表す。**アプリのモジュール一覧はこれに追随**し、
  接続されていないモジュールの行は表示しない。接続され続けているモジュールの割当は維持される。
- スイッチのエッジはソースを **PGM バスへ直接** 載せ替える（仕様 §3）。タリーとバックライトもこれに
  従う。
- バックライト色は現在の PGM/PVW 状態から算出して出力レポートで返す。HID の I/O 失敗は握りつぶす
  （1つの障害が全体を止めない）。

---

## 6. Web API

`http://<host>:8080`（既定）。すべて JSON、snake_case、列挙は文字列。

| メソッド | パス | 用途 |
|---|---|---|
| GET/POST | `/api/v1/sources` | 一覧 / 追加 |
| PUT/DELETE | `/api/v1/sources/{id}` | 更新 / 削除 |
| POST | `/api/v1/program` | バスのレイヤー適用（`take` で PVW→PGM） |
| PUT | `/api/v1/multiview` | レイアウト適用 |
| PUT | `/api/v1/outputs` | 出力ルーティング（PGM ごとに最低1つ必須） |
| GET | `/api/v1/audio/devices` | 音声出力デバイス列挙 |
| PUT | `/api/v1/audio/outputs` | バス→デバイスの音声ルーティング |
| PUT | `/api/v1/modules` | モジュール割当 |
| PUT/POST | `/api/v1/atem`, `/api/v1/atem/command` | ATEM 設定 / 直接コマンド |
| PUT | `/api/v1/pico/network` | Pico のネットワーク設定（保存のみ） |
| GET | `/api/v1/devices/{type}` | デバイス列挙（`webcam` / `ndi`） |
| GET | `/api/v1/srt/setup` | SRT 接続情報の提示 |
| WS | `/ws` | 旧コントローラー用フォールバック |

入力はすべてバリデータを通す（`SourceDefinitionValidator` / `MultiviewLayoutValidator` /
`ProgramRequestValidator` / `OutputsRequestValidator` / `AudioOutputsRequestValidator` /
`ModulesRequestValidator`）。

タリーは `255.255.255.255:9999` へ UDP ブロードキャストされる（2系統ペイロード）。

---

## 7. ビルド

### 7.1 マネージド

```
dotnet build HybridSwitcher.sln
dotnet test  HybridSwitcher.sln
```

### 7.2 ネイティブ

libobs の import lib とヘッダが必要。`find_package(libobs)` が使えない場合は明示パス:

```
cmake -S native/switcher-engine -B native/switcher-engine/build -G "Visual Studio 17 2022" -A x64 \
  -DSWITCHER_ENGINE_USE_FIND_PACKAGE=OFF \
  "-DLIBOBS_INCLUDE_DIR=<repo>/obs-studio/libobs;<repo>/obs-studio/build_x64/config" \
  "-DLIBOBS_LIB=<repo>/obs-studio/build_x64/libobs/Release/obs.lib"
cmake --build native/switcher-engine/build --config Release
```

NDI は同梱ヘッダで自動的に `SWITCHER_HAS_NDI` が有効になる（SDK のインストールは不要）。ビルド成果物は
`Switcher.App` のビルド後ターゲットが自動でアプリ出力へコピーする。

### 7.3 実行時の前提

- Windows 10/11 x64
- **libobs ランタイム**。次の順で解決する（`Configuration/ObsRuntime`）:
  1. アプリに **同梱** された `obs-runtime/`（`tools/bundle-obs-runtime.ps1` が作る）── これがあれば
     OBS Studio のインストールは不要
  2. インストール済みの OBS Studio（レジストリ / 既定のパス）
  検証済みバージョンは **30.0〜32.99**。範囲外なら起動時に警告を出して続行する（ABI が合わなければ
  そこで落ちるが、勝手に止めはしない）。
- 任意: **NDI Tools / NDI Runtime**（NDI 入出力を使う場合。無ければ NDI 機能だけが無効）

---

## 8. テスト

333 件（`dotnet test`）。

| 対象 | 内容 |
|---|---|
| Contracts | DTO・JSON 往復・列挙表現 |
| Engine | `FakeVideoEngine` の観測可能な状態 |
| Hid | レポート符号化、エッジ検出、VR デッドバンド、`seq` ギャップ、バックライト算出、`module_present` |
| Atem | コマンド写像・接続状態 |
| Web | 全バリデータ（境界値・不正トークン・被覆漏れ・重複・範囲外グリッド・MIX 自己参照 等）、エンドポイント |
| App | オーケストレーターの TAKE/削除/モジュール突合、マルチビュー領域モデルの結合ガードとリサイズ、タリー判定と MIX 伝播、AFV ポリシーと音声ルーティング、永続化と復元順序 |

ネイティブ層（libobs/NDI）は実機検証で確認する（Windows + OBS が必要なため CI では動かせない）。

---

## 9. 未実装・ロードマップ

- **録画 / RTMP 配信**: 未対応（`obs-outputs` / `rtmp-services` はロード済み）。
- **音声のミキサー UI**: モードとデバイス割当のみ。レベル・ゲイン・遅延補正・メーターは未対応。
- **マルチビュー合成出力のタリー枠**: オペレーター画面のマルチビューには赤/緑枠が付くが、
  合成された MULTIVIEW 出力（全画面/HDMI）には未実装。バス変更のたびにシーンを作り直す実装は
  グラフィックススレッドを停止させたため撤回済み。領域ごとに色ソースを保持して設定だけ更新する
  設計であれば実現可能（`apply_multiview_locked` のコメント参照）。
- **PiP の不透明度**: `PipSettings.Opacity` は未適用（共有ソースにフィルタを掛けると両バスに波及する
  ため、バス単位のラッパーが必要）。位置・サイズ・クロップ・Z順・表示は反映される。
- **ATEM 連携の拡張**、**ハードウェアパネルの拡充**。
