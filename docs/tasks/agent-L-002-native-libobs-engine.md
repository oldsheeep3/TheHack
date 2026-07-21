---
name: agent-L-002-native-libobs-engine
status: doing
pid: 
agent_cli: opus
---

# 実装指示書: ネイティブ libobs エンジン `switcher-engine`（Phase 1 / 並列）

## 概要
`engine.h`(L-001 で確定) を **libobs 上に実装**するネイティブ C/C++ ライブラリ `switcher-engine` を新設する。デュアルM/E を **`obs_view`×2** で構成し、物理入力ソースを**1回だけ開いて両系統から共有参照**する。ソースは **OBS 標準バンドルモジュール**のみ、NDI は DistroAV 前提。仕様 §2.1〜§2.4。GPLv2 はこの層に閉じる。

## 前提条件（依存タスク）
- **L-001 完了**（`native/switcher-engine/include/engine.h` と `IVideoEngine`/DTO/JSON 契約が確定）。

## 対象ディレクトリ（このタスクで作成/編集してよい範囲）
- `native/switcher-engine/`（`CMakeLists.txt` / `src/*.c(pp)` / `include/engine.h` は L-001 の定義に従い実装補完）
- 本タスクは **`.sln`・`src/Switcher.*` を触らない**（マネージド非競合）。

## 実装ステップ
1. **CMake ビルド基盤**: 対象 OBS バージョンの **libobs ヘッダ + import lib にリンク**（`find_package`/明示パス）。出力は `switcher-engine.dll`（`NativeMethods` の `DllImport` 名に一致）。**採用 OBS バージョンを固定し `native/switcher-engine/README.md` に前提（OBSインストール要件・パス）を明記**。
2. **libobs 起動**: `obs_startup` → `obs_reset_video`(解像度/FPS/カラースペース)/`obs_reset_audio` → **バンドルモジュール読込**（`obs_add_module_path` → `obs_load_all_modules` → `obs_post_load_modules`）。同梱・読込対象を確定（`win-dshow`/`obs-ffmpeg`/`image-source`/`text`(+DistroAV 任意)）。`options_json`(L-001) で解像度/FPS/モジュールパスを受ける。
3. **共有ソースプール**: `engine_add_source(id,type,settings_json)` で `obs_source_t` を**1回だけ**生成し id で管理。`type`→OBS ソース種別マップ（`webcam`→`dshow_input`、`media/srt`→`ffmpeg_source`(`srt://` 対応)、`ndi`→DistroAV ソース、`image/color/text`→標準）。`engine_remove_source` で解放（参照カウント厳守）。
4. **デュアルM/E**: バスごとに `transition` ソース + `obs_view` を保持。`obs_view_set_source(view, 0, transition)` → `obs_view_add(view)` で `video_t` を取得。
   - `engine_set_preview(bus, source_id)`: そのバスの PVW 対象を保持（内部状態）。
   - `engine_take(bus, kind, ms)`: `kind==CUT` は `obs_transition_set`、`kind==AUTO` は `obs_transition_start(..., ms, dest)`。**他バスへ波及しない**こと。
   - PiP は `obs_scene`/`obs_sceneitem` 変換（座標/拡縮/Z/不透明度/クロップ）で表現。
5. **出力** `engine_apply_outputs(outputs_json)`: `OutputSink` を各バスの `video_t` に紐付け。
   - **VCAM1/VCAM2**: libobs 仮想カメラ出力。**OBS 標準仮想カメラは1系統**のため、2系統化の実現手段（第2仮想カメラ提供 or 一方を NDI/HDMI 振替）を**検証し README に結論を記録**（仕様 §9）。
   - **NDI1/NDI2**: DistroAV 出力に `ndi_name` を設定（未導入時は無効化しエラーを返さず導線相当のステータス）。
   - **HDMI**: `engine_start_display(target, hwnd, display_id)` で `obs_display` を App 提供 HWND に作成し当該 View を全画面描画（カーソル非表示は App 側）。
   - **1 sink 障害が他 sink を止めない**こと（隔離）。
6. **マルチビュー** `engine_apply_multiview(layout_json)`: region/cells（`PGM1/PGM2/PVW1/PVW2/SRC:<id>/EMPTY`）を 4x4 に合成し、`SetTap("MULTIVIEW")` 時に読み戻し、または `engine_start_display("MULTIVIEW",...)` で全画面提示。
7. **プレビュー読み戻し** `engine_set_tap(target,enabled)` + `engine_set_frame_cb`: 有効な target の合成結果を**スロットルして BGRA で読み戻し**、コールバック送出（App の `WriteableBitmap` 用）。読み戻しは負荷を考慮し既定 15〜30fps 上限。
8. **状態通知** `engine_set_state_cb`: 各バスの現 PGM/PVW ソースID・接続状態が変化したら `state_json` を送出（マネージド `OnStateChanged`→タリー算出）。
9. **スレッド安全性**: `gs_*` 操作は `obs_enter_graphics/obs_leave_graphics` 内。P/Invoke から呼ばれる各関数はスレッド安全に（内部キュー/ロック）。文字列は UTF-8、コールバックのバッファはコールバック内でのみ有効。
10. **ネイティブテスト（任意, Windows+OBS のみ）**: 起動/ソース追加/2系統独立 TAKE/読み戻しの smoke テスト。CI（ヘッドレス）には含めない。

## 完了条件 / 検証
- `native/switcher-engine/README.md` に記載の手順で `switcher-engine.dll` がビルドできる（対象 OBS バージョン明記）。
- 単一プロセスで **1枚の webcam ソースが ME1/ME2 両方に載る**（共有参照）ことを確認。
- ME1 の CUT/AUTO が ME2 に波及しない。
- VCAM/NDI/HDMI/MV の出力割当が `engine_apply_outputs`/`_multiview` 経由で反映。1 sink 障害が他を止めない。
- **マネージド側 `dotnet build/test HybridSwitcher.sln` は本タスクの成果に依存せず引き続き緑**（ネイティブは別ビルド）。

## 技術的な補足 / レビュー観点
- **参照カウント/リソース解放**（`obs_source_release`/view/transition/display/output のライフサイクル）を厳格に。旧実装で頻発した NDI リーク・present 例外の轍を踏まない。
- **GPL 境界**: libobs 依存は本ライブラリ内に完全に閉じる。
- **標準ソース制約**: 追加するソースは OBS バンドル + DistroAV(NDI) のみ。独自デコーダを持ち込まない。
- 検証: `.claude/review-patterns.md`「リソース管理」「境界」「並行性」。

## 実機検証で判明した実装ポイント（2026-07-21 追記）

L-001 スキャフォールドを **実 OBS 環境でビルド・起動して**判明した事項。実装時にここで詰まるので先に対処すること。

### 検証済みのビルド/実行経路（土台は動く）
- **採用 OBS = 31.0.3**（Windows）。libobs だけを `cmake --build build_x64 --target libobs` でビルドし、`obs.lib`(import) / `obs.dll` / 生成ヘッダ **`obsconfig.h`**（`build_x64/config/`、ハイフン無し）を得る。公開ヘッダは `<obs-studio>/libobs/`。
- ネイティブビルドは `native/switcher-engine/build.bat`（本セッションで追加）で、`LIBOBS_INCLUDE_DIR`（`libobs` と `build_x64/config` を `;` 区切りで両方）+ `LIBOBS_LIB` を渡す方式Bが確実。
- `Switcher.App.csproj` にビルド後コピー Target を追加済み → `switcher-engine.dll` は VS F5/Rebuild でも常に出力へ入る。**マネージド↔P/Invoke 境界・DLLチェーン(`switcher-engine.dll`→`obs.dll`)ロードは検証済み**で、`engine_startup` の実行到達までは確認できている。

### 現状のブロッカー：`obs_reset_video` 失敗（最優先で対処）
スキャフォールドの `engine_startup` は **`obs_reset_video` で失敗して `nullptr` を返す**（マネージド側は "Native switcher-engine failed to start" で停止）。原因は **ホスト型 libobs のデータパス未配線**：

- **`obs_reset_video` の *前* に `obs_add_data_path(<obs>/data/libobs/)` が必須**。これが無いと libobs 組み込み effect（`default.effect` 等）を見つけられず graphics 初期化が失敗する。※ステップ2は module path のみ言及していたが、**コア data path は別に必要**。
- ソース用に `obs_add_module_path(<obs>/obs-plugins/64bit/, <obs>/data/obs-plugins/%module%/)` → `obs_load_all_modules()`（module 読込失敗は起動を止めない）。
- **`options_json` のパースは libobs 同梱の `obs_data_create_from_json()` で可能**（追加依存不要。`TODO(L-002): parse options_json` の解）。
- `graphics_module` が `"libobs-d3d11"` 固定 → 非Windows開発機の compile-check 用に opengl フォールバック導線を検討（実配布は d3d11）。
- **診断**: `engine_startup` 冒頭で `base_set_log_handler(...)` を仕込み libobs 自身のログをファイル出力すると、effect/module/版不一致のどれで落ちたか即判る。

### マネージド側との依存（タスク境界に注意）
- 現行 `EngineOptions`(`Switcher.Contracts`) は `CanvasWidth/Height/Fps/ModulePath` のみで、**libobs コア data パス / plugin(bin・data) パスのフィールドが無い**。`AppHostService` も `new EngineOptions()`（全デフォルト）で呼んでいる。
- これらパスを渡すには **`Switcher.Contracts`/`Switcher.App` の変更が必要**だが、本タスクは「`.sln`・`src/Switcher.*` を触らない」制約（対象ディレクトリ節）。**マネージド側フィールド追加は別タスク/計画で調整**すること（暫定で env 変数や options_json 固定値で回避も可だが、恒久解はオプション拡張）。

### 実行時の前提
- `obs.dll` + `data/` + プラグイン + `libobs-d3d11.dll` が到達可能であること（インストール済み OBS の `bin\64bit` を PATH、またはビルドの `build_x64\rundir\Release\bin\64bit`）。バージョンは import lib と一致必須。

## 参照
- 仕様: `docs/specs/libobs-engine-migration.md` §2.1〜§2.4, §9
- 契約: `native/switcher-engine/include/engine.h`（L-001）
- 計画: `docs/tasks/orchestration-plan-libobs-migration.md`
