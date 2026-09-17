#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# Regenerates DESIGN.html from the theme: the colour variables for both variants are read
# from src/managed/Nota.App/Theme/NotaTheme.axaml, so the page can never show a value the
# app does not use. Run after changing a token:  python3 scripts/design-html.py
import re, math, os
ROOT=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
t=open(ROOT+'/src/managed/Nota.App/Theme/NotaTheme.axaml',encoding='utf-8').read()
def branch(name):
    i=t.index('<ResourceDictionary x:Key="%s">'%name); j=t.index('</ResourceDictionary>',i); return t[i:j]
rx=r'<SolidColorBrush x:Key="Brush\.([\w.]+)">(#[0-9A-Fa-f]+)</SolidColorBrush>'
D=dict(re.findall(rx,branch('Dark'))); L=dict(re.findall(rx,branch('Light')))
def css(h):
    h=h.lstrip('#')
    if len(h)==8: a=int(h[:2],16)/255; r,g,b=(int(h[i:i+2],16) for i in (2,4,6)); return f'rgba({r},{g},{b},{a:.3f})'
    return '#'+h.upper()
def var(k): return '--'+re.sub(r'(?<!^)(?=[A-Z])','-',k).lower().replace('.','-')
root='\n'.join(f'  {var(k)}: {css(v)};' for k,v in D.items())
paper='\n'.join(f'  {var(k)}: {css(L[k])};' for k in D if k in L)
geo={m.group(1):m.group(2) for m in re.finditer(r'x:Key="((?:Radius|Control|Space)\.\w+)">([0-9.]+)<',t)}

def sw(key, name, use):
    return (f'<button class="sw" data-dark="{D[key]}" data-light="{L[key]}" title="Copy hex">'
            f'<span class="sw__chip" style="background:var({var(key)})"></span>'
            f'<span class="sw__name">{name}</span><code class="sw__key">Brush.{key}</code>'
            f'<span class="sw__hex mono"></span><span class="sw__use">{use}</span></button>')

surfaces=[('SurfaceAbyss','Void','Deepest recess — the transport strip'),('BgApp','App','Window ground, plugin body'),('Gutter','Gutter','The gaps panels float in'),('BgSunken','Well','Graph windows, fields, slider tracks'),('Panel','Panel','Browser, headers, inspector'),('SurfaceCard','Card','A section inside a device'),('SurfaceRaised','Raised','Button at rest'),('SurfaceHover','Hover','Hovered button or row'),('TrackOff','Track off','Track of an off switch'),('SurfaceSelected','Brass Wash','Selected row, engaged ground')]
lines=[('Hairline','Hairline','Row dividers, graph frame'),('BorderDefault','Border','Panel, button, field'),('BorderStrong','Border strong','Knob cap, hovered border'),('BorderBrass','Border brass','Engaged button, focus'),('GridBeat','Grid','Grid inside graphs'),('GridBar','Grid bar','Bar lines')]
inks=[('TextHeading','Ink 0','Page title, project name'),('TextPrimary','Ink 1','Names, values'),('TextStrong','Ink 2','Button text, readouts'),('TextSecondary','Ink 3','Inactive but readable'),('TextMuted','Ink 4','Explanations, units'),('TextTertiary','Ink 5','Caps labels, empty states'),('TextDisabled','Ink 6','Placeholder, disabled'),('TextAxis','Ink 7','Axis labels in graphs only')]
brass=[('Accent','Brass','Arc, fill, playhead, one solid button'),('AccentBright','Brass Light','Pointer, modified label'),('AccentHover','Brass Hover','Engaged text'),('AccentDeep','Brass Deep','Pressed'),('AccentDim','Brass Dim','Eyebrows, section marks'),('AccentEdge','Brass Edge','Brass chip border')]
sem=[('Record','Record','Active recording — the only red'),('RecordInk','Record ink','Disc on engaged record'),('Danger','Alert','Overload, clipping'),('DangerBright','Alert Light','Overload readout'),('Warning','Caution','Meter −6…0 dB'),('Success','Signal','Working zone, live signal'),('SuccessDim','Signal Dim','Input, metronome')]
chromas=[('Accent','1 · Brass','Primary: left, mid, main'),('ChromaTeal','2 · Teal','Lows, early, input, modulation'),('ChromaRose','3 · Rose','Right, highs, tail'),('ChromaSteel','4 · Steel','Rare fourth layer'),('ChromaTealLight','Teal light','Text on teal'),('ChromaRoseLight','Rose light','Text on rose')]
tracks=[(f'Track{i+1}',n,'') for i,n in enumerate(['Drums','Perc','Bass','Keys','Texture','FX','Brass','Vox','Return'])]
def grid(items): return '<div class="swatches">'+''.join(sw(*x) for x in items)+'</div>'

def knob(size, value, arc='var(--accent)', dim=False, label=None, val=None, hot=False):
    C=2*math.pi*21; sweep=C*0.75
    a=math.radians(135+270*value); px=26+14*math.cos(a)*0.62; py=26+14*math.sin(a)*0.62
    arc_col='var(--border-strong)' if dim else arc
    ptr='var(--text-disabled)' if dim else ('var(--chroma-teal-light)' if 'teal' in arc else 'var(--accent-bright)')
    svg=(f'<svg class="knob" width="{size}" height="{size}" viewBox="0 0 52 52" aria-hidden="true">'
         f'<circle cx="26" cy="26" r="21" style="fill:none;stroke:var(--bg-sunken);stroke-width:5;stroke-linecap:round" stroke-dasharray="{sweep:.1f} {C:.1f}" transform="rotate(135 26 26)"/>'
         f'<circle cx="26" cy="26" r="21" style="fill:none;stroke:{arc_col};stroke-width:5;stroke-linecap:round" stroke-dasharray="{sweep*value:.1f} {C:.1f}" transform="rotate(135 26 26)"/>'
         f'<circle cx="26" cy="26" r="14" style="fill:var(--surface-raised);stroke:var(--border-strong);stroke-width:1"/>'
         f'<line x1="26" y1="26" x2="{px:.1f}" y2="{py:.1f}" style="stroke:{ptr};stroke-width:2.4;stroke-linecap:round"/></svg>')
    if label is None: return svg
    cls='cell cell--hot' if hot else ('cell cell--dim' if dim else 'cell')
    return f'<div class="{cls}">{svg}<span class="cell__label">{label}</span><span class="cell__value mono">{val}</span></div>'

T=f'''<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Ember</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Geist:wght@400;500;600;700&family=Geist+Mono:wght@400;500&display=swap">
<style>
/* ============================================================================
   Ember — Nota's design system, documented in its own palette.
   The colour variables below are generated from
   src/managed/Nota.App/Theme/NotaTheme.axaml (Dark → :root, Light → [data-variant=paper]).
   Generated by scripts/design-html.py — edit that, then rerun it. Every specimen on the page is painted
   through these variables — nothing uses a raw hex.
   ========================================================================= */
:root {{
{root}
  --r-bar: {geo['Radius.Bar']}px; --r-clip: {geo['Radius.Clip']}px; --r-badge: {geo['Radius.Badge']}px; --r-control: {geo['Radius.Control']}px;
  --r-tile: {geo['Radius.Tile']}px; --r-panel: {geo['Radius.Panel']}px; --r-body: {geo['Radius.Body']}px;
  --font-ui: "Geist", system-ui, sans-serif;
  --font-mono: "Geist Mono", ui-monospace, Menlo, monospace;
  --sunken: inset 0 1px 0 rgba(0,0,0,.5);
  --raised: inset 0 1px 0 rgba(255,255,255,.03);
  color-scheme: dark;
}}
:root[data-variant="paper"] {{
{paper}
  --sunken: inset 0 1px 0 rgba(0,0,0,.12);
  --raised: inset 0 1px 0 rgba(255,255,255,.4);
  color-scheme: light;
}}
*{{box-sizing:border-box}}
html,body{{margin:0;background:var(--gutter);color:var(--text-primary);font:400 13px/1.55 var(--font-ui);-webkit-font-smoothing:antialiased}}
.mono{{font-family:var(--font-mono);font-variant-numeric:tabular-nums}}
code{{font-family:var(--font-mono);font-size:11px;color:var(--text-secondary)}}
a{{color:var(--accent-hover)}}
.page{{max-width:1120px;margin:0 auto;padding:0 20px 80px}}
header.mast{{position:sticky;top:0;z-index:5;display:flex;align-items:center;gap:16px;padding:14px 20px;margin:0 -20px 12px;background:var(--gutter);border-bottom:1px solid var(--border-default)}}
.mast h1{{font:600 13px/1 var(--font-ui);color:var(--text-heading);margin:0}}
.mast .eyebrow{{margin:0}}
.mast nav{{display:flex;gap:14px;flex-wrap:wrap;margin-left:8px}}
.mast nav a{{color:var(--text-secondary);text-decoration:none;font-size:11px;font-weight:500}}
.mast nav a:hover{{color:var(--text-primary)}}
.spacer{{flex:1}}
.eyebrow{{font:500 9px/1 var(--font-mono);letter-spacing:.18em;text-transform:uppercase;color:var(--accent-dim);margin:0 0 8px}}
section{{background:var(--panel);border:1px solid var(--border-default);border-radius:var(--r-panel);padding:20px;margin:12px 0}}
h2{{font:600 26px/1.1 var(--font-ui);letter-spacing:-.015em;color:var(--text-heading);margin:0 0 8px}}
h3{{font:700 10px/1 var(--font-ui);letter-spacing:.14em;text-transform:uppercase;color:var(--text-tertiary);margin:22px 0 10px}}
p{{color:var(--text-secondary);max-width:72ch;margin:0 0 10px}}
.lead{{color:var(--text-muted)}}
/* segmented */
.seg{{display:inline-flex;gap:2px;padding:3px;height:30px;background:var(--bg-sunken);border:1px solid var(--hairline);border-radius:var(--r-tile);box-shadow:var(--sunken)}}
.seg button{{all:unset;cursor:pointer;height:22px;padding:0 10px;border-radius:var(--r-badge);font:500 11px/22px var(--font-ui);color:var(--text-secondary)}}
.seg button[aria-pressed="true"]{{background:var(--accent);color:var(--text-on-accent)}}
.seg--device button{{font-size:9px;height:18px;line-height:18px;padding:0 7px}}
.seg--device{{height:24px}}
/* rules */
.rules{{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:8px}}
.rule{{background:var(--surface-card);border:1px solid var(--border-default);border-radius:var(--r-tile);padding:12px 14px}}
.rule b{{display:block;color:var(--text-primary);font-weight:600;margin-bottom:4px}}
.rule span{{color:var(--text-secondary)}}
/* swatches */
.swatches{{display:grid;grid-template-columns:repeat(auto-fill,minmax(250px,1fr));gap:8px}}
.sw{{all:unset;cursor:pointer;display:grid;grid-template-columns:36px 1fr auto;grid-template-areas:"chip name hex" "chip key key" "chip use use";column-gap:10px;padding:8px;border-radius:var(--r-tile);background:var(--surface-card);border:1px solid var(--border-default)}}
.sw:hover{{background:var(--surface-hover);border-color:var(--border-strong)}}
.sw__chip{{grid-area:chip;border-radius:var(--r-control);border:1px solid var(--border-strong);min-height:48px}}
.sw__name{{grid-area:name;font-weight:600;color:var(--text-primary)}}
.sw__key{{grid-area:key;font-size:10px;color:var(--text-muted)}}
.sw__hex{{grid-area:hex;font-size:10px;color:var(--text-secondary)}}
.sw__use{{grid-area:use;font-size:11px;color:var(--text-muted)}}
.sw.copied .sw__hex{{color:var(--accent-hover)}}
/* tables */
.tbl{{overflow-x:auto}}
table{{border-collapse:collapse;width:100%;font-size:12px}}
th{{text-align:left;font:700 9px/1 var(--font-ui);letter-spacing:.12em;text-transform:uppercase;color:var(--text-tertiary);padding:8px 10px;border-bottom:1px solid var(--border-default)}}
td{{padding:7px 10px;border-bottom:1px solid var(--hairline);color:var(--text-secondary);vertical-align:middle}}
td:first-child{{color:var(--text-primary)}}
/* type */
.spec{{display:grid;grid-template-columns:200px 1fr;gap:6px 16px;align-items:baseline}}
.spec .k{{font:400 10px/1.4 var(--font-mono);color:var(--text-muted)}}
/* geometry */
.radii{{display:flex;flex-wrap:wrap;gap:14px}}
.radius{{display:flex;flex-direction:column;align-items:center;gap:6px;font-size:11px;color:var(--text-secondary)}}
.radius i{{display:block;width:56px;height:40px;background:var(--surface-raised);border:1px solid var(--border-strong)}}
.depth{{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px}}
.depth div{{height:84px;border-radius:var(--r-panel);padding:10px;font-size:11px;color:var(--text-secondary)}}
/* controls */
.row{{display:flex;flex-wrap:wrap;align-items:center;gap:16px;margin:6px 0}}
.demo{{background:var(--surface-card);border:1px solid var(--border-default);border-radius:var(--r-body);padding:16px}}
.knob{{display:block}}
.knobs{{display:flex;gap:22px;align-items:flex-end}}
.knobs figure{{margin:0;display:flex;flex-direction:column;align-items:center;gap:6px;font-size:10px;color:var(--text-muted)}}
.cell{{display:flex;flex-direction:column;align-items:center;width:53px}}
.cell__label{{font:700 7px/9px var(--font-ui);letter-spacing:.08em;text-transform:uppercase;color:var(--text-tertiary)}}
.cell__value{{font-size:7px;line-height:10px;color:var(--text-secondary)}}
.cell--hot .cell__label,.cell--hot .cell__value{{color:var(--accent-bright)}}
.cell--dim .cell__label,.cell--dim .cell__value{{color:var(--text-disabled)}}
.switch{{display:inline-flex;align-items:center;gap:6px;font:700 7px/1 var(--font-ui);letter-spacing:.08em;text-transform:uppercase;color:var(--text-tertiary)}}
.switch i{{position:relative;width:18px;height:10px;border-radius:5px;background:var(--track-off)}}
.switch i::after{{content:"";position:absolute;top:1.5px;left:1.5px;width:7px;height:7px;border-radius:50%;background:var(--text-tertiary)}}
.switch.on i{{background:var(--accent)}}
.switch.on i::after{{left:9.5px;background:var(--panel)}}
.switch.on{{color:var(--text-secondary)}}
.chip{{display:inline-flex;align-items:center;gap:6px;height:20px;padding:0 9px;border-radius:10px;border:1px solid var(--border-default);background:var(--surface-raised);font:500 11px/1 var(--font-ui);color:var(--text-strong)}}
.chip i{{width:6px;height:6px;border-radius:50%}}
.chip.on{{background:var(--accent-subtle);border-color:var(--border-brass);color:var(--accent-hover)}}
.slider{{display:grid;grid-template-columns:70px 1fr 42px;align-items:center;gap:8px;width:280px;height:18px}}
.slider span:first-child{{font:700 8px/1 var(--font-ui);letter-spacing:.1em;text-transform:uppercase;color:var(--text-tertiary)}}
.slider span:last-child{{font:400 9px/1 var(--font-mono);color:var(--text-primary);text-align:right}}
.track{{position:relative;height:3px;border-radius:var(--r-clip);background:var(--bg-sunken)}}
.track b{{position:absolute;left:0;top:0;bottom:0;border-radius:var(--r-clip);background:var(--accent)}}
.track u{{position:absolute;top:-2px;width:6px;height:7px;border-radius:var(--r-clip);background:var(--accent-bright);margin-left:-3px}}
.slider.dim .track b{{background:var(--border-strong)}} .slider.dim .track u{{background:var(--text-tertiary)}} .slider.dim span:first-child{{color:var(--text-disabled)}}
.btn{{display:inline-flex;align-items:center;height:26px;padding:0 12px;border-radius:var(--r-control);border:1px solid var(--border-default);background:var(--surface-raised);box-shadow:var(--raised);font:500 11px/1 var(--font-ui);color:var(--text-strong)}}
.btn.hover{{background:var(--surface-hover);border-color:var(--border-strong);color:var(--text-primary)}}
.btn.pressed{{background:var(--bg-sunken);box-shadow:none;color:var(--accent-deep)}}
.btn.on{{background:var(--accent-subtle);border-color:var(--border-brass);color:var(--accent-hover)}}
.btn.primary{{background:var(--accent);border-color:var(--accent);color:var(--text-on-accent);box-shadow:none}}
.btn.off{{background:var(--panel);border-color:var(--hairline);color:var(--text-disabled);box-shadow:none}}
.field{{display:inline-flex;align-items:center;gap:8px;height:26px;width:220px;padding:0 9px;border-radius:var(--r-tile);border:1px solid var(--hairline);background:var(--bg-sunken);box-shadow:var(--sunken);font-size:11px;color:var(--text-disabled)}}
.field.focus{{border-color:var(--border-brass);color:var(--text-primary)}}
.btn small,.states small{{display:block}}
.states{{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:10px}}
.states figure{{margin:0;display:flex;flex-direction:column;gap:6px;font-size:10px;color:var(--text-muted)}}
.states figure .btn{{align-self:flex-start}}
.transport+.note{{margin-top:8px}}
/* transport */
.transport{{display:flex;align-items:center;gap:12px;height:42px;padding:0 10px;background:var(--surface-abyss);border:1px solid var(--border-default);border-radius:var(--r-panel);box-shadow:var(--sunken);overflow-x:auto}}
.tp{{flex:none;display:grid;place-items:center;width:34px;height:34px;border-radius:var(--r-tile);background:var(--surface-raised);border:1px solid var(--border-default);box-shadow:var(--raised)}}
.tp.play{{width:46px;background:var(--accent);border-color:var(--accent)}}
.tp .stop{{width:10px;height:10px;border-radius:1px;background:var(--text-strong)}}
.tp .play-tri{{width:0;height:0;border-left:11px solid var(--text-on-accent);border-top:7px solid transparent;border-bottom:7px solid transparent;margin-left:2px}}
.tp .rec{{width:11px;height:11px;border-radius:50%;background:var(--record)}}
.tp.rec-on{{background:var(--record);border-color:var(--record)}} .tp.rec-on .rec{{background:var(--record-ink)}}
.readout{{flex:none;display:flex;flex-direction:column;align-items:center;gap:2px}}
.readout b{{font:500 13px/1 var(--font-mono);color:var(--accent-bright)}}
.readout b.n{{color:var(--text-primary)}}
.readout span{{font:700 7px/1 var(--font-ui);letter-spacing:.12em;color:var(--text-tertiary)}}
.vr{{flex:none;width:1px;height:24px;background:var(--border-default)}}
/* graph window */
.graph{{background:var(--bg-sunken);border:1px solid var(--hairline);border-radius:var(--r-control);position:relative}}
.graph .axis{{position:absolute;font:400 7px/1 var(--font-mono);color:var(--text-axis)}}
.graph .title{{position:absolute;left:6px;top:5px;font:700 8px/1 var(--font-ui);letter-spacing:.08em;color:var(--text-tertiary)}}
.meters{{display:flex;gap:5px;align-items:flex-end;height:120px}}
.meter{{position:relative;width:11px;height:120px;border-radius:var(--r-clip);background:var(--bg-sunken);overflow:hidden}}
.meter i{{position:absolute;left:0;right:0;bottom:0}}
/* card */
.card{{width:700px;height:260px;background:var(--bg-app);border:1px solid var(--border-default);border-radius:var(--r-body);overflow:hidden;display:grid;grid-template-rows:22px 1fr}}
.card__head{{display:flex;align-items:center;gap:8px;padding:0 8px;border-bottom:1px solid var(--border-default);background:var(--panel)}}
.card__name{{font:600 12px/1 var(--font-ui);color:var(--text-primary)}}
.card__badge{{margin-left:auto;font:500 9px/1 var(--font-mono);letter-spacing:.12em;color:var(--text-tertiary)}}
.card__body{{display:grid;grid-template-columns:150px 1fr 150px;gap:5px;padding:6px}}
.sect{{background:var(--surface-card);border:1px solid var(--border-default);border-radius:var(--r-panel);padding:6px 8px;display:flex;flex-direction:column;gap:6px;min-width:0}}
.sect h5{{margin:0;font:700 9px/1 var(--font-ui);letter-spacing:.12em;text-transform:uppercase;color:var(--text-tertiary)}}
.anat{{position:relative;display:inline-block}}
.scroll{{overflow-x:auto;padding-bottom:4px}}
.note{{font-size:11px;color:var(--text-muted)}}
/* shell */
.shell{{display:grid;grid-template-rows:24px 30px 1fr 14px;gap:0;height:260px;border:1px solid var(--border-default);border-radius:var(--r-body);overflow:hidden;background:var(--gutter);font:700 9px/1 var(--font-ui);letter-spacing:.12em;color:var(--text-tertiary)}}
.shell>div{{display:flex;align-items:center;padding:0 10px}}
.shell .h{{background:var(--bg-sunken);justify-content:center;color:var(--text-heading);letter-spacing:0;font:600 11px/1 var(--font-ui);border-bottom:1px solid var(--border-default)}}
.shell .t{{background:var(--surface-abyss);border-bottom:1px solid var(--border-default)}}
.shell .b{{display:grid;grid-template-columns:180px 1fr;gap:12px;padding:12px;align-items:stretch}}
.shell .b div{{border-radius:var(--r-panel);border:1px solid var(--border-default);display:grid;place-items:center}}
.shell .b div:first-child{{background:var(--panel)}} .shell .b div:last-child{{background:var(--bg-sunken)}}
.shell .s{{background:var(--bg-sunken);border-top:1px solid var(--border-default)}}
ul.plain{{margin:0;padding-left:18px;color:var(--text-secondary)}} ul.plain li{{margin:3px 0}}
@media (max-width:760px){{.spec{{grid-template-columns:1fr}} .mast nav{{display:none}}}}
</style>
</head>
<body>
<div class="page">
<header class="mast">
  <div><p class="eyebrow">Nota · design</p><h1>Ember</h1></div>
  <nav><a href="#rules">Rules</a><a href="#colour">Colour</a><a href="#type">Type</a><a href="#geometry">Geometry</a><a href="#controls">Controls</a><a href="#states">States</a><a href="#graphs">Graphs</a><a href="#cards">Cards</a><a href="#shell">Shell</a></nav>
  <span class="spacer"></span>
  <div class="seg" role="group" aria-label="Variant"><button data-v="graphite" aria-pressed="true">Graphite</button><button data-v="paper" aria-pressed="false">Paper</button></div>
</header>

<section id="rules">
  <p class="eyebrow">01 · Foundation</p>
  <h2>Rules</h2>
  <p class="lead">The Nota Design Almanac, as the app implements it. <a href="DESIGN.md">DESIGN.md</a> is the text version with every value, the tests that enforce it and the accepted departures.</p>
  <div class="rules">
    <div class="rule"><b>One accent</b><span>Brass means “active, selected, changed”. There is no second accent.</span></div>
    <div class="rule"><b>Semantics only where they apply</b><span>Red is recording and overload. Green and yellow are meter zones and live signal.</span></div>
    <div class="rule"><b>Chromas live inside graphs</b><span>Teal, rose and steel separate sources in a visualisation — never on buttons, never as state.</span></div>
    <div class="rule"><b>Flat material</b><span>No gradients, glow or texture. Depth is lightness plus a hairline, three levels at most.</span></div>
    <div class="rule"><b>Two faces, one split</b><span>Geist for names and words, Geist Mono for measurements. Nothing under 7 px.</span></div>
    <div class="rule"><b>One control, many sizes</b><span>Each control has one implementation; its value is always visible next to it.</span></div>
    <div class="rule"><b>State changes colour only</b><span>Never size, border width or position. Nothing moves on hover.</span></div>
    <div class="rule"><b>Real time is not animated</b><span>Meters, playhead and spectra jump to the value.</span></div>
    <div class="rule"><b>Muted is an Ink step</b><span>Disabled and quiet text take the next Ink step. Opacity is not a state.</span></div>
    <div class="rule"><b>Numbers are set</b><span class="mono">−3.3 dB · 72 % · 3.2 k · 120.00</span></div>
  </div>
</section>

<section id="colour">
  <p class="eyebrow">02 · Colour</p>
  <h2>Palette</h2>
  <p>Click a swatch to copy its hex for the variant selected above. Values come straight from <code>NotaTheme.axaml</code>.</p>
  <h3>Surfaces · far to near</h3>{grid(surfaces)}
  <h3>Lines</h3>{grid(lines)}
  <h3>Ink · eight steps</h3>{grid(inks)}
  <h3>Brass · the only accent</h3>{grid(brass)}
  <h3>Semantics</h3>{grid(sem)}
  <h3>Role chromas · inside graphs only, in this order</h3>{grid(chromas)}
  <h3>Track palette · nine roles</h3>{grid(tracks)}
</section>

<section id="type">
  <p class="eyebrow">03 · Type</p>
  <h2>Geist and Geist Mono</h2>
  <p>A proportional face for names and words; mono for anything that is a measurement, so digits keep their width as a knob turns.</p>
  <h3>Shell scale</h3>
  <div class="spec">
    <span class="k">Title · 600 · 26</span><span style="font:600 26px/1.1 var(--font-ui);letter-spacing:-.015em;color:var(--text-heading)">Controls</span>
    <span class="k">Heading · 600 · 13</span><span style="font:600 13px var(--font-ui);color:var(--text-heading)">Northern Reverb Study</span>
    <span class="k">Name · 500 · 12</span><span style="font:500 12px var(--font-ui)">Stone Vault · Long Tail</span>
    <span class="k">default · 500 · 11</span><span style="font:500 11px var(--font-ui);color:var(--text-strong)">Arrangement · Session · Modular</span>
    <span class="k">Caption · 400 · 11 · Ink 4</span><span style="font:400 11px/1.65 var(--font-ui);color:var(--text-muted)">Load as an effect on the selected track</span>
    <span class="k">SectionLabel · 700 · 10 caps</span><span style="font:700 10px var(--font-ui);letter-spacing:.14em;color:var(--text-tertiary)">SURFACES</span>
    <span class="k">Readout · mono 500 · 13</span><span class="mono" style="font-weight:500;font-size:13px">12.3.04 · 120.00</span>
    <span class="k">Value · mono 400 · 10</span><span class="mono" style="font-size:10px;color:var(--text-secondary)">−3.3 dB · 48 k · 2.4 MB</span>
    <span class="k">Eyebrow · mono 500 · 9 caps</span><span class="mono" style="font-weight:500;font-size:9px;letter-spacing:.18em;color:var(--accent-dim)">NOTA · CONVOLUTION</span>
  </div>
  <h3>Device scale · inside a 700 × 260 card</h3>
  <div class="spec">
    <span class="k">DeviceName · 600 · 12</span><span style="font:600 12px var(--font-ui)">Nota Chamber</span>
    <span class="k">DeviceSection · 700 · 9 caps</span><span style="font:700 9px var(--font-ui);letter-spacing:.12em;color:var(--text-tertiary)">EARLY REFLECTIONS</span>
    <span class="k">RowLabel · 700 · 8 caps</span><span style="font:700 8px var(--font-ui);letter-spacing:.1em;color:var(--text-tertiary)">PRE-DELAY</span>
    <span class="k">KnobLabel · 700 · 7 caps</span><span style="font:700 7px var(--font-ui);letter-spacing:.08em;color:var(--text-tertiary)">DECAY</span>
    <span class="k">KnobValue · mono · 7</span><span class="mono" style="font-size:7px;color:var(--text-secondary)">4.6 s</span>
    <span class="k">Axis · mono · 7 · Ink 7</span><span class="mono" style="font-size:7px;color:var(--text-axis)">30 Hz — 18k</span>
  </div>
  <h3>Setting numbers</h3>
  <div class="tbl"><table>
    <tr><th>Rule</th><th>Yes</th><th>No</th></tr>
    <tr><td>Thin space before the unit</td><td class="mono">4.6 s · −3.3 dB · 72 %</td><td class="mono">4.6s · -3,3dB</td></tr>
    <tr><td>Typographic minus; plus only where the sign matters</td><td class="mono">−18.0 dB · +0.8 dB</td><td class="mono">-18.0 dB</td></tr>
    <tr><td>Fixed precision</td><td class="mono">dB 1 · tempo 2 · % 0 · 440 Hz · 3.2 k</td><td class="mono">3200 Hz · 72.4 %</td></tr>
    <tr><td>Labels: English caps, one word</td><td class="mono">FEEDBACK · PRE-DELAY · FREQ</td><td class="mono">Fdbk: · Value</td></tr>
  </table></div>
</section>

<section id="geometry">
  <p class="eyebrow">04 · Material</p>
  <h2>Radii, sizes, depth</h2>
  <h3>Radii · by size, not role</h3>
  <div class="radii">
    <div class="radius"><i style="border-radius:var(--r-bar)"></i>1 · bar</div>
    <div class="radius"><i style="border-radius:var(--r-clip)"></i>2 · clip, meter</div>
    <div class="radius"><i style="border-radius:var(--r-badge)"></i>3 · badge</div>
    <div class="radius"><i style="border-radius:var(--r-control)"></i>4 · graph, button</div>
    <div class="radius"><i style="border-radius:var(--r-tile)"></i>5 · tile, search</div>
    <div class="radius"><i style="border-radius:var(--r-panel)"></i>6 · panel, section</div>
    <div class="radius"><i style="border-radius:var(--r-body)"></i>8 · device body</div>
    <div class="radius"><i style="border-radius:20px"></i>pill · chip, switch</div>
  </div>
  <h3>Sizes and steps</h3>
  <div class="tbl"><table>
    <tr><th>Element</th><th>px</th><th>Token</th></tr>
    <tr><td>Device card</td><td class="mono">700 × 260</td><td><code>DeviceCardKit.CardH</code></td></tr>
    <tr><td>Transport strip · buttons · Play</td><td class="mono">42 · 34 · 46</td><td><code>Control.Console / Transport / Play</code></td></tr>
    <tr><td>Shell button, field</td><td class="mono">26</td><td><code>Control.Shell</code></td></tr>
    <tr><td>Tab segment in its container</td><td class="mono">24 in 30</td><td><code>Control.Seg / SegGroup</code></td></tr>
    <tr><td>Filter chip</td><td class="mono">20</td><td><code>Control.Chip</code></td></tr>
    <tr><td>Knob · secondary / regular / main</td><td class="mono">34 / 36 / 44</td><td><code>Control.Knob*</code></td></tr>
    <tr><td>Parameter cell</td><td class="mono">53</td><td><code>Control.ParamCell</code></td></tr>
    <tr><td>Switch · slider track · meter</td><td class="mono">18 × 10 · 3 · 11 / 5</td><td></td></tr>
    <tr><td>Device steps · hair / gap / inset / wide</td><td class="mono">2 · 5 · 6 · 8</td><td><code>Space.Device*</code></td></tr>
    <tr><td>Shell steps · tile / gutter / inset</td><td class="mono">8 · 12 · 20</td><td><code>Space.Tile / Gutter / Inset</code></td></tr>
  </table></div>
  <h3>Three levels of depth</h3>
  <div class="depth">
    <div style="background:var(--bg-sunken);border:1px solid var(--hairline);box-shadow:var(--sunken)">Sunken · Well · Hairline · inner top line</div>
    <div style="background:var(--surface-card);border:1px solid var(--border-default)">Flat · Card · Border · no shadow</div>
    <div style="background:var(--surface-raised);border:1px solid var(--border-default);box-shadow:var(--raised)">Raised · Raised · Border · inner highlight</div>
  </div>
</section>

<section id="controls">
  <p class="eyebrow">05 · Controls</p>
  <h2>One implementation each</h2>
  <p>Vertical drag, up increases, ~140 px for the full range; Shift for fine steps; double-click resets; left button only, so right-click reaches the modulation menu.</p>
  <h3>Knob · 34 / 36 / 44</h3>
  <div class="demo knobs">
    <figure>{knob(34,.55)}secondary</figure>
    <figure>{knob(36,.4)}regular</figure>
    <figure>{knob(44,.7)}main</figure>
    <figure>{knob(36,.62,'var(--chroma-teal)')}modulated</figure>
    <figure>{knob(36,.4,dim=True)}inactive</figure>
  </div>
  <h3>Parameter cell · 53 tall</h3>
  <div class="demo row" style="gap:6px">
    {knob(34,.6,label='DECAY',val='4.6 s',hot=True)}
    {knob(34,.35,label='SIZE',val='31 m')}
    {knob(34,.42,label='MIX',val='42 %')}
    {knob(34,.3,'var(--chroma-teal)',label='MOD',val='18 %')}
    {knob(34,.0,dim=True,label='WIDTH',val='off')}
  </div>
  <p class="note">Brass Light label and value: the parameter differs from its default. A modulated arc takes its source's chroma; the label stays neutral.</p>
  <h3>Switch · segments · chips</h3>
  <div class="demo">
    <div class="row"><span class="switch on"><i></i>True stereo</span><span class="switch"><i></i>Reverse IR</span>
      <div class="seg seg--device"><button aria-pressed="false">Eco</button><button aria-pressed="true">Mid</button><button aria-pressed="false">High</button></div>
      <div class="seg"><button aria-pressed="true">Arrangement</button><button aria-pressed="false">Session</button><button aria-pressed="false">Modular</button></div></div>
    <div class="row"><span class="chip on">Favorites</span><span class="chip"><i style="background:var(--danger)"></i>bass</span><span class="chip"><i style="background:var(--track4)"></i>analog</span><span class="chip"><i style="background:var(--track2)"></i>clean</span><span class="chip mono">+2</span></div>
  </div>
  <h3>Slider row</h3>
  <div class="demo">
    <div class="slider"><span>Pre-delay</span><span class="track"><b style="width:30%"></b><u style="left:30%"></u></span><span>24 ms</span></div>
    <div class="slider"><span>Diffusion</span><span class="track"><b style="width:72%"></b><u style="left:72%"></u></span><span>72 %</span></div>
    <div class="slider dim"><span>Damping</span><span class="track"><b style="width:55%"></b><u style="left:55%"></u></span><span>4.1 k</span></div>
  </div>
  <h3>Transport · shapes, not an icon font</h3>
  <div class="transport">
    <div class="seg"><button aria-pressed="true">Arrangement</button><button aria-pressed="false">Session</button><button aria-pressed="false">Modular</button></div>
    <span class="tp"><i class="stop"></i></span><span class="tp play"><i class="play-tri"></i></span><span class="tp"><i class="rec"></i></span><span class="tp rec-on"><i class="rec"></i></span>
    <span class="vr"></span>
    <span class="readout"><b>12.3.04</b><span>BARS</span></span>
    <span class="vr"></span>
    <span class="readout"><b class="n">120.00</b><span>BPM</span></span><span class="readout"><b class="n">4/4</b><span>SIG</span></span><span class="readout"><b class="n">1/4</b><span>GRID</span></span>
  </div>
  <p class="note">Play is 46 wide against 34 — found by hand, not by eye. Record at rest is neutral with a red disc; engaged it is solid Record with a pale disc.</p>
</section>

<section id="states">
  <p class="eyebrow">06 · States</p>
  <h2>Colour changes, nothing moves</h2>
  <div class="demo states">
    <figure><span class="btn">Rest</span>Raised · Border · Ink 2</figure>
    <figure><span class="btn hover">Hover</span>Hover · Border strong · Ink 1</figure>
    <figure><span class="btn pressed">Pressed</span>Well · Brass Deep</figure>
    <figure><span class="btn on">Engaged</span>Brass Wash · Border brass</figure>
    <figure><span class="btn primary">Export</span>One solid action per context</figure>
    <figure><span class="btn off">Disabled</span>Panel · Hairline · Ink 6</figure>
  </div>
  <div class="row" style="margin-top:12px"><span class="field">Search</span><span class="field focus">Stone Vault</span></div>
  <p class="note">Hover and colour 120 ms ease-out. Empty state: one Ink 5 line, centred, no illustration, no call-to-action button.</p>
</section>

<section id="graphs">
  <p class="eyebrow">07 · Visualisations</p>
  <h2>One graph window</h2>
  <p>Well ground, hairline frame, radius 4, grid lines, mono axis labels in the corners only. Primary curve 1.8 px brass, others 1.2–1.6 px in their chroma. No fills under curves.</p>
  <div class="row" style="align-items:stretch">
    <div class="graph" style="width:420px;height:150px">
      <span class="title">RESPONSE</span>
      <svg width="420" height="150" viewBox="0 0 420 150" aria-hidden="true">
        <g style="stroke:var(--grid-beat);stroke-width:1">{''.join(f'<line x1="{x}" y1="0" x2="{x}" y2="150"/>' for x in (70,140,210,280,350))}<line x1="0" y1="75" x2="420" y2="75"/></g>
        <path d="M0 75 C60 75 80 50 120 48 S170 75 210 75 S260 110 300 108 S360 75 420 75" style="fill:none;stroke:var(--chroma-teal);stroke-width:1.4"/>
        <path d="M0 75 C50 75 90 40 130 38 S190 75 230 80 S300 100 330 96 S390 75 420 75" style="fill:none;stroke:var(--accent);stroke-width:1.8;stroke-linecap:round"/>
        <circle cx="130" cy="38" r="4.5" style="fill:var(--accent);stroke:var(--bg-sunken);stroke-width:2"/>
        <circle cx="330" cy="96" r="4.5" style="fill:var(--text-secondary);stroke:var(--bg-sunken);stroke-width:2"/>
      </svg>
      <span class="axis" style="left:4px;bottom:4px">20</span><span class="axis" style="right:4px;bottom:4px">20k Hz</span>
    </div>
    <div class="graph" style="padding:12px 16px;display:flex;gap:18px;align-items:flex-end">
      <div class="meters">
        <div class="meter"><i style="height:62%;background:var(--success)"></i></div>
        <div class="meter"><i style="height:62%;background:var(--success)"></i><i style="bottom:62%;height:16%;background:var(--warning)"></i></div>
      </div>
      <div class="mono" style="font-size:9px;color:var(--text-secondary);line-height:1.8">L −6.4 dB<br>R <span style="color:var(--danger-bright)">+0.8 dB</span></div>
    </div>
  </div>
  <p class="note">Meters: 11 wide, 5 apart, zones Signal → −6 dB Caution → 0 Alert. Peak hold stays until clicked. Timeline clips: flat, radius 2, no outline; playhead 1 px brass with a 7 × 5 flag.</p>
</section>

<section id="cards">
  <p class="eyebrow">08 · Device cards</p>
  <h2>700 × 260, at true size</h2>
  <p>Header 22: name left; processing-type badge and bypass right — nothing else (presets, A/B, move and delete are in the right-click menu). Body inset 6, sections radius 6 with gap 5. Three columns: choice → work → output.</p>
  <div class="scroll"><div class="card">
    <div class="card__head"><span class="card__name">Nota Chamber</span><span class="card__badge">CONVOLUTION</span><span class="switch on" style="margin-left:6px"><i></i></span></div>
    <div class="card__body">
      <div class="sect"><h5>Choice</h5><div class="field" style="width:100%;height:22px;font-size:10px;color:var(--text-primary)">Concert Hall</div>
        <div class="seg seg--device" style="align-self:flex-start"><button aria-pressed="true">IR</button><button aria-pressed="false">Algo</button></div></div>
      <div class="sect"><h5>Work</h5>
        <div class="graph" style="height:96px"><span class="axis" style="left:4px;bottom:4px">0</span><span class="axis" style="right:4px;bottom:4px">2.6 s</span>
          <svg width="100%" height="96" viewBox="0 0 360 96" preserveAspectRatio="none" aria-hidden="true"><path d="M0 20 C40 22 80 40 140 58 S260 84 360 88" style="fill:none;stroke:var(--accent);stroke-width:1.8"/><path d="M0 34 C40 36 90 52 150 66 S260 86 360 90" style="fill:none;stroke:var(--chroma-teal);stroke-width:1.4"/></svg></div>
        <div class="row" style="gap:4px;margin:0;justify-content:space-around">{knob(34,.3,label='PRE-DELAY',val='20.0 ms')}{knob(34,.62,label='SIZE',val='100 %',hot=True)}{knob(34,.2,label='ATTACK',val='0.0 ms')}{knob(34,.55,label='DECAY',val='2.60 s')}</div></div>
      <div class="sect"><h5>Output</h5>
        <div class="slider" style="width:100%;grid-template-columns:34px 1fr 36px"><span>Conv</span><span class="track"><b style="width:60%"></b><u style="left:60%"></u></span><span>−3.0</span></div>
        <div class="slider" style="width:100%;grid-template-columns:34px 1fr 36px"><span>Dry</span><span class="track"><b style="width:80%"></b><u style="left:80%"></u></span><span>0.0</span></div>
        <div class="slider" style="width:100%;grid-template-columns:34px 1fr 36px"><span>Wet</span><span class="track"><b style="width:45%"></b><u style="left:45%"></u></span><span>45 %</span></div></div>
    </div>
  </div></div>
  <p class="note">Width exceptions by decision: Rhythm 900, Flux 900, Bass 1060, Physical 720, and the plugin / parameter-list stubs.</p>
</section>

<section id="shell">
  <p class="eyebrow">09 · Shell</p>
  <h2>Header, transport, panels on the gutter</h2>
  <div class="shell">
    <div class="h">Northern Reverb Study</div>
    <div class="t">VIEW · STOP PLAY REC · POSITION · LOOP · BPM SIG GRID · SWITCHES · + TRACK</div>
    <div class="b"><div>BROWSER</div><div>CANVAS · ALWAYS SUNKEN</div></div>
    <div class="s">STATUS</div>
  </div>
  <p class="note" style="margin-top:10px">Header 36 · transport 42 under it (a departure from the almanac, which puts it at the bottom) · panels float on Gutter with 12 px gaps · status 22. List rows 26, one line: dot, name, mono metadata. Arrangement tracks 64, groups 26, header column 228.</p>
  <h3>Accepted departures</h3>
  <ul class="plain">
    <li>Transport at the top, with the view switch leading it.</li>
    <li>English shell copy — tooltips are short and verb-first.</li>
    <li>Frequent choices with more than four options stay segments.</li>
    <li>Rhythm, Flux, Bass and Physical cards are wider than 700.</li>
    <li>Default zoom 28 px per beat; <code>FREQ</code>, <code>RESO</code>, <code>THRESH</code> as labels; Play turns green in Session.</li>
  </ul>
</section>
</div>
<script>
(() => {{
  const root = document.documentElement;
  const buttons = document.querySelectorAll('.mast .seg button');
  function hexes() {{
    const paper = root.dataset.variant === 'paper';
    document.querySelectorAll('.sw').forEach(s => {{ s.querySelector('.sw__hex').textContent = paper ? s.dataset.light : s.dataset.dark; }});
  }}
  function set(v) {{
    if (v === 'paper') root.dataset.variant = 'paper'; else delete root.dataset.variant;
    buttons.forEach(b => b.setAttribute('aria-pressed', String(b.dataset.v === v)));
    hexes();
    try {{ localStorage.setItem('ember-variant', v); }} catch (e) {{}}
  }}
  buttons.forEach(b => b.addEventListener('click', () => set(b.dataset.v)));
  let saved = null; try {{ saved = localStorage.getItem('ember-variant'); }} catch (e) {{}}
  const asked = new URLSearchParams(location.search).get('variant');   // ?variant=paper
  set((asked || saved) === 'paper' ? 'paper' : 'graphite');
  document.querySelectorAll('.sw').forEach(s => s.addEventListener('click', () => {{
    const hex = s.querySelector('.sw__hex').textContent;
    const done = () => {{ s.classList.add('copied'); setTimeout(() => s.classList.remove('copied'), 900); }};
    if (navigator.clipboard) navigator.clipboard.writeText(hex).then(done, () => {{}});
  }}));
}})();
</script>
</body>
</html>
'''
open(ROOT+'/DESIGN.html','w',encoding='utf-8').write(T)
print(len(T.splitlines()))
