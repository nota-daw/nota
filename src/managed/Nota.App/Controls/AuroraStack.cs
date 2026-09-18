// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Aurora's wavetable window: the 16 single-cycle frames drawn as a skewed 3-D stack,
// the frame Position sits on lit brass and the rest fading back into the well — "position
// is a place in a stack, not a number". The shapes come from the same additive recipe as
// WavetableSynth::harmAmp (a low harmonic count → representative, not sample-exact).
//
// The recipe is fixed per bank, so all four banks' frames are computed ONCE per process
// and shared by every card: rebuilding them inside Render cost thirty thousand sines and
// a thousand line segments on every one of the thirty ticks a second the device panel
// runs, which is what made switching to an Aurora track stutter. Render now draws each
// frame as a single geometry, and Set only invalidates when the picture actually changes.

using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AuroraStack : Control
{
    private const int Banks = 4, Frames = 16, N = 96, HarmMax = 28;

    private static readonly IBrush Lit = NotaPalette.AccentBright;
    private static readonly IBrush Dim = NotaPalette.TextDisabled;
    // Four depth steps from the back of the stack to the front, so the stack reads as
    // depth rather than as sixteen equal lines.
    private static readonly IBrush[] Depth =
    {
        NotaPalette.Wash(NotaPalette.Accent, 0x38), NotaPalette.Wash(NotaPalette.Accent, 0x58),
        NotaPalette.Wash(NotaPalette.Accent, 0x80), NotaPalette.Wash(NotaPalette.Accent, 0xB0),
    };

    private static readonly Pen[] DepthPens = MakePens(Depth);
    private static readonly Pen[] DimPens = MakePens(new[] { Dim, Dim, Dim, Dim });
    private static readonly Pen HotPen = new(Lit, 1.7, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static Pen[] MakePens(IBrush[] brushes)
    {
        var p = new Pen[brushes.Length];
        for (int i = 0; i < brushes.Length; i++) p[i] = new Pen(brushes[i], 1, lineJoin: PenLineJoin.Round);
        return p;
    }

    // [bank][frame][sample] — built on first use, read-only afterwards.
    private static readonly double[][][] Shapes = new double[Banks][][];
    private static readonly object ShapeLock = new();

    private int _bank = -1, _active = -1;
    private bool _dim;

    /// <summary>Space kept clear at the top of the window for a caption drawn over it.</summary>
    public double TopInset { get; set; }

    /// <summary>Greys the whole stack (the oscillator it belongs to is switched off).</summary>
    public bool Dimmed
    {
        get => _dim;
        set { if (_dim == value) return; _dim = value; InvalidateVisual(); }
    }

    public AuroraStack() { ClipToBounds = true; }

    /// <summary>Point the window at a bank and a position. Only a change of bank or of the
    /// frame the position lands on redraws — a knob moving inside one frame does not.</summary>
    public void Set(int bank, double pos)
    {
        int b = Math.Clamp(bank, 0, Banks - 1);
        int a = Math.Clamp((int)Math.Round(Math.Clamp(pos, 0, 1) * (Frames - 1)), 0, Frames - 1);
        if (b == _bank && a == _active) return;
        _bank = b; _active = a;
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

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 2 || h <= 2 || _bank < 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));

        var frames = Bank(_bank);
        // The stack is skewed up-and-right: frame 0 sits bottom-front, frame 15 top-back,
        // sized so every frame's centre ± amplitude stays inside the window.
        const double padX = 9, padY = 7;
        double top = padY + TopInset;
        double waveW = (w - padX * 2) * 0.70, skewX = (w - padX * 2) * 0.30 / (Frames - 1);
        double amp = (h - top - padY) * 0.15;
        double cyBottom = h - padY - amp, cyTop = top + amp;
        if (cyBottom <= cyTop) return;

        var pens = _dim ? DimPens : DepthPens;
        var hot = _dim ? DimPens[^1] : HotPen;

        // Back to front, so a nearer frame overlaps the one behind it.
        for (int f = Frames - 1; f >= 0; f--)
        {
            bool on = f == _active;
            var buf = frames[f];
            double ox = padX + f * skewX;
            double cy = cyBottom - (cyBottom - cyTop) * f / (Frames - 1);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(ox, cy - buf[0] * amp), false);
                for (int j = 1; j <= N; j++)
                    g.LineTo(new Point(ox + (double)j / N * waveW, cy - buf[j % N] * amp));
            }
            ctx.DrawGeometry(null, on ? hot : pens[f * Depth.Length / Frames], geo);
        }
    }
}
