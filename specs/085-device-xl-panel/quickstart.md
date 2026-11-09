# Quickstart: validating the XL panel

## Prerequisites

- An engine built in Release (`build/`), and the managed solution built.
- `scripts/build.sh` green (the smoke test included).

## 1. Engine contract (smoke test)

`scripts/build.sh` runs `tests/Nota.SmokeTest`. The XL check (see
[contracts/ui-contract.md](contracts/ui-contract.md) § Engine param contract) passes when:

- EQ-8 and Dynamic EQ-8 report `View` max 2, store 2, and read back 2;
- another device with a View (EQ-3) still reports max 1;
- applying a preset to an EQ-8 in XL leaves `View` at 2.

## 2. Panel behaviour (headless harness)

A throwaway headless Avalonia harness (see the Nota UI verify recipe: `DeviceChainView` in
a `Window`, `UseHeadless`, `CaptureRenderedFrame`) on an audio track with EQ-8 and
Dynamic EQ-8:

| Step | Expected |
|------|----------|
| EQ-8 `View = 2`, `view.Show(track)` | Panel row above the chain hosts EQ-8, the chain card is 260 wide, `PreferredHeight` = 272 + 460 + 6 |
| Pick XL on Dynamic EQ-8 through the toggle | EQ-8 `View` = 0, Dynamic EQ-8 `View` = 2, panel hosts Dynamic EQ-8 |
| Close key on the panel | Dynamic EQ-8 `View` = 0, no panel, `PreferredHeight` = 272 |
| XL on track A, `Show(B)` (no XL), `Show(A)` | Panel hidden on B and back on A, sizes unchanged |
| Duplicate the XL device | The copy is L |
| Remove the XL device | No panel |

Screens: the panel in Studio Dark and Studio Light at 1440 × 900, and EQ-8's graph
measured against the L card (SC-001: at least twice as wide and tall).

## 3. Persistence

Save a project with EQ-8 in XL, reopen it, and select the track: the panel shows EQ-8
(SC-004).

## 4. Real app spot check

Run the app, add EQ-8, and pick XL. The Devices panel grows to fit the panel. Drag a
band and the sound follows. Esc closes the panel.
