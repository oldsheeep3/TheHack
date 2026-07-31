# 仕様書: マルチビュー & 出力（HDMI/NDI/フルスクリーン）改訂

> 親仕様書: [`pc-switcher-app.md`](./pc-switcher-app.md)（§2.1 ソース管理 / §2.3 マルチビュー / §2.4 出力）, [`00-system-overview.md`](./00-system-overview.md) §4.2（設定API）。
> 本仕様書は上記 v2 実装（`agent-A2-005-output-dualcam-hdmi` / `agent-A2-006-app-integration-v2` 完了済み）に対する**加算的な改訂**を定義する。既存の契約・出力型・マルチビューを壊さず拡張する。

## 1. 背景・目的

- v2 で HDMI 全画面出力・4x4 マルチビュー・仮想カメラ×2 を実装したが、実運用で以下が不足している:
  - 全画面出力先ディスプレイを明示指定できず、操作画面と衝突する事故が起きる。
  - マルチビューが 4x4 均等固定で、PGM を大きく見せる等のレイアウト自由度がない。
  - マルチビューをオペレーター用に全画面表示できない。
  - 出力が仮想カメラ / HDMI のみで、他系統ワークフローへ渡す NDI 出力がない。
  - ソース追加UIが手入力中心で、PCが認識しているデバイスを選ぶ導線がない。
  - SRT入力（ATEMのPGMをSRTで受ける運用）で、ATEM側に設定すべきホスト名/ポートが分からない。
- 本改訂は上記7点を解消し、配信オペレーションの安全性と操作性を高める。

## 2. 機能要件

### 2.1 全画面出力のディスプレイ指定と同一画面警告（要件1）
- [ ] HDMI/物理ディスプレイ全画面出力は、**出力先ディスプレイを明示指定**する（既存 `OutputAssignment.DisplayId` を UI から必須選択に）。出力割当パネルにディスプレイ選択ドロップダウン（`Display 1 / Display 2 / ...` + 解像度・主モニター表示）を設ける。
- [ ] **操作画面（メインウィンドウが載っているディスプレイ）と出力ディスプレイが同一**のとき、割当確定時に**警告ダイアログ/バナー**を表示する。挙動は**警告のみ（続行可能）**とし、`[このまま続行] / [キャンセル]` を提示する（ブロックはしない）。
- [ ] 操作画面ディスプレイは `AppConfig.OperatorDisplayIndex`（§2.2）とメインウィンドウの実表示位置から判定する。

### 2.2 操作画面ディスプレイ変更（トレイ右クリックメニュー）（要件2）
- [ ] トレイアイコン（インジケーター）の右クリックメニューに **「操作画面を移動 ▸ Display 1 / 2 / 3 …」** サブメニューを追加し、メインウィンドウを選択ディスプレイへ移動する。
- [ ] 同機能をメインウィンドウ側の操作（メニュー/ボタン）からも実行可能にする。
- [ ] 選択したディスプレイは `AppConfig.OperatorDisplayIndex` に**永続化**し、次回起動時も復元する。
- [ ] メニューの `Display N` 項目は接続中モニターを動的列挙して生成する（`System.Windows.Forms.Screen.AllScreens`）。

### 2.3 マルチビューの矩形結合（ドラッグ選択）（要件3）
- [ ] 4x4 グリッド上で**複数セルをドラッグ選択して1つの大きなセルに結合**できる（rowspan/colspan による矩形結合）。例: 左上 2x2（4エリア）を結合して PGM1 を大表示。
- [ ] 結合は**矩形のみ**許可（L字等の非矩形は不可）。結合セルには従来同様 `PGM1/PGM2/PVW1/PVW2/SRC:<id>/EMPTY` を割当可能。
- [ ] 結合の解除（分割）も可能。結合状態はレイアウトとして**保存**（`PUT /api/v1/multiview`）。
- [ ] データモデルは既存 `MultiviewLayout.Cells`（16要素フラット）を**領域（region）表現へ拡張**する（§4.1）。グリッドの寸法は 4x4 を既定とし、全領域の和集合がグリッドを隙間・重なりなく被覆すること（バリデーション §4.2）。

### 2.4 マルチビューの全画面表示（独立ウィンドウ）（要件4）
- [ ] マルチビュー（結合レイアウト反映済み）を**指定ディスプレイに全画面表示する独立ウィンドウ**（`MultiviewFullscreenWindow`）を提供する。出力割当（VCAM/HDMI/NDI sink）とは**別系統**の専用フルスクリーン表示とする。
- [ ] 全画面表示は HDMI 全画面出力と**同等の振る舞い**: 指定ディスプレイへ配置、マウスカーソル非表示、`Esc` で解除。
- [ ] 起動導線は**トレイ右クリックメニュー「マルチビュー全画面 ▸ Display N」**および**メインウィンドウのボタン**の両方（§2.2 と同一メニュー体系）。
- [ ] 全画面表示先が操作画面ディスプレイと同一のときは §2.1 と同じ**同一画面警告（続行可能）**を出す。
- [ ] 全画面中もマルチビューのライブ更新（各セルのプレビュー/枠色）を継続する。

### 2.5 NDI 出力（要件5）
- [ ] 出力 sink に **NDI（`NDI1`〜`NDI3`）を追加**する。ソースは **PGM1 / PGM2 のみ**（既定 PGM1→NDI1, PGM2→NDI2）。
- [ ] 各 NDI 出力は **NDI送出名（sender name）を設定可能**（既定 `SWITCHER PGM1` / `SWITCHER PGM2` / `SWITCHER PGM3`）。ネットワーク上に NDI ソースとして公開する。
- [ ] NDI SDK 未導入時は送出を無効化し、ソース追加と同様に**ダウンロード導線（案内）**を表示する（親 §2.1 の NDI 方針に準拠）。
- [ ] 出力割当は `PUT /api/v1/outputs` で NDI sink を含めて動的変更（§4.1）。1 sink 障害が他 sink を止めない（既存 `OutputRouter` の障害隔離方針を踏襲）。

### 2.6 ソース追加UIの3段構成 + デバイス列挙（要件6）
- [ ] ソース追加UIを次の3項目構成にする:
  1. **ソース名**（自由入力テキスト）。
  2. **ソース種別**（ドロップダウン: `WEBCAM / NDI / SRT`）。
  3. **ソース選択**（種別ごとに **PCが認識しているデバイス一覧をドロップダウン**で選択）。
- [ ] デバイス列挙:
  - **WEBCAM**: OSのキャプチャデバイス一覧（デバイス名/デバイスID、可能なら対応解像度/FPS）。
  - **NDI**: NDI find で検出したネットワーク上の NDI ソース名一覧。
  - **SRT**: SRTには「PCが認識するデバイス」が存在しないため、**デバイスドロップダウンの代わりに接続設定入力欄**（モード Listener/Caller・URL・`latency_ms`）と **§2.7 のATEM設定ヘルパー**を表示する。
- [ ] デバイス一覧は**再スキャン**操作で更新可能。列挙結果は API から取得する（§4.1 `GET /api/v1/devices/{type}`）。
- [ ] 選択結果は既存 `SourceDefinition`（`webcam.device_id` / `ndi.source_name` / `srt`）へマッピングして保存する（契約は既存を維持）。

### 2.7 SRT セットアップ手順とATEM設定用ホスト名表示（要件7）
- [ ] SRT種別選択時（およびヘルプ導線）に、**SRTセットアップ手順**と、**ATEM側に入力すべきサーバーホスト名/ポート**を表示する。
- [ ] PCが **SRT Listener（既定ポート 9000）** として待ち受ける場合、ATEM Mini の SRT ストリーミング設定に入力する値として、**このPCのLAN IPv4アドレス（複数NIC時は候補一覧）とポート**（例 `srt://192.168.1.50:9000`）を算出・表示する。
- [ ] 表示内容にはコピー可能なホスト名/URL、`latency` 目安（20〜50ms）、Listener/Caller の使い分けの短い手順を含める。
- [ ] ホスト情報は API から取得する（§4.1 `GET /api/v1/srt/setup`）。

## 3. 技術スタック（親仕様書に準拠、追加分のみ明記）
- **NDI 出力**: NDI SDK の Sender（送出）。NDI find による受信ソース列挙と共通の SDK 前提。未導入時は導線表示でグレースフルに無効化。
- **Webcam デバイス列挙**: DirectShow/Media Foundation のキャプチャデバイス列挙（既存 GStreamer/DirectShow 依存の範囲内）。
- **マルチビュー全画面**: 既存 `Switcher.VirtualCam` の `Direct3DSwapChainOutput` / `HdmiFullscreenOutput` / `ICursorVisibility` を**再利用**して独立ウィンドウへ提示。
- Windows依存（DirectShow/MF/D3D/NDI）は既存同様に該当プロジェクト内へ閉じ、テストはフェイク注入でOS非依存に保つ。

## 4. 契約・API 変更（設計方針）

> 実際の型定義・スネークケースJSONは `Switcher.Contracts` / `Switcher.Web` の実装タスクで確定。ここでは変更方針を規定する。

### 4.1 追加/変更エンドポイント・契約
- **出力（拡張）** `PUT /api/v1/outputs`:
  - `OutputSink` は**種別＋序数**の可変テーブル（`00-system-overview.md` §4.2）: `VCAM1` /
    `HDMI1`〜`HDMI3` / `NDI1`〜`NDI3`。既定は `VCAM1`＋`HDMI1`、上限は**Webcam 1・HDMI 3・NDI 3・合計6**。
    序数なしの旧トークン（`"HDMI"`/`"VCAM"`/`"NDI"`）は各種別の1番目として読み、保存時に序数付きへ書き戻す。
    `VCAM2`/`VCAM3` は旧ビルドの設定を読み込むためのトークンとしてのみ残り、新規には追加できない。
  - `OutputAssignment` に **`ndi_name`（string?）** を追加（NDI sink のときの送出名）。
  - 例:
    ```json
    {
      "outputs": [
        { "sink": "VCAM1", "source": "PGM1" },
        { "sink": "HDMI1", "display_id": 1, "source": "PGM1", "hide_cursor": true, "fullscreen": true },
        { "sink": "NDI1",  "source": "PGM1", "ndi_name": "SWITCHER PGM1" },
        { "sink": "NDI2",  "source": "PGM2", "ndi_name": "SWITCHER PGM2" }
      ]
    }
    ```
- **マルチビュー（拡張）** `PUT /api/v1/multiview`:
  - 矩形結合を表現できるよう **region 表現**へ拡張。既定グリッドは 4x4。推奨形:
    ```json
    {
      "grid": { "rows": 4, "cols": 4 },
      "regions": [
        { "row": 0, "col": 0, "row_span": 2, "col_span": 2, "content": "PGM1" },
        { "row": 0, "col": 2, "row_span": 1, "col_span": 1, "content": "PVW1" },
        { "row": 0, "col": 3, "row_span": 1, "col_span": 1, "content": "PVW2" },
        { "row": 2, "col": 0, "row_span": 1, "col_span": 1, "content": "SRC:src-ndi-cam1" }
        // ... 残りセルは 1x1 の region（EMPTY 含む）で被覆
      ]
    }
    ```
  - **後方互換**: 従来の `cells`(16フラット) 形式も受理可能とし、内部で 1x1 regions に正規化する（既存クライアント/テストを壊さない）。
- **デバイス列挙（新規）** `GET /api/v1/devices/{type}`（`type` = `webcam` | `ndi`）:
  - `DeviceInfo[]` を返す（`id`, `name`, 任意で `formats`/`resolutions`）。NDI SDK/キャプチャ未検出時は空配列＋導線メッセージ。
- **SRT セットアップ情報（新規）** `GET /api/v1/srt/setup`:
  - `SrtSetupInfo` を返す（`listener_port`, `host_candidates`(LAN IPv4[]), 推奨URL文字列, `latency` 目安, 手順テキスト）。

### 4.2 バリデーション
- `OutputsRequestValidator`: 各 sink は最大1回。**Webcam 1・HDMI 3・NDI 3・合計6を超えないこと**（`OutputRules.DescribeOverLimit`。旧ビルドの `VCAM1`＋`VCAM2` 既定はここで弾かれ、アプリは現行の既定へフォールバックする）。HDMI は `display_id` 必須（既存）。**NDI sink のとき `source` は PGM1/PGM2 のみ**、`ndi_name` 空文字不可（未指定は既定名で補完）。
- `MultiviewLayoutValidator`: region 形式のとき、**全 region が矩形かつグリッド内**、**重なりなし・隙間なし（完全被覆）**、各 `content` が `PGM1/PGM2/PVW1/PVW2/SRC:<id>/EMPTY`。従来 `cells` 形式は既存規則（16セル）を維持。

### 4.3 影響コンポーネント（実装マップ）
- `src/Switcher.Contracts/`: `OutputsRequest`(sink拡張/ndi_name), `MultiviewLayout`(region化+互換), 新 `DeviceInfo`/`SrtSetupInfo`。
- `src/Switcher.VirtualCam/`: NDI 送出型（`NdiOutput` 系）と `OutputRouter` への NDI ルーティング追加、マルチビュー全画面提示（既存 HDMI 全画面型の再利用）。
- `src/Switcher.Media/`: Webcam/NDI デバイス列挙サービス、マルチビュー合成の region 対応、SRT Listener ホスト/ポート算出。
- `src/Switcher.Web/`: `devices`/`srt/setup` エンドポイント、`OutputsRequestValidator`/`MultiviewLayoutValidator` 拡張。
- `src/Switcher.App/`: `TrayIconService`（操作画面移動 / マルチビュー全画面サブメニュー）、`MultiviewFullscreenWindow`（新規）、`ProjectorWindow`/出力割当UIの同一画面警告・ディスプレイ選択、`MainWindow` のマルチビュー・ドラッグ結合UI / ソース追加3段UI（デバイスドロップダウン）/ SRTヘルプパネル、`AppConfig.OperatorDisplayIndex` 追加・永続化。

## 5. 非機能要件
- 既存の低遅延・障害隔離方針を維持（NDI/HDMI/VCAM の1 sink 障害が他 sink・合成を止めない）。
- 契約変更は**後方互換**（`cells` 形式・既存 sink・既存 `SourceDefinition` を壊さない）。既存テスト（Contracts/Web/VirtualCam/App）は全グリーンを維持。
- 全テストプロジェクトが `HybridSwitcher.sln` に登録され `dotnet test HybridSwitcher.sln` で実行されること（v2 の教訓を踏襲）。
- Windows実行時依存（NDI/DirectShow/MF/D3D）はプロジェクト内に閉じ、テストはフェイク注入でヘッドレスCIでもビルド/テストが通ること。

## 6. UI/UX 設計方針
- 出力割当パネル: 既定の VCAM1＋HDMI1 に「出力を追加」（Webcam/HDMI/NDI）で sink を足す（Webcam 1・HDMI 3・NDI 3・合計6で追加不可に。Webcam は既定の1本で上限なので実質 HDMI/NDI を足す）＋ソース(PGM1/PGM2)＋HDMIはディスプレイ選択＋NDIは送出名。同一画面選択時に警告バナー。
- マルチビュー: ドラッグで矩形セル選択→結合/解除。結合セルはラベル・枠色（PGM=赤/PVW=緑）を維持。全画面ボタン。
- ソースドック: 名前(自由入力)→種別(ドロップダウン)→デバイス(ドロップダウン, 再スキャン)。SRTは接続設定＋ATEMヘルパー。
- トレイ右クリック: `Show / 操作画面を移動 ▸ Display N / マルチビュー全画面 ▸ Display N / Exit`。
- ダーク基調を維持（親 §5）。

## 7. エージェント実行要件
- `claude`: 最大並列 2（親 `pc-switcher-app.md` §6 に準拠）。

## 8. 仕様変更履歴
- **2026-07-31**: 出力 sink を固定5系統から**可変テーブル**へ改訂（`00-system-overview.md` §4.2 に追随）—
  `VCAM1` / `HDMI1`〜`HDMI3` / `NDI1`〜`NDI3` をオペレーターが追加（既定 `VCAM1`＋`HDMI1`、Webcam 1・HDMI 3・NDI 3・合計6が上限）。
  旧 `HDMI` は `HDMI1` に読み替え。実体のある仮想カメラは `VCAM1` のみなので **Webcam sink も1本まで**とし、
  `VCAM2`/`VCAM3` は旧ビルドの設定を読み込むための互換トークン（エンジンは NDI 送出
  `SWITCHER VCAM2` / `SWITCHER VCAM3` で処理）としてのみ残す。
- **2026-07-18**: 初版作成。マルチビュー&出力改訂の7要件を定義 —
  ①全画面出力のディスプレイ指定＋同一画面警告(続行可)、②操作画面ディスプレイをトレイ右クリック等から変更・永続化、③マルチビューの矩形結合(ドラッグ, region化/cells後方互換)、④マルチビュー全画面(独立ウィンドウ, HDMI全画面と同等挙動, トレイ+メイン両導線)、⑤NDI出力2系統(NDI1/NDI2, PGM1/PGM2, 送出名設定)、⑥ソース追加3段UI(名前/種別/デバイス列挙, SRTは接続設定+ATEMヘルパー)、⑦SRTセットアップ手順とATEM設定用ホスト名/ポート表示。
