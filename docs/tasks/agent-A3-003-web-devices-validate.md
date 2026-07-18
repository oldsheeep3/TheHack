---
name: agent-A3-003-web-devices-validate
status: done
pid: 
agent_cli: sonnet
---

# 実装指示書: Web — デバイス列挙/SRT情報 API & 出力/マルチビュー バリデータ拡張（Phase 1）

## 概要
`Switcher.Web` を**加算的に拡張**し、(a) `GET /api/v1/devices/{type}`・`GET /api/v1/srt/setup` の新エンドポイント（`IDeviceQueryService` へ委譲）、(b) NDI出力を含む `OutputsRequestValidator` の拡張、(c) region対応の `MultiviewLayoutValidator` 拡張を実装する（仕様 §4.1/§4.2）。Web は Media/VirtualCam の実装を参照せず**IF経由**（テストはフェイク）。

## 前提条件（依存タスク）
- `agent-A3-001-contracts-mvout`（`OutputSink`(NDI1/NDI2)/`OutputAssignment.NdiName`/`MultiviewRegion`/`MultiviewLayoutNormalizer`/`IDeviceQueryService`/`DeviceInfo`/`SrtSetupInfo` 確定）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.Web/`（本体拡張）
- `tests/Switcher.Web.Tests/`（テスト拡張。`.sln` は編集しない）

## 実装ステップ
1. **デバイス列挙エンドポイント**（`Endpoints/DevicesEndpoint.cs` 新規）:
   - `GET /api/v1/devices/{type}`（`type`=`webcam`|`ndi`）→ `IDeviceQueryService.EnumerateAsync` 委譲、`DeviceInfo[]` を返す。未知 `type` は 400、SRT指定は 400（列挙対象外）。列挙0件は空配列＋200（App が導線表示）。
   - DI: `WebHost`/`WebHostServices` に `IDeviceQueryService` を注入配線（実体は App が渡す。Web内はIFのみ）。
2. **SRTセットアップ情報エンドポイント**（`Endpoints/SrtSetupEndpoint.cs` 新規）:
   - `GET /api/v1/srt/setup` → `IDeviceQueryService.GetSrtSetupAsync` 委譲、`SrtSetupInfo` を返す。
3. **エンドポイント登録**（`WebHostEndpoints.cs`）: 上記2つを既存の登録集約に追加（既存 `/api/v1/*` の慣習に合わせる）。
4. **出力バリデータ拡張**（`OutputsRequestValidator.cs`）:
   - 既存規則（各 sink 最大1回・HDMI は `display_id` 必須）を維持。
   - 追加: **NDI1/NDI2 の `source` は PGM1/PGM2 のみ**（それ以外はエラー）、`ndi_name` は空文字不可（未指定=null は許容し、App/実装が既定名で補完）。VCAM/HDMI 割当への回帰がないこと。
5. **マルチビューバリデータ拡張**（`MultiviewLayoutValidator.cs`, 後方互換）:
   - **`cells` 形式**（16セル）: 既存規則を維持。
   - **`regions` 形式**: `MultiviewLayoutNormalizer.ToRegions` 済み領域が **(i) すべて矩形かつグリッド内、(ii) 重なりなし・隙間なしでグリッドを完全被覆、(iii) 各 `content` が `PGM1/PGM2/PVW1/PVW2/SRC:<id>/EMPTY`** を満たすこと。違反は具体的メッセージ。
   - `cells`/`regions` の**どちらも未指定 or 両方指定**は 400。
6. `Switcher.Web.Tests` を拡張: devices エンドポイント（webcam/ndi/未知/SRT指定/0件, フェイク `IDeviceQueryService`）、srt/setup エンドポイント、`OutputsRequestValidator`（NDI source制約・ndi_name空文字・HDMI既存規則）、`MultiviewLayoutValidator`（region完全被覆OK/重なりNG/隙間NG/非矩形NG・cells旧形式OK・両方指定NG）。既存 `WebApiV2Tests`/各Validatorテストを維持。

## 完了条件 / 検証コマンド
- 以下が成功（警告0）:
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
  ```
- `GET /api/v1/devices/{type}`・`GET /api/v1/srt/setup` が IF 委譲で動作（フェイクで検証）。
- `OutputsRequestValidator` が NDI 制約を含めて検証、`MultiviewLayoutValidator` が region 完全被覆＋cells後方互換を検証。
- 既存 `Switcher.Web.Tests` を含む全スイートがグリーン。

## 技術的な補足 / レビュー観点
- Web は Media/VirtualCam の実装型を参照しない（`IDeviceQueryService` のみ）。テストはフェイク注入で OS/実装非依存。
- 正規化は Contracts の `MultiviewLayoutNormalizer` を使用（Web独自の再実装をしない）。
- バリデータの後方互換（`cells` 形式）を必ずテストで担保。
- 検証: `.claude/review-patterns.md`「入力検証」「インターフェース互換性」「テスト網羅」。

## 参照
- 仕様: `docs/specs/multiview-output-revision.md` §4.1/§4.2
- 契約: `docs/tasks/agent-A3-001-contracts-mvout.md`
- 既存: `src/Switcher.Web/`（`Endpoints/` / `WebHostEndpoints.cs` / `WebHost.cs` / `WebHostServices.cs` / `OutputsRequestValidator.cs` / `MultiviewLayoutValidator.cs` / `ISwitcherConfigService.cs`）
