# Data model: Assistant panel (098)

All records are immutable C# records unless noted. File format details: [contracts/manual-index.md](contracts/manual-index.md).

## ManualIndex (Nota.Application.Assistant)

| Field | Type | Notes |
|---|---|---|
| `Format` | int | `1`; a reader refuses a higher value (FR edge case: damaged / unknown → "manual isn't available") |
| `DocsCommit` | string | nota-docs commit the index was built from |
| `BuiltFor` | string | app version it was built for (informational) |
| `Languages` | string[] | `"en"` first, then translation folders (`"ru"`, …) |
| `Sections` | ManualSection[] | every section in every language |
| `Hash` | string | sha256 of the decompressed file (computed on load; keys the vector cache) |

Validation on load: unique `Id`s; every non-English section's `Twin` resolves to an English
section; `Format` known. Failure → `ManualIndex.Unavailable` with the reason (logged).

## ManualSection

| Field | Type | Notes |
|---|---|---|
| `Id` | string | `<lang>/<page>#<slug>`; intro sections use slug `""` |
| `Lang` | string | `"en"`, `"ru"` … |
| `Page` | string | page slug, e.g. `devices/audio-effects/compressor` |
| `PageTitle` | string | frontmatter `title` |
| `Heading` | string | `##` text; `""` for the intro |
| `Slug` | string | Starlight heading slug |
| `Markdown` | string | section body as plain Markdown (R2) |
| `Twin` | string? | English section id for a translation (same page, same position); null for English |
| `HowTo` | bool | matched the how-to rule (R6) |
| `Explanatory` | bool | carries `<At id="none"/>` |
| `Steps` | AnchoredStep[] | ordered; empty when none |

Rules: `HowTo && !Explanatory` ⇒ `Steps.Count > 0` (FR-020, smoke check). For a
translation, `Steps.Select(s => s.Anchor)` equals its twin's (FR-019a).

## AnchoredStep

| Field | Type | Notes |
|---|---|---|
| `Anchor` | string | `ui:<name>` or `cmd:<command id>` ([contracts/anchors.md](contracts/anchors.md)) |
| `Text` | string | the list item or sentence holding the anchor, as Markdown |

## AnchorDefinition (Nota.Presentation.Assistant)

| Field | Type | Notes |
|---|---|---|
| `Id` | string | `ui:…` (commands are resolved through `CommandCatalog`, not listed) |
| `Locator` | string | name the app binds to a live control |
| `View` | AnchorView | `Any`, `Arrangement`, `Session`, `Modular` |
| `Reveal` | RevealStep[] | ordered; what to show first |
| `Requires` | string? | `device:<kind>` etc.; unmet → guidance instead of highlight |

`RevealStep` = (`Kind`: View · Panel · DetailTab · BrowserTab · Prefs, `Arg`: string). Command
anchors: `CommandAnchor` maps a command id to an optional `ui:` id (its button) — e.g.
`view.mixer` → `ui:transport.mixer`.

## Search state (Nota.Application.Assistant)

- **WordIndex**: per language — term → postings (section ordinal, field, tf), doc lengths,
  average length, vocabulary list (for one-edit expansion).
- **SearchHit**: `Section`, `Score` (fused), `WordRank`, `VectorRank?`.
- **SectionVectors** (Infrastructure): `float[sections × 384]`, `IndexHash`, `ModelSha`; file
  header `NOTAVEC1`, count, dim, hashes; state `Missing → Building(progress) → Ready`.

## Conversation (Nota.Application.Assistant)

| Entity | Fields |
|---|---|
| `Conversation` (mutable, session-only) | `Messages: List<ChatMessage>`, `Clear()`, `Changed` event |
| `ChatMessage` | `Role` (User · Assistant), `Markdown`, `Quoted: ManualSection[]`, `Walkthroughs: Walkthrough[]`, `Time` |
| `Walkthrough` | `Section`, `Steps: AnchoredStep[]` |
| `IAssistantResponder` | `Task<ChatMessage> ReplyAsync(string question, CancellationToken)` — this feature's `ManualResponder`; #607 adds another |

State of a walkthrough (`ShowMeController`, App): `Idle → Step(k) → Idle`; `Step(k)` →
`Step(k±1)` on Next/Back; any step → `Idle` on Done / Escape / panel closed / project closed.
Entering a step runs its reveal, then highlights or shows guidance (`Requires` unmet,
control not found).

## Settings additions (Nota.Application.Settings)

| Field | Type | Default |
|---|---|---|
| `AssistantShown` | bool | false |
| `AssistantWidth` | double | 360 (clamped 280 – 560) |

## Model store addition

`AiModels.ManualSearch = "multilingual-e5-small"`; `ModelAsset` gains `Extra:
IReadOnlyList<ModelFile>` (url, sha256, size, name) so the tokenizer installs beside the
model; `IModelStore.ModelFile(id, name)` returns an installed extra file's path.
