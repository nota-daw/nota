# Quickstart: validating the language selector

Prerequisites: the repo builds (`scripts/build.sh` on macOS; `scripts/build-linux.sh`,
`scripts/build-win.ps1` elsewhere). Commands below are run from the repo root.

## 1. Automated gate

`scripts/build.sh` runs the smoke test. The new `LocalizationCheck` must pass:

| Check | Proves |
|---|---|
| Lookup, context, fallback | a missing or unknown key returns English; context never leaks; nothing is blank |
| Plurals | English: 1 → one, 0/2 → other; French: 0 and 1 → one, 2 → other |
| Format | `Loc.F` keeps invariant number notation; `{{`/`}}` literal braces |
| French coverage | every `Loc.*` key in source has a French entry; no orphan French keys |
| Placeholder parity | each French value uses the same `{n}` set as its key |
| Settings | no stored language → `en`; unknown or invalid value → `en`; file untouched |
| Languages list | picker lists `en` and `fr`, English first, with their own names |
| Install order | `Loc` was never used before install |
| Ratchet | the raw-literal allow-list only shrinks; no new raw label literal |
| English identity | with `en`, a sample of surfaces renders the same text as before |

Expected: all green. The number of allow-listed raw literals is printed; the goal at the end
of the feature is data only.

## 2. Manual: switch to French (User Story 1, 2)

1. Launch Nota (English). Open Preferences → Appearance.
2. In the **Language** drop-down, choose **Français**. Expect, within 1 second and with no
   popup, the note "Applies after restart" and a **Restart** button beside the list; the
   interface is unchanged.
3. Leave it. Expect nothing else changes; the note stays on the row.
4. Quit and relaunch. Expect French from the first frame (splash included where it has text).
5. Walk the app: every menu and context menu, transport, Arrangement, Session, Mixer, Piano
   roll, clip editors, Browser, Preferences, Welcome / About / What's new, a device window,
   the command palette, a confirmation dialog, an error (e.g. open a missing file).
   Expect no English interface text other than device/parameter names, units, proper nouns
   and user content.
6. Open Preferences → Appearance → **Langue** and choose **English**. Expect the same restart
   offer; after restart, all English.

## 3. Manual: restart now (FR-002)

1. With a project containing unsaved edits, choose a language and press **Restart**.
2. Expect the usual save prompt first. Cancel: nothing restarts, the stored language stays
   as chosen. Confirm and save: Nota reopens in the new language.
3. From a dev run where relaunch cannot be determined, expect the note "Applies the next
   time Nota starts" and no button.

## 4. Manual: defaults and safety (User Story 3, 4)

1. Delete the settings file, launch: English, whatever the OS language.
2. Hand-edit the setting to `"zz"`: English, and the stored value stays `"zz"` (the file may be saved
   for other reasons, such as the last version seen) until the setting is changed.
3. Save a project in French, open it in English (and the reverse): same tracks, names, sound;
   user-typed names unchanged; project file content identical apart from the save timestamp.
4. Play a loop, change the language setting: playback is not interrupted.

## 5. Layout in French (FR-010, SC-004)

For each interface scale 75 / 100 / 125 / 150 / 200 %, in French: open the main window,
Preferences and a device detail window. Expect no clipped or overlapping labels and every
control reachable (at scales that fit the screen). The `NOTA_UI_LANGUAGE=fr` override runs
this without touching saved settings.

## 6. Add a language (User Story 5, SC-006)

1. Copy `Languages/fr.json` to `Languages/xx.json`, set `code` / `name`, keep a handful of
   strings.
2. Rebuild, open Preferences → Appearance. Expect "xx" in the list; selecting it (after
   restart) shows those strings translated and everything else in English.
3. Remove the file again.

## 7. Docs (FR-013)

`CHANGELOG.md` `[Unreleased]` has an Added entry; `FEATURES.md` and the user manual mention
the setting; `CONTRIBUTING.md` has "Translating Nota" (how to add a language, how to
translate a string, the rule that new texts go through `Loc`).
