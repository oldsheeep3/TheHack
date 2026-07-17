---
name: agent-A-001-foundation-contracts
status: doing
pid: 153144
agent_cli: sonnet
---

# 実装指示書: ソリューション基盤 & 共有Contracts（Phase 0 / 直列ゲート）

## 概要
.NET 9 ソリューション `HybridSwitcher.sln` を新規作成し、全モジュールが依存する共有プロジェクト `Switcher.Contracts`（DTO・インターフェース・プロトコル定義・共通定数）を確定させる。あわせて後続タスク用のプロジェクトスタブを `.sln` に一括登録し、並列フェーズでの `.sln` 競合を根絶する。

## 前提条件（依存タスク）
- なし（本タスクが全タスクの前提）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `HybridSwitcher.sln`（Epicブランチ直下）
- `src/Switcher.Contracts/`（本体実装）
- `src/Switcher.Media/`, `src/Switcher.Web/`, `src/Switcher.Atem/`, `src/Switcher.VirtualCam/`, `src/Switcher.App/` は **空のプロジェクトスタブ（.csproj + Placeholder.cs のみ）** を作成し `.sln` に登録するところまで（中身は各担当タスクが実装）。

## 実装ステップ
1. `dotnet new sln -n HybridSwitcher` を作成。
2. `src/Switcher.Contracts`（`classlib`, `net9.0`）を作成。`<Nullable>enable</Nullable>` と `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` を設定。
3. 後続5プロジェクトのスタブを作成し `.sln` に追加（型は後で置換される Placeholder のみ）:
   - `Switcher.Media` / `Switcher.Web` / `Switcher.Atem` / `Switcher.VirtualCam`（`classlib`）
   - `Switcher.App`（`WPF` アプリ: `dotnet new wpf`。Windows前提のため `net9.0-windows`）
   - 各スタブに `Switcher.Contracts` への ProjectReference を付与。
4. **共有Contractsを定義**（親仕様書 §4 に厳密準拠）。以下を最低限含める:
   - `ProtocolConstants`（定数）: `WebPort = 8080`, `SrtListenPort = 9000`, `TallyBroadcastPort = 9999`, `AtemPort = 9910`, `TallyBroadcastAddress = "255.255.255.255"`。
   - 入力プロトコル列挙: `enum SourceProtocol { Uvc, Ndi, Srt }`。
   - ソース状態: `enum SourceStatus { Connected, Disconnected, Error }`。
   - `record SourceInfo(int Channel, string Name, SourceProtocol Protocol, string? Resolution, SourceStatus Status)`。
   - PiP設定: `record PipSettings(bool Enabled, int X, int Y, int Width, int Height, double Opacity, /* Zorder/Crop */ int ZOrder, CropRect? Crop)`、`record CropRect(int Left, int Top, int Right, int Bottom)`。
   - 設定変更API DTO（§4.2）: `record ConfigChangeRequest(int TargetChannel, SourceProtocol SourceType, string? SourceUrl, PipSettings? PipSettings)`。
   - コントローラー入力イベント（§4.1）: `record ButtonEvent(string ControllerId, int ButtonId, long Timestamp)`（WSラッパ `record WsEnvelope(string Event, ButtonEvent Data)`）。
   - タリーペイロード（§4.3）: `record TallyState(IReadOnlyList<int> ActivePgm, IReadOnlyList<int> ActivePvw)`。
   - JSONは `System.Text.Json` を用い、フィールド名は仕様のスネークケース（`active_pgm` 等）に一致させる（`JsonPropertyName` またはポリシー設定）。
5. モジュール境界のインターフェースを定義（各実装タスクが実装する契約）:
   - `IInputSourceManager`: ソースの追加/削除/一覧取得/状態通知（`event`）。
   - `ICompositorEngine`: PGM/PVW の合成、PiPレイアウト適用、TAKE、フレーム取得（仮想カメラ供給用）。
   - `ITallyBroadcaster`: `Publish(TallyState)`。
   - `IAtemController`: `Connect(string ip)`, `SendCommand(...)`（コントローラー入力→ATEMコマンド変換の受け口）。
   - `IVirtualCameraOutput`: `Start()`, `SubmitFrame(...)`, `Stop()`。
   - `IControllerInputSink`: `Enqueue(ButtonEvent)`（Web層→アプリ中核へ直列キュー投入）。
6. `dotnet build HybridSwitcher.sln` がエラーゼロで通ることを確認し、コミット。

## 完了条件 / 検証コマンド
- `dotnet build HybridSwitcher.sln` がエラー0・警告0で成功。
- `Switcher.Contracts` の公開型が上記を網羅し、JSONシリアライズが親仕様書 §4 のフィールド名と一致（簡易ユニットテストで往復シリアライズ確認）。
- 5つのプロジェクトスタブが `.sln` に登録済みで各々 build 可能。

## 技術的な補足 / レビュー観点
- **本タスクの成果物IF/DTOは以降のタスクの契約となる。** 破壊的変更を避けるため、命名・型・JSON名を仕様と厳密一致させること。
- 公開APIは必要最小限（`internal` を活用）。Nullable を明示。
- 検証: `.claude/review-patterns.md`「型・静的検査」「命名・可読性」「インターフェース互換性」。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md` §2, `docs/specs/00-system-overview.md` §4
