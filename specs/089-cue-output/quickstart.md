# Quickstart: validating the Cue output

Contracts: [c-abi.md](contracts/c-abi.md), [ui.md](contracts/ui.md). Model: [data-model.md](data-model.md).

## Automated (smoke test, `scripts/build.sh`)

New cases in `tests/Nota.SmokeTest/CueOutputTests.cs`, all through
`nota_engine_render_device_block_cue` with a deterministic WAV clip (see memory:
synth notes drift between renders — use a clip, not a synth):

| Case | Setup | Expect |
|------|-------|--------|
| Tap is pre-fader | clip track, Cue on, fader −∞ | Cue ≈ clip level; Main silent |
| Tap is pre-mute / solo | Cue on, track muted; another track soloed | Cue still carries the track; Main has none of it |
| Tap is post-devices | Utility/gain device +6 dB on the track | Cue +6 dB vs dry |
| Sends/master excluded | send to a return, master gain device | Cue unaffected |
| Several cued | two clip tracks cued | Cue = sum |
| Group cued | group with a gain device, group muted | Cue = group bus after device |
| Cue level null test (SC-001) | sweep Cue level 0 → 2 | Main bit-identical to Cue off |
| Click → Cue (SC-002) | metronome on, count-in 1 bar, dest Cue | Main identical to metronome off; Cue has clicks |
| Click → Main unchanged | dest Main | Main identical to the pre-feature render |
| Alignment (SC-003) | dest Main + Cue, impulse clip cued | impulse on the same sample in Main and Cue |
| Scatter | `nota_audio_scatter_test` 4 ch, Main 0/2 Cue 2/2 | ch 0/1 = Main, 2/3 = Cue |
| Scatter fallback (SC-004) | same routing, 2 device channels | ch 0/1 = Main only; state = 2 |
| Main fallback | Main 2/2, 2 device channels | Main on 0/1; state = 1 |
| Overlap after fallback | Main 2/2, Cue 0/2, 2 channels | Main on 0/1, Cue silent; state = 3 |
| Project round-trip (SC-006) | save with 2 cued tracks, Cue level 0.5 | reload restores both |
| v31 project | open a fixture saved before | no Cue, renders identically |

## Manual (hardware)

Prerequisite: an interface with ≥ 4 outputs (or macOS BlackHole 16ch / an aggregate
device; Windows ASIO4ALL or the interface's ASIO driver).

1. Preferences → Audio → Outputs: Main 1/2, Cue 3/4, Click → Cue. Close.
2. Load a project with a drum loop and a vocal clip. Cue the vocal track, mute it.
   Play: speakers (1/2) — drums, no vocal, no click; headphones (3/4) — vocal + click.
3. Move the Cue fader end to end: speakers don't change.
4. Arm a track with input monitoring, cue it, record: the take has no click and no Cue
   level change in it.
5. MIDI Learn: map a pad to the vocal's Cue and a knob to Cue level; both work.
6. Unplug the interface (macOS falls back to built-in output): the Cue strip shows
   unavailable, the speakers never play the vocal or the click. Plug back in → Cue returns.
7. Save, quit, reopen: cued tracks and Cue level back; Outputs page unchanged.
8. Windows: repeat 1–3 on WASAPI and on ASIO; Linux: 1–3 on a multi-channel ALSA/Pulse device.
