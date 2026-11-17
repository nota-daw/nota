# Nota Rhythm: morph between two pattern banks

## Request

Follow-up to #609 (part 2, split out there). The user who asked for bank automation also wanted
"a ramp that morphs from one pattern to another". Since #653 the bank is an automatable param
(*Perform › Bank*) read at each step, so a lane can *switch* banks; it cannot *blend* them. Steps
are on/off, so a morph has to be defined first.

## Proposal

A new param **Bank Morph** (0..1) and a **Morph To** bank (A–D): at 0 the steps play the Bank
param's bank as today, at 1 they play the Morph To bank, in between the two are blended. Two
candidate blends — the decision this issue asks for:

1. **Probability blend.** At 0.3, each step plays from the target bank with 30 % probability.
   Each step's coin is seeded by (bar, step), so a pass repeats exactly and an export matches
   playback; raising the morph only adds target steps, never reshuffles the ones already taken.
2. **Velocity crossfade.** Steps only in the source fade down, steps only in the target fade up,
   steps in both keep their velocity and take the target's accent past 0.5. Fully deterministic,
   but at mid values both patterns sound at once (a busier, softer groove), not an in-between one.

Suggestion: offer both as a **Morph Mode** (Chance / Fade). The default is open.

Either way:
- The morph is read at each step through the same lane hook as Bank (`Instrument::pluginParamLane`),
  so a ramp lane gives the same result in playback and export at any block size.
- The card shows the morph next to the bank segment, and the step strip shows which steps will
  play at the current value (Chance: the seeded pick for the bar under the playhead).
- Bank Switch keeps applying to Bank; the morph applies from the next step.

## Acceptance

- A Rhythm track offers *Bank Morph* (and *Morph To*, *Morph Mode*) in its automation menu.
- A lane ramping 0 → 1 over 8 bars moves from bank A to bank B step by step; two renders and an
  export are identical.
- Morph 0 plays exactly as today; projects saved before open with morph 0.
- The MCP Rhythm guide and `get_rhythm` describe the morph.
