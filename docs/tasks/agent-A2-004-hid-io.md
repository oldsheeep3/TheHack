---
name: agent-A2-004-hid-io
status: done
pid: 405347
agent_cli: sonnet
---

# 実装指示書: USB-HID 入出力 & バックライト算出（新規 Switcher.Hid）（Phase 2）

## 概要
新規プロジェクト `Switcher.Hid`（A2-001 で空スタブ作成・`.sln` 登録済み）の本体を実装する。Pico 2W の **USB-HID 入力レポート（Report ID `0x01`, 親 §4.1）** を購読し、集約された全モジュールのSW状態・VR値を取得、**押下エッジを状態差分で検出**し VR を反映する。現在の PGM1/PGM2・PVW 状態から**各スイッチのバックライト色を算出**し、**HID出力レポート（Report ID `0x02`）** で Pico へ返す（§2.6, §4.5）。**パース/エッジ検出/色算出などの純粋部分はホスト（Windows非依存）でユニットテスト可能**にし、実HIDデバイスI/Oは薄いアダプタに隔離する。

## 前提条件（依存タスク）
- `agent-A2-001-contracts-v2`（`HidInputReport` / `HidOutputReport` / `ModuleSwitchState` / `ModuleVrState` / `BacklightColor` / `ProtocolConstants`（`MaxModules`, `HidInputReportId=0x01`, `HidOutputReportId=0x02` 等）が確定）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `src/Switcher.Hid/`（本体実装。`.csproj` スタブは A2-001 作成済み。`.sln` は編集しない）
- `tests/Switcher.Hid.Tests/`（テスト実装。`.sln` 登録は A2-001 済み）

## 実装ステップ
1. **HIDレポートのパース/シリアライズ（純粋関数）**:
   - `HidReportParser.ParseInput(ReadOnlySpan<byte>) -> HidInputReport`: レイアウト（親 §4.1）を厳密実装 —
     - offset 0: `module_present` ビットマップ、
     - offset 1..1+MAX_MODULES: SW状態（1バイト/モジュール, 下位4bit `b0=PGM1×SRC1, b1=PGM1×SRC2, b2=PGM2×SRC1, b3=PGM2×SRC2`）、
     - 次の 2×MAX_MODULES: VR値（`VR_SRC1`, `VR_SRC2` 各0..255）、
     - 末尾1: `seq`。
   - `HidReportParser.SerializeOutput(HidOutputReport) -> byte[]`: offset0 `module_index`, offset1..12 4灯分RGB（**GRB変換はモジュール側が行うため PC 側は RGB 順で送る**）。
2. **エッジ検出 & 入力直列化**:
   - `SwitchEdgeDetector`: 直前フレームとの差分から各SWの押下/離しエッジ（rising/falling）を導出。`seq` のギャップ（取りこぼし）を検出しログ/フラグ化。
   - VRは適度なデッドバンド/間引き（`VrFilter`）で変化のみ通知。
   - 出力は「モジュール×SW×エッジ」「モジュール×VR×値」の**イベント列**（Contractsの型 or Hid内部型）。実際の操作（PGM載せ降ろし/ATEM中継）への変換は A2-006 が担う。ここは入力の正規化まで。
3. **バックライト色算出（純粋関数）**:
   - `BacklightCalculator.Compute(programState, previewState, moduleMappings) -> IReadOnlyList<HidOutputReport>`: 各モジュール4灯（`PGM1×SRC1, PGM1×SRC2, PGM2×SRC1, PGM2×SRC2`）に対し、現在のバス状態から色を決定（例: PGMオン=赤 / PVW=緑 / 選択可=淡色 / 非該当=消灯）。ポリシーは差し替え可能に（`IBacklightPolicy`）。
   - 入力の PGM/PVW 状態・モジュール割付は Contracts DTO（`ModuleMapping` 等）を引数に取り、Hid は状態を保持しない純粋変換に徹する。
4. **HIDデバイスI/Oアダプタ（薄く隔離）**:
   - `IHidDevice`（`Open`/`ReadInputReport`/`WriteOutputReport`/`Close`）を定義し、純粋部分（1〜3）から分離。実装は Windows の HID API（例: `HidSharp` 等のライブラリ or P/Invoke）で `SharedMemoryVirtualCameraDevice` 同様の薄いラッパにする。
   - `HidInputService`（購読ループ）: `IHidDevice` から入力レポートを読み、パース→エッジ検出→正規化イベントを `event`/コールバックで公開。低遅延（数ms）を意識したブロッキング読み or 専用スレッド。
   - `HidBacklightService`: 算出結果を `IHidDevice.WriteOutputReport` でモジュール単位に送出。
   - `IHidDevice` は Contracts ではなく Hid 内部で定義し、テストではフェイク実装を注入（App からは `HidInputService`/`HidBacklightService` の公開IFのみ利用）。
5. **（任意）無線代替の枠**（§2.6）: HID不可時に同等イベントを供給できるよう、入力ソース抽象を `IHidDevice` と同レベルに用意（実装は将来拡張枠でよい。設計上の差し込み点だけ用意）。
6. `Switcher.Hid.Tests` を充実: レポートパース往復（§4.1 のビット並び・オフセットを固定値で検証）、エッジ検出（差分/`seq`ギャップ）、VRデッドバンド、バックライト算出（PGM=赤/PVW=緑等のポリシー）。**実HIDデバイス不要**でグリーンにする。A2-001 が作った**ダミーテストは本タスクの実テストに置換**する。

## 完了条件 / 検証コマンド
- 以下が成功（警告0）:
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build HybridSwitcher.sln
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test  HybridSwitcher.sln
  ```
- `HidReportParser` が親 §4.1 のバイトレイアウト（オフセット・下位4bit並び・VR・seq）に厳密一致（往復テストがグリーン）。
- エッジ検出・VRフィルタ・バックライト算出の純粋部分が実デバイス無しでテストされ全グリーン。
- `Switcher.Hid.Tests` が `dotnet test HybridSwitcher.sln` で実行される（A2-001 の `.sln` 登録が有効）。

## 技術的な補足 / レビュー観点
- **純粋/副作用の分離**が肝。パース・エッジ・色算出は状態を持たない純関数にし、I/Oは `IHidDevice` の背後へ。既存 `Switcher.VirtualCam` の「純粋変換（`Nv12FrameConverter`）+ 薄い device アダプタ（`SharedMemoryVirtualCameraDevice`）」構成を手本にする。
- 低遅延（SW→PC反映 数ms, §4.1/§5）。読み取りは専用スレッド、割当変換への受け渡しはロックフリー or 最小ロック。
- 直列化: 出力イベントは順序保証。`seq` 取りこぼしはログ＋可能なら再同期。
- Windows依存（HID API）は本体I/Oアダプタのみに閉じ込め、テストプロジェクトは OS 非依存でビルド/実行できるようにする（`net9.0` classlib、UI/WPF 依存を持ち込まない）。
- 検証: `.claude/review-patterns.md`「純粋性/副作用分離」「並行性」「境界値（ビット並び/オフセット）」。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md` §2.6 / `docs/specs/00-system-overview.md` §4.0, §4.1, §4.5, §4.6
- 手本: `src/Switcher.VirtualCam/FrameConversion/Nv12FrameConverter.cs`, `src/Switcher.VirtualCam/Devices/SharedMemoryVirtualCameraDevice.cs`
- 契約: `docs/tasks/agent-A2-001-contracts-v2.md`
