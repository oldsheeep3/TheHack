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

親仕様書の改訂（2系統ME/4x4マルチビュー/出力割当/モジュール割付/Picoネットワーク設定）に合わせ、
「設定モード」タブは現時点ではプレースホルダ表示のみで、詳細UIは後続タスクで実装される。
新しい `protocol/types.ts` の DTO（`SourceDefinition` / `ProgramRequest` / `MultiviewConfig` /
`OutputsConfig` / `ModulesConfig` / `PicoNetworkConfig`）と `apiClient.ts` の対応メソッドは
本タスクで先行整備済み。

旧・単一チャンネル向けの `src/config/`（`ConfigTab` / `PipEditor` / プリセット等）は旧
`ConfigChangeRequest`（`POST /api/v1/config`）を用いた実装のまま残置しており、`App.tsx` からは
参照されていない（後続タスクが新UIへ置き換える）。
