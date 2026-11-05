# Contract: `Loc` API (after this feature)

Replaces [068 loc-api](../../068-ui-language/contracts/loc-api.md).

```csharp
public static class Loc
{
    string Language { get; }                                   // running code
    TranslationTable English { get; }                          // embedded en.json, loaded once
    void Install(TranslationTable? table, string code);
    string T(string key);                                      // text
    string F(string key, params object?[] args);               // pattern, {0}…
    string Plural(string key, long n);                         // n is {0}
}
```

```xml
<MenuItem Header="{app:T menu.file.save_as}" />
```

| Rule | Detail |
|---|---|
| Argument | Always a string literal key (the smoke test reads keys from source; a computed key is a failure). |
| Lookup | running language entry (valid) → English entry → the key itself + one log line per key (unreachable past the smoke test). |
| `F` | `string.Format(CurrentCulture, …)`; a pattern that fails formatting falls back to English, then to the bare English text (068 behaviour). |
| `Plural` | Form chosen by the running language's `plural` rule; English uses `one_is_1`. |
| Removed | `FC`, every `context` parameter, `Plural(n, one, other)`. |
| Before `Install` | As 068: English is returned and `UsedBeforeInstall` is set. English comes from the embedded `en.json` and is available before `Install`. |

## Language sources at start-up (`NotaLanguages`)

1. `NOTA_UI_LANGUAGE` — a code, or a path to a language file (this run only, never saved).
2. `Settings.Language` code → `<data>/languages/<code>.meta.json`:
   `source: repository` → `<data>/languages/<code>.json`; `source: local` → the stored path.
3. Missing / invalid → English; reason logged and shown in the Language row.

No network access at start-up on this path.
