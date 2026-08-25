# Third-party dependency register

Every dependency Nota ships or links must be recorded here, with the license that
applies to each edition:

- **Free** — distributed under `AGPL-3.0-only`. A dependency qualifies if its license
  is permissive, public domain, or AGPL-compatible copyleft.
- **Pro** — distributed under `LicenseRef-Nota-Commercial`. A dependency qualifies if
  its license is permissive **or** the Licensor holds a commercial license for it.

**Rule:** before adding a dependency, add its row here and confirm both columns.

## Register

| Dependency | Version | Purpose | License (Free) | License (Pro) | Status |
|---|---|---|---|---|---|
| .NET runtime | 10 | Managed runtime | MIT | MIT | ✅ in use |
| Avalonia | 12 | UI framework | MIT | MIT | ✅ in use |
| JUCE | 8.0.14 | Plugin hosting (VST3 / AU) | **AGPL-3.0** | JUCE Commercial (**must be purchased**) | ⚠️ in use — see note 1 |
| Steinberg VST3 SDK | bundled with JUCE 8 | VST3 hosting | MIT | MIT | ✅ in use — see note 2 |
| miniaudio | 0.11.25 | Audio device I/O (WASAPI / PulseAudio / ALSA) | Unlicense **or** MIT-0 | same | ✅ in use |
| RtMidi | vendored | MIDI I/O (WinMM / ALSA) | MIT-like (RtMidi) | same | ✅ in use |
| dr_libs (dr_wav / dr_flac / dr_mp3) | vendored | WAV / FLAC / MP3 decoding | Unlicense **or** MIT-0 | same | ✅ in use — see note 3 |
| signalsmith-stretch | vendored | Time-stretch / pitch-shift (clip warping) | MIT | MIT | ✅ in use |
| signalsmith-linear | vendored | FFT / linear DSP primitives | MIT | MIT | ✅ in use |
| HIIR (Laurent de Soras) | 1.40 | Oversampling (polyphase halfband IIR) | WTFPL | WTFPL | ⚠️ in use — see note 4 |
| Apple CoreAudio / CoreMIDI / AudioToolbox | system | Audio + MIDI I/O (macOS) | system SDK | system SDK | ✅ in use |
| Freeverb (algorithm only) | — | Built-in reverb | public domain | public domain | ✅ in use — own implementation of the Jezar algorithm; no vendored code (`src/native/nota.engine/src/Reverb.h`) |

Status key: ✅ cleared · ⚠️ cleared with a condition · ⏳ planned · ❌ rejected (incompatible).

## Notes

**1 — JUCE is AGPL, and it sets the terms for the whole Free build.**
JUCE 8 is dual-licensed under **AGPLv3** or a paid commercial license. It is statically
linked into `nota_pluginhost`, so the Free build is distributed under `AGPL-3.0-only` —
this is why Nota's outbound license is AGPL and not GPL. The AGPL's §13 network clause
applies to the combined work. The core engine is deliberately JUCE-free
(see `ARCHITECTURE.md`); JUCE lives only in the pluginhost module.

Shipping the **Pro** edition requires purchasing a JUCE commercial license first.
Until that is done, no Pro binary may be distributed.

**2 — VST3 SDK is MIT since 2025.**
Steinberg relicensed the VST3 SDK to MIT; the copy bundled with JUCE 8 carries an MIT
notice (`Copyright (c) 2025, Steinberg Media Technologies GmbH`). The *code* imposes no
copyleft. Separately, Steinberg's **VST trademark guidelines** govern use of the "VST"
name and logo in marketing and UI — review them before using the mark.

**3 — MP3 patents.**
The core MP3 patents expired in 2017, so `dr_mp3` decoding is generally considered
patent-free. Re-confirm if targeting a jurisdiction with unusual patent terms.

**4 — WTFPL is not accepted everywhere.**
HIIR is released under the WTFPL, which some corporate legal reviews reject (it carries
no warranty disclaimer and its enforceability is debated). It is fine for the AGPL Free
build. If a Pro customer's legal team objects, HIIR must be replaced with an
own-implementation halfband filter.

## Not shipped

The JUCE submodule (`src/native/nota.engine/vendor/JUCE`) is fetched at build time and
is **not** redistributed in this repository's own source tree. Binary releases that
statically link it do redistribute it, under AGPLv3.
