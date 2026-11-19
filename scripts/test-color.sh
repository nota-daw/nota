#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
# Compile the actual Chroma DSP for repeatable regression, listening and CPU comparisons.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
source_root="$root/src/native/nota.engine/src"
out=
args=(--tests)
inputs=()
while [ "$#" -gt 0 ]; do
    case "$1" in
        --source) source_root="$2"; shift 2 ;;
        --out) out="$2"; shift 2 ;;
        --listen-only) args=(--listen-only); shift ;;
        --input) inputs+=("$2"); shift 2 ;;
        --tests-only) args=(--tests-only); shift ;;
        *) echo "Usage: $0 [--source <native-src>] [--out <directory>] [--listen-only | --tests-only] [--input <audio>]..." >&2; exit 2 ;;
    esac
done
if [ -z "$out" ]; then
    out="$root/build/color-prototype/hq/current"
fi
mkdir -p "$out"
if [ "${inputs[0]+present}" = present ]; then
    for index in "${!inputs[@]}"; do
        raw="$out/recording-$((index + 1)).f32"
        ffmpeg -v error -y -i "${inputs[$index]}" -map 0:a:0 -t 4.5 -ac 2 -ar 48000 -f f32le "$raw"
        args+=("$raw")
    done
fi
fft="$root/src/native/nota.engine/build/_deps/signalsmith_linear-src/include"
if [ ! -f "$fft/signalsmith-linear/fft.h" ]; then
    echo "Prepare the native engine build first (scripts/build.sh); existing FFT headers are required." >&2
    exit 2
fi
flags=(-std=c++20 -O2 -I"$source_root" -I"$root/src/native/nota.engine/src" -isystem "$fft")
if [ "$(uname)" = Darwin ]; then
    flags+=(-DSIGNALSMITH_USE_ACCELERATE -framework Accelerate)
fi
"${CXX:-c++}" "${flags[@]}" "$root/tests/native/ChromaTest.cpp" \
    "$source_root/devices/effects/Chroma.cpp" "$root/src/native/nota.engine/src/dsp/primitives/BiquadCascade.cpp" \
    -o "$out/chroma-test"
"$out/chroma-test" "$out" "${args[@]}" | tee "$out/results.txt"
