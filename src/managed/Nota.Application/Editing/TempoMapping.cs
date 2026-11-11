// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application.Editing;

/// <summary>Plans one manual mapping edit against the original, authoritative map.</summary>
public static class TempoMapping
{
    public static TempoPoint[]? MapBar(IAudioEngine engine, IReadOnlyList<TempoPoint> original,
        double barBeat, double targetSeconds, bool insertMarker)
    {
        if (!double.IsFinite(barBeat) || !double.IsFinite(targetSeconds) || barBeat <= 0) return null;
        var points = original.Count == 0 ? new List<TempoPoint> { new(0, engine.TempoAt(0)) } : original.ToList();
        int anchor = points.FindLastIndex(p => p.Beat < barBeat - 1e-6);
        if (anchor < 0) return null;
        var a = points[anchor];
        double duration = targetSeconds - engine.BeatToSeconds(a.Beat);
        if (duration <= 0) return null;
        double bpm = 60 * (barBeat - a.Beat) / duration;
        if (!double.IsFinite(bpm) || bpm < 1 || bpm > 1000) return null;
        // The incoming endpoint belongs to the already mapped phrase; adjusting
        // this anchor's outgoing tempo must not change that phrase's ramp.
        points[anchor] = a with { Bpm = bpm, Shape = TempoShape.Step,
            IncomingBpm = a.IncomingBpm > 0 ? a.IncomingBpm : a.Bpm };
        if (insertMarker && !points.Any(p => Math.Abs(p.Beat - barBeat) < 1e-6))
            points.Add(new TempoPoint(barBeat, engine.TempoAt(barBeat)));
        return points.OrderBy(p => p.Beat).ToArray();
    }

    public static TempoPoint[] DeleteSelected(IReadOnlyList<TempoPoint> points, IReadOnlySet<int> selected)
        => points.Where((p, i) => i == 0 || !selected.Contains(i)).ToArray();
}
