# Contract: language file

One file per language, `src/managed/Nota.App/Languages/<code>.json`, embedded as
`Nota.Languages.<code>.json`. UTF-8, no BOM. A contributor adds a language by adding one
such file (FR-011, SC-006).

## Shape

```json
{
  "formatVersion": 1,
  "code": "fr",
  "name": "Français",
  "notes": "Free text for translators: glossary, register, conventions.",
  "strings": {
    "Save": "Enregistrer",
    "Delete track": "Supprimer la piste",
    "Open|menu": "Ouvrir",
    "Open|clip": "Ouvrir le clip",
    "{0} track selected": {
      "one": "{0} piste sélectionnée",
      "other": "{0} pistes sélectionnées"
    }
  }
}
```

## Rules

| Rule | Detail |
|---|---|
| `formatVersion` | `1`. A file with a higher version is skipped with a logged error; the language is not offered. |
| `code` | Lower-case, equals the file name. `en` is reserved for English (no `strings`). |
| `name` | The language's own name, shown in the picker. |
| Keys | The English source text exactly as at the call site. A context-tagged key is `"<text>\|<context>"`. Keys are unique; duplicates are an error. |
| Values | A string, or `{ "one": "...", "other": "..." }` for a plural. `other` is required; a missing `one` uses `other`. Other plural categories may be added later per language; unknown categories are ignored. An empty string counts as missing (the English text is shown). A key used by `Loc.Plural` must be a plural entry with both forms, and a key used by `T` / `F` must not be (smoke test). |
| Placeholders | `{0}`, `{1}`, … The value (the `other` form) must use exactly the placeholders its key uses, in any order. The `one` form may drop a placeholder ("Une piste") but may not add one. A literal brace is written `{{` / `}}`. A value that cannot be formatted falls back to the English text at run time. |
| Missing key | Not an error at run time: the English text is shown (FR-007). It *is* a failure in the smoke test for `fr` (coverage). |
| Unknown / extra keys | Ignored at run time. Reported by the smoke test as orphans for `fr`. |
| Malformed file | The language is skipped, an error is logged (as theme load errors are), English remains. Nota never fails to start because of a language file. |
| Comments | JSON has none; use `notes`. No SPDX header, matching the colour-theme JSON files. |

## English file

`en.json` has `code`, `name` ("English") and optional `notes`; `strings` is absent or empty.
English text comes from the call site itself.

## Compatibility

Adding keys is always safe. Renaming an English string at its call site orphans the old key
and makes the new one untranslated until the file is updated; the coverage check fails
until both are fixed.
