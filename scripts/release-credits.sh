#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# What /release needs to write the full change list and the contributors of a release
# (release-notes/X.Y.Z.{en,ru}.md, format in .claude/skills/release/SKILL.md § P4).
#
# Usage: scripts/release-credits.sh <from> <to> <commits|contributors>
#   e.g. scripts/release-credits.sh v0.56.1 origin/develop contributors
#   commits       one line per commit in <from>..<to>, oldest first: short sha, sha,
#                 subject (tab-separated). Local git only.
#   contributors  the ready "Contributors" line: [@login](https://github.com/login), …
#                 Asks GitHub for each commit's authors, co-authors included
#                 (Co-authored-by trailers), so it needs the gh CLI signed in and the
#                 commits pushed. Bots and AI co-authors are left out; an author whose
#                 email maps to no GitHub account is reported on stderr.

set -euo pipefail
cd "$(dirname "$0")/.."

usage="usage: $0 <from> <to> <commits|contributors>"
from="${1:?$usage}"
to="${2:?$usage}"
mode="${3:?$usage}"
repo_owner="nota-daw"
repo_name="nota"

shas="$(git rev-list --reverse "${from}..${to}")" \
  || { echo "error: can't list ${from}..${to}" >&2; exit 1; }
if [ -z "$shas" ]; then
  echo "error: no commits in ${from}..${to}" >&2
  exit 1
fi

case "$mode" in
  commits)
    git log --reverse --format='%h%x09%H%x09%s' "${from}..${to}"
    ;;
  contributors)
    for tool in gh jq; do
      command -v "$tool" >/dev/null || { echo "error: $tool is not installed" >&2; exit 1; }
    done

    # One GraphQL query per 50 commits (aliases c0, c1, …); each answer is one line of
    # JSON: the commit's authors, or {"missing":true} when GitHub doesn't have the commit
    # (the API answers null, which gh's --jq would print as an empty line).
    authors="$(mktemp)"
    trap 'rm -f "$authors"' EXIT
    batch=()
    flush() {
      [ "${#batch[@]}" -eq 0 ] && return
      local q="query{repository(owner:\"${repo_owner}\",name:\"${repo_name}\"){" i=0 sha
      for sha in "${batch[@]}"; do
        q+="c${i}:object(oid:\"${sha}\"){...on Commit{oid authors(first:20){nodes{name email user{login}}}}} "
        i=$((i + 1))
      done
      q+="}}"
      gh api graphql -f query="$q" --jq '.data.repository | to_entries[] | (.value // {missing: true}) | tojson' >>"$authors"
      batch=()
    }
    for sha in $shas; do
      batch+=("$sha")
      [ "${#batch[@]}" -ge 50 ] && flush
    done
    flush

    wanted="$(printf '%s\n' "$shas" | wc -l | tr -d ' ')"
    found="$(jq -s '[.[] | select(.oid)] | length' "$authors")"
    if [ "$found" != "$wanted" ]; then
      echo "error: $((wanted - found)) of ${wanted} commits in ${from}..${to} aren't on GitHub yet — push them first" >&2
      exit 1
    fi

    # Every author of a commit counts once for it. Bots and AI co-authors (Claude signs
    # its commits as noreply@anthropic.com) aren't contributors.
    people='[.[] | .authors.nodes | map(select(
                (.user.login // "" | test("\\[bot\\]$|^claude$") | not)
                and (.email // "" | ascii_downcase) != "noreply@anthropic.com"))
              | unique_by(.user.login // ("~" + (.email // .name | ascii_downcase)))[]]'

    # Without a login: a second address of someone listed (same name, or a mailbox named
    # like a login) is dropped; anyone else is listed by name and reported.
    jq -rs "$people"' as $p
      | ($p | map(select(.user != null))) as $known
      | ($known | map(.user.login | ascii_downcase)) as $logins
      | ($known | map(.name | ascii_downcase)) as $names
      | $p | map(select(.user == null)) | unique_by(.email // .name)[]
      | "warn: no GitHub login for \(.name) <\(.email)>"
        + (if ((.name | ascii_downcase) as $n | $names | index($n))
              or ((.email // "" | split("@")[0] | ascii_downcase) as $m | $logins | index($m))
           then " (looks like a listed contributor, left out)" else "" end)' \
      "$authors" >&2

    jq -rs "$people"' as $p
      | ($p | map(select(.user != null))) as $known
      | ($known | map(.user.login | ascii_downcase)) as $logins
      | ($known | map(.name | ascii_downcase)) as $names
      | ($known | group_by(.user.login)
          | map({key: .[0].user.login, n: length,
                 text: "[@\(.[0].user.login)](https://github.com/\(.[0].user.login))"}))
        + ($p | map(select(.user == null)
                    | select(((.name | ascii_downcase) as $n | $names | index($n)) | not)
                    | select(((.email // "" | split("@")[0] | ascii_downcase) as $m | $logins | index($m)) | not))
              | group_by(.name)
              | map({key: .[0].name, n: length, text: .[0].name}))
      | sort_by(-.n, (.key | ascii_downcase))
      | map(.text) | join(", ")' "$authors"
    ;;
  *)
    echo "error: unknown mode '$mode' (commits | contributors)" >&2
    exit 1
    ;;
esac
