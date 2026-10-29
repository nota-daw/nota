// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// An opt-in timing probe for the UI thread (specs/053 quickstart § 4): with
// NOTA_UI_PROBE=aurora, Nota Aurora's table windows time each paint, the card times each live
// tick, and the device panel times building an instrument card up to its first paint. Every
// ten seconds the p50 / p99 of each go to stderr. Off (the default) each hook is one static
// bool test; nothing is allocated or timed.

using System;
using System.Diagnostics;

namespace Nota.App;

internal static class UiProbe
{
    public static readonly bool On =
        string.Equals(Environment.GetEnvironmentVariable("NOTA_UI_PROBE"), "aurora", StringComparison.OrdinalIgnoreCase);

    private const int Cap = 8192;
    private static readonly long ReportEvery = Stopwatch.Frequency * 10;

    private sealed class Ring
    {
        private readonly long[] _v = new long[Cap];
        private int _n;
        public long Count;
        public void Add(long ticks) { _v[_n++ % Cap] = ticks; Count++; }
        public (double P50, double P99, int N) Take()
        {
            int n = Math.Min(_n, Cap);
            if (n == 0) return (0, 0, 0);
            var s = new long[n];
            Array.Copy(_v, s, n);
            Array.Sort(s);
            _n = 0;
            return (Ms(s[n / 2]), Ms(s[Math.Min(n - 1, (int)(n * 0.99))]), n);
        }
    }

    private static readonly Ring Paints = new(), Ticks = new(), Builds = new();
    private static long _lastReport = Stopwatch.GetTimestamp();
    private static long _buildStart;

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>A timestamp to hand back to an End call, or 0 when the probe is off.</summary>
    public static long Start() => On ? Stopwatch.GetTimestamp() : 0;

    public static void EndPaint(long t0) { if (On && t0 != 0) { Paints.Add(Stopwatch.GetTimestamp() - t0); MaybeReport(); } }
    public static void EndTick(long t0) { if (On && t0 != 0) { Ticks.Add(Stopwatch.GetTimestamp() - t0); MaybeReport(); } }

    /// <summary>A card build begins; the next table-window paint ends it.</summary>
    public static void BuildBegin() { if (On) _buildStart = Stopwatch.GetTimestamp(); }
    public static void FirstPaint()
    {
        if (!On || _buildStart == 0) return;
        Builds.Add(Stopwatch.GetTimestamp() - _buildStart);
        _buildStart = 0;
    }

    /// <summary>Paints counted since the process started (a hidden panel must add none).</summary>
    public static long PaintCount => Paints.Count;

    private static void MaybeReport()
    {
        long now = Stopwatch.GetTimestamp();
        if (now - _lastReport < ReportEvery) return;
        _lastReport = now;
        Console.Error.WriteLine(Report());
    }

    /// <summary>The figures since the last report, as one line (and resets them).</summary>
    public static string Report()
    {
        var p = Paints.Take(); var t = Ticks.Take(); var b = Builds.Take();
        return $"ui-probe aurora: paint p50 {p.P50:0.000} ms p99 {p.P99:0.000} ms (n {p.N}) · "
             + $"tick p50 {t.P50:0.000} ms p99 {t.P99:0.000} ms (n {t.N}) · "
             + $"card build→first paint p50 {b.P50:0.0} ms (n {b.N})";
    }
}
