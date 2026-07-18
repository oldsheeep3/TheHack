---
name: agent-A2-001-contracts-v2
status: done
pid: 349325
agent_cli: sonnet
---

# 実装指示書: 共有Contracts拡張 v2 & Hidスタブ登録（Phase 0 / 直列ゲート）

## 概要
改訂仕様（`pc-switcher-app.md` 改訂版 / 親 §4）に合わせて、共有プロジェクト `Switcher.Contracts` を**加算的に拡張**する。OBS的ソース定義（`SourceDefinition`）、2系統ME（PGM1/PGM2 + PVW1/PVW2）のプログラム/合成DTO、4x4マルチビュー、出力割当、HID入出力レポートモデル（§4.1）、ATEMマッピングDTO（§4.2）、モジュール割付DTO（§4.2）、2系統タリー（`TallyState` 拡張）を定義し、`ProtocolConstants` を更新する。あわせて **新規プロジェクト `Switcher.Hid`（本体は空スタブ）と `Switcher.Hid.Tests`（空スタブ）を作成して `.sln` に登録**し、後続フェーズでの `.sln` 競合を根絶する。**本タスクの成果IF/DTOは以降の全タスクの契約**であり、確定後は変更しない。

## 前提条件（依存タスク）
- なし（本タスクが改訂全タスクの前提 / Phase0 直列ゲート）。
- 既存 `Switcher.Contracts`（初版で `status: done`）を土台とする。既存の公開型（`SourceInfo` / `TallyState` / `PipSettings` / `ConfigChangeRequest` / 各 `Interfaces/*`）は**可能な限り壊さず加算**する。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.Contracts/`（本体拡張）
- `tests/Switcher.Contracts.Tests/`（往復シリアライズテスト追加）
- `HybridSwitcher.sln`（**新規2プロジェクトの登録のみ**）
- `src/Switcher.Hid/`（**空スタブのみ**: `.csproj` + `Placeholder.cs`。`Switcher.Contracts` への ProjectReference を付与。本体は A2-004 が実装）
- `tests/Switcher.Hid.Tests/`（**空スタブのみ**: xUnit `.csproj` + ダミーテスト1本。`Switcher.Hid` / `Switcher.Contracts` 参照）

## 実装ステップ
1. **ソース定義（OBS的自由追加, §4.2）** を追加:
   - `enum SourceType { Ndi, Webcam, Srt }`（JSON: `"NDI"|"WEBCAM"|"SRT"`。既存 `SourceProtocol` は残置し、必要なら相互変換ヘルパを提供）。
   - `record NdiConfig(string SourceName)` / `record WebcamConfig(string DeviceId, string? Format)` / `record SrtConfig(string Url, int LatencyMs)`。
   - `record SourceDefinition(string Id, string Name, SourceType Type, NdiConfig? Ndi, WebcamConfig? Webcam, SrtConfig? Srt)`（JSONフィールド: `id/name/type/ndi/webcam/srt`。`ndi.source_name`, `webcam.device_id`, `webcam.format`, `srt.url`, `srt.latency_ms`）。
   - `SourceInfo` は**互換維持のため既存を残しつつ**、`Id`（string）・`Order`（並べ替え順）を持つ拡張DTO（例: `SourceInfo` に `string? Id` を optional 追加、または新 `record SourceState(...)`）を追加。整数 `Channel` は §4.3 の `active_*` 用の序数として保持。
2. **2系統プログラム/合成DTO（§4.2 `POST /api/v1/program`）** を追加:
   - `enum ProgramBus { Pgm1, Pgm2 }`（JSON: `"PGM1"|"PGM2"`）。
   - `record ProgramLayer(string SourceId, PipSettings Pip)`（JSON: `source_id`, `pip`）。
   - `record ProgramRequest(ProgramBus Bus, IReadOnlyList<ProgramLayer> Layers, bool Take)`（JSON: `bus`, `layers`, `take`）。
   - 既存 `PipSettings`（`x_position`/`y_position`/`width`/`height`/`opacity`/`z_order`/`crop`）は**そのまま再利用**（フィールド名は既に §4.2 と一致）。
3. **4x4 マルチビュー（§4.2 `PUT /api/v1/multiview`）**:
   - `record MultiviewLayout(IReadOnlyList<string> Cells)`（16要素。値: `"PGM1"|"PGM2"|"PVW1"|"PVW2"|"SRC:<id>"|"EMPTY"`。文字列で保持し検証は Web 側）。JSONフィールド: `cells`。
4. **出力割当（§4.2 `PUT /api/v1/outputs`）**:
   - `enum OutputSink { Vcam1, Vcam2, Hdmi }`（JSON: `"VCAM1"|"VCAM2"|"HDMI"`）。
   - `enum OutputSource { Pgm1, Pgm2 }`（JSON: `"PGM1"|"PGM2"`。将来 PVW も許すなら文字列保持でも可）。
   - `record OutputAssignment(OutputSink Sink, OutputSource Source, int? DisplayId, bool? HideCursor, bool? Fullscreen)`（JSON: `sink`, `source`, `display_id`, `hide_cursor`, `fullscreen`）。
   - `record OutputsRequest(IReadOnlyList<OutputAssignment> Outputs)`（JSON: `outputs`）。
5. **HID入出力レポートモデル（§4.1）** — バイト列⇔構造体の**純粋な型**（I/Oは含めない、A2-004が実装）:
   - `record ModuleSwitchState(bool Pgm1Src1, bool Pgm1Src2, bool Pgm2Src1, bool Pgm2Src2)`（下位4bit: b0..b3 の並びを厳密一致）。
   - `record ModuleVrState(byte VrSrc1, byte VrSrc2)`。
   - `record HidInputReport(byte ModulePresent, IReadOnlyList<ModuleSwitchState> Switches, IReadOnlyList<ModuleVrState> Vrs, byte Seq)`（Report ID `0x01`。長さは `1 + MAX_MODULES + 2*MAX_MODULES + 1`）。
   - `record BacklightColor(byte R, byte G, byte B)`。
   - `record HidOutputReport(byte ModuleIndex, IReadOnlyList<BacklightColor> Colors)`（Report ID `0x02`。4灯分 = 12バイト）。
   - HIDレポートIDと定数（`HidInputReportId = 0x01`, `HidOutputReportId = 0x02`）は `ProtocolConstants` へ。
6. **ATEMマッピングDTO（§4.2 `PUT /api/v1/atem`, `POST /api/v1/atem/command`）**:
   - `record AtemButtonMapping(string ControllerId, int ModuleIndex, string Switch, string Action, int MixEffect, int Source)`（JSON: `controller_id`, `module_index`, `switch`, `action`, `mix_effect`, `source`）。
   - `record AtemConfig(bool Enabled, string Ip, IReadOnlyList<AtemButtonMapping> Mappings)`（JSON: `enabled`, `ip`, `mappings`）。
   - `record AtemCommandRequest(string Action, int MixEffect, int Source)`（単発コマンド用）。
   - **注**: 既存 `Switcher.Atem` の `AtemAction`/`ButtonCommandMapping` は変更しない。ここでは Web境界のJSON DTOのみ定義し、App層で既存型へ変換する。
7. **モジュール割付DTO（§4.2 `PUT /api/v1/modules`）**:
   - `enum VrTarget { Transition, Opacity, /* 汎用 */ Assignable }`（JSON: `"transition"|"opacity"|...`。将来拡張のため文字列保持でも可）。
   - `record ModuleSourceBinding(string? SourceId, string VrTarget)`（JSON: `source_id`, `vr_target`）。
   - `record ModuleMapping(int Index, ModuleSourceBinding Src1, ModuleSourceBinding Src2)`（JSON: `index`, `src1`, `src2`）。
   - `record ModulesRequest(IReadOnlyList<ModuleMapping> Modules)`（JSON: `modules`）。
8. **2系統タリー（§4.3）** — `TallyState` を拡張:
   - 新 `record TallyStateV2(IReadOnlyList<int> ActivePgm1, IReadOnlyList<int> ActivePgm2, IReadOnlyList<int> ActivePvw1, IReadOnlyList<int> ActivePvw2)`（JSON: `active_pgm1/2`, `active_pvw1/2`）を追加。
   - 既存 `TallyState(ActivePgm, ActivePvw)` は**互換のため残置**（`ITallyBroadcaster.Publish` の下流影響を最小化）。`ITallyBroadcaster` へ2系統用のオーバーロード/新メソッド（例: `Publish(TallyStateV2)`）を加算するか、`TallyState` を2系統へ差し替えるかは、A2-003/A2-006 の改修範囲を最小化する形を採用し、本ファイル内に**採用した設計を明記**する。
9. **Pico ネットワーク設定DTO（§4.6, `PUT /api/v1/pico/network`）**:
   - `record PicoNetworkConfig(string? WifiSsid, string? WifiPassword, string? ControllerId, bool BluetoothEnabled)`（JSON: `wifi_ssid`, `wifi_password`, `controller_id`, `bluetooth_enabled`）。資格情報を含むため、ログ出力しない旨をコメントで明示。
10. **`ProtocolConstants` 更新**: 親 §4.0 のシステム定数を追加（`MaxModules = 8`, `SwitchesPerModule = 4`, `VrsPerModule = 2`, `BacklightsPerModule = 4`, `ProgramBusCount = 2`）＋ HIDレポートID（手順5）。既存ポート定数（`WebPort`, `SrtListenPort=9000`, `TallyBroadcastPort=9999`, `AtemPort=9910`）は保持。
11. **JSON**: すべて `ProtocolJsonOptions.Default`（snake_case + `JsonStringEnumConverter`）でシリアライズ可能に。enum のJSON値が §4 のトークン（`"NDI"`, `"PGM1"` 等 大文字）と一致するよう、必要に応じ `[EnumMember]`/`JsonStringEnumConverter` の命名を検証（`JsonNamingPolicy.SnakeCaseLower` は enum 値名に対しどう働くか要確認 — 大文字トークンは `JsonPropertyName` 相当の指定 or カスタムコンバータで担保）。
12. **`Switcher.Hid` / `Switcher.Hid.Tests` の空スタブを作成**し `.sln` に登録（`classlib` net9.0 / xUnit）。`Switcher.Hid` に `Switcher.Contracts` への ProjectReference を付与。`<Nullable>enable</Nullable>` と `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` を設定。
13. **往復シリアライズテスト**を `Switcher.Contracts.Tests` に追加し、§4 のフィールド名（`active_pgm1`, `source_id`, `x_position`, `device_id`, `latency_ms`, `mix_effect`, `vr_target`, `hide_cursor` 等）と enum トークンの一致を検証。
14. `dotnet build HybridSwitcher.sln` がエラー0・警告0で通り、`dotnet test HybridSwitcher.sln` が**新スタブ含め全スイート**実行されることを確認してコミット。

## 完了条件 / 検証コマンド
- 以下がエラー0・警告0で成功:
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
  ```
- `Switcher.Hid.Tests` が `dotnet test HybridSwitcher.sln` の実行対象に**含まれている**（`.sln` 登録済み）。
- 追加した全DTOが `ProtocolJsonOptions.Default` で往復し、JSONフィールド名/enumトークンが親 §4.1〜§4.6 と一致（往復テストがグリーン）。
- 既存の公開型・既存テスト（`Switcher.Contracts.Tests` 等）が破壊されていない（全既存テストがグリーン）。

## 技術的な補足 / レビュー観点
- **本タスクの成果物IF/DTOは以降のタスクの契約。** 命名・型・JSON名を仕様と厳密一致させ、確定後は変更しない（要変更時は Opus 親が調整）。
- 後方互換を最優先。既存 `TallyState` / `SourceInfo` / 各 `Interfaces/*` は破壊せず加算。破壊が避けられない場合は**採用設計と下流の改修点を本ファイルへ追記**し、A2-002/003/006 の指示と齟齬が出ないようにする。
- 資格情報（Wi-Fiパスワード等）はログに出さない設計コメントを付す。
- enum の大文字JSONトークン（`"PGM1"`, `"NDI"`）は snake_case ポリシーと衝突しうる。実装前に小さな検証テストで挙動を固めること。

## 採用設計メモ（実装時追記）

- **2系統タリー（手順8）**: `TallyStateV2` を `TallyState.cs` に加算したのみで、`ITallyBroadcaster` インターフェースへの `Publish(TallyStateV2)` オーバーロード追加は**見送った**。
  - 理由: `ITallyBroadcaster` に新メンバーを足すと、既存実装 `src/Switcher.Web/TallyBroadcaster.cs`（本タスクの編集許可範囲外）がインターフェースを満たさなくなり、`dotnet build` がエラーになる。本タスクは `src/Switcher.Contracts/` 等のみ編集可のため、実装側の追従が不可能。
  - 対応: `TallyStateV2` 型のみ契約として先に定義し、`ITallyBroadcaster` へのメソッド追加とその実装（`Switcher.Web` 側の追従）は当該ディレクトリを編集範囲に持つ後続タスク（A2-003 想定）に委譲する。A2-003 は `Publish(TallyStateV2)` オーバーロード追加または `TallyState`→`TallyStateV2` 差し替えのいずれかを選び、`Switcher.Web.TallyBroadcaster` を追従修正すること。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md`（改訂版）§2.1〜§2.8, `docs/specs/00-system-overview.md` §4.0〜§4.6
- 既存: `src/Switcher.Contracts/`（`SourceInfo` / `TallyState` / `PipSettings` / `ConfigChangeRequest` / `ProtocolConstants` / `ProtocolJsonOptions` / `Interfaces/*`）
- 前計画: `docs/tasks/agent-A-001-foundation-contracts.md`
