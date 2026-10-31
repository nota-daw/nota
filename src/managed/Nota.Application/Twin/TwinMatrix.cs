// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin's modulation matrix from the app's side (specs/055 US4): the source and
// destination tables (mirrors of TwinMod.h / TwinMod.cpp — append-only there, so append here
// too; the smoke test checks every name against the engine), and route edits as single undo
// steps. A slot is four params: "<part>.mN.src", "<part>.mN.dst" (indices / 63 and / 255) and
// a depth per side, "<part>.<a|b>.mNamt" (bipolar, 0.5 = none).

using System;
using System.Collections.Generic;

namespace Nota.Application.Twin;

public static class TwinMatrix
{
    public const int Kind = 18;
    public const int Slots = 64;
    public const int SrcScale = 63, DstScale = 255;

    public static readonly string[] Sources =
    {
        "None", "LFO 1", "LFO 2", "LFO 3", "Filter env", "Amp env", "Mod env", "Velocity", "Release velocity", "Key",
        "Pressure", "Slide", "Bend", "Mod wheel", "Pitch wheel", "Pad X", "Pad Y", "Pad Z", "Random",
        "Macro 1", "Macro 2", "Macro 3", "Macro 4", "Macro 5", "Macro 6", "Macro 7", "Macro 8", "Seq gate", "Seq step",
    };

    // Destinations: six virtual ones, then side params by id (TwinMod.cpp kSideDsts order).
    private static readonly string[] Virtual = { "None", "Morph", "Pitch", "Osc 1 pitch", "Osc 2 pitch", "Vibrato" };
    public static readonly string[] SideDestinationIds =
    {
        "o1wave", "o1pw", "o1metal", "o1fine", "o1level", "o1bal", "o2wave", "o2pw", "o2metal", "o2fine", "o2level",
        "o2bal", "fm21", "fmfilt", "noiselevel", "noisecolor", "noisebal", "mmcut", "mmres", "mmmode", "mmkey", "mmenv",
        "ldcut", "ldres", "ldkey", "ldenv", "route", "brute", "drive", "fenva", "fenvd", "fenvs", "fenvr", "fenvvel",
        "fenvcurve", "aenva", "aenvd", "aenvs", "aenvr", "aenvvel", "aenvcurve", "menvdly", "menva", "menvd", "menvs",
        "menvr", "menvvel", "lfo1rate", "lfo1fade", "lfo1amt", "lfo2rate", "lfo2fade", "lfo2amt", "lfo3rate", "lfo3fade",
        "lfo3amt", "lfo3sym", "lfo3curve", "vibamt", "drift", "unidetune", "spread", "modfxrate", "modfxdepth",
        "modfxmix", "dlytime", "dlyfb", "dlymix", "revsize", "revdamp", "revmix",
    };
    public static int DestinationCount => Virtual.Length + SideDestinationIds.Length;
    public static int DestinationOfSide(string id) => Array.IndexOf(SideDestinationIds, id) is var i and >= 0 ? Virtual.Length + i : 0;

    /// <summary>A destination's name: "Morph", or the side param's own ("Ladder Cutoff"), read
    /// from the engine so it never drifts from the param table.</summary>
    public static string DestinationName(IAudioEngine e, int trackId, int dst)
    {
        if (dst >= 0 && dst < Virtual.Length) return Virtual[dst];
        if (dst >= DestinationCount) return "";
        string name = e.PluginParamName(trackId, -1, Index(e, trackId, "lo.a." + SideDestinationIds[dst - Virtual.Length]));
        int dot = name.IndexOf(" · ", StringComparison.Ordinal);
        return dot >= 0 ? name[(dot + 3)..] : name;
    }

    /// <summary>The group a destination sits in for menus ("Osc1", "Ladder"…; "" for the virtual ones).</summary>
    public static string DestinationGroup(IAudioEngine e, int trackId, int dst)
    {
        string n = DestinationName(e, trackId, dst);
        int sp = dst < Virtual.Length ? -1 : n.LastIndexOf(' ');
        return sp > 0 ? n[..sp] : "";
    }

    public readonly record struct Route(int Slot, int Source, int Destination, float DepthA, float DepthB);

    /// <summary>The part's slots that hold a source and a destination.</summary>
    public static List<Route> Routes(IAudioEngine e, int trackId, string part = "lo")
    {
        var list = new List<Route>();
        var at = IndexMap(e, trackId);
        for (int r = 1; r <= Slots; r++)
        {
            int src = Step(e.PluginParamGet(trackId, -1, at[$"{part}.m{r}.src"]), SrcScale);
            int dst = Step(e.PluginParamGet(trackId, -1, at[$"{part}.m{r}.dst"]), DstScale);
            if (src == 0 || dst == 0) continue;
            list.Add(new Route(r, src, dst, Bip(e.PluginParamGet(trackId, -1, at[$"{part}.a.m{r}amt"])),
                Bip(e.PluginParamGet(trackId, -1, at[$"{part}.b.m{r}amt"]))));
        }
        return list;
    }

    /// <summary>Puts a route in the first free slot (or `slot`), one undo step. Returns the slot
    /// (1..64), or −1 when every slot is taken — the matrix is full.</summary>
    public static int Add(IAudioEngine e, int trackId, int source, int destination, float depthA, float depthB, string part = "lo", int slot = 0)
    {
        var at = IndexMap(e, trackId);
        if (slot <= 0)
            for (int r = 1; r <= Slots && slot <= 0; r++)
                if (Step(e.PluginParamGet(trackId, -1, at[$"{part}.m{r}.src"]), SrcScale) == 0
                    || Step(e.PluginParamGet(trackId, -1, at[$"{part}.m{r}.dst"]), DstScale) == 0) slot = r;
        if (slot <= 0 || slot > Slots) return -1;
        Write(e, trackId, at, part, slot, source, destination, depthA, depthB);
        return slot;
    }

    /// <summary>Empties a slot (source, destination, both depths), one undo step.</summary>
    public static void Remove(IAudioEngine e, int trackId, int slot, string part = "lo")
        => Write(e, trackId, IndexMap(e, trackId), part, slot, 0, 0, 0f, 0f);

    private static void Write(IAudioEngine e, int trackId, Dictionary<string, int> at, string part, int slot, int src, int dst, float da, float db)
    {
        var ids = new[] { $"{part}.m{slot}.src", $"{part}.m{slot}.dst", $"{part}.a.m{slot}amt", $"{part}.b.m{slot}amt" };
        var vals = new[] { src / (float)SrcScale, dst / (float)DstScale, 0.5f + 0.5f * Math.Clamp(da, -1f, 1f), 0.5f + 0.5f * Math.Clamp(db, -1f, 1f) };
        var targets = new MixTarget[ids.Length];
        for (int k = 0; k < ids.Length; k++) targets[k] = MixTarget.PluginParam(trackId, -1, at[ids[k]]);
        int g = e.BeginMixGesture(targets);
        for (int k = 0; k < ids.Length; k++) e.PluginParamSet(trackId, -1, at[ids[k]], vals[k]);
        e.EndMixGesture(g);
    }

    private static int Step(float v, int scale) => (int)Math.Round(Math.Clamp(v, 0f, 1f) * scale);
    private static float Bip(float v) => (v - 0.5f) * 2f;
    private static int Index(IAudioEngine e, int trackId, string id)
    {
        int n = e.PluginParamCount(trackId, -1);
        for (int i = 0; i < n; i++) if (e.PluginParamId(trackId, -1, i) == id) return i;
        return -1;
    }
    private static Dictionary<string, int> IndexMap(IAudioEngine e, int trackId)
    {
        int n = e.PluginParamCount(trackId, -1);
        var m = new Dictionary<string, int>(n);
        for (int i = 0; i < n; i++) m[e.PluginParamId(trackId, -1, i)] = i;
        return m;
    }
}
