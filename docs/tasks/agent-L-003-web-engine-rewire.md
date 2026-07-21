---
name: agent-L-003-web-engine-rewire
status: planning
pid: 
agent_cli: sonnet
---

# 実装指示書: Switcher.Web を IVideoEngine 経由へ配線替え（Phase 1 / 並列）

## 概要
`Switcher.Web` のエンドポイント処理が現在依存している `ICompositorEngine` / `IInputSourceManager` を、**新抽象 `IVideoEngine`(L-001) 経由に置き換える**。**外部WebAPI/WebSocket 契約（スネークケースJSON・エンドポイントパス・ペイロード）は一切変更しない**。テストは `FakeVideoEngine`(L-001) 注入で緑を維持。

## 前提条件（依存タスク）
- **L-001 完了**（`IVideoEngine`/DTO/`FakeVideoEngine` が利用可能）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.Web/`
- `tests/Switcher.Web.Tests/`（既存 sln 登録。フェイクを `FakeVideoEngine` へ）

## 実装ステップ
1. **依存の棚卸し**: 現状 `ICompositorEngine`/`IInputSourceManager` を参照している箇所を置換対象として特定（`WebHostServices.cs` / `WebHost.cs` / `ISwitcherConfigService.cs` / `Endpoints/SourcesEndpoint.cs` 等）。
2. **`ISwitcherConfigService` の実装/利用を `IVideoEngine` へ**:
   - ソース CRUD（`GET/POST/DELETE /api/v1/sources`）→ `IVideoEngine.ListSources/AddSourceAsync/RemoveSourceAsync/UpdateSourceAsync`。
   - プログラム/TAKE（`/api/v1/program` 等）→ `SetPreview`/`Take`。
   - 出力（`PUT /api/v1/outputs`）→ `ApplyOutputsAsync`（`OutputSink` 契約はそのまま）。
   - マルチビュー（`PUT /api/v1/multiview`）→ `ApplyMultiviewAsync`（region/cells 後方互換）。
   - デバイス列挙（`GET /api/v1/devices/{type}` / `srt/setup`）→ 既存 `IDeviceQueryService`（実体は L-004 で `Switcher.Engine`/App が配線）。**Web は IF 参照のみ**。
3. **DI 登録の変更**: Web ホスト内の DI で、映像系サービスを `IVideoEngine`（App から注入される実体、テストは `FakeVideoEngine`）に依存する形へ。Web は**実体プロジェクト（Media/VirtualCam/Engine）を `ProjectReference` しない**（現状同様 Contracts のみ参照を維持）。
4. **バリデータ**: `OutputsRequestValidator`/`MultiviewLayoutValidator` 等の既存バリデーションは**そのまま維持**（契約不変）。
5. **テスト更新**: `Switcher.Web.Tests` のフェイク（現状 `ICompositorEngine`/`IInputSourceManager` のスタブ）を **`FakeVideoEngine`** に置き換え、**全エンドポイントの入出力JSON（スネークケース・ステータス）が不変**であることを担保。既存の API 契約テストを壊さない。

## 完了条件 / 検証コマンド
```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
```
- Web が `ICompositorEngine`/`IInputSourceManager` に非依存になり、`IVideoEngine` 経由で全エンドポイントが動作。
- **WebAPI 契約（パス・JSON・ステータス）が完全不変**（既存契約テスト緑）。
- Web は Contracts のみ参照（Media/VirtualCam/Engine を直接参照しない）。
- 全スイート緑・警告0。

## 技術的な補足 / レビュー観点
- **契約不変が最優先**。エンドポイントの外形（スネークケース・後方互換 cells/regions）を変えない。
- Web は抽象のみ依存（App が実体を注入）。これにより L-005 の Media/VirtualCam 削除時に Web が壊れない。
- 検証: `.claude/review-patterns.md`「インターフェース互換性」「境界」「テスト（契約）」。

## 参照
- 仕様: `docs/specs/libobs-engine-migration.md` §2.3/§2.5
- 既存: `src/Switcher.Web/`（`WebHostServices.cs`/`WebHost.cs`/`ISwitcherConfigService.cs`/`Endpoints/SourcesEndpoint.cs`）
- 計画: `docs/tasks/orchestration-plan-libobs-migration.md`
