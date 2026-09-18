# Third-party dependency register

Every dependency Nota ships or links must be recorded here, with its license. Nota is
distributed under `AGPL-3.0-only`; a dependency qualifies if its license is permissive,
public domain, or AGPL-compatible copyleft.

**Rule:** before adding a dependency, add its row here and confirm the license.

## Register

| Dependency | Version | Purpose | License | Status |
|---|---|---|---|---|
| .NET runtime | 10 | Managed runtime | MIT | ✅ in use |
| Avalonia | 12 | UI framework | MIT | ✅ in use |
| JUCE | 8.0.14 | Plugin hosting (VST3 / AU) | **AGPL-3.0** | ✅ in use — see note 1 |
| Steinberg VST3 SDK | bundled with JUCE 8 | VST3 hosting | MIT | ✅ in use — see note 2 |
| miniaudio | 0.11.25 | Audio device I/O (WASAPI / PulseAudio / ALSA) | Unlicense **or** MIT-0 | ✅ in use |
| RtMidi | vendored | MIDI I/O (WinMM / ALSA) | MIT-like (RtMidi) | ✅ in use |
| dr_libs (dr_wav / dr_flac / dr_mp3) | vendored | WAV / FLAC / MP3 decoding | Unlicense **or** MIT-0 | ✅ in use — see note 3 |
| signalsmith-stretch | vendored | Time-stretch / pitch-shift (clip warping) | MIT | ✅ in use |
| signalsmith-linear | vendored | FFT / linear DSP primitives | MIT | ✅ in use |
| HIIR (Laurent de Soras) | 1.40 | Oversampling (polyphase halfband IIR) | WTFPL | ✅ in use — see note 4 |
| Apple CoreAudio / CoreMIDI / AudioToolbox | system | Audio + MIDI I/O (macOS) | system SDK | ✅ in use |
| Geist / Geist Mono (Vercel) | 1.7.2 | UI and numeric typefaces (`assets/fonts`) | **SIL OFL 1.1** | ✅ in use — see note 5 |
| Freeverb (algorithm only) | — | Built-in reverb | public domain | ✅ in use — own implementation of the Jezar algorithm; no vendored code (`src/native/nota.engine/src/Reverb.h`) |

Status key: ✅ cleared · ⚠️ cleared with a condition · ⏳ planned · ❌ rejected (incompatible).

## Notes

**1 — JUCE is AGPL, and it sets the terms for the whole build.**
JUCE 8 is used here under the **AGPLv3**. It is statically linked into `nota_pluginhost`,
so the build is distributed under `AGPL-3.0-only` — this is why Nota's outbound license is
AGPL and not GPL. The AGPL's §13 network clause applies to the combined work. The core
engine is deliberately JUCE-free (see `ARCHITECTURE.md`); JUCE lives only in the
pluginhost module.

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
no warranty disclaimer and its enforceability is debated). It is fine for the AGPL build.

**5 — Geist ships as bundled font files under the SIL Open Font License.** OFL 1.1
allows embedding and redistributing the fonts with any software, including AGPL software,
provided the fonts are not sold on their own and the license travels with them
(`LICENSES/OFL-1.1-Geist.txt`). The eight static weights we use (Regular, Medium,
SemiBold, Bold of each family) are copied unmodified from the `geist` npm package;
"Geist" is a Reserved Font Name, so a subset or other modification must be renamed.
They replace Inter, which came in through the `Avalonia.Fonts.Inter` package.

## Not shipped

The JUCE submodule (`src/native/nota.engine/vendor/JUCE`) is fetched at build time and
is **not** redistributed in this repository's own source tree. Binary releases that
statically link it do redistribute it, under AGPLv3.
