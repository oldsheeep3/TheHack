#!/usr/bin/env bash
# VPS 側のミラー環境を作る／更新する（何度実行しても同じ結果になる）。
#
#   scp -r tools/mirror <vps>:~/mirror-install
#   ssh <vps> 'bash ~/mirror-install/install-vps.sh'
#
# 前提: VPS に git と gh があり、`gh auth status` が成功し、`gh auth setup-git` 済みで
#       ミラー先リポジトリ（private）に push・PR のマージができること。
set -euo pipefail

MIRROR_HOME="${MIRROR_HOME:-$HOME/mirror}"
REPO_DIR="${MIRROR_REPO_DIR:-$MIRROR_HOME/TheHack.git}"
SOURCE_REPO="${MIRROR_SOURCE_REPO:-oldsheeep3/TheHack}"
TARGET_REPO="${MIRROR_TARGET_REPO:-NxTEND-THE-HACK/2026-Team-38}"
BIN_DIR="${BIN_DIR:-$HOME/.local/bin}"

src="$(cd "$(dirname "$0")" && pwd)"

command -v git >/dev/null || { echo "git がありません" >&2; exit 1; }
command -v gh  >/dev/null || { echo "gh がありません" >&2; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "gh にログインしていません" >&2; exit 1; }

mkdir -p "$MIRROR_HOME" "$BIN_DIR"

# 履歴の書き換え用 bare リポジトリ。作業ツリーは要らない。
if [ ! -d "$REPO_DIR" ]; then
  git init --quiet --bare "$REPO_DIR"
  echo "created $REPO_DIR"
fi

# personal: 個人リポジトリ（public なので認証不要）。ここから取り込む。
# hackathon: ミラー先（private / gh のログインで push する）
git -C "$REPO_DIR" remote remove personal  2>/dev/null || true
git -C "$REPO_DIR" remote remove hackathon 2>/dev/null || true
git -C "$REPO_DIR" remote add personal  "https://github.com/${SOURCE_REPO}"
git -C "$REPO_DIR" remote add hackathon "https://github.com/${TARGET_REPO}"

install -m 755 "$src/mirror.sh"   "$MIRROR_HOME/mirror.sh"
install -m 755 "$src/filter.sh"   "$MIRROR_HOME/filter.sh"
install -m 755 "$src/mirror-sync" "$BIN_DIR/mirror-sync"

# 中継 push を受けていた頃の名残。今は GitHub Actions が SSH で mirror-sync を叩くので不要。
rm -f "$REPO_DIR/hooks/post-receive"

echo
echo "installed:"
echo "  作業リポジトリ : $REPO_DIR"
echo "  ミラー本体     : $MIRROR_HOME/mirror.sh"
echo "  履歴フィルタ   : $MIRROR_HOME/filter.sh（.github を履歴ごと除く）"
echo "  同期コマンド   : $BIN_DIR/mirror-sync"
echo
echo "Actions から叩けるようにする手順（SSH 鍵・tailnet ACL・secret）は README「ミラー」を参照。"
echo "疎通確認: ssh <this host> 'bash -lc \"mirror-sync develop\"'"
