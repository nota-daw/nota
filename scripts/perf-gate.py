#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# Compares two perf-gate result sets (scripts/perf-gate.sh) and decides the gate.
# Work rows: id, label, instructions, peak. Timing rows: id, wall_ms_per_s (one per
# round; the fastest is used: noise only ever adds time). Effect scenarios are compared on the effect's own work:
# (scenario - Synth baseline) / copies, so a slower effect is not hidden behind the
# synth that feeds it. Timing compares whole scenarios, base and head medians.

import argparse
import os
import sys

EFFECT_COPIES = 8  # must match kEffectCopies in tests/perf/nota_perf.cpp
SILENT = 1e-6
TIME_FLOOR_MS = 0.25  # ignore timing changes smaller than this (ms per second of audio)


def load(path):
    rows = {}
    with open(path) as f:
        for line in f:
            p = line.rstrip("\n").split("\t")
            if len(p) < 4:
                continue
            ok = p[1] != "error"
            rows[p[0]] = {"label": p[1], "instr": int(p[2]) if ok else 0, "peak": float(p[3]), "ok": ok}
    return rows


def load_timing(path):
    runs = {}
    if path:
        with open(path) as f:
            for line in f:
                p = line.split()
                if len(p) == 2:
                    runs.setdefault(p[0], []).append(float(p[1]))
    return {k: min(v) for k, v in runs.items()}


def work(rows, sid):
    """Instructions attributed to the scenario's subject."""
    r = rows[sid]
    if sid.startswith("effect-") and sid != "effect-base" and "effect-base" in rows:
        return (r["instr"] - rows["effect-base"]["instr"]) / EFFECT_COPIES
    return r["instr"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("base")
    ap.add_argument("head")
    ap.add_argument("--limit", type=float, default=1.0, help="allowed growth in instructions, percent")
    ap.add_argument("--timing", nargs=2, metavar=("BASE", "HEAD"))
    ap.add_argument("--time-limit", type=float, default=15.0, help="allowed growth in median wall time, percent")
    ap.add_argument("--accept", action="store_true", help="regressions approved for this change")
    ap.add_argument("--base-label", default="base")
    ap.add_argument("--head-label", default="head")
    ap.add_argument("--markdown")
    ap.add_argument("--time-suspects", action="store_true",
                    help="only print the ids whose timing is over the limit (for a confirmation pass)")
    a = ap.parse_args()

    base, head = load(a.base), load(a.head)
    tbase, thead = (load_timing(a.timing[0]), load_timing(a.timing[1])) if a.timing else ({}, {})
    if a.time_suspects:
        for sid in sorted(set(tbase) & set(thead)):
            tb, th = tbase[sid], thead[sid]
            if tb > 0 and (th - tb) / tb * 100 > a.time_limit and th - tb > TIME_FLOOR_MS:
                print(sid)
        return 0
    rows, failures = [], []
    for sid in sorted(set(base) | set(head)):
        label = (head.get(sid) or base.get(sid))["label"]
        if sid not in base:
            rows.append((sid, label, None, None, None, None, None, "new"))
            continue
        if sid not in head:
            rows.append((sid, label, None, None, None, None, None, "removed"))
            continue
        b, h = base[sid], head[sid]
        if not b["ok"] or not h["ok"]:
            verdict = "error" if not h["ok"] else "base error"
            rows.append((sid, label, None, None, None, tbase.get(sid), thead.get(sid), verdict))
            if not h["ok"]:
                failures.append(sid)
            continue
        wb, wh = work(base, sid), work(head, sid)
        pct = (wh - wb) / wb * 100 if wb > 0 else 0.0
        tb, th = tbase.get(sid), thead.get(sid)
        tpct = (th - tb) / tb * 100 if tb and th else 0.0
        verdict = "ok"
        if b["peak"] > SILENT and h["peak"] <= SILENT:
            verdict = "went silent"
        elif pct > a.limit and tpct > a.time_limit and th - tb > TIME_FLOOR_MS:
            verdict = "SLOWER (work + time)"
        elif pct > a.limit:
            verdict = "SLOWER (work)"
        elif tpct > a.time_limit and th - tb > TIME_FLOOR_MS:
            verdict = f"SLOWER (time {tpct:+.0f}%)"
        if verdict != "ok":
            failures.append(sid)
        rows.append((sid, label, wb, wh, pct, tb, th, verdict))

    def order(r):
        bad = r[7] not in ("ok", "new", "removed", "base error")
        return (not bad, -(r[4] or 0))

    rows.sort(key=order)
    out = [f"## Engine performance: {a.base_label} → {a.head_label}", ""]
    if failures:
        state = "accepted for this change" if a.accept else "**FAIL**"
        out.append(f"{state}: {len(failures)} scenario(s) slower than the base: more than {a.limit:g}% more CPU "
                   f"instructions, or more than {a.time_limit:g}% more wall time (or now silent / erroring).")
    else:
        out.append(f"PASS: no scenario does more than {a.limit:g}% more work or takes more than "
                   f"{a.time_limit:g}% longer than the base.")
    out += ["", "Work = CPU instructions per render, all engine threads (Valgrind, deterministic; effects: the "
            "effect's own share). Wall = fastest of the interleaved rounds, ms to render 1 s of audio on this machine; a scenario over the time limit is re-timed with more rounds before it fails.", "",
            "| Scenario | | Work base | Work PR | Change | Wall base | Wall PR | Verdict |",
            "| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |"]

    def num(v):
        return "" if v is None else f"{v / 1e6:,.2f} M"

    for sid, label, wb, wh, pct, tb, th, verdict in rows:
        change = "" if pct is None else f"{pct:+.2f}%"
        walls = ["" if t is None else f"{t:.2f} ms" for t in (tb, th)]
        out.append(f"| `{sid}` | {label} | {num(wb)} | {num(wh)} | {change} | {walls[0]} | {walls[1]} | {verdict} |")
    md = "\n".join(out) + "\n"

    print(md)
    if a.markdown:
        with open(a.markdown, "w") as f:
            f.write(md)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a") as f:
            f.write(md)
    return 1 if failures and not a.accept else 0


if __name__ == "__main__":
    sys.exit(main())
