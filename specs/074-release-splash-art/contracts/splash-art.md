# Contract: splash art

This feature has no external API. It has two internal surfaces that other code and tools
rely on.

## 1. Composer (C#, `Nota.App/Theme/SplashArt/SplashArtComposer.cs`)

```text
SplashScene SplashArtComposer.Compose(string version)
```

- **Pure.** No Avalonia types, no I/O, no statics that change. It is safe to call from the
  smoke test, which links the file.
- **Deterministic.** Equal `version` strings give structurally equal scenes (record
  equality over all fields, coordinates rounded to 0.1).
- **Total.** Any string, including empty or non-semver, returns a valid, non-empty scene.
  It never throws.
- **Version-sensitive.** For `a.b.p` and `a.b.(p+1)`, `Family` differs.

## 2. Renderer (Avalonia, `Nota.App/Theme/SplashArt/SplashArtView.cs`)

```text
new SplashArtView(SplashScene scene)   // a Control, sized 420×300
```

- Draws `scene.Items` clipped to `scene.ArtRect`, then a 1px `Seam` line on the boundary
  between `ArtRect` and `TextRect`.
- Resolves every `ColorRef` from the live `NotaPalette` slots inside `Render`, with no
  cached colours.
- Uses gradient brushes. This is allowed only in `Theme/SplashArt/` (DESIGN.md rule 6
  exception), and `DesignTokenCheck` enforces that.

`SplashWindow` composes, then hosts `SplashArtView` under the existing content. The
content panel moves into `scene.TextRect`. If anything throws, it falls back to today's
card.

## 3. Review sheet (smoke-test mode)

```text
dotnet run --project tests/Nota.SmokeTest -- --splash-sheet <out.html> [theme-id] [from-version] [count]
```

| Arg | Default | Meaning |
|---|---|---|
| `out.html` | required | Output path, overwritten |
| `theme-id` | `studio-dark` | Any file stem in `Theme/Themes/` |
| `from-version` | current `VERSION` | First version |
| `count` | 40 | Versions in the sheet: 4 next patches, then successive minors `.0` |

- Output: one standalone HTML page. It holds one inline SVG per version (420×300) and a
  caption `version · family[+overlay] · palette mode`.
- Exit code: 0 on success, 1 on a bad theme id or an unwritable path.
- Text is not drawn on the sheet: it shows the art and the text region's ground.
