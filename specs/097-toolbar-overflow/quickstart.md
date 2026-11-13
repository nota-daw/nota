# Quickstart: validating the toolbar overflow

## Automated

```bash
scripts/build.sh          # builds, runs tests/Nota.SmokeTest (ToolbarOverflowTests + source checks)
```

Expected: all checks pass, including `ToolbarOverflowTests`, localization (every new key used
and present in en.json) and the menu guard.

## In the app

1. Launch Nota with a project open, window wide: the ⋯ button sits after the master strip,
   without the dot. Open it: only "Interface scale ▸".
2. Narrow the window one step at a time. After each step open ⋯: the entries match the
   controls that left the bar (contract table), in bar order; nothing still in the bar is
   listed; the dot shows.
3. Use each entry: Snap / Follow / Loop / Automation / MIDI learn toggle and the bar reflects it
   after widening; Grid ▸ picks a step; Key… opens the picker; Time signature ▸ changes the
   signature; Tap tempo tapped 4× sets the tempo with the menu open; Master fader drags and
   undoes in one step; DSP line moves during playback; Connect Phone… opens its dialog.
4. Interface scale ▸ 125 %: the UI rescales; Settings → Appearance shows 125 %; restart keeps it.
   With Settings open, change it from the menu: the Settings picker follows.
5. Escape, a second click on ⋯ and a click on a clip each close the menu; the clip click does
   not select the clip.
6. Widen the window: controls return, the dot goes.
7. Repeat 2 at 150 % and 200 %, in Studio Light and the classic look; check playback never
   stutters.

Screenshots for the PR: the bar narrow with the ⋯ dot and the open menu (before/after).
