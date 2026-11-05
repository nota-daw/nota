# Research: Per-release splash art

Decisions taken while planning [spec.md](spec.md). Each entry gives the decision, why it
was chosen, and the alternatives considered.

The visual reference is the browser prototype reviewed with the maintainer on 2026-11-03,
kept as [`prototype.html`](prototype.html). Open it in a browser; the logo and Geist fonts
are missing there and fall back. Its generator is the behavioural reference for the C#
port: motif families, palettes, stroke weights, washes and layouts.

## R1 — Where the art code lives: `Theme/SplashArt/`

**Decision**: Put the composer and the renderer in `src/managed/Nota.App/Theme/SplashArt/`.

**Rationale**: `DesignTokenCheck.RunGeometry` bans `Linear|Radial|ConicGradientBrush` in
every `Nota.App` file outside `Theme/`. DESIGN.md rule 6 already keeps its one gradient
exception, `NotaPalette.PluginPreviewWash`, in `Theme/` ("It lives in `Theme/` like every
token; views still draw no gradients of their own"). The splash art is the second exception
of the same kind, so it lives in the same place. The ban on gradients in views stays intact.

A new check pins the exception down: inside `Theme/`, gradient brushes may appear only in
`NotaPalette.cs` and `Theme/SplashArt/`. Without it, any file dropped into `Theme/` could
use gradients.

**Alternatives**:
- Exempt `SplashWindow.cs` in the gradient check. Rejected: it widens a view-layer ban
  instead of keeping the exception with the tokens, and it breaks the precedent.
- Pre-render PNGs. Rejected: FR-003 forbids per-release assets.

## R2 — Composer and renderer are separate; the scene is theme-free

**Decision**: Split the work in two:
- `SplashArtComposer.Compose(string version)` is pure C#, with no Avalonia and no theme.
  It returns a `SplashScene`: the layout, plus primitives (rect, line, polyline, polygon,
  circle) whose paints refer to colour roles (`Ground`, `Rule`, `Track(i)`, `Accent`, ...).
  A role can be shaded toward black or the ground, and can carry an alpha.
- `SplashArtView : Control` resolves the roles against the live `NotaPalette` slots inside
  `Render` and draws the scene. Gradient brushes are built per render from the resolved
  colours.

**Rationale**:
- Determinism (FR-001, SC-001) can be tested on the scene itself, without a renderer. The
  smoke test links the composer file the way it already links other `Nota.App` sources
  (`<Compile Include="..\..\src\managed\Nota.App\…" Link=…>`).
- Theme changes only re-resolve colours. Reading `.Color` inside `Render` is the pattern
  DESIGN.md asks for, and `DesignTokenCheck` flags snapshotting brushes.
- The same scene feeds the review sheet (R6).

**Alternatives**: one class that draws as it rolls dice. Rejected: its output can't be
compared or exported without rendering.

## R3 — Seed and PRNG

**Decision**:
- The seed is FNV-1a (32-bit) over the UTF-16 code units of `AppInfo.Version`, which has
  `+metadata` already stripped, followed by a murmur-style finaliser.
- The PRNG is mulberry32. Both are hand-written, about 15 lines.
- Coordinates are rounded to 0.1 in the scene.

**Rationale**:
- `string.GetHashCode` is randomised per process, so it can't be the seed.
- `new Random(seed)` is stable today, but its algorithm is not a documented contract.
- mulberry32 matches the reviewed prototype, so a version gives the same composition in
  the prototype and in the app. That makes review easy.
- Rounding keeps scenes comparable and the SVG export small.
- `Math.Sin`/`Math.Exp` may differ by an ULP between CPU architectures. After rounding,
  differences are practically invisible, and SC-001 asks for identity on a given machine.

## R4 — Neighbouring versions must differ (FR-002, SC-002)

**Decision**: The primary motif family is no longer a free pick. It is
`family = (H(major.minor) + 5·patch) mod 6`, where `H` is the seed hash of `"major.minor"`
and 5 is coprime to 6. Every other choice is drawn from the full-version seed: palette,
stroke weight, wash, layout, motif parameters and the overlay. A pre-release suffix
changes those choices but keeps the release's family.

**Rationale**: With free picks, two consecutive patches share a family one time in six,
which reads as "the same picture again". Stepping by a number coprime to 6 guarantees that
consecutive patches change family. Minor bumps rehash, so they still vary freely. Their
palette, layout and weight almost always differ as well, which SC-002's distinct-scene
check covers.

**Alternatives**: reroll when the family equals the previous version's. Rejected: it can
still collide with an adjusted neighbour, and it is harder to reason about.

## R5 — Motif families, weights and colour, as approved

**Decision**: Port the reviewed prototype with these settings:

| Setting | Values |
|---|---|
| Families | `Roll`, `Rings`, `Strata`, `Panels`, `Lissajous`, `Faders`. Arrangement lanes are excluded (FR-005). |
| Overlay | Optional and quiet: neutral roles only, thinner stroke, never the accent. Allowed pairs: Panels→{Rings, Lissajous}, Strata→Rings, Faders→Strata, Roll→Rings. |
| Palette | Track colours by mode `mono / analog (i, i+1, i+2) / pair (i, i+4) / triad (i, i+3, i+6)` over `Track1..9` |
| Colour share | 0.35–0.85 |
| Stroke weight | {1.5, 2, 2.5, 3} px per version. Rings never go below 1.5. Seams between the art and the text stay 1px. |
| Wash | none / radial / linear, from the palette's first colour at ≤28% to transparent |
| Accent | Exactly one "live" element per primary family: playhead (Roll, Strata), current step (Rings), touched fader (Faders), a point on the curve (Lissajous), at most one ringed circle (Panels). Overlays never add one (FR-006). |

**Rationale**: These are the settings the maintainer accepted in review on 2026-11-03.

## R6 — Reviewing many versions without building them (FR-012)

**Decision**: Add a smoke-test mode, `--splash-sheet <out.html> [theme-id] [from] [count]`.
- It composes scenes for a range of versions and resolves roles from
  `Theme/Themes/<id>.json`. The default theme is Studio Dark.
- It writes one HTML page of inline SVGs, with the version, family and palette under each.

**Rationale**:
- It follows the existing `--palette-dump` and `--sample-dump` modes.
- It needs neither Avalonia rendering nor a display.
- The SVG writer only maps primitives one-to-one (about 60 lines). The app's renderer
  does the same mapping, so both draw from the same scene.

**Alternatives**: a `NOTA_SPLASH_VERSION` environment override in the app. Rejected as
the main tool: it shows one version per launch, for 650 ms. It may be added later for
manual checks, but is not needed.

## R7 — Text, layout and readability (FR-007)

**Decision**:
- The composer picks a layout: text left (art 270×300 on the right), text right, or text
  at the bottom (art 420×196 on top).
- `SplashWindow` places the existing logo, name, version and progress bar in the text
  region. That region is the plain `BgSunken` ground today's splash already uses.
- The art is clipped to its region, and a 1px `Seam` separates the two.

**Rationale**: The text keeps exactly today's ground and colours, so it passes the
readability bar in every theme without new contrast work (SC-004). The art never sits
under text.

## R8 — Failure and timing (FR-008, FR-010, SC-005)

**Decision**:
- `SplashWindow` composes synchronously in its constructor, inside `try/catch`. On any
  exception it builds today's plain card.
- The smoke test asserts that composing one scene takes under 10 ms (median of 20).
  Rendering is a few hundred to about 3,000 primitives, drawn once.

**Rationale**: Composition is cheap: the largest family, Strata, is about 20 polylines of
135 points. The splash then paints on its first frame with the art in place.

## R9 — Documentation

**Decision**:
- DESIGN.md rule 6 gets a second named exception, the splash art, next to the plug-in
  preview wash. The "Enforced by tests" section names the new check.
- CHANGELOG `[Unreleased]` gets an *Added* line.
- `nota-features` updates FEATURES.md.
- `nota-docs` checks whether the manual shows the splash. If it does, the screenshot is
  re-shot.

No new strings: the motif names are not shown in the app.
