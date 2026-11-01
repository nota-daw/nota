# Research: Interface Language Selector

Phase 0 decisions. The spec's open questions were already settled with the requester
(restart to apply; device and parameter names stay English). What remains are technical
choices.

## R1 — Where the lookup lives

**Decision**: a static `Loc` in `Nota.Application`, fed with a parsed table by `Nota.App`
at startup.

**Rationale**: user text is produced in Application (command palette, status), Presentation
(view models) and App (views, menus); Infrastructure also surfaces some messages. All of
them already reference Application, and `Loc` needs no UI types. A static entry point is
what makes wrapping ~2–3k literals a one-token edit (`"Save"` → `Loc.T("Save")`) instead of
threading a service through every constructor.

**Alternatives**: an injected `ILocalizer` (cleaner for tests, but a constructor change in
dozens of views built by hand in code); a helper in `Nota.App` only (unreachable from
Application and Presentation).

## R2 — Key style: English text as the key

**Decision**: the English source text is the key; there are no symbolic ids. A short
context tag disambiguates the few words with two meanings (see
[contracts/loc-api.md](contracts/loc-api.md)).

**Rationale**: it keeps the code readable, makes the English fallback automatic (no table
needed for English, which satisfies "wrapping never changes English output"), and avoids
inventing and maintaining ~3k identifiers. The cost is that rewording an English string
orphans its translation; the orphan/coverage check turns that into a build failure
instead of a silent regression.

**Alternatives**: symbolic keys (`menu.file.save`) — stable across rewording but need an
English table, a naming scheme and constant churn; resource-class accessors — generated
code and designer files for a code-built UI.

## R3 — File format and loader

**Decision**: one JSON file per language, embedded in `Nota.App`, parsed with
`System.Text.Json`. Same mechanism as the colour themes (`Nota.Themes.*.json`).

**Rationale**: no dependency, contributor-friendly (plain text, diff-able, one file to add
a language: FR-011), no code generation, and the smoke test can read the same files from
disk exactly as it reads the themes. Plural forms and context fit naturally.

**Alternatives**: `.resx` + `ResourceManager` (built in, satellite assemblies, but
XML-heavy, key-by-identifier by convention, awkward to diff and to add plural forms to);
gettext `.po` (excellent tooling but needs a parser dependency or a hand-written one, and
a new licence row); Avalonia `x:Static` resource dictionaries (XAML only; most UI here is
C#).

## R4 — Culture, numbers and plurals (revised during implementation)

**Decision**: do **not** change `CultureInfo.CurrentCulture`, and let `Loc.F` / `Loc.Plural`
format with it. The interface language is `Loc`'s own state. Plural selection uses a small
per-language rule table in `UiLanguage` (English: one when n = 1; French: one when n is 0 or
1, other otherwise).

**Rationale**: ~150 places format with the current culture and ~130 with the invariant one.
Wrapping an interpolated `$"{x:0.0} dB"` in `Loc.F` must not change what is shown, so `Loc.F`
formats exactly as the interpolation did — in the display culture the app installs at
start-up (`NotaNum`: a point, a real minus sign). The language never touches that culture,
so every number reads the same in English and in French. The spec's "notation is not localised"
therefore holds: this feature changes no number. (The first draft said invariant culture; that
would have changed what is shown, as soon as a string was wrapped, wherever the thread culture is not invariant, e.g. in the smoke test).) Typed entry
already accepts both "." and "," (`ValueText.cs`, `BrowserView.cs`).

**Alternatives**: set the culture with the language (a French UI would show decimal commas, against the spec); invariant formatting in `Loc.F` (changes
existing displays on comma locales); ICU/CLDR plural data via a library (a dependency for two
rules).

## R5 — Applying the change: restart and relaunch

**Decision**: the language is a drop-down list. Choosing one writes the setting and shows,
on the same row (no popup), the note "Applies after restart" and a small **Restart** button.
*Restart* goes through the normal close path (unsaved-changes prompt),
then relaunches via a small detached helper started before exit. Per platform:
macOS — `open -n` on the enclosing `.app` bundle (found by walking up from the process
path); Windows — start the same executable; Linux — start the same executable. If the
relaunch target cannot be determined (e.g. a `dotnet run` dev session), the note reads
"Applies the next time Nota starts" and there is no button. No existing restart code was found
in the repo, so this is new but small.

**Rationale**: restart-to-apply was the requester's choice, and it removes the hardest
problem in this feature — rebuilding a mostly code-built UI in place and re-evaluating
static tables. It also keeps every window consistent (spec edge case).

**Alternatives**: live re-render (rejected by the requester; every code-built view would
need a rebuild hook).

## R6 — Initialisation order

**Decision**: resolve `ISettingsService`, call `Loc.Install(...)`, then continue startup,
before any view model, window or static table that holds text is touched. Place it beside
the existing theme and interface-scale application in `App.axaml.cs`. A debug flag set
when `Loc.T` runs before install, asserted false by the smoke test, catches regressions.

**Rationale**: static state cannot be re-translated after the fact; this is the only
ordering constraint the restart model leaves us.

## R7 — Keeping coverage honest

**Decision**: two checks in the smoke test, reusing the repo's existing technique of
reading source literals as text (spec 012, `DesignTokenCheck.Labels`):

1. **Coverage**: every key found in a `Loc.*` call in source has a French entry; every
   French entry is used (no orphans); `{0}`/`{1}` placeholders match between key and
   value; plural entries have `one` and `other`.
2. **Ratchet**: literals handed straight to Text/Content/Header/Title/ToolTip/command
   titles without `Loc` are listed by path and exact literal in
   `LocalizationAllowList.txt`; the list can only shrink. When slice 5 is done it holds
   only data (user content, device/parameter names, proper nouns, unit symbols).

**Rationale**: this is the only thing that turns "translate everything" and the standing
rule FR-012 into something a build can fail on. It reuses a pattern the maintainers
already run.

**Alternatives**: a Roslyn analyzer (new tooling/dependency); manual review (drifts, as
spec 012's history shows).

## R8 — Avalonia XAML and the three `.axaml` files

**Decision**: only a handful of texts live in XAML (`MainWindow.axaml`, `App.axaml`,
`NotaTheme.axaml`). Set them from code-behind through `Loc.T`, or use one tiny markup
extension if more than a few appear. Decide in slice 2 when the real count is known.

## R9 — Verification aids

**Decision**: add an environment override (`NOTA_UI_LANGUAGE=fr`) read at startup, used by
the smoke test, headless screenshots and the layout pass. It never persists to settings.
A pseudo-language (text padded ~30 %) is optional for layout work; not required to ship.

**Rationale**: lets the layout pass and the docs screenshots run in French without
touching a real user's settings file.

## R10 — French terminology

**Decision**: a short glossary kept at the top of `fr.json` notes (or in CONTRIBUTING) fixing
the music terms once (track → piste, clip → clip, scene → scène, send → envoi, return →
retour, bus, warp, quantize → quantification, etc.) and the register (tutoiement or
vouvoiement, one choice). The maintainers' choice is needed before slice 1 text is final.
The plan assumes actions use the infinitive, as most French software does ("Enregistrer",
"Supprimer la piste"), and that explanatory text avoids addressing the user directly
where it can, so no tu/vous decision has to be made string by string.

**Rationale**: SC-001 and FR-013 need one consistent vocabulary; deciding it once is
cheaper than fixing 2k strings later.

## R11 — Texts that reach the user from Application and Infrastructure (T038)

**Decision**: a text is translated where it is *made*, so that every layer that hands text to
the UI returns the language that is running. Wrapped: the command catalog, palette and
browser texts; the MIDI tools (names, parameters, choices, descriptions); the built-in
device descriptions and short kinds, the browser shelf names (`DeviceCategories.Display`, the
English name stays the identifier) and the Modular node kinds and descriptions; the snap-grid
labels; the CPU units; preset-audition reasons, drum-kit warnings and preset-application
errors; progress and status text ("Unpacking…", "Installing…", "Downloading…", "Preparing…",
"Separating stems…"); the AI model names and descriptions; every `StoreException` message
(disk space, damaged or oversized downloads, unsafe archive, update failures); the
project-history "newer version" message.

**Left on purpose** (each is on the allow-list as `# data:` or is outside the ratchet):

- **Logs and diagnostics**: everything written through `ILogSink`, `Console`, `Debug`, theme-file
  validation errors, the IME-log filter. English, so reports and issues read the same.
- **Exception text from the framework or the OS** shown after a translated lead-in
  ("Couldn't download the update: {0}"): the part after the colon is technical.
- **MCP tools and the Remote phone page**: not part of this feature (spec § Out of scope).
- **Content the project does not own**: the plug-in, sample and model registries' own names
  and descriptions (they come from GitHub), factory preset names and shelves, the Grain
  sources, the user's track, clip, scene and section names.
- **Device, parameter and value names** (FR-004), scale names, and the readout lines of device
  cards that spell parameter names ("reso 40 % · env +12"); long tooltips and descriptions of
  those cards *are* translated and keep the parameter names inside the sentence.
- **Search keywords and aliases** of the palette and the browser (English synonyms): a French
  user finds a command by its French title and, for now, by the English synonyms too.
- **Identifiers**: preference page ids, palette view ids, browser shelf and preset-folder ids,
  file and folder names (`Nota Export`, `Downloaded`), AppleScript, the default names of
  tracks that the engine stores (`Audio 3`).

## R12 — Display text that is persisted or compared (T047)

**Audit**: nothing in `Nota.Domain`, the project, preset and settings writers
(`Nota.Infrastructure/Persistence/Project*`, `PresetModel/Store/Library`, `Recovery*`,
`AudioClipState`, `Settings/`) uses `Loc`; the smoke test now enforces it (*no project, preset
or settings writer uses Loc*). Display text that looked like data was split into id and label:

- browser sections keep `BUILT-IN` / `PLUG-INS` as the folding key and show the translated name;
- browser shelves keep the English category name as the filter and ordering id;
- preset folders keep `presetcat:` / `presetdev:` ids; palette `view:` ids stay English;
- a track with no stored name shows a derived default ("Audio 3", "Return 2", the instrument's
  name) that is translated **at display time only**; nothing is stored until the user names it.

A few names *are* generated once and then belong to the user — the demo project's "Bass" and
"Drums", the scene "Captured" the Session view names — as the spec's edge case says (a
generated name is ordinary user text afterwards, and switching language never renames it).
The MIDI-import part names and the live-freeze names are stored in English for the same
reason as the track defaults: they are derived from file or track names.

**Consequence**: saving the same project in English and in French gives the same file, except
for those generated names, and switching the language never touches a saved name (SC-005).
