// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A Drum Rack's pads as the editors show them: each chain that answers a trigger note, named
// after the pad itself (a kit voice or a dropped sample) or else its instrument. Shared by the
// Drum pattern grid and the piano roll's folded keyboard (specs/045).

using Nota.Application;

namespace Nota.Presentation;

public readonly record struct DrumPad(int Chain, int Note, string Name);

public static class DrumPads
{
    /// <summary>The track's pads, lowest note first.</summary>
    public static List<DrumPad> Scan(IAudioEngine e, int trackId)
    {
        var pads = new List<DrumPad>();
        int n = e.RackChainCount(trackId);
        for (int c = 0; c < n; c++)
        {
            int note = e.RackChainTriggerNote(trackId, c);
            // Prefer the pad's own name: every Sampler pad reports the same instrument name,
            // which would make the rows unreadable.
            if (note >= 0)
            {
                string nm = e.RackChainName(trackId, c);
                pads.Add(new DrumPad(c, note, nm.Length > 0 ? nm : e.RackChainInstrumentName(trackId, c)));
            }
        }
        pads.Sort((x, y) => x.Note.CompareTo(y.Note));
        return pads;
    }

    /// <summary>Note → row label; pads sharing a note are joined ("Kick / Kick 2").</summary>
    public static Dictionary<int, string> ByNote(IEnumerable<DrumPad> pads)
    {
        var map = new Dictionary<int, string>();
        foreach (var p in pads)
            map[p.Note] = map.TryGetValue(p.Note, out var had) ? had + " / " + p.Name : p.Name;
        return map;
    }
}
