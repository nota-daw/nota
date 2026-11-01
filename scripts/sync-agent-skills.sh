#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Copies the project skills (nota-* and release) from .claude/skills/ — the one source —
# into the skill directories the other agents read: .agents/skills (Codex and other
# agents that use the shared location), .github/skills (Copilot), .cursor/skills (Cursor)
# and .gemini/skills (Gemini CLI). Edit the skills under .claude/skills/ only, then run
# this script. Spec Kit's speckit-* skills are managed by Spec Kit and are left alone.
#
# Usage: scripts/sync-agent-skills.sh           # write the copies
#        scripts/sync-agent-skills.sh --check   # fail if any copy is missing or stale (CI)

set -euo pipefail
cd "$(dirname "$0")/.."

SOURCE=.claude/skills
TARGETS=(.agents/skills .github/skills .cursor/skills .gemini/skills)

check=0
case "${1:-}" in
  --check) check=1 ;;
  "") ;;
  *) echo "usage: $0 [--check]" >&2; exit 2 ;;
esac

# The project's own skills: everything under .claude/skills except Spec Kit's.
skills=()
for dir in "$SOURCE"/*/; do
  name=$(basename "$dir")
  case "$name" in speckit-*) continue ;; esac
  skills+=("$name")
done

is_project_skill() {
  local s
  for s in "${skills[@]}"; do [ "$s" = "$1" ] && return 0; done
  return 1
}

stale=0
for target in "${TARGETS[@]}"; do
  for name in "${skills[@]}"; do
    if [ "$check" = 1 ]; then
      if ! diff -r "$SOURCE/$name" "$target/$name" >/dev/null 2>&1; then
        echo "stale: $target/$name"
        stale=1
      fi
    else
      rm -rf "${target:?}/$name"
      mkdir -p "$target"
      cp -R "$SOURCE/$name" "$target/$name"
    fi
  done
  # A project skill removed or renamed in the source leaves no copy behind.
  for dir in "$target"/nota-*/ "$target"/release/; do
    [ -d "$dir" ] || continue
    name=$(basename "$dir")
    is_project_skill "$name" && continue
    if [ "$check" = 1 ]; then
      echo "orphaned: $target/$name"
      stale=1
    else
      rm -rf "${target:?}/$name"
    fi
  done
done

if [ "$check" = 1 ] && [ "$stale" = 1 ]; then
  echo "error: agent skill copies are out of date — run scripts/sync-agent-skills.sh" >&2
  exit 1
fi
[ "$check" = 1 ] || echo "synced ${#skills[@]} skills into ${TARGETS[*]}"
