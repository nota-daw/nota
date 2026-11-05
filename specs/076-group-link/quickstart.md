# Quickstart: validating Linked editing on group tracks

## Prerequisites

- macOS build: `scripts/build.sh` (builds the engine in `build/` Release, the app, and runs
  the smoke test).

## 1. Automated

```bash
scripts/build.sh
```

Expected: the smoke test passes, including the `GroupLinkTests` checks —
the ABI table in [contracts/c-abi.md](contracts/c-abi.md), the link-set and match rules
([data-model.md](data-model.md)), and linked arming against the real engine.

## 2. In the app (manual or the headless capture harness)

Setup: two audio tracks *Dry* and *Wet*, each with a clip at bars 1–5 (record both, or
drop one sample on both), grouped with ⌘G. A third audio track *Other* outside the group.

| # | Action | Expected |
|---|--------|----------|
| 1 | Click the group's Link key | Key turns accent; Dry and Wet headers show the link glyph |
| 2 | Click Dry's clip | Wet's clip is selected too |
| 3 | Move the playhead to bar 3, ⌘E | Both clips split at bar 3; one ⌘Z joins both back |
| 4 | Drag Dry's second half one bar later | Wet's second half follows |
| 5 | Trim Dry's first half end by a beat | Wet's first half gets the same end |
| 6 | ⌥-click Wet's first half, Delete | Only Wet's clip is deleted |
| 7 | ⇧-drag a range over Dry only, Delete | The range is removed on Dry and Wet |
| 8 | Arm *Other*, then click Dry's arm | Dry and Wet armed, Other disarmed |
| 9 | ⌥-click Wet's arm | Only Wet disarms |
| 10 | Save, close, reopen | Link still on |
| 11 | Turn Link off, click Dry's clip | Only Dry's clip is selected (today's behaviour) |
| 12 | Drag Dry's clip down onto *Other* | Dry's clip changes track; Wet's clip takes the same time shift and stays on Wet |
