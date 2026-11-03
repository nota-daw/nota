// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Sends stay live levels (specs/069-arrangement-header-sends, #143): moving a send is not an
// undo step, from the plain setter, a mix gesture or MCP set_send, and a send is not an
// automation target. Also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --sends`.

using Nota.Application;
using Nota.Application.Editing;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class SendAutomationTests
{
    public static IEnumerable<(bool ok, string label)> Run()
    {
        foreach (var r in NotUndo()) yield return r;
        foreach (var r in NotAutomation()) yield return r;
    }

    private static float Volume(NotaEngine eng, int id)
    {
        for (int i = 0; i < eng.TrackCount; i++)
            if (eng.TryGetTrackInfo(i, out var ti) && ti.Id == id) return ti.Volume;
        return -1f;
    }

    // ---- not an undo step ----------------------------------------------------------------

    private static IEnumerable<(bool, string)> NotUndo()
    {
        using var eng = new NotaEngine();
        int t = eng.AddInstrumentTrack();
        eng.AddReturnTrack();
        eng.AddReturnTrack();
        eng.ClearUndoHistory();

        eng.SetTrackSend(t, 1, 0.4f);
        yield return (Math.Abs(eng.GetTrackSend(t, 1) - 0.4f) < 1e-6f && !eng.CanUndo,
            "sends: SetTrackSend changes the level and adds no undo step");

        // A volume step, then a send, then undo: the volume returns and the send stays.
        float vol0 = Volume(eng, t);
        MixEdit.Step(eng, () => eng.SetTrackVolume(t, 0.5f), MixTarget.Volume(t));
        eng.SetTrackSend(t, 1, 0.7f);
        bool undone = eng.CanUndo && eng.Undo()
                      && Math.Abs(Volume(eng, t) - vol0) < 1e-4f
                      && Math.Abs(eng.GetTrackSend(t, 1) - 0.7f) < 1e-6f
                      && !eng.CanUndo;
        yield return (undone, "sends: undo after a send restores the volume and leaves the send");

        eng.ClearUndoHistory();
        var g = new MixGesture(eng, MixTarget.Send(t, 0));
        g.Begin();
        g.Change(0.55f, v => eng.SetTrackSend(t, 0, v));
        g.End();
        yield return (Math.Abs(eng.GetTrackSend(t, 0) - 0.55f) < 1e-6f && !eng.CanUndo && !g.IsOpen,
            "sends: a send gesture records nothing");

        MixEdit.Step(eng, () => eng.SetTrackSend(t, 0, 0.2f), MixTarget.Send(t, 0));
        yield return (Math.Abs(eng.GetTrackSend(t, 0) - 0.2f) < 1e-6f && !eng.CanUndo,
            "sends: MixEdit.Step on a send adds no step");

        var tools = new Nota.Mcp.Tools.TrackTools(eng, new SyncDispatch(), new NoRefresh());
        tools.SetSend(t, 0, 0.75f).GetAwaiter().GetResult();
        yield return (Math.Abs(eng.GetTrackSend(t, 0) - 0.75f) < 1e-6f && !eng.CanUndo,
            "sends: an MCP set_send call is not an undo step");
    }

    // ---- not an automation target -------------------------------------------------------

    private static IEnumerable<(bool, string)> NotAutomation()
    {
        using var eng = new NotaEngine();
        int t = eng.AddInstrumentTrack();
        eng.AddReturnTrack();
        int lane = eng.AddAutomationLane(t, AutomationTarget.Send, 0, -1);
        int vol = eng.AddAutomationLane(t, AutomationTarget.Volume, -1, -1);
        yield return (lane < 0 && vol >= 0 && eng.AutomationLaneCount(t) == 1
                      && eng.AutomationLaneInfo(t, vol).Target == AutomationTarget.Volume,
            $"sends: a send lane is refused and a volume lane still opens ({lane}/{vol})");

        eng.SetTrackSend(t, 0, 0.3f);
        eng.SetAutomationRecord(true);
        eng.BeginAutomationWrite(t, AutomationTarget.Send, 0, -1, "");
        eng.SetTrackSend(t, 0, 0.8f);
        eng.EndAutomationWrite(t, AutomationTarget.Send, 0, -1, "");
        eng.SetAutomationRecord(false);
        bool noLane = eng.AutomationLaneCount(t) == 1
                      && eng.AutomationLaneInfo(t, 0).Target == AutomationTarget.Volume
                      && Math.Abs(eng.GetTrackSend(t, 0) - 0.8f) < 1e-6f;
        yield return (noLane, "sends: recording a send writes no lane");

        var tools = new Nota.Mcp.Tools.AutomationTools(eng, new SyncDispatch(), new NoRefresh());
        int mcp = tools.AddAutomationLane(t, "send", 0, -1).GetAwaiter().GetResult();
        yield return (mcp < 0 && eng.AutomationLaneCount(t) == 1,
            "sends: MCP add_automation_lane \"send\" returns -1");
    }
}
