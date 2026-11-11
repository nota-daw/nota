# Contract: MCP theme tools

Added to the in-app MCP server (`Nota.Mcp/Tools/ThemeTools.cs`), loopback only like every
other tool. All run on the UI thread through `IEngineDispatch`. A malformed `theme` argument
is a validation result, never a server error.

`theme` arguments are the theme document as a JSON **string** (so a client can send exactly
the file it would save).

| Tool | Arguments | Returns | Side effects |
|---|---|---|---|
| `list_themes` | — | `{ themes: [{ id, name, kind, source: "built-in"\|"user", warnings: int, fontMissing: bool }], worn: id, tryOn: bool, choice: { followSystem, single, dark, light }, problems: [{ file, reason }] }` | none |
| `get_theme` | `id` | the theme file text (format 1 or 2), keys in catalogue order | none |
| `get_theme_catalogue` | — | `{ tokens: [{ name, group, role, translucent }], radiusSteps: [{ name, design }], ranges: { scale: [0,2], step: [0,24], fontFamilyLength: [1,100] }, formatVersions: [1,2] }` | none |
| `list_system_fonts` | — | `{ families: [string] }` sorted | none |
| `validate_theme` | `theme` | `{ valid, problems: [{ rule, field, message }], warnings: [{ kind, pairing, tokens, measured, minimum, message }] }` | none |
| `preview_theme` | `theme`, `surfaces?: [name]`, `accent?: "user"\|"theme"` (default `user`) | content: 1 JSON text block = validate result + `{ rendered: [name], unknown: [name] }`, then per surface a text block `"<name> — <description>"` and an `image/png` block | none. Invalid theme → validate result only, no images. Unknown names reported in `unknown`; if all are unknown, no images. |
| `save_theme` | `theme`, `overwrite?: bool` (default false) | validate result + `{ saved: bool, file, reason? }` | writes `<id>.json` to the themes folder; reloads user themes. Refused: invalid; built-in id; existing user id without `overwrite`. |
| `apply_theme` | `id` | `{ worn, choice }` | wears and **stores** (single-theme mode), as a pick in Preferences; ends any try-on |
| `try_on_theme` | `theme` | validate result + `{ wearing: bool }` | wears live, **not stored**; replaces an earlier try-on |
| `revert_try_on` | — | `{ worn }` | ends the try-on, back to the stored choice; no-op if none |

## Preview size and order

Images are rendered at scale 1× at the sizes in [preview-surfaces.md](preview-surfaces.md),
in that table's order (or the order of `surfaces`). Same theme + accent option → identical
bytes on the same machine.

## Tool descriptions (for the model on the other end)

Each tool's `[Description]` states: the theme format is version 2 with optional `font` and
`radius`; start from `get_theme` of a built-in; `validate_theme` is cheap, `preview_theme`
takes seconds; readability warnings mean text may be hard to read and should usually be
fixed; `try_on_theme` is visible to the user and should be reverted if not kept.
