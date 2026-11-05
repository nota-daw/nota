# Bug Fix: Nota Remote (iOS) can't launch several Session clips at once

- **Slug**: remote-scenes-multitouch
- **Fixed**: 2026-11-05
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The Scenes screen's buttons acted in `onclick`, which iOS Safari only sends for a single-finger
tap. They now use a shared `use:tap` action that follows each pointer itself and fires on that
pointer's release, so every clip under a finger launches, while a drag that scrolls the grid
still launches nothing.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/web/remote/src/lib/tap.ts` | added | `tap` Svelte action: per-pointer tap with a 10 px slop, release must land over the button, `pointercancel` (browser scroll) drops it; `click` kept for keyboard / VoiceOver, ignored right after a pointer lifted |
| `src/web/remote/src/screens/Scenes.svelte` | modified | clip, scene, Stop and Stop-all buttons: `onclick` → `use:tap` |
| `CHANGELOG.md` | modified | `[Unreleased]` → Fixed entry |

## Diff Highlights (optional)

```svelte
<button class="clip" …
  use:tap={() => guard() && app.send({ t: 'launch', id: c.id, i: si })}
```

```ts
const onUp = (e: PointerEvent) => {
  const p = down.get(e.pointerId);
  lifted = performance.now();
  if (!p) return;
  down.delete(e.pointerId);
  …
  if (!far(e, p) && over) act();
};
const onClick = () => { if (performance.now() - lifted > SAME_TAP_MS) act(); };
```

## Tests Added or Updated

- The phone page has no unit-test harness (the assessment noted this). The behaviour is pinned
  by the manual multi-touch checks recorded in `test.md` (two simultaneous touch points against
  the mock server, scroll-drag, single tap, keyboard activation).

## Local Verification

- Commands run: `npm run check` (src/web/remote) → 0 errors, 0 warnings; `npm run build` → built
  into `Nota.Remote/wwwroot`.
- Manual checks: see `test.md`.

## Deviations from Assessment

None.

## Follow-ups

- Other screens with `onclick` buttons (Mixer M/S/Arm, transport) have the same single-finger
  limitation; they are rarely pressed together, so they stay as they are unless someone asks.
