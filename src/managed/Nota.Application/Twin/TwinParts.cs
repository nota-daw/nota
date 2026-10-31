// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin's part edits (specs/055 FR-075, US9-4): copy one part onto the other, swap them, or
// load a preset's sound into one part. A part is every param under "lo." or "up." — both sides,
// the matrix, the effects, the voice settings — so an edit remaps ids between the two prefixes
// and writes them as one undo step (TwinSides.Write). The patch-wide params (mode, split key,
// share, voices, pad, sequencer) stay, except that loading into Upper of a Single patch turns
// it to Layer, with the current sound as Lower.

using System.Collections.Generic;

namespace Nota.Application.Twin;

public static class TwinParts
{
    public enum Edit { CopyLowerToUpper, CopyUpperToLower, Swap }

    private const string Lo = "lo.", Up = "up.";

    private static Dictionary<string, int> Index(IAudioEngine engine, int trackId)
    {
        int n = engine.PluginParamCount(trackId, -1);
        var index = new Dictionary<string, int>(n);
        for (int i = 0; i < n; i++) index[engine.PluginParamId(trackId, -1, i)] = i;
        return index;
    }

    /// <summary>Copies or swaps the parts of the track's Twin. Returns how many params it wrote
    /// (0 = not a Twin).</summary>
    public static int Apply(IAudioEngine engine, int trackId, Edit edit)
    {
        if (engine.TrackInstrumentKind(trackId) != TwinSides.Kind) return 0;
        var index = Index(engine, trackId);
        var writes = new List<(int Index, float Value)>();
        foreach (var (id, il) in index)
        {
            if (!id.StartsWith(Lo, System.StringComparison.Ordinal) || !index.TryGetValue(Up + id[Lo.Length..], out var iu)) continue;
            float vl = engine.PluginParamGet(trackId, -1, il), vu = engine.PluginParamGet(trackId, -1, iu);
            if (edit != Edit.CopyUpperToLower) writes.Add((iu, vl));
            if (edit != Edit.CopyLowerToUpper) writes.Add((il, vu));
        }
        if (writes.Count == 0) return 0;
        TwinSides.Write(engine, trackId, writes);
        return writes.Count;
    }

    /// <summary>Loads a Twin preset's sound — its Lower part, by param id — into <paramref name="part"/>
    /// ("lo" / "up"); a param the preset doesn't name goes to its default. Loading into Upper of a
    /// Single patch switches it to Layer. One undo step; returns "" or a user-facing warning.</summary>
    public static string LoadIntoPart(IAudioEngine engine, int trackId, IReadOnlyDictionary<string, float> preset, string part)
    {
        if (engine.TrackInstrumentKind(trackId) != TwinSides.Kind) return "Not a Nota Twin.";
        if (part is not ("lo" or "up")) return "Unknown part.";
        var index = Index(engine, trackId);
        string to = part + ".";
        var writes = new List<(int Index, float Value)>();
        foreach (var (id, ti) in index)
        {
            if (!id.StartsWith(to, System.StringComparison.Ordinal)) continue;
            string src = Lo + id[to.Length..];
            writes.Add((ti, preset.TryGetValue(src, out var v) ? v : engine.InstrumentParamDefault(trackId, ti)));
        }
        if (part == "up" && index.TryGetValue("mode", out var mi) && engine.PluginParamGet(trackId, -1, mi) < 0.25f)
            writes.Add((mi, 0.5f));
        TwinSides.Write(engine, trackId, writes);
        return "";
    }
}
