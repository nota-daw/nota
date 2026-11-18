# Feature Specification: Nota Remote — Pads Repeat: slide the finger to change the rate and the pitch

**Feature Branch**: `100-remote-pads-repeat-slide`

**Created**: 2026-11-16

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#558: "Play note repeat on the Pads screen like on a hardware
pad controller — rolls and fills where the rate and the pitch change while the finger is down.
[…] While Repeat is on and a pad is held, moving the finger changes the repeat: Rate — moving the
finger (e.g. up/down on the pad) steps the repeat rate through the divisions (1/4 … 1/32, with
triplets), in time with the project tempo; the next repeat follows the new rate without a gap or
double hit. Pitch — moving the finger sideways bends the repeated note's pitch, like sliding on
Keys does (per-note expression, so other held pads are not bent); it returns to centre when the
finger lifts. The pad shows the current rate (and bend) under the finger […]. Velocity still comes
from where the finger lands, or full velocity when that is on; several fingers each keep their own
rate and pitch. Without Repeat, Pads behave as now."

## Nota Context

**Feature area**:

- [ ] Engine (C++, `src/native/nota.engine`)
- [ ] Managed app (C#, `src/managed/*`)
- [x] UI — Nota Remote's phone page (`src/web/remote`), follows `DESIGN.md` as spec 090 maps it
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`DESIGN.md`](../../DESIGN.md), [`FEATURES.md`](../../FEATURES.md),
[`specs/014-remote-studio`](../014-remote-studio/spec.md), [`specs/090-remote-new-look`](../090-remote-new-look/spec.md).

**Existing behaviour this builds on**: Pads' Repeat retriggers a held pad every 1/8, 1/16 or 1/32
(a tap on the rate chip picks it before playing), one timer chain per note. Keys' Expressive mode
bends a held note with a sideways slide through the `x` message (per-note expression), which only
an instrument that takes MPE answers (`track.mpe`). A note-on resets the note's expression in the
engine (`nota_engine.h`), so a bend has to follow each new note-on.

## User Scenarios & Testing

### User Story 1 - Change the repeat rate with the finger down (Priority: P1)

A drummer holds the snare pad with Repeat on at 1/16 and slides the finger up for a 1/32 roll,
then down again for 1/8 — without lifting.

**Independent Test**: hold a pad with Repeat on; slide up half a pad's height per step: each
step is the next faster rate; slide down: slower; the hits keep coming without a gap or a double.

**Acceptance Scenarios**:

1. **Given** Repeat on at 1/16, **When** a pad is held and the finger slides up, **Then** every
   half a pad's height steps the rate one division faster (1/16T, 1/32, 1/32T) and down one
   slower (1/8T, 1/8, 1/4T, 1/4), stopping at either end.
2. **When** the rate changes, **Then** the next hit lands one step of the new rate after the last
   hit (at once if that moment has passed) — no gap and no double hit.
3. **Given** the finger slides back to where it landed, **Then** the rate is the one it started at.
4. **Then** the rate follows the project tempo, as before.

### User Story 2 - Bend the repeated note (Priority: P2)

On a track whose instrument takes per-note expression, a finger sliding sideways bends the notes
it repeats; another held pad is not bent.

**Acceptance Scenarios**:

1. **Given** an MPE track and Repeat on, **When** a held pad's finger slides sideways, **Then** the
   repeated note bends — a pad's width is a semitone, as the pads run, with the same small dead
   zone as Keys — and every later hit sounds at that bend from its start.
2. **When** the finger lifts, **Then** the note ends; the next press starts unbent.
3. **Given** a track whose instrument doesn't take MPE, **Then** a sideways slide does nothing.

### User Story 3 - See what the finger does (Priority: P2)

**Acceptance Scenarios**:

1. **Given** a pad repeating, **Then** it shows its current rate, and the bend when there is one
   (`1/32`, `+1.0 st`).
2. **Given** Repeat on, **Then** a hint under the pads says what a slide does (rate; and bend on a
   track that bends).

### Edge Cases

- **Several fingers**: each held pad keeps its own rate and bend; two fingers on one pad share
  that pad's one chain, either can slide it.
- **Sliding off the pad**: with Repeat on, the pad keeps the finger, so a long slide keeps playing;
  without Repeat a finger sliding off a pad lets it go, as before.
- **Velocity**: from where the finger landed, or full with Full velocity — a slide does not change it.
- **The bank changes or the screen closes** mid-roll: every roll stops and its note ends, as before.
- **A browser that holds a timer back** (a background tab): the roll goes on from now instead of
  catching up with a burst of hits.

## Requirements

### Functional Requirements

- **FR-001**: The repeat rates MUST be 1/4, 1/4T, 1/8, 1/8T, 1/16, 1/16T, 1/32 and 1/32T; the chip
  picks the rate a press starts at (1/16 by default).
- **FR-002**: With Repeat on, a held finger's vertical slide MUST step its pad's rate (up faster),
  half a pad's height per step, counted from where it landed.
- **FR-003**: A rate change MUST re-time the next hit from the last one, firing at once (once) if
  that time has passed.
- **FR-004**: On an MPE track, a held finger's sideways slide MUST bend its note (a pad's width per
  semitone), re-sent right after every repeated note-on.
- **FR-005**: A repeating pad MUST show its rate and bend; a hint MUST explain the slide while
  Repeat is on.
- **FR-006**: Without Repeat, Pads MUST behave as before.

## Success Criteria

- **SC-001**: A roll that speeds up, slows down and bends is played with one finger, without
  lifting it.

## Assumptions

- Up is faster, as on hardware pads with a rate strip; the rate a press starts at is the chip's.
- The bend is the existing per-note expression, so it applies only where it sounds (an
  instrument that takes MPE), like Keys' Expressive mode.
- No protocol change: the page sends the same `on` / `off` / `x` messages Nota already takes.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header (no new source files)
- [x] No new dependency
- [x] Audio-thread impact: none — the phone page only
- [x] No new managed/native crossings; no Remote protocol change
- [x] Smoke-test impact: none new — the phone page's design checks (`DesignTokenCheck.Remote`) cover the new styles
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] UI change → follows `DESIGN.md` as spec 090 maps it (theme tokens, sentence case, the type scale)
