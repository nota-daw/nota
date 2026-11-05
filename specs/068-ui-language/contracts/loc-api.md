# Contract: `Loc` call surface

What contributors write. Lives in `Nota.Application` (`Nota.Application.Localization`).
Signatures are illustrative of the contract, not final code.

## Calls

```csharp
Loc.T("Save")                                   // simple text
Loc.T("Open", context: "menu")                  // same English word, different meaning
Loc.F("Delete {0} clips?", count)               // text with values
Loc.FC("verb", "Open {0}", name)                // the same, with a context
Loc.Plural(n, "{0} track selected", "{0} tracks selected")   // singular / plural
```

| Call | Returns | Notes |
|---|---|---|
| `T(text, context?)` | translated text, else `text` | The common case. |
| `F(text, args…)` | translated pattern formatted with `args` | Placeholders `{0}`, `{1}`. Numbers format with the thread's current culture, exactly as the `$"…"` strings they replace did; the app installs a display culture (point separator, real minus sign) at start-up and the language never changes it, so choosing French alters no number (R4). A pattern that cannot be formatted falls back to the English one, never an exception. |
| `FC(context, text, args…)` | as `F`, with a context | C# cannot put an optional argument after `params`, so the context-taking form has its own name. |
| `Plural(n, one, other, context?)` | the plural form for `n` in the running language, formatted with `n` as `{0}` | Key is the `other` form. `n` is an integer count. |
| `Install(table, languageCode)` | — | Called once at startup by the app. Not called by feature code. |
| `UsedBeforeInstall` | bool | True if any text was asked for before `Install`; the app logs it, the smoke test watches it (R6). |
| `ResetForTests()` | — | Back to the uninstalled English state. Tests only. |
| `Language` | the running code | Read-only after install. |

## Behaviour guarantees

1. **English is identical.** With English running, every call returns exactly the text
   passed in (after formatting), so wrapping a literal never changes English output.
2. **Never blank, never a key.** A missing entry returns the English text. The context
   suffix is never shown.
3. **Whole sentences.** Text with values is one pattern, so the translator controls word
   order, gender and plural agreement. Do not build a sentence by concatenating
   `Loc.T` pieces.
4. **Not for data.** Do not wrap user content, device/parameter names, unit symbols or
   proper nouns. Those go in the ratchet allow-list or stay as plain literals.
5. **Thread-safe reads.** The installed table is immutable; calls are safe from any thread.
6. **Install first.** `Loc.T` before `Install` returns English and sets `UsedBeforeInstall`,
   which the app logs (R6).
7. **Literal text only.** The text passed to `T` / `F` / `FC` / `Plural` is a plain string
   literal, never `$"…"`: interpolated text cannot be extracted or translated, and the smoke
   test fails on it. Put the value in a `{0}` placeholder and use `F`.

## Where it applies

| Surface | How |
|---|---|
| Code-built views (most of `Nota.App`) | wrap the literal: `Text = Loc.T("Colour theme")` |
| View models (`Nota.Presentation`) | same; texts that depend on state are built with `F` / `Plural` |
| Command registry / palette titles (`Nota.Application`) | wrap at the definition |
| User-facing messages from Infrastructure | wrap where the message is created |
| The three `.axaml` files | set from code-behind with `Loc.T`, or one markup extension if many (R8) |

## Conventions

- Sentence case, as in DESIGN.md § Labels and copy; the French value follows the same rule.
- Keep keyboard shortcut keys out of the text (`"Play"`, not `"Play (Space)"`): the shortcut
  hint is appended by the code that knows the binding.
- A context tag is a short lower-case word saying where or what (`menu`, `clip`, `verb`);
  add one only when the same English text needs two translations.
