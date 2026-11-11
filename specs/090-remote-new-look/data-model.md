# Data model: Nota Remote in the new look, with clip previews

## RemoteTheme (Nota.Remote, extended)

| Field | Type | Notes |
|---|---|---|
| Id, Name, Dark, Colors | as spec 014 | unchanged |
| Font | `string?` | skin's `font.family`; null for built-ins / format 1 |
| Radii | `IReadOnlyDictionary<string,double>?` | 7 keys (`card … fine`), resolved px; null when the design scale |

Built by `MainWindow.Remote.RemoteThemeOf` from `NotaColourTheme.Geometry`
(`SkinGeometry.RadiusOf`, `SkinGeometry.Key`). Cache key already includes the theme
instance; a geometry change yields a new theme instance, so the hub re-sends (G5).

## Phone look state (web, `lib/theme.svelte.ts`)

| Custom property | Source | Default |
|---|---|---|
| `--<token>` (~110) | theme colours | first-paint fallback (spec 014) |
| `--font-ui` | `font` | `Geist, system-ui, -apple-system, sans-serif` |
| `--r-card … --r-fine` | `r` | 9 / 7 / 6 / 5 / 4 / 3 / 2 px |
| `--picked-ink` | `app.css` per `data-theme` | `--accent-bright` dark, `--accent-pale` light |

All of them go into the stored `nota-remote:vars` blob, so a returning phone first-paints
with the last typeface and radii too.

## ClipPreview (hub-side, per slot)

| Field | Type | Notes |
|---|---|---|
| TrackId, Scene | int | key `TrackId * 4096 + Scene` |
| Name | string | `GetSessionClipName` or "<track> <scene+1>" |
| Color | string | `IRemoteHost.ClipColor(track, scene)` |
| LenBeats | double | `SessionSlotLength`, or bars recorded so far while recording |
| Kind | n / p / x | instrument track → notes; audio → peaks; empty → x |
| Notes | `(byte start, byte len, byte pitch)[]` ≤ 128 | 1/64-of-length grid |
| Peaks | `byte[48]` | 0–99 |
| Recording | bool | state 4 / 5 |
| Signature | ulong | FNV-1a over all of the above |

Hub state: `Dictionary<int, ClipPreview> _previews`, a round-robin cursor over
(track, scene), `HashSet<int> _dirty`. Per phone: `Dictionary<int, ulong> SentClips`
(cleared when it leaves Scenes, so re-opening re-sends all — C3).

### Lifecycle

```text
empty ──(clip appears)──▶ filled ──(content/len/name/colour change)──▶ filled (new signature)
  ▲                         │ ──(record starts)──▶ recording ──(stops)──▶ filled
  └────────(cleared)── x ◀──┘
```

## Phone Scenes cell (web)

`app.clips: Map<string, Preview>` keyed `"<id>:<i>"`, filled from `clips` messages (an
`all: true` batch clears it first). The cell draws from `ses` (state, progress) + `clips`
(name, colour, length, content).
