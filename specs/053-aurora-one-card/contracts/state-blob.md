# Contract: WavetableSynth state blob, table trailer v1

The parameter layout is unchanged: `kNumParams` little-endian float32 values
(`WavetableSynth.cpp:227-234`), append-only (`WavetableSynth.h:19`). An optional trailer
follows at the **end** of the blob:

```text
offset            content
0                 float32 × P          parameters (P = kNumParams of the writer; 156 today)
4P                section (S bytes):
                    char[4]  "AWT1"
                    u32      version = 1
                    u8       useCustom[2]
                    for osc in 0..1:
                      u8     present        (0 = no table)
                      u16    missingLen, char[missingLen]  missing name ("" = not missing)
                      if present:
                        u8   sameAsOsc0     (osc 1 only; 1 = identical hash, no frames follow)
                        u16  nameLen, char[nameLen]
                        u8   origin, u8 truncatedLength, u8 truncatedFrames
                        u32  frames (1..256)
                        float32 × 2048 × frames   base level (mips rebuilt on read)
4P + S            u32 S
4P + S + 4        char[4] "AWT1"
```

## Rules

- **Writer.** The trailer is written only when a slot holds a table, a source flag is 1, or
  a missing name is set. Otherwise the blob is exactly `4P` bytes, byte-identical to today's,
  so content-addressed `plugin-states/<hash>.bin` names do not change for existing projects.
- **Reader.**
  1. If `size ≥ 8` and the last 4 bytes are `"AWT1"`, read `S` and check that
     `4 + S + 4 ≤ size` and that the section starts with `"AWT1"`. If so, the parameter
     region is `size − S − 8` bytes; otherwise the whole blob is parameters.
  2. Parameters are read as today: `min(kNumParams, region / 4)`.
  3. Unknown trailer versions are ignored: parameters are read, and the tables stay as they
     were.
- **Excluded tables.** `nota_track_aurora_state(include_tables = 0)` writes the trailer with
  `present = 0` for both slots but keeps `useCustom` and the missing names. The project save
  then lists the tables in `InstrumentDto.Wavetables`.
- **Setting state.** `setState` builds the new `AuroraTableSet` on the calling (message)
  thread, then publishes it through the `SnapshotHistory` (research R1). A blob without a
  trailer clears both slots and both flags, so a v27-era state restored over a live instance
  resets it (the preset FR-015 semantics).
- **Clones and racks.** `clone()` shares the current set and flags. Rack blobs carry the
  child state unchanged (`RackCore.cpp:661-663`).
