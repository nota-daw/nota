# Data Model: Interface Language Selector

## Interface language (preference)

| Field | Type | Rule |
|---|---|---|
| `Settings.Language` | string | Language code, lower-case: `"en"` or `"fr"`. Default `"en"`. |

**Normalisation** (`UiLanguage.Normalize`): a stored value that is not one of the available
codes (empty, wrong case handled by lower-casing, hand-edited, or from a newer version
with more languages) reads as `"en"`. The stored value is **not** changed until the user changes
the setting (spec edge case); other saves of the file keep it as written. Mirrors `InterfaceScale.Normalize`.

**State**: the stored value is what the *next* start uses. The *running* language is the
value `Loc` was installed with at launch and never changes during a run.

```
running = en, stored = en
   │ user picks Français
   ▼
running = en, stored = fr   ──(left alone)──▶ stays; interface still English
   │ Restart button (after unsaved prompt) / next launch
   ▼
running = fr, stored = fr
   │ user picks English, then Français again → no pending change
```

A "pending change" exists exactly when `stored != running`; the Language row shows a short
note ("Applies after restart") in that state.

## Available languages

| Field | Type | Note |
|---|---|---|
| `Code` | string | `en`, `fr` — the file name `Languages/<code>.json`. |
| `NativeName` | string | Shown in the picker in its own language: "English", "Français". Read from the file's `name`. |
| `Plural rule` | function(int) → `One` / `Other` | In `UiLanguage`, keyed by code. English: n = 1 → One. French: n = 0 or 1 → One. A language file with no rule uses the English rule. |

The picker lists every embedded `Languages/*.json`, English first, so adding a language
needs no code change beyond a plural rule when its rule differs (FR-011).

## Translation set

One per language: a map from **key** to **entry**.

| Part | Rule |
|---|---|
| Key | The English source text, exactly as written at the call site. With a context tag: `"<text>\|<context>"` (see contracts). Unique within a file. |
| Entry | Either a string (simple text) or an object `{ "one": "...", "other": "..." }` (plural). |
| Placeholders | `{0}`, `{1}`, … — the same set must appear in the key and in the entry's `other` form; the `one` form may drop a placeholder but not add one. Order may differ (French word order). |
| Fallback | A key with no entry returns the English text, formatted with the same arguments. Never blank, never the key's internal form (the context suffix is stripped). |

English has no table of its own: the key *is* its text. `en.json` carries only the
language `name` and optional translator notes.

## Interface text (classification)

| Class | Translated? | Examples |
|---|---|---|
| Nota interface wording | Yes | menus, labels, tooltips, dialogs, status and error messages, command palette titles |
| Device / effect names, parameter names, value labels | No (English, FR-004) | Cutoff, Attack, Reverb, "Nota Volt" |
| Proper nouns and product names | No | Nota, Studio Dark, VST3, MIDI |
| Unit symbols and musical notation | No | dB, Hz, BPM, bars/beats, note names |
| User content | Never | project, track, clip, scene, preset names, tags |
| Third-party plugin content | Never | plugin names, parameters, editors |
| Out of scope surfaces | Not in this feature | Nota Remote web UI, MCP tool descriptions, CHANGELOG / release notes, logs, crash reports |
| Default names created for the user | At creation time, then plain user text | a new track's name |

## Relationships

- `Settings.Language` → selects one **Translation set** at startup via `UiLanguage`.
- `Loc` holds the one installed set and the language's plural rule; all call sites read it.
- The smoke test's coverage check relates **source call sites** ↔ **French keys** (every
  used key present, every present key used).
