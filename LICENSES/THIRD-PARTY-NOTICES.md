<!-- SPDX-License-Identifier: AGPL-3.0-only -->
# Third-party notices

Nota includes and/or links the third-party software listed below. Each component is the
copyright of its respective authors and is used under the license shown. This file
reproduces the required attribution notices; full license terms are in each project's own
distribution. For the dependency register (versions, purpose, clearance status) see
[`third-party.md`](third-party.md).

---

## JUCE
Copyright © Raw Material Software Limited.
Licensed under the **GNU AGPLv3** (see [`AGPL-3.0-only.txt`](AGPL-3.0-only.txt)). JUCE is
statically linked into the plugin-hosting module; its AGPL terms extend to the combined
work, which is why Nota as a whole is AGPLv3. <https://juce.com>

## Steinberg VST3 SDK
Copyright © 2025 Steinberg Media Technologies GmbH.
Licensed under the **MIT License**. Bundled with JUCE for VST3 hosting. "VST" is a
trademark of Steinberg Media Technologies GmbH — its name and logo are governed by
Steinberg's VST trademark guidelines, separate from the code license.

## Avalonia
Copyright © .NET Foundation and Contributors.
Licensed under the **MIT License**. <https://avaloniaui.net>

## .NET runtime & libraries
Copyright © .NET Foundation and Contributors.
Licensed under the **MIT License**. <https://dotnet.microsoft.com>

## miniaudio
Copyright © David Reid (mackron@gmail.com).
Dual licensed — choice of **public domain (Unlicense)** or **MIT-0**. Used for audio
device I/O (WASAPI / PulseAudio / ALSA). <https://miniaud.io>

## RtMidi
Copyright © 2003–2023 Gary P. Scavone.
Licensed under the **RtMidi license** (MIT-style, with an additional request to send
modifications upstream). Used for MIDI I/O (WinMM / ALSA).
<https://www.music.mcgill.ca/~gary/rtmidi/>

## dr_libs (dr_wav, dr_flac, dr_mp3)
Copyright © David Reid (mackron@gmail.com).
Dual licensed — choice of **public domain (Unlicense)** or **MIT-0**. Used for WAV / FLAC
/ MP3 decoding. <https://github.com/mackron/dr_libs>

## Signalsmith Stretch
Copyright © 2022 Geraint Luff / Signalsmith Audio Ltd.
Licensed under the **MIT License**. Used for time-stretch / pitch-shift (clip warping).
<https://github.com/Signalsmith-Audio/signalsmith-stretch>

## Signalsmith Linear
Copyright © 2025 Signalsmith Audio.
Licensed under the **MIT License**. Used for FFT / linear DSP primitives.
<https://github.com/Signalsmith-Audio/linear>

## HIIR
Copyright © Laurent de Soras.
Licensed under the **WTFPL** (Do What The Fuck You Want To Public License). Used for
oversampling (polyphase half-band IIR filters). <http://ldesoras.free.fr/prod.html>

## Freeverb (algorithm)
Public-domain reverb algorithm by Jezar at Dreampoint. Nota ships its **own
implementation** of the algorithm (`src/native/nota.engine/src/Reverb.h`); no original
code is vendored.

## Apple system frameworks
Portions © Apple Inc. On macOS, Nota calls the CoreAudio, CoreMIDI, AudioToolbox,
AudioUnit, CoreAudioKit and GameController frameworks provided by the operating system.
These are used under Apple's SDK terms and are **not** redistributed with Nota.
