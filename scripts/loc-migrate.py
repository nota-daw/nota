#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
"""
One-off migration of interface texts from English-text-as-key to stable keys (spec 077,
research R2 / R4). Standard library only.

  --plan             scan the source, build the text -> key map, write keymap.tsv (repo root,
                     git-ignored). An existing keymap.tsv is read back first, so keys renamed
                     by hand in it stick on the next run.
  --apply            write src/managed/Nota.Application/Localization/en.json, convert
                     src/managed/Nota.App/Languages/fr.json to format 2 and rewrite every
                     Loc.T / Loc.F / Loc.Plural call and every {app:T ...} in place.
  --verify COMMIT    compare the tree with COMMIT (before the migration): every call site, in
                     order, must now use the key its old text maps to, and en.json / fr.json
                     must give that key exactly the old English / French. Exit 1 on any
                     difference.

The scanner mirrors tests/Nota.SmokeTest/DesignTokenCheck.Localization.cs (LocT, LocF,
LocPlural, LocXaml), so it sees exactly the keys the smoke test checks. It fails on any Loc
call it cannot read rather than guess. Deleted again once spec 077 is done (PR 3).
"""

import argparse
import html
import collections
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MANAGED = os.path.join(ROOT, "src", "managed")
EN_PATH = os.path.join(MANAGED, "Nota.Application", "Localization", "en.json")
FR_PATH = os.path.join(MANAGED, "Nota.App", "Languages", "fr.json")
KEYMAP = os.path.join(ROOT, "keymap.tsv")

LIT = r'"(?:[^"\\\n]|\\.)*"'
RE_T = re.compile(r'\bLoc\.T\(\s*(?P<t>' + LIT + r')(?:\s*,\s*(?:context:\s*)?(?P<c>' + LIT + r'))?')
RE_F = re.compile(r'\bLoc\.F\(\s*(?P<t>' + LIT + r')')
RE_PLURAL = re.compile(r'\bLoc\.Plural\((?P<n>[^"]*?),\s*(?P<one>' + LIT + r')\s*,\s*(?P<t>' + LIT + r')'
                       r'(?:\s*,\s*(?:context:\s*)?(?P<c>' + LIT + r'))?')
RE_PLURAL_NEW = re.compile(r'\bLoc\.Plural\(\s*(?P<t>' + LIT + r')\s*,')
RE_ANY = re.compile(r'\bLoc\.(?:T|F|FC|Plural)\(')
RE_XAML = re.compile(r"\{app:T\s+(?:'(?P<q>(?:[^'\\]|\\.)*)'|(?P<t>[^\s'},][^},]*?))\s*(?:,\s*Context=(?P<c>[^}\s]+))?\s*\}")
KEY_RE = re.compile(r'^[a-z0-9_]+(\.[a-z0-9_]+)+$')
MAX_KEY = 80

SKIP_PREFIXES = ("Nota.Application/Localization/",)
SKIP_FILES = {"Nota.App/TExtension.cs"}


# ---- C# reading ---------------------------------------------------------------------------

def code_of(line):
    """The line without its // comment (a // inside a string stays), as the smoke test's
    CodeOf; returned blank-padded so offsets keep matching the original line."""
    i, n = 0, len(line)
    while i < n:
        ch = line[i]
        if ch == '/' and i + 1 < n and line[i + 1] == '/':
            return line[:i] + ' ' * (n - i)
        if ch in '"\'':
            q = ch
            i += 1
            while i < n and line[i] != q:
                i += 2 if line[i] == '\\' else 1
            i += 1
            continue
        if ch in '@$' and i + 1 < n and line[i + 1] in '"@$':
            # verbatim / interpolated: skip to the closing quote, "" is an escaped quote
            j = i
            while j < n and line[j] != '"':
                j += 1
            verbatim = '@' in line[i:j]
            j += 1
            while j < n:
                if line[j] == '"':
                    if verbatim and j + 1 < n and line[j + 1] == '"':
                        j += 2
                        continue
                    break
                j += 2 if (not verbatim and line[j] == '\\') else 1
            i = j + 1
            continue
        i += 1
    return line


def blank_code(text):
    return '\n'.join(code_of(l) for l in text.split('\n'))


def blank_xml_comments(text):
    return re.sub(r'<!--.*?-->', lambda m: re.sub(r'[^\n]', ' ', m.group(0)), text, flags=re.S)


_ESC = {'n': '\n', 't': '\t', 'r': '\r', '0': '\0', '\\': '\\', '"': '"', "'": "'", 'a': '\a',
        'b': '\b', 'f': '\f', 'v': '\v'}


def unescape(body):
    """A C# regular literal body to its text, as .NET Regex.Unescape does for these."""
    out, i = [], 0
    while i < len(body):
        c = body[i]
        if c != '\\' or i + 1 >= len(body):
            out.append(c)
            i += 1
            continue
        e = body[i + 1]
        if e == 'u' and re.match(r'[0-9a-fA-F]{4}', body[i + 2:i + 6]):
            out.append(chr(int(body[i + 2:i + 6], 16)))
            i += 6
        elif e == 'x' and re.match(r'[0-9a-fA-F]{2}', body[i + 2:i + 4]):
            out.append(chr(int(body[i + 2:i + 4], 16)))
            i += 4
        else:
            out.append(_ESC.get(e, e))
            i += 2
    return ''.join(out)


def lit(m, g):
    v = m.group(g)
    return None if v is None else unescape(v[1:-1])


# ---- scanning -----------------------------------------------------------------------------

Site = collections.namedtuple("Site", "rel line start end kind text ctx one n")
# kind: T, F, P (plural), X (xaml). start/end: the span to replace in the original text.


def source_files():
    for d, dirs, files in os.walk(MANAGED):
        dirs[:] = sorted(x for x in dirs if x not in ("bin", "obj"))
        for f in sorted(files):
            if f.endswith(".cs") or f.endswith(".axaml"):
                p = os.path.join(d, f)
                yield os.path.relpath(p, MANAGED).replace(os.sep, "/"), p


def line_of(text, index):
    return text.count('\n', 0, index) + 1


def scan_text(rel, text):
    """All Loc sites of one file, in source order, plus problems."""
    sites, problems = [], []
    if rel.endswith(".axaml"):
        b = blank_xml_comments(text)
        for m in RE_XAML.finditer(b):
            t = m.group('q') if m.group('q') is not None else m.group('t')
            t = html.unescape(t).replace("\\'", "'")
            sites.append(Site(rel, line_of(b, m.start()), m.start(), m.end(), "X", t, m.group('c'), None, None))
        return sites, problems
    b = blank_code(text)
    taken = []
    for m in RE_PLURAL.finditer(b):
        sites.append(Site(rel, line_of(b, m.start()), m.start(), m.end(), "P", lit(m, 't'), lit(m, 'c'),
                          lit(m, 'one'), m.group('n').strip()))
        taken.append(m.start())
    for m in RE_PLURAL_NEW.finditer(b):   # the migrated form, Loc.Plural("key", n) — for --verify
        depth, j = 1, m.end()
        while j < len(b) and depth:
            depth += {'(': 1, ')': -1}.get(b[j], 0)
            j += 1
        sites.append(Site(rel, line_of(b, m.start()), m.start(), j - 1, "P", lit(m, 't'), None, None,
                          b[m.end():j - 1].strip()))
        taken.append(m.start())
    for m in RE_T.finditer(b):
        sites.append(Site(rel, line_of(b, m.start()), m.start(), m.end(), "T", lit(m, 't'), lit(m, 'c'), None, None))
        taken.append(m.start())
        rest = b[m.end():].lstrip()
        if not rest.startswith(')'):
            problems.append(f"{rel}:{line_of(b, m.start())}: Loc.T text is not the whole argument")
    for m in RE_F.finditer(b):
        sites.append(Site(rel, line_of(b, m.start()), m.start(), m.end(), "F", lit(m, 't'), None, None, None))
        taken.append(m.start())
        rest = b[m.end():].lstrip()
        if not (rest.startswith(',') or rest.startswith(')')):
            problems.append(f"{rel}:{line_of(b, m.start())}: Loc.F pattern is not the whole first argument")
    for m in RE_ANY.finditer(b):
        if m.start() not in taken:
            problems.append(f"{rel}:{line_of(b, m.start())}: Loc call not readable: {b[m.start():m.start() + 70]!r}")
    sites.sort(key=lambda s: s.start)
    return sites, problems


def scan_tree(read=None):
    """Sites per file. `read(rel)` returns a file's text (for a git commit), else the working tree."""
    out, problems = collections.OrderedDict(), []
    if read is None:
        files = [(rel, open(p, encoding='utf-8').read()) for rel, p in source_files()]
    else:
        files = read()
    for rel, text in files:
        if rel in SKIP_FILES or rel.startswith(SKIP_PREFIXES):
            continue
        if "Loc." not in text and "app:T" not in text:
            continue
        s, p = scan_text(rel, text)
        problems += p
        if s:
            out[rel] = s
    return out, problems


# ---- keys ---------------------------------------------------------------------------------

def snake(name):
    s = re.sub(r'(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])', '_', name)
    return re.sub(r'[^a-z0-9]+', '_', s.lower()).strip('_')


STEM_SUFFIXES = ("InstrumentCard", "DeviceBody", "MidiBody", "ViewModel", "Views", "View", "Window",
                 "Service", "Card", "Kit", "Viz", "Graph", "Curve", "Scope")


def strip_suffix(stem):
    for suf in STEM_SUFFIXES:
        if stem.endswith(suf) and len(stem) > len(suf):
            return stem[:-len(suf)]
    return stem


AREA_OVERRIDES = {
    "Nota.App/MainWindow": "main", "Nota.App/App": "app", "Nota.App/MenuKit": "menu",
    "Nota.App/MacAppMenu": "menu.mac", "Nota.App/TextBoxMenu": "menu.text_box",
    "Nota.Presentation/ViewModels/MainWindowViewModel": "main",
    "Nota.App/DeviceCards/PluginCard": "device.plugin",
    "Nota.App/DeviceCards/SidechainSourcePicker": "device.sidechain",
    "Nota.App/DeviceCards/Midi/MidiCardHeader": "device.midi",
    "Nota.App/DeviceCards/Rack/MacroPanel": "device.rack.macros",
    "Nota.App/DeviceCards/Rack/RackKindProfile": "device.rack",
    "Nota.App/DeviceCards/Rack/RackCardView": "device.rack",
    "Nota.App/DeviceCards/Sampler/SamplerViews": "device.sampler",
    "Nota.App/DeviceCardKit": "device.card",
    "Nota.App/Remote/ConnectPhoneView": "remote.connect", "Nota.App/Remote/RemoteService": "remote",
    "Nota.Application/Palette/BuiltinDeviceCatalog": "palette.devices",
    "Nota.Application/Palette/CommandCatalog": "commands",
    "Nota.Application/Palette/ModularCatalog": "palette.modular",
    "Nota.Application/Palette/DeviceCategories": "palette.categories",
}

CONTROL_DEVICES = ("AutoFilter", "AutoShift", "BeatRepeat", "Ceiling", "Comp", "Consort", "Crush",
                   "DynamicEq", "Eq8", "FluxReact", "Keys", "OperatorAlgo", "Pendulum", "Reverb",
                   "Rhythm", "Shutter", "Synth", "Utility", "Vocoder")


def area_of(rel):
    """Key prefix for a file: area[.sub-area from the partial-file suffix] (research R2)."""
    path = rel.rsplit('.', 1)[0] if rel.endswith(".cs") or rel.endswith(".axaml") else rel
    if path.endswith(".axaml"):
        path = path[:-6]
    d, _, fname = path.rpartition('/')
    parts = fname.split('.')
    stem, sub = parts[0], (parts[1] if len(parts) > 1 else None)
    base = f"{d}/{stem}"
    if base in AREA_OVERRIDES:
        area = AREA_OVERRIDES[base]
    elif d == "Nota.App/Controls":
        s = strip_suffix(stem)
        area = ("device." + snake(s)) if any(stem.startswith(c) for c in CONTROL_DEVICES) else "control." + snake(s)
    elif d.startswith("Nota.App/DeviceCards/Rack"):
        area = "device.rack"
    elif d.startswith("Nota.App/DeviceCards"):
        area = "device." + snake(strip_suffix(stem))
    elif d == "Nota.App" or d == "Nota.Presentation" or d == "Nota.Presentation/ViewModels":
        area = snake(strip_suffix(stem))
    elif d.startswith("Nota.Application/") or d.startswith("Nota.Infrastructure/"):
        area = snake(d.split('/')[1])
        rest = snake(strip_suffix(stem))
        if rest.startswith(area + "_"):          # Midi/MidiTransforms -> midi.transforms
            rest = rest[len(area) + 1:]
        if rest != area:
            area += "." + rest
    else:
        raise SystemExit(f"loc-migrate: no area for {rel} — extend area_of()")
    if sub:
        sub = snake(sub)
        if not area.endswith("." + sub) and area.split('.')[-1] != sub:
            area += "." + sub
    return area


def slug(text, limit):
    words = re.findall(r"[A-Za-z0-9]+", re.sub(r"\{\d+[^}]*\}", " ", text.replace("’", "").replace("'", "")))
    words = [w.lower() for w in words][:5]
    s = "_".join(words) or "pattern"            # "{0} · {1}": punctuation only
    s = s[:limit].rstrip('_') or "pattern"
    if re.search(r"[A-Z]{2}", text) and not re.search(r"[a-z]", text):
        s += "_caps"                            # "TRACK" beside "Track"
    return s


def ident(site):
    """What a key stands for: English text (other form), singular form, context, kind."""
    return (site.text, site.one if site.kind == "P" else None, site.ctx or "", "P" if site.kind == "P" else "T")


def old_key(text, ctx):
    return text if not ctx else text + "|" + ctx


def top_area(area):
    """The group a text is shared within: the first segment, or device.<name> for devices."""
    parts = area.split('.')
    return '.'.join(parts[:2]) if parts[0] == "device" and len(parts) > 1 else parts[0]


def groups_of(tree):
    """ident -> {top area -> {full areas}} over the whole tree."""
    g = collections.defaultdict(lambda: collections.defaultdict(set))
    for rel, sites in tree.items():
        a = area_of(rel)
        for s in sites:
            g[ident(s)][top_area(a)].add(a)
    return g


def slot(site, rel, groups):
    """What one key stands for: a text (ident) in one group — "common" when three or more
    groups use it (research R2), else the group of the file it is used in."""
    idt = ident(site)
    return (idt, "common" if len(groups[idt]) >= 3 else top_area(area_of(rel)))


def build_map(tree, previous):
    """slot -> key. `previous` (from keymap.tsv) wins for slots it knows."""
    groups = groups_of(tree)
    slots = {}
    for rel, sites in tree.items():
        for s in sites:
            sl = slot(s, rel, groups)
            idt, grp = sl
            slots[sl] = "common" if grp == "common" else (
                next(iter(groups[idt][grp])) if len(groups[idt][grp]) == 1 else grp)
    out, taken = {}, set()
    for sl, key in previous.items():
        if sl in slots:
            out[sl] = key
            taken.add(key)
    for sl in sorted(slots, key=lambda x: (bool(re.search(r"\{\d", x[0][0])), slots[x], x)):
        if sl in out:
            continue
        (text, one, ctx, kind), prefix = sl[0], slots[sl]
        base = slug(text, MAX_KEY - len(prefix) - 9 - (len(ctx) + 1 if ctx else 0))
        if ctx:
            base += "_" + snake(ctx)
        key, n = f"{prefix}.{base}", 2
        if key in taken and re.search(r"\{\d", text):
            key = f"{prefix}.{base}_n"          # "Audio" beside "Audio {0}"
        while key in taken:
            key, n = f"{prefix}.{base}_{n}", n + 1
        out[sl] = key
        taken.add(key)
    return out


def read_keymap():
    prev = {}
    if not os.path.exists(KEYMAP):
        return prev
    with open(KEYMAP, encoding='utf-8') as f:
        next(f, None)
        for line in f:
            p = line.rstrip('\n').split('\t')
            if len(p) < 7:
                continue
            key, group, kind, ctx, text, one = p[0], p[1], p[2], p[3], json.loads(p[4]), (json.loads(p[5]) if p[5] else None)
            prev[((text, one, ctx, kind), group)] = key
    return prev


def write_keymap(m, tree):
    groups = groups_of(tree)
    files = collections.defaultdict(set)
    for rel, sites in tree.items():
        for s in sites:
            files[slot(s, rel, groups)].add(rel)
    with open(KEYMAP, 'w', encoding='utf-8') as f:
        f.write("key\tgroup\tkind\tcontext\ttext\tone\tfiles\n")
        for sl, key in sorted(m.items(), key=lambda kv: kv[1]):
            (text, one, ctx, kind), group = sl
            f.write(f"{key}\t{group}\t{kind}\t{ctx}\t{json.dumps(text, ensure_ascii=False)}\t"
                    f"{json.dumps(one, ensure_ascii=False) if one is not None else ''}\t{','.join(sorted(files[sl]))}\n")


def check_map(m):
    bad, seen = [], {}
    for idt, key in m.items():
        if not KEY_RE.match(key) or len(key) > MAX_KEY:
            bad.append(f"bad key {key!r}")
        if key in seen:
            bad.append(f"key {key!r} used for two texts: {seen[key][0][0]!r} and {idt[0][0]!r}")
        seen[key] = idt
    return bad


# ---- writing ------------------------------------------------------------------------------

def dump_table(header, strings):
    """A language file: header fields, then strings one per line, keys in order given."""
    out = ["{"]
    for k, v in header.items():
        out.append(f"  {json.dumps(k)}: {json.dumps(v, ensure_ascii=False)},")
    out.append('  "strings": {')
    items = list(strings.items())
    for i, (k, v) in enumerate(items):
        comma = "," if i + 1 < len(items) else ""
        if isinstance(v, dict):
            inner = ", ".join(f"{json.dumps(a)}: {json.dumps(b, ensure_ascii=False)}" for a, b in v.items())
            out.append(f"    {json.dumps(k)}: {{ {inner} }}{comma}")
        else:
            out.append(f"    {json.dumps(k)}: {json.dumps(v, ensure_ascii=False)}{comma}")
    out.append("  }")
    out.append("}")
    return "\n".join(out) + "\n"


def en_strings(m):
    strings = {}
    for (idt, _), key in sorted(m.items(), key=lambda kv: kv[1]):
        text, one, ctx, kind = idt
        strings[key] = {"one": one, "other": text} if kind == "P" else text
    return strings


def fr_strings(m, fr_old):
    strings, used = {}, set()
    for (idt, _), key in sorted(m.items(), key=lambda kv: kv[1]):
        text, one, ctx, kind = idt
        ok = old_key(text, ctx)
        if ok in fr_old:
            strings[key] = fr_old[ok]
            used.add(ok)
    orphans = sorted(set(fr_old) - used)
    return strings, orphans


def rewrite(rel, text, sites, m, groups):
    out, pos = [], 0
    for s in sites:
        key = m[slot(s, rel, groups)]
        out.append(text[pos:s.start])
        if s.kind == "X":
            out.append("{app:T '" + key + "'}")
        elif s.kind == "P":
            out.append(f'Loc.Plural("{key}", {s.n}')
        else:
            out.append(f'Loc.{s.kind}("{key}"')
        pos = s.end
    out.append(text[pos:])
    return "".join(out)


# ---- commands -----------------------------------------------------------------------------

def cmd_plan():
    tree, problems = scan_tree()
    if problems:
        print("\n".join(problems), file=sys.stderr)
        return 1
    m = build_map(tree, read_keymap())
    bad = check_map(m)
    write_keymap(m, tree)
    n_sites = sum(len(s) for s in tree.values())
    commons = sum(1 for k in m.values() if k.startswith("common."))
    print(f"{n_sites} call sites in {len(tree)} files -> {len(m)} keys ({commons} common.*); keymap.tsv written")
    if bad:
        print("\n".join(bad), file=sys.stderr)
        return 1
    return 0


def cmd_apply():
    tree, problems = scan_tree()
    if problems:
        print("\n".join(problems), file=sys.stderr)
        return 1
    if not os.path.exists(KEYMAP):
        print("run --plan first", file=sys.stderr)
        return 1
    m = build_map(tree, read_keymap())
    bad = check_map(m)
    if bad:
        print("\n".join(bad), file=sys.stderr)
        return 1
    fr = json.load(open(FR_PATH, encoding='utf-8'))
    if fr.get("formatVersion") != 1:
        print("fr.json is not format 1 — already migrated?", file=sys.stderr)
        return 1
    with open(EN_PATH, 'w', encoding='utf-8') as f:
        f.write(dump_table({"formatVersion": 2, "code": "en", "name": "English", "plural": "one_is_1",
                            "notes": "The English reference: every interface text of Nota by key (spec 077). "
                                     "Change a text here; a new meaning or new placeholders get a new key."},
                           en_strings(m)))
    frs, orphans = fr_strings(m, fr["strings"])
    notes = fr.get("notes", "")
    notes = re.sub(r" A key is the English text.*?French counts 0 and 1 as one\.",
                   " Keys are the English reference's keys (Nota.Application/Localization/en.json); counted texts "
                   "take {\"one\", \"other\"} and French counts 0 and 1 as one.", notes)
    with open(FR_PATH, 'w', encoding='utf-8') as f:
        f.write(dump_table({"formatVersion": 2, "code": "fr", "name": fr.get("name", "Français"),
                            "plural": "one_is_0_1", "notes": notes}, frs))
    groups = groups_of(tree)
    for rel, sites in tree.items():
        p = os.path.join(MANAGED, rel)
        text = open(p, encoding='utf-8').read()
        new = rewrite(rel, text, sites, m, groups)
        if new != text:
            with open(p, 'w', encoding='utf-8') as f:
                f.write(new)
    print(f"en.json: {len(m)} keys; fr.json: {len(frs)} entries, {len(orphans)} orphans dropped; "
          f"{sum(len(s) for s in tree.values())} call sites rewritten in {len(tree)} files")
    for o in orphans:
        print("  orphan:", o)
    return 0


def git_files(commit):
    names = subprocess.run(["git", "-C", ROOT, "ls-tree", "-r", "--name-only", commit, "src/managed"],
                           capture_output=True, text=True, check=True).stdout.split()
    for n in names:
        if not (n.endswith(".cs") or n.endswith(".axaml")) or "/bin/" in n or "/obj/" in n:
            continue
        text = subprocess.run(["git", "-C", ROOT, "show", f"{commit}:{n}"], capture_output=True, check=True).stdout
        yield n[len("src/managed/"):], text.decode('utf-8')


def cmd_verify(commit):
    old_tree, p1 = scan_tree(lambda: list(git_files(commit)))
    new_tree, p2 = scan_tree()
    fr_old = json.loads(subprocess.run(["git", "-C", ROOT, "show", f"{commit}:src/managed/Nota.App/Languages/fr.json"],
                                       capture_output=True, check=True).stdout)["strings"]
    en = json.load(open(EN_PATH, encoding='utf-8'))["strings"]
    fr = json.load(open(FR_PATH, encoding='utf-8'))["strings"]
    diffs = list(p1) + list(p2)
    if set(old_tree) != set(new_tree):
        diffs.append(f"files differ: {sorted(set(old_tree) ^ set(new_tree))}")
    for rel in old_tree:
        olds, news = old_tree[rel], new_tree.get(rel, [])
        if len(olds) != len(news):
            diffs.append(f"{rel}: {len(olds)} sites before, {len(news)} now")
            continue
        for o, n in zip(olds, news):
            key = n.text  # the literal now is the key
            where = f"{rel}:{n.line}"
            if (o.kind == "P") != (n.kind == "P"):
                diffs.append(f"{where}: plural changed")
                continue
            e = en.get(key)
            want = {"one": o.one, "other": o.text} if o.kind == "P" else o.text
            if e != want:
                diffs.append(f"{where}: en[{key}] = {e!r}, was {want!r}")
            ok = old_key(o.text, o.ctx)
            if ok in fr_old and fr.get(key) != fr_old[ok]:
                diffs.append(f"{where}: fr[{key}] = {fr.get(key)!r}, was {fr_old[ok]!r}")
            if ok not in fr_old and key in fr:
                diffs.append(f"{where}: fr[{key}] appeared")
            if o.kind == "P" and n.n != o.n:
                diffs.append(f"{where}: count expression {n.n!r}, was {o.n!r}")
    n = sum(len(s) for s in old_tree.values())
    if diffs:
        print("\n".join(diffs[:200]), file=sys.stderr)
        print(f"{len(diffs)} differences over {n} call sites", file=sys.stderr)
        return 1
    print(f"verified {n} call sites in {len(old_tree)} files: English and French unchanged")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--plan", action="store_true")
    g.add_argument("--apply", action="store_true")
    g.add_argument("--verify", metavar="COMMIT")
    a = ap.parse_args()
    if a.plan:
        return cmd_plan()
    if a.apply:
        return cmd_apply()
    return cmd_verify(a.verify)


if __name__ == "__main__":
    sys.exit(main())
