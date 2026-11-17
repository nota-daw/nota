# Implementation Plan: Assistant panel

**Branch**: `feat/assistant-panel` | **Date**: 2026-11-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/098-assistant-panel/spec.md` (issue
[#606](https://github.com/nota-daw/nota/issues/606), approved)

## Summary

A chat panel docked on the right of the main window, full height of the work area, toggled
by a toolbar button and `view.assistant` (⌘⇧H). A question is answered from a search index of
the user manual that ships inside each release: words-only BM25 always (reusing the palette's
Russian-aware text normalisation), fused with meaning-based search when the optional
`multilingual-e5-small` model is installed from the AI model store. Replies are Markdown
rendered by Markdig into Avalonia controls styled by the theme tokens. Sections whose steps
carry `<At id="…"/>` anchors offer **Show me**, which reveals and outlines each control in
turn through a data-only anchor registry. A build tool turns `../nota-docs` into the committed
index; the smoke test checks every anchor, every how-to and EN/RU anchor parity on every
build. The manual gets anchors on every how-to in a companion nota-docs pull request.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill: panel chrome
  copies the docked Mixer / Detail panel (32 px header on `Brush.Panel`, seam, `Radius.Docked`);
  the toggle copies the Mixer key (#516); text uses the Geist scale; the highlight uses the
  accent "picked" strength.
- **Shortcuts** → `nota-shortcuts` skill: the new action is declared in `CommandCatalog`
  (so `KeyActionCatalog`, Preferences ▸ Shortcuts and the palette pick it up).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): Markdig needs a
  `LICENSES/third-party.md` row; the e5 model is listed by the model store like htdemucs.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only); no C++ change.

**Primary Dependencies**: Avalonia 12 (existing); ONNX Runtime managed (existing);
**Markdig** (new, BSD-2-Clause, NuGet); the `multilingual-e5-small` model (MIT, downloaded
on demand, not a build dependency).

**Storage**: `assets/manual/manual-index.json.gz` (embedded resource in
`Nota.Infrastructure`); model files under the model store's root; the section-vector cache
`<data>/assistant/vectors-<indexHash>-<modelSha8>.bin`; `Settings.AssistantShown`,
`Settings.AssistantWidth`.

**Audio-Thread Impact**: None. No engine change, no command-queue traffic; indexing,
embedding and rendering run on the UI and below-normal background threads (R10).

**Testing**: `tests/Nota.SmokeTest` — new flags `--assistant` (search, tokenizer goldens,
index checks, anchors, Markdown model, toolbar order), `--manual-index-build <docs> <out>`
(tooling), `--assistant-eval [<model dir>]` (SC-001 question set). Headless Avalonia harness
for screenshots (memory recipe `/tmp/<feature>harness`).

**Target Platform**: macOS (arm64, x64), Windows (x64, arm64), Linux (x64, aarch64).

**Project Type**: desktop app.

**Performance Goals**: reply < 1 s (SC-002; search itself < 50 ms, e5 query < 100 ms on the
slowest machine); first open < 1 s (SC-003); no startup change.

**Constraints**: offline; ≤ ~1 MB added to the installer (index + Markdig); e5 RAM ~200 MB
only while loaded, unloaded after 10 min idle.

**Scale/Scope**: ~212 MDX pages (106 EN + RU), ~1 900 sections; anchor registry sized to every
control the manual's how-tos name (estimated 250–400 ids).

## Constitution Check

| Principle | Status |
|---|---|
| 0. Approved issue | ✅ #606 carries `approved`; scope = issue text; #607 out of scope |
| I. Real-time audio safety | ✅ no engine or audio-thread code touched; background work below normal priority, paused while recording |
| II. Boundary discipline | ✅ no C ABI change, no JUCE |
| III. Licensing hygiene | ✅ SPDX headers on new files; Markdig BSD-2 → `LICENSES/third-party.md`; e5 MIT shown in the model store with its source |
| IV. Verification gate | ✅ `--assistant` joins the full smoke run, so `scripts/build.sh` covers anchors and index |
| V. One logical change per PR | ⚠️ app PR here + manual PR in nota-docs (anchors). Split app work into reviewable commits: panel shell, search, Markdown, model, Show me |
| VI. Docs in sync | ✅ CHANGELOG, FEATURES.md (nota-features), new manual page + shortcut (nota-docs, nota-shortcuts) |
| VII. Spec-first | ✅ this spec |
| VIII. Safety rules not weakened | ✅ |

Post-design re-check: unchanged — the design adds no engine surface and one permissive
dependency.

## Project Structure

### Documentation (this feature)

```text
specs/098-assistant-panel/
├── spec.md · plan.md · research.md · data-model.md · quickstart.md
├── contracts/
│   ├── manual-index.md      # index file format
│   ├── anchors.md           # <At> markup, anchor ids, how-to rule, registry
│   └── ui.md                # panel, command, settings, reply shape
├── checklists/requirements.md
└── tasks.md                 # /speckit-tasks
```

### Source Code

```text
assets/manual/manual-index.json.gz                 # generated, committed (R1)
scripts/build-manual-index.sh                      # runs the generator flag
LICENSES/third-party.md                            # + Markdig

src/managed/Nota.Application/
├── Ports/IModelStore.cs                           # + AiModels.ManualSearch
├── Palette/CommandCatalog.cs                      # + view.assistant (⌘⇧H)
├── Settings.cs                                    # + AssistantShown, AssistantWidth
├── Localization/en.json                           # + assistant.* strings
└── Assistant/
    ├── ManualIndex.cs                             # records + reader (data-model)
    ├── ManualSearch.cs                            # BM25 + fusion + language pick
    ├── EnglishStem.cs                             # light stemmer (R3)
    ├── AssistantReply.cs                          # reply builder (R11)
    └── Conversation.cs                            # messages, IAssistantResponder

src/managed/Nota.Infrastructure/
├── Nota.Infrastructure.csproj                     # + embedded index
├── ModelStore/ModelCatalog.cs                     # + e5 entry, extra asset (tokenizer)
├── ModelStore/ModelStore.cs                       # + extra files per model
└── Ai/
    ├── UnigramTokenizer.cs                        # R5
    ├── TextEmbedder.cs                            # e5 session, pooling
    └── SectionVectors.cs                          # background pass + cache

src/managed/Nota.Presentation/
├── ToolbarOverflow.cs                             # + ToolbarPart.Assistant
└── Assistant/AnchorRegistry.cs                    # R7 (data)

src/managed/Nota.App/
├── Nota.App.csproj                                # + Markdig
├── MainWindow.axaml                               # outer grid, AssistantPanel, AssistantToggle
├── MainWindow.Assistant.cs                        # toggle, width, persistence, focus guard
├── MainWindow.Commands.cs / MenuKeys.cs / TransportFit.cs / TransportOverflow.cs
└── Assistant/
    ├── AssistantView.cs                           # conversation UI
    ├── MarkdownView.cs                            # Markdig AST → controls
    ├── ShowMeController.cs                        # walkthrough + reveal
    ├── HighlightAdorner.cs                        # accent outline
    └── AnchorLocators.cs                          # registry names → live controls

tests/Nota.SmokeTest/
├── AssistantTests.cs                              # --assistant
├── ManualIndexBuilder.cs                          # --manual-index-build (MDX → index)
├── AssistantEval.cs + Fixtures/assistant-questions.json   # SC-001
└── Program.cs                                     # flags + full run

../nota-docs (separate PR)
├── src/components/At.astro                        # renders the "Show in Nota" marker
├── src/content/docs/**/*.mdx (+ ru/)              # <At> on every how-to
└── src/content/docs/basics/assistant.mdx (+ ru)   # the panel's own page
```

**Structure Decision**: domain-free logic (index, search, reply, registry) lives in
`Nota.Application` / `Nota.Presentation` so the smoke test can run it without a window; ONNX
and file work in `Nota.Infrastructure`; Avalonia-only code in `Nota.App/Assistant/`. The MDX
parser is tooling and lives in the smoke-test project with its flag.

## Phasing

1. **Shell** — panel, toggle, command, settings, toolbar shedding, empty conversation (US1 #1, US4).
2. **Words-only answers** — index format, generator, embedded index, BM25, replies (US1).
3. **Markdown** — renderer + copy + links (US2).
4. **Meaning-based search** — model entry, tokenizer, embedder, vector cache, fusion (FR-008).
5. **Show me** — `<At>` contract, registry, reveal, highlight, checks (US3).
6. **Manual anchors** — nota-docs component + every how-to, EN + RU; regenerate the index (FR-020).
7. **Docs & polish** — CHANGELOG, FEATURES.md, manual page, shortcuts page, screenshots,
   RU strings to nota-translations, SC-001 eval, `scripts/build.sh`.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Two repositories (app + nota-docs) for one feature | Anchors must live in the manual's source next to the steps they mark (FR-016, FR-020) | Keeping anchors in the app repo as a side table would drift from the prose on every docs edit |
