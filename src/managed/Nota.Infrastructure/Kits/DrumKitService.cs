// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// IDrumKits over the code-defined kit catalog: renders a kit's one-shots on demand
// (KitLibrary) and assembles a Drum Rack from them — one Sampler chain per pad, with
// the pad's trigger note, choke group, pan, gain, name and effects (KitFx), plus the
// kit's swing and humanize. Loading a kit is an ordinary sequence of engine calls, so the
// result is an ordinary Drum Rack the user can take apart pad by pad.
//
// The same kits load into Nota Rhythm: each of its eight voices takes the kit's pad on the
// voice's own MIDI note (Kick 36, Snare 38, Clap 39, Rim 37, the hats 42 / 46, Tom 45) and
// a percussion pad for Perc, with that pad's effects on the voice's insert chain — so a
// kit sounds the same in either instrument.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nota.Application;
using Nota.Infrastructure.Kits;

namespace Nota.Infrastructure;

public sealed class DrumKitService : IDrumKits
{
    public string Root => KitLibrary.Root;

    public string DefaultRhythmKit => "kompakt";

    public IReadOnlyList<DrumKitInfo> All()
    {
        var list = new List<DrumKitInfo>(KitCatalog.All.Count);
        foreach (var k in KitCatalog.All) list.Add(new DrumKitInfo(k.Id, k.Name, k.Blurb, k.Pads.Count));
        return list;
    }

    public string FolderOf(string id)
    {
        var kit = KitCatalog.ById(id);
        return kit is null ? "" : KitLibrary.DirOf(kit);
    }

    public bool IsRendered(string id)
    {
        var kit = KitCatalog.ById(id);
        return kit is not null && KitLibrary.IsRendered(kit);
    }

    public int EnsureRendered(string id = "")
    {
        if (string.IsNullOrEmpty(id)) return KitLibrary.EnsureAll();
        var kit = KitCatalog.ById(id);
        return kit is null ? 0 : KitLibrary.Ensure(kit);
    }

    public int CreateTrack(IAudioEngine engine, string id, out string warning)
    {
        var kit = KitCatalog.ById(id);
        if (kit is null) { warning = "Unknown drum kit."; return -1; }
        int track = engine.AddDrumRackTrack();
        if (track <= 0) { warning = "Couldn't create a Drum Rack track."; return -1; }
        Fill(engine, track, kit, out warning);
        return track;
    }

    public int CreateRhythmTrack(IAudioEngine engine, string id, out string warning)
    {
        var kit = KitCatalog.ById(id);
        if (kit is null) { warning = "Unknown drum kit."; return -1; }
        int track = engine.AddRhythmTrack();
        if (track <= 0) { warning = "Couldn't create a Nota Rhythm track."; return -1; }
        FillRhythm(engine, track, kit, out warning);
        return track;
    }

    public bool LoadInto(IAudioEngine engine, int trackId, string id, out string warning)
    {
        var kit = KitCatalog.ById(id);
        if (kit is null) { warning = "Unknown drum kit."; return false; }
        if (engine.TrackInstrumentKind(trackId) == RhythmModel.Kind) { FillRhythm(engine, trackId, kit, out warning); return true; }
        // Clear the existing pads first — a kit replaces the rack's contents rather than
        // layering onto whatever was there.
        for (int c = engine.RackChainCount(trackId) - 1; c >= 0; c--) engine.RackRemoveChain(trackId, c);
        Fill(engine, trackId, kit, out warning);
        return true;
    }

    public string Identify(IAudioEngine engine, int trackId)
    {
        // A Rhythm remembers the kit it was loaded from.
        if (engine.TrackInstrumentKind(trackId) == RhythmModel.Kind)
            return KitCatalog.ById(engine.RhythmKitName(trackId)) is { } rk ? rk.Id : "";
        int chains = engine.RackChainCount(trackId);
        if (chains <= 0) return "";
        var pads = new HashSet<(int Note, string Name)>();
        for (int c = 0; c < chains; c++)
        {
            int note = engine.RackChainTriggerNote(trackId, c);
            if (note >= 0) pads.Add((note, engine.RackChainName(trackId, c)));
        }

        // The kit sharing the most pads wins, if it shares at least three quarters of its
        // own and no other kit ties it — most kits have a Kick on 36 and a Snare on 38, so
        // a near-empty rack must not read as whichever kit happens to come first.
        string best = "";
        int bestScore = 0, runnerUp = 0;
        foreach (var kit in KitCatalog.All)
        {
            int score = 0;
            foreach (var p in kit.Pads) if (pads.Contains((p.Note, p.Name))) score++;
            if (score > bestScore) { runnerUp = bestScore; bestScore = score; best = kit.Id; }
            else if (score > runnerUp) runnerUp = score;
        }
        var winner = KitCatalog.ById(best);
        return winner is not null && bestScore * 4 >= winner.Pads.Count * 3 && bestScore > runnerUp ? best : "";
    }

    private static void Fill(IAudioEngine engine, int track, KitDefinition kit, out string warning)
    {
        KitLibrary.Ensure(kit);

        var missing = new List<string>();
        foreach (var pad in kit.Pads)
        {
            var path = KitLibrary.PathOf(kit, pad);
            if (!File.Exists(path)) { missing.Add(pad.Name); continue; }

            // rootNote = the pad's own note, so the sample plays back untransposed and
            // the pad's Tune control stays the only pitch offset.
            int chain = engine.RackAddSamplerChain(track, path, pad.Note, false);
            if (chain < 0) { missing.Add(pad.Name); continue; }

            engine.RackSetChainTriggerNote(track, chain, pad.Note);
            engine.RackSetChainName(track, chain, pad.Name);
            if (pad.Choke > 0) engine.RackSetChainChoke(track, chain, pad.Choke);
            if (pad.Pan != 0f) engine.RackSetChainPan(track, chain, pad.Pan);
            if (Math.Abs(pad.Gain - 1f) > 1e-3f) engine.RackSetChainGain(track, chain, pad.Gain);
            int c = chain;
            KitFx.Apply(KitFx.For(kit, pad), k => engine.RackAddChainDevice(track, c, k),
                d => engine.RackChainDeviceParamCount(track, c, d), (d, p) => engine.RackChainDeviceParamName(track, c, d, p),
                (d, p, v) => engine.RackChainDeviceParamSet(track, c, d, p, v));
        }

        engine.RackSetSwing(track, kit.Swing);
        engine.RackSetHumanize(track, kit.Humanize);

        warning = missing.Count == 0
            ? ""
            : $"{kit.Name}: {missing.Count} pad(s) couldn't be loaded ({string.Join(", ", missing)}).";
    }

    /// <summary>The kit's pad for each Rhythm voice (null = the kit has none for it).</summary>
    public static KitPad?[] RhythmVoicePads(KitDefinition kit)
    {
        KitPad? At(int note) => kit.Pads.FirstOrDefault(p => p.Note == note);
        var pads = new KitPad?[RhythmModel.Voices];
        for (int v = 0; v < RhythmModel.Voices; v++) pads[v] = At(RhythmModel.MidiNotes[v]);
        // Tom: the high tom Rhythm's note names, else the nearest tom; Perc: the kit's own
        // percussion (the 47 / 48 / 50 slots), since Rhythm's note 41 is the low tom in a kit.
        pads[6] ??= new[] { 43, 41, 47, 48, 50 }.Select(At).FirstOrDefault(p => p is not null);
        pads[7] = new[] { 47, 50, 48, 51, 41 }.Select(At).FirstOrDefault(p => p is not null && KitFx.RoleOf(p) == PadRole.Perc)
                  ?? At(47) ?? pads[7];
        return pads;
    }

    // Loads the kit's samples, levels and effects onto a Rhythm's voices. A voice's sample
    // plays as recorded — tune centred, the full file, no decay shortening, the tone open and
    // no drive (the kit's own effects do the colouring) — and the steps are left alone.
    private static void FillRhythm(IAudioEngine engine, int track, KitDefinition kit, out string warning)
    {
        KitLibrary.Ensure(kit);
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        void Set(string id, float v) { if (idx.TryGetValue(id, out int i)) engine.PluginParamSet(track, -1, i, v); }

        var missing = new List<string>();
        var pads = RhythmVoicePads(kit);
        for (int v = 0; v < RhythmModel.Voices; v++)
        {
            while (engine.RhythmVoiceDeviceCount(track, v) > 0) engine.RhythmRemoveVoiceDevice(track, v, 0);
            var pad = pads[v];
            if (pad is null) { engine.InstrumentAction(track, RhythmModel.A_SetSource, v, 0f); continue; }
            if (!engine.SetRhythmVoiceSample(track, v, KitLibrary.PathOf(kit, pad))) { missing.Add(pad.Name); continue; }
            Set(RhythmModel.Id(v, "tune"), 0.5f);
            Set(RhythmModel.Id(v, "decay"), 1f);
            Set(RhythmModel.Id(v, "tone"), 1f);
            Set(RhythmModel.Id(v, "drive"), 0f);
            Set(RhythmModel.Id(v, "start"), 0f);
            Set(RhythmModel.Id(v, "length"), 1f);
            Set(RhythmModel.Id(v, "reverse"), 0f);
            Set(RhythmModel.Id(v, "level"), Math.Clamp(RhythmLevel * pad.Gain, 0f, 1f));
            Set(RhythmModel.Id(v, "pan"), Math.Clamp(0.5f + pad.Pan * 0.5f, 0f, 1f));
            int vv = v;
            KitFx.Apply(KitFx.For(kit, pad), k => engine.RhythmAddVoiceDevice(track, vv, k),
                d => engine.RhythmVoiceDeviceParamCount(track, vv, d), (d, p) => engine.RhythmVoiceDeviceParamName(track, vv, d, p),
                (d, p, x) => engine.RhythmVoiceDeviceParamSet(track, vv, d, p, x));
        }
        Set("swing", kit.Swing);
        Set("humanize", kit.Humanize);
        engine.RhythmSetKitName(track, kit.Id);

        warning = missing.Count == 0
            ? ""
            : $"{kit.Name}: {missing.Count} voice(s) couldn't be loaded ({string.Join(", ", missing)}).";
    }

    /// <summary>A kit voice's Level: the kit's own gain on the headroom Rhythm leaves a voice.</summary>
    private const float RhythmLevel = 0.8f;
}
