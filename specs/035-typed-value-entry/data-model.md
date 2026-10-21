# Data model: Typed value entry

No persisted data changes — project files, presets and settings are untouched. The
entities are UI-side, in `src/managed/Nota.App`.

## ValueField

One typeable value. Fields and their meaning: [contracts/value-entry.md](contracts/value-entry.md#field).

Rules:

- `Get` / `Set` work in 0..1; the parameter mapping stays inside the owner.
- `TextAt` is pure: it formats, it never writes the engine. Exceptions are listed in the
  audit (legacy `SliderRow` callers, plug-in generic params) and use the probing adapter.
- `Default` is in 0..1 and clamped; null disables ⌥ reset and greys the menu item.
- `Clear`, when present, is what ⌥-click and **Clear** call instead of a reset (matrix
  route, cable).

## ValueEntrySession

One open box.

| Field | Meaning |
|---|---|
| Owner | control the box belongs to |
| Field | the `ValueField` being typed |
| Before | `Field.Get()` when the box opened |
| Text | what is typed |
| Scope | the nearest `ValueEntry.Scope` ancestor (for Tab) |

States: **Open** → (Enter / outside click / focus lost) **Committed** · (Esc) **Cancelled** ·
(owner detached / scope hidden) **Abandoned** · (Tab) **Committed** then a new session on
the next field. Only one session exists per top level; opening a second commits the first.

Commit: `n = ValueText.Solve(Text, Field.TextAt(Get()), Field.TextAt)`; if `n` is null or
`Field.TextAt(n) == Field.TextAt(Before)` → no-op; else `Begin(); Set(n); End(); After()`.

## Tab chain

Built when Tab is pressed, never cached: every `Attach`ed control and every host field
under `Scope` that is visible (`IsEffectivelyVisible`, inside the scope's bounds) and
enabled; ordered by reading order — rows grouped by top edge (within half the smaller
height), then left edge; a host contributes `AllFields` in its own order at its position.
Wraps at both ends.

## Control audit item

| Field | Meaning |
|---|---|
| Control / site | class or file |
| Gesture today | DC / RC / ⌥ behaviour before the change |
| Plan | Attach · Host · Kit · Read-out row · Exempt |
| Status | ☐ / ☑, with a reason when exempt |

Kept in [contracts/control-audit.md](contracts/control-audit.md).
