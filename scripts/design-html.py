#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
#
# Regenerates DESIGN.html. Colour comes from the theme files
# (src/managed/Nota.App/Theme/Themes/*.json, in NotaThemes.BuiltInOrder), mapped onto the
# Brush.* keys the app publishes (NotaPalette.KeyMap); the shadows are built from theme
# colours exactly as NotaThemeService builds them; geometry and type classes come from
# src/managed/Nota.App/Theme/NotaTheme.axaml. So the page can never show a value the app
# does not use. Run after changing a token:  python3 scripts/design-html.py
import re, math, os, sys, json
ROOT=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
THEME_DIR=ROOT+'/src/managed/Nota.App/Theme'
t=open(THEME_DIR+'/NotaTheme.axaml',encoding='utf-8').read()
pal=open(THEME_DIR+'/NotaPalette.cs',encoding='utf-8').read()
order_src=re.search(r'BuiltInOrder\s*=\s*\{([^}]*)\}',open(THEME_DIR+'/NotaColourTheme.cs',encoding='utf-8').read()).group(1)
THEMES=[json.load(open(f'{THEME_DIR}/Themes/{i}.json',encoding='utf-8')) for i in re.findall(r'"([a-z0-9-]+)"',order_src)]
KEYMAP=re.findall(r'\["Brush\.(\w+)"\]\s*=\s*(Wash\([^)]*\)|[\w\[\]]+)',
                  re.search(r'KeyMap\s*=\s*new\([^)]*\)\s*\{(.*?)\n    \};',pal,re.S).group(1))

def brushes(c):
    """Brush key (without 'Brush.') -> hex for one theme's colours, through KeyMap."""
    out={}
    for key,expr in KEYMAP:
        m=re.match(r'TrackSlots\[(\d)\]',expr)
        if m: out[key]=c[f'Track{int(m.group(1))+1}']; continue
        m=re.match(r'ReturnSlots\[(\d)\]',expr)
        if m: out[key]=c['Return'+'AB'[int(m.group(1))]]; continue
        m=re.match(r'Wash\((\w+),\s*0x([0-9A-Fa-f]{2})\)',expr)
        if m: out[key]='#'+m.group(2).upper()+c[m.group(1)].lstrip('#')[-6:]; continue
        out[key]=c[expr]
    return out
def shadows(c):
    """NotaThemeService.ApplyResources, as Avalonia BoxShadows strings."""
    return {'Raised':f'inset 0 1 0 0 {c["ShadowBevel"]}','Sunken':f'inset 0 0 0 1 {c["Rule"]}',
            'Popover':f'0 0 0 1 {c["Seam"]}, 0 10 28 0 {c["ShadowPopover"]}','Modal':f'0 0 0 1 {c["Seam"]}, 0 24 64 0 {c["ShadowModal"]}'}
BY_ID={th['id']:th for th in THEMES}
D=brushes(BY_ID['studio-dark']['colors']); L=brushes(BY_ID['studio-light']['colors'])
SD=shadows(BY_ID['studio-dark']['colors']); SL=shadows(BY_ID['studio-light']['colors'])
# The page's variant names: Studio keeps "dark" (the default) and "light"; the others use their id.
def vname(th): return {'studio-dark':'dark','studio-light':'light'}.get(th['id'],th['id'])

def css(h):
    h=h.lstrip('#')
    if len(h)==8: a=int(h[:2],16)/255; r,g,b=(int(h[i:i+2],16) for i in (2,4,6)); return f'rgba({r},{g},{b},{a:.3f})'
    return '#'+h.upper()
def css_shadow(s):
    # Avalonia "inset 0 1 0 0 #12FFFFFF, 0 10 28 0 #6B000000" -> CSS box-shadow
    out=[]
    for part in s.split(','):
        toks=[]
        for tok in part.split():
            if tok=='inset': toks.append(tok)
            elif tok.startswith('#'): toks.append(css(tok))
            else: toks.append('0' if float(tok)==0 else tok+'px')
        out.append(' '.join(toks))
    return ', '.join(out)
def var(k): return '--'+re.sub(r'(?<!^)(?=[A-Z])','-',k).lower().replace('.','-')

geo={m.group(1):m.group(2) for m in re.finditer(r'x:Key="((?:Radius|Control|Space)\.\w+)">([0-9.]+)<',t)}

# Text classes: <Style Selector="TextBlock.Name"> … <Setter Property="FontSize" Value="12"/> …
WEIGHT={'Normal':400,'Medium':500,'SemiBold':600,'Bold':700}
TEXT={}
for m in re.finditer(r'<Style Selector="TextBlock\.(\w+)">(.*?)</Style>',t,re.S):
    body=m.group(2); s={}
    for p,v in re.findall(r'<Setter Property="(\w+)" Value="([^"]+)"/>',body): s[p]=v
    TEXT[m.group(1)]=s

# Every key the page names must exist in both variants — a renamed or dropped token fails
# here with its name, rather than a KeyError deep in the template.
def need(keys, where, dark, light):
    missing=[k for k in keys if k not in dark or k not in light]
    if missing: sys.exit(f'design-html.py: {where} missing from the Studio themes (Dark or Light): {", ".join(missing)}')

# Derived ink on the semantic fills, per theme (NotaPalette.InkOn): computed, never fixed.
INKON=('Mute','Solo','Record')

def sw(key, name, use):
    hexes=json.dumps({vname(th):brushes(th['colors'])[key] for th in THEMES}).replace('"','&quot;')
    return (f'<button class="sw" data-hex="{hexes}" title="Copy hex">'
            f'<span class="sw__chip" style="background:var({var(key)})"></span>'
            f'<span class="sw__name">{name}</span><code class="sw__key">Brush.{key}</code>'
            f'<span class="sw__hex mono"></span><span class="sw__use">{use}</span></button>')

surfaces=[('BgSunken','Well','Graph windows, fields, readouts, the console; pressed keys sink here'),('BgApp','Ground','Window, title bar, arrangement, status line'),('LaneB','Ground, alternate','Alternate arrangement lane'),('Panel','Panel','Transport row, browser, toolbars, device cards, mixer strips'),('SurfaceRaised','Raised','Keys at rest, card header, menus, tooltips'),('SurfaceHover','Raised hover','Hover; the selected row, with an accent tick'),('AccentSubtle','Accent soft','16 % accent over the panel; nothing at rest uses it')]
lines=[('Seam','Seam','Between panels, around controls, between keys'),('Rule','Rule','Within a panel: separators, field edges, graph frame'),('BorderBrass','Accent line','An accent stroke where a fill is too heavy'),('GridBeat','Grid','Beat grid, zero axis'),('GridBar','Grid bar','Bar lines'),('VizGrid','Viz grid','Grid inside a graph window')]
inks=[('TextHeading','Ink 0','Modal and section titles'),('TextPrimary','Primary','Names, values, button text'),('TextStrong','Ink 2','Toggle-key text, transport glyphs'),('TextSecondary','Secondary','Labels, idle icons, metadata'),('TextMuted','Muted','Section labels, units, badges'),('TextTertiary','Ink 5','Row tags, placeholders, empty states'),('TextFaint','Faint','Disabled controls; axis labels in graphs'),('VizInk','Viz ink','A value at rest: arcs, fills, curves'),('TextOnAccent','On accent','Text on the accent')]
accent=[('Accent','Accent','Playing, selected, playhead, loop, toggled on, live'),('AccentBright','Accent glow','The accent as a line or text'),('AccentDeep','Pressed','Pressed accent'),('AccentDim','Accent dim','The soft accent')]
sem=[('Record','Record','Record and arm, solid when on'),('RecordInk','Record ink','Disc on an engaged record key'),('Danger','Error','Overload, clipping, error text'),('DangerBright','Error light','Overload readout'),('Mute','Mute','Mute on — amber'),('Solo','Solo','Solo on — blue'),('Warning','Warning','Meter −6…0 dB'),('Success','Green','Meter safe range, positive status'),('SuccessInk','Green ink','Glyph on a green key (Session play)'),('SuccessDim','Green dim','Input, metronome')]
chromas=[('VizInk','1 · Viz ink','Primary: left, mid, main'),('ChromaTeal','2 · Teal','Lows, early, input, modulation'),('ChromaRose','3 · Rose','Right, highs, tail'),('ChromaSteel','4 · Blue','Rare fourth layer'),('ChromaTealLight','Teal light','Text on teal'),('ChromaRoseLight','Rose light','Text on rose')]
ROLES=['Drums','Perc','Bass','Keys','Texture','FX','Brass','Vox','Return']
tracks=[(f'Track{i+1}',n,'') for i,n in enumerate(ROLES)]+[('ReturnA','Return A','Return bus'),('ReturnB','Return B','Return bus'),('Master','Master','Muted grey: the sum, not a track')]
for group,name in [(surfaces,'surfaces'),(lines,'lines'),(inks,'ink'),(accent,'accent'),(sem,'semantics'),(chromas,'chromas'),(tracks,'track palette')]:
    need([k for k,_,_ in group], f'Brush keys for {name}', D, L)
need(['Raised','Sunken','Popover','Modal'], 'Shadow keys', SD, SL)
need(['Radius.Docked','Radius.Bar','Radius.Clip','Radius.Badge','Radius.Control','Radius.Tile','Radius.Panel','Radius.Body','Radius.Pill','Radius.Modal',
      'Control.TransportBar','Control.Console','Control.Transport','Control.Play','Control.Shell','Control.Seg','Control.SegGroup','Control.Chip',
      'Control.KnobSecondary','Control.KnobRegular','Control.KnobMain','Control.KnobInline','Control.ParamCell',
      'Space.DeviceHair','Space.DeviceGap','Space.DeviceInset','Space.DeviceInsetWide','Space.Tile','Space.Gutter','Space.Inset'], 'geometry keys', geo, geo)
need(['Title','Heading','Name','Caption','SectionLabel','CellLabel','Readout','Value','Eyebrow','DeviceName','DeviceSection','RowLabel','KnobLabel','KnobValue','Axis'], 'TextBlock classes', TEXT, TEXT)
def grid(items): return '<div class="swatches">'+''.join(sw(*x) for x in items)+'</div>'

def tstyle(cls):
    """CSS for a TextBlock class, straight from its setters."""
    s=TEXT[cls]
    fam='var(--font-mono)' if 'Font.Mono' in s.get('FontFamily','') else 'var(--font-ui)'
    w=WEIGHT.get(s.get('FontWeight','Normal'),400); size=s.get('FontSize','12')
    fg=re.search(r'Brush\.(\w+)',s.get('Foreground',''))
    col=f';color:var({var(fg.group(1))})' if fg else ''
    return f'font:{w} {size}px/1.35 {fam}{col}'
def tspec(cls):
    s=TEXT[cls]; mono='Mono ' if 'Font.Mono' in s.get('FontFamily','') else ''
    return f'{cls} · {mono}{WEIGHT.get(s.get("FontWeight","Normal"),400)} · {s.get("FontSize","12")}'

# Clip colours: band = track colour 30% toward black, content 58%, the name in derived ink —
# the same arithmetic as ArrangementView.ClipBand / ClipInk and NotaPalette.ReadableInkOn.
def rgb(h): h=h.lstrip('#')[-6:]; return tuple(int(h[i:i+2],16) for i in (0,2,4))
def mix(a,b,f): return tuple(int(x+(y-x)*f) for x,y in zip(a,b))
def hexs(c): return '#%02X%02X%02X'%c
def lum(c):
    def ch(v): x=v/255; return x/12.92 if x<=0.03928 else ((x+0.055)/1.055)**2.4
    r,g,b=c; return 0.2126*ch(r)+0.7152*ch(g)+0.0722*ch(b)
def contrast(a,b): la,lb=lum(a),lum(b); return (max(la,lb)+0.05)/(min(la,lb)+0.05)
def ink_on(fill): dark,white=(0x17,0x18,0x1A),(0xFF,0xFF,0xFF); return hexs(dark) if contrast(dark,fill)>=contrast(white,fill) else hexs(white)
SHADE_D,SHADE_L=(0x17,0x18,0x1A),(0x16,0x17,0x1A)   # NotaPalette.ShadeDown
def theme_vars(th):
    """Every CSS variable one theme sets: brushes, shadows, derived ink, clip shades."""
    c=th['colors']; B=brushes(c); shade=rgb(c['ShadeDown'])
    out=[f'  {var(k)}: {css(v)};' for k,v in B.items()]
    out+=[f'  --shadow-{k.lower()}: {css_shadow(v)};' for k,v in shadows(c).items()]
    out+=[f'  --ink-on-{k.lower()}: {ink_on(rgb(B[k]))};' for k in INKON]
    for i in range(9):
        body=rgb(c[f'Track{i+1}']); band=mix(body,shade,0.30)
        out+=[f'  --t{i}-body: {hexs(body)}; --t{i}-band: {hexs(band)}; --t{i}-ink: {ink_on(band)}; --t{i}-cont: {hexs(mix(body,shade,0.58))};']
    out.append(f"  color-scheme: {th['kind']};")
    return '\n'.join(out)
VARIANTS='\n'.join((':root' if vname(th)=='dark' else f':root[data-variant="{vname(th)}"]')+' {\n'+theme_vars(th)+'\n}' for th in THEMES)

def clip(idx, name, w, sel=False, notes=True):
    style=f'--c-body:var(--t{idx}-body);--c-band:var(--t{idx}-band);--c-ink:var(--t{idx}-ink);--c-cont:var(--t{idx}-cont);width:{w}px'
    marks=''.join(f'<i style="left:{8+j*11}px;top:{18+((j*7)%4)*6}px"></i>' for j in range(max(1,(w-12)//11))) if notes else ''
    return f'<div class="clip{" sel" if sel else ""}" style="{style}"><b>{name}</b>{marks}</div>'

def knob(size, value, arc='var(--viz-ink)', dim=False, label=None, val=None, mod=False, live=False):
    C=2*math.pi*21; sweep=C*0.75
    a=math.radians(135+270*value); px=26+14*math.cos(a)*0.62; py=26+14*math.sin(a)*0.62
    arc_col='var(--rule)' if dim else ('var(--accent)' if live else arc)
    ptr='var(--text-disabled)' if dim else 'var(--text-primary)'
    svg=(f'<svg class="knob" width="{size}" height="{size}" viewBox="0 0 52 52" aria-hidden="true">'
         f'<circle cx="26" cy="26" r="21" style="fill:none;stroke:var(--bg-sunken);stroke-width:5;stroke-linecap:round" stroke-dasharray="{sweep:.1f} {C:.1f}" transform="rotate(135 26 26)"/>'
         f'<circle cx="26" cy="26" r="21" style="fill:none;stroke:{arc_col};stroke-width:5;stroke-linecap:round" stroke-dasharray="{sweep*value:.1f} {C:.1f}" transform="rotate(135 26 26)"/>'
         f'<circle cx="26" cy="26" r="14" style="fill:var(--surface-raised);stroke:var(--seam);stroke-width:1"/>'
         f'<line x1="26" y1="26" x2="{px:.1f}" y2="{py:.1f}" style="stroke:{ptr};stroke-width:2.4;stroke-linecap:round"/></svg>')
    if label is None: return svg
    cls='cell cell--mod' if mod else ('cell cell--dim' if dim else 'cell')
    return f'<div class="{cls}">{svg}<span class="cell__label">{label}</span><span class="cell__value mono">{val}</span></div>'

def shadow_rows():
    return ''.join(f'<tr><td><code>Shadow.{k}</code></td><td class="mono">{SD[k]}</td><td class="mono">{SL[k]}</td></tr>' for k in ('Raised','Sunken','Popover','Modal'))

STYLE=r'''
*{box-sizing:border-box}
html,body{margin:0;background:var(--bg-app);color:var(--text-primary);font:400 12px/1.5 var(--font-ui);-webkit-font-smoothing:antialiased}
.mono{font-family:var(--font-mono);font-variant-numeric:tabular-nums}
code{font-family:var(--font-mono);font-size:11px;color:var(--text-secondary)}
a{color:var(--accent-bright)}
.page{max-width:1120px;margin:0 auto;padding:0 20px 80px}
header.mast{position:sticky;top:0;z-index:5;display:flex;align-items:center;gap:16px;height:44px;padding:0 20px;margin:0 -20px 12px;background:var(--panel);border-bottom:1px solid var(--seam)}
.mast h1{font:600 13px/1 var(--font-ui);color:var(--text-heading);margin:0}
.mast .eyebrow{margin:0 0 3px}
.mast nav{display:flex;gap:14px;flex-wrap:wrap;margin-left:8px}
.mast nav a{color:var(--text-secondary);text-decoration:none;font-size:12px;font-weight:500}
.mast nav a:hover{color:var(--text-primary)}
.spacer{flex:1}
.eyebrow{font:400 10px/1 var(--font-mono);color:var(--text-muted);margin:0 0 8px}
section{background:var(--panel);border:1px solid var(--seam);border-radius:var(--r-control);padding:20px;margin:12px 0}
h2{font:600 13px/1.3 var(--font-ui);color:var(--text-heading);margin:0 0 8px}
h3{font:600 11px/1 var(--font-ui);color:var(--text-muted);margin:22px 0 10px}
p{color:var(--text-secondary);max-width:72ch;margin:0 0 10px}
.lead{color:var(--text-secondary)}
/* segmented group: raised keys butted on 1px seams; only the outer corners round */
.seg{display:inline-flex;gap:1px;background:var(--seam);border:1px solid var(--seam);border-radius:var(--r-control);overflow:hidden;vertical-align:middle}
.seg button{all:unset;cursor:pointer;height:24px;padding:0 10px;background:var(--surface-raised);box-shadow:var(--shadow-raised);font:500 12px/24px var(--font-ui);color:var(--text-secondary)}
.seg button:hover{background:var(--surface-hover)}
.seg button[aria-pressed="true"]{background:var(--accent);color:var(--text-on-accent);font-weight:600;box-shadow:none}
/* device segments: a well strip with a rule edge */
.seg--device{gap:0;padding:1px;background:var(--bg-sunken);border-color:var(--rule)}
.seg--device button{height:18px;line-height:18px;padding:0 7px;font-size:9px;background:transparent;box-shadow:none;border-radius:var(--r-badge)}
/* rules */
.rules{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:8px}
.rule{background:var(--surface-raised);border:1px solid var(--seam);border-radius:var(--r-control);padding:10px 12px}
.rule b{display:block;color:var(--text-primary);font-weight:600;margin-bottom:3px}
.rule span{color:var(--text-secondary)}
/* swatches */
.swatches{display:grid;grid-template-columns:repeat(auto-fill,minmax(250px,1fr));gap:8px}
.sw{all:unset;cursor:pointer;display:grid;grid-template-columns:36px 1fr auto;grid-template-areas:"chip name hex" "chip key key" "chip use use";column-gap:10px;padding:8px;border-radius:var(--r-control);background:var(--surface-raised);border:1px solid var(--seam)}
.sw:hover{background:var(--surface-hover)}
.sw__chip{grid-area:chip;border-radius:var(--r-clip);box-shadow:0 0 0 1px var(--rule);min-height:48px}
.sw__name{grid-area:name;font-weight:600;color:var(--text-primary)}
.sw__key{grid-area:key;font-size:10px;color:var(--text-muted)}
.sw__hex{grid-area:hex;font-size:10px;color:var(--text-secondary)}
.sw__use{grid-area:use;font-size:11px;color:var(--text-muted)}
.sw.copied .sw__hex{color:var(--accent-bright)}
/* tables */
.tbl{overflow-x:auto}
table{border-collapse:collapse;width:100%;font-size:12px}
th{text-align:left;font:600 11px/1 var(--font-ui);color:var(--text-muted);padding:8px 10px;border-bottom:1px solid var(--rule)}
td{padding:7px 10px;border-bottom:1px solid var(--rule);color:var(--text-secondary);vertical-align:middle}
td:first-child{color:var(--text-primary)}
/* type */
.spec{display:grid;grid-template-columns:220px 1fr;gap:6px 16px;align-items:baseline}
.spec .k{font:400 10px/1.4 var(--font-mono);color:var(--text-muted)}
/* geometry */
.radii{display:flex;flex-wrap:wrap;gap:14px}
.radius{display:flex;flex-direction:column;align-items:center;gap:6px;font-size:11px;color:var(--text-secondary)}
.radius i{display:block;width:56px;height:40px;background:var(--surface-raised);border:1px solid var(--seam);box-shadow:var(--shadow-raised)}
.depth{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:14px;padding:18px;background:var(--bg-app);border:1px solid var(--seam);border-radius:var(--r-control)}
.depth div{height:84px;padding:10px;font-size:11px;color:var(--text-secondary)}
/* controls */
.row{display:flex;flex-wrap:wrap;align-items:center;gap:16px;margin:6px 0}
.demo{background:var(--panel);border:1px solid var(--seam);border-radius:var(--r-body);padding:16px}
.knob{display:block}
.knobs{display:flex;gap:22px;align-items:flex-end}
.knobs figure{margin:0;display:flex;flex-direction:column;align-items:center;gap:6px;font-size:11px;color:var(--text-muted)}
.cell{display:flex;flex-direction:column;align-items:center;width:53px}
.cell__label{font:500 8px/9px var(--font-ui);color:var(--text-secondary);white-space:nowrap}
.cell__value{font-size:8px;line-height:10px;color:var(--text-primary)}
.cell--mod .cell__label{color:var(--text-primary)}
.cell--dim .cell__label,.cell--dim .cell__value{color:var(--text-disabled)}
.switch{display:inline-flex;align-items:center;gap:5px;font:500 8px/1 var(--font-ui);color:var(--text-secondary)}
.switch i{position:relative;width:18px;height:10px;border-radius:var(--r-clip);background:var(--track-off)}
.switch i::after{content:"";position:absolute;top:1.5px;left:1.5px;width:7px;height:7px;border-radius:1.5px;background:var(--text-tertiary)}
.switch.on i{background:var(--accent)}
.switch.on i::after{left:9.5px;background:var(--panel)}
.switch.on{color:var(--text-primary)}
.chip{display:inline-flex;align-items:center;gap:5px;height:20px;padding:0 9px;border-radius:var(--r-pill);border:1px solid var(--seam);background:var(--surface-raised);box-shadow:var(--shadow-raised);font:400 11px/1 var(--font-ui);color:var(--text-primary)}
.chip i{width:6px;height:6px;border-radius:50%}
.chip.on{background:var(--accent);border-color:var(--accent);color:var(--text-on-accent);box-shadow:none}
.slider{display:grid;grid-template-columns:70px 1fr 42px;align-items:center;gap:8px;width:280px;height:18px}
.slider span:first-child{font:500 9px/1 var(--font-ui);color:var(--text-secondary)}
.slider span:last-child{font:400 9px/1 var(--font-mono);color:var(--text-primary);text-align:right}
.track{position:relative;height:3px;border-radius:var(--r-clip);background:var(--bg-sunken)}
.track b{position:absolute;left:0;top:0;bottom:0;border-radius:var(--r-clip);background:var(--viz-ink)}
.track u{position:absolute;top:-2px;width:6px;height:7px;border-radius:var(--r-clip);background:var(--text-primary);margin-left:-3px}
.slider.live .track b{background:var(--accent)} .slider.live .track u{background:var(--accent-bright)}
.slider.dim .track b{background:var(--rule)} .slider.dim .track u{background:var(--text-tertiary)} .slider.dim span{color:var(--text-disabled)}
.btn{display:inline-flex;align-items:center;height:26px;padding:0 10px;border-radius:var(--r-control);border:1px solid var(--seam);background:var(--surface-raised);box-shadow:var(--shadow-raised);font:500 12px/1 var(--font-ui);color:var(--text-primary)}
.btn.hover{background:var(--surface-hover)}
.btn.pressed{background:var(--bg-sunken);box-shadow:none}
.btn.on{background:var(--accent);border-color:var(--accent);color:var(--text-on-accent);box-shadow:none}
.btn.primary{background:var(--surface-hover);color:var(--text-heading);font-weight:600}
.btn.off{background:var(--panel);color:var(--text-disabled);box-shadow:none}
.btn.ghost{background:transparent;border-color:transparent;box-shadow:none}
.field{display:inline-flex;align-items:center;gap:8px;height:26px;width:220px;padding:0 8px;border-radius:var(--r-control);background:var(--bg-sunken);box-shadow:var(--shadow-sunken);font-size:12px;color:var(--text-tertiary)}
.field.focus{box-shadow:inset 0 0 0 1px var(--accent);color:var(--text-primary)}
.states{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:12px}
.states figure{margin:0;display:flex;flex-direction:column;gap:6px;font-size:11px;color:var(--text-muted)}
.states figure>*:first-child{align-self:flex-start}
.listrow{display:flex;align-items:center;gap:8px;width:150px;height:26px;padding:0 8px;font-size:12px;color:var(--text-secondary);border-radius:var(--r-clip)}
.listrow.sel{background:var(--surface-selected);color:var(--text-primary);box-shadow:inset 2px 0 0 var(--accent)}
.tabs{display:flex;gap:14px;height:26px;align-items:stretch}
.tabs span{display:flex;align-items:center;font:500 12px/1 var(--font-ui);color:var(--text-secondary)}
.tabs span.sel{color:var(--text-primary);box-shadow:inset 0 -2px 0 var(--accent)}
.toggles{display:inline-flex;gap:3px}
.toggles b{display:grid;place-items:center;width:20px;height:18px;border-radius:var(--r-badge);border:1px solid var(--seam);background:var(--surface-raised);box-shadow:var(--shadow-raised);font:600 9px/1 var(--font-ui);color:var(--text-secondary)}
.toggles b.m{background:var(--mute);border-color:var(--mute);box-shadow:none}
.toggles b.s{background:var(--solo);border-color:var(--solo);box-shadow:none}
.toggles b.r{background:var(--record);border-color:var(--record);box-shadow:none}
.toggles .disc{width:7px;height:7px;border-radius:50%;background:var(--record)}
/* menu: a raised popover with a seam ring; hover a full accent fill */
.menu{display:inline-flex;flex-direction:column;width:200px;padding:4px;background:var(--surface-raised);border:1px solid var(--seam);border-radius:var(--r-control)}
.menu span{display:flex;justify-content:space-between;height:24px;align-items:center;padding:0 8px;border-radius:var(--r-clip);font-size:12px;color:var(--text-primary)}
.menu span small{font-size:12px;color:var(--text-muted)}
.menu span.hot{background:var(--accent);color:var(--text-on-accent)} .menu span.hot small{color:var(--text-on-accent)}
.menu hr{border:0;height:1px;background:var(--rule);margin:4px}
.menu span small.k{font-family:var(--font-mono);font-size:11px}
.menu span.danger{color:var(--danger)} .menu span.danger.hot{color:var(--text-on-accent)}
.menu span.head{font-weight:600;color:var(--text-heading)} .menu span.head small{font-family:var(--font-mono);font-size:11px;color:var(--text-tertiary)}
.menu .sw{display:grid;grid-template-columns:repeat(9,16px);gap:2px;padding:4px 8px}.menu .sw i{width:12px;height:12px;margin:2px;border-radius:var(--r-badge)}.menu .sw i.on{outline:1px solid var(--text-primary);outline-offset:1px}
/* transport */
.transport{display:flex;align-items:center;gap:10px;height:44px;padding:0 8px;background:var(--panel);border-bottom:1px solid var(--seam);overflow-x:auto}
.transport .name{flex:none;font:600 13px/1 var(--font-ui);color:var(--text-primary)}
.console{flex:none;display:flex;align-items:center;gap:10px;height:30px;padding:0 8px 0 2px;background:var(--bg-sunken);box-shadow:var(--shadow-sunken);border-radius:var(--r-control)}
.keys{display:flex;gap:1px}
.tp{flex:none;display:grid;place-items:center;width:26px;height:26px;border-radius:var(--r-tile);background:var(--surface-raised);border:1px solid var(--seam);box-shadow:var(--shadow-raised)}
.tp.play{width:36px}
.tp.on{background:var(--accent);border-color:var(--accent);box-shadow:none}
.tp .stop{width:9px;height:9px;border-radius:1px;background:var(--text-strong)}
.tp .play-tri{width:0;height:0;border-left:10px solid var(--text-strong);border-top:6px solid transparent;border-bottom:6px solid transparent;margin-left:2px}
.tp.on .play-tri{border-left-color:var(--text-on-accent)}
.tp .rec{width:10px;height:10px;border-radius:50%;background:var(--record)}
.tp.rec-on{background:var(--record);border-color:var(--record);box-shadow:none} .tp.rec-on .rec{background:var(--record-ink)}
.tp .met{width:10px;height:12px;border:1.5px solid var(--text-strong);border-bottom-width:2px;clip-path:polygon(30% 0,70% 0,100% 100%,0 100%)}
.tp.on .met{border-color:var(--text-on-accent)}
.pos{display:flex;align-items:center;gap:6px}
.pos b{font:400 14px/1 var(--font-mono);letter-spacing:.02em;color:var(--text-primary)}
.unit{font:500 10px/1 var(--font-ui);color:var(--text-muted)}
.well{flex:none;display:inline-flex;align-items:center;gap:4px;height:26px;padding:0 8px;background:var(--bg-sunken);box-shadow:var(--shadow-sunken);border-radius:var(--r-control)}
.well b{font:400 13px/1 var(--font-mono);color:var(--text-primary)}
.loop{display:inline-flex;align-items:center;gap:6px;height:22px;padding:0 8px;border-radius:var(--r-control);background:var(--accent);color:var(--text-on-accent);font:400 11px/1 var(--font-mono)}
.loop .unit{color:var(--text-on-accent)}
.vr{flex:none;width:1px;height:18px;background:var(--rule)}
/* graph window */
.graph{background:var(--bg-sunken);box-shadow:inset 0 0 0 1px var(--rule);border-radius:var(--r-control);position:relative}
.graph .axis{position:absolute;font:400 7.5px/1 var(--font-mono);color:var(--text-axis)}
.graph .title{position:absolute;left:6px;top:5px;font:500 8px/1 var(--font-ui);color:var(--text-muted)}
.meters{display:flex;gap:5px;align-items:flex-end;height:120px}
.meter{position:relative;width:11px;height:120px;border-radius:var(--r-clip);background:var(--bg-sunken);box-shadow:inset 0 0 0 1px var(--rule);overflow:hidden}
.meter i{position:absolute;left:0;right:0;bottom:0}
/* clips */
.lane{display:flex;gap:2px;align-items:center;height:64px;padding:0 6px;background:var(--bg-app);border:1px solid var(--seam);border-radius:var(--r-control);overflow-x:auto;position:relative}
.clip{position:relative;flex:none;height:60px;border-radius:var(--r-clip);background:var(--c-body);overflow:hidden}
.clip b{display:block;height:12px;padding:0 5px;background:var(--c-band);color:var(--c-ink);font:600 9.5px/12px var(--font-ui);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.clip i{position:absolute;width:8px;height:3px;border-radius:1px;background:var(--c-cont)}
.clip.sel b{background:var(--accent);color:var(--text-on-accent)}
.clip.sel{box-shadow:inset 0 0 0 1px var(--accent)}
.playhead{position:absolute;top:0;bottom:0;width:1px;background:var(--accent)}
/* card */
.card{width:700px;height:260px;background:var(--panel);border:1px solid var(--seam);border-radius:var(--r-body);overflow:hidden;display:grid;grid-template-rows:22px 1fr}
.card__head{display:grid;grid-template-columns:1fr auto 1fr;align-items:center;gap:8px;padding:0 8px;border-bottom:1px solid var(--seam);background:var(--surface-raised);box-shadow:var(--shadow-raised)}
.card__name{font:600 12px/1 var(--font-ui);color:var(--text-primary);min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.preset{display:inline-flex;align-items:stretch;height:18px;border-radius:var(--r-control);background:var(--bg-sunken);box-shadow:var(--shadow-sunken);overflow:hidden;font-size:11px}
.preset i{font-style:normal;width:18px;display:grid;place-items:center;color:var(--text-tertiary)}
.preset b{font-weight:500;width:132px;padding:0 7px;border-left:1px solid var(--rule);border-right:1px solid var(--rule);display:flex;align-items:center;color:var(--text-primary)}
.card__badge{font:400 10px/1 var(--font-mono);color:var(--text-muted)}
.card__right{display:flex;align-items:center;justify-content:flex-end;gap:8px}
.card__body{display:grid;grid-template-columns:150px 1fr 150px;gap:5px;padding:6px}
.sect{padding:6px 8px;display:flex;flex-direction:column;gap:6px;min-width:0}
.sect+.sect{border-left:1px solid var(--rule)}
.sect h5{margin:0;font:600 10.5px/1 var(--font-ui);color:var(--text-muted)}
.scroll{overflow-x:auto;padding-bottom:4px}
.note{font-size:11px;color:var(--text-muted)}
/* shell */
.shell{display:grid;grid-template-rows:20px 28px 1fr 22px 70px 16px;gap:1px;height:300px;border:1px solid var(--seam);border-radius:var(--r-control);overflow:hidden;background:var(--seam);font:500 11px/1 var(--font-ui);color:var(--text-muted)}
.shell>div{display:flex;align-items:center;padding:0 10px}
.shell .h{background:var(--bg-app);justify-content:center;color:var(--text-primary);font-weight:600}
.shell .t{background:var(--panel)}
.shell .b{display:grid;grid-template-columns:170px 1fr;gap:1px;padding:0;align-items:stretch;background:var(--seam)}
.shell .b div{display:grid;place-items:center}
.shell .b div:first-child{background:var(--panel)} .shell .b div:last-child{background:var(--bg-app)}
.shell .d{background:var(--panel)}
.shell .db{background:var(--bg-app)}
.shell .s{background:var(--bg-app);font-weight:400;color:var(--text-secondary)}
ul.plain{margin:0;padding-left:18px;color:var(--text-secondary)} ul.plain li{margin:3px 0}
@media (max-width:760px){.spec{grid-template-columns:1fr} .mast nav{display:none}}
'''

HEAD=f'''<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Studio</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Geist:wght@400;500;600;700&family=Geist+Mono:wght@400;500&display=swap">
<style>
/* ============================================================================
   Studio — Nota's design system, documented in its own palette.
   The colour and shadow variables below are generated from the colour themes in
   src/managed/Nota.App/Theme/Themes/ (Studio Dark → :root, every other theme →
   [data-variant=…]).
   Generated by scripts/design-html.py — edit that, then rerun it. Every specimen on the page
   is painted through these variables — nothing uses a raw hex.
   ========================================================================= */
:root {{
  --r-docked: {geo['Radius.Docked']}px; --r-bar: {geo['Radius.Bar']}px; --r-clip: {geo['Radius.Clip']}px; --r-badge: {geo['Radius.Badge']}px;
  --r-control: {geo['Radius.Control']}px; --r-tile: {geo['Radius.Tile']}px; --r-panel: {geo['Radius.Panel']}px; --r-body: {geo['Radius.Body']}px;
  --r-pill: {geo['Radius.Pill']}px; --r-modal: {geo['Radius.Modal']}px;
  --font-ui: "Geist", system-ui, sans-serif;
  --font-mono: "Geist Mono", ui-monospace, Menlo, monospace;
}}
{VARIANTS}
'''

g=lambda k: geo[k]
BODY=f'''</style>
</head>
<body>
<div class="page">
<header class="mast">
  <div><p class="eyebrow">Nota · design</p><h1>Studio</h1></div>
  <nav><a href="#rules">Rules</a><a href="#colour">Colour</a><a href="#type">Type</a><a href="#geometry">Geometry</a><a href="#controls">Controls</a><a href="#states">States</a><a href="#graphs">Graphs</a><a href="#cards">Cards</a><a href="#shell">Shell</a></nav>
  <span class="spacer"></span>
  <div class="seg" role="group" aria-label="Colour theme">{''.join(f'<button data-v="{vname(th)}" aria-pressed="{str(vname(th)=="dark").lower()}">{th["name"]}</button>' for th in THEMES)}</div>
</header>

<section id="rules">
  <p class="eyebrow">01 · Foundation</p>
  <h2>Rules</h2>
  <p class="lead">Nota's design language: the work carries the colour, the interface steps back, and one accent marks what is live or chosen. <a href="DESIGN.md">DESIGN.md</a> is the text version with every value, the tests that enforce it and the accepted exceptions.</p>
  <div class="rules">
    <div class="rule"><b>One accent: live or chosen</b><span>Orange is playing, selected, the playhead, the loop, a toggle that is on, menu hover, the power switch, and a value control while hovered or dragged. Nothing else.</span></div>
    <div class="rule"><b>Resting values are neutral</b><span>Knob arcs, fader fills, curves and waveforms at rest are viz ink. A modulation arc keeps its source chroma.</span></div>
    <div class="rule"><b>Semantics by purpose</b><span>Mute amber, solo blue, arm and record red. Green is positive status and the meter's safe range.</span></div>
    <div class="rule"><b>Derived ink on solid fills</b><span>Text on an accent, mute, solo, arm or track-colour fill takes the ink with the higher contrast.</span></div>
    <div class="rule"><b>Seam between, rule within</b><span>Panels and keys meet on the dark seam; items inside a panel are divided by the lighter rule.</span></div>
    <div class="rule"><b>Flat docked, blur only floating</b><span>Depth is a tone step, a 1px seam and a faint top bevel. Only floating layers cast a blurred shadow.</span></div>
    <div class="rule"><b>2 · 3 · 4 corners</b><span>2 for inner keys, rows, clips; 3 for controls and cards; 4 for modals; 0 for docked panels.</span></div>
    <div class="rule"><b>Two faces, one split</b><span>Geist for words, Geist Mono for every number a user reads. Nothing under 7 px.</span></div>
    <div class="rule"><b>No shouting</b><span>Sentence case, letter-spacing 0. Acronyms and units keep their case: LFO rate, Gain dB.</span></div>
    <div class="rule"><b>State changes colour only</b><span>Never size, border width or position. Nothing moves on hover.</span></div>
    <div class="rule"><b>Real time is not animated</b><span>Meters, playhead and spectra jump to the value.</span></div>
    <div class="rule"><b>Opacity is not a state</b><span>Muted and disabled take the next ink step.</span></div>
    <div class="rule"><b>Numbers are set</b><span class="mono">−3.3 dB · 72 % · 3.2 k · 120.00</span></div>
  </div>
</section>

<section id="colour">
  <p class="eyebrow">02 · Colour</p>
  <h2>Palette</h2>
  <p>Click a swatch to copy its hex for the variant selected above. Values come straight from <code>NotaTheme.axaml</code>.</p>
  <h3>Surfaces · well, ground, panel, raised</h3>{grid(surfaces)}
  <h3>Lines · seam and rule</h3>{grid(lines)}
  <h3>Ink · by importance</h3>{grid(inks)}
  <h3>Accent · the only one</h3>{grid(accent)}
  <h3>Semantics</h3>{grid(sem)}
  <h3>Graph chromas · inside graphs only, in this order</h3>{grid(chromas)}
  <h3>Track palette · nine roles, returns, master</h3>{grid(tracks)}
  <h3>Elevation</h3>
  <div class="tbl" style="margin-top:10px"><table>
    <tr><th>Key</th><th>Dark</th><th>Light</th></tr>
    {shadow_rows()}
  </table></div>
  <p class="note" style="margin-top:8px">Only <code>Shadow.Popover</code> and <code>Shadow.Modal</code> carry a blur. A raised control's seam ring is its own 1px seam border; <code>Shadow.Raised</code> is the top bevel.</p>
</section>

<section id="type">
  <p class="eyebrow">03 · Type</p>
  <h2>Geist and Geist Mono</h2>
  <p>A proportional face for names and words; mono for every number a user reads, so digits keep their width as a knob turns. Nothing in the shell is larger than 13 except the position readout.</p>
  <h3>Shell scale</h3>
  <div class="spec">
    <span class="k">{tspec('Title')}</span><span style="{tstyle('Title')}">Export audio</span>
    <span class="k">{tspec('Heading')}</span><span style="{tstyle('Heading')}">Northern Reverb Study</span>
    <span class="k">{tspec('Name')}</span><span style="{tstyle('Name')}">Stone Vault · Long Tail</span>
    <span class="k">default · 500 · 12</span><span style="font:500 12px var(--font-ui)">Arrangement · Session · Modular</span>
    <span class="k">{tspec('Caption')}</span><span style="{tstyle('Caption')}">Load as an effect on the selected track</span>
    <span class="k">{tspec('SectionLabel')}</span><span style="{tstyle('SectionLabel')}">Recent samples</span>
    <span class="k">{tspec('CellLabel')}</span><span style="{tstyle('CellLabel')}">BPM · Loop · Grid</span>
    <span class="k">{tspec('Readout')}</span><span style="{tstyle('Readout')}">120.00 · 12.3.04</span>
    <span class="k">{tspec('Value')}</span><span style="{tstyle('Value')}">−3.3 dB · 48 k · 2.4 MB</span>
    <span class="k">{tspec('Eyebrow')}</span><span style="{tstyle('Eyebrow')}">Convolution · VST3</span>
  </div>
  <h3>Device scale · inside a 700 × 260 card</h3>
  <div class="spec">
    <span class="k">{tspec('DeviceName')}</span><span style="{tstyle('DeviceName')}">Nota Chamber</span>
    <span class="k">{tspec('DeviceSection')}</span><span style="{tstyle('DeviceSection')}">Early reflections</span>
    <span class="k">{tspec('RowLabel')}</span><span style="{tstyle('RowLabel')}">Pre-delay</span>
    <span class="k">{tspec('KnobLabel')}</span><span style="{tstyle('KnobLabel')}">LFO rate</span>
    <span class="k">{tspec('KnobValue')}</span><span style="{tstyle('KnobValue')}">4.6 s</span>
    <span class="k">{tspec('Axis')}</span><span style="{tstyle('Axis')}">30 Hz — 18k</span>
  </div>
  <h3>Setting numbers and labels</h3>
  <div class="tbl"><table>
    <tr><th>Rule</th><th>Yes</th><th>No</th></tr>
    <tr><td>Thin space before the unit</td><td class="mono">4.6 s · −3.3 dB · 72 %</td><td class="mono">4.6s · -3,3dB</td></tr>
    <tr><td>Typographic minus; plus only where the sign matters</td><td class="mono">−18.0 dB · +0.8 dB</td><td class="mono">-18.0 dB</td></tr>
    <tr><td>Fixed precision</td><td class="mono">dB 1 · tempo 2 · % 0 · 440 Hz · 3.2 k</td><td class="mono">3200 Hz · 72.4 %</td></tr>
    <tr><td>Labels: sentence case, no tracking, acronyms kept</td><td>Feedback · Pre-delay · LFO rate · Gain dB</td><td>FEEDBACK · Fdbk: · Lfo Rate</td></tr>
  </table></div>
</section>

<section id="geometry">
  <p class="eyebrow">04 · Material</p>
  <h2>Radii, sizes, depth</h2>
  <h3>Radii · small, nearly square</h3>
  <div class="radii">
    <div class="radius"><i style="border-radius:var(--r-docked)"></i>{g('Radius.Docked')} · docked panel</div>
    <div class="radius"><i style="border-radius:var(--r-bar)"></i>{g('Radius.Bar')} · bar in a plot</div>
    <div class="radius"><i style="border-radius:var(--r-clip)"></i>{g('Radius.Clip')} · clip, row, menu item</div>
    <div class="radius"><i style="border-radius:var(--r-control)"></i>{g('Radius.Control')} · control, card, popover</div>
    <div class="radius"><i style="border-radius:var(--r-modal)"></i>{g('Radius.Modal')} · modal</div>
  </div>
  <h3>Sizes and steps</h3>
  <div class="tbl"><table>
    <tr><th>Element</th><th>px</th><th>Token</th></tr>
    <tr><td>Device card</td><td class="mono">700 × 260</td><td><code>DeviceCardKit.CardH</code></td></tr>
    <tr><td>Transport row · console · keys · Play</td><td class="mono">{g('Control.TransportBar')} · {g('Control.Console')} · {g('Control.Transport')} · {g('Control.Play')}</td><td><code>Control.TransportBar / Console / Transport / Play</code></td></tr>
    <tr><td>Shell button, field, readout well</td><td class="mono">{g('Control.Shell')}</td><td><code>Control.Shell</code></td></tr>
    <tr><td>Tab segment · segment container</td><td class="mono">{g('Control.Seg')} · {g('Control.SegGroup')}</td><td><code>Control.Seg / SegGroup</code></td></tr>
    <tr><td>Filter chip</td><td class="mono">{g('Control.Chip')}</td><td><code>Control.Chip</code></td></tr>
    <tr><td>Title bar · panel toolbar · status · menu item</td><td class="mono">32 · 32 · 22 · 24</td><td></td></tr>
    <tr><td>Knob · secondary / regular / main · inline</td><td class="mono">{g('Control.KnobSecondary')} / {g('Control.KnobRegular')} / {g('Control.KnobMain')} · {g('Control.KnobInline')}</td><td><code>Control.Knob*</code></td></tr>
    <tr><td>Parameter cell</td><td class="mono">{g('Control.ParamCell')}</td><td><code>Control.ParamCell</code></td></tr>
    <tr><td>Switch · slider track · meter</td><td class="mono">18 × 10 · 3 · 11 / 5</td><td></td></tr>
    <tr><td>Device steps · hair / gap / inset / wide</td><td class="mono">{g('Space.DeviceHair')} · {g('Space.DeviceGap')} · {g('Space.DeviceInset')} · {g('Space.DeviceInsetWide')}</td><td><code>Space.Device*</code></td></tr>
    <tr><td>Shell steps · tile / between panels (the seam) / inset</td><td class="mono">{g('Space.Tile')} · {g('Space.Gutter')} · {g('Space.Inset')}</td><td><code>Space.Tile / Gutter / Inset</code></td></tr>
  </table></div>
  <h3>Depth · a tone step and a seam; blur only when it floats</h3>
  <div class="depth">
    <div style="background:var(--bg-sunken);box-shadow:var(--shadow-sunken);border-radius:var(--r-control)">Well · rule edge</div>
    <div style="background:var(--bg-app);box-shadow:0 0 0 1px var(--seam)">Ground</div>
    <div style="background:var(--panel);box-shadow:0 0 0 1px var(--seam)">Panel · docked, flat</div>
    <div style="background:var(--surface-raised);border:1px solid var(--seam);box-shadow:var(--shadow-raised);border-radius:var(--r-control)">Raised · seam border · top bevel</div>
    <div style="background:var(--panel);box-shadow:var(--shadow-modal);border-radius:var(--r-modal)">Floating · Shadow.Modal</div>
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
    <figure>{knob(36,.5,live=True)}live</figure>
    <figure>{knob(36,.62,'var(--chroma-teal)')}modulated</figure>
    <figure>{knob(36,.4,dim=True)}inactive</figure>
  </div>
  <p class="note">At rest the arc is viz ink; it turns accent only while the knob is hovered or dragged. A modulated arc takes its source's chroma.</p>
  <h3>Parameter cell · 53 tall</h3>
  <div class="demo row" style="gap:6px">
    {knob(34,.6,label='Decay',val='4.6 s',mod=True)}
    {knob(34,.35,label='Size',val='31 m')}
    {knob(34,.42,label='Mix',val='42 %',live=True)}
    {knob(34,.3,'var(--chroma-teal)',label='LFO rate',val='18 %')}
    {knob(34,.0,dim=True,label='Width',val='off')}
  </div>
  <p class="note">A parameter changed from its default lifts its name to primary ink — no accent, no dot. Names are sentence case at 8 px.</p>
  <h3>Switch · segments · chips · track toggles</h3>
  <div class="demo">
    <div class="row"><span class="switch on"><i></i>True stereo</span><span class="switch"><i></i>Reverse IR</span>
      <div class="seg seg--device"><button aria-pressed="false">Eco</button><button aria-pressed="true">Mid</button><button aria-pressed="false">High</button></div>
      <div class="seg"><button aria-pressed="true">Arrangement</button><button aria-pressed="false">Session</button><button aria-pressed="false">Modular</button></div></div>
    <div class="row"><span class="chip on">Favorites</span><span class="chip"><i style="background:var(--danger)"></i>bass</span><span class="chip"><i style="background:var(--track4)"></i>analog</span><span class="chip"><i style="background:var(--track2)"></i>clean</span><span class="chip mono">+2</span>
      <span class="toggles"><b>M</b><b>S</b><b><i class="disc"></i></b></span><span class="toggles"><b class="m" style="color:var(--ink-on-mute)">M</b><b class="s" style="color:var(--ink-on-solo)">S</b><b class="r"><i class="disc" style="background:var(--record-ink)"></i></b></span></div>
  </div>
  <p class="note">Toggled on is a solid fill with derived ink: the accent for a chip or segment, amber for mute, blue for solo, red for arm.</p>
  <h3>Slider row</h3>
  <div class="demo">
    <div class="slider"><span>Pre-delay</span><span class="track"><b style="width:30%"></b><u style="left:30%"></u></span><span>24 ms</span></div>
    <div class="slider live"><span>Diffusion</span><span class="track"><b style="width:72%"></b><u style="left:72%"></u></span><span>72 %</span></div>
    <div class="slider dim"><span>Damping</span><span class="track"><b style="width:55%"></b><u style="left:55%"></u></span><span>4.1 k</span></div>
  </div>
  <h3>Transport · shapes, not an icon font</h3>
  <div class="transport">
    <span class="name">Untitled</span>
    <div class="seg"><button aria-pressed="true">Arrangement</button><button aria-pressed="false">Session</button><button aria-pressed="false">Modular</button></div>
    <span class="console">
      <span class="keys"><span class="tp"><i class="stop"></i></span><span class="tp play on"><i class="play-tri"></i></span><span class="tp"><i class="rec"></i></span></span>
      <span class="vr"></span>
      <span class="pos"><b>12.3.04</b><span class="unit">Bars</span></span>
      <span class="vr"></span>
      <span class="loop"><span class="unit">Loop</span>9.1 – 17.1</span>
    </span>
    <span class="well"><b>120.00</b><span class="unit">BPM</span></span><span class="well"><b>4/4</b></span><span class="well"><b>1/4</b><span class="unit">Grid</span></span>
    <span class="keys" style="gap:4px"><span class="tp on"><i class="met"></i></span><span class="tp"><i class="met"></i></span></span>
  </div>
  <p class="note" style="margin-top:8px">A flat 44 px row on the panel tone with a seam under it. Stop, play, record, position and loop share one 30 px well; keys are 26, Play 36 wide and solid accent only while the transport runs. Record at rest is a raised key with a red disc; engaged it is solid red with a pale disc. Readouts are 26 px wells: a mono value and its muted unit on one line.</p>
</section>

<section id="states">
  <p class="eyebrow">06 · States</p>
  <h2>Colour changes, nothing moves</h2>
  <div class="demo states">
    <figure><span class="btn">Rest</span>Raised · seam · bevel</figure>
    <figure><span class="btn hover">Hover</span>Raised hover, 120 ms</figure>
    <figure><span class="btn pressed">Pressed</span>Well, bevel hidden</figure>
    <figure><span class="btn on">Toggled on</span>Solid accent, derived ink</figure>
    <figure><span class="btn primary">Export</span>Primary: weight and tone, not colour</figure>
    <figure><span class="btn off">Disabled</span>Panel · faint ink, no opacity</figure>
    <figure><span class="listrow sel">Stone Vault</span>Selected row: 2 px accent tick</figure>
    <figure><span class="tabs"><span class="sel">Devices</span><span>Clip</span></span>Selected tab: 2 px underline</figure>
    <figure><span class="menu"><span class="head">Vocals<small>Audio · 3</small></span><hr><span class="sw"><i style="background:var(--track1)"></i><i style="background:var(--track2)"></i><i class="on" style="background:var(--track3)"></i><i style="background:var(--track4)"></i><i style="background:var(--track5)"></i><i style="background:var(--track6)"></i><i style="background:var(--track7)"></i><i style="background:var(--track8)"></i><i style="background:var(--track9)"></i></span><hr><span>Record input<small>Ext ›</small></span><hr><span>Copy<small class="k">⌘C</small></span><span class="hot">Duplicate<small class="k">⌘D</small></span><hr><span class="danger">Delete<small class="k">⌫</small></span></span>Menu: object header, inline colour row, value column, shortcuts ⌘C not ⌘+C, accent hover, danger last</figure>
  </div>
  <div class="row" style="margin-top:12px"><span class="field">Search</span><span class="field focus">Stone Vault</span></div>
  <p class="note">Fields and readouts are wells with a 1px rule edge; focus turns the edge accent, no ring. Empty state: one Ink 5 line, centred, no illustration, no call-to-action button.</p>
</section>

<section id="graphs">
  <p class="eyebrow">07 · Visualisations</p>
  <h2>One graph window</h2>
  <p>Well ground, rule frame, radius 3, viz-grid lines, mono axis labels in the corners only. Primary curve 1.8 px in viz ink, others 1.2–1.6 px in their chroma; only the active node is the accent. No fills under curves.</p>
  <div class="row" style="align-items:stretch">
    <div class="graph" style="width:420px;height:150px">
      <span class="title">Response</span>
      <svg width="420" height="150" viewBox="0 0 420 150" aria-hidden="true">
        <g style="stroke:var(--viz-grid);stroke-width:1">{''.join(f'<line x1="{x}" y1="0" x2="{x}" y2="150"/>' for x in (70,140,210,280,350))}<line x1="0" y1="75" x2="420" y2="75"/></g>
        <path d="M0 75 C60 75 80 50 120 48 S170 75 210 75 S260 110 300 108 S360 75 420 75" style="fill:none;stroke:var(--chroma-teal);stroke-width:1.4"/>
        <path d="M0 75 C50 75 90 40 130 38 S190 75 230 80 S300 100 330 96 S390 75 420 75" style="fill:none;stroke:var(--viz-ink);stroke-width:1.8;stroke-linecap:round"/>
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
  <p class="note">Meters: 11 wide, 5 apart, zones green → −6 dB amber → 0 red. Peak hold stays until clicked.</p>
  <h3>Clips · full-colour body, darker title band</h3>
  <div class="lane">
    {clip(0,'Kick loop',150)}{clip(3,'Chords',190,sel=True)}{clip(4,'Pad texture',160)}{clip(6,'Lead',120)}
    <span class="playhead" style="left:250px"></span>
  </div>
  <p class="note" style="margin-top:8px">A flat 2 px rectangle in the track colour, a 12 px title band 30 % toward black with the name in derived ink, content in a deeper shade; selected, the band is the accent. The playhead is 1 px accent, no glow.</p>
</section>

<section id="cards">
  <p class="eyebrow">08 · Device cards</p>
  <h2>700 × 260, at true size</h2>
  <p>Panel body, radius 3, seam frame. Header 22: a raised strip with the bevel, in fixed slots — name · the preset picker, centred between two equal halves · the muted mono badge · power, a slot every card reserves (A/B, move and delete are in the right-click menu). Body inset 6; sections have no fill and no frame, divided by a 1 px rule, titled in muted 600. Three columns: choice → work → output.</p>
  <div class="scroll"><div class="card">
    <div class="card__head"><span class="card__name">Nota Chamber</span><span class="preset"><i>&lsaquo;</i><b>Stone Vault</b><i>&rsaquo;</i></span><span class="card__right"><span class="card__badge">Convolution</span><span class="switch on"><i></i></span></span></div>
    <div class="card__body">
      <div class="sect"><h5>Choice</h5><div class="field" style="width:100%;height:22px;font-size:10px;color:var(--text-primary)">Concert Hall</div>
        <div class="seg seg--device" style="align-self:flex-start"><button aria-pressed="true">IR</button><button aria-pressed="false">Algo</button></div></div>
      <div class="sect"><h5>Work</h5>
        <div class="graph" style="height:96px"><span class="axis" style="left:4px;bottom:4px">0</span><span class="axis" style="right:4px;bottom:4px">2.6 s</span>
          <svg width="100%" height="96" viewBox="0 0 360 96" preserveAspectRatio="none" aria-hidden="true"><path d="M0 20 C40 22 80 40 140 58 S260 84 360 88" style="fill:none;stroke:var(--viz-ink);stroke-width:1.8"/><path d="M0 34 C40 36 90 52 150 66 S260 86 360 90" style="fill:none;stroke:var(--chroma-teal);stroke-width:1.4"/></svg></div>
        <div class="row" style="gap:4px;margin:0;justify-content:space-around">{knob(34,.3,label='Pre-delay',val='20.0 ms')}{knob(34,.62,label='Size',val='100 %',mod=True)}{knob(34,.2,label='Attack',val='0.0 ms')}{knob(34,.55,label='Decay',val='2.60 s',live=True)}</div></div>
      <div class="sect"><h5>Output</h5>
        <div class="slider" style="width:100%;grid-template-columns:34px 1fr 36px"><span>Conv</span><span class="track"><b style="width:60%"></b><u style="left:60%"></u></span><span>−3.0</span></div>
        <div class="slider" style="width:100%;grid-template-columns:34px 1fr 36px"><span>Dry</span><span class="track"><b style="width:80%"></b><u style="left:80%"></u></span><span>0.0</span></div>
        <div class="slider" style="width:100%;grid-template-columns:34px 1fr 36px"><span>Wet</span><span class="track"><b style="width:45%"></b><u style="left:45%"></u></span><span>45 %</span></div></div>
    </div>
  </div></div>
  <p class="note">Width exceptions by decision: Bass 1060, Physical 720, and the parameter-list stub; a hosted plug-in is S 260 or L 700.</p>
</section>

<section id="shell">
  <p class="eyebrow">09 · Shell</p>
  <h2>Docked panels on seams</h2>
  <div class="shell">
    <div class="h">Nota</div>
    <div class="t">Project · view switch · console · tempo, signature, key, grid · switches · CPU · master</div>
    <div class="b"><div>Browser</div><div>Canvas · arrangement, session, modular</div></div>
    <div class="d">Panel toolbar · Devices · Pattern · Clip · track · Freeze</div>
    <div class="db">Detail body · device cards</div>
    <div class="s">Status</div>
  </div>
  <p class="note" style="margin-top:10px">Title 32 on ground · transport 44 on panel · browser, canvas and detail panel docked, radius 0, 1 px seams between them · the detail panel opens with a 32 px toolbar · status 22 on ground. List rows 26, one line: dot, name, mono metadata. Arrangement tracks 64, groups 26, header column 228. Menus are raised popovers with a seam ring and no blur — Avalonia draws each popup in its own window; the command palette is the in-window modal with <code>Shadow.Modal</code>.</p>
  <h3>Accepted exceptions</h3>
  <ul class="plain">
    <li>Device cards keep 700 × 260, so device text sits at 7.5–10.5 px.</li>
    <li>Nine track roles; transport 44 with Nota's layout and order; no in-window menubar row; title bar 32.</li>
    <li>Menu popups carry a seam ring without a blur.</li>
    <li>Play turns green in Session; data hues go through <code>Ink()</code>.</li>
  </ul>
</section>
</div>
'''

SCRIPT=r'''<script>
(() => {
  const root = document.documentElement;
  const buttons = document.querySelectorAll('.mast .seg button');
  // A theme id or the Studio names dark / light; "graphite" and "paper" open the Ember themes.
  const known = Array.from(buttons, b => b.dataset.v);
  const norm = v => { v = ({ 'studio-dark': 'dark', 'studio-light': 'light', graphite: 'ember-graphite', paper: 'ember-paper' })[v] || v; return known.includes(v) ? v : 'dark'; };
  function hexes() {
    const v = root.dataset.variant || 'dark';
    document.querySelectorAll('.sw').forEach(s => { s.querySelector('.sw__hex').textContent = JSON.parse(s.dataset.hex)[v]; });
  }
  function set(v) {
    v = norm(v);
    if (v === 'dark') delete root.dataset.variant; else root.dataset.variant = v;
    buttons.forEach(b => b.setAttribute('aria-pressed', String(b.dataset.v === v)));
    hexes();
    try { localStorage.setItem('studio-variant', v); } catch (e) {}
  }
  buttons.forEach(b => b.addEventListener('click', () => set(b.dataset.v)));
  let saved = null; try { saved = localStorage.getItem('studio-variant'); } catch (e) {}
  const asked = new URLSearchParams(location.search).get('variant');   // ?variant=light, ?variant=ember-paper…
  set(asked || saved || 'dark');
  document.querySelectorAll('.sw').forEach(s => s.addEventListener('click', () => {
    const hex = s.querySelector('.sw__hex').textContent;
    const done = () => { s.classList.add('copied'); setTimeout(() => s.classList.remove('copied'), 900); };
    if (navigator.clipboard) navigator.clipboard.writeText(hex).then(done, () => {});
  }));
})();
</script>
</body>
</html>
'''

T=HEAD+STYLE+BODY+SCRIPT
open(ROOT+'/DESIGN.html','w',encoding='utf-8').write(T)
print(len(T.splitlines()))
