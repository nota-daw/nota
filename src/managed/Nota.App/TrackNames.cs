// SPDX-License-Identifier: AGPL-3.0-only
using Nota.Application;
using Nota.Application.Localization;

namespace Nota.App;

/// <summary>The name a track shows everywhere (arrangement header, sidechain and source
/// pickers): the stored name when the user set one, otherwise the derived default — the
/// instrument's name for an instrument track, "Group", "Return N" or "Audio N".</summary>
internal static class TrackNames
{
    public static string Of(IAudioEngine eng, in NotaTrackInfo ti)
    {
        string stored = eng.GetTrackName(ti.Id);
        if (stored.Length > 0) return stored;
        if (ti.IsGroup) return Loc.T("Group");
        if (ti.IsReturn) return Loc.F("Return {0}", eng.TrackReturnIndex(ti.Id) + 1);
        if (ti.IsInstrument)
        {
            string inst = eng.DeviceName(ti.Id, -1);   // Synth / Sampler / plugin name
            return string.IsNullOrWhiteSpace(inst) ? Loc.F("Inst {0}", ti.Id) : inst;
        }
        return Loc.F("Audio {0}", ti.Id);
    }

    /// <summary>By track id; "—" when no such track exists.</summary>
    public static string Of(IAudioEngine eng, int id)
    {
        for (int i = 0; i < eng.TrackCount; i++)
            if (eng.TryGetTrackInfo(i, out var ti) && ti.Id == id) return Of(eng, ti);
        return "—";
    }
}
