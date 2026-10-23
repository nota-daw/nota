#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# Engine benchmark: compares engine builds on the same machine through the public C ABI
# (scripts/bench/engine-bench.cpp). Each label points at a directory that holds a Release
# libnota_engine (.dylib / .so) — build each revision you compare first. Runs are
# interleaved across builds (run 1 of every build, then run 2 …) so heat and background
# load hit them alike.
#
# Usage: scripts/bench-engine.sh [-r runs] <out-dir> <label>=<engine-lib-dir> ...
#   e.g. scripts/bench-engine.sh -r 3 /tmp/bench before=/tmp/old/build after=src/native/nota.engine/build
#
# Writes <out-dir>/results.csv (one row per run) and <out-dir>/machine.txt. Turn the CSV
# into charts + a Markdown table with scripts/bench-chart.py.

set -euo pipefail

runs=3
if [ "${1:-}" = "-r" ]; then runs="$2"; shift 2; fi
[ $# -ge 2 ] || { sed -n '4,15p' "$0" >&2; exit 2; }

root="$(cd "$(dirname "$0")/.." && pwd)"
out="$1"; shift
mkdir -p "$out"
out="$(cd "$out" && pwd)"

# Deterministic 12 s stereo noise clip at 48 kHz (same input for every build).
clip="$out/clip.wav"
python3 -I - "$clip" <<'EOF'
import random, struct, sys, wave
random.seed(1)
w = wave.open(sys.argv[1], "wb")
w.setnchannels(2); w.setsampwidth(2); w.setframerate(48000)
w.writeframes(b"".join(struct.pack("<hh", random.randint(-8000, 8000), random.randint(-8000, 8000))
                       for _ in range(48000 * 12)))
w.close()
EOF

cxx="${CXX:-c++}"
flags=(-std=c++20 -O2 -I"$root/src/native/nota.engine/include")
[ "$(uname)" = "Darwin" ] && flags+=(-mmacosx-version-min=13.0)

labels=()
for spec in "$@"; do
    label="${spec%%=*}"; dir="${spec#*=}"
    dir="$(cd "$dir" && pwd)"
    ls "$dir"/libnota_engine.* >/dev/null 2>&1 || { echo "no libnota_engine in $dir" >&2; exit 1; }
    "$cxx" "${flags[@]}" "$root/scripts/bench/engine-bench.cpp" -L"$dir" -lnota_engine -Wl,-rpath,"$dir" \
        -o "$out/engine-bench-$label"
    labels+=("$label")
done

scenarios=(
    "idle"
    "audio 1 offline" "audio 8 offline" "audio 32 offline"
    "inst 8 offline" "inst 32 offline"
    "audio 16 export" "inst 16 export" "audio 64 export"
    "audio 1 rt" "audio 8 rt" "audio 32 rt"
    "inst 8 rt" "inst 32 rt"
    "audio 64 rt 128" "audio 128 rt 64" "audio 192 rt 64"
)

{
    echo "date: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "os: $(uname -srm)"
    if [ "$(uname)" = "Darwin" ]; then
        echo "cpu: $(sysctl -n machdep.cpu.brand_string), $(sysctl -n hw.ncpu) threads"
        echo "macos: $(sw_vers -productVersion)"
    else
        echo "cpu: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2- | xargs), $(nproc) threads"
    fi
    echo "runs: $runs"
    for spec in "$@"; do echo "build: $spec"; done
} > "$out/machine.txt"

csv="$out/results.csv"
echo "label,run,scenario,tracks,mode,block,cpu_ms_per_s,wall_ms_per_s,cpu_pct,misses" > "$csv"
for r in $(seq 1 "$runs"); do
    for s in "${scenarios[@]}"; do
        for label in "${labels[@]}"; do
            # shellcheck disable=SC2086 # the scenario is a word list on purpose
            set -- $s
            [ "$1" = idle ] || set -- "$1" "$2" "$3" "$clip" "${@:4}" # clip before the optional block
            row="$("$out/engine-bench-$label" "$@" 2>/dev/null | tail -1)"
            echo "$label,$r,$row" >> "$csv"
            echo "run $r  $label  $row"
        done
    done
done
echo "==> $csv"
