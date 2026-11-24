# Quickstart: validating spec 107

## Prerequisites

- `scripts/build.sh` green (engine Release in `build/`).
- A controller: the KeyLab Essential 61 mk3 (DAW program), or a test port
  (`NOTA_CONTROLLER_TEST=1`, MCP `controller_inject` with the KeyLab DAW identity reply).
- Remote: Nota's Remote on, a browser at the pairing URL; or the mock
  (`node src/web/remote/mock/nota-mock.mjs`, :7790, code 1234) for the web-only checks.

## Automated

1. `dotnet run --project tests/Nota.SmokeTest` — ControllerTests (chain order / step, selectable
   track order / step, BankFollow, TrackStep routing) and RemoteTests (`ses.sel`, `foc`, macro
   follow) pass.
2. `cd src/web/remote && npx vitest run` — Scenes outline and focus-line formatting tests pass.

## Manual / harness (US1–US4)

1. Project with tracks Drums, Bass (Arp → Twin → Valve → EQ8), Keys. Select Bass. Title bar chip
   shows `… · Bass · Twin · 1 / N`. Page ▶ → `2 / N` (spec US1-1/2).
2. Session view, Clip editor open. Controller Track ▶ → selection moves to Keys' slot in the
   same row, Clip editor follows, chip shows `Keys · …` (US1-3, US2-4).
3. Arrangement, select track 3, Track ▶ ×3 → track 6 selected and scrolled into view; Mixer bank
   outline contains it (US2-1/2).
4. Bass selected, Devices panel open: palette "Next device" ×3 from Arp → Twin → Valve → EQ8,
   card mark follows, fourth press stays (US3-1/2). Hide the panel, "Previous device" → chip shows
   Valve; panel stays hidden (US3-3).
5. Remote Scenes screen: walking the grid with the controller outlines the same slot within
   0.5 s (US4-1); switch Nota to Arrangement → no outline (US4-2). Header line shows the focus
   (US4-3). Phone in Follow mode, controller "Next device" → Macros switches device (US4-4).
6. Old client: the mock with `sel`/`foc` against a build of Remote from develop → no errors (US4-5).
