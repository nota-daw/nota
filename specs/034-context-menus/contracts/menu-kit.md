# Contract: menu building blocks (`MenuKit`) and the `MenuItem` theme

Internal UI contract every context menu builds on. Signatures are indicative; the
behaviour is binding.

## MenuItem theme (`Theme/NotaTheme.axaml`)

| Part | Rule |
|---|---|
| Height / radius / padding | 24 · `Radius.Clip` (2) · 8,0 |
| Columns | icon 16 · label * · value-or-shortcut auto (12 left gap) · caret 12 — aligned across all items |
| Icon | `MenuKit.Icon(kind)` only; ink = item foreground (muted at rest via the icon presenter) |
| Shortcut | `MenuKit.Spell(InputGesture)` (maps the Avalonia gesture onto `Shortcut.ToPlatformString`) — `⌘G`, `⇧⌘G`, `⌫` on macOS; `Ctrl+G`, `Ctrl+Shift+G`, `Del` elsewhere; Geist Mono 11; `TextMuted` |
| Value | attached `MenuKit.Trailing` — a string (current value, `TextMuted`), a switch, or a data colour chip; shown before the shortcut and caret |
| Caret | `Glyph(ChevronRight, 8)`, `TextMuted`, inset by the caret column |
| Highlight | `Accent` fill; label, icon, shortcut, value, caret → `TextOnAccent`; 120 ms ease-out |
| Disabled | everything `TextDisabled`; no highlight |
| `.danger` | label + icon `Danger` at rest; highlight as above; disabled wins |
| `.inline` | no highlight, no icon/caret columns, content spans the width |
| Menu frame | `SurfaceRaised`, 1 px `Seam`, radius 3, padding 4, `MinWidth` 200; no blur |
| Separator | 1 px `Rule`, margin 4,4 |

## MenuKit API

| Member | Returns | Behaviour |
|---|---|---|
| `Item(header, icon?, click?, gesture?, enabled?)` | `MenuItem` | existing; labels are written sentence case at the call site (RunLabels) |
| `ValueItem(header, icon, value, submenu)` | `MenuItem` | submenu item showing the current value (FR-010) |
| `Danger(header, icon, click, gesture?)` | `MenuItem` | `.danger` item (FR-006) |
| `Header(name, colour?, badge)` | `MenuItem.inline` | non-interactive, 28 tall, name 600 truncating, badge mono muted (FR-007) |
| `ColourGrid(current, pick)` | `MenuItem.inline` | 9 × 3 swatches 14 px gap 4 + Auto; current ringed; click / Enter → `pick(index)` then close; arrows move (FR-008, FR-017) |
| `Segments(label, options, chosen, pick)` | `MenuItem.inline` | "Height" caption + segmented keys; chosen in accent; `pick(value, allModifier)` then close (FR-009) |
| `SwitchItem(header, icon, isOn, toggle)` | `MenuItem` | 18 × 10 switch in the value column; click anywhere toggles (FR-009a) |
| `ClipboardGroup(copy, cut, paste, duplicate, canPaste)` | `Control[]` | Copy ⌘C · Cut ⌘X · Paste ⌘V · Duplicate ⌘D in that order (FR-013) |
| `Icon(kind)` | `Control` | existing; neutral glyph 11 px |
| `Caption(text)` | `MenuItem.inline` | small muted heading inside a menu ("Recent") |
| `Swatch(brush)` | `Control` | 8 px data colour chip for the Trailing column (tag colours) |
| `AddGroups(items, groups…)` | — | adds non-empty groups with one rule between them |
| `AddRecent(submenu, items)` | — | "Recent" caption + items, nothing when empty |

Order inside a menu (FR-013): `Header` → inline rows → naming / settings → object actions
→ create / group → `ClipboardGroup` → `Danger` items, rules between groups.
