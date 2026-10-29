# C ABI contract (`nota_engine.h`)

All calls: message thread only. Every successful structural edit is one undo step (one
published graph) unless noted.

## New — group / ungroup (R3)

```c
/* Group a contiguous run of a track's devices into a new rack in their place (#328), one undo
 * step. midi_first/midi_count: the MIDI effects (count 0 = none); with_instrument: 1 to include
 * the instrument; fx_first/fx_count: the audio effects. The rack type follows from the run:
 * audio effects only -> Audio Effect Rack at fx_first; MIDI effects only -> MIDI Effect Rack at
 * midi_first; with the instrument -> an Instrument Rack as the track's instrument whose chain 0
 * holds MIDI effects -> instrument -> audio effects (the MIDI run must end at the last MIDI
 * effect and the audio run start at 0).
 * Instances move in without re-preparing; automation lanes, CV links, Arp CC routes and
 * sidechain keys on them are rewritten to point into the rack (nested address).
 * Returns the rack's address (>= 0 audio-effect index, -1 the instrument, or the MIDI
 * effect index encoded as for MIDI effects), or a reason < -1:
 *   -2 empty or out-of-range run      -3 not contiguous / MIDI + audio without instrument
 *   -4 a rack in the run              -5 a plug-in the target rack can't host
 *   -6 the instrument can't be a chain instrument (an Instrument Rack)
 *   -7 not an instrument track (MIDI effects / instrument on an audio track) */
NOTA_API int32_t nota_track_group_devices(NotaEngine* engine, int32_t track_id,
                    int32_t midi_first, int32_t midi_count, int32_t with_instrument,
                    int32_t fx_first, int32_t fx_count);

/* Ungroup a one-chain rack (Audio Effect Rack, MIDI Effect Rack, or an Instrument Rack as the
 * track's instrument; family 0 audio effect at index, 1 MIDI effect at index, 2 the instrument)
 * back into its chain's devices, in place, one undo step. Nested addresses pointing into the
 * rack become track-level again; the rack's own macros, mappings, chain mix and settings are
 * dropped. Returns the number of devices restored (>= 0), or
 *   -2 not a rack / bad address  -3 two or more chains  -4 a Drum Rack.
 * *out_dropped (may be NULL): bit 0 macro mappings dropped, bit 1 non-default chain mix /
 * zones dropped. */
NOTA_API int32_t nota_track_ungroup_rack(NotaEngine* engine, int32_t track_id, int32_t family,
                    int32_t index, int32_t* out_dropped);
```

## Changed

```c
/* #257 wrappers, kept for callers: wrap = group_devices(track, 0, 0, 0, device_index, 1);
 * unwrap = ungroup_rack(track, 0, device_index, NULL). They no longer refuse automated,
 * modulated, keyed or CC-driven devices. */
NOTA_API int32_t nota_track_wrap_device_in_rack(NotaEngine* engine, int32_t track_id, int32_t device_index);
NOTA_API int32_t nota_track_unwrap_rack(NotaEngine* engine, int32_t track_id, int32_t device_index);

/* clipboard_kind: 0 empty, 1 audio effect, 2 MIDI effect, 3 instrument, 4 several devices. */
NOTA_API int32_t nota_device_clipboard_kind(const NotaEngine* engine);
```

## New — multi-device clipboard (R8)

```c
/* Put a set of a track's devices on the device clipboard, in chain order (MIDI effects, the
 * instrument, audio effects), each a full clone. Replaces the clipboard. Returns the entry
 * count, or -1 bad address. */
NOTA_API int32_t nota_track_copy_devices(NotaEngine* engine, int32_t track_id,
                    const int32_t* midi_idx, int32_t midi_n, int32_t with_instrument,
                    const int32_t* fx_idx, int32_t fx_n);
NOTA_API int32_t nota_device_clipboard_count(const NotaEngine* engine);

/* Paste every clipboard entry onto track_id in one undo step: MIDI effects at midi_at, audio
 * effects at fx_at (-1 = the end of the section), an instrument replacing the track's.
 * Entries the track can't take are skipped. Returns the number pasted (>= 0) and
 * *out_skipped (may be NULL); -1 empty clipboard, -3 a clone failed (nothing changed). */
NOTA_API int32_t nota_track_paste_devices(NotaEngine* engine, int32_t track_id,
                    int32_t midi_at, int32_t fx_at, int32_t* out_skipped);
```

## New — nested addresses on lanes and links (R1, R7)

```c
/* Chain and slot of a lane's target: chain_index -1 = track-level (the old meaning);
 * otherwise the chain's index in the rack at the lane's device_index, and slot as in rack
 * mappings (>= 0 device, -1 chain instrument, MIDI effects encoded as RackCore MIDI targets). */
NOTA_API NotaResult nota_track_automation_lane_path(const NotaEngine* engine, int32_t track_id,
                    int32_t lane_index, int32_t* out_chain_index, int32_t* out_slot);
/* Project load: add a lane addressed into a rack chain (plugin params by stable id). Returns
 * the lane index or -1 when the address doesn't resolve. */
NOTA_API int32_t nota_track_add_nested_automation_lane(NotaEngine* engine, int32_t track_id,
                    int32_t target, int32_t device_index, int32_t chain_index, int32_t slot,
                    int32_t param_index, const char* param_id);
/* The same pair for CV links (source and target) and the Arp CC route, read for save and set
 * on load; signatures mirror the existing CV / CC calls with chain_index + slot appended. */
```

The exact CV / CC signatures follow the existing `nota_track_*cv*` and Arp CC calls; they
append `chain_index, slot` and keep the old calls as track-level wrappers.

## Smoke coverage

`DeviceGroupTests.cs` calls every function above: each rack type, each refusal code, render
equality before / after (±0.1 dB), a nested lane / CV link / CC route / sidechain key still
acting after Group and after Ungroup, undo of each, save → reload of a grouped project, multi
copy / paste with skips.
