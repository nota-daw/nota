// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// IDrumKits over the code-defined kit catalog: renders a kit's one-shots on demand
// (KitLibrary) and assembles a Drum Rack from them — one Sampler chain per pad, with
// the pad's trigger note, choke group, pan, gain and name, plus the kit's swing and
// humanize. Loading a kit is an ordinary sequence of engine calls, so the result is an
// ordinary Drum Rack the user can take apart pad by pad.

using System;
using System.Collections.Generic;
using System.IO;
using Nota.Application;
using Nota.Infrastructure.Kits;

namespace Nota.Infrastructure;

public sealed class DrumKitService : IDrumKits
{
    public string Root => KitLibrary.Root;

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

    public bool LoadInto(IAudioEngine engine, int trackId, string id, out string warning)
    {
        var kit = KitCatalog.ById(id);
        if (kit is null) { warning = "Unknown drum kit."; return false; }
        // Clear the existing pads first — a kit replaces the rack's contents rather than
        // layering onto whatever was there.
        for (int c = engine.RackChainCount(trackId) - 1; c >= 0; c--) engine.RackRemoveChain(trackId, c);
        Fill(engine, trackId, kit, out warning);
        return true;
    }

    public string Identify(IAudioEngine engine, int trackId)
    {
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
        }

        engine.RackSetSwing(track, kit.Swing);
        engine.RackSetHumanize(track, kit.Humanize);

        warning = missing.Count == 0
            ? ""
            : $"{kit.Name}: {missing.Count} pad(s) couldn't be loaded ({string.Join(", ", missing)}).";
    }
}
