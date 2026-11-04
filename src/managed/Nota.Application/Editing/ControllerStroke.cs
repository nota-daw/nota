// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A drawn controller stroke replaces only the stretch it covers (specs/063). The curve on
// either side keeps the value it had: the left piece is the same power curve over a shorter
// span (that shaping is a fraction of the segment, so it needs no new exponent), and the
// right piece is refit because a new segment would otherwise start linear and straighten
// everything after the stroke.

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

    private const double MinPiece = 1.0 / 128.0;
    private const int MaxSplits = 8;

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
        foreach (var q in orig)
            if (q.Beat < a - Junction) outp.Add(q);
        if (outp.Count > 0)
            outp.Add(new AutomationPoint(a - Junction, ValueAt(orig, a - Junction)));
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
                EmitRemainder(outp, left, right, edge, right.Beat, 0);
        }
        for (int i = next; i < orig.Count; i++)
            outp.Add(orig[i]);
        return CollapseBeats(outp);
    }

    // The tail of the segment (left → right) from `from` up to `end` (the next stored point,
    // not emitted here). One power curve when it stays within Tolerance; otherwise the piece
    // is halved and each half is shaped on its own.
    private static void EmitRemainder(
        List<AutomationPoint> outp, AutomationPoint left, AutomationPoint right, double from, double end, int depth)
    {
        float value = ValueOn(left, right, from);
        if (Math.Abs(left.Curve) < 1e-6f)
        {
            outp.Add(new AutomationPoint(from, value, 0f));
            return;
        }
        float curve = FitCurve(left, right, from, end, out float err);
        if (err <= Tolerance || depth >= MaxSplits || end - from <= MinPiece)
        {
            outp.Add(new AutomationPoint(from, value, curve));
            return;
        }
        double mid = (from + end) / 2;
        EmitRemainder(outp, left, right, from, mid, depth + 1);
        EmitRemainder(outp, left, right, mid, end, depth + 1);
    }

    private static float FitCurve(
        AutomationPoint left, AutomationPoint right, double from, double end, out float err)
    {
        float bestC = 0f, bestE = float.MaxValue;
        for (int i = 0; i <= 40; i++)
            Consider(-1f + i / 20f);
        float around = bestC;
        for (int i = -10; i <= 10; i++)
            Consider(Math.Clamp(around + i * 0.01f, -1f, 1f));
        err = bestE;
        return bestC;

        void Consider(float c)
        {
            float e = MaxError(left, right, from, end, c);
            if (e < bestE) { bestE = e; bestC = c; }
        }
    }

    private static float MaxError(
        AutomationPoint left, AutomationPoint right, double from, double end, float curve)
    {
        double span = end - from;
        if (span <= 0) return 0;
        float v0 = ValueOn(left, right, from), v1 = ValueOn(left, right, end);
        float err = 0;
        for (int k = 1; k < 16; k++)
        {
            double beat = from + span * k / 16.0;
            float target = ValueOn(left, right, beat);
            double t = (beat - from) / span;
            float got = (float)(v0 + (v1 - v0) * Shape(t, curve));
            float d = Math.Abs(target - got);
            if (d > err) err = d;
        }
        return err;
    }

    private static float ValueOn(AutomationPoint left, AutomationPoint right, double beat)
    {
        double span = right.Beat - left.Beat;
        if (span <= 0) return right.Value;
        double t = Math.Clamp((beat - left.Beat) / span, 0, 1);
        return (float)(left.Value + (right.Value - left.Value) * Shape(t, left.Curve));
    }

    private static double Shape(double t, float curve)
        => curve == 0f ? t : Math.Pow(t, Math.Pow(2.0, -curve * 4.0));

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
