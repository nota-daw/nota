# Research: Classic look

Phase 0 of [plan.md](plan.md). Each entry: decision, rationale, alternatives. Findings
come from the code on `develop` (after #296 and #336) and the `v0.59.2` tag.

## R1 — What actually differs from 0.59.2

**Finding.** Against `v0.59.2`, the three things users asked for differ like this:

| Area | 0.59.2 | Now (new look) |
|---|---|---|
| Colours | 107 tokens per theme | 122 tokens: 15 added (`Well`, `WellTrack`, `KnobTrack`, `Groove`, `DeviceSection`, `DeviceSectionEdge`, `DeviceHeader`, `Bevel`, `AccentSoft`, `KnobCap`, `MeterSafe`, `MeterHot`, `MeterOver`, `TrackHeaderSelected`, `LaneSelected`); changed values only in Studio Dark (`Panel`, `SurfaceRaised`, `TextDisabled`, `VizInk`) and Studio Light (`Mute`); Ember Graphite and Ember Paper unchanged |
| Corners | 0 docked, 1 plot bars, 2 clips/keys/rows/badges, 3 controls/cards/panels/popovers, 4 modals | steps Card 9, Group 7, Field 6, Cell 5, ValueBar 4, Switch 3, Fine 2 (`SkinGeometry.Design`) |
| Type | Geist for words, Geist Mono for measurements (`Font.Mono`, `NotaFonts.Mono*`; 181 code uses in 132 files plus the `TextBlock.Mono/.Readout/.Value/.Eyebrow` classes) | Geist only, tabular figures app-wide; Geist Mono files and the role removed |

The type scale (sizes) also moved (e.g. Readout 13 → 16) but sizes are layout and stay
out of scope (spec, Out of scope).

**Consequence.** The classic palettes are the current files with 5 values reverted and
15 new tokens given classic values; the corner scale is a geometry; the type work is
mostly restoring a role at known call sites.

## R2 — Where the Look lives and how it is stored

**Decision.** A new `Settings.Look` string (`""` = new, `"classic"`), next to
`Settings.Accent`. A pure `Look` enum and `Looks.Resolve(Settings)` in
`Nota.Application` (unknown value → New), so the smoke test walks it like
`Appearance.Resolve`. `AppearanceChoice` is unchanged: the look is orthogonal to which
theme is worn.

**Rationale.** Matches how the accent (spec 084) is stored and worn: one user-wide value,
applied over whichever theme is active. Keeps follow-system, the theme pickers and their
migration logic untouched.

**Alternatives.** Folding Look into `AppearanceChoice` — touches every theme-pick path
for no gain. Classic as extra theme ids — rejected by the user in favour of a switch.

## R3 — Composing theme × look × skin

**Decision.** `NotaThemeService.ApplyActive` builds the worn theme in this order:

1. Pick the theme (stored choice, try-on or fallback) — unchanged.
2. **Colours**: under Classic, a built-in theme is replaced by its classic palette
   (same id, kind and name); a user skin keeps its own colours.
3. **Accent** (`NotaAccent.Wear`) over the result — unchanged, so the accent option works
   under both looks.
4. **Geometry**: `Looks.Compose(skinGeometry, look)`:
   - typeface: the skin's `font.family` if set, else Geist (both looks);
   - radii: if the skin has a `radius` object, the skin's radii; else, under Classic, the
     classic geometry (R4), under New the design scale;
   - values typeface: the skin's `font.values` (R6) if set, else Geist under New and
     Geist Mono under Classic.
5. `NotaSkin.Set(geometry)` + `NotaFonts.SetMeasure(family)` (R5), re-coerce and repaint
   — the path a theme switch already takes, so the switch is live with no restart.

`Worn(theme)` (theme cards, Nota Remote) does **not** apply the look: Remote keeps the new
look (spec, Edge Cases) and theme cards show each theme's own colours under the worn look
via a separate `WornForCard`.

**Rationale.** One switch point, the same one theme/accent/skin changes go through;
"skin wins where it sets something" falls out of `Compose`. A skin's `radius` is treated
as a whole (scale and steps belong together), which is simpler to explain than per-step
merging.

**Alternatives.** Per-step merge of skin steps over classic steps — a skin with
`scale: 0.5` and no steps would then get classic steps, contradicting its own scale.

## R4 — The classic corner scale on today's steps

**Decision.** Classic geometry = `Roundness 0.4`, steps **Card 3, Group 3, Field 3,
Cell 2, ValueBar 2, Switch 2, Fine 2**.

**Rationale.** The skin overlay maps radii **by value** (`SkinGeometry.MapRadius`): every
control written with 9 becomes Card's radius, and so on. Two 0.59.2 distinctions no
longer have a value of their own: modal windows share Card (9) with device cards, and
clips share Field (6) with fields and dropdowns. So under Classic modals get 3 (were 4)
and clips 3 (were 2); plot bars on Fine get 2 (were 1). These three are the only
deviations from 0.59.2 and are recorded in the spec (FR-008). Off-scale radii (pills, 8,
10…) scale by 0.4 — close to 3/9 while pills stay round.

**Alternatives.** New steps `Modal` and `Clip` — they would need distinct design values
(the overlay keys on values), i.e. changing the new look's radii, which #572 rules out.
A per-control classic radius table — a second radius system to maintain forever.

## R5 — The values-and-readouts role

**Decision.**
- XAML: a new resource `Font.Measure`, a `FontFamily` set by the theme service into
  `Application.Resources` and read with `{DynamicResource Font.Measure}` by the
  `TextBlock.Mono`, `.Readout`, `.Value`, `.Eyebrow` classes and by the 0.59.2 markup that
  named `Font.Mono` directly. `NotaTheme.axaml` defines its default as the Geist URI.
- Code: `NotaFonts.Measure`, `MeasureMedium`, `MeasureBold` become properties over a
  current measure family (`NotaFonts.SetMeasure`), read at render time;
  `NotaFormattedText` keeps tabular figures on.
- Under New the family **is** Geist (same URI as `Font.UI`), so every role use renders
  exactly as today (SC-004) and a skin's `font.family` still reaches it through the
  existing overlay (`NotaSkin.IsGeist`). Under Classic it is Geist Mono, which the overlay
  leaves alone; a skin's `font.values` replaces either.

**Inventory (FR-009, clarified).** The role goes exactly where 0.59.2 used mono: the
call sites are recovered from the #296 commit (`cbd77012`), whose hunks replaced
`NotaFonts.Mono*` / `Font.Mono` with `NotaFonts.Sans*` / `Font.UI`; a helper script lists
them by file and line on `develop`. Where a 0.59.2 surface was redrawn, the same value
on the same device/panel/bar takes the role (judged per hunk). Uses of the four role
classes **added after 0.59.2** are moved to a words class (`TextBlock.Number`, Geist with
tabular figures in both looks) so they stay Geist under Classic.

**Rationale.** A dynamic resource changes live with no overlay bookkeeping and costs
nothing under New. The inventory is bounded by 0.59.2, so the work is finite and
reviewable per file.

**Alternatives.** An overlay mapping Geist→Geist Mono per element (like `NotaSkin`) —
cannot tell a value from a word without the role anyway. Making every number mono —
rejected in clarification.

## R6 — Theme file format 3

**Decision.** `formatVersion: 3` allows `font.values` (a family name, 1–100 chars) and
makes `font.family` optional when `font.values` is present. Format 1 and 2 files load
unchanged; the writer emits 3 only when `font.values` is set. Older Nota reports a
format-3 file as F1 (format version) — the existing behaviour for unknown versions.
Validation: V2′ names `font.values`; W1 also warns when the values family is not
installed (falls back to the look's default).

**Rationale.** One new optional key, the same shape as `font.family`; the version bump is
what older builds already understand as "newer than me".

**Alternatives.** A top-level `measureFont` key — two places for type; `font.mono` —
the role is about values, not about the face being monospaced.

## R7 — Classic palettes as data

**Decision.** Four embedded files `Theme/Themes/Classic/<id>.json` (format 1, same ids,
names and kinds as the built-ins), loaded into a lookup by id, never listed as themes.
Their 107 old tokens equal `v0.59.2`; the 15 new tokens take values that read like
0.59.2:

| New token | Classic value from (0.59.2 look) |
|---|---|
| `Well`, `WellTrack` | the 0.59.2 control fill (`Control`/`SurfaceRaised`) and track fill |
| `KnobTrack`, `Groove` | the 0.59.2 knob/slider track colour |
| `DeviceSection`, `DeviceSectionEdge`, `DeviceHeader`, `Bevel` | the 0.59.2 card body, its rule, card header, and transparent bevel |
| `AccentSoft` | the 0.59.2 selection tint |
| `KnobCap` | the 0.59.2 knob face |
| `MeterSafe`, `MeterHot`, `MeterOver` | the 0.59.2 meter green / amber / red |
| `TrackHeaderSelected`, `LaneSelected` | the 0.59.2 selected-row fill |

Exact sources are picked per theme in implementation by reading the 0.59.2 styles that
drew those parts, and noted in the file's sidecar comment table in DESIGN.md.

**Rationale.** Data, not code: the existing loader, validator and catalogue apply; a test
can compare against a committed snapshot of the tag (CI clones may be shallow, so the
test does not read git).

## R8 — Readability rule for classic palettes

**Decision.** Classic palettes must pass F1–F6. T3 readability (W2) may warn only on
pairings whose tokens all existed in 0.59.2 **and** whose values equal 0.59.2
(clarified: keep 0.59.2 exactly). A pairing involving one of the 15 new tokens must
pass. The smoke test enforces this with the 15-token list.

## R9 — Design checks

**Decision.** `DesignTokenCheck.RunType` changes from "one typeface" to "two roles":
`Font.UI` and `Font.Measure` are the only families in the theme, `Font.Measure` defaults
to the Geist URI (so New is one typeface), `NotaFonts` has `UiUri` and `MeasureMonoUri`;
`assets/fonts` holds the four Geist and four Geist Mono weights and nothing else;
`LICENSES/OFL-1.1-Geist.txt` covers both (Geist Mono is the same family, same licence —
it shipped this way until #296); views still never name a family by string. Radius checks
are unchanged (classic is a geometry, not new literals). A new check: every built-in has
a classic palette with the full catalogue.

## R10 — Startup, previews, MCP

**Decision.**
- Startup: `App` resolves `Looks.Resolve(settings)` and passes it with the appearance
  choice to `NotaThemeService.Set` before the splash/first window, so the first frame is
  classic.
- Previews: `RenderScoped` gains a look argument; skin previews render in the named look.
- MCP: `list_themes` → `choice.look`; new tool `set_look` (`new` | `classic`, UI thread,
  stored, Preferences refresh); `preview_theme` and `get_theme` gain optional `look`
  (default: the worn look). `get_theme` with `look: classic` on a built-in returns the
  classic palette, so it can be saved as a user skin.
- Duplicate (Preferences → Skins) copies the colours worn under the current look.

## R11 — Risks

- **Clipping under Geist Mono.** Mono digits are wider than Geist tabular; some redrawn
  cards are tight (260 px rule). Mitigation: preview set at 100 % and the largest UI scale
  under Classic, checked by hand per surface (quickstart); fix by role-local tweaks only
  where clipping appears (no layout size changes, FR-005).
- **Static typeface caches.** Views that cached `NotaFonts.Mono` in `static readonly`
  fields in 0.59.2 must read the property at render time, or they miss a live switch.
  Covered by the inventory review.
- **Maintenance.** New tokens need a classic value (enforced by the catalogue check).

## R12 — Solid clips (scope widened, User Story 4)

**Decision.** `Settings.ClipStyle` string: `""` (follow the look), `"solid"`, `"tinted"`;
`Looks.ClipStyleOf(Settings)` resolves it with the stored look (unknown → follow). MainWindow
sets `ArrangementView.ClipStyle` from settings at start and on `Settings.Changed` — the path
`ArrangementWaveform` takes; the Look switch saves settings, so a follow-the-look style
changes with it. `DrawClipBody` gets the 0.59.2 branch back for Solid: the body in the track
colour, the 12 px title band (`ClipBand`: 30 % toward `ShadeDown`; the accent when selected)
with the name in `InkOn(band)`, content in `ClipInk` (58 % toward `ShadeDown`), no outline,
2 px band on a short row. Corners use today's clip step (`NotaRadius.FieldValue`), so Classic
gives 3 and New 6. The name keeps today's type size and the words role.

**Rationale.** Code from the tag, behind one switch; Tinted is untouched, so New stays
pixel-identical. Session slots were a tint in 0.59.2 as well, so they are left out.

**Alternatives.** A third "Follow look" option in the control — the stored `""` already
means that, and two options are what the issue agreed.
