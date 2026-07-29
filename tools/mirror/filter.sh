#!/usr/bin/env bash
# 作業用 bare リポジトリの refs/heads/<branch> を「除外パスを含まない履歴」に書き換え、
# 書き換え後の tip を refs/filtered/<branch> に置いて、その SHA を stdout に出す（VPS 上で動く）。
#
#   filter.sh <branch>
#
# 既定の除外は .github（$MIRROR_EXCLUDE でトップレベル名を空白区切りで変更できる）。
# 除外パスだけを触ったコミットは親と同じ内容になるので落とす。それ以外は著者・コミッタ・
# 日時・メッセージをそのまま引き継ぐため、同じ入力からは必ず同じ SHA が出る。よって
# 2 回目以降のミラーも mirror/<branch> へ fast-forward で push できる（毎回 force にならない）。
#
# 元コミット → 書き換え後コミットの対応は $MIRROR_HOME/filter-map に追記して貯めるので、
# 毎回は新しいコミットぶんしか処理しない。書き換え後のコミットは refs/filtered/* から辿れる
# ので gc では消えない。filtered ref ごと消してしまった場合は filter-map を削除すれば
# 最初から作り直す。
#
# 制約: ツリーが変わる以上、元のコミット署名は引き継げないので落ちる。
set -euo pipefail

MIRROR_HOME="${MIRROR_HOME:-$HOME/mirror}"
REPO_DIR="${MIRROR_REPO_DIR:-$MIRROR_HOME/TheHack.git}"
MAP_FILE="${MIRROR_MAP_FILE:-$MIRROR_HOME/filter-map}"
EXCLUDE="${MIRROR_EXCLUDE:-.github}"

# 呼び出し元の環境に git の変数が残っていると -C が効かないので落とす。
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_QUARANTINE_PATH

branch="${1:?usage: filter.sh <branch>}"
git_() { git -C "$REPO_DIR" "$@"; }

EMPTY_TREE="$(git_ hash-object -t tree /dev/null)"

# 除外パスを落としたツリーを作る。除外対象はトップレベルなので ls-tree 1 段で足りる。
# 行はそのまま通すので、git が引用する必要のあるパス名でもそのまま往復する。
filtered_tree() {
  git_ ls-tree "$1^{tree}" \
    | awk -F'\t' -v ex="$EXCLUDE" \
        'BEGIN { n = split(ex, a, " "); for (i = 1; i <= n; i++) drop[a[i]] = 1 }
         !($2 in drop)' \
    | git_ mktree
}

# 元のメッセージをそのまま取り出す（ヘッダの直後、最初の空行より後ろ）。
# gpgsig の中の空行は「空白 1 個の継続行」なのでここには引っかからない。
raw_message() { git_ cat-file commit "$1" | sed '1,/^$/d'; }

declare -A MAP=()
if [ -f "$MAP_FILE" ]; then
  while read -r old new; do
    [ -n "${old:-}" ] && MAP["$old"]="$new"
  done < "$MAP_FILE"
fi

# 落としたコミットは '-'（対応するコミットが無い）として記録する。
record() { MAP["$1"]="$2"; printf '%s %s\n' "$1" "$2" >> "$MAP_FILE"; }

mapfile -t commits < <(git_ rev-list --topo-order --reverse "$branch")

for old in "${commits[@]}"; do
  [ -n "${MAP[$old]:-}" ] && continue

  read -r _ parents <<<"$(git_ rev-list --parents -n1 "$old")"

  # 親を書き換え後のものに差し替える。落とした親は外し、同じものに潰れた親はまとめる
  # （マージの両側が同じコミットに潰れたら、そのマージはただのコミットになる）。
  new_parents=()
  for p in ${parents:-}; do
    mapped="${MAP[$p]:-}"
    if [ -z "$mapped" ] || [ "$mapped" = '-' ]; then
      continue
    fi
    for seen in ${new_parents[@]+"${new_parents[@]}"}; do
      [ "$seen" = "$mapped" ] && { mapped=''; break; }
    done
    [ -n "$mapped" ] && new_parents+=("$mapped")
  done

  new_tree="$(filtered_tree "$old")"

  # 除外パスしか触っていないコミットは親と同じ内容なので落とす。
  if [ ${#new_parents[@]} -eq 1 ] \
     && [ "$new_tree" = "$(git_ rev-parse "${new_parents[0]}^{tree}")" ]; then
    record "$old" "${new_parents[0]}"
    continue
  fi
  if [ ${#new_parents[@]} -eq 0 ] && [ "$new_tree" = "$EMPTY_TREE" ]; then
    record "$old" '-'
    continue
  fi

  { read -r an; read -r ae; read -r ad; read -r cn; read -r ce; read -r cd; } \
    < <(git_ log -1 --format='%an%n%ae%n%aI%n%cn%n%ce%n%cI' "$old")

  parent_args=()
  for p in ${new_parents[@]+"${new_parents[@]}"}; do parent_args+=(-p "$p"); done

  # メッセージは commit-tree の stdin へ渡す（-F にプロセス置換を渡すと Windows 側で動かない）。
  new="$(raw_message "$old" \
    | GIT_AUTHOR_NAME="$an" GIT_AUTHOR_EMAIL="$ae" GIT_AUTHOR_DATE="$ad" \
      GIT_COMMITTER_NAME="$cn" GIT_COMMITTER_EMAIL="$ce" GIT_COMMITTER_DATE="$cd" \
      git_ commit-tree "$new_tree" ${parent_args[@]+"${parent_args[@]}"})"
  record "$old" "$new"
done

tip="${MAP[$(git_ rev-parse "$branch")]:-}"
if [ -z "$tip" ] || [ "$tip" = '-' ]; then
  # 除外パスしか無いブランチ。ミラーするものが無い。
  exit 0
fi

git_ update-ref "refs/filtered/$branch" "$tip"
printf '%s\n' "$tip"
