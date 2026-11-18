# Research: Assistant panel (098)

Each entry: **Decision** · **Rationale** · **Alternatives considered**.

## R1. Where the manual index comes from and how it ships

- **Decision**: A build tool reads `../nota-docs/src/content/docs` (EN + every translation
  folder) and writes `assets/manual/manual-index.json.gz`, committed to this repo and
  embedded in `Nota.Infrastructure` as a resource. Run by `scripts/build-manual-index.sh
  <docs dir>` (a smoke-test flag, `--manual-index-build`, does the work) whenever the
  manual changes for a release; the `release` skill's `prepare` phase runs it. The index
  records the nota-docs commit it was built from.
- **Rationale**: FR-006 (ships per release, offline) and the clarification (never
  downloaded). Committing the artefact keeps `scripts/build.sh` and CI free of a nota-docs
  checkout, and lets the smoke test check every anchor on every build (FR-019). Embedding in
  `Nota.Infrastructure` makes it readable from the smoke test, which does not reference
  `Nota.App`.
- **Alternatives**: building the index in CI from a nota-docs checkout (adds a cross-repo
  fetch to every build); shipping it as a loose file next to the app (three installers to
  touch, easy to lose); fetching it online (ruled out by clarification).

## R2. Section model and MDX → Markdown

- **Decision**: One section = the page intro (text before the first `##`) or one `##`
  heading with everything under it (`###` stays inside). MDX is reduced to plain Markdown:
  frontmatter, `import` lines and `<Shot>`/`<ShotRow>` removed; `<Kbd>X</Kbd>` → `<kbd>X</kbd>`
  (an inline HTML tag Markdig keeps); Starlight `<Aside type="note|tip|caution|danger">` →
  GitHub alert blocks (`> [!NOTE]` …); `<Steps>` unwrapped to its ordered list; `<At …/>`
  removed from the text and recorded as a step (R5). Section id = `<lang>/<page>#<heading slug>`
  using Starlight's slug rule (lower-case, spaces → `-`, punctuation dropped), so links land on
  the heading.
- **Rationale**: matches what users see on a page; `##` granularity gives quotes short enough
  for the panel (FR-009) while staying meaningful. Slugs match the site's own anchors.
- **Alternatives**: whole pages (too long to quote); paragraphs (lose context, many near-duplicates).

## R3. Words-only search (FR-007)

- **Decision**: BM25 over normalized terms, title and heading fields weighted ×3 / ×2.
  Normalisation reuses `Nota.Application.Palette.TextNorm` (lower case, ё→е, diacritics,
  `RuStem`) plus a light English stemmer (plural / -ing / -ed / -ly). Typo tolerance: a query
  term with no exact hit expands to vocabulary terms within one edit (`TextNorm.WithinOneEdit`,
  terms ≥ 4 letters). Wrong keyboard layout: a query that finds nothing is retried through
  `RuToEnLayout` / `EnToRuLayout`, like the palette. The palette's `SemanticVocabulary` words
  add synonyms (EN↔RU, "duck" ↔ "sidechain").
- **Rationale**: no dependency, deterministic, < 5 ms over ~2 000 sections; reuses code that
  already handles Russian the way Nota users type it.
- **Alternatives**: Lucene.NET (heavy, Apache-2.0 but large); SQLite FTS5 (native dependency,
  no Russian stemming out of the box).

## R4. Meaning-based search (FR-008)

- **Decision**: `multilingual-e5-small` (intfloat, MIT; 384-dim, ~118 M params) as the
  int8 ONNX export published at `Xenova/multilingual-e5-small`
  (`onnx/model_quantized.onnx`, 118 308 185 B, sha256 `f80102d3…c193`; `tokenizer.json`,
  17 082 730 B, sha256 `0b44a9d7…4c39`), pinned to commit
  `761b726dd34fb83930e26aab4e9ac3899aa1fa78` and listed in `ModelCatalog` as
  `AiModels.ManualSearch` ("Manual search"). Runs on the ONNX Runtime the store already
  installs. Queries are embedded as `query: …`, sections as `passage: <title> — <heading>.
  <text>` (truncated to 256 tokens), mean-pooled and L2-normalised. Section vectors are
  computed in the background the first time the model is present and cached in the data
  folder keyed by (index hash, model sha); until the cache is ready, search is words-only.
  Final ranking = reciprocal-rank fusion of BM25 and cosine (k = 60).
- **Rationale**: best small multilingual retrieval model with an MIT licence; int8 runs at
  ~15–40 ms per query on a laptop CPU; fusion keeps exact-word hits (control names) on top.
  Caching vectors keeps the generator model-free (R1) and costs one background pass
  (~2 000 sections) per release.
- **Alternatives**: `paraphrase-multilingual-MiniLM-L12-v2` (Apache-2.0, weaker on
  retrieval); LaBSE (470 M, too big); precomputing vectors in the generator (the generator would
  need the model and ORT on every index rebuild).

## R5. Tokenizer for e5 (XLM-R Unigram)

- **Decision**: An in-house SentencePiece **Unigram** tokenizer reading `tokenizer.json`
  (vocab with scores): NFKC normalisation, whitespace → `▁` with a leading `▁` (Metaspace),
  Viterbi best segmentation, `<s>`/`</s>` added, unknown → `<unk>`. Golden token ids for a
  dozen EN/RU strings, produced once with Hugging Face `tokenizers`, are kept in the smoke test.
- **Rationale**: ~150 lines, no dependency; the precompiled charsmap differs from plain NFKC
  only on rare characters, which do not move retrieval quality.
- **Alternatives**: `Microsoft.ML.Tokenizers` (MIT; Unigram support is recent and pulls in
  more surface than needed); onnxruntime-extensions (another native library per platform).

## R6. Anchors in the manual (FR-016, FR-019, FR-019a, FR-020)

- **Decision**: A nota-docs component `<At id="…" />` placed inside a step (a list item or the
  sentence that tells the user to act). It renders a small "Show in Nota" marker on the web
  and nothing else. The index builder records, per section, the ordered list of
  `{ id, text }`, where text is the list item or sentence that holds it. Anchor ids:
  - `ui:<name>` — a control in the app's anchor registry (e.g. `ui:transport.mixer`).
  - `cmd:<command id>` — a key-action / command (e.g. `cmd:view.mixer`); Show me highlights
    the control bound to the command when there is one, otherwise shows the user's current
    key binding and menu path.
  - `<At id="none" />` once in a section declares it explanatory (no steps).
  A section is a **how-to** when it contains a `<Steps>` block, an ordered list, or a sentence
  starting with an action verb (EN: Click, Double-click, Right-click, Press, Hold, Drag, Drop,
  Choose, Select, Open, Turn, Type, Set, Pick; RU: Нажмите, Щёлкните, Дважды щёлкните,
  Перетащите, Выберите, Откройте, Включите, Удерживайте, Введите, Задайте). The smoke test
  fails when a how-to section has no anchor and no `none`, when an anchor id is unknown, or when
  a translation's anchor list differs from the English section's.
- **Rationale**: anchors stay out of the prose (translators keep them as-is), the check is
  decidable without reading intent, and `none` gives authors an explicit escape.
- **Alternatives**: a `steps` prop duplicating the step text (two copies drift); HTML comments
  (not valid MDX); a hand-kept list of how-to sections in `coverage.yml` (drifts).

## R7. Anchor registry and highlighting

- **Decision**: `Nota.Presentation.Assistant.AnchorRegistry` — data, no Avalonia: id → label,
  the view it lives in (`Arrangement` / `Session` / `Modular` / any), what has to be shown
  first (a list of *reveal* steps: `view:<name>`, `panel:mixer`, `panel:detail`,
  `detail:<tab>`, `browser:<tab>`, `prefs:<page>`, `device:<kind>` = "a device of this kind
  on the selected track"), and a locator name. `Nota.App` binds locators to live controls
  (a `Func<Control?>` per name, registered by the views that own them) and draws the
  highlight as an adorner: a 2 px accent outline with the accent-soft fill, radius from the
  control's look, pulsing once. Reveal steps only change what is visible; a `device:` step that
  is not satisfied shows the step text "Add a … first" and highlights where to add it
  (FR-018, US3 #4). Command anchors resolve to a control through the toolbar / menu mapping
  the registry holds.
- **Rationale**: a pure-data registry is checkable in the smoke test (FR-019) and reusable by
  #607; live lookup handles controls that are rebuilt (device cards).
- **Alternatives**: `x:Name` lookup by reflection (names are private, unstable across
  refactors); Avalonia `AutomationId` alone (no reveal semantics).

## R8. Markdown rendering (FR-012 – FR-015)

- **Decision**: Markdig (BSD-2-Clause, latest 1.x) parses with the pipe-tables, alert-blocks,
  emphasis-extras (strikethrough) and auto-links extensions. `Nota.App.Assistant.MarkdownView`
  walks the AST into Avalonia controls: headings / paragraphs as `SelectableTextBlock` with
  inlines; lists as a grid of marker + content; block quotes and alerts as bordered boxes
  (alert edges from existing tokens: note → `BorderStrong`, tip → `Success`, caution → `Accent`, danger → `Danger`; the palette has no info / warning roles and this feature adds none); fenced code in a
  bordered box with a horizontal `ScrollViewer` and Geist Mono; tables as a `Grid` in a
  horizontal `ScrollViewer`; `<kbd>` as a key cap (inline container, the manual's Kbd look).
  Links are runs with an `Href`; a click on a `SelectableTextBlock` hit-tests the text layout to
  find the run. Every size, colour and radius comes from theme resources, so theme and look
  changes apply live.
- **Rationale**: Markdig is the de-facto .NET Markdown parser, permissive licence, no native
  code; rendering to Avalonia controls ourselves keeps the style guide in charge.
- **Alternatives**: Markdown.Avalonia (MIT, but its own styling model and Avalonia-version lag);
  hand-written parser (tables, nesting and alerts are where hand parsers break).

## R9. Panel placement, toggle and persistence

- **Decision**: `BodyGrid` moves into a new outer grid with columns `*`, a 6 px splitter over
  the seam (the browser splitter's pattern) and the panel column (default 360, min 280, max
  560). New command `view.assistant` ("Show Assistant", View category) with default
  **⌘⇧H / Ctrl+Shift+H** (free on all three platforms; F1 is free too but needs Fn on Mac
  laptops). Toolbar `AssistantToggle` sits after `MixerToggle`; `ToolbarPart.Assistant` sheds
  right after `Mixer`. `Settings.AssistantShown` and `Settings.AssistantWidth` persist.
  While the input has focus, `MainWindow.Input` skips app keys and the computer-keyboard note
  map (the existing text-box guard).
- **Rationale**: FR-001–FR-005 and the clarification; reuses #516 / spec 097 patterns so the
  overflow menu, shortcut screen and tooltip all follow.
- **Alternatives**: an overlay panel (rejected in clarification); a View-menu entry only
  (issue asks for a toolbar button).

## R10. Threads and startup (FR-021, SC-003)

- **Decision**: nothing loads at startup. On first open, a background task reads and
  decompresses the index (~0.4 MB gz) and builds the BM25 tables; questions asked before it is
  ready wait on it. The e5 session is created on a background thread with
  `IntraOpNumThreads = 2` and `ThreadPriority.BelowNormal`, unloaded after 10 minutes idle.
  Section-vector pass runs at below-normal priority in batches of 16 and pauses while the
  transport is recording.
- **Rationale**: keeps engine cores free and memory down (#252); nothing on the audio thread.

## R11. Reply wording (FR-009 – FR-011)

- **Decision**: a reply is Markdown built from fixed localised phrases: one intro line, then up
  to three quoted sections — `### <page title> › <heading>`, the section's first ~600
  characters cut at a paragraph or sentence end, and a link "Open in the manual" to
  `NotaDocs.Url(<page>)#<slug>` in the answer language. A section with steps adds a Show me
  action under it. "Good enough" = fused score ≥ a threshold tuned on the SC-001 question set;
  below it the "nothing matches" reply (FR-011).
- **Rationale**: quotes only, no generated text (spec Assumptions); the threshold is tuned
  against the same set SC-001 measures.
