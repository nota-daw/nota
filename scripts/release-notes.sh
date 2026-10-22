#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Renders a release's curated notes (release-notes/X.Y.Z.{en,ru}.md) for one place.
# The notes are a summary, then (after "<!-- changes -->") the full Added / Changed /
# Fixed list with commit links, then (after "<!-- contributors -->") the contributors.
# The GitHub release carries all of it; the news posts leave the full list out (a
# Telegram message holds 4096 characters) and point at the release for it.
# The one source of the published release text: the release workflow builds the GitHub
# release body with it, and /release builds the news posts with it, then checks the
# published draft against it, so the text can't drift between the places.
#
# Usage: scripts/release-notes.sh <version> <github|post-en|post-ru>
#   github   GitHub release body: title + EN notes + install table
#   post-en  English news-channel post: title + EN notes without the full list + download link
#   post-ru  Russian news-channel post: title + RU notes without the full list + download link

set -euo pipefail
cd "$(dirname "$0")/.."

version="${1:?usage: $0 <version> <github|post-en|post-ru>}"
target="${2:?usage: $0 <version> <github|post-en|post-ru>}"
release_url="https://github.com/nota-daw/nota/releases/tag/v${version}"

# The note file's body: drop the comment lines (the SPDX header and the part markers)
# and leading/trailing blank lines. "body <lang> summary" also drops the full list —
# everything from "<!-- changes -->" up to "<!-- contributors -->" — and squeezes the
# blank lines it leaves behind.
body() {
  local file="release-notes/${version}.$1.md" part="${2:-all}"
  if [ ! -s "$file" ]; then
    echo "error: $file is missing or empty" >&2
    exit 1
  fi
  # Markers match as in ReleaseNotesParser: alone on the line, any case, any spacing.
  awk -v part="$part" '
    { l = tolower($0) }
    l ~ /^[[:space:]]*<!--[[:space:]]*changes[[:space:]]*-->[[:space:]]*$/      { if (part == "summary") skip = 1 }
    l ~ /^[[:space:]]*<!--[[:space:]]*contributors[[:space:]]*-->[[:space:]]*$/ { skip = 0 }
    /^[[:space:]]*<!--.*-->[[:space:]]*$/ { next }
    skip { next }
    { print }
  ' "$file" | cat -s | sed '/./,$!d' | sed -e :a -e '/^\n*$/{$d;N;ba' -e '}'
}

case "$target" in
  github)
    en="$(body en)"
    cat <<EOF
🎛 **Nota ${version} is out**

${en}

---

### Install

| System | File |
|---|---|
| macOS, Apple Silicon | \`Nota-*-arm64.dmg\` |
| macOS, Intel | \`Nota-*-x86_64.dmg\` |
| Windows | \`Nota-Setup-*-x64.exe\` (\`-arm64\` for Windows on ARM) |
| Linux | \`Nota-*-x86_64.AppImage\` (\`-aarch64\` for ARM) |

**First launch:** Nota isn't signed with a paid certificate yet. On **macOS**, if it says Apple could not verify Nota, open **System Settings → Privacy & Security** and click **Open Anyway** — [step-by-step guide](https://github.com/nota-daw/nota#opening-nota-on-macos). On **Windows**, click **More info → Run anyway** in SmartScreen.
EOF
    ;;
  post-en)
    en="$(body en summary)"
    printf '🎛 **Nota %s is out**\n\n%s\n\nDownload: %s\n' "$version" "$en" "$release_url"
    ;;
  post-ru)
    ru="$(body ru summary)"
    printf '🎛 **Вышла Nota %s**\n\n%s\n\nСкачать: %s\n' "$version" "$ru" "$release_url"
    ;;
  *)
    echo "error: unknown target '$target' (github | post-en | post-ru)" >&2
    exit 1
    ;;
esac
