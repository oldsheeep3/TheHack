# Switcher — 技術仕様（要約）

1枚で読み切る版。詳細は [technical-overview.md](technical-overview.md)、発表用は
[../slides/switcher.md](../slides/switcher.md)。

## これは何か

Windows 上で動く **2系統 M/E のライブ映像スイッチャー**。映像処理は OBS のコアである libobs に
一本化し、その上に独自の制御・UI・外部連携を載せている。物理コントローラー（Raspberry Pi Pico 2W）、
スマホからの Web 操作、ATEM Mini の遠隔制御を1つのアプリにまとめる。

## 構成

```
Switcher.App (WPF)  ──  AppOrchestrator（PGM/PVW 状態の唯一の権威）
   ├ Switcher.Web     REST + WebSocket（:8080）
   ├ Switcher.Hid     USB-HID（Pico 2W）
   ├ Switcher.Atem    ATEM Mini（UDP 9910）
   └ Switcher.Engine  IVideoEngine ─P/Invoke─ switcher-engine (C++ / libobs + NDI SDK)
```

## できること

| | |
|---|---|
| **入力** | Webcam(UVC) · NDI · SRT · 画像 · Web ページ(HTML) · **MIX**（複数ソースを合成して1ソース化） |
| **M/E** | PGM1/PVW1・PGM2/PVW2 の2系統独立。CUT / AUTO(フェード) は当該バスのみに作用 |
| **マルチビュー** | 4×4〜6×6 の可変グリッド。矩形結合。オンエア=赤枠 / ステージ=緑枠（カメラのセルも） |
| **出力** | 仮想カメラ · NDI×2（映像+音声）· HDMI(全画面)。**PGM ごとに最低1つ必須** |
| **音声** | 映像ソースに追従（追加時に自動）。ソースごとに AFV / ON / OFF。PGM1/2 を独立した出力デバイスへ（1バス→複数デバイス可） |
| **操作** | オペレーター画面 · キーボード · 物理モジュール(HID) · Web API |
| **タリー** | UDP ブロードキャスト（`255.255.255.255:9999`、2系統）。色は PGM/PVW × バス1/2 で個別設定でき、Pico の LED も同じ色 |

## 設計上の要点

- **入力は1回だけ開く。** 共有ソースプールにより、同じカメラを PGM1・PGM2・MIX に同時に出せる。
  これは単一プロセス構成でのみ成立する。
- **状態変更は1か所を通る。** HID・Web・UI の3経路が競合してタリーやバックライトが壊れないよう、
  `AppOrchestrator` が全変更を直列化する。
- **NDI は DistroAV 非依存。** NDI SDK を直接使い、入力（自前の obs ソース型）と出力（自前の obs
  出力型）を実装。ヘッダは MIT なので同梱し（SDK 不要でビルド可能）、ランタイムはユーザーが入れた
  NDI Tools を実行時に動的ロードする。NDI のない PC でも同じバイナリが動く。
- **MIX はシーンそのもの。** libobs の private scene を共有プールに登録するだけなので、mix は
  バス・マルチビュー・モジュール割当でカメラと完全に同じ扱いになる。
- **UI はダーク固定・赤緑はタリー専用。** オペレーターは本線モニターの横に座る。
- **合成ソースがオンエアなら中身もオンエア。** MIX の内側まで展開してタリーと AFV を決める。
- **AFV はアプリ側の概念。** libobs にはトラック所属しかないので、オーケストレーターがバス状態から
  ミキサーマスクを算出する。バス n の音声は libobs のトラック n。
- **設定は再起動をまたぐ。** ソース・マルチビュー配置・出力・音声ルーティング・タリー色・モジュール
  割当を保存。ただしバスは空で起動する（起動しただけで本線に乗せない）。

## 動作要件

Windows 10/11 x64 · libobs ランタイム（**同梱の `obs-runtime/` があれば OBS のインストールは不要**。
なければインストール済み OBS を自動検出。検証範囲 30.0〜32.99）· NDI を使う場合のみ NDI Tools。

## 品質

`dotnet test` で **333 件**。契約・HID・ATEM・Web バリデータ・オーケストレーター・マルチビュー領域
モデル・タリー判定と MIX 伝播・AFV ポリシー・永続化を網羅。libobs/NDI を跨ぐ層は実機検証（Windows + OBS 必須のため CI 不可）。

## 現時点の非対応

録画 · RTMP 配信 · 音声のレベル/メーター（モードとデバイス割当のみ）· 合成マルチビュー出力へのタリー枠 ·
PiP 不透明度。
