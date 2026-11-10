# Quickstart: validating user skins (087)

## Prerequisites

- `scripts/build.sh` green (native + managed + smoke test).
- An isolated data folder: `export NOTA_DATA_DIR=/tmp/nota-skins` (themes go to
  `$NOTA_DATA_DIR/Themes`).
- For MCP: Nota running with MCP on (Preferences → AI), an MCP client pointed at
  `http://127.0.0.1:<port>`.

## 1. Validation rules (no UI)

Smoke test group `Skins` (run by `scripts/build.sh`) covers:

- each of the four built-ins round-trips through the new validator with no problems and the
  same T3 results as `DesignTokenCheck.RunThemes`;
- the malformed set from SC-002 (missing / unknown token, bad hex, translucent opaque token,
  bad id, `system` id, bad kind, invalid JSON, v1 with `font`, scale 3, step `huge`, empty
  family) — each yields a problem naming the field; several at once are all reported;
- duplicate ids across files; the 256 KB cap;
- `SkinGeometry.RadiusOf` for scale 0 / 0.5 / 2 and overrides; `Map` keeps 0 at 0 and maps
  each design value to its step.

## 2. Wear a skin (Story 1, 2, 3)

1. Start Nota. Preferences → Appearance → right-click Studio Dark → Duplicate. Expected: a
   "Studio Dark copy" card with a "User" tag is selected; `$NOTA_DATA_DIR/Themes/studio-dark-copy.json` exists.
2. Edit that file: `"formatVersion": 2`, `"font": {"family": "Helvetica"}`,
   `"radius": {"scale": 0}`, change `Panel`. Click Reload. Expected: all text in Helvetica,
   every corner square, the new panel colour, layout sizes unchanged.
3. Pick Studio Dark. Expected: Geist and the normal radii are back.
4. Pick the copy again, restart Nota. Expected: starts in the copy.
5. Set `"family": "No Such Font"`, Reload. Expected: Geist, a warning badge on the card.
6. Delete a token, Reload. Expected: card gone, problems list names the file and token,
   the app shows Studio Dark with the fallback note; fix the file, Reload → back to the copy.

## 3. MCP loop (Story 4)

From the client, in order:

1. `list_themes` → four built-ins + the copy; `worn` is current.
2. `get_theme {id:"studio-light"}` → full v1 document.
3. Change `TextMuted` to `#E0E0E0`, `validate_theme` → valid, a T3.1 warning naming
   `TextMuted` on the light surfaces with measured < 4.5.
4. `preview_theme` with it → 10 images in contract order; the live window does not flicker
   or change. Call again → byte-identical images.
5. `preview_theme {surfaces:["mixer","nope"]}` → one image, `unknown:["nope"]`.
6. `save_theme` with id `studio-light` → refused; with a new id → saved, it appears in
   Preferences without restart; again without `overwrite` → refused.
7. `try_on_theme` → window wears it, Preferences still shows the stored pick;
   `revert_try_on` → back.
8. `apply_theme {id:<new>}` → worn and stored (restart keeps it).

## 4. Evidence for the PR

Before/after screenshots of Preferences → Appearance (nota-pr skill) and one `preview_theme`
image set for a sample skin.
