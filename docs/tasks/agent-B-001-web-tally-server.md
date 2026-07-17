---
name: agent-B-001-web-tally-server
status: planning
pid:
agent_cli: sonnet
---

# 実装指示書: 内蔵WebAPI/WebSocketサーバー & UDPタリー配信（Phase 1）

## 概要
ASP.NET Core Kestrel をアプリ内蔵で起動し、設定変更REST・ソース一覧REST・コントローラー入力WebSocketを提供する。あわせて PGM/PVW 状態をLANにUDPブロードキャストするタリー配信サーバーを実装する。`Switcher.Contracts` の DTO/IF を用いる。

## 前提条件（依存タスク）
- `agent-A-001-foundation-contracts` が `done`。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `src/Switcher.Web/` のみ。`.sln` / `Switcher.Contracts` は編集しない。

## 実装ステップ
1. `Microsoft.AspNetCore` を参照し、内蔵Kestrelホスト（`WebApplication`）をライブラリとして起動できる `WebHost` クラスを実装。待受ポート `ProtocolConstants.WebPort`(8080)。
2. REST エンドポイント:
   - `POST /api/v1/config` … body=`ConfigChangeRequest`。バリデーション後、注入された中核サービス（`ICompositorEngine`/`IInputSourceManager` を束ねるファサード IF）へ委譲。入力検証エラーは 400 で明示。
   - `GET /api/v1/sources` … `IInputSourceManager.GetSources()` を JSON 配列で返す。
3. WebSocket エンドポイント（例 `/ws`）:
   - `WsEnvelope`/`ButtonEvent`（§4.1）を受信し、`IControllerInputSink.Enqueue()` に投入。
   - **入力は単一キューで直列化**し、メイン/サブ同時操作の競合・取りこぼしを防ぐ（`Channel<ButtonEvent>` 等）。目標遅延5ms以下。
4. `TallyBroadcaster : ITallyBroadcaster` を実装:
   - `UdpClient` で `255.255.255.255:9999` に `TallyState`(§4.3) を JSON 送出。`EnableBroadcast=true`。
   - 状態変化時に即時送出＋定期冗長送出（例4回/秒）をバックグラウンドタスクで。停止/破棄で確実にクリーンアップ。
5. 依存注入: 中核サービスIFはコンストラクタ注入（App統合タスクが実体を差し込む）。Web層は実体を知らない。
6. テスト: config のバリデーション、`/sources` シリアライズ、タリーJSONのフィールド名（`active_pgm`/`active_pvw`）一致、入力キューの直列性。`TestServer`(WebApplicationFactory) 利用可。
7. `dotnet build`・`dotnet test` を通しコミット。

## 完了条件 / 検証コマンド
- `dotnet build src/Switcher.Web/Switcher.Web.csproj` エラー0。
- `dotnet test` green（REST/WS/タリーのユニット・統合テスト）。
- タリーUDPペイロードが親仕様書 §4.3 のスキーマ・フィールド名に一致。

## 技術的な補足 / レビュー観点
- **並行性・競合**: WS入力の直列化キュー、タリー送出タスクのキャンセル・破棄パス（`.claude/review-patterns.md`「並行性」「リソース管理」）。
- **セキュリティ**: 外部入力(config/WS)のバリデーション、例外を握り潰さない、ローカル用途でも過剰公開しない。
- UDPブロードキャストはOS/NIC設定・ファイアウォールの影響を受ける旨コメント。
- 中核サービスへの依存は IF 経由に限定（Web が Media/Atem 実体を直接参照しない）。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md` §2.4, §2.5, §4
