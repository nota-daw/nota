using Nota.Application.Localization;
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
        ChainsHint: Loc.T("device.rack.drop_an_instrument_to_add"));

    public static readonly RackKindProfile AudioEffect = new(
        RackKind.AudioEffect, "Select", KeyZones: false, VelocityZones: false, SelectRanges: true,
        Modes:
        [
            (Parallel, "Parallel", Loc.T("device.rack.all_chains_get_the_input")),
            (Serial, "Serial", Loc.T("device.rack.chain_1_feeds_chain_2")),
            (Select, "Select", Loc.T("device.rack.only_chains_whose_range_holds_2")),
        ],
        OutputTab: "Rack out", ChainPan: true, ChainTranspose: false, VolumeIsVelocity: false,
        LevelMeter: true, TakesInstrument: false, TakesAudioEffects: true, TakesMidiEffects: false,
        ChainsHint: Loc.T("device.rack.drop_an_effect_to_add"));

    public static readonly RackKindProfile MidiEffect = new(
        RackKind.MidiEffect, "Zones", KeyZones: true, VelocityZones: true, SelectRanges: true,
        Modes:
        [
            (Parallel, "Parallel", Loc.T("device.rack.each_note_goes_to_every")),
            (Select, "Select", Loc.T("device.rack.only_chains_whose_range_holds")),
        ],
        OutputTab: "Output", ChainPan: false, ChainTranspose: true, VolumeIsVelocity: true,
        LevelMeter: false, TakesInstrument: false, TakesAudioEffects: false, TakesMidiEffects: true,
        ChainsHint: Loc.T("device.rack.drop_midi_effects_here_output"));

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
