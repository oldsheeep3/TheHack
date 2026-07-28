#!/usr/bin/env bash
# 個人リポジトリ → ハッカソン用リポジトリのミラー本体（VPS 上で動く）。
#
#   mirror.sh <branch>...
#
# 中継 bare リポジトリの refs/heads/<branch> を、ミラー先の mirror/<branch> へ push し、
# mirror/<branch> → <branch> の PR を作成する（既にオープンなら何もしない）。
# 認証は VPS の gh ログイン（git の credential helper が gh を呼ぶ）を使うので、
# GitHub 側に secret を置く必要はない。
set -euo pipefail

REPO_DIR="${MIRROR_REPO_DIR:-$HOME/mirror/TheHack.git}"
TARGET_REPO="${MIRROR_TARGET_REPO:-NxTEND-THE-HACK/2026-Team-38}"
SOURCE_REPO="${MIRROR_SOURCE_REPO:-oldsheeep3/TheHack}"
PREFIX="${MIRROR_PREFIX:-mirror}"

# post-receive フックから呼ばれると GIT_DIR 等が環境に残っているので落とす。
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_QUARANTINE_PATH

log() { printf '[mirror] %s\n' "$*"; }
git_() { git -C "$REPO_DIR" "$@"; }

# refs/heads/<branch> を mirror/<branch> へ。通常は fast-forward push で足り、
# 手元で rebase/amend された場合だけ force に落とす（対象は mirror/* のみ）。
push_branch() {
  local branch="$1" dst="refs/heads/${PREFIX}/$1"
  log "${branch} -> ${PREFIX}/${branch}"
  if git_ push --quiet hackathon "refs/heads/${branch}:${dst}" 2>/dev/null; then
    return 0
  fi
  log "warn: ${PREFIX}/${branch} は fast-forward できないため force で上書きします（履歴が書き換わった可能性）"
  git_ push --quiet --force hackathon "refs/heads/${branch}:${dst}"
}

open_pr() {
  local branch="$1" head="${PREFIX}/$1" existing url

  # ベース（同名ブランチ）がミラー先に無ければ PR は作れない。
  if ! gh api "repos/${TARGET_REPO}/branches/${branch}" --silent 2>/dev/null; then
    log "notice: ${TARGET_REPO} に ${branch} が無いため PR はスキップ（${head} は push 済み）"
    return 0
  fi

  # 既に開いている PR があれば、head ブランチの push で内容は自動更新される。
  existing="$(gh pr list -R "${TARGET_REPO}" --head "${head}" --base "${branch}" \
    --state open --json url --jq '.[0].url // ""')"
  if [ -n "${existing}" ]; then
    log "PR は既にオープン: ${existing}"
    return 0
  fi

  # 差分が無ければ PR は作らない（gh pr create がエラーになるため事前に判定）。
  if [ "$(gh api "repos/${TARGET_REPO}/compare/${branch}...${head}" --jq '.ahead_by')" = "0" ]; then
    log "${head} は ${branch} と同じ内容のため PR 不要"
    return 0
  fi

  url="$(gh pr create -R "${TARGET_REPO}" \
    --head "${head}" --base "${branch}" \
    --title "sync: ${SOURCE_REPO} の ${branch} を取り込む" \
    --body "$(printf '%s\n' \
      "個人リポジトリ [\`${SOURCE_REPO}\`](https://github.com/${SOURCE_REPO}) の \`${branch}\` を自動ミラーした PR です。" \
      "" \
      "- 生成元: VPS 上の \`tools/mirror/mirror.sh\`（GitHub Actions は使わない）" \
      "- この PR の head \`${head}\` はミラー専用ブランチです。直接コミットしないでください（次回の同期で上書きされます）。" \
      "- **マージコミット**でマージしてください（squash / rebase merge だと次回の PR に同じコミットが再び載ります）。")")"
  log "PR を作成: ${url}"
}

for branch in "$@"; do
  # mirror/* は自分が作った複製なので送り返さない。
  case "${branch}" in
    '' | "${PREFIX}"/*) continue ;;
  esac
  push_branch "${branch}"
  open_pr "${branch}"
done
