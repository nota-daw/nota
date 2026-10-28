# Contract: project format — plug-in preview blob

Format **v26** (no-op migration from v25, `ProjectMigrations`), as the project bumps the
version for every schema change.

- `InstrumentDto.Preview` and `DeviceDto.Preview` (`string?`, JSON `preview`, omitted
  when null): a bundle-relative path to a blob registered with `RegisterBlob`, the same
  store and naming as the plug-in `State` blobs (content-hashed, so two copies of a
  device with the same preview share one file).
- Written for any device or instrument whose `GetPluginPreview` is non-empty (only hosted
  plug-ins ever capture one; storing it generically lets the smoke test cover it with
  built-ins).
- On load, after the device is created and its state applied:
  `SetPluginPreview(track, di, ReadBlob(bundle, preview))`; a missing or unreadable blob
  is skipped silently (the card stays plain).
- An instrument's preview is not applied to the stand-in Volt track used when its plug-in
  isn't installed (`TrackInstrumentKind` must match the saved kind).
- An older Nota refuses a v26 project, as with every format bump.
- A device inside a rack chain has no DTO of its own: the rack's blob (`RackCore::serialize`,
  blob **v10**) carries each chain's instrument and device previews after the v9 chain data
  (`u32` length + JPEG bytes, 0 = none). So a grouped plug-in keeps its picture through save,
  a copied or duplicated rack (racks clone through the blob) and ungroup. Older blobs read with
  none.
- Presets (`PresetService`) do **not** carry the preview: a preset is sound, not a
  picture of a particular window.
