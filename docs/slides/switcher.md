---
marp: true
theme: default
paginate: true
size: 16:9
backgroundColor: #0E1116
color: #E6EAEF
style: |
  section {
    font-family: "Segoe UI", "Hiragino Kaku Gothic ProN", "Yu Gothic UI", sans-serif;
    padding: 56px 64px;
  }
  h1 { color: #FFFFFF; font-size: 46px; letter-spacing: -0.02em; margin-bottom: 12px; }
  h2 { color: #FFFFFF; font-size: 34px; letter-spacing: -0.015em; border-bottom: 1px solid #2A2E34; padding-bottom: 10px; }
  h3 { color: #4AB6E8; font-size: 22px; }
  strong { color: #FFFFFF; }
  code { background: #1B2027; color: #9FD4EE; padding: 2px 6px; border-radius: 4px; }
  pre { background: #0A0C0F; border: 1px solid #2A2E34; border-radius: 8px; }
  table { font-size: 21px; }
  th { background: #1B2027; color: #98A1AB; font-weight: 600; }
  td, th { border-color: #2A2E34 !important; }
  blockquote { border-left: 3px solid #4AB6E8; color: #98A1AB; padding-left: 18px; }
  .pgm { color: #FF2F2A; font-weight: 700; }
  .pvw { color: #24D07A; font-weight: 700; }
  .muted { color: #8B96A4; }
  section.lead { justify-content: center; text-align: left; }
  section.lead h1 { font-size: 62px; }
  footer { color: #6B747E; }
---

<!-- _class: lead -->

# Switcher

## libobs ベースの 2系統 M/E ライブスイッチャー

<span class="muted">Windows / .NET 9 + C++ · 物理コントローラー・Web・ATEM 連携</span>

<span class="muted">2026-07-25</span>

---

## 何を作ったか

**1台の PC で完結するライブ映像スイッチャー**

- <span class="pgm">PGM1/PVW1</span> ・ <span class="pgm">PGM2/PVW2</span> の **2系統独立 M/E**
- 入力: Webcam / NDI / SRT / 画像 / Web ページ / **MIX（合成ソース）**
- 出力: 仮想カメラ / NDI×2（映像+音声）/ HDMI 全画面 ── **PGM ごとに最低1つ必須**
- 操作: オペレーター画面 · キーボード · **物理モジュール(USB-HID)** · Web API
- 音声: 映像ソースに追従（AFV / ON / OFF）、PGM1/2 を別デバイスへ
- 4×4〜6×6 の可変マルチビュー、タリー UDP ブロードキャスト（色は設定可能）

> 映像処理は自前実装せず **libobs（OBS のコア）** に一本化。
> その上の「スイッチャーとしての振る舞い」を独自に作っている。

---

## アーキテクチャ

```
Switcher.App (WPF / .NET 9)
  AppOrchestrator ── PGM/PVW 状態の唯一の権威（全変更を直列化）
     │        │            │              │
  Switcher   Switcher    Switcher      Switcher
  .Web       .Hid        .Atem         .Engine
  REST+WS    USB-HID     ATEM UDP      IVideoEngine
  :8080      Pico 2W     :9910             │ P/Invoke
                                    switcher-engine (C++)
                                      libobs + NDI SDK
```

**3つの操作経路（HID / Web / UI）が同じ関門を通る。**
競合してタリーやバックライトが壊れないため。

---

## 設計の要: 入力は「1回だけ」開く

<div class="muted">共有ソースプール</div>

```
sources: id -> obs_source_t*      ← 各入力は1インスタンス
   ├── PGM1 の scene が参照
   ├── PGM2 の scene が参照
   ├── マルチビューの cell が参照
   └── MIX の scene が参照
```

- 同じカメラを **PGM1 と PGM2 と MIX に同時に** 出せる
- デバイスの二重オープンは不可能 → **単一プロセス構成でのみ成立**
- 2系統 M/E を実現している核心部分

---

## MIX ソース

**複数ソースを自由配置して、1つのソースとして扱う**

- 実体は libobs の **private scene** を共有プールに登録しただけ
- シーンは `obs_source` なので、**特別扱いのコードが 0 行**
  → バス搭載・プレビュータップ・マルチビューセル・モジュール割当がそのまま動く
- メンバーは共有インスタンス
  → 同じカメラが MIX の中と単体のバスに同時に存在できる
- エディタ: 実寸キャンバスでドラッグ移動・角でリサイズ・ライブサムネイル

---

## NDI を自前実装（DistroAV 非依存）

**NDI SDK を直接叩く。OBS 用 NDI プラグインを必要としない。**

| | |
|---|---|
| 入力 | 自前の obs ソース型 `switcher_ndi`（受信スレッド → `obs_source_output_video`） |
| 出力 | 自前の obs 出力型 `switcher_ndi_output`（`raw_video`/`raw_audio` → `send_send_video_v2`/`send_send_audio_v3`） |
| 探索 | SDK の finder。id は NDI 名そのもの |

- **ヘッダは MIT なので同梱** → NDI SDK 無しでソースからビルドできる
- **ランタイムはユーザーが入れた NDI Tools を `LoadLibrary`** → NDI のない PC でも同じバイナリが動く
- 受信は `BGRX_BGRA` を要求 → 色変換コードが不要

---

## 音声: バス = トラック

**映像ソースは音声ソースでもある** → 入力は共通、追加時に自動で音声も付く

| モード | 意味 |
|---|---|
| `AFV` | そのソースがオンエアのバスでだけ聞こえる（MIX 経由も含む） |
| `ON` | 絵が出ていなくても常に聞こえる（BGM・司会マイク） |
| `OFF` | 鳴らさない |

- 実体は `obs_source_set_audio_mixers(src, mask)`。**バス n = libobs のトラック n**
- libobs に AFV の概念はない → **マスクの算出はアプリ側**（バス状態が変わるたびに再計算）
- 出力は **自前の WASAPI レンダラ**。libobs のモニタリングはプロセスに1デバイスしか持てないため
  → PGM1 と PGM2 を別デバイスへ、**1バス→複数デバイス**も可

<span class="muted">リングは常にステレオで持つ。デバイスのチャンネル数で読んだ最初の実装は、8ch のヘッドセットに 2ch を流した瞬間にプロセスごと落ちた。</span>

---

## 落とし穴と対処（1）— libobs 起動

### プラグインを全部ロードしてはいけない

OBS の `obs-plugins` には **Qt 依存のフロントエンドプラグイン** が同居している。
QApplication のないホストで `obs_load_all_modules()` を呼ぶと

```
Must construct a QApplication before a QWidget
```

でプロセスごと落ちる。

→ **必要なプラグインだけを名前指定でロード**（許可リスト方式）

### データパスは `obs_reset_video` より前

内蔵エフェクトが解決できず graphics 初期化が失敗する。

---

## 落とし穴と対処（2）— プレビュー読み出し

### `obs_view` + `video_output_connect` では1フレームも来ない

カスタム view のミックスは raw-active にならず、それを行う `start_raw_video` は非公開。

→ `obs_add_main_render_callback` で
**texrender にオフスクリーン描画 → stagesurface へステージ → 1フレーム後にマップ**

### TAKE はポインタを入れ替える

`program_scene` ↔ `preview_scene` を swap するため、
**一度だけ解決した `obs_source_t*` を持つものは全部張り直す**（タップ・display・マルチビュー）。

さもないと TAKE 直後の PVW が「たった今 PGM になったシーン」を映す。

---

## 落とし穴と対処（3）— UI スレッド

### コンソール全体が固まった

原因は libobs ではなく **WPF 側**。

```csharp
bitmap.WritePixels(rect, frame.Pixels.ToArray(), stride, 0);
//                        ^^^^^^^^^^^^^^^^^^^^ 1080p で 1回 8MB のコピー
```

1画面なら耐えていたものが、**同じセルを描く2画面目**が開いた瞬間に UI スレッドが埋まった。

- バッファを pin して `WritePixels(IntPtr)` → コピー廃止
- `PreviewBitmapCache` で **1トークン 1 tick 1回** だけ変換し全ウィンドウで共有
- `Tick` の購読は1つだけにして、そこから配る

---

## UI: Studio Dock

**OBS Studio の形を借りる** — 既存ユーザーの学習コストを 0 に近づける

```
メニュー / ツールバー
┌──────────────────────────────┬────────────┐
│  PVW  ┃  PGM （選択中 M/E）   │ マルチビュー │
│  トランジションバー            │  （表示専用）│
├────────┬────────┬─────┬──────┴────────────┤
│ Sources│ Buses  │Out  │ Controller        │
└────────┴────────┴─────┴───────────────────┘
```

- **ダーク固定** — 本線モニターの横に座るオペレーターの目を潰さない
- <span class="pgm">赤=PGM</span> / <span class="pvw">緑=PVW</span> は **タリーの意味だけに予約**、操作色は青
- キーボード: `1`-`9` ステージ / `Space` CUT / `A` AUTO / `Tab` M/E 切替

---

## 物理コントローラー連携

**Raspberry Pi Pico 2W / USB-HID**

- 入力レポート `0x01`: `module_present` + SW + VR + `seq`
- 出力レポート `0x02`: モジュールごとの 4×RGB バックライト

### モジュールは自動で増減する

`module_present` ビットマップを監視し、**接続されているモジュールの行だけ** UI に出す。
存在しないハードウェアの割当欄は表示しない。

接続され続けているモジュールの割当は維持されるので、抜き差ししても設定が消えない。

---

## 品質

### `dotnet test` — **333 件**

| 層 | 内容 |
|---|---|
| Contracts | DTO・JSON 往復・列挙表現 |
| Hid | レポート符号化 · エッジ検出 · VR デッドバンド · `seq` ギャップ · バックライト |
| Web | 全バリデータ（境界値・被覆漏れ・重複・範囲外・MIX 自己参照） |
| App | TAKE/削除/モジュール突合 · 結合ガード · タリー判定と MIX 伝播 · AFV ポリシー · 復元順序 |

libobs / NDI を跨ぐ層は **実機検証**（Windows + OBS 必須のため CI 不可）

---

## 現時点の非対応

- **録画 / RTMP 配信**
- **音声のレベル・メーター** — モードとデバイス割当のみ
- **合成マルチビュー出力のタリー枠**
  （オペレーター画面には付く。合成出力側は、バス変更のたびにシーンを作り直す実装が
  グラフィックススレッドを止めたため撤回。領域ごとに色ソースを保持する設計なら実現可能）
- **PiP の不透明度** — 共有ソースにフィルタを掛けると両バスに波及するため保留

---

<!-- _class: lead -->

# まとめ

- 映像は **libobs に一本化**、その上のスイッチャー挙動を独自実装
- **入力を1回だけ開く共有プール**が 2系統 M/E と MIX を可能にした
- **状態変更は1つの関門**を通し、3経路の競合を排除
- **NDI は SDK 直叩き** でプラグイン非依存
- 詰まったのは libobs の非公開 API と、**UI スレッドのコピーコスト**

<span class="muted">詳細: `docs/spec/technical-overview.md`</span>
