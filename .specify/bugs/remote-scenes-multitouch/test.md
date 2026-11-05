# Bug Verification: Nota Remote (iOS) can't launch several Session clips at once

- **Slug**: remote-scenes-multitouch
- **Tested**: 2026-11-05
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

Reproduced on iOS Safari (iOS Simulator, iPhone 17 Pro) before the fix: a two-finger touch on two
clips sent no `launch` at all. After the fix the same gesture sends one `launch` per clip; a
single tap still sends exactly one, a scroll that starts on a clip sends none, and keyboard
activation still works.

## Checks Performed

Setup: the page built from `HEAD` (pre-fix) and from the working tree (post-fix), each served
by a scratch copy of `src/web/remote/mock/nota-mock.mjs` that logs every `launch` / `scene` /
`stopSlot` / `stopAll` message it receives (ports 7791 / 7792).

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (pre-fix), iOS Safari | Simulator two-finger touch on Drums/Scene 2 + Keys/Scene 3, twice | fail as expected | 0 messages both times; a single tap sent 1 |
| Reproduction (post-fix), iOS Safari | same two-finger touch | pass | `launch 1/1` and `launch 3/2` |
| Single tap, iOS Safari | one tap on Bass/Scene 1 | pass | exactly one `launch` — no click double-fire |
| Scroll drag from a clip, iOS Safari | horizontal swipe starting on Keys/Scene 3 | pass | grid scrolled, nothing sent |
| Held two fingers on clip + Stop, iOS Safari | 400 ms two-finger hold, Strings/Scene 1 + Keys Stop | pass | `launch 6/0`, `stopSlot 3` |
| Chromium touch emulation (CDP) | staggered fingers, simultaneous, single, slide-off, keyboard Enter | pass | post-fix: 2 / 2 / 1 / 0 / 1 (scene); pre-fix: 0 / 0 / 1 / 0 / 1 |
| Type-check | `npm run check` (src/web/remote) | pass | 0 errors, 0 warnings |
| Page build | `npm run build` | pass | built into `Nota.Remote/wwwroot` |
| Full build + smoke test | `scripts/build.sh` | pass | first run failed one timing check (render pool "at most 6 late blocks within 50 ms") while the Simulator, two mocks and headless Chromium loaded the machine (load average 58); re-run on a quiet machine: all checks pass |

## Output Excerpts

```text
iOS, pre-fix, two fingers:   (nothing)
iOS, post-fix, two fingers:  CMD {"t":"launch","id":1,"i":1}
                             CMD {"t":"launch","id":3,"i":2}

CDP post-fix
staggered two fingers        ["launch 3/2","launch 1/1"]
simultaneous two fingers     ["launch 1/1","launch 3/2"]
single tap                   ["launch 1/1"]
finger slides off            []
keyboard Enter on Scene 1    ["scene 0"]
```

## Residual Risks

- Verified in the iOS Simulator, not on a physical iPhone/iPad; the Simulator runs the real
  WebKit, but touch timing on hardware may differ.
- VoiceOver activation was not exercised; it reaches the button as a `click`, which the action
  still handles (covered by the keyboard check, which takes the same path).
- Verified against the mock server, not a running Nota; the desktop side applies each message
  independently (`RemoteHub.Apply.cs`), so nothing there changes.

## Recommendation

Close the bug once the PR lands — verified end-to-end on iOS Safari and Chromium.

## Review follow-up (PR #513)

A keyboard activation within 800 ms of a pointer tap on the same button was dropped as if it
were the tap's own click. The click guard now drops only pointer clicks (`detail > 0`) right
after a pointer lifted; a keyboard click (`detail === 0`) always acts.

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Tap then Enter 100 ms later, Chromium (CDP) | touch tap on Scene 2, focus, Enter | pass | `scene 1` × 2 (the previous guard would drop the Enter by its 800 ms window — inferred from the code, not run) |
| Earlier CDP scenarios, Chromium | staggered / simultaneous / single / slide-off / Enter | pass | 7 runs in a row identical to the first report; one cold first run after a mock restart lost one staggered launch and did not recur |
| Single tap, iOS Safari (Simulator) | one tap on Drums/Scene 2 | pass | exactly one `launch` — WebKit's tap click carries `detail > 0`, so it is still dropped |
| Two fingers + scene tap, iOS Safari | two clips together, then Scene 1 | pass | `launch` × 2, then `scene 0` |
| Type-check / build | `npm run check`, `npm run build` | pass | 0 errors |
