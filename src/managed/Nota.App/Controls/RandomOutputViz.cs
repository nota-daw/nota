// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Random visualisers (almanac mockups 1a / 1b):
//   • RandomModel — the device's roll, ported exactly from MidiRandom.h (hash, draw, the per-bar
//     key), applied to a one-bar demo phrase of eight eighth notes. So the picture is what the
//     device plays: with the seed Locked it is the very roll you hear, every bar.
//   • RandomOutputViz — the OUTPUT window: each input note as a dashed outline, the output as a
//     brass fill (brightness = velocity, the Timing delay as a shift right), a thin link when the
//     pitch moved, × on a skipped note, octave lines (C) and a playhead; the note under the
//     playhead lights Brass Light. The full view adds the C labels in a left gutter.
//   • RandomHistViz — the DICE histogram: 1500 draws from the same generator for the current
//     distribution and seed, in 17 bins across −max … centre … +max.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal static class RandomModel
{
    // Param indices — mirror MidiRandom.h.
    internal const int PChance = 0, PNoteRange = 1, PVelAmt = 2, PTimeAmt = 3, PSkip = 4, POctAmt = 5,
                       PDist = 6, PRate = 7, PStayInScale = 8, PLocked = 9, PSeed = 10, PView = 11, PLockBar = 12;
    internal const double BarBeats = 4.0, MaxDelayMs = 100.0;

    /// <summary>The demo phrase: C4 E4 G4 C5 B4 G4 E4 D4 on the eighths, velocity 96.</summary>
    internal static readonly (int Pitch, double T, double Len, int Vel)[] Input =
    {
        (60, 0.0, 0.4, 96), (64, 0.5, 0.4, 96), (67, 1.0, 0.4, 96), (72, 1.5, 0.4, 96),
        (71, 2.0, 0.4, 96), (67, 2.5, 0.4, 96), (64, 3.0, 0.4, 96), (62, 3.5, 0.4, 96),
    };

    /// <summary>One note as the device plays it: output pitch / start (beats, in the bar) /
    /// velocity (1..127), whether Chance hit it and whether it was skipped.</summary>
    internal readonly record struct Note(int InPitch, double InT, double Len, int InVel, int OutPitch, double OutT, int OutVel, bool Hit, bool Skip)
    {
        public bool Changed => Hit && !Skip && (OutPitch != InPitch || OutVel != InVel || OutT - InT > 1e-4);
    }

    // ---- exact ports of MidiRandom.h ----------------------------------------------------------
    internal static uint Hash(uint a, uint b)
    {
        unchecked
        {
            uint h = a * 2654435761u + b * 40503u + 0x9e3779b9u;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; return h;
        }
    }
    internal static float U01(ref uint s) { unchecked { s = s * 1664525u + 1013904223u; } return (s >> 8) / 16777216.0f; }
    internal static float Draw(int dist, ref uint s, ref float walk)
    {
        if (dist == 1) return U01(ref s) * 2f - 1f;
        if (dist == 2)
        {
            walk += (U01(ref s) * 2f - 1f) * 0.45f;
            if (walk > 1f) walk = 2f - walk;
            if (walk < -1f) walk = -2f - walk;
            return walk;
        }
        float a = Math.Max(U01(ref s), 1e-9f), b = U01(ref s);
        float g = MathF.Sqrt(-2f * MathF.Log(a)) * MathF.Cos(6.2831853f * b);
        return Math.Clamp(g / 2.6f, -1f, 1f);
    }
    internal static int SnapToCMajor(int p)
    {
        int pc = ((p % 12) + 12) % 12;
        return pc is 1 or 3 or 6 or 8 or 10 ? p - 1 : p;
    }
    internal static uint RollKey(uint seed, uint barKey) { unchecked { return Hash(seed * 7919u + 17u, barKey * 104729u + 3u); } }
    private static int LRound(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    internal static int Dist(Func<int, float> g) => Math.Clamp(LRound(g(PDist)), 0, 2);
    internal static int Seed(Func<int, float> g) => LRound(Math.Max(0, g(PSeed)));
    internal static bool Locked(Func<int, float> g) => g(PLocked) >= 0.5f;

    /// <summary>The demo bar as rolled for <paramref name="bar"/> (Locked: the held Lock Bar); the
    /// Timing delay is converted to beats at <paramref name="bpm"/>.</summary>
    internal static Note[] Compute(Func<int, float> g, int bar, double bpm)
    {
        float chance = Math.Clamp(g(PChance), 0f, 1f), velAmt = Math.Clamp(g(PVelAmt), 0f, 1f), timeAmt = Math.Clamp(g(PTimeAmt), 0f, 1f);
        float skipAmt = Math.Clamp(g(PSkip), 0f, 1f), octAmt = Math.Clamp(g(POctAmt), 0f, 1f);
        int nr = Math.Clamp(LRound(g(PNoteRange)), 0, 12), dist = Dist(g);
        bool perBar = g(PRate) >= 0.5f, stay = g(PStayInScale) >= 0.5f;
        uint barKey = Locked(g) ? (uint)LRound(Math.Max(0, g(PLockBar))) : (uint)Math.Max(0, bar);
        uint roll = RollKey((uint)Seed(g), barKey);

        var walk = new float[4];
        var barD = new float[4];
        uint bs = Hash(roll, 0xB0B5EEDu); var w0 = new float[4];
        for (int d = 0; d < 4; d++) barD[d] = Draw(dist, ref bs, ref w0[d]);

        var notes = new Note[Input.Length];
        for (int i = 0; i < Input.Length; i++)
        {
            var n = Input[i];
            int tick = LRound(n.T * 480.0);
            uint nk; unchecked { nk = Hash(roll, (uint)tick * 128u + (uint)n.Pitch); }
            uint hs = Hash(nk, 0xA5A5A5A5u);
            bool hit = U01(ref hs) < chance;
            bool skip = hit && U01(ref hs) < skipAmt;
            var D = new float[4];
            if (perBar) Array.Copy(barD, D, 4);
            else { uint ds = Hash(nk, 0x3c6ef35fu); for (int d = 0; d < 4; d++) D[d] = Draw(dist, ref ds, ref walk[d]); }

            int op = n.Pitch, ov = n.Vel; double ot = n.T;
            if (hit)
            {
                op = n.Pitch + LRound(D[0] * nr) + 12 * LRound(D[3] * octAmt * 2f);
                if (stay) op = SnapToCMajor(op);
                op = Math.Clamp(op, 0, 127);
                float vel = Math.Clamp(n.Vel / 127f + D[1] * velAmt * 64f / 127f, 1f / 127f, 1f);
                ov = Math.Clamp(LRound(vel * 127), 1, 127);
                ot = n.T + Math.Abs(D[2]) * timeAmt * MaxDelayMs * (bpm > 0 ? bpm : 120) / 60000.0;   // ms → beats
            }
            notes[i] = new Note(n.Pitch, n.T, n.Len, n.Vel, op, ot, ov, hit, skip);
        }
        return notes;
    }

    /// <summary>Normalised 17-bin histogram of 1500 draws (seeded by the seed) for a distribution.</summary>
    internal static double[] Histogram(int dist, int seed)
    {
        const int bins = 17;
        var h = new double[bins];
        uint s; unchecked { s = Hash((uint)seed * 31u + 5u, 0x51EDu); }
        float w = 0;
        for (int i = 0; i < 1500; i++)
        {
            float v = Draw(dist, ref s, ref w);
            h[Math.Min(bins - 1, (int)Math.Floor((v + 1) / 2 * bins))]++;
        }
        double mx = 1; foreach (var b in h) mx = Math.Max(mx, b);
        for (int i = 0; i < bins; i++) h[i] /= mx;
        return h;
    }
}

internal sealed class RandomOutputViz : Control
{
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush Edge = NotaPalette.GraphBorder;
    private static readonly IBrush Quarter = NotaPalette.GraphBorder;
    private static readonly IBrush Eighth = NotaPalette.SurfaceCard;
    private static readonly IBrush Link = NotaPalette.BorderStrong;
    private static readonly IBrush Fill = NotaPalette.Accent;
    private static readonly IBrush FillLit = NotaPalette.AccentBright;
    private static readonly IBrush OutlineHit = NotaPalette.TextMuted;
    private static readonly IBrush OutlineMiss = NotaPalette.TextDisabled;
    private static readonly IBrush Mark = NotaPalette.TextMuted;
    private static readonly IBrush Head = NotaPalette.Wash(NotaPalette.AccentBright, 0xB3);
    private static readonly IBrush Axis = NotaPalette.TextTertiary;

    /// <summary>Full view: the C labels in a left gutter, larger marks.</summary>
    public bool Full { get; init; } = true;

    private RandomModel.Note[] _notes = Array.Empty<RandomModel.Note>();
    private double _now;
    private bool _off;

    public RandomOutputViz() { MinHeight = 40; ClipToBounds = true; }

    public void Set(RandomModel.Note[] notes, double nowBeats, bool bypassed)
    {
        _notes = notes; _now = nowBeats; _off = bypassed;
        InvalidateVisual();
    }

    private static FormattedText Text(string s, double size, IBrush ink)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Mono, size, ink);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0 || _notes.Length == 0) return;
        double gutter = Full ? 22 : 0;
        var win = new Rect(gutter, 0, Math.Max(10, w - gutter), h);

        // Pitch span: every in / out pitch ±2, at least two octaves.
        int lo = int.MaxValue, hi = int.MinValue;
        foreach (var n in _notes) { lo = Math.Min(lo, Math.Min(n.InPitch, n.OutPitch)); hi = Math.Max(hi, Math.Max(n.InPitch, n.OutPitch)); }
        lo -= 2; hi += 2;
        if (hi - lo < 24) { double c = (hi + lo) / 2.0; lo = (int)Math.Round(c - 12, MidpointRounding.AwayFromZero); hi = lo + 24; }
        double row = win.Height / (hi - lo);
        double Y(int p) => win.Y + (hi - p - 0.5) * row;
        double X(double beat) => win.X + beat / RandomModel.BarBeats * win.Width;

        ctx.DrawRectangle(Well, new Pen(Edge, 1), new RoundedRect(win.Deflate(0.5), 4));
        using (ctx.PushClip(new RoundedRect(win.Deflate(1), 3)))
        {
            for (int p = (int)Math.Ceiling(lo / 12.0) * 12; p <= hi; p += 12)
                ctx.FillRectangle(Edge, new Rect(win.X, Math.Round(win.Y + (hi - p) * row), win.Width, 1));
            for (int i = 1; i < 8; i++)
                ctx.FillRectangle(i % 2 == 0 ? Quarter : Eighth, new Rect(Math.Round(X(i * 0.5)), win.Y, 1, win.Height));

            double radius = Full ? 2 : 1, nh = row + 2;
            var dash = new Pen(OutlineHit, 1, new DashStyle(new double[] { 2, 2 }, 0));
            var dashMiss = new Pen(OutlineMiss, 1, new DashStyle(new double[] { 2, 2 }, 0));
            foreach (var n in _notes)
            {
                double gx = X(n.InT), gy = Y(n.InPitch) - 1, nw = X(n.Len) - X(0);
                double ox = X(n.OutT), oy = Y(n.OutPitch) - 1;
                if (!n.Skip && n.OutPitch != n.InPitch)
                {
                    double y0 = Math.Min(gy, oy) + row / 2 + 1, y1 = Math.Max(gy, oy) + row / 2 + 1;
                    ctx.FillRectangle(Link, new Rect(Math.Round(ox + nw / 2), y0, 1, y1 - y0));
                }
                ctx.DrawRectangle(null, n.Hit ? dash : dashMiss, new RoundedRect(new Rect(gx + 0.5, gy + 0.5, Math.Max(1, nw - 1), Math.Max(1, nh - 1)), radius));
                if (!n.Skip && !_off)
                {
                    bool lit = _now >= n.OutT && _now < n.OutT + n.Len;
                    using (ctx.PushOpacity(0.35 + 0.65 * n.OutVel / 127.0))
                        ctx.DrawRectangle(lit ? FillLit : Fill, null, new RoundedRect(new Rect(ox, oy, nw, nh), radius));
                }
                if (n.Skip)
                {
                    var x = Text("×", Full ? 8 : 7, Mark);
                    ctx.DrawText(x, new Point(gx + nw / 2 - x.Width / 2, gy + nh / 2 - x.Height / 2));
                }
            }
            if (!_off) ctx.FillRectangle(Head, new Rect(Math.Round(X(_now)), win.Y, 1, win.Height));
        }

        if (!Full) return;
        for (int p = (int)Math.Ceiling(lo / 12.0) * 12; p <= hi; p += 12)
        {
            var ft = Text(DeviceCardKit.NoteName(p), 7, Axis);
            double y = Math.Clamp(win.Y + (hi - p) * row - ft.Height / 2, 0, h - ft.Height);
            ctx.DrawText(ft, new Point(gutter - 4 - ft.Width, y));
        }
    }
}

internal sealed class RandomHistViz : Control
{
    private static readonly IBrush Well = NotaPalette.BgSunken;
    private static readonly IBrush Edge = NotaPalette.GraphBorder;
    private static readonly IBrush Bar = NotaPalette.Ink("#B8862E");
    private static readonly IBrush Centre = NotaPalette.AccentBright;

    private double[] _bins = Array.Empty<double>();
    private int _dist = -1, _seed = -1;

    public RandomHistViz() { MinHeight = 20; }

    public void Set(int dist, int seed)
    {
        if (dist == _dist && seed == _seed) return;
        _dist = dist; _seed = seed; _bins = RandomModel.Histogram(dist, seed);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(Well, new Pen(Edge, 1), new RoundedRect(new Rect(0, 0, w, h).Deflate(0.5), 3));
        int n = _bins.Length;
        if (n == 0) return;
        double x0 = 3, x1 = w - 3, top = 3, bot = h - 1, gap = 1;
        double bw = (x1 - x0 - gap * (n - 1)) / n;
        for (int i = 0; i < n; i++)
        {
            double bh = Math.Max(2, _bins[i] * (bot - top));
            ctx.DrawRectangle(i == n / 2 ? Centre : Bar, null,
                new RoundedRect(new Rect(x0 + i * (bw + gap), bot - bh, Math.Max(1, bw), bh), 1, 1, 0, 0));
        }
    }
}
