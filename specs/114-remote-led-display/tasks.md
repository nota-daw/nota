# Tasks: Remote Macros as a controller surface with an LED display (spec 114)

**Input**: [spec.md](spec.md), [plan.md](plan.md), [contracts/remote-led.md](contracts/remote-led.md)

## Phase 1 — Shared rendering (blocks everything)

- [x] T001 `LedFrame` with primitives, 3 × 5 font and RLE4 in `src/managed/Nota.Application/Displays/LedFrame.cs`
- [x] T002 `DisplayParams` (by-name access, unit helpers) in `Displays/DisplayParams.cs`
- [x] T003 `DisplaySpectrum` (FFT → 64 log bands) and `DisplayContext` (taps) in `Displays/`
- [x] T004 `Widgets` drawing library in `Displays/Widgets.cs`
- [x] T005 `DisplayRenderer` (header, overlay) and `DeviceDisplays` registry with the bars fallback
- [x] T006 [P] Tests: primitives, RLE round trip, header / overlay — in the smoke test (`tests/Nota.SmokeTest/DisplayTests.cs`, `--displays`)

## Phase 2 — US1 navigation (P1)

- [x] T010 `RemoteMacros`: chain-order devices with controller pages → `RemoteParam`s; `MidiParam` kind in `RemoteParams`
- [x] T011 Hub: `mac` with `pg` / `pgs`, knobs from the page; `nav` command; follow (`foc` with `ms` / `mi`)
- [x] T012 Host: `StepTrack`, `StepDevice`, `SetDevicePage`, `DevicePages` on `IRemoteHost` + `MainWindow.Remote.cs` + `ControllerService`
- [x] T013 Web Macros: steppers, page row, 4 × 2 knobs, device sheet keeps working
- [x] T014 [P] Tests: nav device / page, end of range (`RemoteTests`)
- [ ] T015 Tests: notes-only phone refused, nav with Follow moving the controller (host side)

## Phase 3 — US2 / US4 display delivery (P1 / P2)

- [x] T020 `RemoteHub.Led`: render per watched page, overlay from knob turns, send `led` only on change
- [x] T021 Web `Led.svelte`: RLE4 decode, dot painting in the track colour, dim on disconnect
- [x] T022 Mock serves frames (`mock/nota-mock.mjs`)
- [x] T023 [P] Tests: frame arrives and decodes, still page → no resend, Macros closed → no frames
- [ ] T024 Test: two phones on one page get the same bytes and seq

## Phase 4 — Displays for every built-in device (US2 / US3)

- [x] T030 Instruments: synth, sampler, physical, aurora, volt, bass, pendulum, operator, grain, flux, rhythm, monolith, pentad, consort, keys, mosaic, twin
- [x] T031 Audio effects: eq-8, compressor, reverb, delay, utility, valve, auto-filter, vintage, orbit, auto-shift, beat-repeat, crush, dynamic-eq-8, ceiling, strata, eq-3, forge, level, shutter, chamber, prism, lens, flanger, phaser, chorus, vocoder, color
- [x] T032 MIDI effects: arp, chord, scale, length, velocity, random
- [x] T033 Test: every catalog type key and page has a registered display; each renders on a fresh device without throwing and lights dots
- [x] T034 Smoke test: render every built-in device's pages through the C ABI

## Phase 4b — Follow, landscape, full screen (added 2026-11-24)

- [x] T050 `FollowController` on the client, `mfollow` / `fc`, the controller moves the phone across tracks on Macros
- [x] T051 Web: Follow switch, landscape layout with eight knobs in a row, `Led` fit mode
- [x] T052 Web: full-screen display with page steps
- [x] T053 Test: follow to another track, Follow off stays, on catches up (`RemoteTests`)

## Phase 5 — Polish

- [x] T040 CHANGELOG `[Unreleased]`, FEATURES.md
- [ ] T043 Remote user doc (nota-docs repo)
- [ ] T041 `scripts/build-win.ps1` green; `npm run check` + build in `src/web/remote`
- [ ] T042 `/speckit-converge`

## Compliance

- [x] SPDX header on every new file · [x] no new dependency · [x] no audio-thread work · [x] no C ABI change · [x] smoke test extended · [x] CHANGELOG / FEATURES (user docs in nota-docs: not done) · [x] Remote follows DESIGN.md tokens
