// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Twin's side edits (specs/055 FR-022): copy one morph side onto the other, or swap them.
// Every sound parameter of a part exists as "<part>.a.<name>" and "<part>.b.<name>", so a side
// edit is a batch of plain param writes, recorded by mix gestures inside one undo group, so it
// is one undo step in the engine's history (specs/009, specs/016).

using System.Collections.Generic;

namespace Nota.Application.Twin;

public static class TwinSides
{
    public const int Kind = 18;

    public enum Edit { CopyAToB, CopyBToA, Swap }

    // The engine's limit on one mix gesture (nota_mix_gesture_begin).
    private const int MaxGestureTargets = 64;

    /// <summary>Applies <paramref name="edit"/> to the part ("lo" / "up") of the track's Twin.
    /// Returns how many params it wrote (0 = not a Twin or nothing to do).</summary>
    public static int Apply(IAudioEngine engine, int trackId, Edit edit, string part = "lo")
    {
        if (engine.TrackInstrumentKind(trackId) != Kind) return 0;
        int n = engine.PluginParamCount(trackId, -1);
        var index = new Dictionary<string, int>(n);
        for (int i = 0; i < n; i++) index[engine.PluginParamId(trackId, -1, i)] = i;
        string a = part + ".a.", b = part + ".b.";
        var pairs = new List<(int A, int B)>();
        foreach (var (id, ia) in index)
            if (id.StartsWith(a, System.StringComparison.Ordinal) && index.TryGetValue(b + id[a.Length..], out var ib))
                pairs.Add((ia, ib));
        if (pairs.Count == 0) return 0;

        // The writes, decided up front (a swap reads both sides before touching either).
        var writes = new List<(int Index, float Value)>(pairs.Count * 2);
        foreach (var (ia, ib) in pairs)
        {
            float va = engine.PluginParamGet(trackId, -1, ia), vb = engine.PluginParamGet(trackId, -1, ib);
            if (edit != Edit.CopyBToA) writes.Add((ib, va));
            if (edit != Edit.CopyAToB) writes.Add((ia, vb));
        }
        Write(engine, trackId, writes);
        return edit == Edit.Swap ? pairs.Count * 2 : pairs.Count;
    }

    /// <summary>Writes plugin params as one undo step: mix gestures of at most
    /// MaxGestureTargets values (the engine's limit) inside one undo group.</summary>
    internal static void Write(IAudioEngine engine, int trackId, IReadOnlyList<(int Index, float Value)> writes)
    {
        engine.BeginUndoGroup();
        try
        {
            for (int at = 0; at < writes.Count; at += MaxGestureTargets)
            {
                int count = System.Math.Min(MaxGestureTargets, writes.Count - at);
                var targets = new MixTarget[count];
                for (int k = 0; k < count; k++) targets[k] = MixTarget.PluginParam(trackId, -1, writes[at + k].Index);
                int gesture = engine.BeginMixGesture(targets);
                for (int k = 0; k < count; k++) engine.PluginParamSet(trackId, -1, writes[at + k].Index, writes[at + k].Value);
                engine.EndMixGesture(gesture);
            }
        }
        finally
        {
            engine.EndUndoGroup();
        }
    }
}
