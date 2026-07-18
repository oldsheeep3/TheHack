---
name: agent-A2-003-web-api-v2
status: doing
pid: 369845
agent_cli: sonnet
---

# 実装指示書: WebAPI v2（新エンドポイント群 & 2系統タリー）（Phase 1）

## 概要
`Switcher.Web` を**加算的に拡張**し、親仕様書 §4.2 の改訂・拡張エンドポイント群（sources CRUD / program / multiview / outputs / modules / atem / pico-network）を提供する。あわせて `TallyBroadcaster` を **2系統タリー（`active_pgm1/2`, `active_pvw1/2`、§4.3）** へ拡張する。既存の `WebHost` / `WebHostEndpoints` / `Endpoints/*` / `ControllerInputQueue` / `ISwitcherConfigService` を**再利用**し、WebSocketボタン経路（`ControllerWebSocketEndpoint`）は HID 経路（A2-004）に置換されるため**互換維持で残置しつつ非推奨コメント**を付す。

## 前提条件（依存タスク）
- `agent-A2-001-contracts-v2`（`SourceDefinition` / `ProgramRequest` / `MultiviewLayout` / `OutputsRequest` / `ModulesRequest` / `AtemConfig` / `AtemCommandRequest` / `PicoNetworkConfig` / `TallyStateV2` 等が確定）。
- App 中核サービスIF（`ISwitcherConfigService`）への追加メソッドは A2-001 で確定した契約に従う。**具体的な実装配線は A2-006**（本タスクは Web 層のルーティング/バリデーション/ハンドラと、注入されたサービスIFの呼び出しまで）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.Web/`（本体拡張）
- `tests/Switcher.Web.Tests/`（テスト拡張。`.sln` 登録済みのため `.sln` は編集しない）

## 実装ステップ
1. **エンドポイント追加**（`WebHostEndpoints.Map` に集約登録。既存 `/api/v1/config`・`/api/v1/sources`・`/ws` は残置）:
   - ソース: `GET /api/v1/sources`（既存拡張: `SourceInfo[]`/拡張DTO）、`POST /api/v1/sources`（`SourceDefinition`）、`PUT /api/v1/sources/{id}`、`DELETE /api/v1/sources/{id}`。
   - プログラム: `POST /api/v1/program`（`ProgramRequest`: バス指定レイヤ適用 + TAKE）。
   - マルチビュー: `PUT /api/v1/multiview`（`MultiviewLayout`, 16セル）。
   - 出力: `PUT /api/v1/outputs`（`OutputsRequest`）。
   - モジュール: `PUT /api/v1/modules`（`ModulesRequest`）。
   - ATEM: `PUT /api/v1/atem`（`AtemConfig`）、`POST /api/v1/atem/command`（`AtemCommandRequest`）。
   - Pico: `PUT /api/v1/pico/network`（`PicoNetworkConfig`）。
2. **ハンドラ実装**: 各エンドポイントは注入された中核サービスIF（`ISwitcherConfigService` の拡張。A2-001が定義）へ委譲。`Endpoints/` に `ProgramEndpoint` / `MultiviewEndpoint` / `OutputsEndpoint` / `ModulesEndpoint` / `AtemEndpoint` / `PicoNetworkEndpoint` を追加。既存 `SourcesEndpoint` を CRUD へ拡張。
3. **バリデーション**: 既存 `ConfigChangeRequestValidator` に倣い、`multiview` は16セル固定・トークン妥当性（`PGM1|PGM2|PVW1|PVW2|SRC:<id>|EMPTY`）、`outputs` は sink/source 妥当性、`program` は `bus` と `layers` の妥当性を検証。不正は 400 を返す。
4. **2系統タリーへ拡張**（`TallyBroadcaster`）:
   - A2-001 が採用した設計（`TallyStateV2` 追加 or `TallyState` 差し替え）に従い、`active_pgm1/2` / `active_pvw1/2` を `255.255.255.255:9999` にUDP配信。
   - 既存の「状態変化で即時＋~4回/秒の冗長送出」ループと `SerializePayload`（`ProtocolJsonOptions.Default`）は**再利用**。ペイロードのJSONフィールド名が §4.3 と一致すること。
   - 互換: 単系統時は `active_pgm1`/`active_pvw1` のみ使用可（§4.3 注記）。
5. **WebSocketボタン経路の置換**（supersede）: HID 経路（A2-004）が正となるため、`ControllerWebSocketEndpoint` / `ControllerInputQueue` は**互換維持で残置**しつつ、XMLドキュメント/コメントで「HID経路に置換済み、無線代替時のみ利用」と明記。削除はしない（仕様 §2.6 の任意無線代替に該当）。
6. `Switcher.Web.Tests` を拡張: 新エンドポイントのルーティング/バリデーション/正常系（`TestWebHostFactory`/`TestServer` の既存パターンを踏襲）、2系統タリーの `SerializePayload` フィールド名検証（既存 `TallyBroadcasterSerializationTests` を拡張）。

## 完了条件 / 検証コマンド
- 以下が成功（警告0）:
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
  ```
- 新エンドポイント（sources CRUD / program / multiview / outputs / modules / atem / atem-command / pico-network）が `TestServer` 経由で 2xx/400 を正しく返す。
- 2系統タリーのUDPペイロードが `active_pgm1/2`・`active_pvw1/2`（§4.3）で往復一致。
- 既存 `Switcher.Web.Tests`（`WebApiTests` 等）が破壊されず全グリーン。

## 技術的な補足 / レビュー観点
- **再利用最優先**: `WebHost`（Kestrel構成）/ `WebHostServices`（DI）/ `WebHostEndpoints`（ルート集約）/ `TallyBroadcaster`（送信ループ）を土台に加算。
- 本タスクは Web境界（ルーティング・DTO受領・バリデーション・サービスIF呼び出し）まで。**中核状態の実配線は A2-006**。サービスIFに未実装メソッドがある場合は A2-001 の契約に沿ってIFを利用し、実装は App 側に委ねる（Web は具象を参照しない）。
- 入力直列化（メイン/サブ同時操作安全, §5）は最終的に App 中核のロックで担保。Web はスレッドセーフに委譲するのみ。
- 認証・LAN公開: `0.0.0.0` バインドは既存踏襲。必要に応じ簡易認証の枠を用意（仕様 §2.5「必要に応じ認証」。実装は最小/将来拡張枠でよい）。
- 検証: `.claude/review-patterns.md`「API境界」「入力検証」「インターフェース互換性」。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md` §2.5, §2.7, §2.8 / `docs/specs/00-system-overview.md` §4.2, §4.3, §4.6
- 既存: `src/Switcher.Web/`（`WebHost` / `WebHostServices` / `Endpoints/WebHostEndpoints` / `Endpoints/SourcesEndpoint` / `Endpoints/ConfigEndpoint` / `Endpoints/ControllerWebSocketEndpoint` / `TallyBroadcaster` / `ControllerInputQueue` / `ConfigChangeRequestValidator` / `ISwitcherConfigService`）
- 契約: `docs/tasks/agent-A2-001-contracts-v2.md`

## 採用設計メモ（実装時追記）

- **`ISwitcherConfigService`（`src/Switcher.Web/`）を拡張**し、sources CRUD（`AddSourceAsync`/`UpdateSourceAsync`/`RemoveSourceAsync`）・`ApplyProgramAsync`・`ApplyMultiviewAsync`・`ApplyOutputsAsync`・`ApplyModulesAsync`・`ApplyAtemConfigAsync`・`SendAtemCommandAsync`・`ApplyPicoNetworkConfigAsync` を追加した。`ICompositorEngine`/`IInputSourceManager`（`Switcher.Contracts`）自体は A2-002 の採用設計メモの通り拡張されていないため、Web層の全新規エンドポイントはこの `ISwitcherConfigService` 一枚を経由して中核へ委譲する形に統一。
- **`ITallyBroadcaster`（`src/Switcher.Contracts/Interfaces/`, 対象ディレクトリ外）に `Publish(TallyStateV2)` オーバーロードを追加**し、`src/Switcher.Web/TallyBroadcaster.cs` を追従実装した（送信ループ/`SerializePayload`は`byte[]`ペイロード保持に一般化して再利用、`Publish(TallyState)`はそのまま維持）。A2-001の採用設計メモが本タスクへ明示的に委譲した決定であり、他タスクとの対象ディレクトリ競合がないことを確認して実施（A2-002/A2-004/005/006はこのファイルを編集範囲に含まない）。
- **`src/Switcher.App/Orchestration/AppOrchestrator.cs`（対象ディレクトリ外）に新インターフェースメンバーの暫定実装（`NotImplementedException`スタブ）を追加**: `ISwitcherConfigService` を拡張すると唯一の実装クラスである `AppOrchestrator` がビルドを壊すため、完了条件の「`dotnet build`/`dotnet test` がソリューション全体で警告0・成功」を満たす目的の最小限の措置。実配線は `agent-A2-006-app-integration-v2` が担当（同タスクの実装ステップに明記済み）。
- **WebSocketボタン経路の非推奨化**: `ControllerWebSocketEndpoint` / `ControllerInputQueue` は削除・改変せず、XMLドキュメントの `<remarks>` に「HID経路（A2-004）へ置換済み、無線代替時のみ利用」を追記するに留めた。ルーティング（`/ws`）・実装ロジックは無変更。
- **認証枠（§2.5「必要に応じ」）は見送り**: 完了条件・検証コマンドに認証関連の要求がなく、既存コードにも認証ミドルウェアの土台がないため、本タスクでは追加していない（将来必要になった時点で別タスク化を推奨）。
