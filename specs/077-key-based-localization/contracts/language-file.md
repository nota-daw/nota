# Contract: language file (format version 2)

Replaces [068 translation-file](../../068-ui-language/contracts/translation-file.md). One
shape for the English reference (`Nota.Application/Localization/en.json`, embedded in `Nota.Application`), repository files
(`nota-translations/languages/<code>.json`, `reference/<version>.json`) and local files.
UTF-8, no BOM, no SPDX header (licence stated by the repository).

## Shape

```json
{
  "formatVersion": 2,
  "code": "fr",
  "name": "Français",
  "plural": "one_is_0_1",
  "notes": "Glossary, register, conventions for translators.",
  "strings": {
    "common.save": "Enregistrer",
    "menu.file.open": "Ouvrir",
    "clip.open": "Ouvrir le clip",
    "arrangement.selection.tracks": {
      "one": "{0} piste sélectionnée",
      "other": "{0} pistes sélectionnées"
    }
  }
}
```

## Rules

| Rule | Detail |
|---|---|
| `formatVersion` | `2`. `1` (text-keyed) and anything higher are rejected with a logged error; the language is not offered. |
| `code` | Lower-case BCP-47 primary tag (`fr`, `de`, `pt-br` allowed); equals the file name. `en` only for the reference. |
| `name` | The language's own name, shown in the picker. |
| `plural` | A rule id the app knows: `one_is_1`, `one_is_0_1`. Unknown → file rejected. Required except in `en.json` (`one_is_1` implied). |
| Keys | Match `^[a-z0-9_]+(\.[a-z0-9_]+)+$`, ≤ 80 chars, unique. |
| Values | A non-empty string, or a plural object with `other` required; `one` optional (missing → `other`). Unknown categories ignored. An empty string counts as missing (English shown). |
| Shape | A key that is a plural object in the reference must be one here, and the other way round; mismatch → entry invalid (English shown), reported. |
| Placeholders | `{0}`, `{1}`… with optional format (`{0:0.0}`). `other` uses exactly the reference's placeholders, any order; `one` may drop but not add. `{{` `}}` literal. |
| Missing key | Run time: English. Coverage: counted missing. |
| Unknown key (not in the reference checked against) | Run time: ignored. Repository check: error if the key is in **no** reference, warning if only in older ones. |
| Malformed file | Language skipped, error logged, English kept; Nota always starts. |
| `en.json` extra rule | Every value present; `code` = `en`; this file *is* the reference. |

## Coverage

`translated valid entries of reference keys ÷ reference keys`, per reference, rounded down
to a whole percent.

## Key-set hash

SHA-256 (lower-case hex, first 16 chars) of the UTF-8 lines, sorted ordinally and joined with
`\n`: `key \t (text|plural) \t sorted placeholder indices joined by ","`. Computed
identically by the app, the smoke test and `tools/validate.py`.
