# phone-bridge — スマホ経由Webブリッジ & 設定WebUI

スマホのブラウザからアクセスし、以下2機能を1つのモダンなWebUIで提供する（React / TypeScript）。

1. **操作/接続モード**: メインPC常駐アプリ（`:8080` の HTTP/WebSocket）へ**同一LAN/Wi-Fiのネットワーク経由**で接続する（USB Web Serial 中継は廃止）。接続先ホスト/ポートは手動入力＋`localStorage`保存（mDNSホスト名の手入力も許容）。ネットワーク到達性/WebSocket接続状態を可視化し、自動再接続（指数バックオフ）する。
2. **設定モード（スイッチャー設定UI）**: メインPCの WebAPI（`/api/v1/*`, ポート8080）を通じて、ソース管理・2系統ME(PGM1/PGM2)・4x4マルチビュー・出力割当・モジュール割付・Picoネットワーク設定を編集する（詳細UIは後続タスクで実装）。

- 親プロジェクト: [`../../README.md`](../../README.md)
- 仕様書: [`docs/specs/phone-web-bridge.md`](../../docs/specs/phone-web-bridge.md)（共通プロトコルは親仕様書 §4）

## 技術スタック

- React / TypeScript（Vite）
- Tailwind CSS（ダークモード標準）
- WebSocket（PC常駐アプリ :8080）、REST（`/api/v1/*`）、同一LAN/Wi-Fi経由のネットワーク接続

## 開発

```sh
npm install
npm run dev      # 開発サーバ（Vite）
npm run build    # tsc -b && vite build
npm run lint     # ESLint
npm test         # vitest
```

## 操作/接続モードの手動確認手順

1. メインPC常駐アプリ（`:8080`）を起動しておく。スマホ/PCが同一LAN/Wi-Fiに接続していることを確認する。
2. `npm run dev` で開発サーバーを起動し、ブラウザ（またはスマホ実機）で開く。
3. 「操作/接続モード」タブが既定で開き、「接続先PC」欄にデフォルト値（現在のホスト名）が入っていることを確認する。
4. 接続先ホストにPCのIPアドレス（例: `192.168.1.50`）または手入力のmDNSホスト名（例: `switcher-pc.local`）とポート（既定 `8080`）を入力し「接続先を適用」を押す。
5. ネットワークステータスが「確認中…」（黄）→「到達可能」（緑）に、WSステータスが「接続中…」→「接続済み」（緑）に遷移することを確認する（`GET /api/v1/sources` 疎通と WebSocket 接続）。
6. ページをリロードし、入力した接続先が `localStorage` から復元され再接続されることを確認する。
7. PC側を一時停止し、ネットワークステータスが「到達不可」（赤）、WSステータスが「再接続中…」（黄）に遷移することを確認する。
8. PC側を再開すると、ネットワーク/WSステータスがそれぞれ自動的に「到達可能」/「接続済み」（緑）へ復帰する（指数バックオフ再接続）ことを確認する。

## 設定モード（スイッチャー設定UI）について

親仕様書の改訂（2系統ME/4x4マルチビュー/出力割当/モジュール割付/Picoネットワーク設定）に対応した
詳細UIを実装済み。`protocol/types.ts` の DTO（`SourceDefinition` / `ProgramRequest` /
`MultiviewConfig` / `OutputsConfig` / `ModulesConfig` / `PicoNetworkConfig`）と `apiClient.ts` の
対応メソッドを直接利用し、独自スキーマは作らない。

- `src/config/SourcesPanel.tsx` … ソース一覧（`GET /api/v1/sources`）・追加/更新/削除（NDI/WEBCAM/SRT
  判別フォーム）・表示順（`localStorage` 保持、`sourceOrder.ts`）・2系統タリーバッジ。
- `src/config/ProgramPanel.tsx` … PGM1/PGM2 切替、レイヤー追加/削除/並べ替え、選択レイヤーの
  PiPレイアウトを既存 `PipEditor`/`layout.ts` で編集（`debounce.ts` で送信抑制）、TAKEボタン。
- `src/config/MultiviewPanel.tsx` … 16セルの割当編集（`multiview.ts` の純粋関数でセル整形/検証）。
- `src/config/OutputsPanel.tsx` … 出力割当編集（Webcam `VCAM1` / HDMI `HDMI1..3` / NDI `NDI1..3`、
  Webcamは最大1・HDMI/NDIは各最大3・合計最大6）。
- `src/config/ModulesPanel.tsx` … モジュール割付（`MAX_MODULES`=8 上限）＋VR割当先編集。
- `src/config/PicoNetworkPanel.tsx` … Pico の Wi-Fi/BT 設定編集。
- `src/config/PresetsPanel.tsx` … 拡張後の設定（2系統プログラム＋マルチビュー）のプリセット保存/読込
  （既存 `presets.ts` の `PresetStore` 抽象を流用、`localStorage` 保管）。

### 設定モードの手動確認手順

1. メインPC常駐アプリ（`:8080`）を起動しておく（`/api/v1/*` が疎通すること）。
2. 「設定モード」タブを開く。「入力ソース」に `GET /api/v1/sources` の内容が3秒毎に反映されることを確認する。
3. 「+ ソース追加」から NDI/WEBCAM/SRT いずれかのソースを追加し（`POST /api/v1/sources`）、一覧に
   反映されることを確認する。「編集」で更新（`PUT`）、「削除」で削除（`DELETE`）できることを確認する。
   ↑/↓ ボタンで表示順を入れ替え、ページをリロードしても順序が保持される（`localStorage`）ことを確認する。
4. 「2系統ME プログラム＋PiP編集」で PGM1/PGM2 タブを切り替え、レイヤーを追加し、プレビューの
   ドラッグ/スライダーで PiP（位置/サイズ/クロップ/不透明度/Zオーダー）を編集する。連続操作中は
   デバウンスされ、操作が止まってから `POST /api/v1/program` が送出されることを確認する。
   「TAKE」ボタンで `take: true` が送出されることを確認する。
5. 「4x4 マルチビュー割当」で各セルに `PGM1`/`PGM2`/`PVW1`/`PVW2`/ソース/`EMPTY` を割り当て、
   `PUT /api/v1/multiview` が送出されることを確認する。
6. 「出力割当」で各出力（既定は `VCAM1`＋`HDMI1`）へ PGM を割り当て（HDMIは `display_id`/カーソル非表示/全画面も）、
   適用ボタンで `PUT /api/v1/outputs` が送出されることを確認する。
7. 「モジュール割付」でモジュールを追加（最大8）し、`src1`/`src2` を論理ソースへ紐付け、VR割当先を
   指定して適用（`PUT /api/v1/modules`）できることを確認する。
8. 「Pico ネットワーク設定」で SSID/パスワード/Bluetooth を入力し送信すると `PUT /api/v1/pico/network`
   が送出されることを確認する。
9. ソース一覧・マルチビューのタリーバッジ（PGM1/PGM2は赤系、PVW1/PVW2は緑系）が
   `GET /api/v1/tally`（2系統ペイロード）のポーリングに応じて切り替わることを確認する。
10. 「シーンプリセット」で現在の2系統プログラム＋マルチビューを保存し、別内容に変更後「読込」で
    復元されること（画面状態と `POST /api/v1/program` ×2 / `PUT /api/v1/multiview` の再送出）を確認する。
