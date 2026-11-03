// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Composes the launch splash's art from the release version (specs/074). The version is
// the only input: it seeds a small PRNG that picks a layout, one of six motif families
// drawn from the work of making music, a palette of track colours, a stroke weight and a
// wash. The same version always gives the same scene; consecutive patches always change
// family (research R4). The generator is a port of specs/074-release-splash-art/prototype.html,
// the version the maintainer reviewed. Pure and Avalonia-free — the smoke test links it.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nota.App;

internal static partial class SplashArtComposer
{
    /// <summary>The release version as seeds: <c>Full</c> over the whole string, <c>Line</c> over
    /// "major.minor" (it steps the family), and the patch number.</summary>
    internal readonly record struct ReleaseSeed(uint Full, uint Line, int Patch)
    {
        public static ReleaseSeed Of(string version)
        {
            version = (version ?? "").Trim();
            var full = Hash(version);
            var m = SemVer().Match(version);
            if (!m.Success) return new ReleaseSeed(full, full, 0);
            int.TryParse(m.Groups[3].Value, out var patch);
            return new ReleaseSeed(full, Hash($"{m.Groups[1].Value}.{m.Groups[2].Value}"), patch);
        }
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex SemVer();

    /// <summary>FNV-1a over UTF-16 code units plus a finaliser — not string.GetHashCode, which
    /// is randomised per process.</summary>
    internal static uint Hash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in s) { h ^= c; h *= 16777619; }
            h ^= h >> 15; h *= 2246822507; h ^= h >> 13;
            return h;
        }
    }

    /// <summary>mulberry32 — tiny, well-mixed, and identical to the reviewed prototype's.</summary>
    internal sealed class Mulberry32(uint seed)
    {
        private uint _a = seed;
        public double Next()
        {
            unchecked
            {
                _a += 0x6D2B79F5;
                uint t = (_a ^ (_a >> 15)) * (1 | _a);
                t = (t + ((t ^ (t >> 7)) * (61 | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
        public T Pick<T>(T[] a) => a[(int)(Next() * a.Length)];
    }

    private static readonly Dictionary<SplashFamily, SplashFamily[]> Overlays = new()
    {
        [SplashFamily.Panels] = new[] { SplashFamily.Rings, SplashFamily.Lissajous },
        [SplashFamily.Strata] = new[] { SplashFamily.Rings },
        [SplashFamily.Faders] = new[] { SplashFamily.Strata },
        [SplashFamily.Roll] = new[] { SplashFamily.Rings },
    };

    /// <summary>The scene for <paramref name="version"/>. Total: any string yields a valid,
    /// non-empty scene and nothing throws.</summary>
    public static SplashScene Compose(string version)
    {
        version ??= "";
        try { return ComposeCore(version, ReleaseSeed.Of(version)); }
        catch
        {
            // Unreachable in practice; the splash must never lose its art to a bug in a motif.
            var c = new Ctx(new Mulberry32(1), 2, 0.5, new[] { R(SplashRole.Rule) }, grad: false, overlay: false);
            var art = new SRect(150, 0, 270, 300);
            c.Line(new SPoint(art.X + 20, 150), new SPoint(art.X + art.W - 20, 150), R(SplashRole.Rule), 2);
            return new SplashScene(version, SplashLayout.TextLeft, art, new SRect(0, 0, 150, 300), SplashFamily.Panels, null,
                SplashPaletteMode.Mono, new EqList<int>(new[] { 0 }), 2, 0.5, SplashWash.None, new EqList<Prim>(c.Out), c.Out.Count);
        }
    }

    private static SplashScene ComposeCore(string version, ReleaseSeed seed)
    {
        var r = new Mulberry32(seed.Full);
        var layout = r.Pick(new[] { SplashLayout.TextLeft, SplashLayout.TextRight, SplashLayout.TextBottom, SplashLayout.TextLeft, SplashLayout.TextBottom });
        var (art, text) = layout switch
        {
            SplashLayout.TextLeft => (new SRect(150, 0, 270, 300), new SRect(0, 0, 150, 300)),
            SplashLayout.TextRight => (new SRect(0, 0, 270, 300), new SRect(270, 0, 150, 300)),
            _ => (new SRect(0, 0, 420, 196), new SRect(0, 196, 420, 104)),
        };

        // The family steps by a number coprime to 6 per patch, so neighbours always differ.
        var family = (SplashFamily)(int)(unchecked(seed.Line + 5u * (uint)Math.Max(0, seed.Patch)) % 6u);
        SplashFamily? overlay = Overlays.TryGetValue(family, out var ovs) && r.Next() < 0.35 ? r.Pick(ovs) : null;

        int b = (int)(r.Next() * 9);
        var mode = r.Pick(new[] { SplashPaletteMode.Analog, SplashPaletteMode.Analog, SplashPaletteMode.Pair, SplashPaletteMode.Triad, SplashPaletteMode.Mono });
        int[] idx = mode switch
        {
            SplashPaletteMode.Mono => new[] { b },
            SplashPaletteMode.Pair => new[] { b, (b + 4) % 9 },
            SplashPaletteMode.Triad => new[] { b, (b + 3) % 9, (b + 6) % 9 },
            _ => new[] { b, (b + 1) % 9, (b + 2) % 9 },
        };
        double amt = 0.35 + r.Next() * 0.5;
        double w = r.Pick(new[] { 1.5, 2, 2, 2.5, 3 });
        var wash = r.Pick(new[] { SplashWash.None, SplashWash.Radial, SplashWash.Linear, SplashWash.Radial });
        double wx = r.Next(), wy = r.Next();

        var pal = idx.Select(i => new ColorRef(SplashRole.Track, i)).ToArray();
        var c = new Ctx(r, w, amt, pal, grad: true, overlay: false);

        if (wash != SplashWash.None)
        {
            var first = pal[0]; var last = pal[^1];
            Paint p = wash == SplashWash.Radial
                ? new RadialPaint(Stops((0, first.At(0.28)), (1, last.At(0))), new SPoint(wx, wy), 0.8, PaintSpace.Bounds)
                : new LinearPaint(Stops((0, first.At(0.22)), (1, last.At(0))), new SPoint(wx, 0), new SPoint(1 - wx, 1), PaintSpace.Bounds);
            c.Rect(art, p);
        }

        Draw(family, art, c);
        int overlayStart = c.Out.Count;
        if (overlay is { } ov)
        {
            var co = new Ctx(r, Math.Max(1, w * 0.6), amt, new[] { R(SplashRole.Rule), R(SplashRole.Tertiary) }, grad: false, overlay: true, c.Out);
            Draw(ov, art, co);
        }

        return new SplashScene(version, layout, art, text, family, overlay, mode, new EqList<int>(idx), w, amt, wash,
            new EqList<Prim>(c.Out), overlayStart);
    }

    private static void Draw(SplashFamily f, SRect b, Ctx c)
    {
        switch (f)
        {
            case SplashFamily.Roll: Roll(b, c); break;
            case SplashFamily.Rings: Rings(b, c); break;
            case SplashFamily.Strata: Strata(b, c); break;
            case SplashFamily.Panels: Panels(b, c); break;
            case SplashFamily.Lissajous: Lissajous(b, c); break;
            default: Faders(b, c); break;
        }
    }

    // ---- motif families ------------------------------------------------------------

    /// <summary>A piano roll: a step grid and one to three voices walking a scale.</summary>
    private static void Roll(SRect b, Ctx c)
    {
        var r = c.R;
        int steps = 16 * (1 + (int)(r.Next() * 2)); double sw = b.W / steps;
        int rows = 10 + (int)(r.Next() * 12); double rh = b.H / rows;
        for (int s = 0; s <= steps; s++)
            c.Line(new SPoint(b.X + s * sw, b.Y), new SPoint(b.X + s * sw, b.Y + b.H),
                R(s % 4 != 0 ? SplashRole.Grid : SplashRole.Rule), s % 16 != 0 ? 1 : c.W);
        int voices = 1 + (int)(r.Next() * 3);
        for (int v = 0; v < voices; v++)
        {
            var vc = c.Pal[v % c.Pal.Length];
            int p = (int)(rows * (0.25 + r.Next() * 0.5)), st = (int)(r.Next() * 4);
            while (st < steps)
            {
                int len = r.Pick(new[] { 1, 1, 2, 2, 3, 4 });
                if (r.Next() < 0.78)
                {
                    double x = b.X + st * sw + 1, nw = len * sw - 2, y = b.Y + p * rh + 1, vel = 0.35 + r.Next() * 0.65;
                    // The version's colour share decides how many notes carry the voice's colour.
                    if (r.Next() < c.Amt)
                    {
                        Paint fill = c.Grad
                            ? new LinearPaint(Stops((0, vc), (1, vc.At(0.25 + 0.4 * (1 - vel)))), new SPoint(0, 0), new SPoint(1, 0), PaintSpace.Bounds)
                            : new SolidPaint(vc);
                        c.Rect(new SRect(x, y, nw, rh - 2), fill, vc.Darker(0.35), 1, radius: 2);
                    }
                    else c.Rect(new SRect(x, y, nw, rh - 2), new SolidPaint(R(SplashRole.Hover)), R(SplashRole.Seam), 1, radius: 2);
                }
                st += len;
                p = Math.Clamp(p + r.Pick(new[] { -2, -1, -1, 0, 1, 1, 2 }), 0, rows - 1);
            }
        }
        c.Playhead(b, b.X + sw * Math.Floor(steps * (0.3 + r.Next() * 0.4)));
    }

    /// <summary>Euclidean rhythm rings around a centre; the outer ring's last hit is the current step.</summary>
    private static void Rings(SRect b, Ctx c)
    {
        var r = c.R;
        double cx = b.X + b.W * (0.3 + r.Next() * 0.4), cy = b.Y + b.H * (0.3 + r.Next() * 0.4);
        int k = 3 + (int)(r.Next() * 4);
        double r0 = Math.Min(b.W, b.H) * 0.12, dr = (Math.Max(b.W, b.H) * 0.6 - r0) / k;
        double lw = Math.Max(1.5, c.W), dot = 3 + lw;
        var centre = new SPoint(cx, cy);
        if (c.Grad)
            c.Circle(centre, r0 + (k - 1) * dr,
                new RadialPaint(Stops((0, c.Pal[0].At(0.35)), (0.6, c.Pal[^1].At(0.12)), (1, R(SplashRole.Ground).At(0))), new SPoint(0.5, 0.5), 0.5, PaintSpace.Bounds));
        SPoint? last = null;
        for (int i = 0; i < k; i++)
        {
            double radius = r0 + i * dr;
            int n = r.Pick(new[] { 8, 12, 16, 5, 7, 9, 13 });
            int hits = 1 + (int)(r.Next() * (n - 1));
            double rot = r.Next() * 6.28;
            // The outer ring always carries colour; the others by the version's colour share.
            var rc = i == k - 1 || r.Next() < c.Amt ? c.Pal[i % c.Pal.Length] : R(SplashRole.Tertiary);
            c.Circle(centre, radius, null, R(SplashRole.Rule), lw);
            for (int j = 0; j < n; j++)
            {
                bool on = (j + 1) * hits / n != j * hits / n;
                double a = rot + (double)j / n * 6.283;
                var pt = new SPoint(cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius);
                if (on) c.Circle(pt, dot, new SolidPaint(rc));
                else c.Circle(pt, lw, new SolidPaint(R(SplashRole.Hover)));
                if (on && i == k - 1) { c.Line(centre, pt, R(SplashRole.Grid), lw * 0.7); last = pt; }
            }
        }
        if (last is { } l)
        {
            c.Line(centre, l, c.Live, lw);
            c.Circle(l, dot + 3, null, c.Live, lw);
        }
    }

    /// <summary>Stacked waveform strata under a Gaussian swell; palette colours mark hero lines.</summary>
    private static void Strata(SRect b, Ctx c)
    {
        var r = c.R;
        int n = 9 + (int)(r.Next() * 10);
        double gap = b.H / (n + 3);
        double[] fs = { 1 + r.Next() * 3, 4 + r.Next() * 6, 9 + r.Next() * 9 };
        double[] ph = { r.Next() * 6, r.Next() * 6, r.Next() * 6 };
        double cen = b.X + b.W * (0.3 + r.Next() * 0.4), wid = b.W * (0.12 + r.Next() * 0.2);
        var hero = new Dictionary<int, ColorRef>();
        for (int i = 0; i < c.Pal.Length; i++) hero[(int)(n * (0.35 + 0.5 * i / c.Pal.Length))] = c.Pal[i];
        Paint? across = c.Grad && c.Pal.Length > 1
            ? new LinearPaint(Spread(c.Pal), new SPoint(b.X, 0), new SPoint(b.X + b.W, 0), PaintSpace.Art) : null;
        for (int i = 0; i < n; i++)
        {
            double y0 = b.Y + gap * (i + 3);
            var pts = new List<SPoint>();
            for (double x = 0; x <= b.W; x += 2)
            {
                double X = b.X + x, env = Math.Exp(-Math.Pow((X - cen) / wid, 2));
                double v = Math.Sin(x / b.W * fs[0] * 6.28 + ph[0] + i * 0.4) * 0.5
                         + Math.Sin(x / b.W * fs[1] * 6.28 + ph[1] - i * 0.3) * 0.35
                         + Math.Sin(x / b.W * fs[2] * 6.28 + ph[2] + i) * 0.15;
                pts.Add(new SPoint(X, y0 - Math.Abs(v) * env * gap * 3.2));
            }
            var poly = new List<SPoint> { new(b.X, b.Y + b.H) };
            poly.AddRange(pts);
            poly.Add(new SPoint(b.X + b.W, b.Y + b.H));
            bool isHero = hero.TryGetValue(i, out var hc);
            c.Polyline(poly, null, new SolidPaint(R(SplashRole.Ground)), 1);
            if (c.Grad && isHero)
                c.Polyline(poly, null, new LinearPaint(Stops((0, hc.At(0.45)), (0.35, hc.At(0))), new SPoint(0, 0), new SPoint(0, 1), PaintSpace.Bounds), 1);
            c.Polyline(pts, isHero ? across ?? new SolidPaint(hc) : new SolidPaint(R(SplashRole.Tertiary)), null,
                isHero ? c.W * 1.4 : Math.Max(1, c.W * 0.6));
        }
        c.Playhead(b, cen + wid * (r.Next() - 0.5));
    }

    /// <summary>Fader rails with level fills; one fader is being touched.</summary>
    private static void Faders(SRect b, Ctx c)
    {
        var r = c.R;
        int n = 8 + (int)(r.Next() * 10);
        double sw = b.W / n, fq = r.Next() * 3 + 0.5, ph = r.Next() * 6, lw = Math.Max(1, c.W * 0.8);
        int hot = (int)(r.Next() * n);
        for (int i = 0; i < n; i++)
        {
            double x = b.X + sw * (i + 0.5);
            // The touched fader always carries colour; the others by the version's colour share.
            var col = i == hot || r.Next() < c.Amt ? c.Pal[(int)((double)i / n * c.Pal.Length)] : R(SplashRole.Tertiary);
            c.Line(new SPoint(x, b.Y + 12), new SPoint(x, b.Y + b.H - 12), R(SplashRole.Rule), lw);
            double v = 0.5 + 0.4 * Math.Sin((double)i / n * fq * 6.28 + ph) + 0.1 * (r.Next() - 0.5);
            double y = b.Y + 12 + (1 - v) * (b.H - 24), fw = Math.Min(8, sw * 0.3);
            Paint level = c.Grad
                ? new LinearPaint(Stops((0, col), (1, col.At(0.15))), new SPoint(0, 0), new SPoint(0, 1), PaintSpace.Bounds)
                : new SolidPaint(col);
            c.Rect(new SRect(x - fw / 2, y, fw, b.Y + b.H - 12 - y), level);
            c.Rect(new SRect(x - Math.Min(10, sw * 0.38), y - 4, Math.Min(20, sw * 0.76), 8), new SolidPaint(R(SplashRole.Hover)), R(SplashRole.Seam), 1);
            double hw = Math.Min(6, sw * 0.22);
            if (i == hot) c.Line(new SPoint(x - hw, y), new SPoint(x + hw, y), c.Live, 2);
            else c.Line(new SPoint(x - hw, y), new SPoint(x + hw, y), R(SplashRole.Ink), lw);
        }
    }

    /// <summary>The art split into panels on seams, some in track colours, some hatched or dotted.</summary>
    private static void Panels(SRect b, Ctx c)
    {
        var r = c.R;
        var tones = new[] { R(SplashRole.Ground), R(SplashRole.Panel), R(SplashRole.Raised), R(SplashRole.Hover), R(SplashRole.App) };
        var cells = new List<SRect>();
        void Split(double x, double y, double w, double h, int d)
        {
            if (d > 4 || w < 40 || h < 30 || (d > 1 && r.Next() < 0.25)) { cells.Add(new SRect(x, y, w, h)); return; }
            if (w > h * 1.2 || (w > h * 0.8 && r.Next() < 0.5))
            {
                double s = w * (0.25 + r.Next() * 0.5);
                Split(x, y, s, h, d + 1); Split(x + s, y, w - s, h, d + 1);
            }
            else
            {
                double s = h * (0.25 + r.Next() * 0.5);
                Split(x, y, w, s, d + 1); Split(x, y + s, w, h - s, d + 1);
            }
        }
        Split(b.X, b.Y, b.W, b.H, 0);

        var colored = new HashSet<int>();
        int m = Math.Max(1, (int)Math.Round(cells.Count * c.Amt * 0.5));
        while (colored.Count < Math.Min(m, cells.Count)) colored.Add((int)(r.Next() * cells.Count));

        var dirs = new[] { (0d, 0d, 0d, 1d), (0d, 0d, 1d, 0d), (0d, 1d, 1d, 0d) };
        bool accDone = false;
        for (int idx = 0; idx < cells.Count; idx++)
        {
            var (x, y, w, h) = cells[idx];
            ColorRef? col = colored.Contains(idx) ? r.Pick(c.Pal) : null;
            var dir = r.Pick(dirs);
            Paint fill = col is { } cc
                ? c.Grad ? new LinearPaint(Stops((0, cc), (1, cc.ToGround(0.7))), new SPoint(dir.Item1, dir.Item2), new SPoint(dir.Item3, dir.Item4), PaintSpace.Bounds)
                         : new SolidPaint(cc)
                : new SolidPaint(r.Pick(tones));
            c.Rect(new SRect(x, y, w, h), fill);
            double k = r.Next();
            var ink = col is { } c2 ? c2.Darker(0.35) : R(SplashRole.Rule);
            if (k < 0.22)
            {
                for (double q = x + 4; q < x + w; q += 3 + c.W * 2)
                    c.Line(new SPoint(q, y), new SPoint(q, y + h - 1), ink, c.W * 0.8);
            }
            else if (k < 0.38)
            {
                var dotFill = new SolidPaint(col is null ? R(SplashRole.Tertiary) : ink);
                for (double q = y + 5; q < y + h; q += 6)
                    for (double p = x + 5; p < x + w; p += 6)
                        c.Circle(new SPoint(p, q), c.W * 0.6, dotFill);
            }
            else if (k < 0.52)
            {
                bool ac = !accDone && col is null && r.Next() < 0.5;
                if (ac) accDone = true;
                c.Circle(new SPoint(x + w / 2, y + h / 2), Math.Min(w, h) * 0.3, null,
                    ac ? c.Live : col is null ? R(SplashRole.Ink) : ink, c.W);
            }
            c.Rect(new SRect(x + 0.5, y + 0.5, w - 1, h - 1), null, R(SplashRole.Seam), Math.Max(1, c.W * 0.75));
        }
    }

    /// <summary>A Lissajous figure on a grid, its echo flattened beneath, and a live point on it.</summary>
    private static void Lissajous(SRect b, Ctx c)
    {
        var r = c.R;
        for (double x = b.X + 20; x < b.X + b.W; x += 20) c.Line(new SPoint(x, b.Y), new SPoint(x, b.Y + b.H), R(SplashRole.Grid), 1);
        for (double y = b.Y + 20; y < b.Y + b.H; y += 20) c.Line(new SPoint(b.X, y), new SPoint(b.X + b.W, y), R(SplashRole.Grid), 1);
        int a = r.Pick(new[] { 1, 2, 3, 3, 4, 5 }), bb = r.Pick(new[] { 2, 3, 4, 5, 7 });
        double d = r.Next() * 3, cx = b.X + b.W / 2, cy = b.Y + b.H / 2, ax = b.W * 0.4, ay = b.H * 0.38;
        var pts = new List<SPoint>();
        for (double t = 0; t <= 6.2832 + 0.001; t += 0.01)
            pts.Add(new SPoint(cx + ax * Math.Sin(a * t + d), cy + ay * Math.Sin(bb * t)));
        Paint main = c.Grad
            ? new LinearPaint(c.Pal.Length > 1 ? Spread(c.Pal) : Stops((0, c.Pal[0]), (1, c.Pal[0].ToGround(0.6))),
                new SPoint(b.X + b.W * 0.1, b.Y), new SPoint(b.X + b.W * 0.9, b.Y + b.H), PaintSpace.Art)
            : new SolidPaint(c.Pal[0]);
        c.Polyline(pts.Select(p => new SPoint(p.X, cy + (p.Y - cy) * 0.6)),
            new SolidPaint(c.Pal.Length > 1 ? c.Pal[1] : R(SplashRole.Rule)), null, Math.Max(1, c.W * 0.6));
        c.Polyline(pts, main, null, c.W * 1.3);
        var q = pts[(int)(r.Next() * pts.Count)];
        c.Circle(q, 3 + c.W, new SolidPaint(c.Live));
    }

    // ---- helpers ---------------------------------------------------------------------

    private static ColorRef R(SplashRole role) => new(role);

    private static EqList<SStop> Stops(params (double Offset, ColorRef Color)[] s) => new(s.Select(x => new SStop(x.Offset, x.Color)));

    private static EqList<SStop> Spread(ColorRef[] pal) => new(pal.Select((p, i) => new SStop((double)i / (pal.Length - 1), p)));

    private static double Q(double v) => Math.Round(v * 10) / 10;
    private static SPoint Q(SPoint p) => new(Q(p.X), Q(p.Y));

    /// <summary>The drawing context of one family: the shared PRNG, the version's stroke weight,
    /// colour share and palette, and whether gradients and the live accent are allowed (an
    /// overlay draws neutral, flat, and never live).</summary>
    private sealed class Ctx(Mulberry32 r, double w, double amt, ColorRef[] pal, bool grad, bool overlay, List<Prim>? output = null)
    {
        public readonly Mulberry32 R = r;
        public readonly double W = w, Amt = amt;
        public readonly ColorRef[] Pal = pal;
        public readonly bool Grad = grad;
        public readonly List<Prim> Out = output ?? new List<Prim>();

        /// <summary>The colour of the one live element: the accent, or plain ink in an overlay.</summary>
        public ColorRef Live => new(overlay ? SplashRole.Ink : SplashRole.Accent);

        public void Rect(SRect s, Paint? fill, ColorRef? stroke = null, double sw = 1, double radius = 0) =>
            Out.Add(new RectPrim(new SRect(Q(s.X), Q(s.Y), Q(Math.Max(1, s.W)), Q(Math.Max(1, s.H))), radius, fill,
                stroke is { } st ? new SolidPaint(st) : null, sw));
        public void Rect(SRect s, Paint fill) => Rect(s, fill, null);
        public void Line(SPoint a, SPoint b, ColorRef col, double sw) => Out.Add(new LinePrim(Q(a), Q(b), new SolidPaint(col), sw));
        public void Polyline(IEnumerable<SPoint> pts, Paint? stroke, Paint? fill, double sw) =>
            Out.Add(new PolylinePrim(new EqList<SPoint>(pts.Select(Q)), stroke, fill, sw));
        public void Circle(SPoint centre, double radius, Paint? fill, ColorRef? stroke = null, double sw = 1) =>
            Out.Add(new CirclePrim(Q(centre), Q(radius), fill, stroke is { } st ? new SolidPaint(st) : null, sw));
        public void Circle(SPoint centre, double radius, Paint fill) => Circle(centre, radius, fill, null);

        /// <summary>A playhead: the live line down the art with a small head at the top.</summary>
        public void Playhead(SRect b, double x)
        {
            Line(new SPoint(x, b.Y + 5), new SPoint(x, b.Y + b.H), Live, Math.Max(1.5, W * 0.75));
            Out.Add(new PolygonPrim(new EqList<SPoint>(new[] { new SPoint(x - 5, b.Y), new SPoint(x + 5, b.Y), new SPoint(x, b.Y + 6) }.Select(Q)),
                new SolidPaint(Live)));
        }
    }
}
