# Data Model: Remap one controller function

All types in `Nota.Application.Controllers` unless noted.

## FunctionKey

`readonly record struct FunctionKey(ControlRoleKind Kind, int Index = 0, string? Command = null)`

- Knob / Fader: `Index` 0-based. MasterFader, transport, navigation roles, Jog: `Index = 0`.
- Command: `Command` = palette id (`edit.undo`, `transport.metronome`, …).
- `FunctionKey.Of(ControlRole r)` → right-half key; `FunctionKey.LeftOf(ControlRole r)` → left-half
  key when `r.TwoWay`.

## PhysicalControl (helper)

`(ControlMessage Message, int Channel, int Number)` with `Same(a, b)` per research R4.

## MappingEntry

One declaration in the working list.

| Field | Meaning |
|---|---|
| `Decl` | `ControlDecl` (role + message + channel + number + encoder + feedback) |
| `Json` | original `JsonObject` when it came from the file, else null (written fresh) |

## ProfileMapping

Working copy of a profile's controls.

| Member | Meaning |
|---|---|
| `Profile` | the loaded `ControllerProfile` |
| `Entries` | current `List<MappingEntry>` |
| `Original` | entries as loaded (for revert / changed marks) |
| `ControlOf(key)` | the entry covering the key (directly or as a two-way left half), or null |
| `IsChanged(key)` | current control for key differs from the original |
| `Try(key, learned)` → `Conflict?` | entries other than the key's own that use the same physical control |
| `Assign(key, learned, leftKey?)` | removes the key's (and leftKey's) current entries, removes any conflicting entries, adds a new entry; two-way when `leftKey` given |
| `AssignHalf(key, learned)` | a pair half as a button; if the pair was one encoder, the encoder keeps the other half only |
| `Clear(key)` | removes the key's entry (a two-way encoder keeps the other half) |
| `Revert(key)` → `Conflict?` | restores the original entry for key; refuses (returns conflict) when its control is now used elsewhere |
| `ReplaceAll(IEnumerable<LearnedControl>, keys)` | the seeded wizard's result: drop entries for all `keys`, add the learned ones |
| `HasChanges` | any difference from original |
| `ToJson(string originalJson)` | original document with `controls` rebuilt (R1) |
| `Find(status, ch, d1, d2)` | entry an incoming raw message belongs to (FR-010) |

### Rules

- Conflicts compare physical controls (R4); learning the key's own control is a no-op.
- A newly written entry inherits `feedback` and `motorised` from any original entry with the same
  physical control.
- Entry order: unchanged entries keep their position; new entries are appended.

## Function catalog (App: `SetupFunctions`)

`SetupFunction(Group, Kind single|pair|slot, Name, Prompt, Icon/Word/Picture, Right: FunctionKey,
Left: FunctionKey?)` — one per wizard step (8 knobs, 8 faders, master, transport 4 + wind pair,
navigation 4 pairs, Session 2 pairs + 4 commands, Extra 2 pairs + metronome, save, tap, jog).
Command rows added via + Command… are `SetupFunction(Extra, single, title, command key)`, created
for command entries in the profile that the catalog doesn't list.

## Editor row state (App)

`Idle → Listening → (Learned | Conflict → Learned | Idle)`; `Listening(pair) → WaitingSecondButton
→ Learned`; `Listening → Ambiguous (encoder pick) → Learned`.
