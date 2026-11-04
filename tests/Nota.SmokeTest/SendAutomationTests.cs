// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Sends as first-class mix values (specs/072-arrangement-track-sends, #143): a send gesture is
// one exact undo step like volume and pan, a send lane plays back into the track's send level,
// recording a send writes its lane in the same step as the value, the lane survives save/load
// on its bus, and deleting a return drops the lanes to its bus and moves later ones down with
// the send levels — undo bringing both back. The arrangement header's knobs rely on this.
// Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --sends`.

using Nota.Application;
using Nota.Application.Editing;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SendAutomationTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in Undo()) yield return r;
        foreach (var r in Playback()) yield return r;
        foreach (var r in Record()) yield return r;
        foreach (var r in SaveLoad()) yield return r;
        foreach (var r in ReturnDelete()) yield return r;
    }

    // ---- value undo -------------------------------------------------------------------

    private static IEnumerable<(bool, string)> Undo()
    {
        using var eng = new NotaEngine();
        int t = eng.AddInstrumentTrack();
        eng.AddReturnTrack();
        eng.AddReturnTrack();
        eng.ClearUndoHistory();

        // A drag: one gesture, many values, one step back to where it started.
        var g = new MixGesture(eng, MixTarget.Send(t, 1));
        g.Begin();
        for (int i = 1; i <= 10; i++) g.Change(i * 0.06f, v => eng.SetTrackSend(t, 1, v));
        g.End();
        float after = eng.GetTrackSend(t, 1);
        bool one = eng.Undo() && eng.GetTrackSend(t, 1) == 0f && !eng.CanUndo;
        bool redo = eng.Redo() && eng.GetTrackSend(t, 1) == after;
        yield return (Math.Abs(after - 0.6f) < 1e-6f && one && redo,
            $"sends: a send drag is one undo step and redoes (0 → {after})");

        // A gesture that ends where it began adds nothing.
        eng.ClearUndoHistory();
        var still = new MixGesture(eng, MixTarget.Send(t, 1));
        still.Begin();
        still.Change(0.9f, v => eng.SetTrackSend(t, 1, v));
        still.Change(after, v => eng.SetTrackSend(t, 1, v));
        still.End();
        yield return (!eng.CanUndo, "sends: a send gesture that changes nothing adds no step");

        // Other buses are untouched by a step on one.
        eng.SetTrackSend(t, 0, 0.3f);
        eng.ClearUndoHistory();
        MixEdit.Step(eng, () => eng.SetTrackSend(t, 1, 0.1f), MixTarget.Send(t, 1));
        eng.Undo();
        yield return (eng.GetTrackSend(t, 0) == 0.3f && eng.GetTrackSend(t, 1) == after,
            "sends: undoing bus B's step leaves bus A alone");

        // MCP set_send is one step too.
        eng.ClearUndoHistory();
        var tools = new Nota.Mcp.Tools.TrackTools(eng, new SyncDispatch(), new NoRefresh());
        tools.SetSend(t, 0, 0.75f).GetAwaiter().GetResult();
        bool set = eng.GetTrackSend(t, 0) == 0.75f;
        yield return (set && eng.Undo() && eng.GetTrackSend(t, 0) == 0.3f && !eng.CanUndo,
            "sends: an MCP set_send call is one undo step");

        // A step whose track is gone is skipped quietly (like volume).
        int gone = eng.AddAudioTrack();
        eng.ClearUndoHistory();
        MixEdit.Step(eng, () => eng.SetTrackSend(gone, 0, 0.5f), MixTarget.Send(gone, 0));
        eng.RemoveTrack(gone);
        bool skipped = true;
        for (int i = 0; i < 4 && eng.CanUndo; i++) skipped &= eng.Undo();
        yield return (skipped, "sends: undo steps past a send step whose track was deleted");
    }

    // ---- automation playback ------------------------------------------------------------

    private static IEnumerable<(bool, string)> Playback()
    {
        using var eng = new NotaEngine();
        eng.SetBpm(120);
        int t = eng.AddInstrumentTrack();
        eng.AddReturnTrack();
        eng.AddReturnTrack();
        int lane = eng.AddAutomationLane(t, AutomationTarget.Send, 1, -1);
        int again = eng.AddAutomationLane(t, AutomationTarget.Send, 1, -1);
        int other = eng.AddAutomationLane(t, AutomationTarget.Send, 0, -1);
        int bad = eng.AddAutomationLane(t, AutomationTarget.Send, 4, -1);
        var info = lane >= 0 ? eng.AutomationLaneInfo(t, lane) : default;
        yield return (lane >= 0 && again == lane && other >= 0 && other != lane && bad < 0
                      && info.Target == AutomationTarget.Send && info.DeviceIndex == 1,
            $"sends: one send lane per bus, bus kept, bus 4 refused (lanes {lane}/{again}/{other}/{bad})");

        eng.SetAutomationPoints(t, lane, new[] { new AutomationPoint(0, 0f), new AutomationPoint(8, 1f) });
        eng.ClearUndoHistory();
        var buf = new float[512 * 2];
        eng.Seek(4); eng.Play();
        eng.RenderOffline(buf, 512, 48000);
        eng.StopTransport();
        float mid = eng.GetTrackSend(t, 1), a = eng.GetTrackSend(t, 0);
        yield return (Math.Abs(mid - 0.5f) < 0.02f && a == 0f && !eng.CanUndo,
            $"sends: a send lane drives its bus during playback (beat 4 → {mid:F3}, bus A {a}) with no undo step");
    }

    // ---- recording a send ---------------------------------------------------------------

    private static IEnumerable<(bool, string)> Record()
    {
        using var eng = new NotaEngine();
        eng.SetBpm(120);
        int t = eng.AddInstrumentTrack();
        eng.AddReturnTrack();
        eng.ClearUndoHistory();
        var buf = new float[512 * 2];
        eng.SetAutomationRecord(true);
        eng.Seek(0); eng.Play();
        int h = eng.BeginMixGesture(MixTarget.Send(t, 0));   // as SendControl does: undo first
        eng.BeginAutomationWrite(t, AutomationTarget.Send, 0, -1, "");
        for (int i = 0; i < 20; i++) { eng.SetTrackSend(t, 0, 0.2f + i * 0.02f); eng.RenderOffline(buf, 512, 48000); }
        eng.EndAutomationWrite(t, AutomationTarget.Send, 0, -1, "");
        eng.EndMixGesture(h);
        eng.StopTransport();
        eng.SetAutomationRecord(false);
        int lanes = eng.AutomationLaneCount(t);
        bool sendLane = lanes == 1 && eng.AutomationLaneInfo(t, 0) is { Target: AutomationTarget.Send, DeviceIndex: 0 };
        float v1 = eng.GetTrackSend(t, 0);
        eng.Undo();
        bool undone = eng.AutomationLaneCount(t) == 0 && eng.GetTrackSend(t, 0) == 0f && !eng.CanUndo;
        yield return (sendLane && undone,
            $"sends: recording a send creates its lane and undoes with the value in one step ({lanes} lane, {v1:F2})");
    }

    // ---- save / load ----------------------------------------------------------------------

    private static IEnumerable<(bool, string)> SaveLoad()
    {
        using var eng = new NotaEngine();
        int t = eng.AddInstrumentTrack();
        eng.AddReturnTrack();
        eng.AddReturnTrack();
        int lane = eng.AddAutomationLane(t, AutomationTarget.Send, 1, -1);
        eng.SetAutomationPoints(t, lane, new[] { new AutomationPoint(0, 0.25f), new AutomationPoint(4, 0.75f, 0.3f) });
        string dir = Path.Combine(Path.GetTempPath(), "nota-sends-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = ProjectService.Capture(eng, new TransportState(120, 1, false, false), new List<string>());
            ProjectService.Save(doc, dir, eng);
            using var e2 = new NotaEngine();
            ProjectService.Apply(ProjectService.Load(dir), e2, dir);
            int t2 = -1;
            for (int i = 0; i < e2.TrackCount; i++)
                if (e2.TryGetTrackInfo(i, out var ti) && ti.IsInstrument) t2 = ti.Id;
            bool ok = false;
            for (int l = 0; t2 > 0 && l < e2.AutomationLaneCount(t2); l++)
            {
                var info = e2.AutomationLaneInfo(t2, l);
                var pts = e2.GetAutomationPoints(t2, l);
                if (info.Target == AutomationTarget.Send && info.DeviceIndex == 1 && pts.Length == 2
                    && Math.Abs(pts[0].Value - 0.25f) < 1e-6f && Math.Abs(pts[1].Value - 0.75f) < 1e-6f
                    && Math.Abs(pts[1].Curve - 0.3f) < 1e-6f)
                    ok = true;
            }
            yield return (ok, "sends: save/load keeps a send lane on its bus");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    // ---- deleting a return ----------------------------------------------------------------

    private static IEnumerable<(bool, string)> ReturnDelete()
    {
        using var eng = new NotaEngine();
        int t = eng.AddInstrumentTrack();
        int r0 = eng.AddReturnTrack();
        int r1 = eng.AddReturnTrack();
        int r2 = eng.AddReturnTrack();
        eng.SetTrackSend(t, 0, 0.1f);
        eng.SetTrackSend(t, 1, 0.2f);
        eng.SetTrackSend(t, 2, 0.3f);
        foreach (int bus in new[] { 0, 1, 2 })
        {
            int l = eng.AddAutomationLane(t, AutomationTarget.Send, bus, -1);
            eng.SetAutomationPoints(t, l, new[] { new AutomationPoint(0, 0.1f * (bus + 1)) });
        }
        eng.ClearUndoHistory();

        bool removed = eng.RemoveTracks(new[] { r1 });
        var buses = SendLaneBuses(eng, t);
        bool remapped = removed && eng.TrackReturnIndex(r2) == 1 && buses.Count == 2
                        && buses.TryGetValue(0, out var v0) && Math.Abs(v0 - 0.1f) < 1e-6f
                        && buses.TryGetValue(1, out var v1) && Math.Abs(v1 - 0.3f) < 1e-6f
                        && Math.Abs(eng.GetTrackSend(t, 1) - 0.3f) < 1e-6f;
        yield return (remapped,
            $"sends: deleting return B drops its send lane and moves C's lane and level to bus B ({string.Join(", ", buses.Select(kv => $"{kv.Key}:{kv.Value:F1}"))})");

        eng.Undo();
        buses = SendLaneBuses(eng, t);
        yield return (eng.TrackReturnIndex(r0) == 0 && buses.Count == 3 && buses.TryGetValue(2, out var c) && Math.Abs(c - 0.3f) < 1e-6f,
            "sends: undo brings the return and its send lane back");
    }

    // Bus → first point value of each send lane on the track.
    private static Dictionary<int, float> SendLaneBuses(NotaEngine eng, int t)
    {
        var map = new Dictionary<int, float>();
        for (int l = 0; l < eng.AutomationLaneCount(t); l++)
        {
            var info = eng.AutomationLaneInfo(t, l);
            if (info.Target != AutomationTarget.Send) continue;
            var pts = eng.GetAutomationPoints(t, l);
            map[info.DeviceIndex] = pts.Length > 0 ? pts[0].Value : -1f;
        }
        return map;
    }
}
