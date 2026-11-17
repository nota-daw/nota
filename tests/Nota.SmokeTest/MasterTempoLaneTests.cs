// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// specs/099 US1: tempo drawn on the master Tempo lane drives playback — a ritardando drawn as
// a curve and a jump drawn as two points on one beat — and the MCP tools reach that lane.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal static class MasterTempoLaneTests
{
    private const int Sr = 48000;

    // Sample index of each metronome click: the first loud sample after a quiet stretch.
    private static List<int> Clicks(float[] stereo)
    {
        var at = new List<int>();
        int lastLoud = -Sr;
        for (int i = 0; i < stereo.Length / 2; i++)
        {
            if (Math.Abs(stereo[i * 2]) <= 1e-4) continue;
            if (i - lastLoud > Sr / 50) at.Add(i);
            lastLoud = i;
        }
        return at;
    }

    public static IEnumerable<(bool, string)> Run()
    {
        using var e = new NotaEngine();
        e.SetBpm(120); e.RenderOffline(new float[2], 1, Sr);
        int lane = e.AddAutomationLane(e.MasterTrackId, AutomationTarget.Tempo, -1, -1);
        // 120 easing down to 90 over four bars, then a jump to 140 at bar 5.
        var drawn = new[]
        {
            new AutomationPoint(0, 120, -0.6f), new AutomationPoint(16, 90, 0),
            new AutomationPoint(16, 140, 0),
        };
        e.SetAutomationPoints(e.MasterTrackId, lane, drawn);
        yield return (e.IsTempoAutomated && Math.Abs(e.TempoAt(15.999) - 90) < .01 && Math.Abs(e.TempoAt(16.001) - 140) < 1e-9,
            "master tempo: a curve ramps and two points on one beat jump");

        e.SetMetronome(true); e.Seek(0); e.Play();
        var sound = new float[Sr * 14 * 2];
        e.RenderOffline(sound, Sr * 14, Sr);
        var clicks = Clicks(sound);
        // Measured from the first click, so the detector's fixed lag on a click's attack cancels.
        int beats = 0; double worst = 0;
        for (int b = 0; b < 24 && b < clicks.Count; b++, beats++)
            worst = Math.Max(worst, Math.Abs(clicks[b] - clicks[0] - e.BeatToSeconds(b) * Sr));
        yield return (beats >= 20 && worst <= 1.0,
            $"master tempo: every click lands where the drawn tempo puts its beat (worst {worst:0.00} samples over {beats} beats)");
        e.StopTransport(); e.SetMetronome(false);

        e.SetBpm(100); e.RenderOffline(new float[2], 1, Sr);
        yield return (Math.Abs(e.TempoAt(0) - 120) < 1e-9, "master tempo: the transport tempo is refused while the lane leads");

        // MCP: the overview names the master, `tempo` lanes are reachable, set_tempo explains itself.
        var disp = new SyncDispatch(); var refresh = new NoRefresh();
        var project = new Nota.Mcp.Tools.ProjectTools(e, disp, refresh);
        var automation = new Nota.Mcp.Tools.AutomationTools(e, disp, refresh);
        var transport = new Nota.Mcp.Tools.TransportTools(e, disp, refresh);
        var ov = project.GetOverview().Result;
        var lanes = automation.ListAutomation(ov.MasterTrackId).Result;
        yield return (ov.TempoAutomated && ov.MasterTrackId == e.MasterTrackId && lanes.Any(l => l.Target == "tempo"),
            "master tempo: MCP overview names the master and lists its tempo lane");
        bool refused = false;
        try { transport.SetTempo(150).Wait(); }
        catch (Exception ex)   // the test dispatcher runs inline: the error comes bare or wrapped in the task
        {
            var inner = ex is AggregateException agg ? agg.InnerException : ex;
            refused = inner is InvalidOperationException io && io.Message.Contains("Tempo lane");
        }
        yield return (refused, "master tempo: MCP set_tempo explains that the tempo is automated");
        automation.SetAutomationPoints(ov.MasterTrackId, lanes.First(l => l.Target == "tempo").Index,
            new[] { new Nota.Mcp.Tools.AutomationTools.Point(0, 100, 0) }).Wait();
        yield return (Math.Abs(e.TempoAt(8) - 100) < 1e-9, "master tempo: MCP edits of the tempo lane change the tempo");
    }
}
