# 仕様書: ハイブリッドIP映像スイッチャー システム全体概要

> 本ドキュメントは全体像を示す**親仕様書**です。各コンポーネントの詳細は個別仕様書（下記）を参照してください。

## 0. 関連仕様書（分割構成）

| ファイル | コンポーネント | 主担当技術 |
| --- | --- | --- |
| `00-system-overview.md`（本書） | システム全体・共通プロトコル定義 | - |
| `pc-switcher-app.md` | PC常駐アプリ（映像エンジン／制御ハブ） | C# / .NET + GStreamer |
| `switcher-module-firmware.md` | スイッチングモジュール内マイコン ファーム | CH32V003 (RISC-V) |
| `pico2w-controller-firmware.md` | Pico 2W マスターコントローラー ファーム | Pico SDK (C/C++) |
| `phone-web-bridge.md` | スマホ経由Web設定UI（ネットワーク中継） | React / TypeScript |
| `wireless-tally.md` | ワイヤレスタリー子機（外部デバイス） & タリープロトコル | ESP32 (Arduino/ESP-IDF) |

---

## 1. 背景・目的

- 既存のハードウェア（ATEM Mini）と自作ハードウェア（自作スイッチングモジュール群 + Pico 2W）、およびネットワーク技術（SRT / NDI / WebAPI）を融合した、次世代のハイブリッドIP映像スイッチャーシステムを構築する。
- PC上に常駐する制御アプリを「脳」とし、複数プロトコル（WebCam(UVC) / NDI / SRT）の映像入力・PiP合成・**2系統プログラム(PGM1/PGM2)出力**・仮想カメラ/HDMI出力を一元化する。
- DIYでありながらプロ用スイッチャーに匹敵する多人数オペレーション（メイン/サブ分業）と拡張性を実現する。

## 2. システムアーキテクチャ

```text
                        [ サブ ATEM Mini ] ──(SRT/NDI/UVC)──┐
                        [ NDI ソース群 ]  ─────────────────┤
                        [ USB WebCam ]   ─────────────────┤
                                                          ▼
[ スイッチングモジュール×N ]                     [ メインPC (常駐App) ]
  各モジュール:                                     │  ├─ 映像エンジン(GStreamer + GPU合成)
   - SW×4 (pgm1/pgm2 × src1/src2 マトリクス)         │  ├─ 2系統ME(PGM1/PGM2 + PVW1/PVW2)
   - アナログVR×2 (src1/src2)                        │  ├─ 4x4 コンフィギュラブル・マルチビュー
   - SK6812 バックライト×4                           │  ├─ 出力: 仮想カメラ×2 + HDMI(全画面/カーソル非表示)
   - CH32V003F6P4                                    │  ├─ 設定WebUI/API (8080)
        │  ▲                                         │  └─ タリーUDP配信 (9999 → 外部タリーデバイス)
        │  │ (I2C マルチドロップ: Pico=master)         │        ▲
        ▼  │                                          │        │ (Wi-Fi/BT リモート or USB-HID)
[ マスター: Pico 2W ] ──(USB-HID: 集約した SW/VR 状態)──→ メインPC
        │  ▲                                          │
        │  └──(HID出力: バックライト色指定)────────────┘
        └──(任意) Wi-Fi/BT ワイヤレス接続 ──→ メインPC / [ スマホ設定UI(ネットワーク経由) ]

[ 外部ワイヤレスタリー子機(ESP32) ] ←──(UDP 9999 broadcast)── メインPC   [ 赤/緑 LED ]
```

### 2.1 ハードウェア構成方針（改訂: 2026-07-18）

- **スイッチングモジュール**は内部に **CH32V003F6P4（RISC-V, 低コストMCU）** を搭載する。1モジュールは以下を持つ:
  - **スイッチ×4**: `(pgm1, pgm2) × (src1, src2)` のマトリクス。各ソースを PGM1／PGM2 のどちらのプログラムバスに載せるかを直接トグルする。
  - **アナログボリューム×2**: src1／src2 に1つずつ。用途はトランジション/汎用アサイナブルパラメータ（§4.2）。
  - **スイッチバックライト×4**: `SK6812MINI-E`（各スイッチ1灯、モジュール内で数珠つなぎ）。色はアプリ状態を反映（PCが算出）。
- 各モジュールの CH32V003 は **I2Cスレーブ**として、マスターの **Pico 2W（I2Cマスター）** と接続する（マルチドロップ、モジュールをアドレスで識別・拡張）。
- **Pico 2W の役割は縮小**し、以下のみを担う:
  1. 全モジュールを I2C でポーリングして SW/VR 状態を**集約**し、**USB-HID** としてPCへ伝達する（§4.1）。
  2. PCが算出したバックライト色を **HID出力レポート**で受け取り、I2C で各モジュールへ配布する（§4.5）。
  3. 全体設定の保持（PC経由で保存/更新。§4.2, §4.6）。
  4. （任意）Wi-Fi/BT によるワイヤレス接続。ネットワーク設定はPC経由で投入する。
- **タリー抽出機能（HDMI DDCスニッフィング）は Pico から削除**する。タリーは外部の独立デバイス（`wireless-tally.md`）が担当し、PC常駐アプリの UDP配信（§4.3）を受信して点灯する。
- リポジトリ内の既存 KiCad 設計（`softswitcher_module_sw4` 等）は本方針に合わせて参照・再検討する。

## 3. コンポーネント責務サマリ

| コンポーネント | 主な責務 |
| --- | --- |
| PC常駐アプリ | 異種プロトコル映像入力(NDI/WebCam/SRT)のOBS的な自由追加・デコード、2系統ME(PGM1/PGM2)のPiP合成、4x4マルチビュー、仮想カメラ×2/HDMI出力、設定WebAPI/UI、タリーUDP配信、HID経由のコントローラー入力受信/バックライト指示、**ATEM遠隔制御クライアント**(UDP9910) |
| スイッチングモジュール(CH32V003) | SW×4スキャン、VR×2のADC読取、SK6812×4駆動、I2Cスレーブとして状態提供/バックライト受領 |
| Pico 2W マスター | 全モジュールをI2Cで集約→USB-HIDでPCへ、HID出力のバックライトをI2C配布、設定保持、（任意）Wi-Fi/BTワイヤレス |
| スマホWeb設定UI | ネットワーク経由（Wi-Fi/BT/PCプロキシ）でPCへ接続し、ソース定義・レイアウト・出力割当・Pico設定を操作 |
| 外部ワイヤレスタリー子機 | PCのUDPブロードキャストを受信し赤/緑LEDを点灯（本システムからは疎結合の別デバイス） |

## 4. 共通プロトコル定義

各コンポーネントはこの共通定義に準拠する。詳細な各エンドポイントは個別仕様書に記載。定数（最大モジュール数等）は本節を単一のソースとする。

### 4.0 システム定数

| 定数 | 値 | 備考 |
| --- | --- | --- |
| `MAX_MODULES` | 8 | HIDレポート/アドレス空間の上限（拡張時は本値を更新） |
| モジュールあたり SW 数 | 4 | `PGM1×SRC1, PGM1×SRC2, PGM2×SRC1, PGM2×SRC2` |
| モジュールあたり VR 数 | 2 | `SRC1, SRC2` |
| モジュールあたり バックライト数 | 4 | SK6812MINI-E（各SWに1灯） |
| プログラムバス | 2 | `PGM1`, `PGM2`（各々に `PVW1`/`PVW2`） |

### 4.1 コントローラー入力: USB-HID（Pico 2W → PC）【改訂】

Pico 2W はベンダー定義の HID デバイスとして列挙され、集約した SW/VR **状態**を入力レポートで通知する（イベント化＝押下エッジ検出はPC側が状態差分から行う）。

- **入力レポート（Report ID `0x01`, Pico→PC, 状態通知型）** — 全モジュール分をまとめて送出:

| オフセット | 長さ | 内容 |
| --- | --- | --- |
| 0 | 1 | `module_present` ビットマップ（bit n = モジュール n 接続中） |
| 1 | `MAX_MODULES` | 各モジュールのSW状態（1バイト/モジュール, 下位4bit: `b0=PGM1×SRC1, b1=PGM1×SRC2, b2=PGM2×SRC1, b3=PGM2×SRC2`） |
| 1+`MAX_MODULES` | `2×MAX_MODULES` | 各モジュールのVR値（2バイト/モジュール: `VR_SRC1(0..255)`, `VR_SRC2(0..255)`） |
| 末尾 | 1 | `seq`（0-255ローテート, 取りこぼし検出用） |

- **HID出力レポート（Report ID `0x02`, PC→Pico, バックライト指定, モジュール単位アドレス指定）**:

| オフセット | 長さ | 内容 |
| --- | --- | --- |
| 0 | 1 | `module_index`（0..`MAX_MODULES`-1） |
| 1 | 12 | 4灯分の `RGB`（各3バイト, 順序はモジュール側で SK6812 GRB に変換） |

- `controller_id`（`main`/`sub` 等）は HID のシリアル文字列またはPC側設定で紐付ける。
- 低遅延: SW状態→PC反映は数ms以内。VRは適度なデッドバンド/間引きで送出。

### 4.2 スイッチャー設定 API（PC常駐アプリ, ポート8080）【改訂・拡張】

OBS的にソースを自由追加し、2系統ME・マルチビュー・出力割当・モジュール割付・Pico設定を操作する。JSONフィールドはスネークケース。

- **ソース管理**
  - `GET /api/v1/sources` … 定義済みソース一覧（`SourceInfo[]`）。
  - `POST /api/v1/sources` … ソース追加（`SourceDefinition`）。
  - `PUT /api/v1/sources/{id}` / `DELETE /api/v1/sources/{id}` … 更新/削除。

```json
// SourceDefinition（POST /api/v1/sources）
{
  "id": "src-ndi-cam1",
  "name": "Cam 1 (NDI)",
  "type": "NDI",                       // "NDI" | "WEBCAM" | "SRT"
  "ndi": { "source_name": "STUDIO (Cam1)" },
  "webcam": null,                       // { "device_id": "...", "format": "1920x1080@30" }
  "srt": null                           // { "url": "srt://192.168.1.100:9000?mode=caller", "latency_ms": 40 }
}
```

- **プログラム/合成（2系統ME）**
  - `POST /api/v1/program` … PGM1/PGM2 の合成メンバー・PiPレイアウトの適用、および PVW→PGM の TAKE。

```json
{
  "bus": "PGM1",                        // "PGM1" | "PGM2"
  "layers": [                            // このバスの合成レイヤ（背面→前面）
    { "source_id": "src-ndi-cam1", "pip": {
        "enabled": true, "x_position": 0, "y_position": 0,
        "width": 1920, "height": 1080, "opacity": 1.0, "z_order": 0, "crop": null } }
  ],
  "take": false                          // true で当該バスの PVW→PGM 切替
}
```

- **マルチビュー（4x4 コンフィギュラブル）**
  - `PUT /api/v1/multiview` … 16セルの割当（`PGM1`/`PGM2`/`PVW1`/`PVW2`/`SRC:<id>`/`EMPTY`）。

```json
{ "cells": [ "PGM1","PGM2","PVW1","PVW2",
             "SRC:src-ndi-cam1","SRC:src-webcam-1","EMPTY","EMPTY",
             "EMPTY","EMPTY","EMPTY","EMPTY",
             "EMPTY","EMPTY","EMPTY","EMPTY" ] }
```

- **出力割当**
  - `PUT /api/v1/outputs` … 仮想カメラ×2／HDMIディスプレイへ PGM を割当。

```json
{
  "outputs": [
    { "sink": "VCAM1", "source": "PGM1" },
    { "sink": "VCAM2", "source": "PGM2" },
    { "sink": "HDMI",  "display_id": 1, "source": "PGM1", "hide_cursor": true, "fullscreen": true }
  ]
}
```

- **モジュール割付・ボリューム割当**
  - `PUT /api/v1/modules` … 物理モジュールの `src1/src2` を論理ソースへ紐付け、VRの割当先（トランジション/汎用パラメータ）を指定、バックライトポリシーを設定。

```json
{
  "modules": [
    { "index": 0,
      "src1": { "source_id": "src-ndi-cam1", "vr_target": "transition" },
      "src2": { "source_id": "src-webcam-1",  "vr_target": "opacity" } }
  ]
}
```

- **ATEM 遠隔制御**
  - `PUT /api/v1/atem` … 制御対象 ATEM の接続設定（IP）とボタン→ATEMコマンド割付を保存。
  - `POST /api/v1/atem/command` … 単発のATEMコマンド送出（Program/Preview入力切替・Cut/Auto）。

```json
// PUT /api/v1/atem
{
  "enabled": true,
  "ip": "192.168.1.240",
  "mappings": [
    { "controller_id": "main", "module_index": 0, "switch": "PGM1xSRC1",
      "action": "ProgramInput", "mix_effect": 0, "source": 1 }
  ]
}
```

> ATEM側で src1〜4 を一括管理する運用では、モジュールSW（またはVR）を上記マッピングで ATEM コマンドに割り当て、ATEM の入力切替をPCコントローラーから中継する。ATEMのPGMは併せて §4.2 のソース（SRT/NDI等）として受ける。

- **Pico ネットワーク設定（PC経由で投入）**
  - `PUT /api/v1/pico/network` … Pico の Wi-Fi/BT 設定をPC経由で保存・投入（§4.6）。

### 4.3 独自タリー配信プロトコル（UDPブロードキャスト）【改訂: 2系統対応】

- 送信元: メインPC常駐アプリ（**維持**。受信は外部タリーデバイス）。
- 宛先: `255.255.255.255:9999`（LANブロードキャスト）。
- 送出契機: 状態変化時に即時 ＋ 定期冗長送出（例: 4回/秒）。
- ペイロード（2系統ME対応, 各バスの本番/プレビュー中ソースIDを配列で）:

```json
{
  "active_pgm1": [1, 3],
  "active_pgm2": [2],
  "active_pvw1": [4],
  "active_pvw2": []
}
```

> 互換のため単系統時は `active_pgm1`/`active_pvw1` のみ使用してもよい。ソースIDは §4.2 のソース定義に紐づく整数チャンネル/序数。

### 4.4 ポート一覧（共通）

| ポート | プロトコル | 用途 |
| --- | --- | --- |
| 8080 | HTTP/WebSocket | 設定WebAPI・WebUI・（スマホ）リモート接続 |
| 9000 | SRT (Listener) | 映像入力（ATEM PGM/SRTソース等） |
| 9999 | UDP broadcast | タリー配信（外部タリーデバイス向け） |
| 9910 | UDP | ATEM遠隔制御（ATEM純正プロトコル） |

> **ATEM の二役**: ATEM Mini は「①SRT/NDI等の**入力ソース**」であると同時に「②PCから**遠隔制御**できる外部スイッチャー」でもある。src1〜4 のような複数入力を ATEM 側で一括管理し、その PGM を1系統のソースとして受けつつ、PCのコントローラー入力（モジュールSW/VRの割当）を ATEM コマンド（Program/Preview入力切替・Cut/Auto）へ変換して中継する（詳細は `pc-switcher-app.md` §2.8）。

### 4.5 モジュール内部I2Cプロトコル（Pico 2W master ↔ CH32V003 slave）【新規】

- バス: I2C（100k〜400kHz）。マスター= Pico 2W、スレーブ= 各モジュール CH32V003。
- アドレス: ベース `0x30` + モジュール番号（0..`MAX_MODULES`-1）= `0x30`..`0x37`。番号はモジュールのストラップ（GPIO/抵抗ID）で設定。
- レジスタマップ:

| レジスタ | 方向 | 長さ | 内容 |
| --- | --- | --- | --- |
| `0x00` STATE | read | 3 | `[0]`SW状態(下位4bit) / `[1]`VR_SRC1(0..255) / `[2]`VR_SRC2(0..255) |
| `0x10` BACKLIGHT | write | 12 | 4灯分 `RGB`（モジュール側で SK6812 GRB へ変換して駆動） |
| `0xF0` INFO | read | 4 | `[0..1]`firmware version / `[2]`capabilities / `[3]`module HW rev |

- Pico はモジュールを定期ポーリング（集約周期は 1kHz 目安, HID送出は状態変化＋一定レート）。
- SK6812 は 800kHz GRB。CH32V003 が SPI もしくは厳密タイミングのビットバンで局所の4灯チェーンを駆動する。

### 4.6 設定の保存とネットワーク設定【新規】

- 設定の正はPC常駐アプリが保持し、WebUI/APIで編集する。Picoの保持設定（`controller_id`、モジュール割付、Wi-Fi/BT資格情報等）はPC経由でPicoへ投入・保存する（HIDフィーチャーレポートまたはワイヤレス制御チャネル）。
- Wi-Fi/BT: Pico 2W の CYW43 を用い、PCから投入した資格情報でワイヤレス接続を確立できる（HIDが使えない/無線運用したい場合の代替経路）。BTでも可。

## 5. 非機能要件（システム共通）

- **低遅延**: 映像パスはゼロ〜低バッファ。SRTはLAN前提で `latency` を最小化（20〜50ms目安）。
- **コントローラー→切替反映**: SW状態→PGM反映は数ms〜十数ms（HID経路）。
- **同時操作安全性**: メイン/サブ2名同時操作でも取りこぼし・競合が起きないよう、PC側で入力を直列化。
- **障害隔離**: 1系統の映像障害・1モジュールのI2C障害が全体を巻き込まない。
- **拡張性**: 入力ソースはOBS的に無制限追加。モジュールは `MAX_MODULES` まで数珠つなぎ拡張。

## 6. エージェント実行要件

- `claude`: コンポーネントごとに分割実装。実装計画は個別仕様書単位で `/create-task` により策定する。

## 7. 仕様変更履歴

- **2026-07-18**: 大幅改訂（壁打ちにて確定）—
  - モジュール内MCUを **CH32V003F6P4** に確定。SW×4(pgm1/pgm2×src1/src2)＋VR×2＋SK6812×4。
  - モジュール↔Pico を **I2Cマルチドロップ**で確定（§4.5）。
  - Pico 役割を縮小: **USB-HID集約**（§4.1）＋バックライト配布＋設定保持＋（任意）Wi-Fi/BT。
  - **タリー抽出(DDC)を Pico から削除**。タリーUDP配信はPC側で維持し外部デバイスが受信。
  - PCアプリ: OBS的ソース自由追加(NDI/WebCam/SRT)、**2系統ME(PGM1/PGM2)+PiP**、4x4コンフィギュラブル・マルチビュー、**仮想カメラ×2 + HDMI(全画面/カーソル非表示)**出力（§4.2）。
  - スマホWebブリッジを **ネットワーク経由**（Wi-Fi/BT/PCプロキシ）に再定義（USB Web Serial から変更, 機能は維持）。
  - ATEMは「**入力ソース**」かつ「**遠隔制御対象**」の二役として維持（UDP9910）。src1〜4のATEM一括管理運用に対応し、モジュールSW/VRをATEMコマンドへ割付可能（§4.2）。
- **2026-07-17**: 初版作成。
