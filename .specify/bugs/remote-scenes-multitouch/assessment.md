# Bug Assessment: Nota Remote (iOS) can't launch several Session clips at once

- **Slug**: remote-scenes-multitouch
- **Created**: 2026-11-05
- **Source**: https://github.com/nota-daw/nota/issues/512 (host: github.com — allowlisted; read with `gh issue view`)
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

Issue #512, "Nota Remote (iOS): can't launch several Session clips at once with multi-touch":
in Nota Remote on an iPhone/iPad, the Scenes screen (the Session grid) only takes one clip at a
time. Touching two or more clips together launches none of them, or only one. Expected: every
clip touched launches, as Pads and Keys already take several fingers at once. While fixing, a
finger that drags to scroll the grid must not launch the clip it started on.

## Symptom

Two fingers landing on two clips on the Scenes screen send at most one `launch` message to Nota
(often none); one finger at a time works. Expected: one `launch` per clip touched.

## Reproduction

1. A project with Session clips on at least two tracks.
2. Pair Nota Remote from iOS Safari (or the home-screen app), open **Scenes**.
3. Touch two clips on different tracks at the same time and lift.
4. Nota queues one clip or none.

## Suspected Code Paths

- `src/web/remote/src/screens/Scenes.svelte` — the clip buttons (and the scene, Stop and
  Stop-all buttons) act in `onclick`. Nothing else on the screen handles pointers.
- `src/web/remote/src/app.css:32` — `button { touch-action: manipulation }`, and
  `Scenes.svelte` `.scroll { touch-action: pan-x pan-y }`: the grid is a native scroller, so
  touches stay under the browser's gesture recogniser.
- For contrast, `src/web/remote/src/screens/Pads.svelte:38` (`press` on `onpointerdown`, a
  pointer → note map) and `Keys.svelte` track each pointer themselves, which is why they work
  with several fingers.
- Server side is not involved: `src/managed/Nota.Remote/RemoteHub.Apply.cs:128` applies every
  `launch` / `scene` / `stopSlot` message on its own, with no debounce.

## Root Cause Hypothesis

WebKit on iOS synthesises a `click` only for a single-finger tap. When a second finger is down,
the touch sequence is a multi-finger gesture and no `click` is dispatched for either finger (or
only for the one that lifts last, once alone). Since the Scenes buttons only react to `click`,
simultaneous touches are lost. Confidence: high — it is documented WebKit behaviour and matches
the contrast with Pads/Keys, which use Pointer Events.

## Proposed Remediation

**Preferred**: give the Scenes buttons their own per-pointer tap handling. A small shared Svelte
action (`src/web/remote/src/lib/tap.ts`, `use:tap={fn}`) tracks each `pointerdown` by
`pointerId` with its start point, and fires `fn` on that pointer's `pointerup` if the pointer
stayed within a small slop (~10 px) and was not cancelled. A pointer that starts a scroll gets
`pointercancel` from the browser (the grid keeps `touch-action: pan-x pan-y`) or moves past the
slop, so it does not fire. Firing on release keeps scrolling safe; Session launches are
quantised, so clips released a few milliseconds apart still start together. `click` stays as the
path for keyboard and assistive-technology activation, ignored when a pointer has just fired the
same button, so a single tap never launches twice.

**Alternatives**:
- Fire on `pointerdown` (as Pads do): instant, but any scroll that starts on a clip would launch
  it, and the grid scrolls on phones with more than a few tracks. Rejected.
- Use `touchstart`/`touchend` directly: iOS-only semantics; Pointer Events already cover iOS
  13+ and the other browsers. Rejected.

**Files likely to change**:
- `src/web/remote/src/lib/tap.ts` (new)
- `src/web/remote/src/screens/Scenes.svelte`
- `CHANGELOG.md` (Fixed)

**Tests to add or update**:
- The remote page has no unit-test harness; verify by driving the built page against the mock
  server with two simultaneous touch points (Chromium touch emulation via CDP, and iOS Safari in
  the Simulator with a two-finger touch), checking one `launch` per touched clip, and that a
  drag-to-scroll starting on a clip sends nothing. `svelte-check` and `scripts/build.sh` must
  pass.

## Risks & Considerations

- Double fire: a single-finger tap produces both `pointerup` and `click`; the guard must
  suppress the click that follows a pointer fire.
- VoiceOver activation sends a `click` without a matching pointer sequence — it must still work
  (it does, through the click path).
- A held finger that slides off a button and lifts elsewhere must not fire (use the slop and
  check the release point is still within the button).
- Applies to the scene, Stop and Stop-all buttons too, for consistency — same screen, same
  gesture.

## Open Questions

- None blocking.
