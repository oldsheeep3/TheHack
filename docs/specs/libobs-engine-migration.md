# 仕様書: 映像エンジンの libobs 移行（GStreamer/DirectX → libobs リファクタ）

> 親仕様書: [`pc-switcher-app.md`](./pc-switcher-app.md)（§2 機能要件 / §3 技術スタック）, [`00-system-overview.md`](./00-system-overview.md) §4。
> 本仕様書は **PC常駐アプリの映像エンジンを GStreamer + DirectX 自作合成から libobs（OBS のコアライブラリ）へ全面移行**するリファクタを定義する。
> **「その他の機能要件は既存仕様のまま維持」** する（2系統ME・マルチビュー・出力割当・HID/バックライト・タリー・ATEM遠隔制御・WebAPI）。本書は主に **①実装基盤の置換方針、②削除対象、③新設コンポーネント、④非機能/契約への影響**を規定し、機能要件そのものは親仕様書を継承する。
> 開発は本移行以降 **`develop` ブランチ（`main` 基点で新設）** を基点に行う。

## 1. 背景・目的

- 現行の映像パスは **GStreamer（GstSharp）でデコード + DirectX11（Vortice）で自作合成**（`pc-switcher-app.md` §3）。デコーダ・合成・仮想カメラ・HDMI全画面・NDI 入出力・デバイス列挙を自作で抱えており、保守コストと不具合（NDIリーク、HDMI present例外、GStreamerランタイム解決 等の epic での多数修正）が大きい。
- **OBS のコアである libobs を映像エンジンとして採用**し、ソース入力（OBS標準搭載ソース）・GPU合成・仮想カメラ/NDI/表示出力を libobs に一本化する。デュアルM/E（PGM1/PVW1・PGM2/PVW2）は **`obs_view` を2系統**作り、**物理入力ソースを1回だけ開いて両系統から共有参照**することで実現する（単一プロセスでのみ可能な構成）。
- 目的:
  1. 映像入力・合成・出力の実装を libobs に委譲し、自作コード（GStreamer/DirectX/自作仮想カメラ）を**削除**して保守面積を縮小する。
  2. 「ソースは OBS 標準搭載のもののみ」を満たしつつ、デュアルM/E を成立させる。
  3. 既存の .NET/WPF アプリ資産（Contracts / Web / Hid / App(UI) / Atem）と外部契約（WebAPI・タリー・HID）を**壊さず流用**する。

## 2. 移行の全体方針（確定事項）

以下は壁打ちで確定した方針である。

| 項目 | 決定 |
| --- | --- |
| **開発ブランチ** | `main` 基点で `develop` を新設。以降の開発は `develop` を基点とする（epic ブランチは温存）。 |
| **アプリ構成** | **C#/.NET 9 + WPF を維持**。libobs を扱う**薄いネイティブ C/C++ エンジンDLL** を新設し、**P/Invoke** で呼ぶ。GPLv2 の影響はネイティブ層に閉じる。 |
| **削除範囲** | **積極削除**。`Switcher.Media`（GStreamer/DirectX 合成・デバイス列挙）と `Switcher.VirtualCam`（自作 DirectShow 仮想カメラ・自作 HDMI 全画面）を**廃止**し、責務を libobs へ移す。`Switcher.Atem` / `Switcher.Hid` / `Switcher.Web` / `Switcher.Contracts` / `Switcher.App` は**維持**。 |
| **libobs 入手** | **インストール済み OBS Studio に含まれる libobs（ヘッダ + import lib）にリンク**。ビルドに必要な OBS バージョンを README/ビルド手順に明記し固定する。obs-studio のソースビルドや submodule 化は行わない。 |
| **ソース** | **OBS 標準搭載（バンドルモジュール）ソースのみ**を用いる（§2.4）。 |
| **Pico 2 仕様** | 一部変更が入る可能性はあるが、**共通プロトコルの規約（HID/WebSocket/タリー/API の契約）は変更しない**（親 §4 準拠）。 |

## 2.1 新設コンポーネント（映像エンジン）

- **ネイティブエンジン `switcher-engine`（C/C++, GPLv2）**: libobs をリンクし、C ABI を公開する薄い層。libobs の参照カウント・グラフィックススレッド制約（`obs_enter_graphics`/`obs_leave_graphics`）・モジュール読込を**この層に完全に閉じ込める**。
  - 起動: `obs_startup` → `obs_reset_video/obs_reset_audio` → OBS バンドルモジュール読込（`obs_add_module_path`/`obs_load_all_modules`/`obs_post_load_modules`）。
  - デュアルM/E: バスごとに `transition` ソース + `obs_view`（`obs_view_set_source`）を保持し、`obs_view_add` で得た `video_t` に各出力を紐付ける。
  - C ABI（叩き台。詳細は実装タスクで確定）:
    ```c
    engine_startup(cfg) / engine_shutdown()
    engine_add_source(id, type, settings_json) / engine_remove_source(id)
    engine_set_preview(bus, shot_id)              // PVW 選択
    engine_cut(bus)                               // 即切替（obs_transition_set）
    engine_auto(bus, duration_ms)                 // トランジション（obs_transition_start）
    engine_set_pip(bus, layer_json)               // PiP レイヤ（座標/拡縮/Z/不透明度/クロップ）
    engine_start_output(bus, sink, cfg_json)      // VCAM/NDI/HDMI/表示
    engine_stop_output(bus, sink)
    engine_render_multiview(target, layout_json)  // 4x4 マルチビュー描画
    engine_set_state_callback(cb)                 // PGM/PVW/接続状態の通知（タリー算出用）
    ```
- **マネージド相互運用 `Switcher.Engine`（C#）**: 上記 C ABI の P/Invoke ラッパ。`Switcher.Contracts` に定義する **`IVideoEngine` 抽象**を実装する。
- **契約層 `Switcher.Contracts` に `IVideoEngine`（新規）** を追加し、`Switcher.App` / `Switcher.Web` は**抽象にのみ依存**する。テストは**フェイク実装を注入**し、libobs 非搭載のヘッドレスCIでもビルド/テストが通る状態を維持する（親 §4 の耐障害・テスト方針を踏襲）。

## 2.2 デュアルM/E とソース共有（親 §2.2 を libobs で実現）

- [ ] 物理入力（WebCam 等）は **`obs_source_t` として1回だけ生成**し、ME1/ME2 の両系統から参照する（デバイスの二重オープンを回避）。
- [ ] **PGM1/PVW1 = ME1**, **PGM2/PVW2 = ME2** を、各バスの `transition` + `obs_view` で独立保持する。`TAKE`(CUT/AUTO) は当該バスの transition のみを操作し、他バスへ波及しない。
- [ ] PiP 合成（座標・サイズ・Zオーダー・不透明度・クロップ）は libobs のシーン/シーンアイテム変換で表現する。1系統の障害が他系統・合成を止めない（親 §4）。

## 2.3 出力（親 §2.4 を libobs で実現）

- [ ] **仮想カメラ出力**（既定 PGM1→VCAM1）: libobs の仮想カメラ出力を用いる。**OBS 標準の仮想カメラ出力は1系統**のため、**Webcam sink は `VCAM1` の1本まで**とする。`VCAM2`/`VCAM3` は旧ビルドが書いた設定を読み込むための互換トークンとしてのみ残り、NDI 送出（`SWITCHER VCAM2` / `SWITCHER VCAM3`）で処理される。
- [ ] **HDMI/物理ディスプレイ全画面出力**: 当該バスの `obs_view` テクスチャを `obs_display` 経由でフルスクリーンウィンドウに提示（カーソル非表示・`Esc` 解除）。従来 `Switcher.VirtualCam` の D3D スワップチェーン/HDMI全画面の**役割は本エンジン+App側の obs_display 提示に置換**する。提示先は `PGM1`/`PGM2` ではなく **sink トークン（`HDMI1`〜`HDMI3`）**で指定し、同一バスに割り当てた2つの HDMI sink がそれぞれ独立した全画面ウィンドウを持てるようにする。
- [ ] **NDI 出力（`NDI1`〜`NDI3`, ソースは PGM1/PGM2）**: NDI は OBS 標準搭載ではないため **DistroAV(obs-ndi) プラグイン**のモジュール読込を前提とし、未導入時はダウンロード導線を表示（親 `multiview-output-revision.md` §2.5 の方針を継承）。1 sink 障害が他 sink を止めない。
- [ ] 出力割当 API（`PUT /api/v1/outputs`）の sink は**固定5系統ではなく種別＋序数の可変テーブル**（`VCAM1` / `HDMI1`〜`HDMI3` / `NDI1`〜`NDI3`、既定 `VCAM1`＋`HDMI1`、上限は Webcam 1・HDMI 3・NDI 3・合計6。親仕様書 §4.2）とし、内部で `engine_start_output` にマッピングする。序数なしの旧トークン（`"VCAM"`/`"HDMI"`/`"NDI"`）は各種別の1番目として読む。エンジン側のトークン解析は `VCAM2`/`VCAM3` も受理する（旧ビルドの設定を読み込むための互換であって、追加できる上限ではない）。

## 2.4 ソースは「OBS 標準搭載モジュール」のみ

- [ ] 利用するソースは OBS にバンドルされる標準モジュールが提供するもののみとする（例: 映像キャプチャデバイス=`win-dshow`、メディアソース=`obs-ffmpeg`、画像/色/テキスト=`image-source`/`text` 等）。**必要なバンドルモジュールを同梱・読込する**構成をビルド手順に明記する。
- [ ] 既存機能要件で必要な **NDI 入力 / SRT 入力**の扱い:
  - **NDI**: 標準搭載ではないため DistroAV プラグインを前提（§2.3 と同様、導線でグレースフル無効化）。
  - **SRT**: OBS 標準の**メディアソース（FFmpeg）で `srt://` URL 入力**として受ける。ATEM の PGM を SRT Listener/Caller で受ける運用・`latency` 指定・ATEM 設定ヘルパー（ホスト名/ポート表示）は**既存要件のまま維持**（親 §2.1 / `multiview-output-revision.md` §2.7）。
- [ ] ソース追加UI・デバイス列挙・種別別設定（親 §2.1, `multiview-output-revision.md` §2.6）は**契約・UI を維持**し、列挙の実データ取得先を **libobs のプロパティ/デバイス列挙**に差し替える。

## 2.5 維持する既存機能（変更なし・再掲）

以下は**既存仕様のまま維持**する（実装の裏側のみ libobs 化）。

- [ ] マルチビュー（4x4 コンフィギュラブル、矩形結合、全画面表示）— 描画は `engine_render_multiview`/`obs_display` に置換、契約・UI・レイアウト保存は維持（親 §2.3, `multiview-output-revision.md` §2.3/§2.4）。
- [ ] タリー UDP 配信（`255.255.255.255:9999`, 2系統ペイロード）— PGM/PVW 状態は `engine_set_state_callback` から取得し、**既存の Web/配信経路をそのまま利用**（親 §2.7）。
- [ ] コントローラー入力（USB-HID）とバックライト算出（親 §2.6）— `Switcher.Hid` を維持。入力→`(bus, action, target)` 正規化→エンジンAPI（`engine_set_preview`/`engine_cut`/`engine_auto`）へ結線。
- [ ] ATEM 遠隔制御クライアント（親 §2.8）— `Switcher.Atem` を維持。ATEM は SRT/NDI 入力ソース兼遠隔制御対象。
- [ ] WebAPI/WebSocket サーバ（8080）と設定WebUI（親 §2.5）— `Switcher.Web` を維持。エンドポイント契約（sources/program/multiview/outputs/modules/pico/network, スネークケース）は不変。

## 3. 削除対象（積極削除）

> 実際の削除は実装タスクで段階的に行い、ビルド/テストが常時グリーンを保つこと。

### 3.1 削除するプロジェクト・ディレクトリ
- `src/Switcher.Media/`（GStreamer デコード・DirectX 合成・NDI/SRT/UVC パイプライン・デバイス列挙一式）→ **削除**。責務は `switcher-engine`(libobs) + `Switcher.Engine`(P/Invoke) に移設。
- `src/Switcher.VirtualCam/`（自作 DirectShow/MF 仮想カメラ・D3D スワップチェーン・HDMI 全画面・`OutputRouter`）→ **削除**。出力は libobs 出力 + `obs_display` に移設。
- 対応するテスト `tests/Switcher.Media.Tests/`・`tests/Switcher.VirtualCam.Tests/` → **削除**。

### 3.2 依存の付け替え・不要ファイル整理
- `HybridSwitcher.sln` から削除プロジェクトを除去し、新設 `Switcher.Engine`（+ `tests/Switcher.Engine.Tests`）を登録する。
- 削除プロジェクトへの参照（`Switcher.App` / `Switcher.Web` の `ProjectReference`・DI 合成）を `IVideoEngine`/`Switcher.Engine` へ付け替える。
- GStreamer/Vortice/NDI-SDK 直接参照・DLL 解決コード・ランタイムパス設定（epic で追加された `gstreamer-runtime-path` 系を含む）を撤去する。
- 参照されなくなったドキュメント記述（親 §3 の GStreamer/DirectX 技術スタック）を本仕様書へ委譲する旨を注記する（§6 参照）。

### 3.3 削除しないもの（明示）
- `Switcher.Contracts` / `Switcher.Web` / `Switcher.Hid` / `Switcher.App` / `Switcher.Atem` と各テスト。
- `firmware/*`・`apps/phone-bridge/`・`softswitcher*`・`docs/*`。
- 外部契約（WebAPI・タリーペイロード・HID レポート・設定JSON）。

## 4. 技術スタック（親 §3 を上書き）

- **言語/FW**: C# / .NET 9（WPF）※Windows 10/11 前提（**維持**）。
- **映像エンジン**: **libobs**（OBS Studio 同梱のコア）。ネイティブ `switcher-engine`（C/C++）でリンクし C ABI を公開、`Switcher.Engine`(C#) から P/Invoke。
- **合成/GPU**: libobs 内部（D3D11 等）に委譲。自作 DirectX 合成は廃止。
- **ソース**: OBS 標準バンドルモジュール（`win-dshow`/`obs-ffmpeg`/`image-source`/`text` 等）。NDI は DistroAV プラグイン前提。
- **出力**: libobs 仮想カメラ出力 / DistroAV NDI 出力 / `obs_display` フルスクリーン提示。
- **Webサーバー**: ASP.NET Core Kestrel（**維持**）。
- **ATEM 遠隔制御**: 既存 `Switcher.Atem`（UDP 9910）を**維持**。
- **ビルド前提**: 対象 OBS バージョンの libobs ヘッダ + import lib。バージョンを固定し README に明記。

## 5. 非機能要件

- **GPLv2**: libobs は GPLv2。リンクするネイティブ `switcher-engine` は GPL の影響下に置き、**GPL 境界をネイティブ層に閉じる**設計とする（マネージド側は `IVideoEngine` 抽象に依存）。頒布形態を将来変える場合の判断材料として本制約を明記する。
- **ヘッドレスCI**: libobs/OBS 非搭載環境でも **C# 側（Contracts/Web/Hid/App/Atem/Engine ラッパの単体テスト）はフェイク `IVideoEngine` 注入でビルド/テストが緑**であること。ネイティブ `switcher-engine` の実行テストは Windows + OBS 環境に限定してよい。
- **低遅延・耐障害**: 映像パスは低バッファ。1系統/1 sink の障害が全体を止めない（親 §4 を維持）。
- **契約後方互換**: WebAPI・タリー・HID・設定JSON・sink 種別・マルチビュー region 形式を壊さない。既存テスト（Contracts/Web/Hid/App/Atem）は緑を維持。
- **テスト登録**: 全テストプロジェクトが `HybridSwitcher.sln` に登録され `dotnet test HybridSwitcher.sln` で実行されること。

## 6. 影響コンポーネント（実装マップ）

- **新設**: `switcher-engine`（ネイティブ, libobs リンク, C ABI）, `src/Switcher.Engine/`（P/Invoke, `IVideoEngine` 実装）, `tests/Switcher.Engine.Tests/`。
- **`src/Switcher.Contracts/`**: `IVideoEngine` 抽象・エンジン向け DTO を追加（既存 sink/multiview/source 契約は維持）。
- **`src/Switcher.Web/`**: 出力/プログラム/マルチビュー/デバイス列挙の各エンドポイントの実処理を `IVideoEngine` 経由へ付け替え（契約 JSON は不変）。
- **`src/Switcher.App/`**: DI 合成ルートを `Switcher.Media`/`Switcher.VirtualCam` から `Switcher.Engine` へ。マルチビュー/プレビュー/HDMI全画面の提示を `obs_display` 連携へ。
- **`src/Switcher.Hid/` / `src/Switcher.Atem/`**: そのまま維持（結線先のみエンジンAPIへ）。
- **削除**: `src/Switcher.Media/`, `src/Switcher.VirtualCam/`, 両テスト（§3）。
- **`docs/specs/pc-switcher-app.md`**: §3 技術スタックの GStreamer/DirectX 記述に「本仕様書により libobs へ移行」の注記を付す（履歴追記）。

## 7. UI/UX 設計方針

- 既存の UI/UX（4x4 マルチビュー、ソースドック、出力割当、モジュール割付、ダーク基調、全画面プロジェクター）を**維持**。プレビュー/マルチビュー/全画面の描画元が libobs（`obs_display`/View テクスチャ）へ変わるのみで、操作体系・レイアウト・API 連携は不変。

## 8. エージェント実行要件
- `claude`: 最大並列 2（親 `pc-switcher-app.md` §6 に準拠）。

## 9. 未確定・技術検証項目（実装計画で確定）
- ~~**仮想カメラ2系統化**~~: **確定（2026-07-31）** — OBS 標準仮想カメラは1系統のため、**Webcam sink は `VCAM1` の1本まで**とする（2本目を NDI 送出へ振り替えると sink の種別が実際の出口を偽るため、そもそも追加させない）。`VCAM2`/`VCAM3` は旧ビルドの設定を読み込むための互換トークンとしてのみ NDI 送出（`SWITCHER VCAM2` / `SWITCHER VCAM3`）で処理される（§2.3）。
- **OBS バンドルモジュールの同梱範囲**: どのモジュールをアプリに同梱・読込するか（`win-dshow`/`obs-ffmpeg`/`image-source`/`text`/DistroAV）。
- **リンク対象 OBS バージョンの固定**と ABI 差異の扱い。
- **P/Invoke 境界の粒度**（同期呼び出し vs コールバック/イベント、スレッド安全性）。
- **マルチビュー描画の実装方式**（libobs 内合成 vs App 側 `obs_display` 合成）。

## 10. 仕様変更履歴
- **2026-07-31**: 出力（§2.3）を固定5系統から**可変テーブル**へ追随（`00-system-overview.md` §4.2）— sink は種別＋序数（`VCAM1` / `HDMI1`〜`HDMI3` / `NDI1`〜`NDI3`）で既定 `VCAM1`＋`HDMI1`、上限は Webcam 1・HDMI 3・NDI 3・合計6。序数なしの旧トークンは各種別の1番目として読む。全画面提示の対象トークンを `PGM1`/`PGM2` から sink トークン（`HDMI1`〜）へ変更。§9 の「仮想カメラ2系統化」は **Webcam sink 1本まで**で確定し、`VCAM2`/`VCAM3` の NDI 送出は旧ビルド互換の経路としてのみ残す（エンジンのトークン解析はこの互換のため3序数を受理する）。
- **2026-07-21**: 初版作成。映像エンジンを GStreamer/DirectX 自作合成から **libobs** へ全面移行するリファクタを定義 —
  ①`main` 基点で `develop` 新設、②.NET/WPF 維持＋ネイティブ libobs エンジンDLL（P/Invoke, `IVideoEngine` 抽象）、③`Switcher.Media`/`Switcher.VirtualCam` と両テストの積極削除、④OBS 標準搭載ソースのみ（NDI は DistroAV 前提）、⑤デュアルM/E は `obs_view`×2＋ソース共有で実現、⑥出力/マルチビュー/タリー/HID/ATEM/WebAPI 等の既存要件・契約は維持、⑦インストール済み OBS の libobs にリンク、⑧GPLv2 はネイティブ層に閉じ込め、ヘッドレスCIはフェイク注入で維持。Pico 2 は仕様変更の可能性ありも共通プロトコル規約は不変。
