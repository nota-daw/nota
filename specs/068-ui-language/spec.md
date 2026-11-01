# Feature Specification: Interface Language Selector (English + French)

**Feature Branch**: `068-ui-language`

**Created**: 2026-11-01

**Status**: Draft

**Input**: User description: "Ajouter un sélecteur de langue dans les Réglages de Nota, avec mise en place de l'infrastructure d'internationalisation (fichiers de ressources, changement de langue, persistance du choix), extraction de toutes les chaînes de l'interface utilisateur et traduction complète en français, l'anglais restant la langue par défaut."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Current state (for reference)**:

- Nota's interface is English only. Every label, menu item, tooltip, dialog and message
  is written directly where it is drawn; there is no translation mechanism and no
  language setting.
- Preferences → Appearance already holds the app-wide display settings (colour theme,
  interface scale, waveform style). The language setting belongs next to them.
- The user manual and release notes already exist in English and Russian; the app
  itself does not follow them.
- Roughly 900 visible text literals sit in the window and view code alone, before
  counting menus, dialogs, status messages and device/parameter names.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Use Nota in French (Priority: P1)

A French-speaking producer opens Preferences → Appearance and sets **Language** to
**Français**. The whole interface — menus, panels, buttons, tooltips, dialogs, status
messages — now reads in French. They quit and relaunch; Nota opens in French from the
first frame of the main window.

**Why this priority**: This is the feature. Everything else supports it.

**Independent Test**: Switch the language to Français, walk through every window and
menu, and check that no English text remains. Relaunch and confirm French is kept.

**Acceptance Scenarios**:

1. **Given** Nota in English (the default), **When** the user picks Français in
   Preferences → Appearance, **Then** the Language row itself says the change applies
   after a restart and offers a small **Restart** button (with the usual save prompt for
   unsaved work), with no popup; the current session stays in English until then, with no
   window half-translated.
2. **Given** Nota set to French, **When** the user quits and relaunches, **Then** Nota
   opens in French from the first frame of the main window, including the splash screen
   where it shows text.
3. **Given** Nota set to French, **When** the user opens any menu, context menu,
   tooltip, dialog, confirmation, error message or the command palette, **Then** its
   text is in French.
4. **Given** Nota set to French, **When** a French label is longer than its English
   original, **Then** it is not cut off, does not overlap a neighbouring control, and
   the control stays reachable.

---

### User Story 2 - Go back to English (Priority: P1)

A user who switched to French wants English back, for example to follow an English
tutorial or to report a bug. They open Preferences → Appearance, which they can still
find and read, and pick **English**.

**Why this priority**: A language switch that cannot be undone safely is worse than no
switch.

**Independent Test**: From French, open Preferences and set the language back to
English; confirm the whole interface is English again and the choice persists.

**Acceptance Scenarios**:

1. **Given** Nota in French, **When** the user opens Preferences → Appearance, **Then**
   the Language setting is easy to recognise (its choices are always written in their
   own language: "English", "Français").
2. **Given** Nota in French, **When** the user picks English, **Then** the interface
   returns to English with nothing left in French.

---

### User Story 3 - A fresh install is in English (Priority: P2)

A new user, or a user upgrading from a version without the setting, launches Nota.
It opens in English exactly as before, whatever language their operating system uses.

**Why this priority**: English stays the default and nobody's existing setup changes
by surprise.

**Independent Test**: Delete the settings file, launch, and confirm English.

**Acceptance Scenarios**:

1. **Given** no stored language, **When** Nota launches, **Then** the interface is in
   English.
2. **Given** a settings file written by an older version, **When** Nota launches,
   **Then** the interface is in English and all other stored settings are unchanged.

---

### User Story 4 - Projects and content are unaffected (Priority: P2)

The user switches language back and forth while working. Their projects, track and
clip names, presets, files, undo history and playback are not touched, and a project
saved in one language opens identically in the other.

**Why this priority**: The language is a view setting. It must never alter work.

**Independent Test**: Open a project, switch language, compare project content and
saved file before and after.

**Acceptance Scenarios**:

1. **Given** a project with named tracks and clips, **When** the user switches
   language, **Then** the names the user typed are unchanged.
2. **Given** a project saved while Nota was in French, **When** it is opened in
   English (and the reverse), **Then** it opens and sounds identical.
3. **Given** playback is running, **When** the language is changed, **Then** audio is
   not interrupted.

---

### User Story 5 - Adding another language later is cheap (Priority: P3)

A contributor wants to add, say, Spanish. They add one translation file for it,
following the existing French one, and a new choice appears in the selector without
any other change to the app.

**Why this priority**: The infrastructure is the long-lasting part of this work; the
selector must not be hard-wired to two languages.

**Independent Test**: Add a minimal third translation file and confirm it appears in
the selector and is used for the strings it covers, with English for the rest.

**Acceptance Scenarios**:

1. **Given** a new translation covering only some texts, **When** the user selects it,
   **Then** those texts appear translated and every uncovered text appears in English
   rather than blank or as an internal key.

### Edge Cases

- **A text with no French translation yet** (a new feature added after this work):
  it shows in English. It never shows blank, as an internal identifier, or crashes.
- **Texts that mix fixed words and values** ("3 tracks selected", "Bar 12 · Beat 3",
  "Delete 5 clips?"): the French sentence is built as a whole so word order, gender and
  plural forms are correct (1 piste / 2 pistes), not glued together from English
  fragments.
- **Number, time and date formatting**: musical positions, tempo, dB and frequency
  values keep their existing notation in both languages; typed value entry accepts
  either "." or "," as the decimal separator (see Assumptions).
- **Stored language value unknown or invalid** (edited by hand, or from a newer
  version that has more languages): Nota uses English and leaves the stored
  language value as written until the user changes the setting (the file can still be saved
  for other reasons, such as the last version seen; the value and every other setting are kept).
- **Keyboard shortcuts** keep their keys in both languages; only the names and
  descriptions around them are translated.
- **Open windows after choosing a new language but before restarting**: nothing
  changes; every window stays in the previous language until the restart. Choosing
  another language and then the original one again cancels the pending change.
- **User-entered text** (project, track, clip, scene and preset names, tags, notes):
  never translated or altered.
- **Third-party plugin names, parameters and editor windows**: drawn by the plugin and
  not translated.
- **Built-in default names created for the user** (e.g. a new track's name, a section
  name): the name given when something is created follows the language active at that
  moment and is then ordinary user text; switching language later does not rename it.
- **Nota Remote** (the phone web UI), **MCP tool descriptions for AI agents**, the
  engine's log output and crash reports are not part of this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Preferences → Appearance MUST offer a **Language** setting with the
  choices **English** and **Français**. Each choice is written in its own language, so
  it can be found and read whichever language is active. The default is English.
- **FR-002**: Choosing a language MUST take effect for the whole Nota interface at the
  next start. The language is a drop-down list in Preferences → Appearance. On choosing, the
  row itself MUST say so (no popup) and, where Nota can start itself again, offer a
  **Restart** button; *Restart* follows the usual unsaved-changes prompt and reopens Nota
  in the chosen language; leaving it alone applies the choice at the next start. Until the restart the interface stays entirely in the previous language.
- **FR-003**: The French translation MUST cover every user-visible text of the desktop
  app: main window, menus and context menus, transport, arrangement, session, mixer,
  piano roll, audio and clip editors, browser, Preferences, device detail windows,
  welcome, about and what's-new windows, dialogs and confirmations, progress and status
  messages, error messages, tooltips and the command palette.
- **FR-004**: Names of built-in devices (instruments, effects), their parameter names
  and their value labels MUST stay in English in every language ("Cutoff", "Attack",
  "Reverb"), matching the usual studio vocabulary and English tutorials. Everything
  around them — panel titles, section headings, tooltips and descriptions, menus,
  messages — is translated.
- **FR-005**: The chosen language MUST persist across launches, be stored with the
  other Appearance preferences (not in the project), and be applied before the main
  window first appears.
- **FR-006**: With no stored language, or an unknown or invalid one, Nota MUST start in
  English, independent of the operating system's language.
- **FR-007**: A text that has no translation in the selected language MUST be shown in
  English; it MUST NOT be blank, show an internal identifier, or cause an error.
- **FR-008**: Texts that include counts or values MUST be translated as complete
  sentences, with correct singular/plural forms for each language.
- **FR-009**: The language setting MUST NOT change project content, project files,
  user-entered names, exports, audio, the engine's behaviour, or keyboard shortcut
  keys.
- **FR-010**: Layouts MUST accommodate the longer French text at every interface-scale
  preset: no clipped labels, no overlapping controls, every control still reachable.
- **FR-011**: Adding a further language MUST require only adding translation data for
  it and registering its name; the selector lists every available language without
  other code changes.
- **FR-012**: Every user-visible text added to the app from now on MUST be defined in
  the translation system, so English and French stay complete; the contributor
  documentation MUST say so and explain how to add or update a translation.
- **FR-013**: The French translation MUST use one consistent set of terms for music
  vocabulary (piste, clip, scène, enveloppe, filtre, etc.) across the whole app, and
  the user-facing documentation of the setting MUST be updated (CHANGELOG, FEATURES,
  user manual).

### Key Entities

- **Interface language**: one app-wide user preference identifying the language of the
  interface; one of the available languages (English, Français). Stored with the other
  Appearance preferences, not in the project.
- **Translation set**: the complete collection of interface texts for one language,
  keyed so that the same text has one meaning in every language. English is the
  reference set and the fallback.
- **Interface text**: any wording Nota itself shows to the user, as distinct from
  user-entered content and third-party plugin content.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a full walkthrough of the app in French (every menu, every window,
  every dialog reachable in normal use), a reviewer finds zero English interface texts
  other than those explicitly excluded (user content, plugin content, product and
  brand names, standard unit symbols).
- **SC-002**: After choosing a language, the user is told within 1 second that a
  restart is needed, and after that restart 100 % of the interface is in the new
  language, with no loss of the open project (the usual save prompt applies).
- **SC-003**: The chosen language is the one in use after 10 out of 10 relaunches.
- **SC-004**: At every interface-scale preset that fits the screen, 100 % of labels,
  buttons and menu items in French are fully visible and operable in the main window,
  Preferences and a device detail window.
- **SC-005**: A project saved in either language opens bit-for-bit identically in the
  other (same project file content apart from the usual save timestamp), and audio
  output is unchanged.
- **SC-006**: A contributor can add a minimal new language and see it in the selector
  in under 15 minutes, with no change to app code.
- **SC-007**: Audio and CPU load do not change when the language changes.

## Assumptions

- **English is the default language and the fallback.** Nota does not pick a language
  from the operating system on its own; a French-speaking user chooses Français once.
  An "automatic / follow system" choice is out of scope for this feature and can be
  added later on top of the same mechanism.
- **Scope is the desktop app.** Nota Remote (phone web UI), MCP tool descriptions for
  AI agents, the CHANGELOG and release notes, the engine's log output and crash
  reports are not translated here. The user manual already has its own EN/RU
  translation pipeline and French docs are a separate follow-up.
- **Two languages now, any number later.** English and French ship in this feature;
  the system is built so more can be added by contributors (User Story 5).
- **Left-to-right languages only** for now; right-to-left layout is out of scope.
- **Number and unit notation is not localised.** Musical notation (bars, beats, note
  names, dB, Hz, BPM) stays the same in both languages, which is the studio norm.
  Typed value entry keeps accepting both "." and "," as a decimal separator.
- **Translation quality** is native-level French written by the maintainers or
  contributors; machine translation may seed the first draft but a human review is
  part of finishing the feature.
- The control follows the existing picker style in the Appearance pane (DESIGN.md,
  Studio).
- Keyboard shortcuts are unchanged; the shortcuts list shows translated names with
  the same keys.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
