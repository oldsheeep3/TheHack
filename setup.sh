#!/usr/bin/env bash
# ============================================================================
#  setup.sh — clone 直後の初回ビルドを通すための環境準備
#
#  clone しただけの状態では以下が理由で必ずビルドが落ちる。それを一括で潰す。
#    1. dotnet / node が ~/.bashrc 経由でしか PATH に乗らない（非ログインシェル・CI で不在）
#    2. firmware/switcher-module/ch32v003fun サブモジュールが未初期化
#    3. scripts -> .claude/scripts のシンボリックリンクが切れている（.claude は gitignore）
#    4. NuGet / npm の依存が未取得
#
#  使い方:
#    ./setup.sh                 ローカル開発用（不足を可能な範囲で自動修復）
#    ./setup.sh --ci            CI 用（対話なし・リポジトリ外へ書かない・.claude を触らない）
#    ./setup.sh --check         検証のみ。取得も生成もせず、状態を報告して終わる
#    ./setup.sh --install-missing   .NET SDK が無ければ dotnet-install.sh で ~/.dotnet に入れる
#
#  その他: --skip-submodules / --skip-restore / --skip-npm / --skip-agent-scripts
#          （.env の SETUP_SKIP_* でも同じ制御ができる）
#
#  終了コード: 0 = 準備完了 / 1 = 未解決の問題あり（CI はここで落ちる）
#  何度実行しても同じ結果になる（冪等）。
# ============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

SOLUTION="HybridSwitcher.sln"
PHONE_BRIDGE="apps/phone-bridge"
AGENT_SCRIPTS_REPO="https://github.com/oldsheeep3/.claude"

CI_MODE=0
CHECK_ONLY=0
INSTALL_MISSING=0

FAILURES=()
WARNINGS=()
STEPS=()

# 端末なら色を付ける。CI のログでは付けない。
if [ -t 1 ] && [ -z "${NO_COLOR:-}" ]; then
  C_RED=$'\033[31m'; C_GRN=$'\033[32m'; C_YEL=$'\033[33m'; C_DIM=$'\033[2m'; C_OFF=$'\033[0m'
else
  C_RED=''; C_GRN=''; C_YEL=''; C_DIM=''; C_OFF=''
fi

info()  { printf '%s\n' "  $*"; }
note()  { printf '%s\n' "${C_DIM}  $*${C_OFF}"; }
ok()    { printf '%s\n' "${C_GRN}  ok${C_OFF}   $*"; STEPS+=("ok|$*"); }
warn()  { printf '%s\n' "${C_YEL}  warn${C_OFF} $*"; WARNINGS+=("$*"); STEPS+=("warn|$*"); }
fail()  { printf '%s\n' "${C_RED}  NG${C_OFF}   $*"; FAILURES+=("$*"); STEPS+=("fail|$*"); }
head1() { printf '\n%s\n' "== $* =="; }

usage() { sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
  case "$1" in
    --ci)                 CI_MODE=1 ;;
    --check)              CHECK_ONLY=1 ;;
    --install-missing)    INSTALL_MISSING=1 ;;
    --skip-submodules)    SETUP_SKIP_SUBMODULES=1 ;;
    --skip-restore)       SETUP_SKIP_RESTORE=1 ;;
    --skip-npm)           SETUP_SKIP_NPM=1 ;;
    --skip-agent-scripts) SETUP_SKIP_AGENT_SCRIPTS=1 ;;
    -h|--help)            usage; exit 0 ;;
    *) printf '%s\n' "unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

# CI ではサブモジュール（RISC-V クロスビルド用）も .claude も要らない。ci.yml と同じ判断。
if [ "$CI_MODE" = 1 ]; then
  : "${SETUP_SKIP_SUBMODULES:=1}"
  : "${SETUP_SKIP_AGENT_SCRIPTS:=1}"
fi

# ---------------------------------------------------------------------------
# 1. .env
# ---------------------------------------------------------------------------
head1 ".env"

# dotenv の慣例どおり「既に export されている環境変数」を .env より優先する。
# CI の env: / secrets や `DOTNET_ROOT=/opt/dotnet ./setup.sh` が .env に負けないようにする。
load_env() {
  local before
  before="$(export -p)"
  set -a
  # shellcheck disable=SC1091
  . ./.env
  set +a
  eval "$before" 2>/dev/null || true
}

if [ -f .env ]; then
  load_env
  ok ".env を読み込んだ（既存の環境変数が優先）"
elif [ "$CI_MODE" = 1 ]; then
  # CI の環境変数は Actions 側（env: / secrets）で渡す。ファイルは作らない。
  note ".env なし（--ci のため生成しない。値は Actions の env: で渡すこと）"
elif [ "$CHECK_ONLY" = 1 ]; then
  warn ".env が無い（--check のため生成しない。cp .env.example .env）"
elif [ -f .env.example ]; then
  cp .env.example .env
  load_env
  ok ".env.example から .env を作成した（必要に応じて編集）"
else
  fail ".env も .env.example も無い"
fi

# .env が無い/未設定の項目にも既定値を効かせる。
: "${DOTNET_ROOT:=$HOME/.dotnet}"
: "${NODE_VERSION:=24}"
: "${SETUP_SKIP_SUBMODULES:=0}"
: "${SETUP_SKIP_RESTORE:=0}"
: "${SETUP_SKIP_NPM:=0}"
: "${SETUP_SKIP_AGENT_SCRIPTS:=0}"

# ---------------------------------------------------------------------------
# 2. ツールチェーン
# ---------------------------------------------------------------------------
head1 "ツールチェーン"

# --- .NET SDK ---
# PATH に無ければ DOTNET_ROOT を前置きする。これがローカルで最も多い失敗原因。
if ! command -v dotnet >/dev/null 2>&1; then
  if [ -x "$DOTNET_ROOT/dotnet" ]; then
    export DOTNET_ROOT
    export PATH="$DOTNET_ROOT:$PATH"
    note "dotnet を PATH に追加した ($DOTNET_ROOT)"
  elif [ "$INSTALL_MISSING" = 1 ] && [ "$CHECK_ONLY" = 0 ]; then
    info ".NET 9 SDK を $DOTNET_ROOT に導入中..."
    tmp_installer="$(mktemp)"
    if curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp_installer" \
       && bash "$tmp_installer" --channel 9.0 --install-dir "$DOTNET_ROOT" >/dev/null; then
      export DOTNET_ROOT
      export PATH="$DOTNET_ROOT:$PATH"
      note ".NET SDK を導入した"
    else
      fail ".NET SDK の自動導入に失敗した（ネットワーク要確認）"
    fi
    rm -f "$tmp_installer"
  fi
fi

if command -v dotnet >/dev/null 2>&1; then
  dotnet_version="$(dotnet --version 2>/dev/null || echo unknown)"
  case "$dotnet_version" in
    9.*) ok ".NET SDK $dotnet_version" ;;
    *)   warn ".NET SDK $dotnet_version（このリポジトリは 9.0.x 想定）" ;;
  esac
else
  fail ".NET 9 SDK が無い。'./setup.sh --install-missing' か、DOTNET_ROOT を .env で正しい場所に向ける"
fi

# --- Node / npm ---
# ローカルでは nvm 経由のことが多く、非ログインシェルでは読み込まれていない。
if ! command -v node >/dev/null 2>&1 && [ -s "${NVM_DIR:-$HOME/.nvm}/nvm.sh" ]; then
  # nvm.sh は未定義変数を踏むので set -u を一時的に外す。
  set +u
  # shellcheck disable=SC1091
  . "${NVM_DIR:-$HOME/.nvm}/nvm.sh"
  nvm use "$NODE_VERSION" >/dev/null 2>&1 || nvm use default >/dev/null 2>&1 || true
  set -u
  command -v node >/dev/null 2>&1 && note "nvm から node を読み込んだ"
fi

if command -v node >/dev/null 2>&1; then
  node_version="$(node --version)"          # 例: v24.18.0
  node_major="${node_version#v}"; node_major="${node_major%%.*}"
  if [ "$node_major" -ge "$NODE_VERSION" ] 2>/dev/null; then
    ok "Node $node_version"
  else
    warn "Node $node_version（ci.yml は $NODE_VERSION 系。lint/build が食い違う可能性あり）"
  fi
else
  fail "Node が無い（$NODE_VERSION 系が必要。nvm install $NODE_VERSION）"
fi

if command -v npm >/dev/null 2>&1; then
  ok "npm $(npm --version)"
else
  fail "npm が無い"
fi

# --- firmware ホストテスト用 ---
# gcc/make はホストテスト（CI の firmware ジョブ）に必須。cmake は Windows の
# native/switcher-engine ビルドだけで使うので、Linux では無くても警告に留める。
for tool in make gcc; do
  command -v "$tool" >/dev/null 2>&1 && ok "$tool" || fail "$tool が無い（firmware ホストテストに必要）"
done
command -v cmake >/dev/null 2>&1 \
  && ok "cmake" \
  || note "cmake なし（native/switcher-engine は Windows 専用なので Linux では不要）"

# ---------------------------------------------------------------------------
# 3. サブモジュール
# ---------------------------------------------------------------------------
head1 "サブモジュール"

if [ ! -f .gitmodules ]; then
  note ".gitmodules が無い"
elif [ "$SETUP_SKIP_SUBMODULES" = 1 ]; then
  note "スキップ（ホストテストと .NET ビルドには不要）"
else
  # status の先頭 '-' が未初期化。
  if git submodule status --recursive 2>/dev/null | grep -q '^-'; then
    if [ "$CHECK_ONLY" = 1 ]; then
      warn "未初期化のサブモジュールがある（git submodule update --init --recursive）"
    elif git submodule update --init --recursive >/dev/null 2>&1; then
      ok "サブモジュールを初期化した"
    else
      fail "サブモジュールの初期化に失敗した"
    fi
  else
    ok "サブモジュールは初期化済み"
  fi
fi

# ---------------------------------------------------------------------------
# 4. scripts -> .claude/scripts
#    docs/tasks/*.md が ./scripts/manage-screen.sh を参照するが、.claude は
#    gitignore されているので clone しただけではリンクが切れる。
# ---------------------------------------------------------------------------
head1 "エージェント用スクリプト (scripts/)"

if [ "$SETUP_SKIP_AGENT_SCRIPTS" = 1 ]; then
  note "スキップ"
elif [ -d scripts ] && [ -x scripts/manage-screen.sh ]; then
  ok "scripts/ は解決できている"
elif [ "$CHECK_ONLY" = 1 ]; then
  warn "scripts -> .claude/scripts が切れている（.claude 未取得）"
elif [ -e .claude ]; then
  warn ".claude はあるが scripts/manage-screen.sh が見つからない（.claude 側を確認）"
elif git clone --quiet "$AGENT_SCRIPTS_REPO" .claude 2>/dev/null; then
  ok ".claude を取得してリンクを復旧した"
else
  # 取得できなくてもビルドには影響しないので落とさない。
  warn ".claude を取得できなかった（マルチエージェント運用時のみ必要。ビルドには不要）"
fi

# ---------------------------------------------------------------------------
# 5. 依存の取得
# ---------------------------------------------------------------------------
head1 "依存パッケージ"

if [ "$CHECK_ONLY" = 1 ]; then
  note "--check のため取得しない"
else
  if [ "$SETUP_SKIP_RESTORE" = 1 ]; then
    note "NuGet: スキップ"
  elif ! command -v dotnet >/dev/null 2>&1; then
    warn "NuGet: dotnet が無いのでスキップ"
  elif dotnet restore "$SOLUTION" >/dev/null; then
    ok "NuGet restore 完了"
  else
    fail "dotnet restore に失敗した"
  fi

  if [ "$SETUP_SKIP_NPM" = 1 ]; then
    note "npm: スキップ"
  elif ! command -v npm >/dev/null 2>&1; then
    warn "npm: npm が無いのでスキップ"
  elif [ ! -f "$PHONE_BRIDGE/package-lock.json" ]; then
    fail "$PHONE_BRIDGE/package-lock.json が無い"
  # ci.yml と同じ npm ci（lock 厳守）。node_modules は毎回作り直される。
  elif (cd "$PHONE_BRIDGE" && npm ci --no-audit --no-fund >/dev/null 2>&1); then
    ok "npm ci 完了 ($PHONE_BRIDGE)"
  else
    fail "npm ci に失敗した（cd $PHONE_BRIDGE && npm ci で詳細を確認）"
  fi
fi

# ---------------------------------------------------------------------------
# 6. 結果
# ---------------------------------------------------------------------------
head1 "結果"

if [ ${#FAILURES[@]} -gt 0 ]; then
  printf '%s\n' "${C_RED}未解決 ${#FAILURES[@]} 件:${C_OFF}"
  for f in "${FAILURES[@]}"; do printf '%s\n' "  - $f"; done
fi
if [ ${#WARNINGS[@]} -gt 0 ]; then
  printf '%s\n' "${C_YEL}警告 ${#WARNINGS[@]} 件:${C_OFF}"
  for w in "${WARNINGS[@]}"; do printf '%s\n' "  - $w"; done
fi

if [ ${#FAILURES[@]} -gt 0 ]; then
  printf '\n%s\n' "${C_RED}準備未完了${C_OFF}"
  exit 1
fi

printf '\n%s\n' "${C_GRN}準備完了${C_OFF}"
# dotnet を PATH に足した場合、それはこのスクリプトの子プロセス内だけの話なので、
# 呼び出し元のシェルで必要なぶんを案内する。
if [ "$CI_MODE" = 0 ] && [ "$CHECK_ONLY" = 0 ]; then
  cat <<EOS

次のコマンドが通るようになっている（現在のシェルに dotnet が無ければ下記を前置き）:

  export DOTNET_ROOT="$DOTNET_ROOT" PATH="$DOTNET_ROOT:\$PATH"

  dotnet build $SOLUTION -c Release -warnaserror
  dotnet test  $SOLUTION
  (cd $PHONE_BRIDGE && npm run build && npm test)
  make -C firmware/pico2w-controller/test test
  make -C firmware/switcher-module/test test
EOS
fi
