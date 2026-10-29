# Feature Specification: Plug-in card shows a preview of the plug-in's interface

**Feature Branch**: `046-plugin-card-preview`

**Created**: 2026-10-28

**Status**: Implemented

**Input**: User description: "Plug-in card: show a preview of the plug-in's interface (GitHub issue nota-daw/nota#270). A plug-in device card currently shows only text: name, vendor, format, params, latency and the \"Open editor\" button. Expected: the plug-in card shows a thumbnail of the plug-in's editor, captured the last time the editor window was open (or rendered offscreen once when the plug-in is added, where the format allows). Clicking the thumbnail opens the editor (see #258). The thumbnail is cached per plug-in instance in the project and refreshed when the editor closes; a neutral placeholder with the plug-in name is shown if there is no capture yet. Respect the card sizes (S/M/L): no thumbnail on S. Hint: src/managed/Nota.App/DeviceCards/PluginCard.cs. Snapshotting the native editor window is per-platform (NSView/HWND/X11); do it off the audio thread."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [x] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Today the hosted plug-in card (`src/managed/Nota.App/DeviceCards/PluginCard.cs`) has two
sizes, not three: **S** 260 × 260 (info column + "Open editor" / "Save preset") and
**L** 700 × 260 (the same info column, a paged knob grid of every parameter, and the
sidechain source when the plug-in has one). The editor window is a JUCE
`DocumentWindow` owned by `EditorHost` in
`src/native/nota.engine/pluginhost/hosting/PluginEditorWindow.{h,cpp}`; its close button
only hides it. The "Open editor" button already toggles and reports failures (#258).
Capturing the window's pixels is per-platform work inside `pluginhost/` on the message
thread; the image crosses into the app through the C ABI.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See what the plug-in looks like without opening it (Priority: P1)

A producer has several third-party plug-ins on a track. Opening the device chain, each
plug-in card shows, as its background, a darkened picture of that plug-in's own
interface as it looked the last time its window was open, so the producer recognises the
plug-in at a glance instead of opening every window. The card's text and buttons stay
on top and stay readable.

**Why this priority**: This is the whole point of the issue — the card becomes
recognisable. Everything else builds on having a captured picture to show.

**Independent Test**: Add a plug-in with an editor, open its window, change a visible
control, close the window: the card's background shows a picture matching the window as
it was when closed, darkened so the card's text reads over it.

**Acceptance Scenarios**:

1. **Given** a plug-in whose editor window has been opened and closed in this session,
   **When** its card is shown at size S, **Then** the card's background is the editor
   picture as it looked at the moment it closed, scaled to cover the card without
   stretching (cropped at the edges as needed), under a theme-coloured gradient that
   darkens it so the name, facts and buttons stay readable.
2. **Given** a card already showing a preview, **When** the user opens the editor,
   changes something visible and closes it again, **Then** the background updates to the
   new look without the user doing anything else.
3. **Given** the app theme is switched (e.g. Studio Dark → Studio Light), **When** the
   card is shown, **Then** the gradient over the preview follows the new theme's card
   colours and the text stays readable.
4. **Given** the card at size L, **When** it is shown, **Then** the same preview
   background sits behind the info column (the left section with the name and
   buttons); the parameter and sidechain sections are unchanged.

---

### User Story 2 - The preview survives save and reopen (Priority: P1)

The producer saves the project, quits, and reopens it the next day. Each plug-in card
shows the same preview it showed before, without any plug-in window having been opened
in the new session.

**Why this priority**: Without persistence the preview is empty on every project open
until each window is opened once — which defeats the "without opening every window"
goal.

**Independent Test**: Capture a preview (Story 1), save, close the project, reopen it:
the card shows the same picture.

**Acceptance Scenarios**:

1. **Given** a plug-in with a captured preview, **When** the project is saved and
   reopened, **Then** the card shows that preview.
2. **Given** two instances of the same plug-in on different tracks with different
   settings, **When** each has been captured, **Then** each card shows its own preview.
3. **Given** a plug-in with a preview, **When** it is duplicated or copied/pasted,
   **Then** the copy shows the same preview until its own window is closed once.
4. **Given** a project saved by an older Nota version (no previews), **When** it is
   opened, **Then** it loads normally and cards look as they do today.

---

### User Story 3 - No preview yet: the card looks as it does today (Priority: P2)

A plug-in that has never had its window open shows the card exactly as today — the
plain card ground with its name, vendor, facts and buttons. The card already names the
plug-in, so no separate placeholder is needed.

**Why this priority**: A clear, stable empty state; cheap.

**Independent Test**: Add a plug-in and view its card without opening the window: the
card looks as it does today.

**Acceptance Scenarios**:

1. **Given** a plug-in with no captured preview, **When** its card is shown, **Then**
   the card shows its normal ground, unchanged from today.
2. **Given** a plug-in whose capture failed (e.g. the platform returned no pixels),
   **When** the card is shown, **Then** the plain card (or the previous good preview,
   if any) is shown — never a black or garbage image.

---

### User Story 4 - A preview right after adding the plug-in (Priority: P3)

Where the plug-in format and platform allow capturing the editor without showing it on
screen, Nota captures a first preview once when the plug-in is added, so even a fresh
plug-in has a picture.

**Why this priority**: Nice to have; best-effort and platform-dependent. Stories 1–3
deliver the feature without it.

**Independent Test**: On a platform/format where offscreen capture is supported, add a
plug-in and view its card without opening its window: a preview background appears.

**Acceptance Scenarios**:

1. **Given** offscreen capture is supported, **When** a plug-in is added, **Then** a
   preview appears on its card within a few seconds, and no window flashes on screen.
2. **Given** offscreen capture is not supported or fails, **When** a plug-in is added,
   **Then** the card stays plain and nothing else changes.

---

### Edge Cases

- Editor uses GPU-rendered content (OpenGL/Metal): the capture may come back blank —
  detect a blank/uniform capture and keep the previous preview or the plain card.
- The plug-in has no editor of its own (Nota shows the generic parameter editor): the
  generic editor is captured like any other window.
- The window is closed while partly off-screen, minimised, or covered by another
  window: the capture reflects the editor's own content, not what overlaps it; if that
  cannot be guaranteed on a platform, a capture of a minimised window is skipped.
- The editor's aspect ratio differs a lot from the card's (a very wide or very tall
  window): the picture still covers the card without stretching; the crop keeps the top
  of the editor (where plug-ins usually put their logo and header) and centres
  horizontally.
- The editor is smaller than the card at its display scale: the picture is scaled up to
  cover; slight softness is acceptable (it sits under a darkening gradient), distortion
  is not.
- The plug-in is removed while a capture is in progress: the capture is discarded.
- The plug-in is replaced by a different plug-in in the same slot: the old preview is
  dropped and the card is plain.
- The project is closed (or the app quits) with editor windows open: whether a final
  capture is taken is best-effort; it must not delay quitting noticeably.
- Many plug-ins in a project: previews must not noticeably increase project open time
  or project file size (see SC-003, SC-004).
- Missing plug-in (not installed on this machine): the device isn't recreated (an
  instrument becomes a stand-in Volt track), so there is no card to show the preview on;
  it isn't put onto the stand-in.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a plug-in instance has a preview, its card MUST use it as the card's
  background at size S, and behind the info column at size L; the card's layout,
  sizes, text and buttons MUST NOT change.
- **FR-002**: The preview MUST be scaled uniformly to cover its area (aspect ratio
  preserved, never stretched), cropped where it overflows, anchored to the top and
  centred horizontally.
- **FR-003**: A gradient in the active theme's card colours MUST lie over the preview,
  darkening it enough that all card text and controls keep their normal legibility; it
  MUST follow theme switches.
- **FR-004**: When a plug-in's editor window is closed (by the card button or the
  window's own close button), the system MUST capture the editor's current look and make
  it that plug-in instance's preview.
- **FR-005**: Capture MUST NOT run on the audio thread and MUST NOT cause audible
  dropouts; capture work that is slow (scaling, encoding) MUST NOT block the UI.
- **FR-006**: The stored preview MUST be a downscaled image, sized for the card (a
  bounded maximum resolution suitable for high-DPI displays), not a full-size copy of
  the window.
- **FR-007**: The preview MUST be saved with the project per plug-in instance and
  restored when the project opens; projects without previews MUST load unchanged.
- **FR-008**: Duplicating, copying/pasting, or grouping a plug-in device into a rack
  MUST carry its preview along; replacing the plug-in MUST drop it.
- **FR-009**: With no preview (never captured, capture failed, or blank capture), the
  card MUST look as it does today.
- **FR-010**: A blank or failed capture MUST NOT replace an existing good preview.
- **FR-011**: The preview is decoration only: it MUST NOT add a click target; opening
  the editor stays on the "Open editor" button.
- **FR-012**: Where the platform and plug-in format allow capturing without showing a
  window, the system SHOULD capture one preview when the plug-in is added; where it does
  not, nothing visible happens.
- **FR-013**: The preview treatment MUST follow the Studio design language
  (`DESIGN.md`): the card keeps its radius and hairline; the gradient uses neutral theme
  colours, not the accent.
- **FR-014**: Preview capture MUST be supported on macOS; Windows and Linux MUST at least
  degrade to the plain card without errors (capture on those platforms where feasible).

### Key Entities

- **Plug-in preview**: a small image of one plug-in instance's editor, plus its pixel
  size; belongs to exactly one plug-in device in the project; replaced on each
  successful capture; saved and loaded with the project.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After closing a plug-in's window, its card shows the updated preview
  within 1 second.
- **SC-002**: Capturing a preview causes no audio dropouts (no xruns reported) during
  playback with plug-ins running.
- **SC-003**: Each stored preview adds no more than about 100 KB to the project file.
- **SC-004**: Opening a project with 20 plug-ins that have previews takes no more than
  10 % longer than the same project without previews.
- **SC-005**: Users can identify which plug-in is on a card from the preview alone
  (preview visibly matches the plug-in's window) for every plug-in captured on macOS.
- **SC-006**: In every built-in theme, all card text over a preview stays as legible as
  on the plain card (no text lost against bright plug-in interfaces).
- **SC-007**: No preview ever appears stretched or squashed, for any editor size.

## Assumptions

- The card has two sizes today (S and L), not S/M/L as the issue states. Per the
  clarification the preview becomes the S card's background (and the L card's info
  column background) instead of a separate thumbnail; no new card size is added.
- Capture happens on window close only (as the issue states), not continuously while
  the window is open.
- macOS is the primary capture platform (it is the main development and user platform);
  Windows/Linux capture is in scope where straightforward, otherwise the plain card.
- Offscreen capture on add (Story 5) is best-effort and may be deferred if no reliable
  platform mechanism exists; it must never show a window on screen.
- The preview is cosmetic: losing or failing to decode it never affects the plug-in's
  sound or state.
- No new third-party dependency is needed (image scaling/encoding is available through
  JUCE in `pluginhost/` or the app's existing UI toolkit).

## Clarifications

### Session 2026-10-28

- Q: Where does the preview live on the card (L only, a new M size, or inside L's info
  column)? → A: Keep the S card as is and use the plug-in's screenshot as the card's
  background, darkened by a theme-matched gradient. No clickable thumbnail — the
  "Open editor" button already opens the window. The screenshot must never look
  stretched. (Applied: FR-001…FR-003, FR-011; the issue's "click the thumbnail" and
  "placeholder with the name" items are dropped.)

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
