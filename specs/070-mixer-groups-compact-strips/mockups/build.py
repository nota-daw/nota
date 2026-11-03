# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# Builds mixer.html — the specs/070 mockup (group bands, folding, compact strips) — with
# every colour taken from the theme files, so the mockup cannot drift from the app's tokens.
# Usage: python3 build.py  →  mixer.html next to this file; open with ?variant=light etc.

import json
import pathlib

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[2]
THEMES = ROOT / "src/managed/Nota.App/Theme/Themes"
VARIANTS = {"dark": "studio-dark", "light": "studio-light",
            "ember-graphite": "ember-graphite", "ember-paper": "ember-paper"}


def css_vars(theme_id):
    colors = json.loads((THEMES / f"{theme_id}.json").read_text())["colors"]
    out = []
    for k, v in colors.items():
        if len(v) == 9:   # #AARRGGBB → rgba
            a, r, g, b = (int(v[i:i + 2], 16) for i in (1, 3, 5, 7))
            v = f"rgba({r},{g},{b},{a / 255:.3f})"
        out.append(f"--{k}:{v};")
    return "".join(out)


themes_css = "\n".join(
    f'html[data-variant="{name}"]{{{css_vars(tid)}}}' for name, tid in VARIANTS.items())
html = (HERE / "mixer.template.html").read_text()
html = html.replace("/*THEMES*/", themes_css).replace("{{FONTS}}", (ROOT / "assets/fonts").as_uri())
(HERE / "mixer.html").write_text(html)
print(HERE / "mixer.html")
