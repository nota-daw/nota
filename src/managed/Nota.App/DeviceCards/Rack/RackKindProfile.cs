// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — what differs between the three racks on the shared rack card
// (specs/040): the zone tab's name and rows, how chains can run, the right panel's output
// tab, a chain's mix controls and which device kinds a chain takes. The card reads these
// instead of branching on the rack kind, so the three stay one layout.

namespace Nota.App;

internal sealed record RackKindProfile(
    RackKind Kind,
    string ZoneTab,               // centre tab between Chains and Macros
    bool KeyZones,                // per-chain key range
    bool VelocityZones,           // per-chain velocity range
    bool SelectRanges,            // per-chain select range + the selector
    (int Id, string Label, string Hint)[] Modes, // how chains run; empty = no choice
    string OutputTab,             // right panel's rack tab
    bool ChainPan,                // Pan row (audio racks) …
    bool ChainTranspose,          // … or Transpose (MIDI chains)
    bool VolumeIsVelocity,        // a MIDI chain's Volume scales velocity, shown in %
    bool LevelMeter,              // chain rows show an audio level (MIDI: note activity)
    bool TakesInstrument,         // one instrument per chain
    bool TakesAudioEffects,
    bool TakesMidiEffects,
    string ChainsHint)            // the one-line hint under the chain list
{
    // Engine routing ids (RackCore mode): 0 Parallel, 1 Serial, 2 Select.
    public const int Parallel = 0, Serial = 1, Select = 2;

    public static readonly RackKindProfile Instrument = new(
        RackKind.Instrument, "Zones", KeyZones: true, VelocityZones: true, SelectRanges: false,
        Modes: [], OutputTab: "Rack out", ChainPan: true, ChainTranspose: false, VolumeIsVelocity: false,
        LevelMeter: true, TakesInstrument: true, TakesAudioEffects: true, TakesMidiEffects: true,
        ChainsHint: "Drop an instrument to add a layer.");

    public static readonly RackKindProfile AudioEffect = new(
        RackKind.AudioEffect, "Select", KeyZones: false, VelocityZones: false, SelectRanges: true,
        Modes:
        [
            (Parallel, "Parallel", "All chains get the input. Outputs are summed."),
            (Serial, "Serial", "Chain 1 feeds chain 2, top to bottom."),
            (Select, "Select", "Only chains whose range holds the selector play."),
        ],
        OutputTab: "Rack out", ChainPan: true, ChainTranspose: false, VolumeIsVelocity: false,
        LevelMeter: true, TakesInstrument: false, TakesAudioEffects: true, TakesMidiEffects: false,
        ChainsHint: "Drop an effect to add a chain.");

    public static readonly RackKindProfile MidiEffect = new(
        RackKind.MidiEffect, "Zones", KeyZones: true, VelocityZones: true, SelectRanges: true,
        Modes:
        [
            (Parallel, "Parallel", "Each note goes to every chain whose zone it hits. Outputs merge before the instrument."),
            (Select, "Select", "Only chains whose range holds the selector get notes."),
        ],
        OutputTab: "Output", ChainPan: false, ChainTranspose: true, VolumeIsVelocity: true,
        LevelMeter: false, TakesInstrument: false, TakesAudioEffects: false, TakesMidiEffects: true,
        ChainsHint: "Drop MIDI effects here. Output goes to the track instrument.");

    public static RackKindProfile For(RackKind kind) => kind switch
    {
        RackKind.AudioEffect => AudioEffect,
        RackKind.MidiEffect => MidiEffect,
        _ => Instrument,
    };

    /// <summary>The explanation under the "Chains run" choice for an engine mode id.</summary>
    public string ModeHint(int mode)
    {
        foreach (var m in Modes) if (m.Id == mode) return m.Hint;
        return Modes.Length > 0 ? Modes[0].Hint : "";
    }

    /// <summary>The label of an engine mode id ("Parallel" when the rack has no choice).</summary>
    public string ModeLabel(int mode)
    {
        foreach (var m in Modes) if (m.Id == mode) return m.Label;
        return "Parallel";
    }
}
