#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
#
# Where 0.59.2 set values in Geist Mono (spec 091, FR-009). The style guide commit (#296)
# replaced every Mono use with Sans in one go; its hunks are the inventory of the values
# and readouts role. For each removed Mono line this prints the line the same hunk added
# and, when that text is still on HEAD, where it is now — the place to restore the role.
#
# Usage: scripts/classic-role-sites.py [commit]   (default: the #296 merge, cbd77012)
# Output: markdown rows: file | head line | removed (0.59.2) | added (#296)

import re
import subprocess
import sys

COMMIT = sys.argv[1] if len(sys.argv) > 1 else "cbd77012"
MONO = re.compile(r"NotaFonts\.Mono|MonoFamily|Font\.Mono")
# What #296 turned each Mono name into, so a removed line can be found on HEAD as it reads now.
SANS = [("NotaFonts.MonoFamily", "NotaFonts.UiFamily"), ("NotaFonts.MonoMedium", "NotaFonts.SansMedium"),
        ("NotaFonts.MonoBold", "NotaFonts.SansBold"), ("NotaFonts.Mono", "NotaFonts.Sans"),
        ("Font.Mono", "Font.UI")]


def as_sans(line: str) -> str:
    for a, b in SANS:
        line = line.replace(a, b)
    return line.strip()


def git(*args: str) -> str:
    return subprocess.run(["git", *args], check=True, capture_output=True, text=True).stdout


def hunks(diff: str):
    """Yield (file, removed lines, added lines) per hunk."""
    file, removed, added = None, [], []
    for line in diff.splitlines():
        if line.startswith("diff --git"):
            if file and (removed or added):
                yield file, removed, added
            file, removed, added = None, [], []
        elif line.startswith("+++ "):
            file = line[6:] if line.startswith("+++ b/") else None
        elif line.startswith("@@"):
            if file and (removed or added):
                yield file, removed, added
            removed, added = [], []
        elif line.startswith("-") and not line.startswith("---"):
            removed.append(line[1:])
        elif line.startswith("+") and not line.startswith("+++"):
            added.append(line[1:])
    if file and (removed or added):
        yield file, removed, added


def head_lines(path: str):
    try:
        return git("show", f"HEAD:{path}").splitlines()
    except subprocess.CalledProcessError:
        return None


def main() -> None:
    diff = git("show", "-U0", "--format=", COMMIT, "--", "src/managed")
    print("| file | head line | 0.59.2 (removed) | #296 (added) |")
    print("|---|---|---|---|")
    cache: dict[str, list[str] | None] = {}
    for file, removed, added in hunks(diff):
        mono = [r for r in removed if MONO.search(r)]
        if not mono:
            continue
        if file not in cache:
            cache[file] = head_lines(file)
        lines = cache[file]
        for i, old in enumerate(mono):
            new = added[i] if i < len(added) else ""
            where = "gone" if lines is None else "?"
            if lines is not None:
                # First the removed line as #296 would have rewritten it, then the hunk's own line.
                for probe in (as_sans(old), new.strip()):
                    hits = [n + 1 for n, l in enumerate(lines) if probe and l.strip() == probe]
                    if hits:
                        where = ",".join(map(str, hits[:5]))
                        break
            cell = lambda s: s.strip().replace("|", "\\|")[:140]
            print(f"| {file.removeprefix('src/managed/')} | {where} | `{cell(old)}` | `{cell(new)}` |")


if __name__ == "__main__":
    main()
