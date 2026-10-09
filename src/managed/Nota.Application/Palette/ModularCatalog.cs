// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-6a.5): the nodes that exist only in Modular — modulators and CV tools.
// The palette lists them only when it was called from Modular, where a node is added to the
// canvas unconnected. A new modulator kind registered here appears in the palette as is.

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
        new("LFO", 0, "Modulator", D("lfo oscillator wobble", "A low-frequency oscillator, free or synced to the tempo.")),
        new("Envelope Follower", 1, "Modulator", D("envelope follower env sidechain", "Turns an audio signal's level into a control signal.")),
        new("MIDI → CV", 2, "Converter", D("midi cv velocity note pitch", "Turns notes, velocity and controllers into control signals.")),
        new("ADSR", 3, "Modulator", D("adsr envelope", "An attack-decay-sustain-release envelope triggered by notes.")),
        new("Macro", 4, "Control", D("macro knob", "One knob driving several destinations.")),
        new("Math", 5, "CV utility", D("math add multiply mixer attenuverter", "Adds, multiplies or mixes two control signals.")),
        new("Scope", 6, "Monitor", D("scope oscilloscope monitor", "Shows a control signal over time.")),
    };
}
