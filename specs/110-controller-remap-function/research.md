# Research: Remap one controller function

## R1 — Where edits are written: patch the original JSON, don't regenerate

- **Decision**: Load the profile's original text (embedded resource for shipped, file for user)
  into a `JsonNode`; on save replace only the `controls` array. Each unchanged control keeps its
  original JSON object verbatim; a new or re-learned control gets a fresh object written with
  `ProfileWriter`'s entry writer, and inherits `feedback` / `motorised` from the original entry of
  the **same physical control** (same message, channel, number), because an LED belongs to the
  hardware button, not to the function.
- **Rationale**: FR-012 — init / release, LED feedback, `source`, `pickup`, `claimAll`, match rules
  and anything hand-added must survive. `ProfileWriter.Write` only knows what the wizard learns.
- **Alternatives**: regenerate with `ProfileWriter` + re-serialise the loaded `ControllerProfile`
  (loses unknown fields and comments; loader is lossy) — rejected.

## R2 — Shipped profile → user copy

- **Decision**: Save to `<userDir>/<shipped file name>` with the **same id**. `ReloadProfiles`
  already lets a user profile with a shipped id replace it. "Back to shipped profile" deletes that
  file and reloads. `ControllerService` gains `ProfileJson(profile)`, `HasShipped(id)` and
  `RemoveUserProfile(fileName)`.
- **Rationale**: no format or matching change; the copy matches the controller exactly as the
  shipped profile did (same `match`), keeping the KeyLab's identity rules.
- **Alternatives**: a separate "overlay" file of edits on top of the shipped profile — more
  machinery, two sources of truth; rejected.

## R3 — Function identity

- **Decision**: `FunctionKey(Kind, Index, Command)` derived from `ControlRole`: knob / fader by
  index, master and transport / navigation roles by kind, commands by id. A two-way control (an
  encoder with `roleLeft` / `commandLeft`) covers two keys — its right half and its left half.
- **Rationale**: matches how the router dispatches and how the wizard's `LearnedControl` is shaped.

## R4 — Physical identity and conflicts

- **Decision**: two declarations are the same control when channel matches and: same message and
  number; or CC n vs CC14 n / n+32; or both pitch bend. NRPN compares by NRPN number. Learning a
  control another function has → `Conflict(otherKeys)`; **Move** removes the other declaration
  (both halves when it was a two-way encoder, per spec US2.4), then assigns.
- Read-only "Other" declarations (pads, generic buttons, mode-*, knobs beyond the shown slots)
  take part in conflicts the same way; Move removes them.

## R5 — Learning: reuse the wizard's heuristics

- **Decision**: extract the wizard's poll logic into `ControlCatcher` (collect events, decide
  "done" by press-and-release or stillness, classify with `ControlClassifier`, encoder vs button
  for pairs, ambiguous encoder). The wizard and the editor both use it.
- **Rationale**: FR-006 "the wizard's detection"; one implementation.

## R6 — Commands for "+ Command…"

- **Decision**: offer `CommandCatalog.Create()` (the command palette's catalog: id, title,
  category) — the same ids `ControllerService.RunCommand` → `CommandRegistry.Run` executes and the
  same titles `ControllerService.CommandTitle` shows. Exclude ids already present as rows and
  palette-only entries that make no sense on a button (`view.palette`).
- **Rationale**: the spec says "shortcuts catalog"; the key-action catalog (specs/096) holds keys,
  not commands; the palette catalog is the list of commands with ids, which is what a profile's
  `command` stores. Spec wording updated accordingly.

## R7 — Wizard pre-filled (Learn everything again)

- **Decision**: `ControllerSetupWindow` gets an optional seed (`ProfileMapping`). Seeded steps
  start with the current control learned; **Next / Enter** keeps it; a new control replaces it.
  A control seeded on a **later** step is not "already learned" — learning it here takes it from
  that step. Seeded Finish hands the learned set back to the editor, which applies it as
  "replace all wizard functions" and saves through `ProfileMapping` (keeps init / LED / Other /
  command rows).

## R8 — Which window opens

- **Decision**: `Set up` opens the editor when `c.Profile` is generic; the wizard when there is no
  profile; nothing for MCU / HUI (button already hidden). The button reads "Edit mapping…".

## R9 — Live behaviour while editing

- **Decision**: the port stays captured while the editor is open (as with the wizard): controls
  don't drive Nota; changes apply on Finish. That makes "find by touch" (FR-010) safe.
