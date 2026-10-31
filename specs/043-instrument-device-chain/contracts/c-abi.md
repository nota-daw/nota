# C ABI contract (`nota_engine.h`)

All calls: message thread only; each successful edit is one undo step (one published graph).

## Changed

```c
/* Replace the instrument on an existing instrument track with a fresh built-in `kind`.
 * kind 3 = an empty Instrument Rack, 4 = an empty Drum Rack (new in specs/043); other kinds as
 * nota_engine_add_*_track. NOTA_ERR_UNKNOWN for an unknown kind or a non-instrument track. */
NOTA_API NotaResult nota_track_set_builtin_instrument(NotaEngine* engine, int32_t track_id, int32_t kind);

/* clipboard_kind: 0 empty, 1 audio effect, 2 MIDI effect, 3 instrument (specs/043). */
NOTA_API int32_t    nota_device_clipboard_kind(const NotaEngine* engine);
```

## New

```c
/* The track's instrument on the shared device clipboard (specs/043): a full clone — every
 * param, its state, a rack's chains / pads and macros, a plug-in's state, bypass. Replaces
 * whatever the clipboard held. 1 = copied, 0 = no instrument (not an instrument track). */
NOTA_API int32_t nota_track_copy_instrument(NotaEngine* engine, int32_t track_id);

/* Replace track_id's instrument with a fresh clone of the clipboard's instrument; one undo
 * step, the clipboard kept. 0 = done, -1 nothing to paste (the clipboard holds no
 * instrument), -2 not an instrument track, -3 the clone failed (a plug-in that won't load). */
NOTA_API int32_t nota_track_paste_instrument(NotaEngine* engine, int32_t track_id);

/* Move (keep_source 0) or copy (keep_source 1) a device to another track, both tracks in one
 * undo step. family: 0 audio effect at `index`, 1 MIDI effect at `index`, 2 the instrument
 * (index ignored). at_index: position on the target, -1 = the end of its section (ignored for
 * the instrument, which replaces the target's).
 * A move takes the device's automation lanes along (index rewritten); a copy takes none. A move
 * of the instrument removes the source track. CV links and rack macro mappings to the device
 * stay with the source (dropped from a moved device). A sidechain key whose source is the
 * target track is cleared.
 * Returns the new index (>= 0; 0 for the instrument), -1 bad address or dst == src, -2 the
 * target can't take the family (an instrument / MIDI effect onto an audio, return, group or
 * master track), -3 the clone failed. *out_flags (may be NULL): bit 0 sidechain cleared,
 * bit 1 links dropped, bit 2 source track removed. */
NOTA_API int32_t nota_track_transfer_device(NotaEngine* engine, int32_t src_track_id, int32_t family,
                                            int32_t index, int32_t dst_track_id, int32_t at_index,
                                            int32_t keep_source, int32_t* out_flags);
```

## Managed mirror (`IAudioEngine`)

```csharp
bool CopyInstrument(int trackId);
int  PasteInstrument(int trackId);
int  TransferDevice(int srcTrackId, DeviceFamily family, int index, int dstTrackId, int atIndex,
                    bool keepSource, out TransferFlags flags);
// DeviceClipboardKind() gains 3 = instrument.
```

## Smoke coverage (`InstrumentChainTests`)

Each new or changed call above is exercised: copy / paste instrument across built-in kinds
(params equal), Volt → Instrument Rack → Drum Rack → Volt via `set_builtin_instrument`, transfer
of each family with an automation lane, copy vs move, the instrument move removing its track,
the −2 refusals, the sidechain-cleared flag, and one undo restoring both tracks.
