#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Guard the Linux release baseline: every ELF under the given tree (the AppDir
# payload of the AppImage) must reference only GLIBC_ <= 2.28 and
# GLIBCXX_/CXXABI_ <= RHEL 8's system libstdc++ (GCC 8: 3.4.25 / 1.3.11), so the
# AppImage runs on RHEL/Alma/Rocky 8 and 9 and every newer distro. Release CI
# (.github/workflows/release.yml) runs this right after packaging and fails the
# build on the first regression — a binary linked on a newer build image quietly
# demands a newer glibc/libstdc++ and stops loading on old distros otherwise.
#
# Usage: scripts/check-glibc-floor.sh [file-or-dir ...]   (default: dist/Nota.AppDir/usr/bin)

set -euo pipefail

MAX_GLIBC="2.28"
MAX_GLIBCXX="3.4.25"   # GCC 8 — RHEL 8's system libstdc++
MAX_CXXABI="1.3.11"

# true when $1 sorts after $2 in version order (sort -V)
too_new() {
  [ "$(printf '%s\n%s\n' "$2" "$1" | sort -V | tail -n1)" != "$2" ]
}

targets=("$@")
[ ${#targets[@]} -gt 0 ] || targets=("dist/Nota.AppDir/usr/bin")

# Collect ELFs: everything the loader could refuse on an old distro — our own
# engine + scanworker + apphost, plus the bundled .NET/Avalonia/SkiaSharp libs.
elfs=()
for t in "${targets[@]}"; do
  if [ -d "$t" ]; then
    while IFS= read -r -d '' f; do
      case "$(file -b "$f")" in ELF*) elfs+=("$f") ;; esac
    done < <(find "$t" -type f -print0)
  elif [ -f "$t" ]; then
    case "$(file -b "$t")" in ELF*) elfs+=("$t") ;; esac
  else
    echo "check-glibc-floor: no such file or directory: ${t}" >&2
    exit 1
  fi
done
if [ ${#elfs[@]} -eq 0 ]; then
  echo "check-glibc-floor: no ELF files found under ${targets[*]}" >&2
  exit 1
fi

fail=0
for f in "${elfs[@]}"; do
  refs="$(objdump -T "$f" 2>/dev/null | grep -oE '(GLIBC|GLIBCXX|CXXABI)_[0-9]+(\.[0-9]+)*' | sort -u || true)"
  for ref in $refs; do
    case "$ref" in
      GLIBC_*)   ver="${ref#GLIBC_}";   max="${MAX_GLIBC}" ;;
      GLIBCXX_*) ver="${ref#GLIBCXX_}"; max="${MAX_GLIBCXX}" ;;
      CXXABI_*)  ver="${ref#CXXABI_}";  max="${MAX_CXXABI}" ;;
      *) continue ;;
    esac
    if too_new "$ver" "$max"; then
      echo "check-glibc-floor: FAIL ${f#./}: needs ${ref} (baseline ${ref%%_*} ${max})" >&2
      fail=1
    fi
  done
done

if [ "$fail" -ne 0 ]; then
  echo "check-glibc-floor: baseline exceeded — rebuild in the manylinux_2_28 container (the" >&2
  echo "linux job of .github/workflows/release.yml); binaries built on a newer image only run" >&2
  echo "on distros with a matching glibc/libstdc++." >&2
  exit 1
fi
echo "check-glibc-floor: ${#elfs[@]} ELFs OK (GLIBC_ <= ${MAX_GLIBC}, GLIBCXX_ <= ${MAX_GLIBCXX}, CXXABI_ <= ${MAX_CXXABI})"
