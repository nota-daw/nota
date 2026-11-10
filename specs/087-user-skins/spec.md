# Feature Specification: User skins (colours, typeface, corner radii) with MCP support

**Feature Branch**: `feat/user-skins`

**Created**: 2026-11-10

**Status**: Draft

**Input**: User description: "Support for skins (GitHub issue nota-daw/nota#336, approved). Users can make their own colour theme ("skin") for Nota: a theme JSON file in the same shape as the built-in themes (specs/011-colour-themes contracts/theme-file.md, validated by NotaThemes.Parse) that the app loads from the user data folder, lists in Preferences next to the four built-in themes, and can wear. Plus MCP support: tools on the Nota MCP server to list themes, read a theme's JSON (as a starting template), validate a theme JSON, apply/preview a theme sent in the request body, and render preview screenshots — given a theme JSON, return an array of images, one per representative UI component/surface (e.g. arrangement, mixer, device cards, piano roll, browser, preferences, menus), showing how each looks in that skin — so an external client such as an AI can iterate on a palette and check it maps onto a usable skin (contrast, readability). Optionally save a validated theme as a user theme via MCP."

**Issue**: nota-daw/nota#336 — "Support for skins" (approved). The issue asks for user skins
and "an API hook where you edit the json (or send it in the request body), and it gives you
back an array of screenshots, one for how every component looks with your new skin", so that
something like an AI on the other end can help map a colour palette onto a usable skin.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Builds on spec 011 (colour themes — the theme file format and its rules F1–F8, the
readability pairings T3) and spec 084 (accent colour worn over any theme). The MCP side
extends the existing in-app MCP server (`src/managed/Nota.Mcp`).

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md),
[`../011-colour-themes/contracts/theme-file.md`](../011-colour-themes/contracts/theme-file.md),
[`../011-colour-themes/contracts/ui-contract.md`](../011-colour-themes/contracts/ui-contract.md).

## Clarifications

### Session 2026-11-10

- Q: Is a skin colours only, or can it also change the typeface and corner radii? → A: A skin can also change the UI typeface and the corner radii (user request); spacing, sizes and icons stay fixed.
- Q: Where does a skin's typeface come from? → A: Only from fonts installed on the system, named by family; if the family is not installed the skin falls back to Geist with a warning.
- Q: How does a skin set the corner radii? → A: One roundness multiplier (0–2×) over the whole radius scale, plus optional exact values for individual radius steps; docked panels stay square.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Wear a skin I made (Priority: P1)

A user who wants Nota to look different copies a theme file (for example one of the
built-ins, exported as a starting point), changes its id, name and colours, and drops it in
Nota's themes folder. Nota lists it in Preferences → Appearance next to the built-in themes,
under Dark or Light according to its kind, and the user picks it like any built-in theme.
The whole app takes the new colours at once, and the choice survives a restart.

**Why this priority**: This is the feature the issue asks for. Without loading and wearing a
user theme, the MCP tools have nothing to save into or switch to.

**Independent Test**: Put a valid theme file with a new id in the themes folder, open
Preferences → Appearance, pick it, restart Nota. The app wears the skin before and after
the restart.

**Acceptance Scenarios**:

1. **Given** a valid theme file with a new id in the themes folder, **When** the user opens
   Preferences → Appearance, **Then** the theme shows as a card in the Dark or Light list
   (by its kind), after the built-in themes, marked as a user theme.
2. **Given** that user theme is listed, **When** the user picks it, **Then** every window
   repaints in its colours at once, exactly as picking a built-in theme does, and the user's
   accent colour (spec 084) is worn over it the same way.
3. **Given** a user theme is chosen, **When** Nota restarts, **Then** it starts in that theme.
4. **Given** a user theme that names an installed typeface and sets roundness 0, **When** it
   is worn, **Then** all of Nota's text uses that typeface and all rounded corners become
   square, with no change in layout sizes; picking a built-in theme again restores Geist and
   the standard radii.
5. **Given** a user theme is chosen in follow-system mode for one OS mode, **When** the OS
   switches mode, **Then** Nota switches between that theme and the theme chosen for the
   other mode.
6. **Given** the user adds or edits a file while Nota is running, **When** they reopen the
   Appearance page (or use its reload action), **Then** the list and the worn theme reflect
   the file as it is now.

---

### User Story 2 - Know why my skin did not load (Priority: P1)

A user's theme file has a mistake: a missing token, a typo in a colour, an id that clashes
with a built-in theme. Instead of the theme silently not appearing, Preferences → Appearance
names the file and the first problem in plain words, and the rest of the app is unaffected.

**Why this priority**: A hand-written file with ~100 tokens will be wrong the first few
times. Without feedback the feature is unusable for anyone not reading logs.

**Independent Test**: Put a file missing one token, and a second one with an invalid colour,
in the themes folder. Open Appearance: both files are named with their problem; neither is
in the theme lists; the app keeps its current theme.

**Acceptance Scenarios**:

1. **Given** a theme file that breaks a format rule (F1–F6 of the theme-file contract),
   **When** themes are loaded, **Then** the theme is left out and Appearance shows the file
   name and the reason, naming the token where there is one.
2. **Given** a theme file whose id equals a built-in theme's id or another user theme's id,
   **When** themes are loaded, **Then** only the first one (built-ins win) is loaded and the
   other is reported as a duplicate.
3. **Given** a valid theme that fails some readability pairings (T3), **When** it is loaded,
   **Then** it is listed and can be worn, and Appearance notes that it has readability
   warnings (how many) so the user knows text may be hard to read.
4. **Given** the chosen user theme's file is removed or becomes invalid, **When** Nota starts
   or themes are reloaded, **Then** Nota falls back to the default for that mode (Studio Dark
   or Studio Light) without an error dialog, as for a missing built-in theme.

---

### User Story 3 - Start from an existing theme (Priority: P2)

From Appearance the user opens the themes folder in the system file manager, and saves a copy
of any listed theme (built-in or user) into that folder under a new id and name, ready to edit.

**Why this priority**: A complete template is the practical way to write a ~100-token file.
It is a small addition that makes Story 1 reachable without reading the contract.

**Independent Test**: Use "Duplicate" on Studio Dark, edit one colour in the new file, reload:
the duplicate is listed and differs in that one colour.

**Acceptance Scenarios**:

1. **Given** Appearance is open, **When** the user chooses "Open themes folder", **Then** the
   folder (created if missing) opens in the system file manager.
2. **Given** any listed theme, **When** the user duplicates it, **Then** a new valid file with
   a fresh id and the name "<name> copy" is written to the themes folder, appears in the list
   and is selected for wearing.

---

### User Story 4 - An AI (or script) designs a skin over MCP (Priority: P1)

An MCP client connected to a running Nota — for example an AI assistant asked to "make a
skin from this palette" — lists the themes, reads a theme's full JSON as a template, sends a
modified theme JSON to be checked, gets back the format errors and the readability warnings,
gets back a set of preview images showing how each main part of Nota looks in that skin, and
iterates. When the skin is good it saves it as a user theme and, if asked, makes Nota wear it.

**Why this priority**: This is the second half of the issue ("an API hook … gives you back an
array of screenshots"), and the user asked for it explicitly. It shares everything with
Story 1 except the tools themselves.

**Independent Test**: With Nota running, call the tools in order from an MCP client: list,
read Studio Dark, validate a copy with one broken token (error names it), validate a copy
with low-contrast text (warning names the pairing and ratio), render previews of a valid copy
(one image per surface, each in the new colours, the live window unchanged), save it (it
appears in Preferences), apply it (the window wears it).

**Acceptance Scenarios**:

1. **Given** Nota is running with MCP on, **When** a client lists themes, **Then** it gets
   every loaded theme: id, name, kind, whether it is built-in or a user theme, and which theme
   is worn now; plus any user theme files that failed to load, with their reasons.
2. **Given** a theme id, **When** a client reads it, **Then** it gets the theme's complete
   file content in the theme-file format, with keys in catalogue order, usable as a template.
   The client can also get the token catalogue: every token name with a one-line role and
   whether it may be translucent.
3. **Given** a theme JSON in the request, **When** a client validates it, **Then** it gets:
   valid or not; every format problem found (not only the first), each naming the token; and
   every failed readability pairing with the two tokens, the measured value and the minimum.
   Nothing is saved or changed in the app.
4. **Given** a valid theme JSON in the request, **When** a client asks for previews, **Then**
   it gets back a list of images — one per named surface (see FR-016) — each paired with the
   surface's name and a one-line description, rendered in that skin. The user's live window
   does not change, flicker or lose its state while previews render.
5. **Given** an invalid theme JSON, **When** a client asks for previews, **Then** no images
   are rendered and the same problems as validate are returned.
6. **Given** a client asks for previews of only some surfaces (by name), **When** it renders,
   **Then** only those come back; an unknown surface name is reported, not ignored.
7. **Given** a valid theme JSON, **When** a client saves it, **Then** it is written to the
   themes folder as a user theme and appears in Preferences; saving over an existing user
   theme with the same id requires an explicit overwrite flag; saving with a built-in id is
   refused.
8. **Given** a listed theme id, **When** a client applies it, **Then** Nota wears it and
   stores the choice exactly as picking it in Preferences would (single-theme mode).
9. **Given** a valid theme JSON that is not saved, **When** a client applies it as a
   try-on, **Then** Nota wears it live without storing the choice; the try-on ends when the
   client reverts it, the user picks a theme in Preferences, or Nota restarts.

---

### Edge Cases

- **Themes folder missing**: treated as empty; created on first save, duplicate or "Open
  themes folder".
- **Huge or non-theme files in the folder**: only `*.json` files are read; a file larger than
  a sane limit (assumed 256 KB) is reported and skipped; other files are ignored.
- **Same id in two user files**: the first in file-name order loads; the other is reported as
  a duplicate.
- **File name and id differ**: allowed; the id inside the file is what Nota stores. Saving over
  MCP names the file after the id.
- **A user theme with the id `system`** or an id that is not lower-case-with-hyphens: rejected
  (rule F2).
- **Typeface not installed** (skin made on another machine): falls back to Geist with a
  warning; the skin's colours and radii still apply.
- **Font without tabular figures or without some weights**: numbers may change width and
  bold text may be synthesised; accepted, not an error.
- **Font lacking glyphs** (e.g. Cyrillic for the Russian UI): per-character fallback to the
  system font, as today for glyphs Geist lacks.
- **Roundness 0**: every corner square, including device cards and modal windows.
- **Translucent value on an opaque token**: rejected (rule F6), naming the token.
- **Ember-era value rule (F8)**: does not apply to user themes; only built-in Studio themes.
- **Theme file edited while it is worn**: takes effect on reload, not by watching the file.
- **Preview request while another preview is rendering**: requests are served one at a time;
  the second waits rather than failing.
- **Preview of a theme whose kind differs from the window's current kind** (light skin while
  the app is dark): the preview shows the skin's own kind correctly.
- **MCP request with a malformed or non-object JSON body**: returned as a validation error,
  never a server crash.
- **Accent colour**: previews and try-ons are shown with the user's current accent, as the
  skin would really be worn; a client can ask to render with the theme's own accent instead.

## Requirements *(mandatory)*

### Functional Requirements

**Loading and wearing user themes**

- **FR-001**: Nota MUST load user themes from a `Themes` folder inside the Nota user data
  folder (the same root that honours the test data-folder override), at startup and on demand.
- **FR-002**: A user theme MUST use the theme-file format of spec 011, either format version 1
  (colours only) or format version 2, which is version 1 plus two optional parts: `font` and
  `radius` (FR-021–FR-026). It MUST pass rules F1–F6, and the rules for those parts, to load. F7 (readability) failures MUST NOT block loading; they
  are reported as warnings. F8 does not apply to user themes.
- **FR-003**: A user theme's id MUST be unique: an id equal to a built-in theme's id, or to an
  already-loaded user theme's id, MUST be rejected for that file with a "duplicate id" reason.
- **FR-004**: Loaded user themes MUST appear in Preferences → Appearance in the Dark or Light
  card list according to their kind, after the built-in themes, sorted by name, visibly marked
  as user themes, and MUST be selectable everywhere a built-in theme is (single mode and each
  slot of follow-system mode).
- **FR-005**: Wearing a user theme MUST behave exactly as wearing a built-in theme: all
  windows, custom-drawn views, Fluent controls, shadows and Nota Remote follow it, and the
  user's accent colour is worn over it.
- **FR-006**: The theme choice MUST persist across restarts when it names a user theme. If the
  stored theme is not found or no longer valid at startup or reload, Nota MUST fall back as
  spec 011 defines for a missing theme, without an error dialog, and keep the stored choice
  so that the theme comes back if the file is fixed.
- **FR-007**: Appearance MUST offer a way to reload themes from the folder without
  restarting, and MUST reload them each time the page is opened.
- **FR-008**: Appearance MUST list every user theme file that failed to load, with the file
  name and the reason in plain words, and MUST show a readability-warning count on any loaded
  user theme that fails pairings.
- **FR-009**: Appearance MUST offer "Open themes folder" (creating it if missing) and a way
  to duplicate any listed theme into a new user theme file with a fresh id and the name
  "<name> copy".
- **FR-010**: Loading user themes MUST never crash or block the app: an unreadable, invalid or
  oversized file is skipped and reported. File reading MUST NOT happen on the audio thread
  (it is UI-side only).

**Typeface and corner radii**

- **FR-021**: A theme MAY name a UI typeface by the family name of a font installed on the
  system. When worn, that family MUST replace Geist for all of Nota's own text — shell,
  menus, dialogs, device cards and custom-drawn views — keeping the existing type scale,
  weights and tabular figures (where the font offers them). Plug-in windows are not affected.
- **FR-022**: If the named family is not installed, the theme MUST still load and be wearable;
  Nota MUST use Geist instead and show a warning on that theme (in Appearance and in MCP
  validation) naming the missing family. A theme with no typeface uses Geist.
- **FR-023**: A theme MAY set a roundness multiplier from 0 to 2 (default 1) applied to every
  step of the radius scale (Card, Group, Field, Cell, ValueBar, Switch, Fine), and MAY set an
  exact value in pixels (0–24) for any individual step, which wins over the multiplier for
  that step. Docked panels MUST stay at 0. Computed radii are rounded to whole pixels.
- **FR-024**: Values outside these ranges, an unknown radius step name, or a typeface value
  that is not a non-empty string of at most 100 characters MUST fail validation, naming the
  field.
- **FR-025**: Changing to a theme with a different typeface or radii MUST take effect live in
  every open window, like a colour change, without a restart and without changing layout
  sizes (text that no longer fits is truncated as it would be for any long label).
- **FR-026**: Each theme carries its own typeface and radii; in follow-system mode, switching
  OS mode switches them along with the colours.

**Validation shared by app and MCP**

- **FR-011**: There MUST be one validation used both by the loader and by MCP. For MCP it MUST
  report every format problem found (not only the first) and every failed readability pairing
  with the tokens, measured value and minimum, as structured data, plus a warning when the
  named typeface is not installed.

**MCP tools** (on the existing in-app MCP server)

- **FR-012**: A tool MUST list themes: id, name, kind, built-in or user, which theme is worn
  now (and the follow-system choice), plus failed user theme files with reasons.
- **FR-013**: A tool MUST return a theme's complete file content by id, and a tool (or the same
  tool) MUST return the token catalogue: name, group, short role, translucency allowed, plus
  the radius steps with their default values and the allowed ranges for font and radius.
- **FR-013a**: A tool MUST list the font families installed on the system, so a client can
  choose a typeface that will resolve.
- **FR-014**: A tool MUST validate a theme sent as JSON in the request and return the result
  of FR-011, without side effects.
- **FR-015**: A tool MUST render preview images of a theme sent as JSON in the request,
  returning a list of images (PNG), each with its surface name and description. It MUST NOT
  change the live window's theme, layout, selection or open project, and MUST NOT save
  anything.
- **FR-016**: The preview surfaces MUST cover the main parts of the app, at least: the main
  window overview, arrangement (tracks, clips, automation), mixer, device rack with built-in
  device cards, piano roll, browser, transport/top bar, Preferences → Appearance, a context
  menu, and a dialog. Each surface MUST have a stable name the client can request on its own.
- **FR-017**: Previews MUST be deterministic: rendered from a fixed built-in sample scene at a
  fixed size and scale, so the same theme gives the same images on the same machine, and
  differences between two calls come only from the theme (and the accent option). The
  exceptions are the main window's own parts (`overview`, `top-bar`, `browser`). They are
  drawn from the live window, so they show the user's open project (plan: research R6,
  implementation finding).
- **FR-018**: A tool MUST save a valid theme JSON as a user theme file named after its id;
  overwriting an existing user theme MUST require an explicit flag; a built-in id MUST be
  refused; the new theme MUST then be listed without a restart.
- **FR-019**: A tool MUST apply a listed theme by id, wearing it and storing the choice as
  Preferences would; and a tool MUST try on an unsaved valid theme JSON live without storing
  it, with a way to revert to the stored choice.
- **FR-020**: MCP theme tools that change what the user sees (apply, try-on, save) MUST run on
  the UI thread and MUST leave Preferences, if open, showing the current state.

### Key Entities

- **Theme file**: the spec 011 format — format version, id, name, kind (dark/light), and one
  colour per catalogue token. Format version 2 adds the optional typeface and radius settings. Built-in themes ship inside the app; user themes live in the
  user's themes folder.
- **User theme**: a theme loaded from the themes folder; same capabilities as a built-in,
  plus a source file path; cannot share an id with a built-in.
- **Load problem**: a file in the themes folder that did not load — file name, reason.
- **Validation result**: valid flag, list of format problems (rule, token, message), list of
  readability warnings (pairing, the tokens, measured value, minimum).
- **Token catalogue**: the ordered list of colour tokens a theme must define, each with group,
  role and whether it may be translucent.
- **Typeface (`font`)**: optional; the family name of an installed system font.
- **Radius settings (`radius`)**: optional; a roundness multiplier (0–2, default 1) and
  optional exact pixel values per radius step.
- **Preview surface**: a named part of the app (e.g. `mixer`) with a description, rendered to
  an image from the sample scene.
- **Try-on**: a live, unsaved theme the app wears until reverted or replaced.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can go from a duplicated built-in theme to wearing their edited skin in
  under 5 minutes, without restarting Nota.
- **SC-002**: 100% of malformed theme files in a test set (missing token, unknown token, bad
  hex, translucent opaque token, bad id, duplicate id, bad kind, invalid JSON) are reported
  with a reason that names the problem and token, and none of them crash or change the app.
- **SC-003**: An MCP client gets the full set of preview images for one theme in under 10
  seconds on a typical development machine, and the user sees no change in the live window
  while it happens.
- **SC-004**: Rendering previews of the same theme twice gives identical images for every
  surface drawn from the sample scene.
- **SC-005**: Every one of the four built-in themes, read over MCP and saved back under a new
  id, loads as a user theme and looks identical to the original when worn.
- **SC-006**: A full design loop — list, read template, validate, preview, save, apply — can
  be completed by an MCP client with no human step in Nota.
- **SC-007**: A skin that sets an installed typeface and roundness 0 changes every visible
  text run and every rounded corner in the preview set, and none of the layout sizes.

## Assumptions

- A skin covers colours, the UI typeface and the corner radii. Sizes, spacing, type scale,
  icons and images stay as Nota's design language defines them. Theme files without a
  typeface or radius part (every format-version-1 file, including the four built-ins) keep
  Geist and the style guide's radius scale.
- Readability pairings warn but do not block, because a user is free to choose a low-contrast
  look for themselves; the warnings make the trade-off visible.
- Previews use a fixed built-in sample scene (a small project with several coloured tracks,
  clips, devices and notes) rather than the user's open project, so that results are
  comparable between calls and do not depend on or disturb the user's work.
- The MCP server only listens on loopback (existing behaviour), so the new tools need no extra
  authentication; writing files is limited to the themes folder.
- A full in-app colour editor is out of scope; the user edits files in any text editor, or an
  MCP client edits them.
- No sharing/gallery of skins and no import from other apps' theme formats.
- Previews are rendered by Nota's own UI renderer inside the running app, not by an external
  browser or tool; no new dependency is expected.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — themes and previews are UI-side only
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no native change
- [x] Smoke-test impact assessed: no engine behaviour change; theme validation of user files is covered by managed checks
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
