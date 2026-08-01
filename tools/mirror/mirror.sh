#!/usr/bin/env bash
# 個人リポジトリ → ハッカソン用リポジトリのミラー本体（VPS 上で動く）。
#
#   mirror.sh <branch>...
#
# 作業用 bare リポジトリの refs/heads/<branch> をミラー先の mirror/<branch> へ push し、
# mirror/<branch> → <branch> の PR を作ってマージコミットでマージするところまでやる。
# 認証は VPS の gh ログイン（git の credential helper が gh を呼ぶ）。
# .github/workflows/ を含むので、gh のログインには workflow スコープが要る。
set -euo pipefail

MIRROR_HOME="${MIRROR_HOME:-$HOME/mirror}"
REPO_DIR="${MIRROR_REPO_DIR:-$MIRROR_HOME/TheHack.git}"
TARGET_REPO="${MIRROR_TARGET_REPO:-NxTEND-THE-HACK/2026-Team-38}"
SOURCE_REPO="${MIRROR_SOURCE_REPO:-oldsheeep3/TheHack}"
PREFIX="${MIRROR_PREFIX:-mirror}"

# 呼び出し元の環境に git の変数が残っていると -C が効かないので落とす。
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_QUARANTINE_PATH

log() { printf '[mirror] %s\n' "$*"; }
git_() { git -C "$REPO_DIR" "$@"; }

# 通常は fast-forward で足り、元の履歴が rebase/amend された場合だけ force に落とす
# （対象は mirror/* のみ）。
push_branch() {
  local branch="$1" src="refs/heads/$1" dst="refs/heads/${PREFIX}/$1"
  log "${branch} -> ${PREFIX}/${branch}"
  if git_ push --quiet hackathon "${src}:${dst}" 2>/dev/null; then
    return 0
  fi
  log "warn: ${PREFIX}/${branch} は fast-forward できないため force で上書きします（履歴が書き換わった可能性）"
  git_ push --quiet --force hackathon "${src}:${dst}"
}

# PR を「マージコミット」でマージする。squash / rebase merge だと merge-base が進まず、
# 次回の PR に同じコミットが再び載る。
# 作成直後は GitHub 側でマージ可能かの計算が終わっておらず一時的に失敗するので数回待つ。
# 保護ブランチで弾かれた場合だけ --admin にフォールバックする（gh のログインが管理者権限を
# 持っていなければここも失敗し、warn を出して次のブランチへ進む）。
merge_pr() {
  local pr="$1" head="$2" out attempt
  for attempt in 1 2 3 4 5; do
    if out="$(gh pr merge -R "${TARGET_REPO}" "${pr}" --merge --delete-branch=false 2>&1)"; then
      log "PR #${pr} をマージしました"
      return 0
    fi
    if [ "${attempt}" -lt 5 ]; then sleep 5; fi
  done

  if out="$(gh pr merge -R "${TARGET_REPO}" "${pr}" --merge --admin --delete-branch=false 2>&1)"; then
    log "PR #${pr} をマージしました（ブランチ保護を --admin で通した）"
    return 0
  fi

  log "warn: PR #${pr} (${head}) をマージできませんでした。手でマージしてください:"
  printf '%s\n' "${out}" | sed 's/^/[mirror]        /'
  return 1
}

# mirror/<branch> → <branch> の PR を用意して（既にオープンならそれを使って）マージする。
sync_pr() {
  local branch="$1" head="${PREFIX}/$1" pr url ahead

  # ベース（同名ブランチ）がミラー先に無ければ PR は作れない。
  if ! gh api "repos/${TARGET_REPO}/branches/${branch}" --silent 2>/dev/null; then
    log "notice: ${TARGET_REPO} に ${branch} が無いため PR はスキップ（${head} は push 済み）"
    return 0
  fi

  # ミラー先の <branch> と共通の祖先が無いと PR を作ってもマージできない（compare も 404）。
  if ! ahead="$(gh api "repos/${TARGET_REPO}/compare/${branch}...${head}" --jq '.ahead_by' 2>/dev/null)"; then
    log "warn: ${branch}...${head} を比較できません。ミラー先の ${branch} を ${head} の内容に"
    log "      合わせ直してください。PR はスキップします。"
    return 0
  fi

  # 差分が無ければ PR は作らない（gh pr create がエラーになるため事前に判定）。
  if [ "${ahead}" = "0" ]; then
    log "${head} は ${branch} と同じ内容のため PR 不要"
    return 0
  fi

  # 既に開いている PR があれば、head ブランチの push で内容は更新済み。
  pr="$(gh pr list -R "${TARGET_REPO}" --head "${head}" --base "${branch}" \
    --state open --json number --jq '.[0].number // ""')"

  if [ -n "${pr}" ]; then
    log "オープン中の PR #${pr} を使う"
  else
    url="$(gh pr create -R "${TARGET_REPO}" \
      --head "${head}" --base "${branch}" \
      --title "sync: ${SOURCE_REPO} の ${branch} を取り込む" \
      --body "$(printf '%s\n' \
        "個人リポジトリ [\`${SOURCE_REPO}\`](https://github.com/${SOURCE_REPO}) の \`${branch}\` を自動ミラーした PR です。" \
        "" \
        "- 生成元: VPS 上の \`tools/mirror/mirror.sh\`。作成後そのままマージコミットでマージされます。" \
        "- この PR の head \`${head}\` はミラー専用ブランチです。直接コミットしないでください（次回の同期で上書きされます）。")")"
    pr="${url##*/}"
    log "PR を作成: ${url}"
  fi

  merge_pr "${pr}" "${head}"
}

failed=0
for branch in "$@"; do
  # mirror/* は自分が作った複製なので送り返さない。
  case "${branch}" in
    '' | "${PREFIX}"/*) continue ;;
  esac
  if push_branch "${branch}"; then
    sync_pr "${branch}" || failed=1
  else
    failed=1
  fi
done

# 通らなかったブランチがあれば呼び出し元（Actions のジョブ）を赤くする。
exit "${failed}"
