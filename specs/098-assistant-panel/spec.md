# Feature Specification: Assistant panel

**Feature Branch**: `feat/assistant-panel`

**Created**: 2026-11-14

**Status**: Draft

## Clarifications

### Session 2026-11-14

- Q: Which manual pages get Show me anchors in this feature? → A: every how-to in the manual (EN + RU), not a starter set.
- Q: Is meaning-based search (the optional model in the AI model store) part of this feature, or words-only now and the model moved to #607? → A: part of this feature — words-only search always works, the meaning-based model is an optional download in the AI model store.
- Q: Does the manual index update only with a Nota release, or also download between releases? → A: only with a release — the index is built into the app and changes with the Nota version.
- Q: How tall is the Assistant panel on the right? → A: the full height of the work area (beside the main view, the docked Mixer and the Detail panel); the Mixer and Detail panel narrow to make room.
- Q: Are Show me anchors written once and shared by every language, or per language? → A: shared — written in the English page; each translation repeats the same anchors in the same order, and the build fails on a mismatch.

**Input**: Issue [#606](https://github.com/nota-daw/nota/issues/606) (approved) "Assistant
panel: a chat on the right that finds answers in the manual and shows where they are in the
app": a chat docked on the right of the main window, opened by a toolbar button and a
bindable shortcut; questions in the user's own words (English or Russian) are answered from
the user manual, replies render as proper Markdown, and **Show me** highlights the controls a
how-to describes, one step at a time. Works offline and without any language model. A
language model that writes answers and acts in Nota through the MCP tools is
[#607](https://github.com/nota-daw/nota/issues/607) and out of scope here; this panel is the
base it builds on.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [x] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md` *(the manual index ships with the app)*
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Where things stand today**: the manual (nota-daw/nota-docs, ~106 pages in English with a
Russian translation, French where translated) is only reachable in a web browser — Help ▸
Documentation opens its home page and each device card's Documentation item opens that
device's page, in the interface language (`NotaDocs`, `ManualLanguage`). Nothing in the app
searches it, and nothing connects a manual page to the controls it talks about. The main
window has the browser on the left, the docked Mixer and the Detail panel at the bottom, and
nothing docked on the right. The AI model store (Separate Stems, Convert to MIDI) already
downloads ONNX Runtime and pinned models on demand. The manual keeps a coverage map
(`coverage.yml`) linking each page to the app sources it describes, and every keyboard
action lives in the key-action catalog (spec 096).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask a question, get the manual's answer in the app (Priority: P1)

I want to sidechain the bass to the kick but don't know where that lives. I open the
Assistant panel from the toolbar, type "how do I duck the bass when the kick hits" and get a
reply right in the panel: the matching manual section, quoted short and readable, with a
link to the full page. I didn't leave Nota and didn't need the internet.

**Why this priority**: this is the core of the issue and works on every computer; Show me
and the model (#607) build on it.

**Independent Test**: with the network off and no AI model installed, open the panel, ask
ten questions taken from the manual's how-to topics in each language, and check the right
section is among the answers.

**Acceptance Scenarios**:

1. **Given** the main window is open, **When** I click the Assistant toolbar button (or press
   its shortcut), **Then** the panel opens docked on the right, full height beside the main
   view, the Mixer and the Detail panel, with the
   input focused; pressing the button or shortcut again closes it.
2. **Given** the panel is open, **When** I type a question and press Return, **Then** my
   message and a reply appear in the conversation within 1 second, the reply quoting the
   2–3 best-matching manual sections with their page titles and a link to each.
3. **Given** the interface is in Russian, **When** I ask in Russian, **Then** the reply
   quotes the Russian manual; asking in English while the interface is Russian answers from
   the English manual.
4. **Given** the computer is offline, **When** I ask, **Then** the reply still appears
   (the manual's text ships with the app); only opening a page in the web browser needs the
   network.
5. **Given** nothing in the manual matches, **When** I ask, **Then** the reply says so
   plainly and offers to open the manual's home page or search page.

---

### User Story 2 - Replies that read like the manual (Priority: P1)

Replies and quoted sections render as formatted text — headings, lists, bold and italic,
inline code, code blocks, tables, links and keyboard keys — in the app's own look, not as raw
Markdown symbols.

**Why this priority**: manual sections are Markdown; shown raw they are hard to read, and
#607's model replies are Markdown too.

**Independent Test**: feed the panel a reply containing every supported element and compare
the result with the style guide in each of the four themes and the classic look.

**Acceptance Scenarios**:

1. **Given** a reply with a heading, a numbered list, a table, a code block and a link,
   **When** it shows, **Then** each renders as that element with the style guide's type,
   colours and spacing, in the current theme.
2. **Given** a reply, **When** I select text and copy, **Then** the plain text (and Markdown
   for a whole message, via the message's Copy action) goes to the clipboard.
3. **Given** a link to a manual page, **When** I click it, **Then** the page opens in the web
   browser in the interface language; a link to another site asks nothing and opens in the
   browser; an action link (Show me) runs in the app.
4. **Given** a long reply, **When** the panel is narrow, **Then** text wraps, wide tables and
   code blocks scroll sideways inside their own box, and nothing is clipped.

---

### User Story 3 - Show me where it is (Priority: P2)

Under an answer that describes steps in the app, a **Show me** button walks me through them:
the panel says step 1, the control it names is highlighted in the main window, and Next /
Back move between steps. When a step's control is in a closed panel or another view, Nota
opens that panel or switches to that view first.

**Why this priority**: it turns "read the manual" into "see it in Nota" — the issue's second
half — but needs anchors written into the manual, so it follows the search.

**Independent Test**: on a page with anchors (e.g. adding a sidechain to a Compressor),
run Show me from a fresh project and check every step highlights the right control.

**Acceptance Scenarios**:

1. **Given** a reply quoting a section that has anchored steps, **When** it shows, **Then**
   it carries a Show me button; sections without anchors have none.
2. **Given** I press Show me, **When** step 1 starts, **Then** the named control is
   outlined in the accent colour, scrolled into view if needed, and the panel shows the
   step's text with Back / Next / Done and "step 1 of N".
3. **Given** a step's control is in the Mixer while the Mixer is hidden, **When** that step
   starts, **Then** Nota shows the Mixer, then highlights the control.
4. **Given** a step's control needs a selection that doesn't exist (e.g. "the Compressor on
   the selected track" with no Compressor), **When** that step starts, **Then** the panel
   says what to do first (in the manual's words) and highlights where to do it, instead of
   failing silently.
5. **Given** a walkthrough is running, **When** I click the highlighted control and do the
   step, **Then** nothing in the walkthrough blocks it; Next is mine to press.
6. **Given** a walkthrough is running, **When** I press Escape or Done, or close the panel,
   **Then** the highlight disappears and nothing else changes.

---

### User Story 4 - The panel fits the way I work (Priority: P3)

The panel keeps its width, remembers whether it was open, follows the theme and the
interface language, and its shortcut can be rebound like any other.

**Independent Test**: resize, restart, rebind, switch theme and language; check each sticks.

**Acceptance Scenarios**:

1. **Given** I drag the panel's edge, **When** I restart Nota, **Then** it opens at that
   width and in the same open/closed state.
2. **Given** Preferences ▸ Shortcuts, **When** I rebind Toggle Assistant, **Then** the new
   key opens the panel and the toolbar tooltip shows it.
3. **Given** the toolbar is too narrow, **When** controls shed (spec 097), **Then** the
   Assistant button moves into the ⋯ overflow menu like the other sheddable buttons.
4. **Given** a conversation, **When** I press Clear, **Then** it empties; it is never saved
   with the project and is gone after a restart.

### Edge Cases

- A question one or two words long ("sidechain") or with typos ("sidechian") still finds
  the section.
- A question naming a control by its on-screen label in the other language finds it.
- An anchor that no longer exists in this build (renamed control) — that step says the
  control could not be found and still shows its text; the build's check (FR-019) is meant
  to stop this from shipping.
- Show me while a modal window or a plug-in window is in front — the walkthrough waits or
  brings the main window forward; it never highlights behind a modal.
- Show me starts while playing or recording — it never stops the transport and never
  touches the project by itself.
- The panel open on a small screen with the browser and Detail panel open — the main view
  keeps a usable minimum width; the panel narrows to its own minimum, then overlays.
- The manual index is missing or damaged in a broken install — the panel says the manual
  isn't available and offers the online manual, and the rest of Nota is unaffected.
- A very long question (pasted text) — it is accepted and trimmed for searching.

## Requirements *(mandatory)*

### Functional Requirements

**Panel**

- **FR-001**: The main window MUST have an Assistant panel docked on the right, the full
  height of the work area (from under the toolbar to the status bar); the main view, the
  docked Mixer and the Detail panel (which today span the window's full width under the
  browser row) all narrow to make room.
  It is resizable by dragging its edge, with a minimum and maximum width.
- **FR-002**: A toolbar button and a key-action catalog action ("Toggle Assistant", default
  shortcut chosen in the plan without clashing with existing ones) MUST open and close the
  panel; the button follows the toolbar's shedding order and overflow menu (spec 097).
- **FR-003**: The panel MUST hold a conversation: the user's messages, the replies, an input
  box (Return sends, Shift+Return adds a line), and a Clear action. The conversation lives
  for the running session only and is never written to the project or to disk.
- **FR-004**: Width and open/closed state MUST persist across restarts.
- **FR-005**: The panel MUST NOT take keyboard focus away while closed or while the user
  works in the main view; typing in its input MUST NOT trigger app shortcuts or play notes
  from the computer keyboard.

**Answers from the manual**

- **FR-006**: Each Nota release MUST ship a search index of the manual matching that
  release — every page, split by section, in English and in each translated language — so
  answers work offline with no download. The index changes only with a new Nota version;
  the app never downloads a newer one, so its anchors always match the build's controls.
- **FR-007**: Search MUST work with no AI model: ranking by the words of the question,
  tolerant to word forms and small typos, in English and Russian.
- **FR-008**: When the AI model store has the manual-search model installed, search MUST
  also match by meaning (a question phrased differently from the manual still finds the
  section); without it, search MUST fall back to words-only with no error. The model is an
  optional, small download in the AI model store, pinned like the other models, and ships in
  this feature (not deferred to #607, which reuses it).
- **FR-009**: A reply MUST quote the 2–3 best sections — page title, section heading, the
  section's text shortened to a readable length — each with a link to the full page in
  the manual language that matches the question.
- **FR-010**: The language of the answer MUST follow the language of the question when that
  manual language exists, otherwise the interface language, otherwise English.
- **FR-011**: When no section matches well enough, the reply MUST say so and offer the
  online manual's home page.

**Markdown**

- **FR-012**: Replies MUST render Markdown: headings, paragraphs, bold, italic,
  strikethrough, inline code, code blocks, bulleted and numbered lists (nested), block
  quotes, tables, horizontal rules, links, and the manual's keyboard-key and callout
  (note / tip / caution) styles.
- **FR-013**: Rendering MUST follow the style guide (type scale, colours, spacing, radius)
  in all four themes and the classic look, and update live when the theme changes.
- **FR-014**: Text MUST be selectable; each message MUST have a Copy action giving its
  Markdown source.
- **FR-015**: Links MUST open by kind: manual pages in the browser in the right language,
  other web links in the browser, `nota:` action links (Show me) in the app. Images in the
  manual MAY be shown as a link to the page rather than inline.

**Show me**

- **FR-016**: The manual MUST be able to mark a how-to's steps with anchors — the on-screen
  control (by a stable identifier) or a key-action catalog action — plus the step text;
  anchors ship inside the release's manual index.
- **FR-017**: A reply quoting a section with anchored steps MUST offer Show me, which walks
  the steps one at a time: highlight the anchored control in the accent colour, scroll it
  into view, show "step k of N" with Back / Next / Done, and Escape to stop.
- **FR-018**: Before highlighting, Show me MUST make the control reachable in the same way
  the user would — switch the view, open the panel, the tab, the device card's section —
  and MUST NOT change the project, the selection, the transport or any setting beyond what
  is visible.
- **FR-019**: The build MUST check every anchor in the shipped manual index against the
  app's identifiers and action catalog and fail on an anchor that names nothing.
- **FR-019a**: Anchors MUST be the same in every language: the English page defines them and
  each translation repeats the same anchors in the same order, changing only the step text;
  the build MUST fail when a translation's anchors differ from the English page's.
- **FR-020**: Every how-to in the manual — every section that tells the user to do something
  in the app, step by step — MUST carry anchors in this feature, in English and in each
  translated language, so Show me is offered on all of them when the feature ships. A
  section that only explains (no steps in the app) carries none.

**Quality**

- **FR-021**: Searching, rendering and Show me MUST run off the audio thread and MUST NOT
  cause audio dropouts or delay app startup; the index loads the first time the panel is
  opened.
- **FR-022**: All panel text MUST be localised (English and Russian strings at least).
- **FR-023**: The panel's controls MUST be reachable by keyboard and carry accessible names.

### Key Entities

- **Manual index**: one per Nota release; the manual's sections in every language, each
  with page slug, page title, section heading, text, language and the anchored steps it
  has.
- **Section**: a heading and the text under it on one manual page; the unit search returns
  and a reply quotes.
- **Anchored step**: one step of a how-to — its text, and the control identifier or
  key-action it points at, plus what has to be opened to reach it.
- **Conversation**: the session's messages (question or reply), each reply holding the
  sections it quotes and its Show me steps.
- **Manual-search model**: optional entry in the AI model store that adds matching by meaning.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a fixed set of 40 questions written from the manual's how-to topics (20 in
  English, 20 in Russian, phrased the way users ask, not copied from headings), the right
  section is in the reply for at least 80% with words-only search and at least 90% with the
  manual-search model installed.
- **SC-002**: A reply appears within 1 second of pressing Return on the slowest supported
  machine, with or without the manual-search model.
- **SC-003**: Opening the panel for the first time in a session takes under 1 second; app
  startup time is unchanged when the panel starts closed.
- **SC-004**: Playing a busy project while asking 20 questions and running Show me causes no
  audio dropouts beyond what the same project produces with the panel closed.
- **SC-005**: Every anchored walkthrough in the shipped manual completes from a fresh project
  with each step highlighting a visible control (checked in the build and by a walkthrough
  test over all anchored pages).
- **SC-006**: The rendered sample reply (every FR-012 element) matches the style guide in all
  four themes and the classic look, checked by screenshot.
- **SC-007**: Every how-to section in the shipped manual offers Show me (checked in the build
  against the manual's list of how-to sections).

## Assumptions

- The issue's #607 (a language model writing answers, acting through MCP tools, bring your
  own model) is out of scope; this panel's conversation, rendering and search are designed so
  #607 adds a reply source without changing them.
- Anchoring every how-to is done in nota-docs alongside this feature (its own pull request
  there); the anchors land in the same release as the panel.
- No "Do it" action in this feature: Show me only shows; acting on the project is #607.
- The manual index is produced from nota-docs when the release is cut and is bundled with the
  app; it adds a few megabytes to the install. The optional manual-search model is a separate
  download in the AI model store, like Separate Stems; it runs on the ONNX Runtime the store
  already provides.
- Answers quote the manual; the panel doesn't write its own text beyond fixed phrases
  ("Here is what the manual says", "Nothing in the manual matches").
- Conversation is not saved; there is no history across sessions.
- Markdown rendering needs a parser; a permissively licensed one (Markdig, BSD-2-Clause) is
  assumed, to be confirmed in the plan.
- The panel is part of the main window only; it does not appear in pop-out windows or on
  Nota Remote.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned *(Markdig, BSD-2-Clause; the manual-search model's licence)*
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) *(no engine change; all work on the UI and background threads)*
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` *(no engine change; search and anchor checks get their own smoke flags)*
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
