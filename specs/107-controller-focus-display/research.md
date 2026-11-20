# Research: Controller focus you can see (spec 107)

## R1 — Why the user "doesn't see" the track switch

- Decision: change Track ◀ / ▶ from a bank step of one to a track selection (TrackStep).
- Rationale: in 051, TrackNext / TrackPrev produce `BankStep ±1`; only in the Session a special
  case also moves the slot selection. Outside the Session the selected track — hence the focused
  device — never changes, so nothing visible happens. In the Session the slot moves and
  `FollowSessionSlot` keeps the Clip editor (by design), so the focus does change but is not shown.
- Alternatives: keep the bank step and show the bank (doesn't answer "which device");
  Session-only selection (rejected in clarify).

## R2 — Where to show the focus

- Decision: extend the existing controller chip in the title bar (beside the MIDI button).
- Rationale: it is already always visible while a controller is active and already shows the
  mode; no new surface, no pop-up noise; works in every view. Click → "Show device".
- Alternatives: transient HUD (rejected in clarify), auto-switching the lower panel (rejected:
  it fights the Session's Clip-editor follow, #334).

## R3 — Marking the focused card

- Decision: an attached property `CardAddress` on each top-level card; the existing
  `ControllerFocusOverlay` draws the mark.
- Rationale: the overlay already scans the Devices panel and the Mixer each 200 ms for MIDI
  Learn bindings; a card-level tag reuses that scan and keeps cards free of controller code
  (051's "no card needs code of its own").
- Alternatives: per-card `IsControllerFocused` styling (touches every card type).

## R4 — Shipped profile bindings for device stepping

- KeyLab Essential 61 mk3 (DAW program): all buttons seen on the real device are already used
  (transport, Undo/Redo/Metro/Save/Quantize/Tap, track ◀ ▶, encoder + push). Shift-layer
  messages were not captured. Decision: capture Shift + track ◀ / ▶ (and Shift + encoder) with
  the raw monitor on the user's hardware; if they send distinct messages, bind them to
  `device.focusPrevious` / `device.focusNext`; otherwise leave the profile and document the
  context-menu / wizard way.
- Launchkey MK3: "Device Select" is a modifier (hold + pad) in Novation's own DAW scripts, not a
  step button; no change.
- X-Touch Mini: its buttons are generic MIDI Learn buttons; no change.
- Mackie Control: no standard "next device" button outside plug-in mode; no change.

## R5 — Remote transport of the new state

- Decision: Session selection as a field `sel` of `ses` (only phones on the Scenes screen need
  it, at the `ses` cadence); controller focus as its own message `foc`, sent to every phone on
  change (the header shows it on every screen).
- Rationale: additive; old clients ignore an unknown field and an unknown `t` (both the web
  app's and Nota NX's dispatch `switch` fall through).
- Alternatives: putting focus into `proj` (re-sent every few ticks with the whole track list —
  heavier and resets the phone's screen state on change).

## R6 — Macros following the focus

- Decision: only on a *change* of focus, only for a phone in Follow mode on the focus track.
- Rationale: the phone's own device picker stays usable; following never fights a manual pick
  until the controller moves again.
- Mapping: Remote's macro devices are the instrument / rack (index −1) and the audio effects
  (index i). MIDI effects are not shown in Remote → no switch.

## R7 — Selectable track order

- Decision: `HierarchyOrder` minus return tracks minus members of collapsed groups.
- Rationale: matches what a user sees as the track list and what a header click can reach;
  returns are outside the bank-of-eight walk (051 FR-009 puts them last in the bank list for
  mixer mode only).

## R8 — Mackie Control channel buttons

- Decision: unchanged — on an MCU / HUI surface Channel ◀ / ▶ keep shifting the strips by one
  (and walk the Session slot across while the Session shows).
- Rationale: on a control surface those buttons are strip banking by definition (Logic, Live and
  others do the same); its per-strip **Select** buttons already select a track. The Track ◀ / ▶
  roles (generic profiles, the KeyLab DAW program, Launchkey, learned controllers) select tracks.
