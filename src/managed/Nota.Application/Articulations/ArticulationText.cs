// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// How articulations read in the interface (specs/085): an action as "Keyswitch C1 · vel 1",
// a map as "44 articulations". Pure, so the import preview and the inspector show the same
// wording and the smoke test can check it without a window.

using Nota.Application.Localization;

namespace Nota.Application.Articulations;

public static class ArticulationText
{
    public static string Action(MidiAction a)
    {
        string body = a.Kind switch
        {
            MidiActionKind.Keyswitch => Loc.F("articulations.action.keyswitch", SamplerModel.NoteName(a.Data1), a.Data2),
            MidiActionKind.ControlChange => Loc.F("articulations.action.control_change", a.Data1, a.Data2),
            MidiActionKind.ProgramChange => Loc.F("articulations.action.program_change", a.Data1),
            MidiActionKind.BankSelectMsb => Loc.F("articulations.action.bank_msb", a.Data2),
            _ => Loc.F("articulations.action.bank_lsb", a.Data2),
        };
        return a.Channel is int ch ? Loc.F("articulations.action.on_channel", body, ch) : body;
    }

    /// <summary>Every action of an articulation, in send order; a dash when it sends nothing.</summary>
    public static string Actions(Articulation art) =>
        art.Actions.Count == 0 ? Loc.T("articulations.action.none") : string.Join("  ·  ", art.Actions.Select(Action));

    public static string Count(ArticulationMap map) => Loc.Plural("articulations.count", map.Articulations.Count);

    /// <summary>A group's label: its name, else a numbered one (files often number groups without naming them).</summary>
    public static string Group(ArticulationGroup g) => g.Name.Length > 0 ? g.Name : Loc.F("articulations.group_n", g.Id);

    /// <summary>FR-018: when each articulation's notes play, relative to the grid — the track offset
    /// plus the articulation's own, applied once. Only articulations with an offset of their own
    /// (the rest play at the track offset), enabled ones, in map order. The view formats the numbers.</summary>
    public static IReadOnlyList<(string Name, double TotalMs)> EffectiveOffsets(ArticulationMap map, double trackOffsetMs) =>
        map.Articulations.Where(a => a.Enabled && a.NoteOffsetMs != 0)
            .Select(a => (a.Name, trackOffsetMs + a.NoteOffsetMs)).ToList();
}
