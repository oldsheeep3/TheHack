# HybridSwitcher — ハイブリッドIP映像スイッチャー

[![CI](https://github.com/NxTEND-THE-HACK/2026-Team-38/actions/workflows/ci.yml/badge.svg)](https://github.com/NxTEND-THE-HACK/2026-Team-38/actions/workflows/ci.yml)

既存機材（ATEM Mini）と自作ハードウェア（自作スイッチングモジュール群 + Pico 2W）、
ネットワーク技術（SRT / NDI / WebAPI）を融合した、次世代のハイブリッドIP映像スイッチャーシステム。

PC上に常駐する制御アプリを「脳」とし、複数プロトコル（UVC / NDI / SRT）の映像入力・PiP合成・
仮想カメラ出力・タリー送出・外部機材（ATEM）の遠隔制御を一元化する。DIYでありながらプロ用
スイッチャーに匹敵する多人数オペレーション（メイン/サブ分業）と拡張性を目指す。

> 全体仕様は [`docs/specs/00-system-overview.md`](docs/specs/00-system-overview.md)（親仕様書）を参照。

## システム構成

```text
[ サブ ATEM Mini ] ──(SRT/HDMI/NDI)──→ [ メインPC (常駐App) ] ──(仮想WebCam/HDMI)──→ [ 配信/録画/OBS等 ]
        ▲                                    ▲        │
        │ (ATEM遠隔制御: UDP 9910)            │        │ (UDPタリー: 9999 ブロードキャスト)
        └────────────────────────────────────┘        ▼
                                             ▲   [ ワイヤレスタリー子機(ESP32) ]  [ 赤/緑 LED ]
                                             │ (WebSocket / WebAPI: 8080)
                                    (USB/HID/Serial)
[ 自作コントローラー: Pico 2W ] ──┬── (USB直結) ──→ メインPC
        │                         └── (USB-OTG) ──→ [ スマホ (Web App) ] ──(Wi-Fi/WebSocket)──→ メインPC
        │
        ├─(内部制御: 後日詳細化)──→ [ 各スイッチングモジュール内マイコン ]
        └─(HDMI DDC/I2Cスニッフィング)──→ [ 物理タリー抽出 ]
```

## コンポーネント

| コンポーネント | 場所 | 技術 | 責務 | 個別README / 仕様書 |
| --- | --- | --- | --- | --- |
| PC常駐アプリ | `src/` | C# / .NET 9 + **libobs**(P/Invoke) | 映像入力デコード・PiP合成・仮想カメラ/HDMI出力・WebAPI/WebSocket・UDPタリー送出・ATEM制御 | [App](src/Switcher.App/README.md) / [仕様](docs/specs/pc-switcher-app.md) / [移行](docs/specs/libobs-engine-migration.md) |
| ネイティブ映像エンジン | `native/switcher-engine/` | C/C++ + libobs (GPLv2) | libobs を駆動する `IVideoEngine` の実体（デュアルM/E=obs_view×2・ソース共有・出力・マルチビュー） | [switcher-engine](native/switcher-engine/README.md) |
| Pico 2W コントローラー | `firmware/pico2w-controller/` | Pico SDK (C/C++) | 物理ボタン入力→PC/スマホ送信・モジュール統括・HDMI DDCタリー抽出 | [Firmware](firmware/pico2w-controller/README.md) / [仕様](docs/specs/pico2w-controller-firmware.md) |
| スマホWebブリッジ | `apps/phone-bridge/` | React / TypeScript | Web Serial/WebUSBでPico 2Wを中継・ブラウザ設定UI | [phone-bridge](apps/phone-bridge/README.md) / [仕様](docs/specs/phone-web-bridge.md) |
| ワイヤレスタリー子機 | (未着手) | ESP32 (Arduino/ESP-IDF) | UDPブロードキャスト受信→赤/緑LED点灯 | [仕様](docs/specs/wireless-tally.md) |

### PC常駐アプリの .NET プロジェクト構成（`src/`）

| プロジェクト | 役割 |
| --- | --- |
| `Switcher.Contracts` | 共通インターフェース・DTO（`IVideoEngine` 抽象を含む契約層） |
| `Switcher.Engine` | `IVideoEngine` の実体。ネイティブ `switcher-engine`(libobs) への P/Invoke ＋ テスト用 `FakeVideoEngine` |
| `Switcher.Atem` | ATEM遠隔制御クライアント（UDP 9910） |
| `Switcher.Web` | WebAPI/WebSocketサーバ・UDPタリーブロードキャスト |
| `Switcher.App` | WPFホスト。上記を1つの常駐プロセスに結線するDI合成ルート |

> **libobs 移行**: 旧 `Switcher.Media`(GStreamer/DirectX 合成) / `Switcher.VirtualCam`(自作仮想カメラ) は廃止し、映像パスは libobs に一本化した（[`docs/specs/libobs-engine-migration.md`](docs/specs/libobs-engine-migration.md)）。ネイティブ `switcher-engine.dll` は Windows + OBS でのみビルド/実行し、`.sln` には含まない（テストは `FakeVideoEngine` 注入でヘッドレス実行）。

## リポジトリ構成

```text
.
├── src/                       # PC常駐アプリ (.NET 9) — HybridSwitcher.sln
├── native/switcher-engine/    # ネイティブ映像エンジン (C/C++ + libobs, CMake, .sln外)
├── tests/                     # 上記各プロジェクトのユニットテスト
├── apps/phone-bridge/         # スマホWebブリッジ & 設定UI (React/TS)
├── firmware/pico2w-controller/# Pico 2W コントローラー / タリー抽出ファーム
├── docs/
│   ├── specs/                 # 仕様書（親仕様書 + コンポーネント別）
│   └── tasks/                 # マルチエージェント実装計画・タスク指示書
├── softswitcher*/             # 既存KiCadハードウェア設計（参考資産）
└── HybridSwitcher.sln         # .NET ソリューション
```

## ビルド & テスト

### PC常駐アプリ（.NET 9）

> このリポジトリでは `dotnet` は PATH 上に無く SDK は `~/.dotnet` にある。以下を前置きする。

```sh
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH

dotnet build HybridSwitcher.sln     # 全プロジェクトのビルド
dotnet test  HybridSwitcher.sln     # 全ユニットテスト
```

`Switcher.App` は WPF (`net9.0-windows`) だが `EnableWindowsTargeting` によりLinuxでもCIでも
**コンパイルは可能**。ただしネイティブ `switcher-engine`(libobs) ランタイムは Windows 限定で、
**実行**には Windows 10/11 と下記の起動順が必要。

### ネイティブ映像エンジン（libobs）のビルド & Windows 実起動 — 起動順

`switcher-engine.dll`（`native/switcher-engine/`）は **Windows + OBS 専用**で `.sln` 外。以下が検証済みの順序
（詳細は [switcher-engine README](native/switcher-engine/README.md) / [L-002 タスク](docs/tasks/agent-L-002-native-libobs-engine.md)）。

**前提**: Windows / Visual Studio 2022（「C++によるデスクトップ開発」）/ CMake ≥ 3.24 / インストール済み OBS（例: 31.0.3）。
以下はすべて **「x64 Native Tools Command Prompt for VS 2022」** から実行する（`cmake`/`cl` に PATH が通る。通常の cmd/PowerShell 不可）。

1. **libobs dev files を用意**（採用 OBS と**同一版**のソースをビルド。インストール済み OBS アプリだけではヘッダ/`obs.lib` が無い）
   ```bat
   git clone --recursive https://github.com/obsproject/obs-studio.git
   cd obs-studio && git checkout 31.0.3 && git submodule update --init --recursive
   cmake --preset windows-x64
   cmake --build build_x64 --config Release --target libobs
   ```
   → `build_x64\libobs\Release\{obs.lib,obs.dll}`、生成ヘッダ `build_x64\config\obsconfig.h`、公開ヘッダ `libobs\`。

2. **`switcher-engine.dll` をビルド**（リポジトリルートで。ヘッダは公開＋生成の2フォルダを `;` 区切りで渡す）
   ```bat
   set OBS=<obs-studio のパス>
   set LIBOBS_INCLUDE_DIR=%OBS%\libobs;%OBS%\build_x64\config
   set LIBOBS_LIB=%OBS%\build_x64\libobs\Release\obs.lib
   native\switcher-engine\build.bat
   ```
   `Switcher.App.csproj` がビルド時に `switcher-engine.dll` を App 出力へ自動コピーする（VS F5/Rebuild でも維持）。

3. **App を起動**（`obs.dll` 等を PATH に通してから）
   - **Visual Studio**: 構成 `Debug`/**`x64`** → `Switcher.App` をスタートアップに → プロパティ「デバッグ」→ デバッグ起動プロファイルUIで環境変数
     `PATH = C:\Program Files\obs-studio\bin\64bit;%PATH%`（版一致のインストール済み OBS）→ **F5**。
   - **コマンドライン**:
     ```bat
     dotnet build src\Switcher.App\Switcher.App.csproj -c Release
     set PATH=%OBS%\build_x64\rundir\Release\bin\64bit;%PATH%
     src\Switcher.App\bin\x64\Release\net9.0-windows\Switcher.App.exe
     ```

> **前提/注意**
> - `obs.dll` + `data\` + プラグイン + `libobs-d3d11.dll` が PATH 上で到達可能、かつ import lib(`obs.lib`) と**同一版**であること（不一致は起動失敗/クラッシュ）。
> - **L-002 未完のうちは `engine_startup` が `obs_reset_video` で失敗**する（libobs データパス未配線。[L-002 タスク](docs/tasks/agent-L-002-native-libobs-engine.md)の「実機検証で判明した実装ポイント」参照）。
> - ネイティブ抜きで App を動かすなら DI（`src/Switcher.App/Composition/ServiceCollectionExtensions.cs`）を `FakeVideoEngine` に差し替える。

### スマホWebブリッジ（React/TS）

```sh
cd apps/phone-bridge
npm install
npm run dev      # 開発サーバ
npm run build    # プロダクションビルド
npm test         # vitest
```

### Pico 2W ファームウェア

I/Oに依存しないロジックはホストの `gcc` でユニットテスト可能（Pico SDK不要）。

```sh
cd firmware/pico2w-controller/test
make             # ビルド + 実行（全テスト green で終了コード0）
```

実機向けクロスビルド（Pico SDK / `arm-none-eabi` ツールチェーン必要）は
[`firmware/pico2w-controller/README.md`](firmware/pico2w-controller/README.md) を参照。

### CI（GitHub Actions）

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) が **全 PR**（タスク → 親Epic の PR も含む）と
`main` / `develop` への push で走る。すべて `ubuntu-latest`。

| ジョブ | 内容 |
| --- | --- |
| `.NET` | `dotnet build -c Release -warnaserror` → `dotnet format --verify-no-changes` → `dotnet test`（結果 `.trx` をアーティファクト化） |
| `phone-bridge` | `npm ci` → `npm run lint`(eslint) → `npm run build`(`tsc -b` 型チェック + vite) → `npm test`(vitest)、`dist/` をアーティファクト化 |
| `firmware` | `firmware/pico2w-controller/test` と `firmware/switcher-module/test` のホストテスト（gcc、`-Werror`） |

ブランチ保護の必須チェックには集約ジョブ **`All checks`** 1つを指定すればよい。

**CI が走るのは個人リポジトリ（public）だけ。** ミラー先の `NxTEND-THE-HACK/2026-Team-38` は
Free org の private リポジトリで Actions のランナーが割り当てられず、どのジョブも 0 ステップの
まま即座に失敗する（ワークフロー側の問題ではなく org の Actions 枠の問題で、修正には org 管理者
による支払い設定・repo の public 化・セルフホストランナー登録のいずれかが要る）。そのため全ジョブに
`if: github.repository_owner != 'NxTEND-THE-HACK'` を付けて向こうでは skip させている。ジョブレベルの
`if` はランナー要求より前に評価されるので、skip されたジョブは失敗扱いにならない。

CI 対象外（ローカル/Windows でのみ検証可能）:

- `native/switcher-engine`（libobs 依存・Windows + OBS 専用・`.sln` 外）と、それを要する App の実起動
- `web/build-preview.ps1`（Windows PowerShell 前提のパス処理）
- `apps/phone-bridge` の Prettier 整形チェック（既存ファイルが未整形のため未導入。`npm run format` で一括整形後に追加可能）

### ミラー（個人リポジトリ → ハッカソン用リポジトリ）

開発を個人アカウントの public リポジトリ（`oldsheeep3/TheHack`）で行い、ハッカソン用 private
リポジトリ（`NxTEND-THE-HACK/2026-Team-38`）へ push のたびに反映する。**片方向**（個人 → ハッカソン用）。

**ミラー先へ書けるトークンは GitHub に預けない。** 実際に push するのは tailnet 上の VPS（`gh`
ログイン済み）で、自分の資格情報を使う。GitHub Actions はその VPS に SSH して起動をかけるだけ
なので、この public リポジトリに置く secret は tailnet への参加情報と SSH 鍵だけで済む。

`.github/` を含め、履歴をそのまま送る（コミット SHA も個人リポジトリ側と一致する）。ミラー先では
CI を走らせないよう、[`ci.yml`](.github/workflows/ci.yml) と [`mirror.yml`](.github/workflows/mirror.yml)
の両方がリポジトリのオーナーを見てジョブを skip する。

```text
手元 ── git push origin ──→ oldsheeep3/TheHack (public)
                              ├ Actions: ci.yml（green になったら↓を起動）
                              └ Actions: mirror.yml
                                  ├ tailscale/github-action で tailnet に参加
                                  └ ssh ──→ VPS: mirror-sync <branch>
                                              ├ 個人リポジトリから fetch（~/mirror/TheHack.git）
                                              └ mirror.sh  … mirror/X を push + PR 作成 + マージ
                                                              ↓
                                              NxTEND-THE-HACK/2026-Team-38 (private)
```

ブランチ `X` を push すると:

0. まず CI が走り、**green で終わったときだけ**ミラーが起動する（`workflow_run` トリガー）。
   赤い場合・CI が走らないブランチは、直さない限りミラーされない
1. [`.github/workflows/mirror.yml`](.github/workflows/mirror.yml) が tailnet に ephemeral ノードとして参加し、
   VPS へ SSH して [`mirror-sync X`](tools/mirror/mirror-sync) を実行する（CI が通った `X` だけが対象）
2. VPS が個人リポジトリから `X` を fetch し、[`tools/mirror/mirror.sh`](tools/mirror/mirror.sh) を呼ぶ
3. ミラー先の **`mirror/X`**（ミラー専用ブランチ）へ push
4. `mirror/X` → `X` の PR を作成（既にオープンならそれを使う）し、**マージコミットでマージする**

起点が GitHub 側にあるので、どのマシンから push しても（GitHub 上で直接編集しても）ミラーされる。
同期するのは CI が通ったそのブランチだけ。全ブランチ + 全タグをまとめて送るのは手動実行のとき。

ミラー先の `main` / `develop` へ直接 push も force push もしない。force は「手元で rebase / amend して
`mirror/X` が fast-forward できなくなった」場合のフォールバックとして `mirror/*` に対してのみ使う。
ミラー先の ref を削除することはないので、ブランチ削除は同期されない。

Actions が使えないとき（ワークフローの失敗、tailnet の不調、初回の一括コピー）は VPS 側で
`mirror-sync` を直接実行すれば追いつく。

| 場所 | もの | 役割 |
| --- | --- | --- |
| GitHub | `.github/workflows/mirror.yml` | tailnet に参加して VPS へ SSH し `mirror-sync` を起動 |
| VPS | `~/mirror/TheHack.git` | 中継用の bare リポジトリ（remote: `personal` / `hackathon`） |
| VPS | `~/mirror/mirror.sh` | `mirror/X` の push・PR 作成・マージ（[`tools/mirror/mirror.sh`](tools/mirror/mirror.sh)） |
| VPS | `~/.local/bin/mirror-sync` | 個人リポジトリから取り込んで同期（[`tools/mirror/mirror-sync`](tools/mirror/mirror-sync)） |

セットアップ:

```sh
# VPS 側（gh でログイン済み・gh auth setup-git 済みであること）
scp -r tools/mirror <vps>:~/mirror-install
ssh <vps> 'bash ~/mirror-install/install-vps.sh'

# CI 用の SSH 鍵を作って公開鍵を VPS に置く
ssh-keygen -t ed25519 -N '' -f mirror_ci
ssh-copy-id -i mirror_ci.pub <vps>

# 手動同期（全ブランチ + 全タグ / ブランチ指定）
ssh sheep 'bash -lc mirror-sync'
ssh sheep 'bash -lc "mirror-sync develop"'
```

個人リポジトリの Settings → Secrets and variables → Actions に登録する secret:

| secret | 中身 |
| --- | --- |
| `TS_CLIENT` / `TS_SECRET` | Tailscale の OAuth クライアント ID / シークレット（scope `auth_keys` write / tag `tag:ci`） |
| `SSH_HOST` / `SSH_USER` | VPS の tailnet アドレス（100.x or MagicDNS 名）とログインユーザー名 |
| `SSH_KEY` | ログインに使う**秘密鍵**（`mirror_ci` の中身。公開鍵は VPS の `authorized_keys` へ） |
| `SSH_KNOWN_HOSTS` | VPS のホスト鍵（`ssh-keyscan <host>` の出力）。任意だが推奨 |

tailnet の ACL で `tag:ci` から VPS の 22/tcp を許可しておくこと。

> - マージは **マージコミット**で行う（squash / rebase merge だと merge-base が進まず、次回の
>   PR に同じコミットが再び載る）。ブランチ保護で弾かれた場合は `gh pr merge --admin` で通す。
> - ミラー先に同名ブランチが無いと PR は作れない（`mirror/X` の push だけ行い notice を出す）。
>   その場合はミラー先で `mirror/X` からブランチを作る。
> - 手動で `mirror-sync` を叩くときは Actions の実行と重ならないようにする（作業リポジトリを共有
>   していて、排他はしていない）。
> - `workflow_run` は**デフォルトブランチにあるワークフローファイル**しか起動しない。`mirror.yml`
>   は `main` にも入っている必要がある（`develop` だけに置いても発火しない）。
> - CI の push トリガーは `main` / `develop` だけなので、作業ブランチを直接 push してもミラーは
>   起動しない（PR を出せば CI は走るが、`pull_request` 由来の実行はミラーの対象外）。作業ブランチを
>   今すぐミラーしたいときは VPS で `mirror-sync <branch>` を叩くか、Actions から手動実行する。
> - ミラーの実行は `concurrency` で 1 本に絞っている。実行中 + 待機中がいる状態でさらに CI が
>   green になると待機中が追い出され、そのブランチは次の CI green まで待つ。
> - `.github/workflows/` も同期するので、VPS の `gh` ログインには **`workflow` スコープ**が要る
>   （`gh auth refresh -h github.com -s workflow`）。無いと push が拒否される。
> - タグは手動同期（引数なしの `mirror-sync`）のときにまとめて送る。ミラー先で既に別のコミットを
>   指しているタグは上書きしない。

## 共通プロトコル / ポート

各コンポーネントは親仕様書 §4 の共通定義に準拠する。

| ポート | プロトコル | 用途 |
| --- | --- | --- |
| 8080 | HTTP/WebSocket | 設定WebAPI・コントローラー入力受信 |
| 9000 | SRT (Listener) | 映像入力（ATEM PGM等） |
| 9999 | UDP broadcast | タリー配信（`255.255.255.255:9999`） |
| 9910 | UDP | ATEM遠隔制御（ATEM純正プロトコル） |

詳細（イベントペイロード・設定変更API・タリーペイロード）は
[`docs/specs/00-system-overview.md`](docs/specs/00-system-overview.md) §4 を参照。

## ドキュメント

- **技術仕様（現行実装）**: [`docs/spec/technical-overview.md`](docs/spec/technical-overview.md)（詳細） /
  [`docs/spec/summary.md`](docs/spec/summary.md)（要約） / [`docs/slides/switcher.md`](docs/slides/switcher.md)（Marp）
- **配布サイト**: [`web/`](web/) — LP + 使い方（`web/build-preview.ps1` で1ファイルのプレビューを生成）
- **ライセンス**: GPL-2.0-or-later（[`LICENSE`](LICENSE) / [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)）
- **仕様書**: [`docs/specs/`](docs/specs/) — 親仕様書 + コンポーネント別詳細仕様
- **実装計画 / タスク**: [`docs/tasks/`](docs/tasks/) — マルチエージェント実装のオーケストレーション計画とタスク指示書
