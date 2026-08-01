# phone-bridge — スイッチャー設定WebUI

スマホ/PCのブラウザから、メインPC常駐アプリの設定を編集する**設定専用**クライアント（React / TypeScript）。
本番運用の操作（TAKE、タリー監視）はPCコンソールとモジュールパネルが担当し、本UIは行わない。

1. **接続先タブ**: メインPC常駐アプリ（`:8080`）へ**同一LAN/Wi-Fi経由**で接続する。接続先ホスト/ポートは
   手動入力＋`localStorage`保存（mDNSホスト名の手入力も可）。到達性/WebSocket状態を可視化し、
   自動再接続（指数バックオフ）する。
2. **設定タブ**: PCの WebAPI（`/api/v1/*`, ポート8080）で、ソース管理・2系統ME(PGM1/PGM2)の構成・
   マルチビュー・出力割当・音声出力・モジュール割付・ATEM・Picoネットワーク設定を編集する。

- 親プロジェクト: [`../../README.md`](../../README.md)
- 仕様書: [`docs/specs/phone-web-bridge.md`](../../docs/specs/phone-web-bridge.md)（共通プロトコルは親仕様書 §4）

## 技術スタック

- React / TypeScript（Vite）
- Tailwind CSS（ダークモード標準）
- REST（`/api/v1/*`）、同一LAN/Wi-Fi経由のネットワーク接続

## 開発

```sh
npm install
npm run dev      # 開発サーバ（Vite）
npm run build    # tsc -b && vite build
npm run lint     # ESLint
npm test         # vitest
```

## 配信（実運用）

PC常駐アプリの Kestrel が **API と同一オリジンでこのUIを配信する**。ブラウザから
`http://<PCのIP>:8080/` を開けばそのまま使える。

- `npm run build` で作った `dist/` を、`Switcher.App.csproj` の `CopyPhoneBridgeUi` ターゲットが
  アプリ出力の `wwwroot/` へコピーする（`setup.sh` も `npm ci` の後にビルドまで行う）。
- `WebHost` は実行ファイル隣の `wwwroot/` を見つけたときだけ静的配信する。無ければAPIは通常どおり
  起動し、ページだけ返らない（CI や C# しか触らない開発者のために必須にしていない）。
- クライアントの既定接続先は**ページ自身のオリジン**なので、`WebPort` を既定から変えても追随する。
  「接続先」タブでの手入力は、別の場所から開いた場合の上書き用。
- 同一オリジンなので `:8080` に CORS を開ける必要はない（0.0.0.0 で待ち受けるホストに対して、
  LAN内の任意のページからのAPI呼び出しを許可せずに済む）。

> `npm run dev`（Vite の `:5173`）はUIの見た目確認用。API はクロスオリジンになりブラウザに
> ブロックされるため、実際の設定操作は `:8080` から開いて行う。

## 設定タブの構成

`protocol/types.ts` の DTO と `apiClient.ts` のメソッドを直接使い、独自スキーマは作らない。
各パネルは**まずPCの現在値を読み出してから編集する**（各 `PUT` はテーブル全体を置換するため、
読み出さずに適用すると既存設定を潰してしまう）。

- `src/config/SourcesPanel.tsx` + `SourceEditor.tsx` … ソース一覧（`GET /api/v1/sources`）と
  設定内容（`GET /api/v1/sources/definitions`）、追加/更新/削除。NDI / ウェブカメラ / SRT /
  静止画 / Webページ / ミックス の6種別と音声モード（OFF/ON/AFV）に対応。
  ウェブカメラ・NDIは `GET /api/v1/devices/{type}` から選択、SRTは `GET /api/v1/srt/setup` の
  受信ガイダンスと Listener/Caller 切替（モードはURLの `mode=` に載る → `srtUrl.ts`）。
- `src/config/ProgramPanel.tsx` … PGM1/PGM2 のレイヤー構成と PiP レイアウト編集
  （`PipEditor`/`layout.ts`、`debounce.ts` で送信抑制）。TAKEは設定専用のため置かない。
- `src/config/MultiviewPanel.tsx` … 任意の行×列（4〜6）とセル結合に対応した割当編集
  （`multiview.ts` の純粋関数で結合/分割/検証、`grid`＋`regions` 形式で送信）。
- `src/config/OutputsPanel.tsx` … 出力割当（Webcam `VCAM1` / HDMI `HDMI1..3` / NDI `NDI1..3`）の
  追加・削除・編集。Webcam最大1・HDMI/NDI各最大3・合計6、NDI送信名も設定可。
- `src/config/AudioPanel.tsx` … プログラムバス→再生デバイスのルーティング
  （`GET /api/v1/audio/devices`、`GET/PUT /api/v1/audio/outputs`）。
- `src/config/ModulesPanel.tsx` … モジュール割付（`MAX_MODULES`=8）＋VR割当先。
- `src/config/AtemPanel.tsx` … ATEM の接続設定・ネットワーク検索・モジュールSW→ATEMコマンド割付・
  ATEMのストリーミング出力設定（`/api/v1/atem`, `/atem/discover`, `/atem/streaming`）。
- `src/config/PicoNetworkPanel.tsx` … Pico の Wi-Fi/BT 設定（資格情報のため書き込み専用）。
- `src/config/PresetsPanel.tsx` … 2系統プログラム＋マルチビューのプリセット保存/読込
  （`presets.ts` の `PresetStore`、`localStorage` 保管）。

### タリーを表示しない理由

PCはタリーを UDP ブロードキャスト（`255.255.255.255:9999`, 親仕様書 §4.3）でのみ配信しており、
ブラウザはこれを受信できない。REST のミラーは存在しないため、以前あった `GET /api/v1/tally` の
ポーリングは常に空振りしていた。本UIは設定専用なのでタリー表示ごと削除している。

## 手動確認手順

1. `npm run build` 後にPC常駐アプリをビルド・起動し、スマホ等のブラウザで `http://<PCのIP>:8080/` を開く。
   このUIが表示されること（＝`wwwroot` が配信されていること）を確認する。
2. 「接続先」タブでネットワークステータスが（手入力なしで）「到達可能」（緑）になることを確認する。
   別ホストから開いた場合は IP/ポートを入力し「接続先を適用」→ リロード後も `localStorage` から
   復元されること、PC停止時に「到達不可」（赤）へ遷移し再開で復帰することを確認する。
3. 「設定」タブを開き、各パネルに**PCの現在の設定**が表示されることを確認する
   （出力割当・音声出力・モジュール割付・マルチビュー・ATEM）。
4. 「+ ソース追加」で各種別を追加できること、ウェブカメラ/NDIでデバイスがプルダウンに出ること、
   SRTで受信URLのガイダンスが出ることを確認する。「編集」で既存の設定値が復元されることを確認する。
5. 「マルチビュー割当」で行数/列数を変え、セルを複数選択して「結合」「分割」ができ、
   `PUT /api/v1/multiview` が `grid`＋`regions` 形式で送出されることを確認する。
6. 「出力割当」で HDMI/NDI を追加・削除し（上限で追加ボタンが無効になること）、適用で
   `PUT /api/v1/outputs` が送出されることを確認する。バスに出力が無い状態では適用できないことも確認する。
7. 「音声出力」でバスを再生デバイスへ割り当て、適用で `PUT /api/v1/audio/outputs` が送出されることを確認する。
8. 「ATEM 遠隔制御」で「ネットワークを検索」→ 見つかった機器を選択 → 適用（`PUT /api/v1/atem`）でき、
   スイッチ割付が保存され再読込で復元されることを確認する。
9. 「Pico ネットワーク設定」で SSID/パスワード/Bluetooth を送信すると `PUT /api/v1/pico/network` が
   送出されることを確認する。
10. 「シーンプリセット」で現在の2系統プログラム＋マルチビューを保存し、変更後「読込」で復元される
    （`POST /api/v1/program` ×2 / `PUT /api/v1/multiview` の再送出）ことを確認する。
