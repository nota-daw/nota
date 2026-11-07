# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# Builds accent.html — the specs/084 mockup (Settings → Appearance → Accent, the custom
# colour picker, every preset in every theme) — with every colour computed from the theme
# files by the same derivation the app uses (research.md § Derivation), so the mockup
# shows real values, not a painter's guess.
# Usage: python3 build.py  →  accent.html next to this file.

import json
import math
import pathlib

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[2]
THEMES = ROOT / "src/managed/Nota.App/Theme/Themes"
IDS = ["studio-dark", "studio-light", "ember-graphite", "ember-paper"]
NAMES = {"studio-dark": "Studio Dark", "studio-light": "Studio Light",
         "ember-graphite": "Ember Graphite", "ember-paper": "Ember Paper"}

# The accent family. Solid tokens move with the accent's lightness; blend tokens are
# washes over the theme's surfaces and keep the theme's lightness.
SOLID = ["Accent", "AccentBright", "AccentHover", "AccentDeep", "AccentPale",
         "SystemAccent", "SystemAccentLight1", "SystemAccentLight2", "SystemAccentLight3",
         "SystemAccentDark1", "SystemAccentDark2", "SystemAccentDark3"]
BLEND = ["AccentDim", "AccentEdge", "AccentMute", "AccentSubtle", "BorderBrass"]

# id, name, OKLCH chroma, OKLCH hue. Orange is the theme files themselves.
PRESETS = [("orange", "Orange", None, None), ("lime", "Lime", 0.15, 125), ("aqua", "Aqua", 0.12, 185),
           ("sky", "Sky", 0.13, 224), ("violet", "Violet", 0.15, 295), ("magenta", "Magenta", 0.16, 330),
           ("pink", "Pink", 0.15, 355)]
CUSTOM = "#4C6FFF"
# Picker examples: one worn as is, one adjusted, one refused (no lightness within reach passes).
EXAMPLES = ["#00B5D8", "#4C6FFF", "#3FA7FF", "#8C8C8C"]
MAX_SHIFT = 0.12   # OKLCH lightness a custom colour may be moved, at most
MIN_CHROMA = 0.05  # below this a colour reads as grey


def hx(h):
    h = h.lstrip("#")[-6:]
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


def tohex(rgb):
    return "#" + "".join(f"{round(min(1, max(0, v)) * 255):02X}" for v in rgb)


def lin(c): return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
def gam(c): return 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def oklch(rgb):
    r, g, b = (lin(v) for v in rgb)
    l = (0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b) ** (1 / 3)
    m = (0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b) ** (1 / 3)
    s = (0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b) ** (1 / 3)
    L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s
    A = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s
    B = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s
    return L, math.hypot(A, B), math.degrees(math.atan2(B, A)) % 360


def srgb(L, C, h):
    A, B = C * math.cos(math.radians(h)), C * math.sin(math.radians(h))
    l = (L + 0.3963377774 * A + 0.2158037573 * B) ** 3
    m = (L - 0.1055613458 * A - 0.0638541728 * B) ** 3
    s = (L - 0.0894841775 * A - 1.2914855480 * B) ** 3
    lr = (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
          -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
          -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s)
    ok = all(-1e-4 <= v <= 1 + 1e-4 for v in lr)
    return [gam(min(1, max(0, v))) for v in lr], ok


def in_gamut(L, C, h):
    L = min(max(L, 0.0), 1.0)
    for _ in range(60):
        rgb, ok = srgb(L, C, h)
        if ok:
            return tohex(rgb)
        C *= 0.95
    return tohex(srgb(L, 0, h)[0])


def lum(h):
    r, g, b = (lin(v) for v in hx(h))
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(a, b):
    la, lb = lum(a), lum(b)
    return (max(la, lb) + .05) / (min(la, lb) + .05)


def lab(h):
    r, g, b = (lin(v) for v in hx(h))
    x, y, z = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047, 0.2126 * r + 0.7152 * g + 0.0722 * b, \
        (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883
    def t(v): return v ** (1 / 3) if v > 0.008856 else 7.787 * v + 16 / 116
    return 116 * t(y) - 16, 500 * (t(x) - t(y)), 200 * (t(y) - t(z))


def delta_e(a, b): return math.dist(lab(a), lab(b))


# Colour difference with lightness left out: a lighter or darker copy of the mute amber is
# still the mute amber. Orange's smallest today is 16.7 (Mute, Ember Paper).
def delta_ab(a, b):
    la, lb = lab(a), lab(b)
    return math.hypot(la[1] - lb[1], la[2] - lb[2])
def ink(f): return "#17181A" if contrast("#17181A", f) >= contrast("#FFFFFF", f) else "#FFFFFF"


T = {i: json.loads((THEMES / f"{i}.json").read_text())["colors"] for i in IDS}


def tint(c, L1, C1, h1):
    La, Ca, ha = oklch(hx(c["Accent"]))
    out = {}
    for k in SOLID + BLEND:
        Lk, Ck, hk = oklch(hx(c[k]))
        out[k] = in_gamut(Lk + (L1 - La if k in SOLID else 0), Ck * (C1 / Ca), hk + (h1 - ha))
    out["TextOnAccent"] = ink(out["Accent"])
    return out


def failures(c, o, tracks):
    f = []
    for s in ("BgApp", "Panel", "BgSunken"):
        if contrast(o["AccentBright"], c[s]) < 2.3:
            f.append("contrast")
    if contrast(ink(o["Accent"]), o["Accent"]) < 4.5:
        f.append("ink")
    for s, word in STATES:
        if delta_e(o["Accent"], c[s]) < 15 or delta_ab(o["Accent"], c[s]) < 15:
            f.append(word)
    if tracks:
        f += [f"Track{i}" for i in range(1, 10) if delta_e(o["Accent"], c[f"Track{i}"]) < 15]
    return f


STATES = (("Record", "the record red"), ("Mute", "mute"), ("Solo", "solo"), ("Success", "the meter green"))


def clash(c, colour):
    """The state colour a chosen colour sits on, if any — checked on the colour itself, so
    moving its lightness cannot talk it past the record red."""
    for s, word in STATES:
        if delta_e(colour, c[s]) < 15 or delta_ab(colour, c[s]) < 15:
            return word
    return None


def wear(tid, C1, h1, L1=None, tracks=False, why=None):
    c = T[tid]
    start = oklch(hx(c["Accent"]))[0] if L1 is None else L1
    if C1 < MIN_CHROMA:
        if why is not None: why.append("grey")
        return None, False
    if L1 is not None:
        word = clash(c, in_gamut(L1, C1, h1))
        if word:
            if why is not None: why.append(word)
            return None, False
    for step in range(int(MAX_SHIFT / 0.005) + 1 if L1 is not None else 80):
        for sgn in ([0] if step == 0 else [1, -1]):
            L = start + sgn * step * 0.005
            if 0.2 < L < 0.95:
                o = tint(c, L, C1, h1)
                if not failures(c, o, tracks):
                    return o, step != 0
    if why is not None: why.append("unreadable")
    return None, False


table = {}
for tid in IDS:
    table[tid] = {}
    for pid, _, C, h in PRESETS:
        if pid == "orange":
            table[tid][pid] = {k: T[tid][k] for k in SOLID + BLEND + ["TextOnAccent"]}
            continue
        o, _ = wear(tid, C, h, tracks=True)
        assert o is not None, (tid, pid)
        table[tid][pid] = o
    L, C, h = oklch(hx(CUSTOM))
    o, adjusted = wear(tid, C, h, L1=L)
    table[tid]["custom"] = o
    table[tid]["custom-adjusted"] = adjusted

examples = {}
for hexv in EXAMPLES:
    L, C, h = oklch(hx(hexv))
    row, why = {}, []
    for tid in IDS:
        w = []
        o, adjusted = wear(tid, C, h, L1=L, why=w)
        row[tid] = None if o is None else {"Accent": o["Accent"], "adjusted": adjusted}
        if w: why.append((NAMES[tid], w[0]))
    import colorsys
    hh, ss, vv = colorsys.rgb_to_hsv(*hx(hexv))
    examples[hexv] = {"rows": row, "why": why, "hsv": [round(hh * 360), round(ss * 100), round(vv * 100)]}
    print(hexv, row)

base = {tid: {k: v for k, v in T[tid].items() if len(v) == 7} for tid in IDS}
html = (HERE / "accent.template.html").read_text()
html = html.replace("/*DATA*/", "const THEMES = " + json.dumps(base) + ";\nconst ACCENTS = " + json.dumps(table)
                    + ";\nconst PRESETS = " + json.dumps([[p, n] for p, n, _, _ in PRESETS])
                    + ";\nconst NAMES = " + json.dumps(NAMES) + ";\nconst CUSTOM = " + json.dumps(CUSTOM) + ";\nconst EXAMPLES = " + json.dumps(examples) + ";")
html = html.replace("{{FONTS}}", "../../../assets/fonts")
(HERE / "accent.html").write_text(html)
for tid in IDS:
    print(tid, " ".join(f"{p}={table[tid][p]['Accent']}" for p, *_ in PRESETS), "custom=" + table[tid]["custom"]["Accent"])
