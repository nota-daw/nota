import csv, statistics, subprocess

rows = list(csv.DictReader(open("/tmp/issue-142/results.csv")))
scen = [("stopped-inside-clip", "Stopped, playhead in clip"),
        ("playing-outside-clip", "Playing, outside the clip"),
        ("playing-inside-clip", "Playing, inside the clip")]
builds = [("before", "Before (develop db9990a)", "#8a8f98"), ("after", "This PR", "#f0a030")]
val = {(b, s): statistics.mean(float(r["waveform_renders_per_s"]) for r in rows if r["build"] == b and r["scenario"] == s)
       for b, _, _ in builds for s, _ in scen}

W, H, L, R, T, B = 760, 400, 70, 20, 70, 70
ymax = 35
pw, ph = W - L - R, H - T - B
y = lambda v: T + ph - v / ymax * ph
out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" font-family="DejaVu Sans, sans-serif">',
       f'<rect width="{W}" height="{H}" fill="#1c1d21"/>',
       f'<text x="{L}" y="28" fill="#e8e8ea" font-size="16" font-weight="bold">Audio clip editor: full waveform repaints per second</text>',
       f'<text x="{L}" y="48" fill="#a0a3aa" font-size="12">Clip open in the Clip panel, 120 BPM, mean of 2 runs × 8 s (Linux, Xvfb)</text>']
for v in range(0, ymax + 1, 5):
    out.append(f'<line x1="{L}" x2="{W - R}" y1="{y(v):.1f}" y2="{y(v):.1f}" stroke="#2e3036"/>')
    out.append(f'<text x="{L - 8}" y="{y(v) + 4:.1f}" fill="#a0a3aa" font-size="11" text-anchor="end">{v}</text>')
gw = pw / len(scen)
bw = 70
for i, (s, label) in enumerate(scen):
    cx = L + gw * (i + 0.5)
    for j, (b, _, color) in enumerate(builds):
        v = val[(b, s)]
        x = cx - bw - 4 + j * (bw + 8)
        h = max(1.5, v / ymax * ph)
        out.append(f'<rect x="{x:.1f}" y="{T + ph - h:.1f}" width="{bw}" height="{h:.1f}" fill="{color}"/>')
        out.append(f'<text x="{x + bw / 2:.1f}" y="{T + ph - h - 6:.1f}" fill="#e8e8ea" font-size="12" text-anchor="middle">{v:.1f}</text>')
    out.append(f'<text x="{cx:.1f}" y="{T + ph + 20}" fill="#c8c9cd" font-size="12" text-anchor="middle">{label}</text>')
lx = L
for b, name, color in builds:
    out.append(f'<rect x="{lx}" y="{H - 28}" width="12" height="12" fill="{color}"/>')
    out.append(f'<text x="{lx + 18}" y="{H - 18}" fill="#c8c9cd" font-size="12">{name}</text>')
    lx += 220
out.append('</svg>')
open("/tmp/issue-142/waveform-renders.svg", "w").write("\n".join(out))
subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-width", str(W * 2), "-height", str(H * 2), "-i",
                "/tmp/issue-142/waveform-renders.svg", "-frames:v", "1", "-update", "1", "/tmp/issue-142/waveform-renders.png"], check=True)
