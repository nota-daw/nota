# Contract: persisted formats (spec 092)

Two files change shape. Both changes are additive: an older Nota reads them and ignores
(and, for `project.json`, preserves) the new fields.

## settings JSON (`Nota.Application/Settings.cs`)

```jsonc
{
  "SamplesFolder": "/Users/me/Music/Nota Samples",      // kept: mirror of the default entry's Path
  "SampleFolders": [                                      // new
    { "Path": "",                       "Name": null,     "IsDefault": true  },   // "" = default location
    { "Path": "/Volumes/Lib/Splice",    "Name": "Splice", "IsDefault": false }
  ],
  "FilesBrowseMode": false,                               // new: Files tab shows Tree (false) / Browse (true)
  "FilesBrowseFolder": "/Users/me/Downloads",             // new: last browsed folder
  "ProjectsSort": "Date"                                  // new: "Date" | "Name"
}
```

- Load: `SampleFolders` missing/empty → `[{ Path: SamplesFolder, IsDefault: true }]`.
- Load: zero or several defaults → the first entry becomes the only default.
- Load: an entry equal to / nested in / containing an earlier one is dropped (logged).
- Save: `SamplesFolder = default.Path`.

`ISettingsService`:

| Member | Meaning |
|---|---|
| `string ResolvedSamplesFolder()` | **Unchanged meaning**: the default folder, created if missing. |
| `IReadOnlyList<ResolvedSampleFolder> ResolvedSampleFolders()` | New: every entry → `(Index, Path, Label, IsDefault, Available)`; never creates folders other than the default. |
| `SampleFolderResult AddSampleFolder(string path)` | New: `Added` · `AlreadyPresent(index)` · `Overlaps(index)` · `NotADirectory`. Saves on success. |
| `bool RemoveSampleFolder(int index)` | New: false for the last remaining entry; removing the default promotes the next. |
| `void RenameSampleFolder(int index, string? name)` / `void SetDefaultSampleFolder(int index)` | New; save. |
| `event Action? SampleFoldersChanged` | New: raised after any change; Browser and Preferences rebuild from it. |

## project.json (`Nota.Infrastructure/Persistence/ProjectModel.cs`)

```jsonc
// AudioClipDto (arrangement clip, session slot "Audio") and AudioTakeDto
{
  "Sample": "samples/3f2a….wav",
  "Name": "Kick 01",
  "Source": "/Users/me/Music/Nota Samples/Drums/Kick 01.wav"   // new, optional
}
```

- Written only when the engine knows a source for the clip's sample id.
- No `FormatVersion` change; no migration step.

## IAudioEngine additions (managed only — no C ABI change)

| Member | Meaning |
|---|---|
| `string SampleSource(long sampleId)` | Full path the sample was loaded from, or `""`. |
| `void SetSampleSource(long sampleId, string path)` | Restore on project load. |

Recorded by `AddAudioClip`, `AddAudioClipEx`, `AddSessionAudioClip`, `AddSessionAudioFile`
(and the existing Sampler/Rack/Rhythm loads, for free) unless the path is inside a
`*.nota/samples/` folder.

## Cache: `NOTA_DATA_DIR/cache/project-summaries.json`

Disposable; a missing or corrupt file is rebuilt. Shape in [data-model.md](../data-model.md).
