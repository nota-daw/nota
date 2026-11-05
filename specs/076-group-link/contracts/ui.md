# Contract: UI

Follows [`DESIGN.md`](../../../DESIGN.md) (Studio): neutral keys, orange accent only while on.

## Link key (group tracks)

| Place | Position | Look |
|-------|----------|------|
| Arrangement group header, slim bar | right of M / S | `ChainLink` glyph key, same size as M / S; accent fill when on |
| Arrangement group header, full row | in the M / S row | same |
| Mixer group strip (full and compact), Session mixer group column | in the M / S row | same |
| Group header context menu | after the grouping items | checkable **Link tracks** |

- Tooltip: "Link tracks — arming and clip edits act on every track of the group ·
  ⌥-click a clip or an arm to act on one track" (⌥ spelled per platform via `MenuKit.Keys`).
- Click: toggle, one undo step; every view repaints (Arrangement, Mixer, Session).

## Member mark

- A linked member's Arrangement header shows a small `ChainLink` glyph (10 px) before its
  name, in the secondary text colour — membership, not a live state, so no accent.
- Tooltip on the glyph: "Linked with {group name}".

## Selection

- Matches draw as selected clips (existing selection look); no extra decoration.

## Shortcuts (Preferences → Shortcuts, Arrangement section)

- "⌥-click a clip — select it alone in a linked group"
- "⌥-click Arm — arm one track of a linked group"

## Strings (English source + French in `Languages/fr.json`, checked by the smoke test)

"Link tracks", "Linked with {0}", the Link tooltip above, the extended arm tooltip, and the
status line "The group's tracks are linked" shown after toggling on.
