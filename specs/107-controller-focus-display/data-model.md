# Data model: Controller focus you can see (spec 107)

All in-memory, session state; nothing is saved in projects or preferences.

## ControllerFocusSummary (Nota.App → readout, Remote, MCP)

| Field | Type | Notes |
|---|---|---|
| Controller | string | profile name, or "N controllers" |
| Mode | KnobMode | Device / Volume / Pan / Send(k) |
| TrackId | int | 0 = none (Device mode) |
| TrackName | string | |
| Device | string? | focused device name; null = track has no device |
| Address | DeviceAddress? | the focus (rack paths kept) |
| Page, PageCount | int | 0-based page; count ≥ 1 when a device is focused |
| PageName | string | may be empty |
| BankFirst, BankLast | int | 1-based track positions of the bank (non-Device modes) |

Changes on: focus resolve, PageStep, StepMode, BankStep / BankFollow, track rename, controller
connect / disconnect.

## SessionSelection (SessionView → Remote, MCP)

| Kind | Fields |
|---|---|
| None | — (Session not effectively visible, or a Bus column selected) |
| Slot | TrackId, Scene |
| Scene | Scene (the scene row) |

## ChainOrder

Ordered list of top-level DeviceAddress for a track: `Midi 0..n-1`, `Instrument` (only when the
track has an instrument), `Audio 0..m-1`. A rack is one entry. `Step(order, current, ±1)`: the
neighbour, or null past either end; a `current` not in the list (deleted) → the first entry.

## Selectable track order

`HierarchyOrder` ids minus return tracks minus members of collapsed groups. `Step(order,
selected, ±1)`: neighbour, null at the ends; `selected` absent → first id.

## ControllerState additions

- `BankFollow(int index, int trackCount)`: if `index` is outside
  `[BankOrigin, BankOrigin + 7]`, move `BankOrigin` the least amount to contain it (clamped like
  BankStep). Returns whether it moved.

## Remote client additions (RemoteClient)

- `SentFocus` (string?) — last `foc` sent.
- `SeenFocusKey` (string?) — the focus key the macro follow last acted on.
