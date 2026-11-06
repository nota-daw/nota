# Data model: Choose the accent colour

## Settings (settings.json)

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Accent` | string | `""` | `""` or `"orange"` → Orange; a preset id; `"custom"` → `CustomAccent` |
| `CustomAccent` | string | `""` | last custom colour, `#RRGGBB`; kept while a preset is worn |

## AccentChoice (Nota.Application)

`record AccentChoice(string Preset, uint? Custom)` — `Preset` is a preset id or `"custom"`;
`Custom` is `0xRRGGBB` when `Preset == "custom"`.

- `Accent.Resolve(Settings) → AccentChoice` — `""`, unknown id, `"custom"` with a missing or
  malformed `CustomAccent` → Orange. Never throws, never writes.
- `Accent.Store(Settings, AccentChoice)` — writes `Accent` (and `CustomAccent` for custom).
- `AccentChoice.IsDefault` — Orange.

## AccentPreset

`record AccentPreset(string Id, double Chroma, double Hue)` — Orange has no
chroma/hue (identity). The list, in display order: orange, lime, aqua, sky, violet,
magenta, pink (values: research R4). Names are the localization keys `accent.<id>`,
looked up by a literal switch in the Preferences page (the localization check rejects
computed keys).

## Derivation (Nota.Application, pure)

`AccentColours.Wear(IReadOnlyDictionary<string, uint> theme, AccentChoice) → AccentResult`

- `AccentResult { IReadOnlyDictionary<string, uint>? Tokens; bool Adjusted; AccentRefusal? Refusal }`
- `Tokens` holds the accent family (`SolidTokens` + `BlendTokens`) and `TextOnAccent`; the
  caller overlays them on the theme. Orange → `Tokens` = the theme's own values.
- `AccentRefusal { Reason: Grey | Clash | Unreadable; string? ClashWith }` — `ClashWith` is the
  token (`Record`, `Mute`, `Solo`, `Success`).
- `AccentColours.Failures(theme, tokens, includeTracks)` — the rule set R3, for the tests.
- A preset never refuses (asserted by the smoke test); if it ever did, the app falls back to
  Orange for that theme.

Helpers (public for the smoke test): `OkLch(uint)`, `FromOkLch(L, C, h)`, `Contrast`,
`DeltaE76`, `DeltaAb`, `InkOn(uint)`.

## Accented theme (Nota.App)

`NotaAccent.Wear(NotaColourTheme, AccentChoice) → NotaColourTheme` — same `Id`, `Name`,
`Kind`; `Colors` = theme colours with `Tokens` overlaid. Cached per `(Id, AccentChoice)`;
Orange returns the theme object itself.

## State transitions

```text
          pick preset ─────────────► worn + saved
Accent row ─ Custom ─► picker open ─ drag / hex ─► worn (not saved)
                              │ refused colour ─► note, accent unchanged
                              ├ click outside / close ─► saved (Accent = custom, CustomAccent = hex)
                              └ Esc ─► previous accent worn again, nothing saved
theme switch / OS mode flip ─► same AccentChoice worn in the new theme
```
