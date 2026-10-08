// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-3/4/6): where the highlighted item would go, in words — the pill in the
// search field ("→ Bass · after Nota Vintage", "→ new MIDI track") — and whether it can go
// anywhere at all ("Select a track first"). Pure: reads the frozen context, so the line
// changes only with the highlighted row and the held modifiers.

namespace Nota.Application.Palette;

public static class PaletteTargets
{
    /// <summary>Why an item can't be applied in this context (CP-4/CP-8), or Yes. Actions
    /// answer through their command's CanExecute, passed in.</summary>
    public static Availability Check(PaletteItem it, PaletteContext ctx, Func<string, Availability>? actionCheck = null)
    {
        switch (it.Kind)
        {
            case PaletteKind.Action:
                return actionCheck?.Invoke(it.Payload as string ?? "") ?? Availability.Yes;
            case PaletteKind.Modular:
                return ctx.HasTrack ? Availability.Yes : Availability.No("Select a track first");
            case PaletteKind.Device:
            case PaletteKind.Preset:
                if (it.Role == PaletteDeviceRole.Instrument) return Availability.Yes;   // no track → a new one (CP-4)
                if (!ctx.HasTrack) return Availability.No("Select a track first");
                if (it.Role == PaletteDeviceRole.MidiEffect && ctx.TrackType != PaletteTrackType.Instrument)
                    return Availability.No("MIDI effects need an instrument track");
                return Availability.Yes;
        }
        return Availability.Yes;
    }

    /// <summary>The target line for an item under the held modifiers. <c>Bad</c>: it can't be
    /// applied, and the text says why.</summary>
    public static (string Text, bool Bad) Describe(PaletteItem? it, PaletteContext ctx, ApplyMode mode, Func<string, Availability>? actionCheck = null)
    {
        if (it is null) return ("", false);
        var a = Check(it, ctx, actionCheck);
        if (!a.Ok) return (a.Reason, true);
        string keep = (mode & ApplyMode.KeepOpen) != 0 && it.Kind is PaletteKind.Device or PaletteKind.Preset or PaletteKind.Modular ? " · keep open" : "";
        string track = ctx.TrackName.Length > 0 ? ctx.TrackName : $"track {ctx.TrackId}";
        switch (it.Kind)
        {
            case PaletteKind.Action: return ("↵ run", false);
            case PaletteKind.Track:
                return ((mode & ApplyMode.NewTrack) != 0 ? $"→ open {it.Name} in Devices" : $"→ select {it.Name} in {ctx.View}", false);
            case PaletteKind.Modular: return ($"→ {track} · canvas, unconnected{keep}", false);
        }

        bool newTrack = (mode & ApplyMode.NewTrack) != 0;
        if (it.Role == PaletteDeviceRole.Instrument)
        {
            bool instTrack = ctx.HasTrack && ctx.TrackType == PaletteTrackType.Instrument;
            if (it.Kind == PaletteKind.Preset)
            {
                if (it.PresetOrigin == PresetOrigin.Kit)
                    return !newTrack && instTrack && ctx.InstrumentKind is 4 or 12
                        ? ($"→ {track} · loads the kit{keep}", false) : ($"→ new track{keep}", false);
                if (it.PresetOrigin != PresetOrigin.Factory || newTrack || !instTrack || ctx.IsRack)
                    return ($"→ new MIDI track{keep}", false);
                return ($"→ {track} · {(ctx.InstrumentName.Length > 0 ? "replaces " + ctx.InstrumentName : "instrument")}{keep}", false);
            }
            if (newTrack || !instTrack) return ($"→ new MIDI track{keep}", false);
            if (ctx.InstrumentKind == 4) return ($"→ {track} · new pad in Drum Rack{keep}", false);
            if (ctx.InstrumentKind == 3) return ($"→ {track} · new chain in Instrument Rack{keep}", false);
            return ($"→ {track} · replaces {(ctx.InstrumentName.Length > 0 ? ctx.InstrumentName : "the instrument")}{keep}", false);
        }

        bool midi = it.Role == PaletteDeviceRole.MidiEffect;
        string where;
        if (ctx.Origin == PaletteOrigin.Devices)
        {
            var ins = ctx.Insert;
            bool alt = (mode & ApplyMode.Replace) != 0;
            if (!midi && ins.Section == ChainSection.Audio && ins.Index >= 0) where = (alt ? "replaces " : "after ") + ins.DeviceName;
            else if (midi && ins.Section == ChainSection.Midi && ins.Index >= 0) where = (alt ? "replaces " : "after ") + ins.DeviceName;
            else if (!midi && ins.Section == ChainSection.Instrument && ctx.InstrumentName.Length > 0) where = "after " + ctx.InstrumentName;
            else where = midi ? "end of MIDI FX" : "end of Audio FX";
        }
        else where = midi ? "MIDI FX, end" : "end of chain";
        return ($"→ {track} · {where}{keep}", false);
    }

    /// <summary>Ids suggested for an empty query in each context (CP-19).</summary>
    public static IReadOnlyList<string> Suggested(PaletteContext ctx) => ctx.Origin switch
    {
        PaletteOrigin.Devices or PaletteOrigin.DeviceWindow or PaletteOrigin.Mixer =>
            ["d:be:1", "d:be:0", "d:be:2", "d:be:3", "d:be:17"],
        PaletteOrigin.Modular => ["m:0", "m:1", "m:3", "m:5"],
        PaletteOrigin.Session => ["a:session.launchScene", "a:session.stopAll", "a:session.insertScene"],
        PaletteOrigin.PianoRoll => ["a:roll.quantize", "a:roll.octaveUp", "a:roll.octaveDown"],
        PaletteOrigin.Browser => ["d:bi:6", "d:be:2", "a:prefs.plugins"],
        _ => ctx.HasTrack
            ? ["a:edit.duplicate", "a:edit.split", "a:edit.consolidate", "a:track.freeze"]
            : ["a:track.newInstrument", "a:track.newAudio", "d:bi:6"],
    };
}
