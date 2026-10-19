// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Default project template (specs/004-default-project-template): the new-project content
// (instrument + audio track, "Reverb" and "Delay" returns, both wet only), the
// nota_engine_clear_undo_history baseline it relies on, and a save/load round trip.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --template`.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class ProjectTemplateTests
{
    private const int ReverbWetOnly = 19, DelayWetOnly = 22;

    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in ClearUndo()) yield return r;
        foreach (var r in Template()) yield return r;
        foreach (var r in RoundTrip()) yield return r;
    }

    // The ABI primitive: history goes, the session stays, later edits undo as usual.
    private static IEnumerable<(bool, string)> ClearUndo()
    {
        using var eng = new NotaEngine();
        int a = eng.AddAudioTrack();
        yield return (eng.CanUndo, "clear-undo: an edit is undoable before clearing");
        eng.ClearUndoHistory();
        yield return (!eng.CanUndo && !eng.CanRedo, "clear-undo: no undo/redo after clearing");
        yield return (!eng.Undo(), "clear-undo: Undo reports nothing to undo");
        yield return (eng.TrackCount == 1 && eng.TryGetTrackInfo(0, out var t) && t.Id == a,
            "clear-undo: the session is untouched");
        eng.AddAudioTrack();
        yield return (eng.CanUndo, "clear-undo: the next edit is undoable");
        yield return (eng.Undo() && eng.TrackCount == 1 && !eng.CanUndo,
            "clear-undo: Undo goes back to the cleared baseline, no further");
    }

    // Tracks in order, as plain tracks first, then the return section.
    private static (List<NotaTrackInfo> tracks, List<NotaTrackInfo> returns) Split(IAudioEngine eng)
    {
        var tracks = new List<NotaTrackInfo>();
        var returns = new List<NotaTrackInfo>();
        for (int i = 0; i < eng.TrackCount; i++)
            if (eng.TryGetTrackInfo(i, out var t)) (t.IsReturn ? returns : tracks).Add(t);
        return (tracks, returns);
    }

    private static bool ReturnHolds(IAudioEngine eng, NotaTrackInfo r, string name, int kind, int wetOnly)
        => eng.GetTrackName(r.Id) == name
        && eng.TrackDeviceCount(r.Id) == 1
        && eng.TrackDeviceBuiltinKind(r.Id, 0) == kind
        && eng.DeviceParamName(r.Id, 0, wetOnly) == "Wet Only"
        && eng.DeviceGetParam(r.Id, 0, wetOnly) >= 0.5f;

    // Checks shared by the fresh template and its reloaded copy.
    private static IEnumerable<(bool, string)> Shape(IAudioEngine eng, string tag)
    {
        var (tracks, returns) = Split(eng);
        yield return (tracks.Count == 2 && returns.Count == 2,
            $"{tag}: 2 tracks + 2 returns (got {tracks.Count} + {returns.Count})");
        if (tracks.Count != 2 || returns.Count != 2) yield break;
        var inst = tracks[0];
        var audio = tracks[1];
        yield return (inst.IsInstrument && eng.TrackInstrumentKind(inst.Id) == 6, $"{tag}: track 1 is an instrument with Nota Volt");
        yield return (inst.ClipCount == 1 && eng.TryGetClipInfo(inst.Id, 0, out var c)
                      && c.IsMidi && c.StartBeat == 0.0 && c.LengthBeats == 4.0,
            $"{tag}: the instrument has the seed 1-bar MIDI clip");
        yield return (audio.Type == 0 && audio.ClipCount == 0, $"{tag}: track 2 is an empty audio track");
        yield return (eng.TrackReturnIndex(returns[0].Id) == 0 && eng.TrackReturnIndex(returns[1].Id) == 1,
            $"{tag}: returns are buses A and B");
        yield return (ReturnHolds(eng, returns[0], "Reverb", 2, ReverbWetOnly), $"{tag}: return A = \"Reverb\" with Nota Reverb, Wet Only on");
        yield return (ReturnHolds(eng, returns[1], "Delay", 3, DelayWetOnly), $"{tag}: return B = \"Delay\" with Nota Delay, Wet Only on");
    }

    private static IEnumerable<(bool, string)> Template()
    {
        using var eng = new NotaEngine();
        int inst = ProjectTemplate.ApplyDefault(eng);
        foreach (var r in Shape(eng, "template")) yield return r;
        var (tracks, _) = Split(eng);
        yield return (tracks.Count > 0 && tracks[0].Id == inst, "template: returns the instrument track id");

        // Sends start where a hand-added track's sends start (off).
        using var refEng = new NotaEngine();
        int refTrack = refEng.AddAudioTrack();
        refEng.AddReturnTrack();
        float off = refEng.GetTrackSend(refTrack, 0);
        bool sendsOff = tracks.All(t => eng.GetTrackSend(t.Id, 0) == off && eng.GetTrackSend(t.Id, 1) == off);
        yield return (sendsOff, $"template: every send starts at the default level ({off})");

        yield return (!eng.CanUndo && !eng.CanRedo, "template: the undo history is empty");
        if (tracks.Count == 2)
        {
            int audio = tracks[1].Id;
            eng.RemoveTrack(audio);
            yield return (eng.Undo() && Split(eng).tracks.Any(t => t.Id == audio) && !eng.CanUndo,
                "template: deleting a template track undoes back to the template");
        }
    }

    private static IEnumerable<(bool, string)> RoundTrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nota-template-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var eng = new NotaEngine();
            ProjectTemplate.ApplyDefault(eng);
            var warnings = new List<string>();
            var doc = ProjectService.Capture(eng, new TransportState(120, 1, false, false), warnings);
            ProjectService.Save(doc, dir, eng);
            using var eng2 = new NotaEngine();
            var applied = ProjectService.Apply(ProjectService.Load(dir), eng2, dir);
            yield return (warnings.Count == 0 && applied.Count == 0, "round-trip: saved and reloaded without warnings");
            foreach (var r in Shape(eng2, "round-trip")) yield return r;
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
