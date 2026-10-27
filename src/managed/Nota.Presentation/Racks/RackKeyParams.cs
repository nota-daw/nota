// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The parameters a rack's compact device card shows (specs/040, research D8): up to three
// per device kind, the ones a player reaches for first. Listed by parameter NAME and
// resolved against the device's own names at run time, so a device that reorders or adds
// parameters keeps its key params. A kind without an entry, or whose names no longer
// match, falls back to its first parameters. While a macro is being mapped the card lists
// every parameter instead; this table is only what shows at rest.

namespace Nota.Presentation;

/// <summary>Which device list a kind number belongs to.</summary>
public enum RackDeviceFamily { Instrument, AudioEffect, MidiEffect }

public static class RackKeyParams
{
    public const int Max = 3;

    // Built-in instruments by instrument kind (chain instruments use the plugin-param names).
    private static readonly Dictionary<int, string[]> Instruments = new()
    {
        [0] = ["Cutoff", "Resonance", "Attack"],                                // Nota Synth
        [2] = ["Mallet Stiffness", "Res1 Decay", "Res1 Bright"],                // Nota Physical
        [5] = ["Osc1 Position", "Cutoff", "Resonance"],                         // Nota Aurora
        [6] = ["Filter 1 Freq", "Filter 1 Reso", "Filter 1 Env"],               // Nota Volt
        [7] = ["Filter Freq", "Filter Reso", "Drive"],                          // Nota Bass
        [8] = ["Motion Rate", "Voice Tone", "Voice Release"],                   // Nota Pendulum
        [9] = ["FM Depth", "Feedback", "Filter Freq"],                          // Nota Operator
        [10] = ["Position", "Grain Size", "Density"],                           // Nota Grain
        [11] = ["Vector X", "Vector Y", "Filter"],                              // Nota Flux
        [12] = ["Kick Level", "Snare Level", "Closed Hat Level"],               // Nota Rhythm
        [13] = ["Filter Cutoff", "Filter Emphasis", "Filter Contour"],          // Nota Monolith
        [14] = ["Filter Cutoff", "Filter Resonance", "Filter Env-Amount"],      // Nota Pentad
        [15] = ["Filter Cutoff", "Filter Resonance", "Mixer Drive"],            // Nota Consort
        [16] = ["Resonator Bright", "Hammer Hardness", "Tremolo Depth"],        // Nota Keys
    };

    // Built-in audio effects by device kind.
    private static readonly Dictionary<int, string[]> AudioEffects = new()
    {
        [0] = ["1 Freq", "1 Gain", "Output"],              // Nota EQ-8
        [1] = ["Thresh", "Ratio", "Makeup"],               // Nota Compressor
        [2] = ["Size", "Decay", "Dry/Wet"],                // Nota Reverb
        [3] = ["Time L", "Feedback", "Dry/Wet"],           // Nota Delay
        [4] = ["Gain", "Width", "Balance"],                // Nota Utility
        [6] = ["Gain", "Treble", "Mix"],                   // Nota Valve
        [7] = ["Freq", "Res", "Dry/Wet"],                  // Nota Auto Filter
        [8] = ["Drive", "Tone", "Mix"],                    // Nota Vintage
        [9] = ["Rate", "Amount", "Mix"],                   // Nota Orbit
        [10] = ["Amount", "Speed", "Mix"],                 // Nota Auto Shift
        [11] = ["Interval", "Gate", "Mix"],                // Nota Beat Repeat
        [12] = ["Bits", "Rate", "Dry/Wet"],                // Nota Crush
        [13] = ["1 Freq", "1 Thr", "1 Gain"],              // Nota Dynamic EQ-8
        [14] = ["Ceiling", "Gain", "Release"],             // Nota Ceiling
        [15] = ["Feedback", "InputGain", "Speed"],         // Nota Strata
        [16] = ["Low", "Mid", "High"],                     // Nota EQ-3
        [17] = ["Amount", "Tone", "Wet"],                  // Nota Forge
        [18] = ["Target", "Trim", "Max Gain"],             // Nota Level
        [19] = ["Threshold", "Attack", "Release"],         // Nota Shutter
        [20] = ["Blend", "Algo Decay", "Dry/Wet"],         // Nota Chamber
        [21] = ["Amount", "Output", "Mix"],                // Nota Prism
        [22] = ["Scale Top", "Scale Range", "Freeze"],     // Nota Lens
        [23] = ["Rate", "Depth", "Feedback"],              // Nota Flanger
        [24] = ["Rate", "Depth", "Feedback"],              // Nota Phaser
        [25] = ["Rate", "Amount", "Mix"],                  // Nota Chorus
        [26] = ["Formant", "Pitch", "Mix"],                // Nota Vocoder
    };

    // Built-in MIDI effects by MIDI kind.
    private static readonly Dictionary<int, string[]> MidiEffects = new()
    {
        [0] = ["Rate", "Gate", "Octaves"],                 // Nota Arp
        [1] = ["Strum", "Spread", "Fold"],                 // Nota Chord
        [2] = ["Root", "Scale", "Transpose"],              // Nota Scale
        [3] = ["Gate", "Percent", "Random"],               // Nota Length
        [4] = ["Drive", "Random", "Out High"],             // Nota Velocity
        [5] = ["Chance", "Note Range", "Vel Amt"],         // Nota Random
    };

    private static Dictionary<int, string[]> Table(RackDeviceFamily family) => family switch
    {
        RackDeviceFamily.Instrument => Instruments,
        RackDeviceFamily.MidiEffect => MidiEffects,
        _ => AudioEffects,
    };

    /// <summary>The kinds with a curated entry in <paramref name="family"/>.</summary>
    public static IEnumerable<int> Kinds(RackDeviceFamily family) => Table(family).Keys;

    /// <summary>The curated names for a kind (empty when the kind has none).</summary>
    public static IReadOnlyList<string> Names(RackDeviceFamily family, int kind)
        => Table(family).TryGetValue(kind, out var n) ? n : [];

    /// <summary>The parameter indices a compact card shows for a device, at most <see cref="Max"/>:
    /// the curated names found on the device, in table order, topped up with the device's first
    /// parameters when a name is missing.</summary>
    public static int[] Resolve(RackDeviceFamily family, int kind, int paramCount, Func<int, string> paramName)
    {
        var picked = new List<int>(Max);
        foreach (var want in Names(family, kind))
        {
            for (int p = 0; p < paramCount; p++)
            {
                if (!string.Equals(paramName(p), want, StringComparison.OrdinalIgnoreCase) || picked.Contains(p)) continue;
                picked.Add(p);
                break;
            }
        }
        for (int p = 0; p < paramCount && picked.Count < Max; p++)
            if (!picked.Contains(p)) picked.Add(p);
        return picked.Count > Max ? picked.GetRange(0, Max).ToArray() : picked.ToArray();
    }
}
