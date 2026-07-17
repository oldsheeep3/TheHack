# 仕様書: スマホ経由Webブリッジ & 設定WebUI

> 親仕様書: [`00-system-overview.md`](./00-system-overview.md) / 共通プロトコルは親仕様書 §4 を参照。

## 1. 背景・目的

- スマホのブラウザ（またはPWA）からアクセスし、①USB-OTG接続された Pico 2W コントローラーの信号をネットワークへ中継する、②スイッチャー設定をGUIで変更する、の2機能を1つのモダンなWebUIで提供する。
- 物理ケーブルの引き回し制約を、スマホ＋Wi-Fiによる中継で解消する。

## 2. 機能要件

### 2.1 USB→Network ブリッジ機能
- [ ] スマホにUSB-OTGで接続された Pico 2W を Web Serial API / WebUSB API で認識。
- [ ] Pico 2W から送られるシリアル/HIDデータ（ボタン押下等）を検知。
- [ ] メインPC常駐アプリへ WebSocket 経由で即座にコマンド（親仕様書 §4.1 のJSON）を転送。
- [ ] `controller_id`（main/sub等）を付与して転送する。
- [ ] 接続状態（USB接続・WebSocket接続）の可視化と自動再接続。

### 2.2 スイッチャー設定UI (GUI)
- [ ] メインPCのWebAPI（`GET /api/v1/sources`）から入力ソース（1〜10ch）の名前・プロトコル・ステータスを取得して一覧表示。
- [ ] 各チャンネルのPiPレイアウト（サイズ・表示位置・クロップ・不透明度・背景）をスライダー/ドラッグで視覚的にリアルタイム変更（`POST /api/v1/config`）。
- [ ] シーンプリセットの保存・読み込み。
- [ ] タリー状態（PGM/PVW）の表示。

## 3. 技術スタック

- **フロントエンド**: React / TypeScript（Vite）
- **UI**: Tailwind CSS（ダークモード標準）
- **ブラウザAPI**: Web Serial API または WebUSB API（OTG接続のPico 2Wを直接読取）
- **通信**: WebSocket（PC常駐アプリ :8080）、REST（`/api/v1/*`）

## 4. 非機能要件

- ブリッジ経路の遅延を最小化（ボタン→PC反映 数ms〜十数ms目安）。
- Web Serial/WebUSB はHTTPS（またはlocalhost）かつ対応ブラウザ（Chromium系）前提。制約をUIで明示。
- ネットワーク断時の再接続・エラー表示。

## 5. UI/UX 設計方針

- ダーク基調・大きめのタップターゲット（現場でのスマホ片手操作を想定）。
- 「中継モード」と「設定モード」をタブ等で切替。
- 眩しさを抑えた低輝度配色、状態はアイコン＋色で即判別。

## 6. エージェント実行要件

- **使用エージェント構成**:
  - `claude`: 最大並列 1

## 7. 仕様変更履歴

- **2026-07-17**: 初版作成。コントローラーは Pico 2W（USB-OTG）を Web Serial/WebUSB で認識しWebSocket中継する構成で確定。
