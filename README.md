# HybridSwitcher — ハイブリッドIP映像スイッチャー

既存機材（ATEM Mini）と自作ハードウェア（自作スイッチングモジュール群 + Pico 2W）、
ネットワーク技術（SRT / NDI / WebAPI）を融合した、次世代のハイブリッドIP映像スイッチャーシステム。

PC上に常駐する制御アプリを「脳」とし、複数プロトコル（UVC / NDI / SRT）の映像入力・PiP合成・
仮想カメラ出力・タリー送出・外部機材（ATEM）の遠隔制御を一元化する。DIYでありながらプロ用
スイッチャーに匹敵する多人数オペレーション（メイン/サブ分業）と拡張性を目指す。

> 全体仕様は [`docs/specs/00-system-overview.md`](docs/specs/00-system-overview.md)（親仕様書）を参照。

## システム構成

```text
[ サブ ATEM Mini ] ──(SRT/HDMI/NDI)──→ [ メインPC (常駐App) ] ──(仮想WebCam/HDMI)──→ [ 配信/録画/OBS等 ]
        ▲                                    ▲        │
        │ (ATEM遠隔制御: UDP 9910)            │        │ (UDPタリー: 9999 ブロードキャスト)
        └────────────────────────────────────┘        ▼
                                             ▲   [ ワイヤレスタリー子機(ESP32) ]  [ 赤/緑 LED ]
                                             │ (WebSocket / WebAPI: 8080)
                                    (USB/HID/Serial)
[ 自作コントローラー: Pico 2W ] ──┬── (USB直結) ──→ メインPC
        │                         └── (USB-OTG) ──→ [ スマホ (Web App) ] ──(Wi-Fi/WebSocket)──→ メインPC
        │
        ├─(内部制御: 後日詳細化)──→ [ 各スイッチングモジュール内マイコン ]
        └─(HDMI DDC/I2Cスニッフィング)──→ [ 物理タリー抽出 ]
```

## コンポーネント

| コンポーネント | 場所 | 技術 | 責務 | 個別README / 仕様書 |
| --- | --- | --- | --- | --- |
| PC常駐アプリ | `src/` | C# / .NET 9 + GStreamer | 映像入力デコード・PiP合成・仮想カメラ/HDMI出力・WebAPI/WebSocket・UDPタリー送出・ATEM制御 | [App](src/Switcher.App/README.md) / [仕様](docs/specs/pc-switcher-app.md) |
| Pico 2W コントローラー | `firmware/pico2w-controller/` | Pico SDK (C/C++) | 物理ボタン入力→PC/スマホ送信・モジュール統括・HDMI DDCタリー抽出 | [Firmware](firmware/pico2w-controller/README.md) / [仕様](docs/specs/pico2w-controller-firmware.md) |
| スマホWebブリッジ | `apps/phone-bridge/` | React / TypeScript | Web Serial/WebUSBでPico 2Wを中継・ブラウザ設定UI | [phone-bridge](apps/phone-bridge/README.md) / [仕様](docs/specs/phone-web-bridge.md) |
| ワイヤレスタリー子機 | (未着手) | ESP32 (Arduino/ESP-IDF) | UDPブロードキャスト受信→赤/緑LED点灯 | [仕様](docs/specs/wireless-tally.md) |

### PC常駐アプリの .NET プロジェクト構成（`src/`）

| プロジェクト | 役割 |
| --- | --- |
| `Switcher.Contracts` | 共通インターフェース・DTO（各モジュールが依存する契約層） |
| `Switcher.Media` | 多ソース入力管理（UVC/NDI/SRT）とGPU/PiPコンポジター |
| `Switcher.Atem` | ATEM遠隔制御クライアント（UDP 9910） |
| `Switcher.VirtualCam` | 仮想カメラ出力・全画面物理ディスプレイ出力 |
| `Switcher.Web` | WebAPI/WebSocketサーバ・UDPタリーブロードキャスト |
| `Switcher.App` | WPFホスト。上記を1つの常駐プロセスに結線するDI合成ルート |

## リポジトリ構成

```text
.
├── src/                       # PC常駐アプリ (.NET 9) — HybridSwitcher.sln
├── tests/                     # 上記各プロジェクトのユニットテスト
├── apps/phone-bridge/         # スマホWebブリッジ & 設定UI (React/TS)
├── firmware/pico2w-controller/# Pico 2W コントローラー / タリー抽出ファーム
├── docs/
│   ├── specs/                 # 仕様書（親仕様書 + コンポーネント別）
│   └── tasks/                 # マルチエージェント実装計画・タスク指示書
├── softswitcher*/             # 既存KiCadハードウェア設計（参考資産）
└── HybridSwitcher.sln         # .NET ソリューション
```

## ビルド & テスト

### PC常駐アプリ（.NET 9）

> このリポジトリでは `dotnet` は PATH 上に無く SDK は `~/.dotnet` にある。以下を前置きする。

```sh
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH

dotnet build HybridSwitcher.sln     # 全プロジェクトのビルド
dotnet test  HybridSwitcher.sln     # 全ユニットテスト
```

`Switcher.App` は WPF (`net9.0-windows`) だが `EnableWindowsTargeting` によりLinuxでもCIでも
**コンパイルは可能**。ただし GStreamer / DirectX / DirectShow のネイティブランタイムは Windows 限定で、
**実行**には Windows 10/11 と各コンポーネントREADMEに記載の前提が必要。

### スマホWebブリッジ（React/TS）

```sh
cd apps/phone-bridge
npm install
npm run dev      # 開発サーバ
npm run build    # プロダクションビルド
npm test         # vitest
```

### Pico 2W ファームウェア

I/Oに依存しないロジックはホストの `gcc` でユニットテスト可能（Pico SDK不要）。

```sh
cd firmware/pico2w-controller/test
make             # ビルド + 実行（全テスト green で終了コード0）
```

実機向けクロスビルド（Pico SDK / `arm-none-eabi` ツールチェーン必要）は
[`firmware/pico2w-controller/README.md`](firmware/pico2w-controller/README.md) を参照。

## 共通プロトコル / ポート

各コンポーネントは親仕様書 §4 の共通定義に準拠する。

| ポート | プロトコル | 用途 |
| --- | --- | --- |
| 8080 | HTTP/WebSocket | 設定WebAPI・コントローラー入力受信 |
| 9000 | SRT (Listener) | 映像入力（ATEM PGM等） |
| 9999 | UDP broadcast | タリー配信（`255.255.255.255:9999`） |
| 9910 | UDP | ATEM遠隔制御（ATEM純正プロトコル） |

詳細（イベントペイロード・設定変更API・タリーペイロード）は
[`docs/specs/00-system-overview.md`](docs/specs/00-system-overview.md) §4 を参照。

## ドキュメント

- **仕様書**: [`docs/specs/`](docs/specs/) — 親仕様書 + コンポーネント別詳細仕様
- **実装計画 / タスク**: [`docs/tasks/`](docs/tasks/) — マルチエージェント実装のオーケストレーション計画とタスク指示書
