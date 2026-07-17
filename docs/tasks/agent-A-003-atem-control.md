---
name: agent-A-003-atem-control
status: planning
pid:
agent_cli: sonnet
---

# 実装指示書: ATEM遠隔制御クライアント（Phase 2）

## 概要
ATEMプロトコル（UDP 9910）のクライアントとして振る舞い、メイン側コントローラーのボタン入力を ATEM コマンド（シーン/入力切替等）に変換して指定IPのATEM Miniへ中継する。`Switcher.Contracts` の `IAtemController` を実装する。

## 前提条件（依存タスク）
- `agent-A-001-foundation-contracts` が `done`。

## 対象ディレクトリ（このタスクで編集してよい範囲）
- `src/Switcher.Atem/` のみ。`.sln` / `Switcher.Contracts` は編集しない。

## 実装ステップ
1. ATEM接続方式を選定・実装:
   - 第一候補: 既存C#ライブラリ（AtemSharp 等）を NuGet/参照で利用。ライセンス・入手性を確認しコメントに明記。
   - 入手できない場合: `UdpClient` で ATEM ハンドシェイク（`ProtocolConstants.AtemPort`=9910）＋必要コマンド（Program/Preview入力切替, Cut/Auto）に限定した最小実装。
2. `AtemController : IAtemController` を実装:
   - `Connect(string ip)`: 接続・ハンドシェイク・再接続（バックオフ）・接続状態通知。
   - ボタン→コマンドの**マッピングテーブル**（`controller_id`+`button_id` → ATEM操作）を設定可能に。App統合タスクがコントローラー入力を本コントローラーへルーティングする前提で、`SendCommand`/変換メソッドを公開。
3. スレッド安全性: 送受信・状態を排他制御。切断時はコマンドをドロップまたはキューイング方針を明確化。
4. テスト: ボタン→コマンド変換テーブルの写像、接続状態遷移、パケット組み立て（自前実装時はバイト列のユニットテスト）。ネットワーク実機部分は抽象化しモック可能に。
5. `dotnet build`・`dotnet test` を通しコミット。

## 完了条件 / 検証コマンド
- `dotnet build src/Switcher.Atem/Switcher.Atem.csproj` エラー0。
- `dotnet test` green。
- `IAtemController` を仕様通り実装（App統合から中継利用できるIF）。

## 技術的な補足 / レビュー観点
- **リソース/並行性**: UDPソケット・受信ループ・タイマーの破棄、再接続の競合回避（`.claude/review-patterns.md`）。
- 外部ライブラリ採用時はバージョン固定とライセンス確認。自前実装時はプロトコル仕様の出典をコメント。
- ボタン→コマンド写像はマジックナンバーを避け設定/定数化。

## 参照
- 仕様: `docs/specs/pc-switcher-app.md` §2.6, `docs/specs/00-system-overview.md` §4.4
