# Quickstart: validating spec 102

## Engine side

```bash
scripts/build.sh
```

`RemoteTests.Session` must report: a group in `ses` with its members as `k`; a group `launch`
queues its members' clips; a group `stopSlot` stops them.

## Phone page

```bash
cd src/web/remote
npm run check
npm test
npm run build
node mock/nota-mock.mjs        # serves the built page with a mock project (includes a group)
```

Open the page at a phone size (390 × 844) and at phone landscape:

1. Scenes, scenes down: scroll to the bottom-right — track names stay on top, scene buttons on the
   left, Stop row and Stop All at the bottom.
2. Tap the group header: members appear; the group slot reads "n of m" with a bar per member.
3. ⋯ → Session grid → Scenes across: tracks become rows (name + Stop on the left), scene buttons on
   top, Stop All top-left. Scroll to the far corner — all still on screen. Reload: still across.
4. Tap a group slot / a group Stop: the mock logs `launch` / `stopSlot` with the group id.
