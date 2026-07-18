# React + TypeScript + Vite

This template provides a minimal setup to get React working in Vite with HMR and some Oxlint rules.

Currently, two official plugins are available:

- [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react) uses [Oxc](https://oxc.rs)
- [@vitejs/plugin-react-swc](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react-swc) uses [SWC](https://swc.rs/)

## React Compiler

The React Compiler is not enabled on this template because of its impact on dev & build performances. To add it, see [this documentation](https://react.dev/learn/react-compiler/installation).

## Expanding the Oxlint configuration

If you are developing a production application, we recommend enabling type-aware lint rules by installing `oxlint-tsgolint` and editing `.oxlintrc.json`:

```json
{
  "$schema": "./node_modules/oxlint/configuration_schema.json",
  "plugins": ["react", "typescript", "oxc"],
  "options": {
    "typeAware": true
  },
  "rules": {
    "react/rules-of-hooks": "error",
    "react/only-export-components": ["warn", { "allowConstantExport": true }]
  }
}
```

See the [Oxlint rules documentation](https://oxc.rs/docs/guide/usage/linter/rules) for the full list of rules and categories.

## USB→Network ブリッジ（中継モード）の手動確認手順

中継モードは Web Serial 対応の Chromium 系ブラウザ（Chrome / Edge 等）かつ HTTPS または `localhost` 経由でのみ動作する。

1. `npm run dev` で開発サーバーを起動し、Chromium 系ブラウザで `http://localhost:<port>` を開く（スマホ実機の場合は自己署名証明書等でHTTPS化するか、USBデバッグ経由の `localhost` 転送を使う）。
2. メインPC常駐アプリ（PC(8080)）を起動しておく。
3. 「中継モード」タブを開き、USBステータスが「未接続」（灰色）、WSステータスが未接続→接続試行の様子（WebSocketサーバー到達可否に応じ「接続済み」緑 or 「再接続中…」黄）で表示されることを確認する。
4. コントローラーID（`main`/`sub`）を選択する。
5. Pico 2W (P-002) をUSB接続し、「USB接続」ボタンを押してブラウザのポート選択ダイアログでデバイスを選ぶ。USBステータスが「接続中…」→「接続済み」（緑）に遷移することを確認する。
6. Pico 2W のボタンを押し、「受信イベントログ」に `controller_id` / `button_id` のエントリが追加されることを確認する（低遅延であること）。
7. PC側でメインPCアプリが同イベントを受信していることを確認する（ペイロードは親仕様書 §4.1 準拠）。
8. Pico 2W のUSBケーブルを抜き、USBステータスが「再接続中…」（黄）に遷移し、再接続すると自動的に「接続済み」（緑）へ戻ることを確認する。
9. PC側WebSocketサーバーを一時停止/再開し、WSステータスが「再接続中…」→「接続済み」へ自動復帰することを確認する。
10. 「USB切断」ボタンでユーザー操作による切断ができ、USBステータスが「切断」になることを確認する。

## スイッチャー設定UI（設定モード）の手動確認手順

設定モードはメインPC常駐アプリの WebAPI（`GET /api/v1/sources` / `POST /api/v1/config`、ポート8080）に対してポーリング／送信する。

> **既知の連携ギャップ**: タリー（PGM/PVW）は親仕様書 §4.3 上 UDPブロードキャスト（`255.255.255.255:9999`）が正経路であり、ブラウザから直接受信できない。本UIは `GET /api/v1/tally` からの取得を前提にポーリングしているが、この REST エンドポイントは現時点でPC側（`agent-A`/`agent-B`系タスク）に未実装。実装され次第、タリーバッジ（PGM赤/PVW緑）がソース一覧に反映される。未実装の間はポーリングが失敗し続けるだけで、ソース一覧・レイアウト編集・プリセット機能には影響しない。

1. メインPC常駐アプリを起動しておく（`GET /api/v1/sources` / `POST /api/v1/config` が疎通すること）。
2. `npm run dev` で開発サーバーを起動し、ブラウザで開いて「設定モード」タブを選択する。
3. 入力ソース一覧が取得され、チャンネル番号・名前・プロトコル（UVC/NDI/SRT）・接続状態（アイコン＋色）が表示されることを確認する。
4. 任意のチャンネルを選択し、PiPレイアウトエディタが表示されることを確認する。
5. プレビュー枠内の矩形をドラッグして移動し、枠外にはみ出さないようクランプされることを確認する。ドラッグ中の送信はデバウンスされ、PC側へは操作終了後にまとめて反映されることを確認する。
6. 幅・高さ・不透明度・Zオーダー・クロップ（左/上/右/下）の各スライダーを操作し、値の変更が即座にプレビューへ反映され、`POST /api/v1/config` の `pip_settings` に反映されることを確認する（親仕様書 §4.2 準拠）。
7. 複数チャンネルのレイアウトを調整した状態で「シーンプリセット」欄に名前を入力し「保存」する。ページをリロードしても一覧に残ること（`localStorage` 永続化）を確認する。
8. 保存したプリセットの「読込」を押すと、保存時の各チャンネルのレイアウトが復元され、PCへ再送信されることを確認する。「削除」で一覧から消えることを確認する。
9. PC側でタリー連動（PGM切替）を発生させ、（`GET /api/v1/tally` 実装後）ソース一覧のPGM(赤)/PVW(緑)バッジが切り替わることを確認する。
