# Contract: value entry (gesture, helper, parser)

The behaviour every typeable control must show, and the internal API controls use to get
it. Internal to `Nota.App` — no C ABI, MCP or file-format change.

## Gestures (user-facing)

| Input | On a single-value control / read-out | On a host (graph, matrix, envelope) |
|---|---|---|
| Double-click (left) | Open the box for its value | On a handle: open the box for the handle's first field. Off a handle: the control's own double-click, unchanged |
| ⌥/Alt-click (left) | Reset to default (no-op without a default) | On a handle: reset its fields (matrix cell / route: clear). Where the control already uses ⌥ (paint / pull / remove), that meaning stays |
| Right-click | Parameter menu: **Type value…**, **Reset to default** — merged into the control's own menu if it has one | Same, for the handle under the pointer (**Clear** for a route) |
| Drag, Shift/Ctrl/⌘ drag | Unchanged | Unchanged |
| Return (box closed) | Unchanged: Stop / Session launch | Unchanged |

In the open box:

| Key / action | Effect |
|---|---|
| Enter / Return | Commit, close |
| Click outside, focus lost | Commit, close |
| Esc | Cancel, close — value as before the box opened, nothing in undo |
| Tab / Shift+Tab | Commit, then open the next / previous field in the scope (wraps) |
| Any other key | Goes to the box (no transport, no note keys) |
| Control removed / scope hidden | Close without commit |

Commit, for any field: `Begin()` → `Set(n)` → `End()` once — one undo step, one
automation gesture. Unreadable text or an unchanged value: nothing is called.

## Field

```text
ValueField
  Name      string               "Cutoff" — menu header, Tab announce
  Get       () -> double          current position, 0..1
  TextAt    (double) -> string    read-out at a position — pure (no engine writes)
  Set       (double) -> void      write the position (parameter setter)
  Begin     () -> void            opens the gesture (BeginAutomationWrite / undo)
  End       () -> void            closes it
  Default   double?               0..1 position of the default; null = no reset
  Clear     () -> void?           optional: remove instead of reset (routes, points)
  After     () -> void?           repaint the owner after a commit / reset
```

Legacy probing (`SliderRow` / `ClipEditorKit` without a pure `textAt`, plug-in generic
params): `TextAt` is built from `Set` + read-back and restores the original position
before `Begin` — as `ValueEntry.Attach` does today.

## Helper API (Nota.App, internal)

```text
ValueEntry.Attach(Control control, ValueField field, TextBlock? readout = null)
    Tunnel handlers on control and readout: DC → open, ⌥ → reset; RC → menu.
    The box sits over `readout` when given, else over the control.

ValueEntry.AttachHost(Control host)            host : IValueEntryHost
    Same, using host.FieldsAt(point); null → let the event through.

interface IValueEntryHost
    IReadOnlyList<ValueField>? FieldsAt(Point p)   fields of the handle at p, in Tab order
    Rect AnchorOf(ValueField f)                     where the box sits for that field
    IReadOnlyList<ValueField> AllFields             for the Tab chain, in order

ValueEntry.Scope  (attached bool)               marks a Tab-chain root (card / strip / panel)
ValueEntry.Open(Control control, ValueField f)  open programmatically ("Type value…", Tab)
ValueEntry.MenuItems(Control control, ValueField f) -> MenuItem[]
                                                for controls that build their own menu
Knob.Format  Func<double, string>?             the knob's read-out text at a value
```

`ValueEntry.Attach(TextBlock readout, norm, setNorm, text, begin, end, after)` — the
current signature — stays as a thin wrapper over the above.

## Parser (`ValueText.Read` / `ValueText.Solve`)

Accepted input, case-insensitive, surrounding spaces ignored, decimal point or comma,
`−` or `-`:

| Form | Examples | Meaning |
|---|---|---|
| number, fraction | `12`, `-6.5`, `1/4`, `.5` | in the read-out's unit (or the unit that fits the range) |
| unit multiplier | `1.2k`, `1.2kHz`, `12ms`, `500µs` / `500us`, `2s` | base units |
| plain units | `dB`, `Hz`, `%`, `st`, `ct`, `°` | taken as written (multiplier 1) |
| pan | `30L`, `L30`, `30 R`, `C` | left negative, C = 0 |
| infinity | `∞`, `inf`, `-inf` | the end of the range |
| a word the read-out shows | `off`, `HOLD`, `−∞` | the position(s) showing it |

Out of range → the nearest end. Nothing readable → no change. A stepped read-out lands in
the middle of the step that shows the value; a run that touches an end lands on the end.
