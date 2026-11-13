# Data model: User skins (087)

Types are described by role. Names are proposals for the implementation, not a contract.

## ThemeFile (on disk / in MCP requests)

The JSON document. See [contracts/theme-file-v2.md](contracts/theme-file-v2.md).

| Field | Type | Rules |
|---|---|---|
| formatVersion | int | 1 or 2 |
| id | string | `^[a-z0-9]+(-[a-z0-9]+)*$`, not `system`, unique across built-in + user themes |
| name | string | 1–40 chars |
| kind | `dark` \| `light` | |
| colors | object | exactly the catalogue tokens; `#RRGGBB` / `#AARRGGBB`; opaque except translucent tokens |
| font | object? | v2 only; `{ "family": string(1–100) }` |
| radius | object? | v2 only; `{ "scale": number 0–2 = 1, "steps": { <step>: number 0–24 } }` |

## SkinGeometry (value, `Nota.Application`)

What a theme does to type and corners, once validated.

- `FontFamily: string?` — null = Geist.
- `Roundness: double` — 0–2, default 1.
- `StepOverrides: IReadOnlyDictionary<RadiusStep, double>` — exact pixels.
- `RadiusOf(step)` → override, else round(design[step] × Roundness).
- `Default` — no font, roundness 1, no overrides (what every v1 theme and built-in has).

`RadiusStep`: `Card 9, Group 7, Field 6, Cell 5, ValueBar 4, Switch 3, Fine 2` (design values,
style guide spec 038). `Docked` (0) is not a step and is never changed.

## ThemeValidation (value, `Nota.Application`)

Result of validating one theme document.

- `IsValid: bool` — no problems.
- `Problems: List<ThemeProblem>` — `{ Rule: "F1".."F6" | "V2-font" | "V2-radius" | "JSON", Field: string? (token / key path), Message: string }`. Every problem found, not only the first.
- `Warnings: List<ThemeWarning>` — `{ Kind: Readability | FontMissing, Pairing: "T3.1".., Tokens: string[], Measured: double, Minimum: double, Message }`.
- `Theme: ParsedTheme?` — id, name, kind, colours (`uint` ARGB by token), `SkinGeometry`; set when valid.

Readability warnings are computed only when the colours are valid. `FontMissing` needs the
installed-fonts list, so the app layer adds it (the pure validator takes an optional
`Func<string,bool> fontInstalled`).

## NotaColourTheme (existing, `Nota.App`) — extended

Adds:

- `Geometry: SkinGeometry` (Default for built-ins).
- `Source: ThemeSource` — `BuiltIn` | `User(path)` | `TryOn` | `Preview`.
- `Warnings: IReadOnlyList<ThemeWarning>` (empty for built-ins).

## User theme registry (`NotaThemes`, existing static) — extended

- `BuiltIn` (unchanged), `User` (loaded from the folder), `All` = BuiltIn then User by name.
- `UserLoadProblems: List<(string FileName, string Reason)>`.
- `ThemesFolder` = `NotaPaths.DataDir/Themes`.
- `ReloadUser()` — re-read the folder; replaces `User` and `UserLoadProblems`; raises
  `ThemesChanged`.
- `Find(id)` searches `All`.

Load order for the folder: `*.json` by file name (ordinal), size ≤ 256 KB, then validation;
ids already taken (built-in or earlier file) → problem "duplicate id".

## Theme state (`NotaThemeService`, existing) — extended

```
           Set(choice) / reload
 stored ──────────────────────────▶ worn = Find(choice.Active(os)) ?? fallback
   │                                      ▲
   │  TryOn(theme)                        │ RevertTryOn() / Set(choice) / restart
   └────────────▶ worn = theme (not stored) ┘
```

- `Choice` (stored), `TryOnTheme: NotaColourTheme?` (not stored).
- `Geometry` — the worn theme's `SkinGeometry`; `GeometryChanged` → re-coerce radii and
  fonts in every top level, then repaint (R2, R3).
- `RenderScoped(theme, Action)` — wear `theme` for the duration of the action within one UI
  job, then restore (previews, R6).

## Preview surface (`Nota.App`)

`{ Name: string (stable, kebab-case), Description: string, Size: (w,h) px, Build: (sampleEngine) → Control }`. The list: [contracts/preview-surfaces.md](contracts/preview-surfaces.md).

## Sample scene (`Nota.App`)

A private `NotaEngine` (never started) plus the view-models the surfaces need, built once
per app run on the first preview request. Content is fixed (see research R6).
