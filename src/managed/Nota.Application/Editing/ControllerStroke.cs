// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A drawn controller stroke replaces only the stretch it covers (specs/063). The curve on
// either side keeps the value it had: a piece of a shaped segment is the same shape with its
// curve scaled to the piece's share of the segment (AutomationCurve, #591), so the left piece
// and the right remainder are both exact — without the remainder a new segment would start
// linear and straighten everything after the stroke.

using System;
using System.Collections.Generic;

namespace Nota.Application.Editing;

public static class ControllerStroke
{
    /// <summary>Beats between a drawn stretch and the curve it meets. Matches native
    /// <c>kCtlJunction</c>.</summary>
    public const double Junction = 1.0 / 1024.0;

    /// <summary>How far playback outside the stroke may move: one 7-bit CC step
    /// (<c>ctlThinTolerance</c>).</summary>
    public const float Tolerance = 0.008f;


    /// <summary>The engine's reading of a continuous controller curve
    /// (<c>ControllerCurve::valueAt</c>): held outside the first and last point.</summary>
    public static float ValueAt(IReadOnlyList<AutomationPoint> p, double beat)
    {
        if (p.Count == 0) return 0;
        if (beat <= p[0].Beat) return p[0].Value;
        if (beat >= p[^1].Beat) return p[^1].Value;
        int i = 1;
        while (p[i].Beat <= beat) i++;
        var a = p[i - 1];
        var b = p[i];
        double span = b.Beat - a.Beat;
        if (span <= 0) return b.Value;
        return (float)(a.Value + (b.Value - a.Value) * Shape((beat - a.Beat) / span, a.Curve));
    }

    /// <summary>A switch lane (sustain, sostenuto…) as the engine will play it: the engine
    /// holds the first point's value back to the clip start, while the lane shows the pedal up
    /// until its first block. When the first point is down and later than the start, an up
    /// point at beat 0 makes playback match.</summary>
    public static List<AutomationPoint> SwitchLeadIn(List<AutomationPoint> p)
    {
        if (p.Count > 0 && p[0].Value >= 0.5f && p[0].Beat > 1e-9)
            p.Insert(0, new AutomationPoint(0, 0f));
        return p;
    }

    /// <summary>The curve after drawing <paramref name="samples"/> over <paramref name="orig"/>.
    /// Samples are in clip-local beats. With nothing after the stroke, the drawn value holds.</summary>
    public static List<AutomationPoint> Apply(
        IReadOnlyList<AutomationPoint> orig, IReadOnlyList<(double Beat, float Value)> samples)
    {
        if (samples.Count == 0) return Copy(orig);
        orig = SortedPoints(orig);
        var ordered = Sorted(samples);
        double a = ordered[0].Beat, b = ordered[^1].Beat;
        var outp = new List<AutomationPoint>(orig.Count + ordered.Count + 4);
        int kept = 0;
        foreach (var q in orig)
            if (q.Beat < a - Junction) { outp.Add(q); kept++; }
        if (outp.Count > 0)
        {
            // The last point's segment now ends at the junction: shorten its curve so the
            // stretch before the stroke keeps its values (#591).
            if (kept < orig.Count)
            {
                var last = outp[^1];
                double span = orig[kept].Beat - last.Beat;
                if (span > 0) last.Curve = AutomationCurve.Sub(last.Curve, (a - Junction - last.Beat) / span);
                outp[^1] = last;
            }
            outp.Add(new AutomationPoint(a - Junction, ValueAt(orig, a - Junction)));
        }
        foreach (var (sb, sv) in ordered)
            outp.Add(new AutomationPoint(sb, sv));

        double edge = b + Junction;
        int next = 0;
        while (next < orig.Count && orig[next].Beat <= edge) next++;
        if (next == 0 && orig.Count > 0)
        {
            // The stroke ends before the first point. The old value is held there, so pin it
            // just after the stroke. Without that pin the last drawn value ramps up to the
            // first point and the untouched stretch moves.
            outp.Add(new AutomationPoint(edge, ValueAt(orig, edge)));
        }
        else if (next > 0 && next < orig.Count)
        {
            var left = orig[next - 1];
            var right = orig[next];
            if (edge > left.Beat + 1e-9 && edge < right.Beat)
                EmitRemainder(outp, left, right, edge);
        }
        for (int i = next; i < orig.Count; i++)
            outp.Add(orig[i]);
        return CollapseBeats(outp);
    }

    // The tail of the segment (left → right) from `from` on: the same shape with the curve
    // scaled to the part that is left, which is exact (#591).
    private static void EmitRemainder(List<AutomationPoint> outp, AutomationPoint left, AutomationPoint right, double from)
    {
        double span = right.Beat - left.Beat;
        float curve = span > 0 ? AutomationCurve.Sub(left.Curve, (right.Beat - from) / span) : 0f;
        outp.Add(new AutomationPoint(from, ValueOn(left, right, from), curve));
    }

    private static float ValueOn(AutomationPoint left, AutomationPoint right, double beat)
    {
        double span = right.Beat - left.Beat;
        if (span <= 0) return right.Value;
        double t = Math.Clamp((beat - left.Beat) / span, 0, 1);
        return (float)(left.Value + (right.Value - left.Value) * Shape(t, left.Curve));
    }

    private static double Shape(double t, float curve)
        => AutomationCurve.Shape(t, curve);

    private static IReadOnlyList<AutomationPoint> SortedPoints(IReadOnlyList<AutomationPoint> orig)
    {
        for (int i = 1; i < orig.Count; i++)
            if (orig[i].Beat < orig[i - 1].Beat)
            {
                var copy = new List<AutomationPoint>(orig);
                copy.Sort((a, b) => a.Beat.CompareTo(b.Beat));
                return copy;
            }
        return orig;
    }

    private static List<(double Beat, float Value)> Sorted(IReadOnlyList<(double Beat, float Value)> samples)
    {
        for (int i = 1; i < samples.Count; i++)
            if (samples[i].Beat < samples[i - 1].Beat)
            {
                var copy = new List<(double Beat, float Value)>(samples);
                copy.Sort((a, b) => a.Beat.CompareTo(b.Beat));
                return copy;
            }
        if (samples is List<(double Beat, float Value)> list) return list;
        return new List<(double Beat, float Value)>(samples);
    }

    private static List<AutomationPoint> Copy(IReadOnlyList<AutomationPoint> orig)
    {
        var copy = new List<AutomationPoint>(orig.Count);
        foreach (var p in orig) copy.Add(p);
        return copy;
    }

    private static List<AutomationPoint> CollapseBeats(List<AutomationPoint> pts)
    {
        var outp = new List<AutomationPoint>(pts.Count);
        foreach (var p in pts)
        {
            if (outp.Count > 0 && Math.Abs(outp[^1].Beat - p.Beat) < 1e-9) outp[^1] = p;
            else outp.Add(p);
        }
        return outp;
    }
}
