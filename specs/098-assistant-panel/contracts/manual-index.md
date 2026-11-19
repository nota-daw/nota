# Contract: manual index file

`assets/manual/manual-index.json.gz` — gzip of UTF-8 JSON, written by
`scripts/build-manual-index.sh <nota-docs dir>` (smoke flag `--manual-index-build`), embedded
in `Nota.Infrastructure` as `Nota.Manual.manual-index.json.gz`.

```json
{
  "format": 1,
  "docsCommit": "849f1fb…",
  "builtFor": "0.60.0",
  "languages": ["en", "ru"],
  "sections": [
    {
      "id": "en/devices/audio-effects/compressor#sidechain",
      "lang": "en",
      "page": "devices/audio-effects/compressor",
      "pageTitle": "Nota Compressor",
      "heading": "Sidechain",
      "slug": "sidechain",
      "markdown": "Turn on **Sidechain** and pick the source…",
      "twin": null,
      "howTo": true,
      "explanatory": false,
      "steps": [
        { "anchor": "ui:device.compressor.sidechain", "text": "Turn on **Sidechain** …" },
        { "anchor": "ui:device.compressor.sidechain-source", "text": "Pick the kick track …" }
      ]
    }
  ]
}
```

## Builder rules

1. Pages: every `*.mdx` / `*.md` under `src/content/docs/`; a top-level folder named for one of
   Starlight's `locales` in `astro.config.mjs` (`ru`, `fr`, …) holds that language; everything
   else is English (so `ai/` is a section, not a language). `index.mdx`
   pages use the folder slug.
2. Frontmatter: `title` → `pageTitle`; pages with `draft: true` or `status: todo` are skipped.
3. Sections: the intro (before the first `##`, kept only when it has text) and each `##`.
4. MDX → Markdown (research R2): drop `import` lines, `<Shot …/>`, `<ShotRow>…</ShotRow>`;
   `<Kbd>X</Kbd>` → `<kbd>X</kbd>`; `<Aside type="t" title="T">…</Aside>` → `> [!T]` block
   (`note`, `tip`, `caution`, `danger`); unwrap `<Steps>`; JSX comments removed; relative links
   (`../foo/`) rewritten to page slugs (`nota-manual:<page>#<slug>`), absolute links kept.
5. `<At id="x" />` is removed from the text; each occurrence adds a step whose text is the
   enclosing list item, or else the enclosing sentence. `id="none"` sets `explanatory`.
6. How-to rule (research R6) sets `howTo`.
7. Twins: a translated page's sections pair with the English page's sections by position
   (intro ↔ intro, n-th `##` ↔ n-th `##`), and a translation is a how-to exactly when its English
   section is. A translated page whose `##` count differs from the English page (a translation
   that fell behind) is left out with a warning; questions in that language are answered from
   its other pages. A translated page with no English page is an error.
8. Output sorted by `id`; the file is byte-for-byte reproducible for the same docs commit.

## Reader rules

- Unknown `format` → unavailable; malformed JSON → unavailable; both logged once.
- `nota-manual:` links resolve to `NotaDocs.Url(page) + "#" + slug` in the section's language.

## Smoke checks (`--assistant`)

- The embedded index loads, has `en` and `ru`, and ≥ 1 500 sections.
- Every `ui:` anchor is in `AnchorRegistry`; every `cmd:` anchor is a `CommandCatalog` id.
- Every how-to, non-explanatory section has ≥ 1 step (SC-007 / FR-020) — or is listed in
  `tests/Nota.SmokeTest/ManualHowToRatchet.txt`, a ratchet that may only shrink while the
  manual's how-tos are being anchored; the feature's FR-020 is met when the list is empty.
- Every translation's anchor list equals its twin's (FR-019a).
- Every registry locator is bound in the app (checked by the headless harness, not the smoke test).
