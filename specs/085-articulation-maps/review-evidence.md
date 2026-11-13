# PR #549 documentation and evidence

Approved issue: [#547](https://github.com/nota-daw/nota/issues/547).
PR: [#549](https://github.com/nota-daw/nota/pull/549).
Prepared using `.claude/skills/nota-pr/SKILL.md` and `.claude/skills/nota-docs/SKILL.md`.

## User documentation

The English and Russian articulation-map manual pages explain import, per-note assignment,
colours, track/articulation offsets, trigger lead, editing, export, project persistence and
the timing limits at playback/loop starts. The related piano-roll and track pages link to them.
The native recipient gate, rack restrictions, unsupported `.soundvariation`, existing-group
editing, action ordering and deferred measurement/presets are stated explicitly.

- [English guide](https://github.com/thoi1/nota/blob/pr-assets/pr549-documentation/user-guide.md)
- [Russian guide](https://github.com/thoi1/nota/blob/pr-assets/pr549-documentation/user-guide.ru.md)
- [Manual repository patch](https://github.com/thoi1/nota/blob/pr-assets/pr549-documentation/nota-docs.patch)

The patch targets `nota-daw/nota-docs` develop `05732b0`, includes a deterministic
`articulations` scene for its normal screenshot runner, and normalizes Windows path separators
in the docs checker. It is committed separately in the local `../nota-docs` repository and
attached for integration; it has not been published to the upstream manual site.

Validation: three manual screenshots captured; `npm run build` passes (322 pages),
`npm run check` passes with zero errors. Existing documentation warnings and French translation
gaps remain visible in the checker output. No keyboard shortcut was added by this feature.

## UI evidence

[Screenshot index and reproduction](https://github.com/thoi1/nota/blob/pr-assets/pr549-documentation/screenshots.md).
Real Avalonia UI, Studio Dark, 2× rendering, isolated demo data. Inspector and piano-roll
before/after pairs use the same dimensions, theme, track name and note geometry. The before UI
comes from `ad5b1c6f`; the after UI comes from `6c876cf5`. No vendor files or personal data
are included. The demo's MIDI mappings and latency values are illustrative.

## Engine evidence

- [Charts, tables, machine and reproduction](https://github.com/thoi1/nota/blob/pr-assets/pr549-documentation/summary.md)
- [Full canonical CI report](https://github.com/thoi1/nota/blob/pr-assets/pr549-documentation/report.md)
- [All scenario rows as CSV](https://github.com/thoi1/nota/blob/pr-assets/pr549-documentation/engine-comparison.csv)
- [Cross-platform CI](https://github.com/nota-daw/nota/actions/runs/42629193115): Windows, macOS and Linux pass.
- [Performance CI](https://github.com/nota-daw/nota/actions/runs/42629193021): all 63 engine scenarios and all three idle-CPU jobs pass.

Both workflows test source `6c876cf5`; subsequent changes in this documentation batch do not
change engine or application behaviour. Windows full native/managed/smoke/scanner/scan-scale
validation also passed locally; the focused articulation suite passes all 414 checks, including
38 native probes. The render-pool checks remain unchanged.

The latest split/undo instruction change is −0.10% against the base. The attached Windows
before/after timing has overlapping ranges and is reported without claiming a speedup.
The controlled mapped-library benchmark (T072) and remaining convergence items stay open.
