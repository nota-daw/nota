# Contract: desktop UI surface (spec 108)

## Preference

- Preferences → Editing → **Session** section → "Session grid": segmented **Scenes down** /
  **Scenes across**. Writes `Settings.SessionGrid`, saves, applies at once.
- Session toolbar: a toggle button with a turn glyph; tooltip names the layout it switches to.
  Same setting.

## Commands (ids unchanged, names changed)

| Id | New English name | Effect (both orientations) |
|---|---|---|
| `session.selectUp` | Select the previous scene | PrevScene |
| `session.selectDown` | Select the next scene | NextScene |
| `session.selectLeft` | Select the previous track | PrevTrack |
| `session.selectRight` | Select the next track | NextTrack |

Controller setup labels: "Previous scene" / "Next scene" / "Previous track" / "Next track".

## Layout (Scenes across)

```
┌ Scenes 8 ┬ Scene 1 ┬ Scene 2 ┬ … ┬ + Scene ┐ ┌ All ┬ I/O ┬ Sends ┬ Mixer ┐   ← top row moves with the grid
├ Track A  ┼  slot   ┼  slot   ┼ … ┤         │ │ ■   │ … │  …   │ pan/vol │   ← lane
├ Track B  ┼  slot   ┼  slot   ┼ … ┤         │ │ ■   │ … │  …   │ pan/vol │
├ Return A ┼ (blank) ┼ (blank) ┼ … ┤         │ │     │ … │      │ pan/vol │
└ Master   ┴ (blank) ┴ (blank) ┴ … ┘         └ └     ┴ … ┴      ┴ vol     ┘
  pinned left        scrolls sideways                 pinned right
```
