# Feature Specification: Key-Based Interface Texts and Downloadable Languages

**Feature Branch**: `feat/key-based-localization`

**Created**: 2026-11-04

**Status**: Draft

**Issue**: [nota-daw/nota#476](https://github.com/nota-daw/nota/issues/476) (approved)

**Input**: User description: "Implement GitHub issue nota-daw/nota#476 (approved): rework interface localization. (1) Priority: migrate from English-text-as-key to stable key-based strings with en.json embedded in the app as the source of truth, a key naming convention, a migration script that generates keys, rewrites the call sites and converts fr.json; smoke-test checks move to keys. (2) Translations move to the new repository nota-daw/nota-translations (default branch main, AGPL-3.0-only, index.json listing languages with coverage and the app versions they fit, one file per language, CI there validating each language against the matching en.json); fr moves there. (3) The app ships English only; Preferences → Language lists languages from the repository and downloads the chosen one into the user data directory, works offline with installed languages, offers updates, verifies a checksum, lets a translator load a local file, falls back to English for any missing key, applies after restart. Decide resx vs key-based JSON. Out of scope: new languages, number culture, RTL, live language switch. Builds on spec 068-ui-language."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [x] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Current state (for reference)** — spec [`068-ui-language`](../068-ui-language/spec.md):

- Every interface text goes through one lookup (`Loc.T` / `F` / `FC` / `Plural`), about
  4,600 calls in 243 files. **The key is the English text itself**; a word with two
  meanings gets a `"text|context"` suffix.
- English needs no file: it is written at the call site. French lives in
  `Nota.App/Languages/fr.json` (~3,650 entries), embedded into the app. The picker in
  Preferences → Appearance → Language lists whatever files are embedded; a change applies
  after a restart.
- The smoke test checks that every key used in the source has a French entry, that none
  is orphaned, that placeholders match, and keeps a shrink-only ratchet of raw literals
  (`LocalizationAllowList.txt`).
- Consequence: rewording, fixing a typo in or re-punctuating any English text silently
  detaches it from every translation; adding or fixing a translation needs a pull request
  to the app and a release.
- The repository `nota-daw/nota-translations` exists (empty; `main`, squash-only, PR
  required). Translations there are AGPL-3.0-only, like the app.

## Clarifications

### Session 2026-11-04

- Q: Which format do language files use — key-based JSON (current parser adapted), `.resx` + `ResourceManager`, or gettext `.po`? → A: Key-based JSON with plural objects; the current parser moves to keys. `.resx` and `.po` are rejected.
- Q: Once the app stops embedding French, does it keep any embedded copy as an offline fallback? → A: No. The app embeds English only; every other language, French included, is downloaded. A user offline on the first launch after updating runs in English until the language arrives (story 5).
- Q: How does the app know which translation version fits its own version? → A: Each Nota release publishes its English reference to `nota-translations` as `reference/<version>.json` with a hash of its key set. The repository's checks validate each language against the references of every supported version. The app takes the translation checked against a reference with its own key-set hash, else the one checked against the nearest older reference.
- Q: When a stored language is not installed yet (an update from a version with embedded French), is the downloaded language applied on the same launch or after a restart? → A: After a restart. Nota starts in English without waiting for the network, downloads the language in the background, and the Language row and a quiet status-bar notice say it is ready, with a Restart button.
- Q: How is the work split into pull requests and releases? → A: Three pull requests. (1) nota: keys and `en.json`, French still embedded, no visible change. (2) nota-translations: README, LICENSE, checks, language list, `fr`, the first reference. (3) nota: downloading, the Language row, removing the embedded `fr.json`, publishing the reference from the release workflow. Pull requests 2 and 3 ship in the same release.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Change an English text without breaking translations (Priority: P1)

A contributor rewords a button ("Bounce in place" → "Bounce to new track") or fixes a
typo in a tooltip. They edit the English text in one place. Every translation of that
text stays attached to it; nothing in the smoke test turns red; the French screen still
shows the (now possibly outdated) French text until a translator updates it.

**Why this priority**: This is the maintenance cost the issue is about, and it grows with
every release. It is also the prerequisite of stories 2–4: a translation kept outside
the app needs a stable key to refer to.

**Independent Test**: After the migration, change the English text of a translated entry,
build, run the smoke test and the app in French: the build is green and the entry still
shows its French text.

**Acceptance Scenarios**:

1. **Given** the migrated source, **When** a contributor changes the English text of a
   key, **Then** only the English reference file changes, no call site changes, and every
   language keeps its entry for that key.
2. **Given** a new interface text, **When** a contributor adds it, **Then** they add one
   key to the English reference file and use the key at the call site; the smoke test
   fails if they use a key that is not in the reference file, or leave a key in it that
   nothing uses.
3. **Given** two places where one English word means different things ("Open" a file,
   "Open" a clip), **When** they are migrated, **Then** they are two keys and need no
   separate context marker.
4. **Given** the migration is done, **When** Nota runs in English or in French, **Then**
   every screen shows exactly the text it showed before the migration (no visible change).

---

### User Story 2 - Translate Nota without touching the app (Priority: P2)

A translator wants to fix a French wording or start German. They open the
`nota-translations` repository, edit or add one language file following its README and
open a pull request. Its checks tell them, without building Nota, whether the file is
valid against the current English reference: unknown keys, wrong placeholders, broken
plurals, and how complete the language is.

**Why this priority**: Opens translation to people who will never build a DAW, and stops
translation fixes from waiting for an app release.

**Independent Test**: Open a pull request in `nota-translations` that breaks a placeholder
in `fr`: the check fails and names the key. Fix it: the check passes and reports French
coverage.

**Acceptance Scenarios**:

1. **Given** the repository, **When** a translator reads its README, **Then** it explains
   the file shape, the key/English reference, plurals, placeholders, how coverage is
   counted and how to preview a file in Nota (story 4).
2. **Given** a pull request changing a language file, **When** its checks run, **Then**
   they validate it against the English reference of every app version the language
   claims to fit and report coverage per language.
3. **Given** a merge to `main`, **When** the checks pass, **Then** the language list the
   app reads is updated (coverage, checksum) without manual edits.
4. **Given** the French translation that lived in the app, **When** this feature ships,
   **Then** it lives in `nota-translations` and no longer in the app repository.

---

### User Story 3 - Get a language from Preferences (Priority: P2)

A user opens Preferences → Appearance → Language. Besides English they see the languages
available for their Nota version, each with its own name and how complete it is. They
pick Français: Nota downloads it, the row says it applies after a restart (as today), and
after the restart the interface is in French. Later, offline, Nota still starts in French.
When a better French translation is published, Nota offers to update it.

**Why this priority**: The user-facing half of moving translations out of the app; until
it ships, users of non-English languages must not lose them (see story 5).

**Independent Test**: With a clean data directory, choose Français, restart, check the
interface is French; disconnect the network, restart, still French; publish a newer `fr`
in a test copy of the repository, check Nota offers the update.

**Acceptance Scenarios**:

1. **Given** Nota online, **When** the user opens the Language setting, **Then** it lists
   English plus every language available for this Nota version, with name and coverage,
   and marks which are already installed.
2. **Given** the user picks a language not installed, **When** the download succeeds and
   the file checks out, **Then** it is stored in the user data directory and the restart
   prompt appears as it does today; **When** the download fails or the file does not check
   out, **Then** the row says so in place, nothing is stored and the running language is
   unchanged.
3. **Given** a language installed, **When** Nota starts offline, **Then** it starts in that
   language; the setting still lists installed languages.
4. **Given** Nota offline or the repository unreachable, **When** the user opens the
   setting, **Then** it lists English and the installed languages and says that more are
   available online — never an error dialog.
5. **Given** a newer version of an installed language that fits this Nota version, **When**
   Nota learns of it, **Then** the Language row offers an update; nothing is downloaded
   until the user agrees.
6. **Given** any text missing from the installed language, **When** it is shown, **Then**
   it is shown in English — never blank, never as a key.

---

### User Story 4 - Preview a translation in progress (Priority: P3)

A translator working on a language file wants to see it in Nota before publishing. In the
Language setting they load a local file; after a restart Nota runs in it. Problems in the
file (unknown keys, bad placeholders) are listed so they can fix them.

**Why this priority**: Makes translation work verifiable without a pull request or a
build; needed by few users.

**Independent Test**: Load a local file with one broken placeholder and one valid entry:
Nota starts in it, the valid entry shows translated, the broken one in English, and the
problem is reported.

**Acceptance Scenarios**:

1. **Given** a local language file, **When** the user loads it, **Then** it becomes the
   running language after a restart and is marked as local (not updated from the
   repository).
2. **Given** a local file with errors, **When** it is loaded, **Then** each problem is
   reported with its key, the broken entries fall back to English and Nota still starts.

---

### User Story 5 - Existing French users keep French after updating (Priority: P1)

A user who chose Français before this feature updates Nota. Nota gets French from the
repository on the first launch and keeps showing French. If it cannot, it says so in
place and runs in English until it can.

**Why this priority**: Shipping English only must not take the interface language away
from people who already use it.

**Independent Test**: With settings saying `fr` and no installed language, start the new
build online: it starts in English, offers a restart once French is installed, and runs
in French after it. Offline: it runs in
English and the Language row explains why.

**Acceptance Scenarios**:

1. **Given** a stored language that is not installed, **When** Nota starts online, **Then**
   it starts in English without waiting, downloads that language in the background and,
   once it is installed, the Language row and a quiet status-bar notice say it is ready
   with a **Restart** button (the usual save prompt for unsaved work); after the restart
   Nota runs in it.
2. **Given** the same offline, **When** Nota starts, **Then** it runs in English, keeps the
   stored choice, and the Language row says the language will be downloaded when online.

---

### Edge Cases

- A language file published for an older Nota version: keys that no longer exist are
  ignored, new keys show in English; the coverage shown is for the running version.
- A language whose file does not fit the running version at all (format too new): it is
  not offered, and an installed copy is not used; Nota runs in English and says why.
- The download is interrupted, truncated or its checksum does not match: nothing replaces
  the installed copy.
- Two Nota instances or versions sharing one data directory: an installed language never
  breaks the version that did not download it.
- The repository list is unreachable or malformed: the setting falls back to installed
  languages, with no dialog.
- A key used in the source is missing from the English reference: the build (smoke test)
  fails, so this never reaches a user.
- A translated value uses a placeholder its English text does not have: the entry shows
  in English and the problem is reported (smoke test for published languages, CI in the
  repository, the in-app report for local files).
- The user removes an installed language while it runs: it stays in use until restart,
  then English.

## Requirements *(mandatory)*

### Functional Requirements

**Keys (story 1)**

- **FR-001**: Every interface text MUST be looked up by a stable key, not by its English
  text. Contexts (`"text|context"`) MUST disappear; two meanings are two keys.
- **FR-002**: The English text of every key MUST live in one English reference file in the
  app repository, embedded in the app; it is the only place English interface text is
  written.
- **FR-003**: Keys MUST follow a documented naming convention (lower-case, dot-separated,
  area first, e.g. `settings.audio.buffer_size`, `mixer.track.mute.tooltip`), described in
  `CONTRIBUTING.md`.
- **FR-004**: The switch MUST be done by a repeatable migration tool that generates keys,
  rewrites the call sites and converts the French file, so the change is mechanical and
  reviewable; generated keys MUST be unique and follow FR-003.
- **FR-005**: After the migration every screen MUST show, in English and in French, the
  same text as before it (no visible change).
- **FR-006**: The smoke test MUST fail when a key used in the source is missing from the
  English reference, when the reference has a key nothing uses, when a reference entry's
  plural shape does not match how it is used, or when the raw-literal ratchet grows.
- **FR-007**: Plurals and placeholders MUST keep today's behaviour: one whole pattern per
  text, the language's plural rule picks the form, a value that cannot be formatted falls
  back to English.

**Translations repository (story 2)**

- **FR-008**: `nota-daw/nota-translations` MUST hold one file per language (not English),
  a language list (code, own name, coverage, checksum, the Nota versions it fits) and a
  README for translators.
- **FR-009**: Each pull request in that repository MUST be checked against the English
  reference of the Nota versions the language fits: valid shape, unknown keys reported,
  placeholders and plural forms valid, coverage computed. Failures MUST name the key.
- **FR-010**: Each Nota release MUST publish its English reference to the repository as
  `reference/<version>.json` together with a hash of its key set, without a manual copy
  step. A language fits a Nota version when it passed the checks against a reference with
  that version's key-set hash; versions whose keys did not change share one hash and need
  no new publication.
- **FR-011**: The language list MUST be produced from the files by the repository's checks,
  not edited by hand.
- **FR-012**: The French translation MUST move to that repository and leave the app
  repository; its history of who translated it SHOULD be kept in the move.
- **FR-013**: Files in the repository MUST be AGPL-3.0-only, stated in the repository.

**Getting languages (stories 3, 5)**

- **FR-014**: The app MUST ship with English only — no embedded copy of any other
  language, not even as an offline fallback.
- **FR-015**: Preferences → Appearance → Language MUST list English, installed languages
  and, when reachable, the languages available for the running Nota version, with own
  name, coverage and installed / update-available state.
- **FR-016**: Choosing a language not installed MUST download it only on that choice,
  verify its checksum and fit, store it in the user data directory, and keep today's
  "applies after a restart" behaviour.
- **FR-017**: An installed language MUST work offline; Nota MUST never need the network to
  start.
- **FR-018**: Nota MUST offer, not perform, an update when a newer fitting version of an
  installed language exists; checking for one MUST follow the existing update-check
  setting (no network request when the user has turned checks off, beyond one the user
  triggers).
- **FR-019**: A failed list, download or check MUST be reported in place in the Language
  row; it MUST NOT change the running language or replace an installed copy.
- **FR-020**: Any key missing or broken in the running language MUST show in English.
- **FR-021**: A user whose stored language is not installed (an update from a version with
  embedded French) MUST get it automatically when online, in the background, without
  delaying start-up; it applies after a restart offered in the Language row and the status
  bar (story 5). Offline — or with update checks turned off, where the row offers a
  **Download** button instead (FR-018) — Nota runs in English, keeps the stored choice and
  says why.
- **FR-022**: The user MUST be able to remove an installed language.

**Local files (story 4)**

- **FR-023**: The user MUST be able to load a local language file as the running language;
  it is marked local and never replaced by a download.
- **FR-024**: Problems in a loaded file MUST be reported per key; Nota MUST still start.

**Format**

- **FR-025**: Language files MUST use one format for the English reference, the
  repository and local files, readable by common translation tools: key/value JSON (`"settings.title": "Settings…"`) with plural objects
  (`{ "one": …, "other": … }`). `.resx` and gettext `.po` are not used.

### Key Entities

- **Key**: stable identifier of one interface text (`settings.title`); never shown.
- **English reference**: every key with its English text and plural shape; embedded in the
  app; published per release as `reference/<version>.json` with its key-set hash.
- **Language file**: one language's translations of keys, plus code, own name, format
  version and translator notes; may be partial.
- **Language list**: what the repository offers — per language: code, own name, coverage,
  checksum, version, the key-set hashes (English references) it was checked against.
- **Installed language**: a language file in the user data directory with where it came
  from (repository version or local) — what Nota loads at start.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Changing the English text of any key touches exactly one file and leaves 100%
  of the existing translations of that key attached.
- **SC-002**: After the migration, a screen-by-screen comparison in English and French
  shows zero changed texts.
- **SC-003**: Zero interface texts are looked up by their English text; zero contexts
  remain.
- **SC-004**: A translator can fix a French entry and see the check result on their pull
  request without building Nota, in under 5 minutes of check time.
- **SC-005**: A user picks a language and has it running after one restart, with the
  download taking under 10 seconds on a typical connection.
- **SC-006**: Nota starts in an installed language with the network off, every time.
- **SC-007**: 100% of users who ran Nota in French before updating run it in French after
  updating, after being online once and one restart.
- **SC-008**: The app package contains no translation other than English.

## Assumptions

- **Format — key-based JSON, not `.resx`.** Spec 068 research R3 already chose JSON over
  `.resx`. Downloadable languages tip it further: `.resx` is compiled into satellite
  assemblies, so a file fetched at run time needs an extra reader; it has no plurals;
  its XML diffs badly. Key/value JSON with plural objects is read by Weblate and Crowdin
  as is. The current parser is kept and adapted rather than replaced (confirmed in
  Clarifications).
- **Delivery in three pull requests** (confirmed in Clarifications): (1) nota — keys and
  the English reference, French still embedded, no user-visible change (story 1);
  (2) nota-translations — README, licence, checks, language list, French, the first
  English reference (story 2); (3) nota — downloading, the Language row, local files,
  removing the embedded French, publishing the reference from the release workflow
  (stories 3–5). Pull requests 2 and 3 ship in the same release, so no release lacks
  French.
- **Versions** (confirmed in Clarifications): the release workflow publishes the English
  reference and its key-set hash (FR-010). The app asks for the newest language version
  that fits its key-set hash; if none does, the newest one checked against the nearest
  older reference (unknown keys ignored, new keys in English).
- **Download source**: the app reads the language list and files over HTTPS from GitHub
  (raw files or release assets of `nota-translations`), like the existing update check
  reads GitHub releases. Integrity is a SHA-256 checksum in the list; signing is out of
  scope.
- The user data directory is the existing Nota data root (`NOTA_DATA_DIR` in tests), under
  a `languages` folder.
- Key naming is generated by the migration from the file/area and the English text, then
  reviewed; renaming keys later is a normal change (it does orphan translations, which the
  checks report).
- `nota-docs` (EN + RU) gets a short page on choosing and previewing a language; the
  translator guide lives in the repository README.
- Out of scope: new languages, number culture (spec 068 R4 stays), right-to-left layout,
  switching language without a restart, a translation platform hook-up, signing.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — managed UI text only; the engine is untouched
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no crossing
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — localization checks move to keys; download/install covered by smoke checks on a local fixture
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language) — the Language row grows (list, coverage, download/update/remove, load local file)
