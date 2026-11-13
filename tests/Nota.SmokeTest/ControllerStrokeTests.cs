// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Drawing a controller stroke replaces only that stretch (specs/063). Playback on either
// side stays put: the shortened left piece and the right remainder are the same shape with
// the curve scaled to their share of the segment (#591), so they do not fall back to a
// straight line. Also checks the segment shape itself (no jump, mirrored ease-in/out). Also runnable alone:
// `dotnet run --project tests/Nota.SmokeTest -- --controller-stroke`.

using Nota.Application.Editing;

namespace Nota.SmokeTest;

internal static class ControllerStrokeTests
{
    // The engine formula, written out here so the check is not the implementation calling itself.
    private static float Oracle(IReadOnlyList<AutomationPoint> p, double beat)
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
        double t = (beat - a.Beat) / span;
        double k = -a.Curve * 8.0;   // native AutomationLane::shape, K = 8
        double shaped = Math.Abs(k) < 1e-6 ? t : (Math.Exp(k * t) - 1) / (Math.Exp(k) - 1);
        return (float)(a.Value + (b.Value - a.Value) * shaped);
    }

    private static float Worst(IReadOnlyList<AutomationPoint> orig, IReadOnlyList<AutomationPoint> result, double from, double to)
    {
        float worst = 0;
        for (int k = 0; k <= 40; k++)
        {
            double beat = from + (to - from) * k / 40.0;
            float d = Math.Abs(Oracle(orig, beat) - Oracle(result, beat));
            if (d > worst) worst = d;
        }
        return worst;
    }

    public static IEnumerable<(bool Ok, string Label)> Run()
    {
        var shaped = new AutomationPoint[] { new(0, 0f, -1f), new(4, 1f, 0f) };
        var stroke = new (double Beat, float Value)[] { (1, 0.4f), (2, 0.6f) };
        var merged = ControllerStroke.Apply(shaped, stroke);

        yield return (merged.Count > 0 && merged[0].Beat == 0 && merged[0].Curve < 0f && merged[0].Curve > -1f,
            "the point before the stroke keeps a shortened ease-in");
        yield return (Worst(shaped, merged, 0, 1 - 0.05) < 1e-4f,
            "shortening the left of a shaped segment does not move its values");

        float right = Worst(shaped, merged, 2 + 0.01, 4);
        yield return (right < 1e-4f,
            $"the curve after the stroke keeps its values (off by {right:0.0000})");

        bool straightTail = true;
        foreach (var p in merged)
            if (p.Beat > 2 && p.Beat < 4 && Math.Abs(p.Curve) > 1e-4f) straightTail = false;
        yield return (!straightTail, "the right-hand boundary keeps a curve instead of going straight");

        var easeOut = new AutomationPoint[] { new(0, 0f, 1f), new(4, 1f, 0f) };
        var eased = ControllerStroke.Apply(easeOut, stroke);
        float outErr = Math.Max(Worst(easeOut, eased, 0, 0.95), Worst(easeOut, eased, 2.01, 4));
        yield return (outErr < 1e-4f,
            $"an ease-out keeps both sides (off by {outErr:0.0000})");

        // The segment shape (#591): no jump at either end, ease-in mirrors ease-out, the bend
        // handle round-trips, and the shared helper is the engine formula.
        yield return (AutomationCurve.Shape(0.01, 1f) < 0.1 && AutomationCurve.Shape(0.99, -1f) > 0.9,
            "a full ease-out / ease-in does not jump at its steep end");
        yield return (Math.Abs(AutomationCurve.Shape(0.3, 1f) - (1 - AutomationCurve.Shape(0.7, -1f))) < 1e-9,
            "ease-in mirrors ease-out");
        bool trips = true, matches = true;
        foreach (float c in new[] { -1f, -0.6f, -0.1f, 0.25f, 0.8f })
        {
            if (Math.Abs(AutomationCurve.FromMidpoint(AutomationCurve.Shape(0.5, c)) - c) > 1e-4f) trips = false;
            var seg = new AutomationPoint[] { new(0, 0f, c), new(1, 1f, 0f) };
            for (int i = 0; i <= 10; i++)
                if (Math.Abs(ControllerStroke.ValueAt(seg, i / 10.0) - Oracle(seg, i / 10.0)) > 1e-5f) matches = false;
        }
        yield return (trips, "the bend handle's curve round-trips through the midpoint");
        yield return (matches, "the shared shape matches the engine formula");

        var linear = new AutomationPoint[] { new(0, 0f, 0f), new(4, 1f, 0f) };
        var lin = ControllerStroke.Apply(linear, stroke);
        yield return (Worst(linear, lin, 0, 0.95) < 1e-4f && Worst(linear, lin, 2.01, 4) < 1e-4f,
            "a straight segment stays straight outside the stroke");
        bool extraCurve = false;
        foreach (var p in lin)
            if (p.Beat > 2 && Math.Abs(p.Curve) > 1e-4f) extraCurve = true;
        yield return (!extraCurve, "a straight tail does not grow a curve");

        var only = ControllerStroke.Apply(Array.Empty<AutomationPoint>(), stroke);
        yield return (only.Count == 2 && only[0].Value == 0.4f && only[1].Value == 0.6f,
            "a stroke on an empty lane is just the stroke");

        var held = ControllerStroke.Apply(new AutomationPoint[] { new(0, 0.25f, 0.5f) }, stroke);
        yield return (held[^1].Beat <= 2 + 1e-6,
            "with nothing after the stroke the drawn value holds");

        // A stroke that ends before the first point must pin the held value. Otherwise the
        // last drawn value ramps across the untouched stretch up to that point.
        var ahead = new AutomationPoint[] { new(4, 1f, 0f) };
        var early = ControllerStroke.Apply(ahead, new (double Beat, float Value)[] { (1, 0.2f), (2, 0f) });
        float at3 = Oracle(early, 3);
        yield return (Math.Abs(at3 - 1f) < 1e-3f,
            $"drawing before the first point leaves the later value (read {at3:0.00} at beat 3)");

        // A sustain block drawn over beats 2–3 of an empty lane must not hold the pedal from
        // beat 0: the engine reads the first point's value back to the clip start.
        var block = ControllerStroke.SwitchLeadIn(new List<AutomationPoint> { new(2, 1f), new(3, 0f) });
        // A switch steps: it holds the earlier point's value (ControllerCurve::valueAt).
        static float Step(IReadOnlyList<AutomationPoint> p, double beat)
        {
            if (p.Count == 0) return 0;
            float v = p[0].Value;
            foreach (var q in p) if (q.Beat <= beat) v = q.Value;
            return v;
        }
        yield return (Step(block, 0) < 0.5f && Step(block, 1.9) < 0.5f && Step(block, 2.5) >= 0.5f && Step(block, 3.5) < 0.5f,
            "a switch block plays only where it is drawn");
        var atStart = ControllerStroke.SwitchLeadIn(new List<AutomationPoint> { new(0, 1f), new(3, 0f) });
        yield return (atStart.Count == 2, "a block from the clip start needs no lead-in point");
    }
}
