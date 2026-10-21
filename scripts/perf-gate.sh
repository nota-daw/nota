#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
#
# Engine performance regression gate (specs/034-engine-perf-gate).
# Builds the engine at two commits and renders the same scenarios with each. Two checks:
#  - work: CPU instructions per render (Valgrind Callgrind, all engine threads). They
#    repeat exactly between runs, so any scenario more than PERF_LIMIT_PCT (default 1)
#    percent above the base fails.
#  - time: wall-clock render time, base and head interleaved over PERF_ROUNDS rounds,
#    fastest round, re-timed with more rounds before failing. Catches what instruction counts cannot see (denormals, cache misses, thread
#    hand-offs); timing is noisy on shared machines, so its limit is PERF_TIME_LIMIT_PCT
#    (default 15).
#
# Usage: scripts/perf-gate.sh <base-ref> [head-ref]
#   head-ref defaults to the working tree as it is.
# Env:   PERF_LIMIT_PCT=1   allowed growth in instructions per scenario
#        PERF_TIME_LIMIT_PCT=15  allowed growth in median wall time per scenario
#        PERF_ROUNDS=5      interleaved timing rounds
#        PERF_CONFIRM_ROUNDS=10  extra rounds for a scenario over the time limit
#        PERF_SECONDS=1     audio rendered per scenario
#        PERF_ACCEPT=1      report regressions but exit 0 (an approved regression)
#        PERF_WORK=build/perf   scratch dir (worktrees, builds, results)
# Needs: Linux, valgrind, cmake, ninja, python3 (+ the engine's own build deps).

set -euo pipefail
cd "$(dirname "$0")/.."
ROOT=$PWD

BASE_REF=${1:?usage: scripts/perf-gate.sh <base-ref> [head-ref]}
HEAD_REF=${2:-}
WORK=${PERF_WORK:-$ROOT/build/perf}
SECONDS_PER=${PERF_SECONDS:-1}
JOBS=$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 2)
mkdir -p "$WORK"

command -v valgrind >/dev/null || { echo "perf-gate: valgrind is required (Linux: apt install valgrind)"; exit 2; }

LAUNCHER=()
command -v ccache >/dev/null && LAUNCHER=(-DCMAKE_C_COMPILER_LAUNCHER=ccache -DCMAKE_CXX_COMPILER_LAUNCHER=ccache)

# Checks out <ref> into a detached worktree (or uses the working tree) and echoes its path.
checkout() {
  local side=$1 ref=$2 dir="$WORK/src-$1"
  if [ -z "$ref" ]; then echo "$ROOT"; return; fi
  git worktree remove --force "$dir" >/dev/null 2>&1 || rm -rf "$dir"
  git worktree add --detach "$dir" "$ref" >&2
  if [ -f "$dir/.gitmodules" ]; then (cd "$dir" && git submodule update --init --recursive --depth 1 >&2); fi
  echo "$dir"
}

build_side() {
  local side=$1 src=$2
  echo "==> [$side] engine at $(git -C "$src" rev-parse --short HEAD)"
  cmake -G Ninja -S "$src/src/native/nota.engine" -B "$WORK/engine-$side" \
    -DCMAKE_BUILD_TYPE=Release "${LAUNCHER[@]}" >"$WORK/configure-$side.log"
  cmake --build "$WORK/engine-$side" --target nota_engine >"$WORK/build-$side.log"
  # The harness always comes from the head checkout, so both sides run identical scenarios.
  cmake -G Ninja -S "$ROOT/tests/perf" -B "$WORK/tools-$side" -DCMAKE_BUILD_TYPE=Release \
    -DNOTA_ENGINE_DIR="$src/src/native/nota.engine" -DNOTA_ENGINE_BUILD_DIR="$WORK/engine-$side" \
    >"$WORK/tools-configure-$side.log"
  cmake --build "$WORK/tools-$side" >"$WORK/tools-build-$side.log"
}

# One scenario on one side under Callgrind: id, label, instructions, peak.
run_one() {
  local side=$1 id=$2 tool="$WORK/tools-$1/nota_perf" out="$WORK/cg/$1-$2"
  local instr
  valgrind --tool=callgrind --instr-atstart=no --cache-sim=no --callgrind-out-file="$out.cg" \
    "$tool" run "$id" "$SECONDS_PER" >"$out.vg.out" 2>"$out.vg.err" || { echo -e "$id\terror\t0\t0"; return; }
  instr=$(sed -n 's/.*Collected : \([0-9]*\).*/\1/p' "$out.vg.err" | tail -1)
  # nota_perf prints: id, label, wall_ms_per_s, peak
  awk -F'\t' -v n="${instr:-0}" 'BEGIN{OFS="\t"} {print $1, $2, n, $4}' "$out.vg.out"
}
export -f run_one
export WORK SECONDS_PER

BASE_SRC=$(checkout base "$BASE_REF")
HEAD_SRC=$(checkout head "$HEAD_REF")
build_side base "$BASE_SRC" || { echo "perf-gate: the BASE engine failed to build; cannot compare (see $WORK/build-base.log)"; exit 2; }
build_side head "$HEAD_SRC"

mkdir -p "$WORK/cg"
"$WORK/tools-head/nota_perf" list >"$WORK/scenarios-head.txt"
"$WORK/tools-base/nota_perf" list >"$WORK/scenarios-base.txt"

for side in base head; do
  echo "==> [$side] running $(wc -l <"$WORK/scenarios-$side.txt") scenarios on $JOBS cores"
  xargs -P "$JOBS" -I{} bash -c "run_one $side {}" <"$WORK/scenarios-$side.txt" | sort >"$WORK/results-$side.tsv"
done

# Timing: one scenario at a time (no contention), base and head back to back, each round.
: >"$WORK/timing-base.tsv"; : >"$WORK/timing-head.tsv"
ROUNDS=${PERF_ROUNDS:-5}
echo "==> timing $ROUNDS interleaved rounds"
for r in $(seq "$ROUNDS"); do
  while read -r id; do
    for side in base head; do
      grep -qx "$id" "$WORK/scenarios-$side.txt" || continue
      "$WORK/tools-$side/nota_perf" run "$id" "$SECONDS_PER" | cut -f1,3 >>"$WORK/timing-$side.tsv" || true
    done
  done <"$WORK/scenarios-head.txt"
done

# Confirmation: a scenario over the time limit gets PERF_CONFIRM_ROUNDS more rounds; the
# fastest of all rounds decides. Noise only adds time, so a real slowdown survives this.
SUSPECTS=$(python3 "$ROOT/scripts/perf-gate.py" "$WORK/results-base.tsv" "$WORK/results-head.tsv" \
  --timing "$WORK/timing-base.tsv" "$WORK/timing-head.tsv" --time-limit "${PERF_TIME_LIMIT_PCT:-15}" --time-suspects)
if [ -n "$SUSPECTS" ]; then
  echo "==> re-timing $(wc -w <<<"$SUSPECTS") scenario(s) over the time limit"
  for r in $(seq "${PERF_CONFIRM_ROUNDS:-10}"); do
    for id in $SUSPECTS; do
      for side in base head; do
        "$WORK/tools-$side/nota_perf" run "$id" "$SECONDS_PER" | cut -f1,3 >>"$WORK/timing-$side.tsv" || true
      done
    done
  done
fi

python3 "$ROOT/scripts/perf-gate.py" "$WORK/results-base.tsv" "$WORK/results-head.tsv" \
  --timing "$WORK/timing-base.tsv" "$WORK/timing-head.tsv" --time-limit "${PERF_TIME_LIMIT_PCT:-15}" \
  --limit "${PERF_LIMIT_PCT:-1}" ${PERF_ACCEPT:+--accept} \
  --base-label "$(git -C "$BASE_SRC" rev-parse --short HEAD)" --head-label "$(git -C "$HEAD_SRC" rev-parse --short HEAD)" \
  --markdown "$WORK/report.md"
