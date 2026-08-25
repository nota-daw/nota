// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — maps a MIDI-effect kind to its body: Nota Arp (kind 0) the step editor,
// Nota Chord (kind 1) its chord/keyboard editor, Nota Scale (kind 2) its note-map editor;
// anything else falls back to generic param faders.

namespace Nota.App;

internal sealed class MidiDeviceCardFactory
{
    private readonly IMidiDeviceBody _arp = new ArpMidiBody();
    private readonly IMidiDeviceBody _chord = new ChordMidiBody();
    private readonly IMidiDeviceBody _scale = new ScaleMidiBody();
    private readonly IMidiDeviceBody _length = new LengthMidiBody();
    private readonly IMidiDeviceBody _velocity = new VelocityMidiBody();
    private readonly IMidiDeviceBody _random = new RandomMidiBody();
    private readonly IMidiDeviceBody _generic = new GenericMidiBody();

    public IMidiDeviceBody Resolve(int midiEffectKind) => midiEffectKind switch
    {
        0 => _arp,
        1 => _chord,
        2 => _scale,
        3 => _length,
        4 => _velocity,
        5 => _random,
        _ => _generic,
    };
}
