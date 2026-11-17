# Research: Automate the Nota Rhythm pattern bank

## R1 — Why a plain param is not enough

`Engine::applyAutomation` evaluates every lane once, at the block's start beat
(`Engine_Render.cpp` → `mixGraph`). A lane that changes A→B exactly on a bar line reaches the
block that contains the downbeat with the value from *before* the bar, so the downbeat step
plays the old bank. Real-time blocks (the device buffer) and export blocks differ, so the
error differs too — the issue's "offline render matches real time" would fail.

- **Decision**: the instrument reads the lane at each step's grid position.
- **Rationale**: exact and block-size independent; the lane is already in the immutable Graph.
- **Alternatives rejected**:
  - *Split the block at every breakpoint*: costs every track and plug-in, not just the two
    sequencers; still inexact for steps (breakpoints need not sit on steps).
  - *Evaluate at the block end*: right for on-grid points, wrong for points just after a step,
    and still block-size dependent.
  - *Keep a pointer to the lane in the instrument*: a skipped render (sleep, freeze) could leave
    it pointing into a retired Graph. The hook samples the lane during the call and keeps
    only numbers.

## R2 — Hook shape

`virtual void pluginParamLane(int32_t index, const AutomationLane& lane, double beatFrom, double beatTo)`,
called by `applyAutomation` right after `pluginParamSet` for instrument plugin-param lanes
(track-level and nested in a rack chain). `beatTo = (blockStart + frames) / spb`. The default is
a no-op, so plug-ins and the other built-ins pay one virtual call per such lane.

`ParamLaneSteps` (header-only) samples `lane.valueAt(k · stepBeats)` for every step `k` whose
grid beat lies in `[beatFrom − stepBeats, beatTo)` — one step earlier than the block, because a
swung step triggers after its grid position and may fall in the next block. ≤ 32 entries; steps
past that fall back to the param value.

## R3 — Switch timing

- **Decision**: param `bank_switch`, Step (default) / Pattern.
- **Rationale**: Step is today's behaviour of the A–D buttons, so nothing saved changes. Pattern
  is how Nota Twin switches sequencer patterns (queued to the pattern end) and keeps a bar whole
  when a breakpoint is drawn late. Both read the lane at the step, so on-grid lanes play the
  same in either mode.
- A step that does not follow the previous one (transport start, seek, loop wrap) takes the
  bank at once in both modes — there is no "current pattern" to finish.

## R4 — Edit bank vs playing bank

The `bank` param is the bank the card edits and the one that will play. The *playing* bank is
audio-thread state (scope `S_Bank`), equal to the param except in Pattern mode while a change is
queued. Step edits (`A_ToggleStep` …) act on the param's bank, as the card shows it.

## R5 — Persistence

RTH4 = RTH3 + 2 params. The bank byte after the params stays (written = the param's bank) so
older readers and the C# parser still find it. Loading RTH1–RTH3 sets `bank` from that byte and
`bank_switch` to Step.

## R6 — Where else

Every built-in device's `action()` channel was surveyed (only Rhythm and Twin implement one).
Twin's sequencer Pattern is already a param (`GSeqPattern`), so it is automatable, but
`fireStep` reads it from the block-start value; a lane change on a bar line that ends the
playing pattern is missed by one step and queued a whole pattern late. It adopts the same
hook. Rack selectors (`RackSelector`), Shutter's pattern steps and the Arpeggiator's mode are
params read continuously, without a step-quantized switch, so block-rate is right for them.

## R7 — Out of scope

Morphing between banks (issue part 2) — needs its own design; can be proposed as a new issue.
