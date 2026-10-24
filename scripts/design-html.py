#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# Regenerates DESIGN.html, the visual reference for DESIGN.md.
#
# Every colour on the page comes from the theme files (src/managed/Nota.App/Theme/Themes/
# *.json, in NotaThemes.BuiltInOrder), mapped onto the Brush.* keys the app publishes
# (NotaPalette.KeyMap); the four Shadow.* keys are built from theme colours exactly as
# NotaThemeService builds them; the corner scale and the Control.* / Space.* sizes come
# from NotaTheme.axaml. The type scale, the size table and the device widths are read from
# DESIGN.md itself, so the page and the guide cannot disagree. All four themes can be
# switched on the page. The controls and cards are live HTML, CSS and SVG: no images.
#
# Run after changing a token or DESIGN.md:  python3 scripts/design-html.py
# Standard library only. A missing token, key or table stops the script with its name.
import re, os, sys, json, math, html

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
THEME_DIR = ROOT + '/src/managed/Nota.App/Theme'
OUT = ROOT + '/DESIGN.html'


def fail(msg):
    sys.exit(f'design-html.py: {msg}')


def read(path):
    try:
        return open(path, encoding='utf-8').read()
    except OSError as e:
        fail(f'cannot read {os.path.relpath(path, ROOT)}: {e.strerror}')


AXAML = read(THEME_DIR + '/NotaTheme.axaml')
PALETTE = read(THEME_DIR + '/NotaPalette.cs')
GUIDE = read(ROOT + '/DESIGN.md')

# ---- Themes and brushes ---------------------------------------------------------------

m = re.search(r'BuiltInOrder\s*=\s*\{([^}]*)\}', read(THEME_DIR + '/NotaColourTheme.cs'))
if not m:
    fail('NotaThemes.BuiltInOrder not found in NotaColourTheme.cs')
THEMES = [json.loads(read(f'{THEME_DIR}/Themes/{i}.json')) for i in re.findall(r'"([a-z0-9-]+)"', m.group(1))]
if not THEMES:
    fail('BuiltInOrder lists no themes')
BY_ID = {th['id']: th for th in THEMES}
for need_id in ('studio-dark', 'studio-light'):
    if need_id not in BY_ID:
        fail(f'theme {need_id} is not in BuiltInOrder')

m = re.search(r'KeyMap\s*=\s*new\([^)]*\)\s*\{(.*?)\n    \};', PALETTE, re.S)
if not m:
    fail('NotaPalette.KeyMap not found')
KEYMAP = re.findall(r'\["Brush\.(\w+)"\]\s*=\s*(Wash\([^)]*\)|[\w\[\]]+)', m.group(1))


def brushes(c):
    """Brush key (without 'Brush.') -> hex for one theme's colours, through KeyMap."""
    out = {}
    for key, expr in KEYMAP:
        mm = re.match(r'TrackSlots\[(\d)\]', expr)
        if mm:
            out[key] = c[f'Track{int(mm.group(1)) + 1}']
            continue
        mm = re.match(r'ReturnSlots\[(\d)\]', expr)
        if mm:
            out[key] = c['Return' + 'AB'[int(mm.group(1))]]
            continue
        mm = re.match(r'Wash\((\w+),\s*0x([0-9A-Fa-f]{2})\)', expr)
        if mm:
            out[key] = '#' + mm.group(2).upper() + c[mm.group(1)].lstrip('#')[-6:]
            continue
        if expr not in c:
            fail(f'KeyMap maps Brush.{key} to {expr}, which is not a theme token')
        out[key] = c[expr]
    return out


SERVICE = read(THEME_DIR + '/NotaThemeService.cs')
SHADOW_SRC = re.findall(r'res\["Shadow\.(\w+)"\]\s*=\s*(.*?);\n', SERVICE, re.S)
if not SHADOW_SRC:
    fail('NotaThemeService.cs builds no Shadow.* resource (res["Shadow.X"] = ...;)')


def split_top(text, sep):
    """Split on `sep` outside brackets."""
    out, depth, cur = [], 0, ''
    for ch in text:
        if ch in '([{':
            depth += 1
        elif ch in ')]}':
            depth -= 1
        if ch == sep and depth == 0:
            out.append(cur)
            cur = ''
        else:
            cur += ch
    out.append(cur)
    return out


def pick_kind(expr, dark):
    """Resolve a top-level `t.IsDark ? a : b`."""
    expr = expr.strip()
    if not expr.startswith('t.IsDark'):
        return expr
    rest = expr[len('t.IsDark'):].strip().lstrip('?')
    a, b = split_top(rest, ':')[0], ':'.join(split_top(rest, ':')[1:])
    return (a if dark else b).strip()


def shadows(c, dark):
    """Every Shadow.* that NotaThemeService.ApplyResources builds, for one theme, as Avalonia
    BoxShadows strings ("inset x y blur spread #colour, ...")."""
    out = {}
    for key, expr in SHADOW_SRC:
        parts = []
        for body in re.findall(r'new BoxShadow\s*\{([^}]*)\}', pick_kind(expr, dark)):
            props = {}
            for item in split_top(body, ','):
                if '=' not in item:
                    continue
                k, v = item.split('=', 1)
                props[k.strip()] = pick_kind(v, dark)
            col = props.get('Color', '')
            mm = re.fullmatch(r't\["(\w+)"\]', col)
            nb = re.fullmatch(r'Black\(\s*([.\d]+)\s*,\s*([.\d]+)\s*\)', col)
            if mm:
                hexv = c[mm.group(1)]
            elif nb:
                hexv = '#%02X000000' % round(float(nb.group(1) if dark else nb.group(2)) * 255)
            else:
                fail(f'Shadow.{key}: cannot read the colour "{col}" in NotaThemeService.cs')
            nums = ' '.join(props.get(n, '0') for n in ('OffsetX', 'OffsetY', 'Blur', 'Spread'))
            parts.append(('inset ' if props.get('IsInset') == 'true' else '') + f'{nums} {hexv}')
        if not parts:
            fail(f'Shadow.{key}: no BoxShadow found in NotaThemeService.cs')
        out[key] = ', '.join(parts)
    return out


NEEDED_SHADOWS = ('Raised', 'Sunken', 'Popover', 'Modal', 'Card', 'Section', 'Groove', 'Lifted', 'SwitchInset')
missing = [k for k in NEEDED_SHADOWS if k not in dict(SHADOW_SRC)]
if missing:
    fail('NotaThemeService.cs builds no Shadow.' + ', Shadow.'.join(missing))

B = {th['id']: brushes(th['colors']) for th in THEMES}
# Slot-only colours the page uses (NotaPalette fields with no Brush.* key).
SLOTS = ('SurfaceDeep', 'AccentPale', 'Sage', 'TooltipBg')


def vname(th):
    """The page's variant names: Studio keeps "dark" (the default) and "light"."""
    return {'studio-dark': 'dark', 'studio-light': 'light'}.get(th['id'], th['id'])


def css(h):
    h = h.lstrip('#')
    if len(h) == 8:
        a = int(h[:2], 16) / 255
        r, g, b = (int(h[i:i + 2], 16) for i in (2, 4, 6))
        return f'rgba({r},{g},{b},{a:.3f})'
    return '#' + h.upper()


def css_shadow(s):
    """Avalonia "inset 0 1 0 0 #12FFFFFF, 0 10 28 0 #6B000000" -> CSS box-shadow."""
    out = []
    for part in s.split(','):
        toks = []
        for tok in part.split():
            if tok == 'inset':
                toks.append(tok)
            elif tok.startswith('#'):
                toks.append(css(tok))
            else:
                toks.append('0' if float(tok) == 0 else tok + 'px')
        out.append(' '.join(toks))
    return ', '.join(out)


def var(k):
    return '--' + re.sub(r'(?<!^)(?=[A-Z])', '-', k).lower()


def rgb(h):
    h = h.lstrip('#')[-6:]
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def hexs(c):
    return '#%02X%02X%02X' % c


def lum(c):
    def ch(v):
        x = v / 255
        return x / 12.92 if x <= 0.03928 else ((x + 0.055) / 1.055) ** 2.4
    r, g, b = c
    return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b)


def contrast(a, b):
    la, lb = lum(a), lum(b)
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)


INK_DARK, INK_LIGHT = (0x17, 0x18, 0x1A), (0xFF, 0xFF, 0xFF)


def ink_on(fill):
    """NotaPalette.ReadableInkOn: dark ink or white, whichever reads better."""
    return INK_DARK if contrast(INK_DARK, fill) >= contrast(INK_LIGHT, fill) else INK_LIGHT


def lab(c):
    def f(t):
        return t ** (1 / 3) if t > 216 / 24389 else (24389 / 27 * t + 16) / 116
    def lin(v):
        x = v / 255
        return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4
    r, g, b = (lin(v) for v in c)
    x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047
    y = 0.2126 * r + 0.7152 * g + 0.0722 * b
    z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883
    fx, fy, fz = f(x), f(y), f(z)
    return 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)


def delta_e(a, b):
    return math.dist(lab(a), lab(b))


# Every Brush key the page names must exist in every theme: a renamed or dropped token
# fails here with its name, rather than as a KeyError deep in the template.
NEEDED_BRUSHES = '''Seam BgApp Panel SurfaceCard SurfaceRaised SurfaceHover SurfaceInset LaneB BgSunken
 Well WellTrack KnobTrack Groove DeviceSection DeviceSectionEdge DeviceHeader Bevel KnobCap Rule GridBeat
 GridBar VizGrid BorderBrass TextPrimary TextSecondary TextTertiary TextDisabled TextHeading TextStrong
 TextMuted TextAxis TextFaint VizInk TextOnAccent Accent AccentSoft AccentBright AccentDeep Record RecordInk
 Mute Solo MeterSafe MeterHot MeterOver TrackHeaderSelected LaneSelected Danger Success SuccessInk Warning
 Track1 Track2 Track3 Track4 Track5 Track6 Track7 Track8 Track9 ReturnA ReturnB Master ChromaTeal
 ChromaRose ChromaSteel'''.split()
for th in THEMES:
    missing = [k for k in NEEDED_BRUSHES if k not in B[th['id']]]
    missing += [k for k in SLOTS if k not in th['colors']]
    if missing:
        fail(f'theme {th["id"]} has no value for: {", ".join(missing)}')

# ---- Geometry from NotaTheme.axaml ----------------------------------------------------

RADIUS = {k: float(v) for k, v in re.findall(r'<CornerRadius x:Key="Radius\.(\w+)">([0-9.]+)<', AXAML)}
SIZES = {k: float(v) for k, v in re.findall(r'x:Key="((?:Control|Space)\.\w+)">([0-9.]+)<', AXAML)}
RADIUS_ORDER = ['Docked', 'Card', 'Group', 'Field', 'Cell', 'ValueBar', 'Switch', 'Fine']
missing = [k for k in RADIUS_ORDER if k not in RADIUS]
if missing:
    fail('NotaTheme.axaml has no Radius.' + ', Radius.'.join(missing))


def px(v):
    return ('%g' % v) + 'px'


R = {k: px(v) for k, v in RADIUS.items()}

# ---- Tables read from DESIGN.md -------------------------------------------------------


def md_table(start, label):
    """The first Markdown table after the line matching `start`: a list of cell lists."""
    mm = re.search(start, GUIDE, re.M)
    if not mm:
        fail(f'DESIGN.md: {label} not found')
    rows, seen = [], False
    for line in GUIDE[mm.end():].splitlines():
        if line.startswith('|'):
            seen = True
            cells = [c.strip() for c in line.strip().strip('|').split('|')]
            if all(re.fullmatch(r':?-+:?', c) for c in cells):
                continue
            rows.append(cells)
        elif seen:
            break
    if len(rows) < 2:
        fail(f'DESIGN.md: no table under {label}')
    return rows[1:]   # drop the header row


def inline(md):
    """The little Markdown a table cell uses: `code`, **bold**, *italic*."""
    s = html.escape(md, quote=False)
    s = re.sub(r'`([^`]+)`', r'<code>\1</code>', s)
    s = re.sub(r'\*\*([^*]+)\*\*', r'<b>\1</b>', s)
    s = re.sub(r'(?<![\w*])\*([^*]+)\*(?![\w*])', r'<i>\1</i>', s)
    s = re.sub(r'§ ([A-Z][\w ]+)', r'§ \1', s)
    return s


TYPE_SCALE = md_table(r'^\*\*The scale\.\*\*', 'the type scale (Type, "The scale.")')
SIZE_TABLE = md_table(r'^### Sizes', 'the Sizes table (Shapes)')
DEVICES = {fam: md_table(rf'^### {re.escape(fam)}', f'the {fam} table (Devices)')
           for fam in ('Instruments (16)', 'Audio effects (25, plus Vocoder)', 'MIDI effects (6)', 'Racks (3)')}
ICON_STATES = md_table(r'^### Icon button', 'the icon button states')

# DESIGN.md writes every colour out; warn (never fail) where a value there no longer
# matches the theme files, so the guide is fixed in the same change as the token.
drift = []
order = [th['id'] for th in THEMES]
for line in GUIDE.splitlines():
    if not line.startswith('|'):
        continue
    cells = [c.strip() for c in line.strip().strip('|').split('|')]
    hexes = [c for c in cells if re.fullmatch(r'`#[0-9A-Fa-f]{6,8}`', c)]
    if len(hexes) != len(THEMES) or len(cells) < 2 + len(THEMES):
        continue
    named = re.search(r'`Brush\.(\w+)`', line) or next(
        (re.search(r'`(\w+)`', c) for c in cells if re.search(r'`\w+`', c) and not c.startswith('`#')), None)
    key = named.group(1) if named else None
    if not key:
        continue
    for tid, cell in zip(order, hexes):
        want = B[tid].get(key) or BY_ID[tid]['colors'].get(key)
        if want and want.upper() != cell.strip('`').upper():
            drift.append(f'{key} in {tid}: DESIGN.md says {cell.strip("`")}, the theme has {want}')
for d in drift:
    print(f'design-html.py: warning: {d}', file=sys.stderr)

# ---- Per-theme CSS variables ----------------------------------------------------------


def theme_vars(th):
    c = th['colors']
    br = B[th['id']]
    light = th['kind'] == 'light'
    out = [f'  {var(k)}: {css(v)};' for k, v in br.items()]
    out += [f'  --slot-{var(k)[2:]}: {css(c[k])};' for k in SLOTS]
    out += [f'  --shadow-{var(k)[2:]}: {css_shadow(v)};' for k, v in shadows(c, not light).items()]
    # Orange text on the soft tint: AccentBright in dark themes, AccentPale in light ones.
    out.append(f'  --acc-text: {css(c["AccentPale"] if light else c["AccentBright"])};')
    # The device depth on the page is the app's own Shadow.* resources.
    out += ['  --sh-groove: var(--shadow-groove);', '  --sh-section: var(--shadow-section);',
            '  --sh-rise: var(--shadow-lifted);', '  --sh-key: var(--shadow-lifted);',
            '  --sh-switch: var(--shadow-switch-inset);', '  --sh-card: var(--shadow-card);']
    for k in ('Accent', 'Mute', 'Solo', 'Record', 'Success'):
        out.append(f'  --ink-on-{k.lower()}: {hexs(ink_on(rgb(br[k])))};')
    for i in range(1, 10):
        out.append(f'  --ink-on-track{i}: {hexs(ink_on(rgb(br[f"Track{i}"])))};')
    out.append(f"  color-scheme: {th['kind']};")
    return '\n'.join(out)


VARIANTS = '\n'.join((':root' if vname(th) == 'dark' else f':root[data-variant="{vname(th)}"]')
                     + ' {\n' + theme_vars(th) + '\n}' for th in THEMES)
# The current theme's column in the colour tables is the one picked above.
CURRENT_COL = '\n'.join(
    (':root:not([data-variant])' if vname(th) == 'dark' else f':root[data-variant="{vname(th)}"]')
    + f' .v-{vname(th)}{{color:var(--text-primary);background:var(--well)}}' for th in THEMES)

# ---- Specimen builders ----------------------------------------------------------------

ICONS = {
    'play': '<path class="f" d="M8 5.5v13l10.5-6.5z"/>',
    'stop': '<rect class="f" x="6.5" y="6.5" width="11" height="11" rx="1.5"/>',
    'record': '<circle class="f" cx="12" cy="12" r="5.5"/>',
    'loop': '<path d="M4 12a5 5 0 0 1 5-5h9M15 4l3 3-3 3M20 12a5 5 0 0 1-5 5H6M9 20l-3-3 3-3"/>',
    'metronome': '<path d="M8.5 4h7l3 16h-13zM7.3 15h9.4M12 15l4.5-8.5"/>',
    'follow': '<path d="M7 7.5V20M4.5 4h5L7 7.5zM12 13.5h8M17 10.5l3 3-3 3"/>',
    'snap': '<path d="M6 4h4v8a2 2 0 0 0 4 0V4h4v8a6 6 0 0 1-12 0zM6 8h4M14 8h4"/>',
    'automation': '<path d="M3.5 17.5 8 9l5 5 7.5-8.5"/><circle cx="8" cy="9" r="1.7"/><circle cx="13" cy="14" r="1.7"/>',
    'arrangement': '<path d="M4 7h9M4 12h15M4 17h7"/>',
    'session': '<rect x="4" y="4" width="7" height="7" rx="1.5"/><rect x="13" y="4" width="7" height="7" rx="1.5"/><rect x="4" y="13" width="7" height="7" rx="1.5"/><rect x="13" y="13" width="7" height="7" rx="1.5"/>',
    'modular': '<circle cx="6" cy="7" r="2.5"/><circle cx="18" cy="7" r="2.5"/><circle cx="12" cy="17.5" r="2.5"/><path d="M8.5 7h7M7.3 9.2l3.4 6M16.7 9.2l-3.4 6"/>',
    'browser': '<rect x="4" y="5" width="16" height="14" rx="2"/><path d="M9.5 5v14"/>',
    'plus': '<path d="M12 5v14M5 12h14"/>',
    'search': '<circle cx="10.5" cy="10.5" r="6"/><path d="M15 15l5 5"/>',
    'filter': '<path d="M4 6h16M7 12h10M10 18h4"/>',
    'star': '<path d="M12 3.8l2.5 5.2 5.7.8-4.1 4 1 5.6L12 16.8l-5.1 2.6 1-5.6-4.1-4 5.7-.8z"/>',
    'star-filled': '<path class="f" d="M12 3.8l2.5 5.2 5.7.8-4.1 4 1 5.6L12 16.8l-5.1 2.6 1-5.6-4.1-4 5.7-.8z"/>',
    'folder': '<path d="M3.5 7.5a2 2 0 0 1 2-2h4l2 2h7a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z"/>',
    'close': '<path d="M6.5 6.5l11 11M17.5 6.5l-11 11"/>',
    'chevron': '<path d="M7.5 10l4.5 4.5 4.5-4.5"/>',
    'power': '<path d="M12 4v7M7.4 7.2a7 7 0 1 0 9.2 0"/>',
    'more': '<circle class="f" cx="6" cy="12" r="1.4"/><circle class="f" cx="12" cy="12" r="1.4"/><circle class="f" cx="18" cy="12" r="1.4"/>',
    'pop-out': '<path d="M14 4h6v6M20 4l-8 8M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4"/>',
    'midi': '<circle cx="12" cy="12" r="8"/><circle class="f" cx="8" cy="12" r="1"/><circle class="f" cx="16" cy="12" r="1"/><circle class="f" cx="9.2" cy="8.6" r="1"/><circle class="f" cx="14.8" cy="8.6" r="1"/><circle class="f" cx="12" cy="7.5" r="1"/>',
    'remote': '<rect x="7" y="3" width="10" height="18" rx="2.5"/><path d="M11 17.5h2"/>',
    'freeze': '<path d="M12 3.5v17M4.6 7.8l14.8 8.4M4.6 16.2l14.8-8.4M9.5 4.8 12 7l2.5-2.2M9.5 19.2 12 17l2.5 2.2"/>',
}
ICON_NAMES = {'play': 'Play', 'stop': 'Stop', 'record': 'Record', 'loop': 'Loop', 'metronome': 'Metronome',
              'follow': 'Follow playhead', 'snap': 'Snap', 'automation': 'Show automation',
              'arrangement': 'Arrangement', 'session': 'Session', 'modular': 'Modular', 'browser': 'Browser',
              'plus': 'Plus', 'search': 'Search', 'filter': 'Filter', 'star': 'Star', 'star-filled': 'Star filled',
              'folder': 'Folder', 'close': 'Close', 'chevron': 'Chevron', 'power': 'Power', 'more': 'More',
              'pop-out': 'Pop out', 'midi': 'MIDI', 'remote': 'Remote', 'freeze': 'Freeze'}


def icon(name, size=16):
    return (f'<svg class="ic" width="{size}" height="{size}" viewBox="0 0 24 24" aria-hidden="true">'
            f'{ICONS[name]}</svg>')


def ib(name, state='', title=None):
    t = title or ICON_NAMES.get(name, name)
    return f'<span class="ib {state}" title="{t}">{icon(name)}</span>'


def seg(items, on=0, dev=False, dis=()):
    cells = ''.join(f'<span class="{"on" if i == on else ""}{" dis" if i in dis else ""}">{t}</span>'
                    for i, t in enumerate(items))
    return f'<span class="seg{" dv" if dev else ""}">{cells}</span>'


def polar(c, r, a):
    """Point at angle a (degrees clockwise from 12 o'clock) on a circle of radius r."""
    t = math.radians(a)
    return c + r * math.sin(t), c - r * math.cos(t)


def arc(c, r, a0, a1):
    x0, y0 = polar(c, r, a0)
    x1, y1 = polar(c, r, a1)
    big = 1 if a1 - a0 > 180 else 0
    return f'M{x0:.2f} {y0:.2f}A{r:.2f} {r:.2f} 0 {big} 1 {x1:.2f} {y1:.2f}'


def knob(size=26, v=0.6, mode='dev', bip=False):
    """mode: dev (orange, centre cap), shell (flat: grey at rest), live (flat, being set), dim (ink 4)."""
    c = size / 2
    sw = 2.4
    r = c - sw / 2 - 0.3
    col = {'dev': 'var(--accent)', 'shell': 'var(--text-tertiary)', 'live': 'var(--accent)', 'dim': 'var(--text-disabled)'}[mode]
    parts = [f'<path d="{arc(c, r, -135, 135)}" style="stroke:var(--knob-track)"/>']
    a0, a1 = (0, (v - 0.5) * 270) if bip else (-135, -135 + 270 * v)
    if a1 < a0:
        a0, a1 = a1, a0
    if a1 - a0 > 0.5:
        parts.append(f'<path d="{arc(c, r, a0, a1)}" style="stroke:{col}"/>')
    ang = (v - 0.5) * 270
    if mode in ('dev', 'dim'):
        cr = size * 0.30
        parts.append(f'<circle cx="{c}" cy="{c}" r="{cr:.2f}" style="fill:var(--knob-cap);stroke:var(--seam);stroke-width:1"/>')
        x, y = polar(c, cr - 1.2, ang)
    else:
        x, y = polar(c, r - 3, ang)
    ptr = 'var(--text-disabled)' if mode == 'dim' else 'var(--text-primary)'
    parts.append(f'<line x1="{c}" y1="{c}" x2="{x:.2f}" y2="{y:.2f}" style="stroke:{ptr};stroke-width:1.5"/>')
    return (f'<svg class="kn" width="{size}" height="{size}" viewBox="0 0 {size} {size}" aria-hidden="true">'
            + ''.join(parts) + '</svg>')


def kcell(label, value, v, mode='dev', bip=False, mod=False):
    cls = ' mod' if mod else (' dim' if mode == 'dim' else '')
    return (f'<span class="kc{cls}">{knob(26, v, mode, bip)}<span class="kv">{value}</span>'
            f'<span class="kl">{label}</span></span>')


def kside(label, value, v):
    return (f'<span class="ks">{knob(22, v)}<span><span class="kl">{label}</span><br>'
            f'<span class="kv">{value}</span></span></span>')


def vbar(w, v, bip=False, drag=False, dim=False, h=16):
    x = v * 100
    if bip:
        lo, hi = sorted((50, x))
        fill = f'left:{lo:.1f}%;width:{hi - lo:.1f}%'
    else:
        fill = f'left:0;width:{x:.1f}%'
    cls = 'vb' + (' drag' if drag else '') + (' dim' if dim else '')
    return (f'<span class="{cls}" style="width:{w}px;height:{h}px"><b style="{fill}"></b>'
            f'<i style="left:{x:.1f}%"></i></span>')


def vrow(label, w, v, value, bip=False, drag=False, dim=False, mod=False):
    cls = ' dim' if dim else (' mod' if mod else '')
    return (f'<span class="vr{cls}"><span class="kl">{label}</span>{vbar(w, v, bip, drag, dim)}'
            f'<span class="kv">{value}</span></span>')


def uvbar(h, v, bip=False):
    """An upright value bar: a vertical fader inside a device."""
    y = (1 - v) * 100
    if bip:
        lo, hi = sorted((50, y))
        fill = f'top:{lo:.1f}%;height:{hi - lo:.1f}%'
    else:
        fill = f'top:{y:.1f}%;bottom:0'
    return (f'<span class="uvb" style="height:{h}px"><b style="{fill}"></b>'
            f'<i style="top:{y:.1f}%"></i></span>')


def slider(w, v, text, state=''):
    x = v * 100
    return (f'<span class="sl {state}" style="width:{w}px"><b style="width:{x:.1f}%"></b>'
            f'<i style="left:{x:.1f}%"></i><em>{text}</em></span>')


def switch(state=''):
    return f'<span class="sw {state}"></span>'


def meter(h, levels):
    return '<span class="mt">' + ''.join(
        f'<span class="mc" style="height:{h}px"><i style="clip-path:inset({100 - p:.0f}% 0 0 0)"></i></span>'
        for p in levels) + '</span>'


def wheel(h, pos, state=''):
    """The round pitch / mod wheel: pos 0 (bottom) to 1 (top)."""
    y = (1 - pos) * (h - 10) + 4
    return (f'<span class="wh {state}" style="height:{h}px"><span class="grip"><i></i><i></i><i></i></span>'
            f'<span class="bar" style="top:{y:.0f}px"></span></span>')


def tb(letter, state=''):
    return f'<span class="tb {state}">{letter}</span>'


def readout(value, unit=''):
    u = f'<span class="unit">{unit}</span>' if unit else ''
    return f'<span class="ro">{value}{u}</span>'


def graph(w, h, title, kind='eq', sel=1):
    """A graph window: SurfaceInset, rule frame, grid, a VizInk curve, corner labels."""
    g = []
    for i in range(1, 6):
        x = w * i / 6
        g.append(f'<line x1="{x:.1f}" y1="0" x2="{x:.1f}" y2="{h}"/>')
    for j in range(1, 4):
        y = h * j / 4
        g.append(f'<line x1="0" y1="{y:.1f}" x2="{w}" y2="{y:.1f}"/>')
    grid = '<g style="stroke:var(--viz-grid);stroke-width:1">' + ''.join(g) + '</g>'
    if kind == 'eq':
        pts, nodes = [], [(0.16, 0.62), (0.42, 0.34), (0.70, 0.56), (0.88, 0.46)]
        for i in range(0, 121):
            t = i / 120
            y = 0.5 - 0.16 * math.exp(-((t - 0.42) / 0.09) ** 2) + 0.07 * math.exp(-((t - 0.70) / 0.07) ** 2) \
                + 0.12 / (1 + math.exp((t - 0.13) * 40)) - 0.04 / (1 + math.exp(-(t - 0.88) * 30))
            pts.append(f'{t * w:.1f},{y * h:.1f}')
        curve = f'<polyline points="{" ".join(pts)}" style="fill:none;stroke:var(--viz-ink);stroke-width:1.8;stroke-linejoin:round"/>'
        ns = ''
        for i, (nx, ny) in enumerate(nodes):
            col = 'var(--accent)' if i == sel else 'var(--viz-ink)'
            ns += f'<circle cx="{nx * w:.1f}" cy="{ny * h:.1f}" r="3.5" style="fill:{col};stroke:var(--surface-inset);stroke-width:2"/>'
        labels = (f'<text x="5" y="{h - 5}" class="ax">20</text>'
                  f'<text x="{w - 5}" y="{h - 5}" class="ax" text-anchor="end">20k Hz</text>')
        body = curve + ns
    else:   # an impulse response: a real-looking decaying tail
        bars, n = [], int(w / 3)
        for i in range(n):
            t = i / n
            amp = math.exp(-t * 3.2) * (0.55 + 0.45 * abs(math.sin(i * 1.7) * math.cos(i * 0.37)))
            bh = amp * (h * 0.42)
            bars.append(f'<line x1="{i * 3 + 1.5:.1f}" y1="{h / 2 - bh:.1f}" x2="{i * 3 + 1.5:.1f}" y2="{h / 2 + bh:.1f}"/>')
        body = '<g style="stroke:var(--viz-ink);stroke-width:1">' + ''.join(bars) + '</g>'
        labels = (f'<text x="5" y="{h - 5}" class="ax">0</text>'
                  f'<text x="{w - 5}" y="{h - 5}" class="ax" text-anchor="end">2.4 s</text>')
    ttl = f'<text x="6" y="13" class="gt">{title}</text>'
    return (f'<svg class="gw" width="{w}" height="{h}" viewBox="0 0 {w} {h}" role="img" aria-label="{title}">'
            f'{grid}{body}{labels}{ttl}</svg>')


def dd(text, cls='', label=None, w=None):
    lab = f'<span class="lbl">{label}</span>' if label else ''
    st = f' style="min-width:{w}px"' if w else ''
    return f'{lab}<span class="dd {cls}"{st}>{text}</span>'


def preset(name):
    return (f'<span class="pre"><span class="pa">{icon("chevron", 12)}</span><span class="dd dv">{name}</span>'
            f'<span class="pa nx">{icon("chevron", 12)}</span></span>')


def card_header(name, tag, pre=None, power='acc', extra=''):
    p = preset(pre) if pre else ''
    return (f'<div class="chead"><span class="cname">{name}</span>{p}<span class="ctag">{tag}</span>'
            f'{extra}<span class="flex"></span>{switch(power)}</div>')


def section(title, body, w=None, grow=False, extra=''):
    st = []
    if w:
        st.append(f'width:{w}px')
    if grow:
        st.append('flex:1 1 0')
    s = f' style="{";".join(st)}"' if st else ''
    return f'<div class="dsec"{s}><div class="dst"><span>{title}</span>{extra}</div>{body}</div>'


def chamber_card(sel=False):
    s1 = section('Space', f'<div class="col g8">{seg(["Hall", "Room", "Plate", "Spring"], 0, dev=True, dis=(3,))}'
                 f'{graph(176, 112, "Impulse", "ir")}</div>', w=200)
    knobs = ''.join([kcell('Decay', '2.40 s', 0.62), kcell('Pre-delay', '18 ms', 0.22),
                     kcell('Size', '72 %', 0.72, mod=True), kcell('Damp', '4.2 k', 0.55),
                     kcell('Diffusion', '80 %', 0.80), kcell('Mod', '12 %', 0.12),
                     kcell('Low', '−2.0 dB', 0.42, bip=True), kcell('Shimmer', '0 %', 0.0, mode='dim')])
    s2 = section('Tail', f'<div class="kgrid">{knobs}</div>', grow=True,
                 extra=f'<span class="flex"></span><span class="tabs dv mini"><span class="on">Tail</span><span>Early</span><span>Mod</span></span>')
    rows = ''.join([vrow('Dry', 96, 0.78, '0.0 dB'), vrow('Wet', 96, 0.55, '−6.0 dB', drag=True),
                    vrow('Width', 96, 0.62, '+24 %', bip=True), vrow('Low cut', 96, 0.18, '120 Hz'),
                    vrow('Ducking', 96, 0.30, '30 %', dim=True)])
    sw_rows = (f'<span class="swr">{switch("acc")}<span class="kl">Freeze</span></span>'
               f'<span class="swr">{switch()}<span class="kl">Ducking</span></span>')
    s3 = section('Levels', f'<div class="col g6">{rows}<div class="row g12">{sw_rows}</div></div>', w=212)
    return (f'<div class="card{" sel" if sel else ""}" style="width:700px">'
            + card_header('Chamber', 'Convolution', 'Stone Vault')
            + f'<div class="cbody">{s1}{s2}{s3}</div></div>')


TRACK_ORDER = [1, 4, 5, 6, 7, 2, 9, 8, 3]   # ArrangementView.AutoTrackColorIndex, slots 1-based
TRACK_NAMES = {1: 'Green', 2: 'Teal', 3: 'Sky', 4: 'Blue', 5: 'Amber', 6: 'Violet', 7: 'Rose', 8: 'Olive', 9: 'Slate'}


def notes_svg(w, h, seed=1, n=None):
    n = n or max(3, w // 18)
    out = []
    for j in range(n):
        x = 6 + j * (w - 12) / n
        y = 3 + ((j * 5 + seed * 3) % 6) * (h - 8) / 6
        ln = (w - 12) / n * (0.6 + 0.3 * ((j + seed) % 3) / 2)
        out.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{ln:.1f}" height="3" rx="1"/>')
    return f'<svg width="{w}" height="{h}" style="fill:var(--c)">' + ''.join(out) + '</svg>'


def wave_svg(w, h, seed=1):
    out, mid = [], h / 2
    for i in range(0, int(w) - 4, 3):
        t = i / w
        a = (0.25 + 0.75 * abs(math.sin(i * 0.11 + seed) * math.sin(i * 0.031 + seed * 2))) * (0.4 + 0.6 * math.exp(-((t * 7 + seed) % 1.4)))
        bh = max(1, a * (h / 2 - 2))
        out.append(f'<line x1="{i + 2.5}" y1="{mid - bh:.1f}" x2="{i + 2.5}" y2="{mid + bh:.1f}"/>')
    return f'<svg width="{w}" height="{h}" style="stroke:var(--c);stroke-width:1">' + ''.join(out) + '</svg>'


def clip(slot, name, w, kind='midi', sel=False, h=50, seed=1):
    content = notes_svg(w, h - 22, seed) if kind == 'midi' else wave_svg(w, h - 22, seed)
    return (f'<div class="clip{" sel" if sel else ""}" style="--c:var(--track{slot});width:{w}px;height:{h}px">'
            f'<b>{name}</b>{content}</div>')


def track_header(slot, name, tag, vol, voltext, pan, pantext, states='', sel=False, levels=(62, 55)):
    m = tb('M', 'm' if 'm' in states else '')
    s = tb('S', 's' if 's' in states else '')
    a = tb('<i class="dot"></i>', 'ra' if 'r' in states else 'r')
    return (f'<div class="th{" sel" if sel else ""}" style="--c:var(--track{slot})"><span class="bar"></span>'
            f'<div class="r1"><span class="nm">{name}</span><span class="lbl">{tag}</span><span class="flex"></span>{m}{s}{a}</div>'
            f'<div class="r2">{slider(80, vol, voltext)}{knob(18, pan, "shell", bip=True)}<span class="val">{pantext}</span>'
            f'<span class="dd sm">All ins</span></div>'
            f'<div class="tmeter">{meter(40, levels)}</div></div>')


def lane(clips, sel=False):
    return f'<div class="lane{" sel" if sel else ""}">{clips}</div>'


def menu():
    def item(icn, label, value='', short='', cls='', caret=False, check=False):
        ic = icon('chevron', 14).replace('class="ic"', 'class="ic ck"') if check else (icon(icn, 14) if icn else '')
        if check:
            ic = '<svg class="ic ck" width="14" height="14" viewBox="0 0 24 24"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>'
        cr = '<svg class="ic car" width="12" height="12" viewBox="0 0 24 24"><path d="M9.5 6l6 6-6 6"/></svg>' if caret else ''
        return (f'<div class="mi {cls}"><span class="mic">{ic}</span><span class="mlab">{label}</span>'
                f'<span class="mval">{value}</span><span class="msc">{short}</span>{cr}</div>')
    return ('<div class="menu" style="width:248px">'
            '<div class="mhead"><i style="background:var(--track4)"></i><b>Keys</b><span class="lbl">MIDI · 3</span></div>'
            '<div class="mrow"><span class="lbl">Height</span>' + seg(['Compact', 'Standard', 'Large'], 1) + '</div>'
            '<div class="msep"></div>'
            + item('folder', 'Rename', short='⌘R') + item('', 'Record input', value='Ext', caret=True)
            + item('freeze', 'Live freeze', value=switch('on'), cls='hov')
            + item('', 'Show in Session', check=True)
            + '<div class="msep"></div>'
            + item('', 'Copy', short='⌘C') + item('', 'Duplicate', short='⌘D')
            + item('', 'Paste', short='⌘V', cls='dis')
            + '<div class="msep"></div>'
            + item('close', 'Delete', short='⌫', cls='danger') + '</div>')


def mx_node(name, cat, ports_l, ports_r, params=None, state='', small=None):
    """A Modular node: 214 wide, header 34 tinted by category, ports on the edges."""
    cc = {'inst': 'var(--n-inst)', 'fx': 'var(--n-fx)', 'out': 'var(--rule)'}[cat]
    rows = max(len(ports_l), len(ports_r))
    pr = ''
    for i in range(rows):
        lp = ports_l[i] if i < len(ports_l) else None
        rp = ports_r[i] if i < len(ports_r) else None
        def port(p, side):
            if not p:
                return '<span></span>'
            kind, lab = p
            return (f'<span class="pl">{lab}</span><span class="pt {kind} {side}" style="top:{11 + i * 22}px"></span>')
        pr += f'<div class="pr">{port(lp, "l")}{port(rp, "r")}</div>'
    act = (f'<span class="act">{icon("pop-out", 13)}{icon("close", 13)}'
           + (switch('acc') if cat != 'out' else '<i class="ondot" title="Always on"></i>') + '</span>')
    plist = ''
    if params:
        plist = '<div class="plist">' + ''.join(
            f'<div class="prm"><span>{n}</span>{vbar(96, v, h=12)}<em>{t}</em></div>' for n, v, t in params) + '</div>'
    sm = f'<small>{small}</small>' if small else ''
    return (f'<div class="node {state}" style="--cc:{cc}"><div class="nh"><b>{name}</b>{sm}{act}</div>'
            f'<div class="ports">{pr}</div>{plist}</div>')


# ---- Sections -------------------------------------------------------------------------


def colour_table(rows):
    head = ''.join(f'<th class="v-{vname(th)}">{th["name"]}</th>' for th in THEMES)
    body = []
    for key, use in rows:
        slot = key.startswith('slot:')
        k = key[5:] if slot else key
        vals = [(BY_ID[th['id']]['colors'][k] if slot else B[th['id']][k]) for th in THEMES]
        hexmap = html.escape(json.dumps({vname(th): v for th, v in zip(THEMES, vals)}))
        cssvar = f'--slot-{var(k)[2:]}' if slot else var(k)
        name = f'<code>{k}</code> <span class="lbl">slot only</span>' if slot else f'<code>Brush.{k}</code>'
        cells = ''.join(f'<td class="hex v-{vname(th)}">{v}</td>' for th, v in zip(THEMES, vals))
        body.append(f'<tr class="crow" data-hex="{hexmap}" title="Click to copy the hex for the theme shown">'
                    f'<td class="chip"><span><i style="background:var({cssvar})"></i></span></td>'
                    f'<td>{use}</td><td>{name}</td>{cells}</tr>')
    return (f'<div class="tw"><table class="t ct"><thead><tr><th></th><th>Use</th><th>Brush</th>{head}</tr></thead>'
            f'<tbody>{"".join(body)}</tbody></table></div>')


GROUPS = [
    ('Backgrounds', [('Seam', 'Line between panels'), ('BgApp', 'Behind the arrangement; window, title bar, detail-panel body'),
                     ('slot:SurfaceDeep', 'Toolbar, left rail, pinned Return / Master rows'),
                     ('Panel', 'Browser, track headers, panel toolbars, mixer strips'),
                     ('SurfaceCard', 'Cards in the shell; a bypassed Modular node’s header'),
                     ('SurfaceHover', 'A raised control inside a device; tooltip'),
                     ('SurfaceInset', 'Behind graphs'), ('LaneB', 'Track lane behind clips'),
                     ('BgSunken', 'Sunken: a shell slider’s body, a wheel, the knob cap edge in dark themes')]),
    ('Wells, grooves and raised parts', [('Well', 'Well behind a button, field or menu at rest'),
                                         ('WellTrack', 'Switch and slider track'), ('KnobTrack', 'Knob track'),
                                         ('Groove', 'Groove inside a device'), ('DeviceSection', 'A pressed-in section inside a device'),
                                         ('DeviceSectionEdge', 'That section’s inner edge'),
                                         ('SurfaceRaised', 'Device card body and Modular node'), ('DeviceHeader', 'Device card header'),
                                         ('Bevel', 'Light top edge on raised parts'), ('KnobCap', 'Knob centre cap')]),
    ('Lines', [('Rule', 'Inside a panel: separators, tab row, menu rules, graph frame'), ('GridBeat', 'Beat grid, zero axis'),
               ('GridBar', 'Bar lines'), ('VizGrid', 'Grid inside a graph'), ('BorderBrass', 'An orange stroke where a fill is too heavy')]),
    ('Ink', [('TextPrimary', '<b>Ink 1</b>: names, values'), ('TextSecondary', '<b>Ink 2</b>: secondary labels, icons at rest'),
             ('TextTertiary', '<b>Ink 3</b>: labels, units, tags, placeholders'),
             ('TextDisabled', '<b>Ink 4</b>: axes, column heads, hints, anything unavailable'),
             ('TextHeading', 'Modal and section titles (rare)'), ('TextStrong', 'Glyphs on transport keys'),
             ('TextMuted', 'Section labels, counts, badges in the shell'), ('TextAxis', 'Tick labels inside graphs'),
             ('TextFaint', 'Older faint ink; new code uses ink 4'), ('VizInk', 'A curve in a graph; a value at rest outside devices'),
             ('TextOnAccent', 'Text on solid orange')]),
    ('Orange', [('Accent', 'Solid: playing, playhead, loop, power, device values'), ('AccentSoft', 'Soft tint behind something picked'),
                ('AccentBright', 'Orange text or line on the ground; on the soft tint in dark themes'),
                ('slot:AccentPale', 'Orange text on the soft tint in light themes'), ('AccentDeep', 'Pressed orange')]),
    ('Track states and meters', [('Record', 'Record and arm'), ('RecordInk', 'The disc on an engaged record key'), ('Mute', 'Mute'),
                                 ('Solo', 'Solo'), ('MeterSafe', 'Meter, safe (to 70 %)'), ('MeterHot', 'Meter, hot (70–88 %)'),
                                 ('MeterOver', 'Meter, over (above 88 %)'), ('TrackHeaderSelected', 'Selected track header'),
                                 ('LaneSelected', 'Selected track lane'), ('Danger', 'Overload, clipping, error text'),
                                 ('Success', 'Positive status; Play in Session'), ('SuccessInk', 'The glyph on a green key'),
                                 ('Warning', 'Warning text')]),
    ('Track colours', [(f'Track{i}', TRACK_NAMES[i]) for i in range(1, 10)]
     + [('ReturnA', 'Return A'), ('ReturnB', 'Return B'), ('Master', 'Master: a grey, because it is the sum')]),
    ('Inside graphs', [('VizInk', 'The graph’s own curve'), ('ChromaTeal', 'Second source, where shade cannot carry it'),
                       ('ChromaRose', 'Third source'), ('ChromaSteel', 'Fourth source')]),
]


def readability():
    """The readability bar of DESIGN.md, computed from the theme files."""
    grounds = ('BgApp', 'Panel', 'SurfaceRaised', 'SurfaceHover', 'BgSunken')
    checks = [
        ('T3.1', 'Ink 1, ink 2 and muted on the five grounds', 4.5,
         lambda c: min(contrast(rgb(c[i]), rgb(c[g])) for i in ('TextPrimary', 'TextSecondary', 'TextMuted') for g in grounds)),
        ('T3.2', 'Ink 3 on the five grounds', 3.0, lambda c: min(contrast(rgb(c['TextTertiary']), rgb(c[g])) for g in grounds)),
        ('T3.3', 'Faint, ink 4 and axis on the five grounds', 2.5,
         lambda c: min(contrast(rgb(c[i]), rgb(c[g])) for i in ('TextFaint', 'TextDisabled', 'TextAxis') for g in grounds)),
        ('T3.4', 'Orange line on BgApp, Panel, BgSunken', 2.3,
         lambda c: min(contrast(rgb(c['AccentBright']), rgb(c[g])) for g in ('BgApp', 'Panel', 'BgSunken'))),
        ('T3.5', 'Derived ink on orange, mute, solo, record', 4.5,
         lambda c: min(contrast(ink_on(rgb(c[k])), rgb(c[k])) for k in ('Accent', 'Mute', 'Solo', 'Record'))),
        ('T3.6', 'Derived ink on track and return colours', 3.0,
         lambda c: min(contrast(ink_on(rgb(c[k])), rgb(c[k])) for k in [f'Track{i}' for i in range(1, 10)] + ['ReturnA', 'ReturnB'])),
        ('T3.7', 'Track colours pairwise (ΔE)', 12,
         lambda c: min(delta_e(rgb(c[f'Track{a}']), rgb(c[f'Track{b}'])) for a in range(1, 10) for b in range(a + 1, 10))),
        ('T3.8', 'Orange, mute, solo, record pairwise (ΔE)', 15,
         lambda c: min(delta_e(rgb(c[a]), rgb(c[b])) for i, a in enumerate(('Accent', 'Mute', 'Solo', 'Record'))
                       for b in ('Accent', 'Mute', 'Solo', 'Record')[i + 1:])),
        ('T3.9', 'Record ink on record, success ink on success', 4.5,
         lambda c: min(contrast(rgb(c['RecordInk']), rgb(c['Record'])), contrast(rgb(c['SuccessInk']), rgb(c['Success'])))),
    ]
    head = ''.join(f'<th class="v-{vname(th)}">{th["name"]}</th>' for th in THEMES)
    rows = []
    for code, what, minimum, fn in checks:
        cells = ''
        for th in THEMES:
            v = fn(th['colors'])
            ok = v + 1e-9 >= minimum
            cells += f'<td class="hex v-{vname(th)}{"" if ok else " bad"}">{v:.2f}</td>'
        rows.append(f'<tr><td>{code}</td><td>{what}</td><td class="hex">{minimum:g}</td>{cells}</tr>')
    extra = []
    for th in THEMES:
        c = th['colors']
        ref = c['AccentPale'] if th['kind'] == 'light' else c['AccentBright']
        extra.append(f'<td class="hex v-{vname(th)}">{contrast(rgb(ref), rgb(c["AccentSoft"])):.2f}</td>')
    rows.append('<tr><td></td><td>Orange text on the soft tint (not in the bar)</td><td class="hex">–</td>' + ''.join(extra) + '</tr>')
    return (f'<div class="tw"><table class="t"><thead><tr><th></th><th>Pairing</th><th>Minimum</th>{head}</tr></thead>'
            f'<tbody>{"".join(rows)}</tbody></table></div>')


def shadow_table():
    head = ''.join(f'<th class="v-{vname(th)}">{th["name"]}</th>' for th in THEMES)
    use = {'Raised': 'The top bevel on a raised part; the dialog header',
           'Sunken': 'A rule edge around a recess, where a groove is not used',
           'Popover': 'A popover drawn inside the main window', 'Modal': 'The command palette',
           'SwitchInset': 'The slight inner shadow of a switch track',
           'Card': 'A device card or Modular node: seam outline and light top edge',
           'Section': 'A pressed-in section inside a device',
           'Groove': 'A groove: segment groups, value bars and fields inside a device',
           'Lifted': 'The picked option raised out of its groove; raised keys in a device'}
    rows = []
    for k, _ in SHADOW_SRC:
        cells = ''.join(f'<td class="hex v-{vname(th)}">{html.escape(shadows(th["colors"], th["kind"] == "dark")[k])}</td>' for th in THEMES)
        rows.append(f'<tr><td><span class="shs" style="box-shadow:var(--shadow-{var(k)[2:]})"></span></td>'
                    f'<td><code>Shadow.{k}</code></td><td style="min-width:190px">{use.get(k, "")}</td>{cells}</tr>')
    return (f'<div class="tw"><table class="t"><thead><tr><th></th><th>Key</th><th>Use</th>{head}</tr></thead>'
            f'<tbody>{"".join(rows)}</tbody></table></div>')


def sec(id_, title, lead, body):
    return f'<section class="doc" id="{id_}"><h2>{title}</h2><p class="lead">{lead}</p>{body}</section>'


def stage(body, cls='', cap=None):
    c = f'<p class="cap">{cap}</p>' if cap else ''
    return f'<div class="stage {cls}">{body}</div>{c}'


def fig(body, cap):
    return f'<figure class="sp">{body}<figcaption>{cap}</figcaption></figure>'


def dodont(do, dont):
    return ('<div class="dd2"><div class="do"><b>Do</b><ul>' + ''.join(f'<li>{x}</li>' for x in do)
            + '</ul></div><div class="dont"><b>Don’t</b><ul>' + ''.join(f'<li>{x}</li>' for x in dont) + '</ul></div></div>')


def toolbar(compact=False):
    views = f'<span class="grp">{ib("arrangement", "on")}{ib("session")}{ib("modular")}</span>'
    transport = f'{ib("play", "live", "Play")}{ib("stop")}{ib("record")}{ib("loop", "on")}'
    timing = readout('12.3.02', 'bars') + readout('124.00', 'BPM') + readout('4/4') + readout('C', 'minor')
    grid = dd('1/16', w=60)
    tools = f'{ib("metronome", "on")}{ib("follow")}{ib("snap", "on")}{ib("automation")}'
    pills = '<span class="pill">Overdub</span><span class="pill on">Tap<i class="beats"><i class="b on"></i><i class="b"></i><i class="b"></i><i class="b"></i></i></span>'
    right = (f'<span class="stat"><i class="sdot on"></i>MIDI</span><span class="stat"><i class="sdot"></i>Remote</span>'
             f'{slider(110, 0.74, "Master · 0.0 dB")}')
    sep = '<span class="vsep"></span>'
    return (f'<div class="toolbar">{views}{sep}{transport}{timing}{pills}{sep}{grid}{sep}{tools}'
            f'<span class="flex"></span>{right}</div>')


def window_mock():
    title = ('<div class="wtitle"><span class="lights"><i></i><i></i><i></i></span>'
             '<span class="wname">Nota</span></div>')
    rail = ('<div class="rail">' + f'<span class="rsel">{ib("browser", "on")}</span>' + ib('star') + ib('folder')
            + ib('search') + '</div>')
    rows = [('Aurora', 'Analog poly', 'var(--track4)', False), ('Chamber', 'Reverb', 'var(--track1)', True),
            ('Grain', 'Granular', 'var(--track6)', False), ('Keys', 'Electric piano', 'var(--track5)', False),
            ('Pentad', 'Five-voice', 'var(--track7)', False), ('Rhythm', 'Drum machine', 'var(--track2)', False)]
    brows = ''.join(f'<div class="brow{" sel" if s else ""}"><i style="background:{c}"></i><span>{n}</span>'
                    f'<span class="lbl">{t}</span></div>' for n, t, c, s in rows)
    browser = (f'<div class="browser"><div class="bsearch"><span class="search sm">{icon("search")}Search'
               f'<b>124</b></span></div><div class="bchips"><span class="chip on">All</span><span class="chip">Synths</span>'
               f'<span class="chip">Keys</span><span class="chip">Drums</span></div>{brows}</div>')
    ruler = '<div class="ruler">' + ''.join(f'<span style="left:{8 + i * 112}px">{i * 4 + 1}</span>' for i in range(6)) + '</div>'
    tracks = [
        (1, 'Drums', 'MIDI', 0.68, '−2.0 dB', 0.5, 'C', '', [(1, 'Beat A', 214, 'midi', 0), (1, 'Beat B', 214, 'midi', 228)]),
        (4, 'Bass', 'MIDI', 0.6, '−4.5 dB', 0.42, '12L', 's', [(4, 'Root line', 320, 'midi', 60)]),
        (5, 'Keys', 'Audio', 0.72, '0.0 dB', 0.6, '20R', 'r', [(5, 'Rhodes take 3', 300, 'audio', 0)]),
        (6, 'Pad', 'MIDI', 0.55, '−8.0 dB', 0.5, 'C', 'm', [(6, 'Swell', 400, 'midi', 120)]),
    ]
    th_html, lanes = '', ''
    for i, (slot, name, tag, vol, vt, pan, pt, st, clips) in enumerate(tracks):
        th_html += track_header(slot, name, tag, vol, vt, pan, pt, st, sel=(i == 1), levels=(72 - i * 9, 64 - i * 7))
        cl = ''.join(f'<span class="cpos" style="left:{x}px">{clip(s, n, w, k, sel=(i == 2), seed=i + 1)}</span>' for s, n, w, k, x in clips)
        lanes += lane(cl, sel=(i == 1))
    canvas = (f'<div class="canvas"><div class="cvrow"><div class="thcol"><div class="ruler-pad"></div>{th_html}</div>'
              f'<div class="lanes">{ruler}<span class="playhead" style="left:306px"></span>{lanes}</div></div></div>')
    ptool = ('<div class="ptool"><span class="tabs out"><span class="on">Devices</span><span>Pattern</span><span>Clip</span></span>'
             '<span class="vsep"></span><i class="sq" style="background:var(--track4)"></i><span class="tn">Bass</span>'
             '<span class="lbl">MIDI · All ins → Master</span><span class="flex"></span>'
             '<span class="pill">Freeze</span><span class="pill">Live freeze</span><span class="vsep"></span>'
             f'{ib("pop-out")}{ib("close")}</div>')
    detail = f'<div class="detail">{ptool}<div class="dbody">{chamber_card(sel=True)}<span class="adddev">{icon("plus")}</span></div></div>'
    status = '<div class="status">Saved · 48 kHz · 128 samples · CPU 12 %</div>'
    return (f'<div class="win">{title}{toolbar()}<div class="wbody">{rail}{browser}'
            f'<div class="wmain">{canvas}{detail}</div></div>{status}</div>')


def build_sections():
    S = []
    principles = [
        ('Keep every control', 'The layout and every control stay where they are; only how they are drawn changes.'),
        ('A flat, grey shell', 'Toolbar, browser, track headers, mixer and editors are flat and grey, so the track colours carry the content.'),
        ('Depth only in devices', 'The device card is raised, its sections are pressed in, its controls sit in grooves.'),
        ('Orange has three jobs', 'Solid for what is live, a soft tint for what you picked, orange lines for values in a device.'),
        ('One typeface', 'Geist for every word and number, with tabular figures. No monospace face, no capital-letter labels.'),
        ('Never see-through', 'An unavailable option is drawn in ink 4 with nothing picked, never faded.'),
    ]
    pr = '<div class="princ">' + ''.join(f'<div><span class="pn">{i + 1}</span><b>{t}</b><span>{d}</span></div>'
                                         for i, (t, d) in enumerate(principles)) + '</div>'
    S.append(sec('start', 'Overview',
                 'How every part of Nota is drawn. This page draws the rules of <a href="DESIGN.md">DESIGN.md</a> live from the '
                 'theme files: pick a theme at the top and everything follows. Sizes are pixels at 100 % interface scale.',
                 pr + '<h3>The window</h3>' + stage(window_mock(), 'flush scroll',
                 'Title bar 32, toolbar 44, the left rail and browser, the arrangement with its track headers, the detail panel '
                 'with a device card, and the status line. Every region is docked: corner 0, separated by 1 px seams.')))

    # Colour
    body = ('<p>Click a row to copy its hex for the theme shown. See-through values are <code>#AARRGGBB</code>; their chip '
            'is drawn on <code>Panel</code>.</p>')
    for title, rows in GROUPS:
        body += f'<h3>{title}</h3>' + colour_table(rows)
        if title == 'Track colours':
            strip = ''.join(f'<span class="tord" style="background:var(--track{s});color:var(--ink-on-track{s})">{n + 1}<small>{TRACK_NAMES[s]}</small></span>'
                            for n, s in enumerate(TRACK_ORDER))
            body += ('<p>New tracks take the colours in this order, so each contrasts with the one before. The slot table '
                     'does not change: saved projects keep their colours.</p>' + stage(f'<div class="row g4">{strip}</div>'))
    body += ('<h3>Readability bar</h3><p>Every theme meets these minimums (WCAG contrast ratio; ΔE is CIE76), computed '
             'here from the theme files. A failing value is shown in the error colour.</p>' + readability())
    S.append(sec('colour', 'Colour', 'Every colour has one job. Four themes are built in; a theme changes values only, never '
                 'shapes, type, layout or where orange goes.', body))

    # Orange
    live = stage(
        fig(ib('play', 'live', 'Play'), 'Play while running')
        + fig(f'<span class="tline"><span class="loop"></span><span class="ph" style="left:58px"></span></span>', 'Playhead and loop range')
        + fig(f'<span class="cardlet">{switch("acc")}<span class="kl">Power</span></span>', 'A switch that is on, in a device')
        + fig('<span class="tabs dv"><span class="on">Oscillators</span><span>Filter</span><span>Mod</span></span>', 'Current device tab'))
    picked = stage(
        fig(seg(['Notes', 'Envelopes']), 'Picked segment')
        + fig(f'<span class="grp">{ib("arrangement", "on")}{ib("session")}{ib("modular")}</span>', 'Current view')
        + fig(f'<span class="row g4">{ib("metronome", "on")}{ib("snap", "on")}{ib("follow")}</span>', 'Tools that are on')
        + fig('<span class="row g4"><span class="chip on">Synths</span><span class="chip">Keys</span></span>', 'Filter chip on')
        + fig('<span class="pill on">Overdub</span>', 'Overdub on'))
    values = stage(
        fig(kcell('Cutoff', '1.2 k', 0.64), 'Knob arc')
        + fig(vrow('Mix', 120, 0.62, '62 %'), 'Value bar: soft fill, orange line')
        + fig(graph(220, 90, 'Band 2', 'eq', 1), 'The selected EQ point')
        + fig(f'<span class="row g12">{uvbar(90, 0.7)}{uvbar(90, 0.4, bip=True)}</span>', 'Upright value bars'), 'device')
    body = ('<div class="three"><div><h3>Live: solid</h3><p>Playing, recording, the playhead and loop, a switch that is on '
            'inside a device, power, the current device tab.</p>' + live + '</div>'
            '<div><h3>Picked: soft tint</h3><p>The option you chose. A pale fill with orange text or icon. Never solid.</p>' + picked + '</div></div>'
            '<h3>Values in devices: orange lines</h3><p>Only inside a device card: knob arcs, value bars, the selected EQ '
            'point. Everywhere else values stay grey.</p>' + values
            + '<h3>Outside devices, values stay grey</h3>'
            + stage(fig(f'<span class="row g8">{knob(18, 0.4, "shell", bip=True)}<span class="val">12L</span></span>', 'Pan on a track header')
                    + fig(slider(140, 0.66, '−3.3 dB'), 'Track volume at rest')
                    + fig(slider(140, 0.66, '−3.3 dB', 'live'), 'Under the pointer: the value bar’s colours')
                    + fig(meter(60, (66, 58)), 'Meters'))
            + '<h3>Menus and focus</h3>' + stage(fig('<span class="fld foc">Bass line</span>', 'Focus: a 1 px orange outline')
                                                  + fig('<div class="menu" style="width:200px"><div class="mi"><span class="mic"></span><span class="mlab">Rename</span></div>'
                                                        '<div class="mi hov"><span class="mic"></span><span class="mlab">Duplicate</span><span class="mval"></span><span class="msc">⌘D</span></div></div>',
                                                        'A hovered menu item keeps its full orange fill (accepted exception)'))
            + dodont(['Use solid orange only for something live.', 'Mark a picked option with the soft tint: a pale fill and orange text.',
                      'Keep orange values inside device cards.'],
                     ['Fill a picked option with solid orange.', 'Turn a selected clip or Session slot orange: it gets an outline in ink 1.',
                      'Colour knobs, faders or meters orange outside a device.']))
    S.append(sec('accent', 'Orange', 'Orange comes in three strengths, so you can tell at a glance what is playing, what you '
                 'picked and what a device is set to.', body))

    # Type
    samples = {'Readout': readout('124.00', 'BPM'), 'Device name': 'Chamber', 'Track name': 'Bass line',
               'Body': 'Export audio…', 'Value': '−3.3 dB', 'Label': 'Analog poly · 0/5',
               'Device label': 'Pre-delay', 'Column head': '20 Hz', 'Key letter': 'M S'}
    ink_var = {'1': 'var(--text-primary)', '2': 'var(--text-secondary)', '3': 'var(--text-tertiary)',
               '4': 'var(--text-disabled)', '1–2': 'var(--text-primary)'}
    rows = ''
    for role, sw_, ink, where in TYPE_SCALE:
        mm = re.match(r'([\d.]+)\s*/\s*(\d+)', sw_)
        if not mm:
            fail(f'DESIGN.md type scale: cannot read "{sw_}" for {role}')
        size, weight = mm.groups()
        smp = samples.get(role, 'Aa')
        style = f'font-size:{size}px;font-weight:{weight};color:{ink_var.get(ink, "var(--text-primary)")}'
        if role == 'Readout':
            smp = f'<span style="font-size:{size}px;font-weight:{weight}">124.00</span><span class="unit">BPM</span>'
            style = ''
        rows += (f'<tr><td><span style="{style}">{smp}</span></td><td>{role}</td><td class="hex">{sw_}</td>'
                 f'<td>Ink {ink}</td><td>{inline(where)}</td></tr>')
    scale = ('<div class="tw"><table class="t"><thead><tr><th>Sample</th><th>Role</th><th>Size / weight</th><th>Ink</th>'
             f'<th>Where</th></tr></thead><tbody>{rows}</tbody></table></div>')
    nums = stage(
        fig('<span class="tnum"><span>−3.3 dB</span><span>−12.0 dB</span><span>+0.8 dB</span><span>120.00</span><span>72 %</span><span>3.2 k</span></span>',
            'Tabular figures: the column does not jump')
        + fig('<span class="tnum"><span>1–127</span><span>C1–C3</span><span>0.20 – 1.50 s</span><span>20 Hz – 20 kHz</span></span>',
              'Ranges: an en dash, spaced when an end has a space')
        + fig('<span class="tnum"><span>+6 · 0 · −12 · −48</span><span>4.6 s</span><span>Stone Vault</span></span>',
              'Minus is U+2212; a thin space before the unit'))
    body = (f'<p>Geist for every word and number (400, 500, 600, 700, bundled from <code>assets/fonts</code>), with the '
            f'font’s own tabular figures (<code>tnum</code>) turned on everywhere. No monospace face, no letter spacing, '
            f'no capital-letter labels.</p><h3>The scale</h3>{scale}<h3>Numbers</h3>{nums}'
            + dodont(['Set every word and number in Geist.', 'Write tags in sentence case.', 'Put a unit in ink 3 beside its value.'],
                     ['Use a monospace face, even for numbers.', 'Fade text with opacity: use ink 4.', 'Set letter spacing.']))
    S.append(sec('type', 'Text', 'One typeface with fixed-width digits, so a value can change without the text around it jumping.', body))

    # Shapes
    use = {'Docked': 'A docked panel', 'Card': 'Device card, view group, modal', 'Group': 'Icon button, chip, segment group, section, menu',
           'Field': 'Field, dropdown, clip, pill', 'Cell': 'Segment cell, track button, tooltip, slot',
           'ValueBar': 'Value bar, shell slider', 'Switch': 'Switch track', 'Fine': 'Switch knob, notes, meter channel'}
    rad = '<div class="rad">' + ''.join(
        f'<figure><i style="border-radius:{R[k]}"></i><b>{k} · {RADIUS[k]:g}</b><span>{use[k]}</span></figure>'
        for k in RADIUS_ORDER) + '</div>'
    stable = ''.join(f'<tr><td>{inline(a)}</td><td>{inline(b)}</td></tr>' for a, b in SIZE_TABLE)
    tok = ''.join(f'<tr><td><code>{k}</code></td><td class="hex">{v:g}</td></tr>' for k, v in sorted(SIZES.items()))
    depth = stage(
        f'<div class="card" style="width:430px">{card_header("Card", "raised: outline and top edge", power="acc")}'
        f'<div class="cbody"><div class="dsec" style="flex:1"><div class="dst"><span>Section: pressed in</span></div>'
        f'<div class="col g8">{seg(["Groove", "Picked rises", "Off"], 1, dev=True, dis=(2,))}'
        f'{vrow("Groove", 150, 0.5, "50 %")}<span class="row g8"><span class="tb dv">1</span><span class="tb dv">2</span>'
        f'<span class="tb dv">3</span><span class="kl">Raised number buttons</span></span></div></div></div></div>', 'ground',
        'The card is raised, each section is pressed in, controls sit in grooves, and the picked option rises out of its groove. '
        'Only slightly different greys, thin lines and 1–2 px shadows: no gradients, no blur, no glow.')
    body = (f'<h3>Corner scale</h3><p>From <code>Radius.*</code> in <code>NotaTheme.axaml</code>.</p>{stage(rad)}'
            f'<h3>Sizes</h3><div class="two"><div class="tw"><table class="t"><thead><tr><th>Part</th><th>Size (the guide)</th></tr></thead>'
            f'<tbody>{stable}</tbody></table></div><div class="tw"><table class="t"><thead><tr><th>Token in NotaTheme.axaml today</th>'
            f'<th>px</th></tr></thead><tbody>{tok}</tbody></table></div></div>'
            f'<h3>Depth</h3>{depth}<h3>Elevation tokens</h3><p>Built by <code>NotaThemeService</code> from the theme’s colours. '
            f'Only <code>Popover</code> and <code>Modal</code> blur more than 2\u2009px: they float. The rest are edges, bevels and 1\u20132\u2009px depth inside devices.</p>{shadow_table()}'
            + dodont(['Keep depth inside device cards.', 'Build depth from greys, thin lines and 1–2 px shadows.',
                      'Keep every device card 260 high and let the width fit.'],
                     ['Raise buttons or panels in the shell.', 'Use gradients or blur.', 'Put the pitch and mod wheels in a groove.']))
    S.append(sec('geometry', 'Shapes', 'Corners run from 2 on small parts up to 9 on device cards; docked panels have none.', body))

    # Buttons
    states = ''.join(f'<tr><td>{inline(a)}</td><td>{inline(b)}</td></tr>' for a, b in ICON_STATES)
    body = (
        '<h3>Toolbar</h3>' + stage(toolbar(), 'flush deep',
                                   'Values are plain numbers at 17 / 500 with the unit in ink 3, no box. Groups are split by short thin lines.')
        + '<h3>Icon button</h3>'
        + stage(fig(ib('metronome'), 'Rest') + fig(ib('metronome', 'hov'), 'Hover: the well')
                + fig(ib('metronome', 'on'), 'On: soft tint') + fig(ib('play', 'live'), 'Live: solid')
                + fig(f'<span class="grp">{ib("arrangement", "on")}{ib("session")}{ib("modular")}</span>', 'In a group: a 2 px well, corner 9'), 'deep')
        + f'<div class="tw"><table class="t"><thead><tr><th>State</th><th>Drawn</th></tr></thead><tbody>{states}</tbody></table></div>'
        + '<h3>Segment buttons</h3>'
        + stage(fig(seg(['Notes', 'Envelopes']), 'Shell: on the well') + fig(seg(['Pre', 'Post', 'Off'], 1, dis=(2,)), 'A disabled cell in ink 4'))
        + stage(fig(seg(['Sine', 'Tri', 'Saw', 'Square'], 2, dev=True), 'Device: in a groove, the picked cell rises')
                + fig(seg(['12', '24', '36'], 0, dev=True, dis=(2,)), 'Device, one cell unavailable'), 'device')
        + '<h3>Chips and pills</h3>'
        + stage(fig('<span class="row g4"><span class="chip on">All</span><span class="chip"><i class="cdot" style="background:var(--track4)"></i>Synths</span>'
                    '<span class="chip">Keys</span><span class="chip">Drums</span><span class="chip">+3</span></span>', 'Chips: 24 high, corner 7')
                + fig('<span class="row g4"><span class="pill">Overdub</span><span class="pill on">Overdub</span></span>', 'Pills: off and on'))
        + '<h3>Track buttons</h3>'
        + stage(fig(f'<span class="row g4">{tb("M")}{tb("S")}{tb("<i class=dot></i>", "r")}</span>', 'Rest: the well, ink 3; arm shows a red dot')
                + fig(f'<span class="row g4">{tb("M", "m")}{tb("S", "s")}{tb("<i class=dot></i>", "ra")}</span>', 'On: mute, solo, arm with derived ink'))
        + stage(fig('<span class="row g4">' + ''.join(f'<span class="tb dv">{i}</span>' for i in range(1, 6)) + '</span>',
                    'Device number buttons: 14 × 16, raised'), 'device')
        + '<h3>Switch</h3>'
        + stage(fig(f'<span class="swr">{switch()}<span class="l2">Live freeze</span></span>', 'Off')
                + fig(f'<span class="swr">{switch("on")}<span class="l2">Live freeze</span></span>', 'On, outside devices: ink 2'))
        + stage(fig(f'<span class="swr">{switch()}<span class="kl">Sync</span></span>', 'Off, in a device')
                + fig(f'<span class="swr">{switch("acc")}<span class="kl">Sync</span></span>', 'On, in a device and for power: orange'), 'device')
        + '<h3>Tabs</h3>'
        + stage(fig('<span class="tabs out" style="width:300px"><span class="on">Devices</span><span>Pattern</span><span>Clip</span></span>',
                    'Shell: 10 padding, the underline in ink 1'))
        + stage(fig('<span class="tabs dv" style="width:300px"><span class="on">Oscillators</span><span>Filter</span><span>Mod</span></span>',
                    'Device: 16 apart, the underline orange'), 'device')
        + '<h3>Primary and destructive actions</h3>'
        + stage(fig('<span class="row g8"><span class="btn">Cancel</span><span class="btn primary">Export</span></span>',
                    'Primary by weight and one tone step, not colour')
                + fig('<span class="btn">Delete track</span>', 'Destructive: an ordinary button that says what it does'))
        + dodont(['Show the well at rest, or on hover for icon buttons.', 'Draw a disabled cell in ink 4.'],
                 ['Draw a border around a button.', 'Box toolbar values: they are plain text with a quieter unit.']))
    S.append(sec('keys', 'Buttons', 'Buttons have no border: they sit on the faint well. What you picked gets the soft tint; '
                 'what is live gets solid orange. Inside a device, controls sit in grooves and the picked option is raised.', body))

    # Knobs, sliders, meters, wheels
    wheels = (f'<div class="wrail"><span class="kl">Wheels</span><div class="row g10" style="align-items:stretch">'
              f'<span class="col g4 c">{wheel(120, 0.5)}<span class="kl">Pitch</span></span>'
              f'<span class="col g4 c">{wheel(120, 0.25, "moved")}<span class="kl">Mod</span></span></div>'
              f'<span class="kv">±2 st</span><span class="kv">25 %</span></div>')
    body = (
        '<h3>Knob</h3>'
        + stage(fig(kcell('Cutoff', '1.2 k', 0.64), 'Device knob 26, cap, orange arc')
                + fig(kcell('Pan', 'C', 0.5, bip=True), 'Bipolar: fills from 12 o’clock')
                + fig(kcell('Detune', '−18 ct', 0.36, bip=True), 'Bipolar, left')
                + fig(kcell('Size', '72 %', 0.72, mod=True), 'Modified: the name lifts to ink 1')
                + fig(kcell('Shimmer', '0 %', 0.3, 'dim'), 'Inactive: ink 4')
                + fig(kside('Glide', '40 ms', 0.3), 'Knob 22 with its label beside it'), 'device')
        + stage(fig(f'<span class="row g8">{knob(28, 0.7, "shell")}<span class="val">−6.0 dB</span></span>', 'Flat knob outside devices: 28, no cap, grey at rest')
                + fig(f'<span class="row g8">{knob(28, 0.7, "live")}<span class="val">−6.0 dB</span></span>', 'While it is being set: orange')
                + fig(f'<span class="row g8">{knob(18, 0.4, "shell", bip=True)}<span class="val">12L</span></span>', 'Pan on a track header, as the guide draws it'))
        + '<h3>Value bar</h3><p>The slider inside devices: 16 high, corner 4, in a groove. Drag anywhere; a click never jumps the value; double-click resets.</p>'
        + stage(fig(vrow('Mix', 140, 0.62, '62 %'), 'Unipolar') + fig(vrow('Pan', 140, 0.3, '40L', bip=True), 'Bipolar: from the centre')
                + fig(vrow('Wet', 140, 0.55, '−6.0 dB', drag=True), 'While dragged the fill deepens')
                + fig(vrow('Slope', 140, 0.4, '24 dB', dim=True), 'Unavailable: ink 4')
                + fig(f'<span class="row g12">{uvbar(100, 0.72)}{uvbar(100, 0.38, bip=True)}</span>', 'Upright value bars (faders in a device)'), 'device')
        + '<h3>Slider</h3><p>Outside devices: track volume, Master, sends. A flat 16 px bar on <code>BgSunken</code> with its value written inside. Hover the bars.</p>'
        + stage(fig(slider(160, 0.66, '−3.3 dB'), 'Rest: a quiet grey fill, a 2 px line in ink 2')
                + fig(slider(160, 0.66, '−3.3 dB', 'live'), 'Live: the value bar’s colours')
                + fig(slider(160, 0.66, '−3.3 dB', 'live drag'), 'Dragged')
                + fig(slider(160, 0.08, '−48 dB'), 'Short fill: the value still reads'))
        + '<h3>Wheels</h3>'
        + stage(fig(wheels, 'The round wheel on every instrument; never in a groove')
                + fig(f'<span class="row g10">{wheel(120, 0.5)}{wheel(120, 0.7, "moved")}{wheel(120, 0.85, "drag")}</span>',
                      'Bar: ink 3 at rest, viz ink once moved, orange while dragged'), 'device')
        + '<h3>Meter</h3>'
        + stage(fig(meter(80, (62, 55)), 'Safe') + fig(meter(80, (82, 78)), 'Hot, above 70 %') + fig(meter(80, (96, 91)), 'Over, above 88 %')
                + fig('<span class="lbl">Channels 3 wide, corner 2, 1.5 apart, on the well. No ballistics.</span>', ''))
        + '<h3>Graphs</h3>'
        + stage(fig(graph(360, 156, 'EQ', 'eq', 1), 'Graph window: SurfaceInset, rule frame, labels in the corners, no fills under curves')
                + fig(graph(240, 156, 'Impulse', 'ir'), 'Bars: an impulse response'), 'device')
        + '<h3>Interaction contract</h3><ul class="plain">'
          '<li><b>Vertical drag</b>, up increases, full range over about 140 px; Shift (or Ctrl / ⌘) is fine. A click never jumps the value.</li>'
          '<li><b>Double-click on the control</b> restores the default.</li>'
          '<li><b>⌥ / Alt-click, or a double-click on the read-out</b>, types a value: the read-out turns editable '
          f'<span class="vedit">−3.<u>3</u><span class="unit">dB</span></span>. Enter commits, Esc cancels, Tab moves on.</li>'
          '<li><b>Right-click</b> opens the parameter menu (Type value…, Reset to default).</li></ul>'
        + dodont(['Use value bars for sliders inside a device.', 'Write a shell slider’s value inside the bar.',
                  'Use the round wheel for pitch and mod on every instrument.'],
                 ['Use a thin slider inside a device.', 'Put a groove under a slider outside a device.', 'Use a flat or restyled pitch and mod wheel.']))
    S.append(sec('values', 'Knobs, sliders, meters and wheels', 'Inside devices, knobs have a centre cap and sliders are wide value '
                 'bars. Outside devices the controls stay flat and grey.', body))

    # Fields
    body = (
        stage(fig('<span class="fld">Bass line</span>', 'Field: 26 high, corner 6, 12.5')
              + fig('<span class="fld ph">Project name</span>', 'Placeholder in ink 3')
              + fig('<span class="fld foc">Bass line</span>', 'Focus: 1 px orange outline')
              + fig(f'<span class="row g8">{dd("All ins", label="Input")}</span>', 'Dropdown 22, label before it')
              + fig(f'<span class="search">{icon("search")}Search<b>124</b></span>', 'Search 30, the count on the right'))
        + stage(fig('<span class="fld dv">1.1.00</span>', 'Field in a device: a groove')
                + fig(preset('Stone Vault'), 'Device preset: raised, with previous and next')
                + fig(f'{dd("Hall", "dv")}', 'A device dropdown'), 'device')
        + '<h3>Menus, popovers, tooltips</h3>'
        + stage(fig(menu(), 'Object header, an inline row, fixed columns, the orange hover, a destructive item last')
                + fig('<span class="tt"><b>Snap</b>Snap clips to the grid (hold Alt to drag freely)</span>', 'Tooltip: corner 5, 10.5')
                + fig('<div class="palette"><span class="search">' + icon('search') + 'Export audio</span>'
                      '<div class="mi hov"><span class="mic"></span><span class="mlab">Export audio…</span><span class="mval"></span><span class="msc">⌘⇧E</span></div>'
                      '<div class="mi"><span class="mic"></span><span class="mlab">Export MIDI…</span></div></div>',
                      'Command palette: corner 9, Shadow.Modal'), 'ground'))
    S.append(sec('input', 'Fields', 'Fields and menus have no border; they sit on the well like buttons. Inside a device, a field '
                 'sits in a groove and a preset menu is raised.', body))

    # Cards
    th_rows = (track_header(1, 'Drums', 'MIDI', 0.68, '−2.0 dB', 0.5, 'C', '')
               + track_header(4, 'Bass', 'MIDI', 0.6, '−4.5 dB', 0.42, '12L', 'sr', sel=True)
               + track_header(7, 'Vox', 'Audio', 0.72, '0.0 dB', 0.6, '20R', 'm'))
    clips = (fig(clip(4, 'Root line', 220, 'midi'), 'MIDI clip: 20 % tint, notes and name in the track colour')
             + fig(clip(5, 'Rhodes take 3', 220, 'audio', seed=2), 'Audio clip: spaced bars')
             + fig(clip(6, 'Swell', 220, 'midi', sel=True, seed=3), 'Selected: a 1 px outline in ink 1, never orange'))
    group = ('<div class="grpdemo"><div class="gr head" style="--c:var(--track6)"><span class="gbar"></span><span class="nm">Synths</span><span class="lbl">3 tracks</span></div>'
             + ''.join(f'<div class="gr mem" style="--c:var(--track6)"><span class="gbar"></span><span class="ibar" style="background:var(--track{s})"></span><span class="nm">{n}</span></div>'
                       for s, n in ((4, 'Lead'), (5, 'Pad'), (2, 'Arp'))) + '</div>')
    slots = ('<div class="slots">' + ''.join(
        f'<span class="slot{" sel" if sel else ""}{" empty" if not n else ""}" style="--c:var(--track{s})">{icon("play", 10) if n else ""}{n}</span>'
        for s, n, sel in ((1, 'Beat A', False), (1, 'Beat B', True), (1, '', False), (4, 'Root', False), (4, '', False), (4, 'Walk', False))) + '</div>')
    nodes = ('<div class="mcanvas"><div class="row g24" style="align-items:flex-start">'
             + mx_node('Aurora', 'inst', [('midi', 'MIDI in'), ('cv', 'Cutoff')], [('audio', 'Audio out')])
             + mx_node('Chamber', 'fx', [('audio', 'Audio in')], [('audio', 'Audio out')], state='sel',
                       params=[('Decay', 0.62, '2.4'), ('Size', 0.72, '72'), ('Mix', 0.35, '35')])
             + mx_node('Prism', 'fx', [('audio', 'Audio in')], [('audio', 'Audio out')], state='byp')
             + mx_node('Track out', 'out', [('audio', 'Audio in')], [], small='Master')
             + '</div></div>')
    body = (
        '<h3>Device card</h3>'
        + stage(chamber_card(), 'ground scroll', 'Always 260 high; the width fits the device. Header 32: name, preset menu, type tag, '
                'power at the right. Sections 8 from the edge, 10 apart, 12 padding. No status line under the card.')
        + '<h3>Track header</h3>'
        + stage(f'<div class="threads">{th_rows}</div>', 'flush', '229 × 54, two rows: the name row (name, tag, M, S, arm) and the '
                'controls row (volume, pan, input). A 3 px bar in the track colour; the meter down the right. The middle one is selected.')
        + '<h3>Clip</h3>' + stage(clips, 'lanebg')
        + '<h3>Group</h3>' + stage(group, 'flush', 'A 4 px bar runs from the group row through every member; members get a 6 % wash.')
        + '<h3>Session slot</h3>' + stage(slots, '', 'Corner 5, the track tint with the name in the track colour; selected: an outline in ink 1; empty: the well.')
        + '<h3>Modular node</h3>' + stage(nodes, 'flush scroll', '214 wide, corner 7, raised like a device card. The header is tinted by '
                                          'category (blue instruments, green effects, grey output; plain grey when bypassed). Ports on the edges: '
                                          'round audio, square MIDI, diamond CV. Selected: a thin orange outline.')
        + dodont(['Keep every control a device or track already has.', 'Outline a selected clip or slot in ink 1.',
                  'Tint a Modular node’s header by category.'],
                 ['Add a status line under a device card.', 'Fill a selected clip with orange.']))
    S.append(sec('containers', 'Cards', 'The device card is the one place with depth. Everywhere else panels meet on seams and the '
                 'colour comes from the tracks.', body))

    # Icons
    grid = '<div class="icons">' + ''.join(f'<figure>{icon(k, 24)}<figcaption>{ICON_NAMES[k]}</figcaption></figure>' for k in ICONS) + '</div>'
    body = (stage(grid)
            + stage(fig(f'<span class="row g4">{ib("follow")}{ib("follow", "on")}{ib("play", "live")}</span>', 'Ink 2 at rest, orange on the soft tint, derived ink on solid')
                    + fig('<svg width="96" height="96" viewBox="0 0 24 24" class="ic gridded"><path class="g" d="M0 0h24v24H0z"/>'
                          + ''.join(f'<path class="g" d="M{i} 0v24M0 {i}h24"/>' for i in range(4, 24, 4)) + ICONS['follow'] + '</svg>',
                          'Drawn on the 24-unit grid with a 1.6 line, round caps and joins'), 'deep')
            + '<p>Filled only where a shape needs mass to read: the transport shapes, the OS marks, a favourite star, the disclosure '
              'carets. Every icon is a kind in <code>Controls/Glyph.cs</code>; no unicode symbols and no emoji as text. These '
              'drawings show the rules; the app’s own glyphs are the reference for each shape.</p>')
    S.append(sec('icons', 'Icons', 'One icon set, drawn on a 24-unit grid with one line weight, so every icon matches.', body))

    # Devices summary
    fams = ''
    maxw = 0
    parsed = {}
    for fam, rows in DEVICES.items():
        items = []
        for name, width, change in rows:
            mm = re.match(r'(\d+)', width)
            if not mm:
                fail(f'DESIGN.md {fam}: cannot read the width "{width}" of {name}')
            items.append((name, int(mm.group(1)), width, change))
            maxw = max(maxw, int(mm.group(1)))
        parsed[fam] = items
    for fam, items in parsed.items():
        bars = ''.join(
            f'<div class="dw"><span class="dn">{html.escape(n)}</span><span class="dbar"><i style="width:{w / maxw * 100:.1f}%"></i></span>'
            f'<span class="hex">{html.escape(wt)}</span></div>' for n, w, wt, _ in items)
        notes = ''.join(f'<tr><td>{html.escape(n)}</td><td class="hex">{html.escape(wt)}</td><td>{inline(ch)}</td></tr>' for n, w, wt, ch in items)
        fams += (f'<h3>{fam}</h3>{stage(f"<div class=dws>{bars}</div>")}<details><summary>What changes on each</summary>'
                 f'<div class="tw"><table class="t"><thead><tr><th>Device</th><th>Width</th><th>What changes</th></tr></thead>'
                 f'<tbody>{notes}</tbody></table></div></details>')
    body = ('<p>Every device keeps every control, label and value it had in 0.56.1, in the same groups. Each card is 260 high; '
            'the bars show each card’s width to scale (from the tables in DESIGN.md).</p>' + fams
            + dodont(['Keep every control, in the same groups as 0.56.1.', 'Use pressed-in sections, value bars, capped knobs and orange values inside the card.',
                      'Put the on/off switch at the right of every header, instruments and racks included.'],
                     ['Add a status line under the card; a control that lived there moves into the card.',
                      'Use thin sliders, monospace readouts or capital-letter labels.', 'Fade anything out; draw it in ink 4 with nothing picked.']))
    S.append(sec('devices', 'Devices', 'All 50 devices of 0.56.1, plus Vocoder, are redrawn with these rules.', body))

    # Changes
    changes = [
        ('Toolbar', 'Every cell, readout and menu has its own box', 'No boxes: values are plain text with a quieter unit, and thin lines split the bar into groups'),
        ('Text', 'Geist Mono for every number', 'Geist everywhere; numbers keep a fixed width so they don’t jump as they change; tags in sentence case'),
        ('Clips', 'Solid track colour with a darker title band; selected clips turn orange', 'A pale tint of the track colour with the notes, wave and name in the full colour; a selected clip gets an outline in the text colour'),
        ('Track colours', 'New tracks go green, teal, sky…, so neighbours look alike', 'The same nine colours in a new order so neighbours contrast: green, blue, amber, violet, rose…'),
        ('Track header', 'Three rows of boxed controls and a pan bar', 'Two quieter rows with every control kept, and a small pan knob'),
        ('Groups', 'Tracks in a group are indented a few pixels', 'A bar in the group colour runs down past every track in the group'),
        ('Piano roll', 'A narrow settings column on the left that scrolls', 'All the settings in one strip above the notes'),
        ('Audio editor', 'The same narrow column', 'One column at the same width (232), rearranged so nothing scrolls'),
        ('Follow icon', 'A playhead flag on a ruler', 'A playhead with an arrow pointing right'),
        ('Icon grid', 'Icons drawn on a 16 px grid with a 1.5 px line', 'Icons drawn on a 24 px grid with a 1.6 px line; every icon gets redrawn'),
        ('Modular nodes', 'Ports in rows that don’t line up, so cables bend; a status dot and a bypass button; a grid of large knobs',
         'Ports on the card edges, lined up so cables run straight; a different shape per signal; one power switch; a header tinted by category; parameters as a short list of bars'),
        ('Devices', 'Bordered boxes, big gauge knobs and a status line underneath', 'Even spacing inside the card, smaller knobs with a centre cap, the round wheels from Aurora, Bass, Operator and Volt (Pentad’s flat wheels go) and square switches, no status line'),
        ('Orange', 'Orange for anything playing or selected, always as a solid fill', 'Three strengths: solid for what is playing, a pale tint for what you picked, orange values inside devices'),
        ('Depth', 'Raised buttons everywhere; flat panels', 'Only device cards look raised; the sections inside them look pressed in, and their controls sit in grooves'),
        ('Device card size', 'Every card 700 × 260', 'Always 260 high; the width grows to fit the device (Pentad 724, EQ-8 720, or 580 with its right panel folded)'),
        ('Sliders outside devices', 'A 3 px line with a small cap and the value beside it', 'A flat 16 px bar with the value written inside it, in the same family as the device value bars'),
        ('Device sliders', 'A thin line with a small handle', 'A wider bar you can drag anywhere, with an orange line at the value'),
        ('Corners', 'Small corners (2 to 4 px), nothing fully round', 'Rounder corners, from 2 px on small parts up to 9 px on device cards'),
    ]
    rows = ''.join(f'<tr><td><b>{a}</b></td><td>{b}</td><td>{c}</td></tr>' for a, b, c in changes)
    body = (f'<p>What is drawn differently from Nota 0.56.1. Anything not listed stays as it is. The work lands in phases on the '
            f'<code>ui/style-guide</code> branch; DESIGN.md § Known drift says what is not built yet.</p>'
            f'<div class="tw"><table class="t"><thead><tr><th>What</th><th>0.56.1</th><th>Now</th></tr></thead><tbody>{rows}</tbody></table></div>')
    S.append(sec('changes', 'Changes from 0.56.1', 'For anyone building this.', body))
    return ''.join(S)


NAV = [('start', 'Overview'), ('colour', 'Colour'), ('accent', 'Orange'), ('type', 'Text'), ('geometry', 'Shapes'),
       ('keys', 'Buttons'), ('values', 'Knobs'), ('input', 'Fields'), ('containers', 'Cards'), ('icons', 'Icons'),
       ('devices', 'Devices'), ('changes', 'Changes')]

STYLE = r'''
@font-face{font-family:"Geist";font-weight:400;font-display:swap;src:local("Geist"),local("Geist-Regular"),url("assets/fonts/Geist-Regular.ttf") format("truetype")}
@font-face{font-family:"Geist";font-weight:500;font-display:swap;src:local("Geist Medium"),local("Geist-Medium"),url("assets/fonts/Geist-Medium.ttf") format("truetype")}
@font-face{font-family:"Geist";font-weight:600;font-display:swap;src:local("Geist SemiBold"),local("Geist-SemiBold"),url("assets/fonts/Geist-SemiBold.ttf") format("truetype")}
@font-face{font-family:"Geist";font-weight:700;font-display:swap;src:local("Geist Bold"),local("Geist-Bold"),url("assets/fonts/Geist-Bold.ttf") format("truetype")}
:root{--font:"Geist",system-ui,-apple-system,"Segoe UI",sans-serif}
*{box-sizing:border-box}
html{background:var(--bg-app)}
body{margin:0;background:var(--bg-app);color:var(--text-primary);font:400 12px/1.5 var(--font);font-feature-settings:"tnum";font-variant-numeric:tabular-nums;-webkit-font-smoothing:antialiased}
a{color:var(--acc-text)}
code{font:inherit;font-size:.95em;color:var(--text-secondary);background:var(--well);padding:0 4px;border-radius:4px;white-space:nowrap}
.flex{flex:1 1 auto}
.row{display:flex;align-items:center;flex-wrap:wrap}
.col{display:flex;flex-direction:column}
.col.c{align-items:center}
.g4{gap:4px}.g6{gap:6px}.g8{gap:8px}.g10{gap:10px}.g12{gap:12px}.g24{gap:24px}
.lbl{font-size:11px;color:var(--text-tertiary);white-space:nowrap}
.l2{font-size:11px;color:var(--text-secondary);white-space:nowrap}
.val{font-size:11.5px;color:var(--text-primary);white-space:nowrap}
.unit{font-size:11px;font-weight:400;color:var(--text-tertiary);margin-left:4px}

/* page chrome */
.mast{position:sticky;top:0;z-index:20;display:flex;align-items:center;gap:6px 14px;flex-wrap:wrap;min-height:44px;padding:7px 20px;background:var(--slot-surface-deep);border-bottom:1px solid var(--seam)}
.mast h1{margin:0;font:600 13px/1 var(--font);white-space:nowrap}
.mast nav{display:flex;flex-wrap:wrap;gap:2px}
.mast nav a{color:var(--text-tertiary);text-decoration:none;font-size:12px;padding:3px 7px;border-radius:6px}
.mast nav a:hover{background:var(--well);color:var(--text-primary)}
.mast .seg span{cursor:pointer}
.page{max-width:1180px;margin:0 auto;padding:0 20px 80px}
section.doc{padding:30px 0 10px;border-top:1px solid var(--rule);scroll-margin-top:52px}
section.doc:first-child{border-top:0}
h2{margin:0 0 6px;font:600 17px/1.3 var(--font);color:var(--text-heading)}
h3{margin:24px 0 8px;font:600 13px/1.3 var(--font);color:var(--text-primary)}
p{margin:0 0 10px;max-width:80ch;font-size:13px;line-height:1.55;color:var(--text-secondary)}
p.lead{font-size:13px;color:var(--text-secondary)}
p.cap{font-size:11px;color:var(--text-tertiary);margin:-2px 0 12px}
ul.plain{margin:6px 0 12px;padding-left:18px;color:var(--text-secondary);font-size:13px;line-height:1.6;max-width:90ch}
.stage{display:flex;flex-wrap:wrap;gap:20px 28px;align-items:flex-start;margin:10px 0;padding:18px;overflow-x:auto;background:var(--panel);border:1px solid var(--seam);border-radius:9px}
.stage.ground{background:var(--bg-app)}
.stage.deep{background:var(--slot-surface-deep)}
.stage.lanebg{background:var(--lane-b)}
.stage.device{background:var(--device-section);box-shadow:var(--sh-section);border:0;border-radius:7px}
.stage.flush{padding:0;overflow:hidden;display:block}
.stage.scroll{overflow-x:auto}
figure.sp{margin:0;display:flex;flex-direction:column;align-items:flex-start;gap:8px;max-width:100%}
figure.sp figcaption{font-size:11px;color:var(--text-tertiary);max-width:260px}
.tw{overflow-x:auto;margin:8px 0 14px}
table.t{border-collapse:collapse;width:100%;font-size:12px}
table.t th{text-align:left;font-weight:400;font-size:10px;color:var(--text-disabled);padding:6px 8px;border-bottom:1px solid var(--rule);white-space:nowrap}
table.t td{padding:6px 8px;border-bottom:1px solid color-mix(in srgb,var(--rule) 55%,transparent);color:var(--text-secondary);vertical-align:top}
table.t td.hex{white-space:nowrap;color:var(--text-tertiary);font-size:11.5px}
table.t td.bad{color:var(--danger)!important}
table.ct tr.crow{cursor:pointer}
table.ct tr.crow:hover td{background:var(--well)}
td.chip{width:44px}
td.chip span{display:block;width:34px;height:22px;border-radius:5px;background:var(--panel);box-shadow:0 0 0 1px var(--rule);overflow:hidden}
td.chip i{display:block;width:100%;height:100%}
.two{display:grid;grid-template-columns:3fr 2fr;gap:16px}
.three{display:grid;grid-template-columns:1fr 1fr;gap:16px}
@media (max-width:820px){.two,.three{grid-template-columns:1fr}}
.princ{display:grid;grid-template-columns:repeat(auto-fill,minmax(250px,1fr));gap:8px;margin:14px 0 4px}
.princ div{display:grid;grid-template-columns:22px 1fr;gap:2px 8px;padding:12px;background:var(--panel);border:1px solid var(--seam);border-radius:9px}
.princ .pn{grid-row:span 2;font-size:17px;font-weight:500;color:var(--acc-text);line-height:1.1}
.princ b{font-weight:600;font-size:12.5px}
.princ span:last-child{color:var(--text-secondary);font-size:12px}
.dd2{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin:16px 0 6px}
@media (max-width:700px){.dd2{grid-template-columns:1fr}}
.dd2>div{padding:10px 14px;background:var(--panel);border-radius:7px;box-shadow:inset 2px 0 0 var(--meter-safe)}
.dd2>.dont{box-shadow:inset 2px 0 0 var(--danger)}
.dd2 b{font-weight:600}
.dd2 ul{margin:4px 0 0;padding-left:16px;color:var(--text-secondary);font-size:12.5px}
details{margin:2px 0 10px}
summary{cursor:pointer;color:var(--text-tertiary);font-size:12px}
.toast{position:fixed;left:50%;bottom:20px;transform:translateX(-50%);padding:6px 12px;border-radius:5px;background:var(--slot-tooltip-bg);color:var(--text-primary);box-shadow:var(--shadow-popover);font-size:11.5px;opacity:0;transition:opacity .12s ease-out;pointer-events:none}
.toast.show{opacity:1}

/* controls */
.ic{display:block;flex:none;stroke:currentColor;fill:none;stroke-width:1.6;stroke-linecap:round;stroke-linejoin:round}
.ic .f{fill:currentColor;stroke:none}
.ib{width:28px;height:28px;border-radius:var(--r-group);display:inline-flex;align-items:center;justify-content:center;color:var(--text-secondary);flex:none;transition:background-color .12s ease-out}
.ib:hover,.ib.hov{background:var(--well)}
.ib.on{background:var(--accent-soft);color:var(--acc-text)}
.ib.live{background:var(--accent);color:var(--text-on-accent)}
.grp{display:inline-flex;gap:2px;padding:2px;border-radius:var(--r-card);background:var(--well)}
.vsep{width:1px;height:22px;background:var(--rule);flex:none}
.seg{display:inline-flex;padding:2px;border-radius:var(--r-group);background:var(--well);flex:none}
.seg span{height:20px;line-height:20px;padding:0 8px;border-radius:var(--r-cell);font-size:11.5px;color:var(--text-tertiary);white-space:nowrap}
.seg span.on{background:var(--accent-soft);color:var(--acc-text)}
.seg span.dis{color:var(--text-disabled)}
.seg.dv{background:var(--groove);box-shadow:var(--sh-groove)}
.seg.dv span{padding:0 6px;font-size:11px}
.seg.dv span.on{box-shadow:var(--sh-rise)}
.chip{display:inline-flex;align-items:center;gap:5px;height:24px;padding:0 10px;border-radius:var(--r-group);background:var(--well);color:var(--text-secondary);font-size:12px;white-space:nowrap}
.chip.on{background:var(--accent-soft);color:var(--acc-text)}
.cdot{width:7px;height:7px;border-radius:2px}
.pill{display:inline-flex;align-items:center;gap:6px;padding:3px 7px;border-radius:var(--r-field);background:var(--well);color:var(--text-secondary);font-size:11px;white-space:nowrap}
.pill.on{background:var(--accent-soft);color:var(--acc-text)}
.beats{display:inline-flex;gap:2px}.beats .b{width:4px;height:4px;border-radius:1px;background:var(--text-disabled)}.beats .b.on{background:var(--acc-text)}
.dd{display:inline-flex;align-items:center;justify-content:space-between;gap:6px;height:22px;padding:0 8px;border-radius:var(--r-field);background:var(--well);font-size:11.5px;color:var(--text-primary);white-space:nowrap;min-width:90px}
.dd:after{content:"";width:5px;height:5px;border-right:1.3px solid var(--text-tertiary);border-bottom:1.3px solid var(--text-tertiary);transform:rotate(45deg) translate(-1px,-1px)}
.dd.dv{background:var(--surface-hover);box-shadow:var(--sh-key)}
.dd.sm{min-width:0;height:18px;font-size:10.5px;padding:0 6px;gap:4px}
.lbl+.dd{margin-left:6px}
.pre{display:inline-flex;align-items:center;gap:2px}
.pre .pa{color:var(--text-tertiary);transform:rotate(90deg)}
.pre .pa.nx{transform:rotate(-90deg)}
.pre .dd{min-width:110px}
.fld{display:inline-flex;align-items:center;height:26px;min-width:140px;padding:0 9px;border-radius:var(--r-field);background:var(--well);font-size:12.5px;color:var(--text-primary)}
.fld.ph{color:var(--text-tertiary)}
.fld.foc{box-shadow:inset 0 0 0 1px var(--accent)}
.fld.dv{background:var(--groove);box-shadow:var(--sh-groove)}
.search{display:inline-flex;align-items:center;gap:7px;height:30px;width:240px;padding:0 9px;border-radius:var(--r-group);background:var(--well);color:var(--text-tertiary);font-size:12.5px}
.search b{margin-left:auto;font-weight:400;font-size:11px}
.search.sm{width:100%}
.tb{width:18px;height:18px;border-radius:var(--r-cell);display:inline-flex;align-items:center;justify-content:center;font-size:10px;font-weight:650;color:var(--text-tertiary);background:var(--well);flex:none}
.tb .dot{display:block;width:6px;height:6px;border-radius:50%;background:var(--record)}
.tb.m{background:var(--mute);color:var(--ink-on-mute)}
.tb.s{background:var(--solo);color:var(--ink-on-solo)}
.tb.ra{background:var(--record)}
.tb.ra .dot{background:var(--record-ink)}
.tb.dv{width:14px;height:16px;font-size:9.5px;font-weight:550;color:var(--text-secondary);background:var(--surface-hover);box-shadow:var(--sh-key)}
.sw{position:relative;display:inline-block;width:20px;height:12px;border-radius:var(--r-switch);background:var(--well-track);box-shadow:var(--sh-switch);flex:none}
.sw:after{content:"";position:absolute;top:2px;left:2px;width:8px;height:8px;border-radius:var(--r-fine);background:var(--text-tertiary)}
.sw.on{background:var(--text-secondary)}
.sw.on:after{left:10px;background:var(--slot-surface-deep)}
.sw.acc{background:var(--accent)}
.sw.acc:after{left:10px;background:var(--text-on-accent)}
.swr{display:inline-flex;align-items:center;gap:6px}
.tabs{display:flex;gap:16px;height:26px;border-bottom:1px solid var(--rule);align-items:flex-end}
.tabs span{font-size:12px;line-height:24px;color:var(--text-tertiary);white-space:nowrap}
.tabs span.on{color:var(--text-primary);font-weight:580;box-shadow:inset 0 -2px 0 var(--accent)}
.tabs.out{gap:2px}
.tabs.out span{padding:0 10px}
.tabs.out span.on{box-shadow:inset 0 -2px 0 var(--text-primary)}
.tabs.mini{height:18px;border:0;gap:10px}
.tabs.mini span{font-size:10.5px;line-height:16px}
.btn{display:inline-flex;align-items:center;height:26px;padding:0 12px;border-radius:var(--r-field);background:var(--well);font-size:12px;color:var(--text-secondary)}
.btn.primary{background:color-mix(in srgb,var(--text-primary) 14%,transparent);color:var(--text-primary);font-weight:600}
.ro{font-size:17px;font-weight:500;color:var(--text-primary);white-space:nowrap;letter-spacing:0}
.stat{display:inline-flex;align-items:center;gap:5px;font-size:11px;color:var(--text-secondary)}
.sdot{width:6px;height:6px;border-radius:50%;background:var(--text-disabled)}
.sdot.on{background:var(--success)}

/* knobs and bars */
.kn{display:block;flex:none;overflow:visible}
.kn path{fill:none;stroke-width:2.4;stroke-linecap:round}
.kn line{stroke-linecap:round}
.kc{display:inline-flex;flex-direction:column;align-items:center;gap:2px;min-width:48px}
.kv{font-size:11px;color:var(--text-primary);white-space:nowrap}
.kl{font-size:10.5px;color:var(--text-tertiary);white-space:nowrap}
.kc.mod .kl,.vr.mod .kl{color:var(--text-primary)}
.kc.dim .kv,.kc.dim .kl,.vr.dim .kl,.vr.dim .kv{color:var(--text-disabled)}
.ks{display:inline-flex;align-items:center;gap:6px;line-height:1.2}
.vb{position:relative;display:inline-block;flex:none;border-radius:var(--r-value-bar);background:var(--groove);box-shadow:var(--sh-groove);overflow:hidden}
.vb b{position:absolute;top:0;bottom:0;background:var(--accent-soft)}
.vb i{position:absolute;top:0;bottom:0;width:3px;margin-left:-1.5px;background:var(--accent)}
.vb.drag b{background:color-mix(in oklab,var(--accent) 45%,var(--accent-soft))}
.vb.dim b{background:transparent}
.vb.dim i{background:var(--text-disabled)}
.vr{display:inline-flex;align-items:center;gap:8px}
.vr>.kl{width:52px}
.vr>.kv{width:58px;text-align:right}
.uvb{position:relative;display:inline-block;width:16px;border-radius:var(--r-value-bar);background:var(--groove);box-shadow:var(--sh-groove);overflow:hidden;flex:none}
.uvb b{position:absolute;left:0;right:0;background:var(--accent-soft)}
.uvb i{position:absolute;left:0;right:0;height:3px;margin-top:-1.5px;background:var(--accent)}
.sl{position:relative;display:inline-block;height:16px;flex:none;border-radius:var(--r-value-bar);background:var(--bg-sunken);overflow:hidden;cursor:ns-resize}
.sl b{position:absolute;left:0;top:0;bottom:0;background:color-mix(in oklab,var(--text-tertiary) 40%,var(--bg-sunken));transition:background-color .12s ease-out}
.sl i{position:absolute;top:0;bottom:0;width:2px;margin-left:-1px;background:var(--text-secondary)}
.sl em{position:absolute;left:6px;top:0;line-height:16px;font-style:normal;font-size:10.5px;color:var(--text-primary);white-space:nowrap;pointer-events:none}
.sl:hover b,.sl.live b{background:var(--accent-soft)}
.sl:hover i,.sl.live i{background:var(--accent)}
.sl.drag b{background:color-mix(in oklab,var(--accent) 45%,var(--accent-soft))}
.mt{display:inline-flex;gap:1.5px;align-items:flex-end}
.mc{position:relative;display:inline-block;width:3px;border-radius:var(--r-fine);background:var(--well);overflow:hidden}
.mc i{position:absolute;inset:0;background:linear-gradient(to top,var(--meter-safe) 0 70%,var(--meter-hot) 70% 88%,var(--meter-over) 88%)}
.wh{position:relative;display:inline-block;width:16px;border-radius:8px;background:var(--bg-sunken);box-shadow:inset 0 0 0 1px var(--seam);flex:none}
.wh .grip{position:absolute;left:2px;right:2px;top:33.3%;height:33.3%;border-radius:6px;background:var(--surface-raised);display:flex;flex-direction:column;justify-content:center;gap:5px;align-items:center}
.wh .grip i{display:block;width:6px;height:1px;background:var(--rule)}
.wh .bar{position:absolute;left:2px;right:2px;height:2px;border-radius:1px;background:var(--text-tertiary)}
.wh.moved .bar{background:var(--viz-ink)}
.wh.drag .bar{background:var(--accent)}
.wrail{display:flex;flex-direction:column;align-items:center;gap:6px;padding:10px 14px;border-radius:var(--r-group);background:var(--device-section);box-shadow:var(--sh-section)}
.gw{display:block;border-radius:var(--r-field);background:var(--surface-inset);box-shadow:inset 0 0 0 1px var(--rule)}
.gw .ax{font-size:10px;fill:var(--text-disabled)}
.gw .gt{font-size:10.5px;fill:var(--text-tertiary)}
.vedit{display:inline-flex;align-items:baseline;font-size:11px;color:var(--text-primary);box-shadow:inset 0 -1px 0 var(--accent);padding:0 1px}
.vedit u{text-decoration:none;background:color-mix(in srgb,var(--accent) 35%,transparent)}
.tline{position:relative;display:block;width:200px;height:28px;background:var(--lane-b);border-radius:var(--r-field);overflow:hidden}
.tline .loop{position:absolute;left:30px;width:120px;top:0;height:4px;background:var(--accent)}
.tline .ph{position:absolute;top:0;bottom:0;width:1px;background:var(--accent)}
.tline .ph:before{content:"";position:absolute;left:-3px;top:0;width:7px;height:5px;background:var(--accent);clip-path:polygon(0 0,100% 0,50% 100%)}
.cardlet{display:inline-flex;align-items:center;gap:6px;padding:8px 10px;border-radius:var(--r-group);background:var(--device-section);box-shadow:var(--sh-section)}

/* cards */
.card{border-radius:var(--r-card);overflow:hidden;background:var(--surface-raised);box-shadow:var(--sh-card);flex:none}
.card.sel{box-shadow:var(--sh-card),0 0 0 2.5px var(--accent)}
.chead{display:flex;align-items:center;gap:8px;height:32px;padding:0 10px;background:var(--device-header);box-shadow:inset 0 1px 0 var(--bevel);border-bottom:1px solid var(--seam)}
.cname{font-size:13px;font-weight:600;white-space:nowrap}
.ctag{font-size:11px;color:var(--text-tertiary);white-space:nowrap}
.cbody{display:flex;gap:10px;padding:8px;height:228px}
.card .cbody{height:228px}
.dsec{display:flex;flex-direction:column;gap:8px;padding:12px;border-radius:var(--r-group);background:var(--device-section);box-shadow:var(--sh-section);min-width:0}
.dst{display:flex;align-items:center;gap:8px;font-size:10.5px;color:var(--text-tertiary);height:18px}
.kgrid{display:grid;grid-template-columns:repeat(4,1fr);gap:10px 6px;justify-items:center;align-content:space-around;flex:1}
.clip{position:relative;border-radius:var(--r-field);overflow:hidden;background:color-mix(in oklab,var(--c) 20%,var(--lane-b));box-shadow:inset 0 0 0 1px color-mix(in oklab,var(--c) 45%,transparent);flex:none}
.clip.sel{box-shadow:inset 0 0 0 1px var(--text-primary)}
.clip b{position:absolute;left:8px;top:4px;font-size:11.5px;font-weight:600;color:var(--c);white-space:nowrap}
.clip svg{position:absolute;left:0;top:20px}
.th{position:relative;width:229px;height:54px;background:var(--panel);border-bottom:1px solid var(--seam);flex:none}
.th.sel{background:var(--track-header-selected)}
.th .bar{position:absolute;left:0;top:0;bottom:0;width:3px;background:var(--c)}
.th .r1{position:absolute;left:12px;right:18px;top:7px;height:18px;display:flex;align-items:center;gap:4px}
.th .r2{position:absolute;left:12px;right:18px;top:29px;height:20px;display:flex;align-items:center;gap:5px}
.th .nm{font-size:13px;font-weight:580;white-space:nowrap;margin-right:4px}
.th .tmeter{position:absolute;right:6px;top:7px}
.threads{display:flex;flex-direction:column;width:229px;border-right:1px solid var(--seam)}
.lane{position:relative;height:55px;background:var(--lane-b);border-bottom:1px solid var(--seam)}
.lane.sel{background:var(--lane-selected)}
.lane .cpos{position:absolute;top:2px}
.grpdemo{width:360px}
.gr{position:relative;display:flex;align-items:center;gap:8px;height:30px;padding-left:16px;border-bottom:1px solid var(--seam);background:var(--panel)}
.gr.head{background:color-mix(in oklab,var(--c) 16%,var(--panel))}
.gr.mem{background:color-mix(in oklab,var(--c) 6%,var(--panel));padding-left:22px}
.gr .gbar{position:absolute;left:0;top:0;bottom:0;width:4px;background:var(--c)}
.gr .ibar{position:absolute;left:10px;top:7px;bottom:7px;width:3px;border-radius:2px}
.gr .nm{font-size:13px;font-weight:580}
.slots{display:grid;grid-template-columns:repeat(3,110px);gap:4px}
.slot{display:flex;align-items:center;gap:6px;height:26px;padding:0 8px;border-radius:var(--r-cell);background:color-mix(in oklab,var(--c) 22%,var(--lane-b));color:var(--c);font-size:11.5px;font-weight:600;white-space:nowrap}
.slot.empty{background:var(--well)}
.slot.sel{box-shadow:inset 0 0 0 1px var(--text-primary)}
.mcanvas{padding:22px;background:var(--bg-app) radial-gradient(circle,var(--rule) .9px,transparent 1px) 0 0/20px 20px;min-width:1000px}
.node{position:relative;width:214px;border-radius:var(--r-group);background:var(--surface-raised);box-shadow:var(--sh-card);flex:none}
.node.sel{box-shadow:0 0 0 1px var(--accent),inset 0 1px 0 var(--bevel)}
.node .nh{display:flex;align-items:center;gap:8px;height:34px;padding:0 10px;border-radius:7px 7px 0 0;background:color-mix(in srgb,var(--cc) 24%,var(--surface-raised));border-bottom:1px solid color-mix(in srgb,var(--cc) 40%,var(--surface-raised))}
.node.byp .nh{background:var(--surface-card);border-bottom-color:var(--rule)}
.node.byp .nh b{color:var(--text-tertiary)}
.node .nh b{font-size:12.5px;font-weight:600;white-space:nowrap}
.node .nh small{font-size:11px;color:var(--text-tertiary)}
.node .act{margin-left:auto;display:flex;align-items:center;gap:8px;color:var(--text-tertiary)}
.node .ondot{width:6px;height:6px;border-radius:2px;background:var(--success)}
.node .ports{position:relative;padding:0 0 6px}
.node .pr{display:flex;justify-content:space-between;height:22px;align-items:center;padding:0 12px;font-size:10.5px;color:var(--text-tertiary)}
.node .pt{position:absolute;width:10px;height:10px;margin-top:-5px;border:2px solid var(--surface-raised)}
.node .pt.l{left:-5px}.node .pt.r{right:-5px}
.node .pt.audio{border-radius:50%;background:var(--slot-sage)}
.node .pt.midi{border-radius:2px;background:var(--n-midi)}
.node .pt.cv{background:var(--accent);transform:rotate(45deg);border-radius:1px}
.node .plist{border-top:1px solid var(--rule);padding:6px 10px 6px;display:flex;flex-direction:column;gap:3px}
.node .prm{display:flex;align-items:center;gap:8px;height:18px;font-size:10.5px;color:var(--text-secondary)}
.node .prm>span:first-child{width:52px}
.node .prm .vb{border-radius:3px}
.node .prm em{font-style:normal;width:24px;text-align:right;color:var(--text-primary)}
:root{--n-midi:#6D8FB5;--n-inst:#6D8FB5;--n-fx:var(--slot-sage)}

/* menus */
.menu{padding:4px;border-radius:var(--r-group);background:var(--surface-raised);box-shadow:0 0 0 1px var(--seam)}
.mhead{display:flex;align-items:center;gap:8px;height:26px;padding:0 8px}
.mhead i{width:10px;height:10px;border-radius:3px}
.mhead b{font-weight:600;font-size:12px}
.mhead .lbl{margin-left:auto}
.mrow{display:flex;align-items:center;justify-content:space-between;gap:8px;padding:2px 8px 4px}
.msep{height:1px;background:var(--rule);margin:4px 6px}
.mi{display:grid;grid-template-columns:18px 1fr auto auto 12px;align-items:center;gap:6px;height:24px;padding:0 6px;border-radius:var(--r-field);font-size:12px;color:var(--text-primary)}
.mi .mic{color:var(--text-tertiary)}
.mi .mic .ck{color:var(--accent)}
.mi .mval,.mi .msc{font-size:11px;color:var(--text-tertiary);display:inline-flex}
.mi .car{color:var(--text-tertiary)}
.mi.hov{background:var(--accent);color:var(--text-on-accent)}
.mi.hov .mic,.mi.hov .mval,.mi.hov .msc,.mi.hov .car{color:var(--text-on-accent)}
.mi.dis,.mi.dis .msc{color:var(--text-disabled)}
.mi.danger,.mi.danger .mic{color:var(--danger)}
.tt{display:inline-block;max-width:220px;padding:5px 8px;border-radius:var(--r-cell);background:var(--slot-tooltip-bg);color:var(--text-secondary);font-size:10.5px;line-height:1.35;box-shadow:0 0 0 1px var(--seam)}
.tt b{display:block;font-weight:600;color:var(--text-primary)}
.palette{width:320px;padding:6px;border-radius:var(--r-card);background:var(--panel);box-shadow:var(--shadow-modal)}
.palette .search{width:100%;margin-bottom:4px;color:var(--text-primary)}

/* shapes */
.rad{display:flex;flex-wrap:wrap;gap:18px}
.rad figure{margin:0;display:flex;flex-direction:column;gap:4px;width:118px}
.rad i{display:block;width:64px;height:44px;margin-bottom:4px;background:var(--surface-raised);box-shadow:0 0 0 1px var(--seam),inset 0 1px 0 var(--bevel)}
.rad b{font-weight:600;font-size:12px}
.rad span{font-size:11px;color:var(--text-tertiary)}
.shs{display:block;width:34px;height:22px;border-radius:5px;background:var(--surface-raised)}

/* icons */
.icons{display:grid;grid-template-columns:repeat(auto-fill,minmax(92px,1fr));gap:8px;width:100%}
.icons figure{margin:0;display:flex;flex-direction:column;align-items:center;gap:8px;padding:12px 4px;border-radius:var(--r-group);color:var(--text-secondary)}
.icons figure:hover{background:var(--well);color:var(--text-primary)}
.icons figcaption{font-size:10.5px;color:var(--text-tertiary);text-align:center}
.gridded .g{stroke:var(--rule);stroke-width:.08;fill:none}

/* the window mock */
.win{min-width:1120px;background:var(--bg-app);color:var(--text-primary)}
.wtitle{position:relative;height:32px;display:flex;align-items:center;justify-content:center;background:var(--bg-app);border-bottom:1px solid var(--seam)}
.wtitle .lights{position:absolute;left:12px;display:flex;gap:8px}
.wtitle .lights i{width:12px;height:12px;border-radius:50%;background:var(--rule)}
.wname{font-size:12px;font-weight:500;color:var(--text-secondary)}
.toolbar{display:flex;align-items:center;gap:8px;height:44px;padding:0 10px;background:var(--slot-surface-deep);border-bottom:1px solid var(--seam);min-width:1080px}
.toolbar .ro{margin:0 4px}
.wbody{display:flex;border-bottom:1px solid var(--seam)}
.rail{width:40px;flex:none;display:flex;flex-direction:column;align-items:center;gap:6px;padding-top:6px;background:var(--slot-surface-deep);border-right:1px solid var(--seam)}
.rsel{position:relative}
.rsel:before{content:"";position:absolute;left:-6px;top:7px;width:2px;height:14px;border-radius:1px;background:var(--accent)}
.browser{width:220px;flex:none;background:var(--panel);border-right:1px solid var(--seam);padding:6px 0}
.bsearch{padding:0 8px 6px}
.bchips{display:flex;gap:4px;padding:0 8px 8px;flex-wrap:wrap}
.brow{display:flex;align-items:center;gap:8px;height:30px;margin:0 4px;padding:0 8px;border-radius:var(--r-field);font-size:12px}
.brow i{width:8px;height:8px;border-radius:2px;flex:none}
.brow .lbl{margin-left:auto}
.brow.sel{background:var(--well);box-shadow:inset 2px 0 0 var(--accent)}
.wmain{flex:1;min-width:0;display:flex;flex-direction:column}
.canvas{background:var(--bg-app)}
.cvrow{display:flex}
.thcol{display:flex;flex-direction:column;border-right:1px solid var(--seam)}
.ruler-pad{height:24px;background:var(--panel);border-bottom:1px solid var(--seam)}
.lanes{position:relative;flex:1;min-width:0;overflow:hidden}
.ruler{position:relative;height:24px;background:var(--panel);border-bottom:1px solid var(--seam)}
.ruler span{position:absolute;top:5px;font-size:10px;color:var(--text-disabled)}
.playhead{position:absolute;top:0;bottom:0;width:1px;background:var(--accent);z-index:2}
.playhead:before{content:"";position:absolute;left:-3px;top:0;width:7px;height:5px;background:var(--accent);clip-path:polygon(0 0,100% 0,50% 100%)}
.detail{border-top:1px solid var(--seam)}
.ptool{display:flex;align-items:center;gap:8px;height:32px;padding:0 8px;background:var(--panel);border-bottom:1px solid var(--seam)}
.ptool .tabs{border:0;height:32px}
.ptool .tabs span{line-height:30px}
.ptool .sq{width:10px;height:10px;border-radius:2px}
.ptool .tn{font-size:12px;font-weight:580}
.dbody{display:flex;gap:6px;padding:6px;background:var(--bg-app)}
.adddev{width:28px;height:260px;border-radius:var(--r-group);background:var(--well);display:flex;align-items:center;justify-content:center;color:var(--text-tertiary)}
.status{height:22px;line-height:22px;padding:0 16px;font-size:11px;color:var(--text-secondary);background:var(--bg-app)}

/* devices */
.dws{display:flex;flex-direction:column;gap:3px;width:100%}
.dw{display:grid;grid-template-columns:150px 1fr 110px;align-items:center;gap:10px;font-size:12px}
.dw .dn{color:var(--text-primary)}
.dw .dbar{height:10px;border-radius:3px;background:var(--well)}
.dw .dbar i{display:block;height:100%;border-radius:3px;background:color-mix(in oklab,var(--text-tertiary) 40%,var(--bg-sunken));box-shadow:inset -2px 0 0 var(--text-secondary)}
.dw .hex{font-size:11.5px;color:var(--text-tertiary)}
.tord{display:inline-flex;flex-direction:column;align-items:center;justify-content:center;width:64px;height:40px;border-radius:var(--r-field);font-size:13px;font-weight:600}
.tord small{font-size:10px;font-weight:500}
.tnum{display:inline-flex;flex-direction:column;align-items:flex-end;font-size:13px;line-height:1.6}
'''


def radius_vars():
    return ':root{' + ''.join(f'--r-{re.sub(r"(?<!^)(?=[A-Z])", "-", k).lower()}:{v};' for k, v in R.items()) + '}'


SCRIPT = r'''<script>
(() => {
  const root = document.documentElement;
  const seg = document.getElementById('theme');
  const names = [...seg.querySelectorAll('[data-v]')].map(b => b.dataset.v);
  function show(v) {
    if (!names.includes(v)) v = 'dark';
    if (v === 'dark') delete root.dataset.variant; else root.dataset.variant = v;
    seg.querySelectorAll('[data-v]').forEach(b => {
      b.classList.toggle('on', b.dataset.v === v);
      b.setAttribute('aria-pressed', b.dataset.v === v ? 'true' : 'false');
    });
    try { localStorage.setItem('studio-variant', v); } catch (e) {}
  }
  seg.addEventListener('click', e => { const b = e.target.closest('[data-v]'); if (b) show(b.dataset.v); });
  seg.addEventListener('keydown', e => {
    const b = e.target.closest('[data-v]');
    if (b && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); show(b.dataset.v); }
  });
  let saved = null; try { saved = localStorage.getItem('studio-variant'); } catch (e) {}
  const asked = new URLSearchParams(location.search).get('variant');   // ?variant=light, ?variant=ember-paper
  show(asked || saved || 'dark');

  const toast = document.getElementById('toast');
  let timer = 0;
  document.querySelectorAll('tr.crow').forEach(row => row.addEventListener('click', () => {
    const map = JSON.parse(row.dataset.hex);
    const hex = map[root.dataset.variant || 'dark'];
    const done = () => { toast.textContent = 'Copied ' + hex; toast.classList.add('show');
                         clearTimeout(timer); timer = setTimeout(() => toast.classList.remove('show'), 1200); };
    try { navigator.clipboard.writeText(hex).then(done, done); } catch (e) { done(); }
  }));
})();
</script>'''


def page():
    theme_btns = ''.join(f'<span data-v="{vname(th)}" role="button" tabindex="0" aria-pressed="false">{th["name"]}</span>' for th in THEMES)
    nav = ''.join(f'<a href="#{i}">{t}</a>' for i, t in NAV)
    head = ('<!doctype html>\n'
            '<!-- SPDX-License-Identifier: AGPL-3.0-only -->\n'
            '<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->\n'
            '<!-- Generated by scripts/design-html.py from the theme files, NotaTheme.axaml and DESIGN.md. Do not edit by hand. -->\n'
            '<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">'
            '<title>Nota design</title>\n<style>\n' + VARIANTS + '\n' + radius_vars() + '\n' + CURRENT_COL + '\n' + STYLE + '</style></head>\n')
    body = (f'<body><header class="mast"><h1>Nota design</h1><nav>{nav}</nav><span class="flex"></span>'
            f'<span class="seg" id="theme" aria-label="Colour theme">{theme_btns}</span></header>'
            f'<main class="page">{build_sections()}</main><div class="toast" id="toast" role="status"></div>\n')
    return head + body + SCRIPT + '\n</body></html>\n'


out = page()
open(OUT, 'w', encoding='utf-8').write(out)
print(f'design-html.py: wrote {os.path.relpath(OUT, ROOT)} ({len(out) // 1024} KB, {len(THEMES)} themes)')
