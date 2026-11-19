# Research: Modular device cards and a searchable cable-drop picker

Phase 0 for [spec.md](spec.md). Every decision below was checked against the code on
`develop` (2599930d).

## R1 — Hosting the Detail card inside a Modular node

**Decision**: Modular builds only the card **body**, with the same builders Detail uses:
`DeviceCardFactory.Resolve(kind).Build(ctx, i)` for effects, the instrument factory /
`PluginCard.Build` for instruments and plug-ins, the MIDI bodies for MIDI FX, and
`RackCardView(ctx)` for racks. The node keeps its own frame: header, port block, then
the body where the parameter rows sit today. The Detail shell (`BuildCardShell`) is
not used.

**Rationale**: the bodies are already stateless strategies over a `DeviceCardContext`
seam (DeviceCardContext.cs: "card strategies stay stateless; all per-rebuild state
lives here"), so a second host is what that seam is for. The shell is where the
chain-only behaviour lives: reorder ◀▶, chain drag (`HookDeviceDrag`), fold, XL,
preset header, the selection ring. FR-017 forbids those in Modular. Keeping Modular's
header also keeps the ports at a fixed offset under the header, so the port anchors
(`GNode.PortY`) don't depend on the body's height.

**Alternatives considered**: reusing `DeviceChainView.DeviceCard()` whole (drags in
reorder / fold / XL and its private state such as `_xlIndex` and `_cardExtra`); a
Modular-specific card per device (doubles ~60 card UIs).

**Context wiring**: ModularView creates one `DeviceCardContext` per rebuild.
`AddDeviceRefresher` and `AddRackParamRefresher` → Modular's per-tick `_liveKnobs`.
`AddInstFader` → a refresher that follows that instrument param. `SetInstLiveViz` → a
Modular-owned slot run on the tick. `NotifyChanged` → `Changed`. `RequestRebuild` →
`Rebuild`. Preset save → no-op (FR-015: presets stay in Detail). `canXl: false`,
`popout: false`.

## R2 — Which devices have an S size, and where it is stored

Today there are four stores, and the bodies read them directly (39 call sites in
`DeviceCards/Bodies`, 53 in `Instruments` / `Midi` / `Rack`):

| Family | Store | Has S when |
|---|---|---|
| Audio effect (built-in) | `DeviceView` — the device's `View` param (0 = L, 1 = S, 2 = XL) | the device has a `View` param |
| Instrument (built-in) | `InstrumentView` — plug-in param id `view` (0 = L, 1 = S) | `InstrumentView.Index ≥ 0` |
| MIDI effect | a per-body `View` param (e.g. `ArpMidiBody.PView`) | the MIDI effect has a param named `View` |
| Hosted plug-in (VST3 / AU) | view-local `PluginCardState.Mini` in DeviceChainView's `_cardExtra` | always (compact card, `PluginCard.MiniWidth`) |
| Racks, generic-param cards | — | never (L only) |

So plug-ins **do** have an S card. spec.md was corrected for this: plug-ins open at S
by default.

**Decision**: a new engine view `CardSizeEngineProxy` (a `DispatchProxy` over
`IAudioEngine`, next to `RackEngineProxies.cs` in `Nota.Application/Engine`). It is
handed to the bodies through the Modular card context. It intercepts reads and writes
of **the card-size param of the one device it wraps**:
`DeviceGetParam` / `DeviceSetParam` at `DeviceView.Index`,
`PluginParamGet` / `PluginParamSet` at `InstrumentView.Index`, and
`MidiEffectGetParam` / `MidiEffectSetParam` at the MIDI effect's `View` index. It
answers from, and writes to, the Modular node size (S → 1, L → 0). Every other call
passes through to the real engine. For a plug-in, Modular passes its own
`PluginCardState` with `Mini` set from the node size.

**Rationale**: FR-014 needs the Modular size to be independent of Detail. Writing the
engine param would resize Detail and leave an undo step. The proxy pattern is already
how the rack chains reuse instrument editors (`RackChainEngineProxy`,
`NestedRackEngineProxy`), so no card body changes. A body's own S/L toggle (for
example the Arp's, inside its body row) writes through the proxy, which updates the
Modular size and asks for a rebuild. Both controls stay consistent.

**Alternatives considered**: adding a `SizeOverride` to `DeviceCardContext` and
migrating ~90 direct `IsMini` / `WidthFor(engine, …)` reads in ~40 files (big, touches
every card, easy to miss one). Writing the real param for the duration of a build and
restoring it (races the UI tick and the engine's undo).

**Risk**: `DispatchProxy` costs a reflection dispatch per call. The rack proxies run at
the same 60 Hz refresh rate, and the measured cost is checked by quickstart Q5 (SC-004).

## R3 — Node size control and the XS node

**Decision**: XS is today's node (`BuildNodeView` + `ParamsBlock`), unchanged. S / L
replace `ParamsBlock` with the card body (bypass dimming is handled by the body). A
segmented `XS · S · L` chip in the node header (S hidden when R2 says no S) switches
the size. A node's `Width` becomes per-node instead of the `NodeW` constant: XS keeps
`NodeW`, S / L use the measured card width. Everything that assumed `NodeW` now reads
`n.Width`: output anchors on the right edge, the connect hit-test in `FinishConnect`,
`PortLabel.MaxWidth`, the minimap, and the default chain layout spacing (`GapX` after
each node's width).

**Rationale**: the spec's XS is literally the current node. A header chip matches the
Detail S / L toggle (`MidiCardHeader.SizeToggle`) visually and needs no menu. Fixed
widths elsewhere would put output ports in the middle of a wide card.

**Alternatives**: a right-click "Size" submenu (hidden); size via ⇧-double-click
(undiscoverable).

## R4 — Persisting the node size

**Decision**: extend the existing `modular-layout.json` sidecar. Today its values are
`[x, y]`; with this change they become `[x, y, size]` (0 = default / unset,
1 = XS, 2 = S, 3 = L). `LoadLayout` accepts length 2 (old files → default size) and 3.
An older app reading a new file only checks `xy.Length >= 2`, so it ignores the third
value. Size changes are saved on change, the same way positions are saved at the end
of a drag.

**Rationale**: FR-014 / SC-006. The layout data is UI-only and already keyed by
doc-index (`ToDocKey`), so it survives track-id reassignment and needs no engine or
project-format change.

**Known limitation (pre-existing)**: keys are positional (`dev0`, `dev1`, …), so after
reordering the chain in Detail a saved position/size can attach to the neighbour.
Positions already behave this way, and fixing it is out of scope for #656.

## R5 — Dropping a cable directly on a parameter (FR-001a)

**Decision**: both the card controls and the XS rows already carry their parameter
identity: `MidiLearn.Bind(control, MidiTarget, name)` stores a `MidiBinding` in an
attached property (`MidiLearn.BindingProperty`, ~190 call sites, including Modular's
own XS rows, ModularView.cs:835 / 853). While a CV cable is dragged, each pointer move
hit-tests the viewport (`InputHitTest`) and walks up the visual tree to the first
control with a binding. That binding counts as a direct target when its kind maps to a
CV link target on **this** node's device:

| MidiTargetKind | CvLinkAddToTarget kind | device |
|---|---|---|
| `DeviceParam` | 0 | `DeviceIndex` (≥ 0; not a rack-inner encoding) |
| `PluginParam` with device −1 | 1 | −1 (the instrument) |
| `MidiDeviceParam` | 2 | `DeviceIndex` |

A matching control gets a brass outline drawn on an overlay layer above the nodes (it
does not restyle the control). `FinishConnect` links it directly. Anything else (rack
inner params, tabs, graphs, buttons, non-param controls) falls back to the picker. A
control already linked to this modulator links again as a no-op, the same as today's
menu.

**Rationale**: reuses an identity the cards already maintain for MIDI learn, so new
cards get drop support for free.

**Alternatives**: a new tag on every control (duplicates MidiLearn), or geometry lookup
tables per body (fragile).

## R6 — The picker panel

**Decision**:
- **Model** (`ParamPickerModel`, Nota.Presentation, no Avalonia types) holds the
  entries (group headings + items with label, secondary text and a payload), the query,
  the filtered view, and the highlight index. It owns filtering
  (`string.Contains(…, OrdinalIgnoreCase)` on the label, plus the group name for
  grouped lists), hides empty groups, skips headings when moving the highlight, and
  supplies fallback labels for empty or duplicate names ("Parameter 42", or "Gain (2)"
  for duplicates).
- **View** (`ParamPickerPanel`, Nota.App/Controls) is a `Flyout` placed at the
  pointer, which Avalonia flips and shifts to stay on screen (FR-003). It holds a
  `TextBox` focused on open and a `ListBox`, whose default `VirtualizingStackPanel`
  realises only the visible rows (FR-007). Fixed size 320 × max 420 (SC-002). Heading
  rows aren't selectable. Keys: ↑ ↓ / Enter / Esc, with PageUp / PageDown as a bonus.
  An empty state is shown as a row.
- **Names** are read once when the picker opens (the `…ParamName` calls), into a plain
  list. Filtering works on that list, so a keystroke doesn't cross the ABI.

**Rationale**: the virtualizing `ListBox` is built in (no dependency) and already used
by BrowserView and the Arrangement automation picker (`ShowPluginParamPicker`, a
simpler filter-only version). Putting the logic in the model makes it testable
headlessly. The same panel serves all four call sites (FR-001, FR-002, FR-002a).

**Risk**: reading 5,000 plug-in names when the picker opens. Each name is an in-process
call into the plug-in host's cached parameter list. Quickstart Q2 measures it against
SC-001 (150 ms). If it's over, the fallback is to read names in pages of 256 as the list
scrolls or the search runs (the model already works on indices).

**Alternatives**: the Command Palette view (window-level overlay with ranking and
chips; too heavy for a drop target); keeping `MenuFlyout` with a search item (menus
can't virtualize).

## R7 — The right-click submenus (FR-002a)

**Decision**: in `ShowModulateMenu`, "Modulate from a parameter" (sources: parameters
on this track, grouped by device) and "Modulate from another track" (modulators on
other tracks, grouped by track) become plain menu items. Each one closes the menu and
opens the picker at the same anchor, with the entries those submenus list today. The
click actions are unchanged.

## R8 — Tests

The repository has no unit-test project. `tests/Nota.TempoUiTest` is a headless
Avalonia program that drives a real view against a real engine without an audio device.
**Decision**: add `tests/Nota.ModularUiTest` in the same style (not in `Nota.sln`, run
by hand and by quickstart). It covers the picker model (filtering, headings,
navigation, fallback labels, a timed 5,000-entry filter), sizes and their persistence
round-trip, port anchors after a resize, drop-on-knob link creation, and that the
Detail `View` param is unchanged after a Modular size change. The smoke test is
untouched: there's no engine change.
