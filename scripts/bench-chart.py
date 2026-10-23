#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
"""Turn scripts/bench-engine.sh output into charts and a Markdown summary.

Usage: scripts/bench-chart.py <out-dir> [label=Display name ...]

Reads <out-dir>/results.csv and machine.txt; takes the median of the runs per
build and scenario; writes into <out-dir>:
  cpu-playing.svg  - CPU while idle and while playing in real time (% of one core)
  bounce-time.svg  - offline render time per second of audio (lower is faster)
  bounce-cpu.svg   - CPU work per second of audio during an offline render
  export-time.svg / export-cpu.svg - the same for exports in 8192-frame chunks, when there
                     are any
  small-buffer-late.svg / small-buffer-cpu.svg - late blocks and CPU for the rt runs
                     at buffers other than 512 (heavy sessions), when there are any
  summary.md       - the same numbers as Markdown tables, plus the machine
Each SVG is also rendered to a 2x PNG (rsvg-convert or headless Chrome) for PR descriptions.
Builds keep the order given on the command line (else CSV order). Stdlib only.
"""

import csv
import math
import os
import shutil
import statistics
import subprocess
import sys
from collections import OrderedDict

# Categorical slots 1-3 (validated all-pairs, light surface), chart chrome.
SERIES = ["#2a78d6", "#eb6834", "#1baf7a"]
SURFACE, INK, INK2, MUTED, GRID, AXIS = "#fcfcfb", "#0b0b0b", "#52514e", "#898781", "#e1e0d9", "#c3c2b7"
FONT = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif"

SCEN_NAME = {"audio": "audio", "inst": "synth"}


def group_name(scenario, tracks, block="512", mode=""):
    if scenario == "idle":
        return "Idle"
    n = int(tracks)
    name = f"{n} {SCEN_NAME[scenario]} track{'s' if n != 1 else ''}"
    if mode == "export":
        return f"{name} · {block}-frame chunks"
    return name if block in ("", "512") else f"{name} · {block}-sample buffer"


def load(out):
    rows = list(csv.DictReader(open(os.path.join(out, "results.csv"))))
    data = OrderedDict()  # (scenario, tracks, mode) -> label -> [rows]
    labels = []
    for r in rows:
        if r["label"] not in labels:
            labels.append(r["label"])
        block = r.get("block") or ("" if r["scenario"] == "idle" else "512")
        r["block"] = block
        if r.get("misses") not in (None, "") and block:  # late blocks, % of the run's blocks
            r["late_pct"] = str(100.0 * float(r["misses"]) / (10 * 48000 / int(block)))
        data.setdefault((r["scenario"], r["tracks"], r["mode"], block), {}).setdefault(r["label"], []).append(r)
    return data, labels


def med(rs, field):
    vals = [float(r[field]) for r in rs if r.get(field)]
    return statistics.median(vals) if vals else None


def nice_step(raw):
    # 1, 2, 2.5 or 5 times a power of ten: ticks read as round numbers.
    if raw <= 0:
        return 1.0
    mag = 10 ** math.floor(math.log10(raw))
    return next(m * mag for m in (1, 2, 2.5, 5, 10) if m * mag >= raw)


def fmt(v):
    return f"{v:,.0f}" if v >= 100 else (f"{v:.1f}" if v >= 1 else f"{v:.2f}".rstrip("0").rstrip(".") or "0")


def bar_chart(path, title, subtitle, unit, groups, labels, names):
    """groups: [(group label, {label: value})]."""
    W, H = 960, 440
    left, right, top, bottom = 64, 24, 96, 56
    pw, ph = W - left - right, H - top - bottom
    peak = max((v for _, vals in groups for v in vals.values() if v is not None), default=1) * 1.08
    step = nice_step(peak / 4)
    ticks = max(1, math.ceil(peak / step))
    vmax = step * ticks
    gw = pw / len(groups)
    bw = min(40, (gw - 24) / len(labels) - 2)
    y = lambda v: top + ph - ph * v / vmax
    s = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" font-family="{FONT}">',
         f'<rect width="{W}" height="{H}" fill="{SURFACE}"/>',
         f'<text x="{left}" y="34" font-size="18" font-weight="600" fill="{INK}">{title}</text>',
         f'<text x="{left}" y="56" font-size="13" fill="{INK2}">{subtitle}</text>']
    # legend (identity never colour-alone: labels sit next to the swatches)
    lx = left
    for i, lab in enumerate(labels):
        s.append(f'<rect x="{lx}" y="70" width="12" height="12" rx="3" fill="{SERIES[i]}"/>')
        s.append(f'<text x="{lx + 18}" y="80" font-size="13" fill="{INK2}">{names.get(lab, lab)}</text>')
        lx += 30 + 7.5 * len(names.get(lab, lab))
    # grid + y axis
    for k in range(ticks + 1):
        v = step * k
        yy = y(v)
        s.append(f'<line x1="{left}" x2="{W - right}" y1="{yy:.1f}" y2="{yy:.1f}" stroke="{GRID if k else AXIS}" stroke-width="1"/>')
        tick = f"{v:,.0f}" if step >= 1 else f"{v:g}"
        s.append(f'<text x="{left - 8}" y="{yy + 4:.1f}" font-size="12" fill="{MUTED}" text-anchor="end">{tick}</text>')
    s.append(f'<text x="14" y="{top + ph / 2:.0f}" font-size="12" fill="{MUTED}" transform="rotate(-90 14 {top + ph / 2:.0f})" text-anchor="middle">{unit}</text>')
    for gi, (gname, vals) in enumerate(groups):
        gx = left + gi * gw + (gw - len(labels) * (bw + 2) + 2) / 2
        for i, lab in enumerate(labels):
            v = vals.get(lab)
            if v is None:
                continue
            x = gx + i * (bw + 2)
            h = max(ph * v / vmax, 1.5)
            r = min(4, h / 2, bw / 2)
            yt = top + ph - h
            # rounded data end, square at the baseline
            s.append(f'<path d="M{x:.1f},{top + ph:.1f} V{yt + r:.1f} Q{x:.1f},{yt:.1f} {x + r:.1f},{yt:.1f} '
                     f'H{x + bw - r:.1f} Q{x + bw:.1f},{yt:.1f} {x + bw:.1f},{yt + r:.1f} V{top + ph:.1f} Z" fill="{SERIES[i]}"/>')
            s.append(f'<text x="{x + bw / 2:.1f}" y="{yt - 5:.1f}" font-size="11" fill="{INK2}" text-anchor="middle">{fmt(v)}</text>')
        s.append(f'<text x="{left + gi * gw + gw / 2:.1f}" y="{top + ph + 22}" font-size="13" fill="{INK2}" text-anchor="middle">{gname}</text>')
    s.append("</svg>")
    open(path, "w").write("\n".join(s))


def to_png(svg, w=960, h=440, scale=2):
    """Rasterize at 2x with rsvg-convert, else headless Chrome; skip if neither exists."""
    png = svg[:-4] + ".png"
    if shutil.which("rsvg-convert"):
        subprocess.run(["rsvg-convert", "-z", str(scale), "-o", png, svg], check=True)
        return
    chrome = next((c for c in ("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                               shutil.which("google-chrome") or "", shutil.which("chromium") or "")
                   if c and os.path.exists(c)), None)
    if chrome:
        subprocess.run([chrome, "--headless", "--disable-gpu", f"--window-size={w},{h}",
                        f"--force-device-scale-factor={scale}", f"--screenshot={png}", "file://" + os.path.abspath(svg)],
                       capture_output=True)


def main():
    if len(sys.argv) < 2:
        print(__doc__, file=sys.stderr)
        sys.exit(2)
    out = sys.argv[1]
    names = dict(a.split("=", 1) for a in sys.argv[2:])
    data, labels = load(out)
    if names:
        labels = [l for l in names if l in labels] + [l for l in labels if l not in names]

    def series(mode, field, small=False):
        groups = []
        for (scen, tracks, m, block), by in data.items():
            if m != mode or (mode != "export" and (block not in ("", "512")) != small):
                continue
            groups.append((group_name(scen, tracks, block, m), {l: med(by.get(l, []), field) for l in labels}))
        return groups

    playing = series("idle", "cpu_pct") + series("rt", "cpu_pct")
    bounce_t = series("offline", "wall_ms_per_s")
    bounce_c = series("offline", "cpu_ms_per_s")
    export_t = series("export", "wall_ms_per_s")
    export_c = series("export", "cpu_ms_per_s")
    small_late = series("rt", "late_pct", small=True)
    small_cpu = series("rt", "cpu_pct", small=True)
    charts = [
        ("cpu-playing.svg", "CPU while idle and while playing",
         "Process CPU, median of runs. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.",
         "% of one core", playing),
        ("bounce-time.svg", "Offline render (bounce) time",
         "Wall-clock time per second of audio, 512-frame blocks back-to-back. Lower is faster.",
         "ms per second of audio", bounce_t),
        ("bounce-cpu.svg", "CPU work during an offline render",
         "CPU time summed over all threads, per second of audio. Lower uses less energy.",
         "CPU ms per second of audio", bounce_c),
    ]
    if export_t:
        charts += [
            ("export-time.svg", "Export and freeze render time",
             "Render time per second of audio, 8192-frame chunks with 2 ms between them (the app's exporter). Lower is faster.",
             "ms per second of audio", export_t),
            ("export-cpu.svg", "CPU work during an export",
             "CPU time summed over all threads, per second of audio. Lower uses less energy.",
             "CPU ms per second of audio", export_c),
        ]
    if small_late:
        charts += [
            ("small-buffer-late.svg", "Late blocks at small buffers (heavy sessions)",
             "Blocks that took longer than their buffer period (a dropout with a device). Lower is better.",
             "% of blocks late", small_late),
            ("small-buffer-cpu.svg", "CPU at small buffers (heavy sessions)",
             "Process CPU while playing in real time. Lower is better.",
             "% of one core", small_cpu),
        ]
    for fname, title, sub, unit, groups in charts:
        p = os.path.join(out, fname)
        bar_chart(p, title, sub, unit, groups, labels, names)
        to_png(p)

    md = []
    machine = open(os.path.join(out, "machine.txt")).read().strip() if os.path.exists(os.path.join(out, "machine.txt")) else ""
    for _, title, sub, unit, groups in charts:
        md.append(f"**{title}** — {unit}, median. {sub.split('. ', 1)[-1]}\n")
        md.append("| | " + " | ".join(names.get(l, l) for l in labels) + " |")
        md.append("|---|" + "---:|" * len(labels))
        for g, vals in groups:
            md.append(f"| {g} | " + " | ".join(fmt(vals[l]) if vals[l] is not None else "—" for l in labels) + " |")
        md.append("")
    if machine:
        md.append("<details><summary>Machine and builds</summary>\n\n```\n" + machine + "\n```\n</details>")
    open(os.path.join(out, "summary.md"), "w").write("\n".join(md) + "\n")
    print(os.path.join(out, "summary.md"))


if __name__ == "__main__":
    main()
