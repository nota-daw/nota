// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Test shorthand for the master Tempo automation lane (specs/099). The tempo tests from
// specs/086 describe maps as stepped / linear / curved points; this writes them as the lane
// the engine now derives its tempo map from: a step holds its tempo to the next point and
// jumps there (two points on one beat), a ramp is a lane segment with that curve.

using Nota.Application;
using Nota.Infrastructure;

namespace Nota.SmokeTest;

internal enum TempoShape { Step, Linear, Curve }

/// <summary>A tempo point as the tests write maps: Shape is the segment to the next point;
/// Curve (−1..1) bends a Curve segment like any automation lane.</summary>
internal readonly record struct TempoPoint(double Beat, double Bpm, TempoShape Shape = TempoShape.Step, float Curve = 0);

internal static class TempoLane
{
    /// <summary>The master Tempo lane's index (added when missing).</summary>
    public static int Lane(NotaEngine e) => e.AddAutomationLane(e.MasterTrackId, AutomationTarget.Tempo, -1, -1);

    /// <summary>The lane points a map of tempo points becomes.</summary>
    public static AutomationPoint[] ToLane(IReadOnlyList<TempoPoint> map)
    {
        var lane = new List<AutomationPoint>();
        for (int i = 0; i < map.Count; i++)
        {
            var p = map[i];
            if (i > 0 && map[i - 1].Shape == TempoShape.Step && map[i - 1].Bpm != p.Bpm)
                lane.Add(new AutomationPoint(p.Beat, (float)map[i - 1].Bpm, 0));   // held up to here, then the jump
            float curve = p.Shape == TempoShape.Curve ? p.Curve : 0;
            lane.Add(new AutomationPoint(p.Beat, (float)p.Bpm, curve));
        }
        return lane.ToArray();
    }

    /// <summary>Writes <paramref name="map"/> as the master Tempo lane (one undo step); an empty
    /// map clears the lane, so the transport tempo leads again.</summary>
    public static void SetTempoPoints(this NotaEngine e, IReadOnlyList<TempoPoint> map)
        => e.SetAutomationPoints(e.MasterTrackId, Lane(e), ToLane(map));

    /// <summary>The master Tempo lane as it stands.</summary>
    public static AutomationPoint[] Points(NotaEngine e) => e.GetAutomationPoints(e.MasterTrackId, Lane(e));

    /// <summary>Whether the lane holds exactly what <paramref name="map"/> writes.</summary>
    public static bool Holds(NotaEngine e, IReadOnlyList<TempoPoint> map)
    {
        var have = Points(e); var want = ToLane(map);
        return have.Length == want.Length && have.Zip(want).All(x =>
            x.First.Beat == x.Second.Beat && x.First.Value == x.Second.Value && x.First.Curve == x.Second.Curve);
    }
}
