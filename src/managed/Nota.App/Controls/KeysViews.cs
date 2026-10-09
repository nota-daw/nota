// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Keys graphs (instrument kind 16):
//
//   KeysPickupView  the PICKUP transfer curve — the exact function the engine runs
//                   (KeysModel.PickupRaw) — with the tine's travel drawn over it, a dashed
//                   linear reference and the handle: drag X = Symmetry, Y = Distance. L adds
//                   the H1–H8 bars of what the curve does to a sine. For Clav it draws the
//                   string instead, with its two pickups lit by Pickup Position.
//   KeysTremView    the tremolo: the left (brass) and right (teal) gains scrolling with the
//                   engine's LFO, a playhead and the pan dot it puts the sound at.
//   KeysVoiceGrid   the voice pool: held notes in brass, pedal / release tails dim, free
//                   slots dark, the cells past the voice limit darker still.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class KeysPickupView : Control, IMidiLearnRegions
{
    private readonly IAudioEngine _e;
    private readonly int _t;
    private readonly Dictionary<string, int> _idx;
    private bool _drag;

    /// <summary>The mini (S) card's window: no harmonics inset, the readout top-right.</summary>
    public bool Mini { get; init; }

    public event Action? Changed;

    public KeysPickupView(IAudioEngine engine, int track, Dictionary<string, int> idx)
    {
        _e = engine; _t = track; _idx = idx;
        MinWidth = 120; MinHeight = 60;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public void Refresh() => InvalidateVisual();

    private int I(string id) => _idx.TryGetValue(id, out var i) ? i : -1;
    private float G(string id) { int i = I(id); return i >= 0 ? _e.PluginParamGet(_t, -1, i) : 0f; }
    private void Set(string id, double v) { int i = I(id); if (i >= 0) _e.PluginParamSet(_t, -1, i, (float)Math.Clamp(v, 0, 1)); }
    private int Model => KeysModel.Index(G("model"), 4);

    // The plot box: X spans −1..1 of displacement, the handle's Y spans Distance 0..1.
    private Rect Plot() => new(8, 8, Math.Max(10, Bounds.Width - 16), Math.Max(10, Bounds.Height - 16));

    public IReadOnlyList<(Rect rect, MidiTarget target, string name)> GetMidiLearnRegions()
    {
        var list = new List<(Rect, MidiTarget, string)>(2);
        var r = Plot();
        if (Model == 3) return list;
        if (I("sym") is var s and >= 0) list.Add((new Rect(r.X, r.Y, r.Width / 2, r.Height), MidiTarget.PluginParam(_t, -1, s), "Pickup Symmetry"));
        if (I("dist") is var d and >= 0) list.Add((new Rect(r.X + r.Width / 2, r.Y, r.Width / 2, r.Height), MidiTarget.PluginParam(_t, -1, d), "Pickup Distance"));
        return list;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (Model == 3 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _drag = true;
        _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, "sym");
        _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, "dist");
        e.Pointer.Capture(this); Apply(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag) Apply(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_drag) return;
        _drag = false;
        _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, "sym");
        _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, "dist");
        e.Pointer.Capture(null);
    }

    private void Apply(Point p)
    {
        var r = Plot();
        double sym = Math.Clamp((p.X - (r.X + r.Width / 2)) / (r.Width / 2), -1, 1);
        double dist = Math.Clamp((p.Y - r.Y) / r.Height, 0, 1);
        Set("sym", (sym + 1) / 2);
        Set("dist", dist);
        InvalidateVisual();
        Changed?.Invoke();
    }

    private static FormattedText Mono(string t, IBrush ink, double size = 8)
        => new(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Mono, size, ink);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        int model = Model;
        using (ctx.PushClip(new RoundedRect(frame.Deflate(1), NotaGraph.Radius)))
        {
            if (model == 3) RenderString(ctx, frame);
            else RenderCurve(ctx, frame, model);
        }
    }

    private void RenderCurve(DrawingContext ctx, Rect frame, int model)
    {
        var r = Plot();
        double sym = KeysModel.Bipolar(G("sym")), dist = G("dist");
        double norm = KeysModel.PickupNorm(sym, dist, model);
        double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, sx = r.Width / 2, sy = r.Height / 2 * 0.8;
        Point Pt(double x) => new(cx + x * sx, cy - KeysModel.PickupRaw(x, sym, dist, model) / norm * sy);

        // Grid: quarters + the centre cross.
        for (int i = 1; i < 4; i++)
        {
            double x = r.X + r.Width * i / 4;
            ctx.DrawLine(NotaGraph.GridPen, new Point(x, frame.Y + 1), new Point(x, frame.Bottom - 1));
        }
        ctx.DrawLine(NotaGraph.GridPen, new Point(frame.X + 1, cy), new Point(frame.Right - 1, cy));
        // Linear reference.
        ctx.DrawLine(new Pen(NotaPalette.TextAxis, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) },
            new Point(cx - sx, cy + sy), new Point(cx + sx, cy - sy));

        // The curve, and the tine's travel at a full-velocity note over it.
        var curve = new StreamGeometry();
        using (var g = curve.Open())
        {
            for (int i = 0; i <= 80; i++) { var p = Pt(-1 + i / 40.0); if (i == 0) g.BeginFigure(p, false); else g.LineTo(p); }
            g.EndFigure(false);
        }
        var travel = new StreamGeometry();
        using (var g = travel.Open())
        {
            for (int i = 0; i <= 40; i++) { var p = Pt(-0.72 + 1.44 * i / 40.0); if (i == 0) g.BeginFigure(p, false); else g.LineTo(p); }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(NotaPalette.Accent, NotaGraph.PrimaryWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), curve);
        ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.TealBright, 0x80), 4.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), travel);

        // The handle: X = symmetry, Y = distance, with its crosshair.
        double hx = cx + sym * sx, hy = r.Y + dist * r.Height;
        var dash = new Pen(NotaPalette.Wash(NotaPalette.Accent, 0xA0), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        ctx.DrawLine(dash, new Point(hx, frame.Y + 1), new Point(hx, frame.Bottom - 1));
        ctx.DrawLine(dash, new Point(frame.X + 1, hy), new Point(frame.Right - 1, hy));
        NotaGraph.Node(ctx, new Point(hx, hy), true);

        // Captions.
        string read = $"sym {(sym >= 0 ? "+" : "−")}{Math.Round(Math.Abs(sym) * 100)}\u2009% · dist {Math.Round(dist * 100)}\u2009%";
        var title = Mono($"PICKUP · {KeysModel.ModelNames[model].ToUpperInvariant()}", NotaPalette.TextTertiary);
        var readout = Mono(read, NotaPalette.AccentBright);
        if (Mini)
        {
            ctx.DrawText(title, new Point(6, 4));
            ctx.DrawText(readout, new Point(frame.Right - 6 - readout.Width, 4));
            return;
        }
        ctx.DrawText(title, new Point(6, 4));
        ctx.DrawText(readout, new Point(6, 15));

        // H1–H8 of the curve driven by a sine (brass even, teal odd, the fundamental neutral).
        var mags = KeysModel.Harmonics(sym, dist, model);
        double bw = 6, gap = 3, ih = 34, iw = 8 * bw + 7 * gap + 10;
        var inset = new Rect(frame.Right - iw - 8, frame.Bottom - ih - 6, iw, ih);
        ctx.DrawRectangle(NotaGraph.Ground, NotaGraph.FramePen, new RoundedRect(inset, 3));
        ctx.DrawText(Mono("H1–H8", NotaPalette.TextAxis, 7), new Point(inset.X + 4, inset.Y + 2));
        for (int k = 0; k < 8; k++)
        {
            double db = 20 * Math.Log10(mags[k] / Math.Max(1e-9, mags[0]) + 1e-9);
            double bh = Math.Max(1, (1 + db / 60) * (ih - 14));
            IBrush ink = k == 0 ? NotaPalette.TextSecondary : k % 2 == 1 ? NotaPalette.TealBright : NotaPalette.Accent;
            double x = inset.X + 5 + k * (bw + gap);
            ctx.DrawRectangle(ink, null, new Rect(x, inset.Bottom - 3 - bh, bw, bh));
        }
    }

    private void RenderString(DrawingContext ctx, Rect frame)
    {
        double x0 = frame.X + 24, x1 = frame.Right - 24, cy = frame.Y + frame.Height * 0.55;
        double amp = frame.Height * 0.2;
        double X(double t) => x0 + t * (x1 - x0);
        ctx.DrawLine(NotaGraph.GridPen, new Point(frame.X + 1, cy), new Point(frame.Right - 1, cy));
        StreamGeometry Shape(double sign)
        {
            var geo = new StreamGeometry();
            using var g = geo.Open();
            for (int i = 0; i <= 48; i++)
            {
                double t = i / 48.0, env = Math.Sin(Math.PI * t) * (1 - 0.65 * Math.Exp(-t * 6));
                var p = new Point(X(t), cy - sign * amp * env * (sign > 0 ? 1 : 0.7));
                if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
            }
            g.EndFigure(false);
            return geo;
        }
        ctx.DrawGeometry(null, new Pen(NotaPalette.Accent, NotaGraph.PrimaryWidth, lineJoin: PenLineJoin.Round), Shape(1));
        ctx.DrawGeometry(null, new Pen(NotaPalette.Wash(NotaPalette.TealBright, 0xB0), 1.2, lineJoin: PenLineJoin.Round), Shape(-1));
        // Bridge / nut.
        ctx.DrawRectangle(NotaPalette.TextSecondary, null, new Rect(x0 - 3, cy - 8, 3, 16));
        ctx.DrawRectangle(NotaPalette.TextSecondary, null, new Rect(x1, cy - 8, 3, 16));
        // The two pickups.
        int pos = KeysModel.Index(G("pupos"), 3);
        void Pickup(double t, string label, bool lit)
        {
            var rr = new Rect(X(t) - 8, cy + amp + 6, 16, 9);
            ctx.DrawRectangle(lit ? NotaPalette.Accent : NotaPalette.BorderStrong, null, new RoundedRect(rr, 2));
            var ft = Mono(label, lit ? NotaPalette.AccentBright : NotaPalette.TextAxis, 7);
            ctx.DrawText(ft, new Point(X(t) - ft.Width / 2, rr.Bottom + 2));
        }
        Pickup(0.2, "UP", pos != 2);
        Pickup(0.34, "LO", pos != 0);
        ctx.DrawText(Mono("STRING · 2 PICKUPS", NotaPalette.TextTertiary), new Point(6, 4));
        var read = Mono(KeysModel.PickupPositions[pos], NotaPalette.AccentBright);
        if (Mini) ctx.DrawText(read, new Point(frame.Right - 6 - read.Width, 4));
        else ctx.DrawText(read, new Point(6, 15));
    }
}

internal sealed class KeysTremView : Control
{
    private readonly Func<string, float> _get;   // a Keys param by id
    private double _phase;

    public KeysTremView(Func<string, float> get) { _get = get; MinHeight = 40; MinWidth = 100; }

    /// <summary>The engine's tremolo LFO phase (0..1), from the scope.</summary>
    public void SetPhase(double ph) { if (Math.Abs(ph - _phase) > 1e-4) { _phase = ph; InvalidateVisual(); } }

    private static double Shape(double p) => Math.Tanh(2.2 * Math.Sin(p)) / Math.Tanh(2.2);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        bool on = _get("tremon") >= 0.5f, stereo = _get("tremmode") >= 0.5f;
        double depth = _get("tremdepth") * (on ? 1 : 0.15);
        double barH = 12, cy = (h - barH) / 2 + 2, a = (h - barH) / 2 - 8;
        using (ctx.PushClip(new RoundedRect(frame.Deflate(1), NotaGraph.Radius)))
        {
            ctx.DrawLine(NotaGraph.GridPen, new Point(1, cy), new Point(w - 1, cy));
            StreamGeometry Line(double sign)
            {
                var geo = new StreamGeometry();
                using var g = geo.Open();
                for (int i = 0; i <= 96; i++)
                {
                    double t = i / 96.0, p = 2 * Math.PI * (t * 2.25 + _phase);
                    var pt = new Point(4 + t * (w - 8), cy - sign * Shape(p) * a * depth);
                    if (i == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
                }
                g.EndFigure(false);
                return geo;
            }
            var ink = on ? NotaPalette.Accent : NotaPalette.BorderStrong;
            if (stereo) ctx.DrawGeometry(null, new Pen(on ? NotaPalette.Teal : NotaPalette.BorderStrong, NotaGraph.SecondaryWidth, lineJoin: PenLineJoin.Round), Line(-1));
            ctx.DrawGeometry(null, new Pen(ink, NotaGraph.PrimaryWidth, lineJoin: PenLineJoin.Round), Line(1));
            ctx.DrawLine(new Pen(NotaPalette.TextAxis, 1), new Point(w * 0.33, 2), new Point(w * 0.33, h - barH - 2));
            ctx.DrawText(NotaGraph.AxisText(stereo ? "L R" : "MONO"), new Point(6, 3));

            // Where the sound sits: the pan dot (stereo) or the level (mono) at the playhead.
            double s = Shape(2 * Math.PI * (0.33 * 2.25 + _phase)) * _get("tremdepth");
            var bar = new Rect(8, h - barH + 1, w - 16, 6);
            ctx.DrawRectangle(NotaPalette.BgSunken, NotaGraph.FramePen, new RoundedRect(bar, 3));
            double pos = stereo && on ? 0.5 + s * 0.5 : 0.5;
            double px = bar.X + pos * bar.Width;
            ctx.DrawEllipse(on ? NotaPalette.AccentBright : NotaPalette.TextAxis, null, new Point(px, bar.Y + bar.Height / 2), 3, 3);
        }
    }
}

internal sealed class KeysVoiceGrid : Control
{
    private int _limit = 32, _held, _tail;
    private const int Cols = 16, Rows = 4;

    public KeysVoiceGrid() { Height = Rows * 6 - 1; }

    public void Set(int limit, int held, int tail)
    {
        if (limit == _limit && held == _held && tail == _tail) return;
        _limit = limit; _held = held; _tail = tail;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double cw = Math.Floor((Bounds.Width + 1) / Cols) - 1;
        if (cw <= 0) return;
        for (int i = 0; i < Cols * Rows; i++)
        {
            int c = i % Cols, r = i / Cols;
            IBrush ink = i >= _limit ? NotaPalette.BgSunken
                : i < _held ? NotaPalette.Accent
                : i < _held + _tail ? NotaPalette.AccentDeep
                : NotaPalette.BorderDefault;
            ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(c * (cw + 1), r * 6, cw, 5), 1));
        }
    }
}
