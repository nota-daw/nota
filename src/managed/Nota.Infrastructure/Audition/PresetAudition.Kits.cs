// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Drum kits in the audition rig: a kit plays on the rig's pad instrument with each pad's
// gain, pan, choke group and effects — what loading it onto a Drum Rack does (a Nota Rhythm
// gets the kit's pad for each of its eight voices, as DrumKitService.FillRhythm picks them).
// Macros stay at rest. The groove is written on the shared 16-pad map (36 kick … 51 cowbell)
// and played at a tempo that suits the kit's style.

using System.Collections.Generic;
using System.IO;
using Nota.Application;
using Nota.Infrastructure.Kits;

namespace Nota.Infrastructure;

public sealed partial class PresetAudition
{
    // Kit pad notes (every factory kit uses the same map).
    private const int Kick = 36, Rim = 37, Snare = 38, Clap = 39, Snare2 = 40, TomLo = 41, HatC = 42, TomMid = 43,
                      HatP = 44, TomHi = 45, HatO = 46, PercLo = 47, PercMid = 48, Crash = 49, PercHi = 50, Bell = 51;

    /// <summary>Sets the rig up as the kit. <paramref name="rhythm"/>: only the eight pads a
    /// Nota Rhythm takes, on its voices' notes.</summary>
    private static bool SetupKit(IAuditionRig rig, KitDefinition kit, bool rhythm)
    {
        KitLibrary.Ensure(kit);   // first use renders the kit's one-shots
        if (!rig.UseKit()) return false;
        var pads = new List<(int Note, KitPad Pad)>();
        if (rhythm)
        {
            var voices = DrumKitService.RhythmVoicePads(kit);
            for (int v = 0; v < voices.Length; v++)
                if (voices[v] is { } p) pads.Add((RhythmModel.MidiNotes[v], p));
        }
        else foreach (var p in kit.Pads) pads.Add((p.Note, p));

        int loaded = 0;
        foreach (var (note, pad) in pads)
        {
            var path = KitLibrary.PathOf(kit, pad);
            if (!File.Exists(path)) continue;
            int pi = rig.KitAddPad(note, path, pad.Gain, pad.Pan, pad.Choke);
            if (pi < 0) continue;
            loaded++;
            foreach (var fx in KitFx.For(kit, pad))
            {
                int di = rig.KitPadAddDevice(pi, fx.Kind);
                if (di < 0) continue;
                foreach (var (name, value) in fx.Params) rig.KitPadDeviceParam(pi, di, name, value);
            }
        }
        return loaded > 0;
    }

    // Tempo from the kit's style line: a breakbeat kit wants pace, boom bap wants to lean back.
    private static double KitBpm(KitDefinition kit)
    {
        string b = kit.Blurb.ToLowerInvariant();
        bool Has(params string[] w) { foreach (var x in w) if (b.Contains(x)) return true; return false; }
        if (Has("jungle", "breakbeat")) return 160;
        if (Has("techno", "industrial", "hyperpop", "edm")) return 128;
        if (Has("house", "garage", "disco", "afro", "electro")) return 122;
        if (Has("boom bap", "vinyl", "dub", "reggae", "808", "sub-forward")) return 88;
        if (Has("jazz", "ambient", "cinematic", "orchestral")) return 84;
        return 104;
    }

    // Two bars: a groove with a crash on the one, a 16th-note hat run with the kit's swing on
    // the off-16ths, some percussion in bar 2 and a tom fill into the end. Notes a kit lacks
    // simply don't sound.
    private static AuditionNote[] KitGroove(float swing)
    {
        var n = new List<AuditionNote>();
        double Sw(double b) => Math.Abs(b * 4 - Math.Round(b * 4)) < 1e-6 && ((int)Math.Round(b * 4)) % 2 == 1 ? b + swing * 0.125 : b;
        void Hit(double b, int note, float v) => n.Add(N(Sw(b), 0.2, note, v));
        foreach (double b in new[] { 0, 1.75, 2.5, 4, 4.75, 6.5 }) Hit(b, Kick, 0.95f);
        foreach (double b in new[] { 1.0, 3.0, 5.0 }) Hit(b, Snare, 0.9f);
        Hit(7, Clap, 0.85f); Hit(7, Snare, 0.75f);
        Hit(3.75, Snare, 0.3f);                                    // ghost
        Hit(0, Crash, 0.7f);
        for (int i = 0; i < 16; i++)                               // bar 1: eighths, open on the "and" of 4
            if (i % 2 == 0) Hit(i * 0.25, i == 14 ? HatO : HatC, i % 4 == 0 ? 0.7f : 0.5f);
        for (int i = 16; i < 28; i++)                              // bar 2: sixteenths (swing shows)
            Hit(i * 0.25, HatC, i % 4 == 0 ? 0.7f : i % 2 == 0 ? 0.5f : 0.32f);
        Hit(4.75, PercMid, 0.6f); Hit(5.5, PercHi, 0.55f); Hit(5.75, PercLo, 0.6f); Hit(6.25, Bell, 0.5f); Hit(2.25, Rim, 0.45f);
        Hit(7.25, TomHi, 0.8f); Hit(7.5, TomMid, 0.8f); Hit(7.75, TomLo, 0.85f);
        return n.ToArray();
    }

    private AuditionPlan? KitPlan(string keyBase, string kitId, bool rhythm, out string reason)
    {
        reason = "";
        var kit = KitCatalog.ById(kitId);
        if (kit is null) { reason = "Kit not found."; return null; }
        return new AuditionPlan(keyBase, rhythm ? "Nota Rhythm" : "Nota Drum Rack", $"groove · {kit.Name}", false, "", "",
            rig => SetupKit(rig, kit, rhythm), KitGroove(kit.Swing), KitBpm(kit), 8, 2.5, false);
    }
}
