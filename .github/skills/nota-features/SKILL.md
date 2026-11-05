---
name: nota-features
description: Keep FEATURES.md (the consolidated "what Nota can do" list in the repo root) in sync with the app. Run this WHENEVER you add, change or remove a user-visible capability — a new device or parameter, a new view/panel/window, a new workflow or editing command, a new MCP tool, a new export option, a new platform/build/CI capability — i.e. whenever you write a CHANGELOG [Unreleased] Added/Changed/Removed entry. Also run it from /release in `audit` mode to catch anything that slipped and bump the "as of version" line.
user-invocable: true
---

# Keeping FEATURES.md honest

`FEATURES.md` is the single page that answers "what can Nota do *right now*". It is written
by hand and is not generated from the changelog, so it drifts unless every feature change is
folded into it. It describes **what is implemented in the code**, not plans, and it is a
**current-state** document: no history, no "now", no "previously", no version numbers per
item — that is the CHANGELOG's job.

**Trigger:** you wrote (or are about to write) a `CHANGELOG.md` entry under `[Unreleased]`.
- `Added` → almost always a new bullet or an extension of an existing one.
- `Changed` → rewrite the bullet that describes the old behaviour so it describes the new.
- `Removed` → delete the mention (search the whole file — features get cross-referenced).
- `Fixed` → only if the fix changes what the list claims (a limit lifted, a platform now
  supported). Pure bug fixes, refactors, perf and internals need nothing.

**Argument** (`/nota-features <arg>`, optional): a feature name, a changelog line, or
`audit` (reconcile everything since the version in the file's header — used by `/release`).
No argument → the change you just made in this session.

## 1. Find where it belongs

Read the Contents list at the top of `FEATURES.md` and pick the section by what the user
sees, not by which code you touched:

| Change | Section |
|---|---|
| OS support, installers, auto-update, audio/MIDI backends | Platforms and distribution |
| Projects, saving, version history, transport, tempo, loop, metronome | Project and transport |
| Arrangement / Session / Modular / clip editors / piano roll behaviour | Views |
| Track types, routing, sends, groups, mixer window | Tracks and mixer |
| Note editing, MIDI input, MIDI Learn, controllers | MIDI and virtual instruments |
| Recording, audio clips, warp, stretch, stems | Audio: recording, clips, warp |
| Automation lanes, envelopes, modulation | Automation |
| A built-in instrument / audio effect / MIDI effect / rack | Built-in devices → the matching `###` subsection |
| VST3/AU hosting, plugin scanning, plugin windows | Plugin hosting |
| Browser tabs, sample library, presets, smart samples | Browser and assets |
| Render / bounce / stems export | Export |
| Windows, themes, menus, dialogs, Nota Remote UI | Windows and interface |
| A new Preferences option | Preferences |
| A new or changed MCP tool | MCP / AI control (also mention it on the feature's own bullet if it has one, as existing device bullets do: "MCP `read_sampler` / `set_sampler`") |
| Build scripts, CI, tests, licensing | Other |

Before writing, `grep -n` the file for the feature's name and its key words — the feature
may already be partly described (often it is an extension of an existing bullet, e.g. a new
parameter on a device). **Extend the existing bullet rather than adding a duplicate.** If a
genuinely new top-level area appears, add a `##` section *and* its Contents link (anchor =
GitHub slug of the heading).

## 2. Write it in the file's voice

Match the surrounding bullets — read the neighbours first.
- One bullet per feature; **bold** the feature's name at the start, then a dash or colon and
  what it does. Nested bullets only for sub-features of one thing (see Transport).
- English, present tense, plain language a musician understands. State capabilities and
  user-facing names (menu items, tab names, button labels exactly as in the UI), shortcuts
  in the app's glyphs (⌘ ⇧ ⌥, "Cmd/Ctrl" where the file already does).
- Concrete over vague: parameter names, modes, counts ("25 factory presets", "Poly 16 /
  Mono / Legato") — but verify each number in the code/presets, don't copy it blindly from
  the changelog.
- Condense. The changelog entry explains the change; FEATURES states the result in roughly
  half the words. No rationale, no "fixes", no internal class names (file paths only in
  Other / Platforms, as already done there).
- Built-in devices: follow the existing device bullet shape — what it is · its main controls
  and modes · tabs/sizes of the card · preset count · MCP tools if any.
- Hard-wrap at ~90 columns like the rest of the file.

## 3. Verify against the code

The changelog can be ahead of, or behind, reality. For each claim you add, confirm it in the
source (device kind registration, the view/menu code, the MCP tool list in the MCP server,
the preset folder). If something in the changelog was later reverted or is behind a flag
that is off by default, leave it out (or say "experimental, off by default" if the user can
turn it on).

## 4. `audit` mode (from /release)

1. Read the version in the header line ("as of version **X.Y.Z**").
2. Collect every `CHANGELOG.md` entry newer than that version: all `## [A.B.C]` sections
   above `## [X.Y.Z]`, plus `[Unreleased]` / the section being released right now.
3. For each `Added` / `Changed` / `Removed` entry (and relevant `Fixed`), check whether
   `FEATURES.md` reflects it; fix what is missing or stale per sections 1–3.
4. Update the header: set the version to the one being released (or the latest released
   version when run outside a release), keep "(plus changes in development on `main`)".
5. Report the list of entries you added/changed in FEATURES.md and anything you
   deliberately skipped (and why).

## 5. Finish

- Re-read the edited section as a whole: no duplicates, no contradictions with other
  sections, Contents still matches the `##` headings.
- The SPDX header on line 1 stays.
- Commit FEATURES.md together with the feature (or the release commit in audit mode);
  don't make a separate commit unless the user asks.

## Don't
- Don't paste changelog entries verbatim, and don't add dates or version tags to items.
- Don't describe planned, unmerged or disabled-by-default work as available.
- Don't reorganize or restyle sections you weren't asked to touch.
