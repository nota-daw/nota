// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Aurora's table window, one per oscillator on the Osc tab (specs/053): the table's
// frames drawn as a skewed 3-D stack, the frame the oscillator plays in bright neutral ink
// and the rest fading back into the well — "position is a place in a stack, not a number".
//
// Two sources. A built-in bank is drawn from the same additive recipe as
// WavetableSynth::harmAmp (a low harmonic count → representative, not sample-exact); the
// recipe is fixed per bank, so all four banks' frames are computed ONCE per process and
// shared by every card. An imported table is drawn from the engine's preview of it (up to
// 16 frames for the stack and a 64-step grid for the lit frame, fetched once per table and
// shared by every window that shows it).
//
// Cost. The device panel ticks thirty times a second and the Osc tab shows two of these, so
// the window and its stack are drawn once into a bitmap at the screen's scale, kept until
// the source, the size, the inset, the dim state or the theme changes; the lit frame's
// geometry is cached per grid step. A repaint is therefore one bitmap and one line, and one
// only happens when the lit step, the source, the dim state or the drop ring changes — a
// knob moving inside one step does not.

using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Nota.App;

internal sealed class AuroraTableView : Control
{
    private const int Banks = 4, Frames = 16, N = 96, HarmMax = 28;

    private static readonly IBrush Lit = NotaPalette.VizInk;
    private static readonly IBrush Dim = NotaPalette.TextDisabled;
    // Four depth steps from the back of the stack to the front, so the stack reads as
    // depth rather than as sixteen equal lines.
    private static readonly IBrush[] Depth =
    {
        NotaPalette.Wash(NotaPalette.VizInk, 0x28), NotaPalette.Wash(NotaPalette.VizInk, 0x40),
        NotaPalette.Wash(NotaPalette.VizInk, 0x60), NotaPalette.Wash(NotaPalette.VizInk, 0x88),
    };

    private static readonly Pen[] DepthPens = MakePens(Depth);
    private static readonly Pen[] DimPens = MakePens(new[] { Dim, Dim, Dim, Dim });
    private static readonly Pen HotPen = new(Lit, 1.7, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static Pen[] MakePens(IBrush[] brushes, PenLineJoin join = PenLineJoin.Round)
    {
        var p = new Pen[brushes.Length];
        for (int i = 0; i < brushes.Length; i++) p[i] = new Pen(brushes[i], 1, lineJoin: join);
        return p;
    }

    // [bank][frame][sample] — built on first use, read-only afterwards.
    private static readonly double[][][] Shapes = new double[Banks][][];
    private static readonly object ShapeLock = new();

    private int _bank = -1, _active = -1;
    private bool _dim, _drag;

    // The imported source: a preview of up to 16 stack frames and a 64-step lit grid.
    public sealed record Preview(float[] Data, int Rep, int Grid, int Points, int Frames);
    private Preview? _imp;
    private uint _serial;
    private readonly StreamGeometry?[] _gridGeo = new StreamGeometry?[64];
    private uint _geoSerial;
    private static readonly Pen DragPen = new(NotaPalette.Accent, 1.5);
    // An imported frame's outline zigzags where the frame holds fast cycles; bevel joins
    // stroke it at a fraction of the cost of round ones and look the same at this size.
    private static readonly Pen[] ImpDepthPens = MakePens(Depth, PenLineJoin.Bevel);
    private static readonly Pen[] ImpDimPens = MakePens(new[] { Dim, Dim, Dim, Dim }, PenLineJoin.Bevel);
    private static readonly Pen ImpHotPen = new(Lit, 1.7, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Bevel);

    // Previews by table serial, shared by every window (a serial is never reused).
    private static readonly System.Collections.Generic.Dictionary<uint, Preview> PreviewCache = new();
    private static readonly System.Collections.Generic.Queue<uint> PreviewOrder = new();

    /// <summary>The preview for a table serial, fetched with <paramref name="fetch"/> the first
    /// time any window asks for it.</summary>
    public static Preview? PreviewFor(uint serial, Func<Preview?> fetch)
    {
        if (serial == 0) return null;
        if (PreviewCache.TryGetValue(serial, out var p)) return p;
        p = fetch();
        if (p is null) return null;
        PreviewCache[serial] = p;
        PreviewOrder.Enqueue(serial);
        while (PreviewOrder.Count > 32) PreviewCache.Remove(PreviewOrder.Dequeue());
        return p;
    }

    // The geometry cache: the frames as laid out for (bank, size, inset).
    private readonly StreamGeometry?[] _geo = new StreamGeometry?[Frames];
    private int _geoBank = -1;
    private double _geoW = -1, _geoH = -1, _geoInset = -1;

    /// <summary>Space kept clear at the top of the window for a caption drawn over it.</summary>
    public double TopInset { get; set; }

    /// <summary>Greys the whole stack (the oscillator it belongs to is switched off).</summary>
    public bool Dimmed
    {
        get => _dim;
        set { if (_dim == value) return; _dim = value; InvalidateVisual(); }
    }

    public AuroraTableView() { ClipToBounds = true; }

    /// <summary>The orange inner ring that says a dropped file would land here.</summary>
    public bool DragOver
    {
        get => _drag;
        set { if (_drag == value) return; _drag = value; InvalidateVisual(); }
    }

    /// <summary>Point the window at a built-in bank and a position. Only a change of bank or
    /// of the frame the position lands on redraws — a knob moving inside one frame does not.</summary>
    public void Set(int bank, double pos)
    {
        int b = Math.Clamp(bank, 0, Banks - 1);
        int a = Math.Clamp((int)Math.Round(Math.Clamp(pos, 0, 1) * (Frames - 1)), 0, Frames - 1);
        if (_imp is null && b == _bank && a == _active) return;
        _imp = null; _serial = 0;
        _bank = b; _active = a;
        InvalidateVisual();
    }

    /// <summary>Point the window at an imported table (its serial and preview) and a position,
    /// quantised to the preview's lit grid: only a change of step redraws.</summary>
    public void SetImported(uint serial, Preview preview, double pos)
    {
        int steps = Math.Max(1, preview.Grid);
        int a = Math.Clamp((int)Math.Round(Math.Clamp(pos, 0, 1) * (steps - 1)), 0, steps - 1);
        if (_imp is not null && serial == _serial && a == _active) return;
        _imp = preview; _serial = serial; _active = a;
        InvalidateVisual();
    }

    private static double HarmAmp(int bank, double t, int n) => bank switch
    {
        0 => n == 1 ? 1.0 : t / n,
        1 => (n % 2 == 1) ? (n == 1 ? 1.0 : t / n) : 0.0,
        2 => Math.Exp(-Sq((n - (1 + t * 16.0)) / (2.0 + t * 3.0))) + (n == 1 ? 0.15 : 0.0),
        _ => (1.0 / n) * Math.Abs(Math.Cos(Math.PI * n * (0.25 + 0.75 * t))),
    };
    private static double Sq(double x) => x * x;

    private static double[][] Bank(int bank)
    {
        var got = Volatile.Read(ref Shapes[bank]);
        if (got is not null) return got;
        lock (ShapeLock)
        {
            if (Shapes[bank] is { } already) return already;
            var frames = new double[Frames][];
            Span<double> amp = stackalloc double[HarmMax + 1];
            for (int f = 0; f < Frames; f++)
            {
                double t = (double)f / (Frames - 1);
                // The recipe depends on (bank, frame), not on the sample: evaluate the
                // harmonic amplitudes once and only sum the ones that carry anything.
                for (int n = 1; n <= HarmMax; n++) amp[n] = HarmAmp(bank, t, n);
                var buf = new double[N];
                double peak = 1e-6;
                for (int j = 0; j < N; j++)
                {
                    double ph = 2 * Math.PI * j / N, acc = 0;
                    for (int n = 1; n <= HarmMax; n++) if (amp[n] > 1e-4) acc += amp[n] * Math.Sin(n * ph);
                    buf[j] = acc;
                    peak = Math.Max(peak, Math.Abs(acc));
                }
                for (int j = 0; j < N; j++) buf[j] /= peak;
                frames[f] = buf;
            }
            Volatile.Write(ref Shapes[bank], frames);
            return frames;
        }
    }

    // The window and its stack, drawn once (see the header) and the key it was drawn for.
    private RenderTargetBitmap? _bg;
    private (uint Serial, int Bank, double W, double H, double Inset, bool Dim, double Scale, Color Ink) _bgKey;

    public override void Render(DrawingContext ctx)
    {
        long t0 = UiProbe.Start();
        UiProbe.FirstPaint();
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 2 || h <= 2 || (_bank < 0 && _imp is null)) return;
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var ink = (Depth[^1] as ISolidColorBrush)?.Color ?? default;   // follows the theme
        var key = (_imp is null ? 0u : _serial, _imp is null ? _bank : -1, w, h, TopInset, _dim, scale, ink);
        if (_bg is null || key != _bgKey)
        {
            _bg?.Dispose();
            _bg = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale)),
                new Vector(96 * scale, 96 * scale));
            using (var bc = _bg.CreateDrawingContext())
            {
                NotaGraph.Window(bc, new Rect(0, 0, w, h));
                if (_imp is { } bimp) RenderImported(bc, bimp, w, h, stack: true);
                else RenderBuiltIn(bc, w, h, stack: true);
            }
            _bgKey = key;
        }
        ctx.DrawImage(_bg, new Rect(0, 0, w, h));
        if (_imp is { } imp) RenderImported(ctx, imp, w, h, stack: false);
        else RenderBuiltIn(ctx, w, h, stack: false);
        if (_drag)
            ctx.DrawRectangle(null, DragPen, new RoundedRect(new Rect(0, 0, w, h).Deflate(1.25), NotaGraph.Radius));
        UiProbe.EndPaint(t0);
    }

    // The stack's layout: skewed up-and-right, frame 0 bottom-front and the last top-back,
    // sized so every frame's centre ± amplitude stays inside the window.
    private readonly record struct Layout(double PadX, double WaveW, double SkewSpan, double Amp, double CyBottom, double CyTop)
    {
        public double Ox(double d) => PadX + d * SkewSpan;
        public double Cy(double d) => CyBottom - (CyBottom - CyTop) * d;
    }

    private Layout? Lay(double w, double h)
    {
        const double padX = 9, padY = 7;
        double top = padY + TopInset;
        double amp = (h - top - padY) * 0.15;
        double cyBottom = h - padY - amp, cyTop = top + amp;
        if (cyBottom <= cyTop) return null;
        return new Layout(padX, (w - padX * 2) * 0.70, (w - padX * 2) * 0.30, amp, cyBottom, cyTop);
    }

    private static StreamGeometry Wave(Layout l, double d, ReadOnlySpan<double> buf, double ampScale = 1)
    {
        double ox = l.Ox(d), cy = l.Cy(d), amp = l.Amp * ampScale;
        int n = buf.Length;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(ox, cy - buf[0] * amp), false);
            for (int j = 1; j <= n; j++)
                g.LineTo(new Point(ox + (double)j / n * l.WaveW, cy - buf[j % n] * amp));
        }
        return geo;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bg?.Dispose();
        _bg = null;
    }

    // stack: the frames in their depth inks (into the bitmap); otherwise the lit frame alone.
    private void RenderBuiltIn(DrawingContext ctx, double w, double h, bool stack)
    {
        if (Lay(w, h) is not { } l) return;
        var pens = _dim ? DimPens : DepthPens;
        var hot = _dim ? DimPens[^1] : HotPen;
        if (_geoSerial != 0 || _geoBank != _bank || _geoW != w || _geoH != h || _geoInset != TopInset)
        {
            var frames = Bank(_bank);
            for (int f = 0; f < Frames; f++) _geo[f] = Wave(l, (double)f / (Frames - 1), frames[f]);
            _geoBank = _bank; _geoSerial = 0; _geoW = w; _geoH = h; _geoInset = TopInset;
        }
        // Back to front, so a nearer frame overlaps the one behind it; the lit one on top.
        if (stack)
            for (int f = Frames - 1; f >= 0; f--) ctx.DrawGeometry(null, pens[f * Depth.Length / Frames], _geo[f]!);
        else if (_active is >= 0 and < Frames)
            ctx.DrawGeometry(null, hot, _geo[_active]!);
    }

    private void RenderImported(DrawingContext ctx, Preview p, double w, double h, bool stack)
    {
        if (Lay(w, h) is not { } l) return;
        var pens = _dim ? ImpDimPens : ImpDepthPens;
        var hot = _dim ? ImpDimPens[^1] : ImpHotPen;
        int pts = p.Points, rep = Math.Min(p.Rep, Frames);
        ReadOnlySpan<double> Frame(int index)
        {
            var buf = new double[pts];
            for (int j = 0; j < pts; j++) buf[j] = p.Data[index * pts + j];
            return buf;
        }
        if (rep <= 1)
        {
            // One cycle: the cycle itself, front and centre, in the lit ink.
            if (_geoSerial != _serial || _geoW != w || _geoH != h || _geoInset != TopInset)
            {
                var single = new Layout(l.PadX, w - l.PadX * 2, 0, l.Amp, (l.CyBottom + l.CyTop) / 2, (l.CyBottom + l.CyTop) / 2);
                _geo[0] = Wave(single, 0, Frame(0), 2.6);
                Array.Clear(_gridGeo);
                _geoSerial = _serial; _geoBank = -1; _geoW = w; _geoH = h; _geoInset = TopInset;
            }
            if (!stack) ctx.DrawGeometry(null, hot, _geo[0]!);
            return;
        }
        if (_geoSerial != _serial || _geoW != w || _geoH != h || _geoInset != TopInset)
        {
            for (int f = 0; f < rep; f++) _geo[f] = Wave(l, (double)f / (rep - 1), Frame(f));
            Array.Clear(_gridGeo);
            _geoSerial = _serial; _geoBank = -1; _geoW = w; _geoH = h; _geoInset = TopInset;
        }
        if (stack)
        {
            for (int f = rep - 1; f >= 0; f--) ctx.DrawGeometry(null, pens[f * Depth.Length / rep], _geo[f]!);
            return;
        }
        // The lit frame comes from the finer grid, at its own depth in the stack.
        int grid = Math.Max(1, p.Grid), a = Math.Clamp(_active, 0, grid - 1);
        _gridGeo[a] ??= Wave(l, grid > 1 ? (double)a / (grid - 1) : 0, Frame(rep + a));
        ctx.DrawGeometry(null, hot, _gridGeo[a]!);
    }
}
