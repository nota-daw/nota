// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-6a.5): the nodes that exist only in Modular — modulators and CV tools.
// The palette lists them only when it was called from Modular, where a node is added to the
// canvas unconnected. A new modulator kind registered here appears in the palette as is.

using Nota.Application.Localization;

namespace Nota.Application.Palette;

/// <summary>A Modular-only node: <see cref="Kind"/> is the engine modulator kind.</summary>
public sealed record ModularNode(string Name, int Kind, string Sub, SemanticDescriptor Descriptor);

public static class ModularCatalog
{
    private static SemanticDescriptor D(string aliases, string description) => new()
    {
        Role = "", Task = ["modulation"], Character = ["evolving"],
        Aliases = aliases.Split(' ', StringSplitOptions.RemoveEmptyEntries), Description = description,
    };

    public static IReadOnlyList<ModularNode> All { get; } = new ModularNode[]
    {
        new("LFO", 0, Loc.T("palette.modular.modulator"), D("lfo oscillator wobble", Loc.T("palette.modular.a_low_frequency_oscillator_free"))),
        new("Envelope Follower", 1, Loc.T("palette.modular.modulator"), D("envelope follower env sidechain peak controller ducking duck pump loudness level volume", Loc.T("palette.modular.turns_the_tracks_loudness_into"))),
        new("MIDI → CV", 2, Loc.T("palette.modular.converter"), D("midi cv velocity note pitch", Loc.T("palette.modular.turns_notes_velocity_and_controllers"))),
        new("ADSR", 3, Loc.T("palette.modular.modulator"), D("adsr envelope", Loc.T("palette.modular.an_attack_decay_sustain_release"))),
        new("Macro", 4, Loc.T("palette.modular.control"), D("macro knob", Loc.T("palette.modular.one_knob_driving_several_destinations"))),
        new("Math", 5, Loc.T("palette.modular.cv_utility"), D("math add multiply mixer attenuverter", Loc.T("palette.modular.adds_multiplies_or_mixes_two"))),
        new("Scope", 6, Loc.T("palette.modular.monitor"), D("scope oscilloscope monitor", Loc.T("palette.modular.shows_a_control_signal_over"))),
    };
}
