// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the three Nota Synth graph windows, one per tab of the editor. Each
// fills the top of its tab and carries the readouts that tab is about:
//
//   Osc     the oscillator shape over eight cycles, the unison stack drawn behind it
//   Env     the amplitude ADSR with a note-off marker; its breakpoints drag the params
//   Filter  the response curve for the chosen type, a drag pad for cutoff (X) / reso (Y),
//           with the envelope's reach on the cutoff drawn as a teal ghost
//
// All three read the engine's normalized params straight and denormalise them with the
// exact maps Synth.h uses, so the scales are real. Drawn through NotaGraph — well ground,
// hairline frame, corner axis labels, no fills under curves.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class SynthViz : Control, IMidiLearnRegions
{
    public enum K { Osc, Adsr, Filter }

    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush Axis = NotaPalette.TextAxis;
    private static readonly IBrush Ghost = NotaPalette.Wash(NotaPalette.Accent, 0x40);
    private static readonly Typeface Mono = NotaFonts.Mono;

    private const double Pad = 6;

    private readonly K _k;
    private readonly IAudioEngine _e;
    private readonly int _t;
    private readonly Dictionary<string, int> _idx;
    private int _drag = -1;   // Env: 0 attack · 1 decay/sustain · 2 release. Filter: 0 pad.

    public SynthViz(K k, IAudioEngine engine, int track, Dictionary<string, int> idx)
    {
        _k = k; _e = engine; _t = track; _idx = idx;
        MinWidth = 180; MinHeight = 56;
        if (k != K.Osc) Cursor = new Cursor(StandardCursorType.Hand);
    }

    public void Refresh() => InvalidateVisual();

    private int I(string id) => _idx.TryGetValue(id, out var i) ? i : -1;
    private float G(string id) { int i = I(id); return i >= 0 ? _e.PluginParamGet(_t, -1, i) : 0f; }
    private void Set(string id, double v) { int i = I(id); if (i >= 0) _e.PluginParamSet(_t, -1, i, (float)Math.Clamp(v, 0, 1)); }

    // ---- the engine's maps, mirrored (Synth.h) -----------------------------
    private static double ExpMap(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
    /// <summary>A bipolar amount with its sign always shown ("+12", "−8", "0").</summary>
    private static string Signed(double v) => v.ToString("+0;−0;0", NotaNum.Culture);
    private static double Osc(int wave, double ph, double pw) => wave switch
    {
        1 => ph < pw ? 1.0 : -1.0,
        2 => 4.0 * Math.Abs(ph - 0.5) - 1.0,
        3 => Math.Sin(2.0 * Math.PI * ph),
        _ => 2.0 * ph - 1.0,
    };
    private static readonly int[] UnisonCounts = { 1, 2, 4, 7 };
    private int Unison() => UnisonCounts[Math.Clamp((int)Math.Round(G("unison") * 3), 0, 3)];
    private int Wave() => Math.Clamp((int)Math.Round(G("wave") * 3), 0, 3);
    private int FilType() => Math.Clamp((int)Math.Round(G("filtype") * 3), 0, 3);

    private (double x0, double x1, double top, double bot) Geo()
        => (Pad, Math.Max(Pad + 1, Bounds.Width - Pad), Pad + 11, Math.Max(Pad + 12, Bounds.Height - Pad - 9));

    private void Text(DrawingContext ctx, string t, double x, double y, IBrush ink, double size = 8)
        => ctx.DrawText(new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, ink), new Point(x, y));
    private static double MeasureW(string t, double size = 8)
        => new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, NotaPalette.TextPrimary).Width;

    // ---- MIDI learn --------------------------------------------------------
    // The graphs are the only place some of these params can be grabbed, so each drag
    // target gets a region that learns it.
    public IReadOnlyList<(Rect rect, MidiTarget target, string name)> GetMidiLearnRegions()
    {
        var list = new List<(Rect, MidiTarget, string)>(4);
        var (x0, x1, top, bot) = Geo();
        void Add(string id, string name, double fx, double fw)
        {
            int i = I(id); if (i < 0) return;
            double w = (x1 - x0);
            list.Add((new Rect(x0 + w * fx, top, w * fw, bot - top), MidiTarget.PluginParam(_t, -1, i), name));
        }
        if (_k == K.Adsr)
        {
            Add("attack", "Attack", 0, 0.25); Add("decay", "Decay", 0.25, 0.25);
            Add("sustain", "Sustain", 0.5, 0.25); Add("release", "Release", 0.75, 0.25);
        }
        else if (_k == K.Filter)
        {
            Add("cutoff", "Cutoff", 0, 0.5); Add("resonance", "Reso", 0.5, 0.5);
        }
        return list;
    }

    // ---- interaction -------------------------------------------------------
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_k == K.Osc || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        if (_k == K.Filter) _drag = 0;
        else
        {
            var (xa, xd, sy, _, xr) = EnvNodes();
            var (_, _, top, bot) = Geo();
            double dA = Math.Abs(p.X - xa) + Math.Abs(p.Y - top);
            double dDS = Math.Abs(p.X - xd) + Math.Abs(p.Y - sy);
            double dR = Math.Abs(p.X - xr) + Math.Abs(p.Y - bot);
            _drag = (dA <= dDS && dA <= dR) ? 0 : (dDS <= dR ? 1 : 2);
        }
        Gesture(true); e.Pointer.Capture(this); Apply(p); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag >= 0) Apply(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { if (_drag >= 0) { Gesture(false); _drag = -1; e.Pointer.Capture(null); } }

    /// <summary>Raised after a drag writes a param, so the card's readouts follow.</summary>
    public event Action? Changed;

    private void Gesture(bool begin)
    {
        void One(string id)
        {
            if (I(id) < 0) return;
            if (begin) _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, id);
            else _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, id);
        }
        if (_k == K.Filter) { One("cutoff"); One("resonance"); }
        else if (_drag == 0) One("attack");
        else if (_drag == 1) { One("decay"); One("sustain"); }
        else One("release");
    }

    private void Apply(Point p)
    {
        var (x0, x1, top, bot) = Geo();
        double span = Math.Max(1, x1 - x0), height = Math.Max(1, bot - top);
        if (_k == K.Filter)
        {
            Set("cutoff", (p.X - x0) / span);
            Set("resonance", 1 - (p.Y - top) / height);
        }
        else
        {
            double xa = x0 + G("attack") * 0.30 * span;
            if (_drag == 0) Set("attack", (p.X - x0) / (0.30 * span));
            else if (_drag == 1) { Set("decay", (p.X - xa) / (0.25 * span)); Set("sustain", 1 - (p.Y - top) / height); }
            else { double xh = Math.Min(x1, xa + G("decay") * 0.25 * span + 0.20 * span); Set("release", (p.X - xh) / (0.25 * span)); }
        }
        InvalidateVisual();
        Changed?.Invoke();
    }

    private (double xa, double xd, double sy, double xh, double xr) EnvNodes()
    {
        var (x0, x1, top, bot) = Geo();
        double span = x1 - x0;
        double xa = x0 + G("attack") * 0.30 * span;
        double xd = Math.Min(x1, xa + G("decay") * 0.25 * span);
        double xh = Math.Min(x1, xd + 0.20 * span);
        double xr = Math.Min(x1, xh + G("release") * 0.25 * span);
        double sy = top + (1 - G("sustain")) * (bot - top);
        return (xa, xd, sy, xh, xr);
    }

    // ---- drawing -----------------------------------------------------------
    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        NotaGraph.Window(ctx, new Rect(0, 0, w, h));
        var (x0, x1, top, bot) = Geo();
        switch (_k)
        {
            case K.Osc: RenderOsc(ctx, x0, x1, top, bot); break;
            case K.Adsr: RenderEnvelope(ctx, x0, x1, top, bot); break;
            default: RenderFilter(ctx, x0, x1, top, bot); break;
        }
    }

    private static readonly string[] WaveWords = { "saw", "square", "triangle", "sine" };

    private void RenderOsc(DrawingContext ctx, double x0, double x1, double top, double bot)
    {
        double span = x1 - x0, mid = (top + bot) / 2, amp = (bot - top) / 2 - 1;
        var gridPen = new Pen(NotaGraph.Grid, 1);
        ctx.DrawLine(gridPen, new Point(x0, mid), new Point(x1, mid));
        for (int i = 1; i <= 3; i++) { double gx = x0 + span * i / 4.0; ctx.DrawLine(gridPen, new Point(gx, top), new Point(gx, bot)); }

        const int cycles = 8, steps = 480;
        int wave = Wave(), uni = Unison();
        double pw = 0.05 + G("pulsewidth") * 0.90;
        double cents = (G("detune") - 0.5) * 100.0;

        // The unison stack behind the shape it is built from: each copy runs at its own
        // detuned rate, so the drawing shows the beating the ear hears.
        void Curve(double ratio, IPen pen)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (int s = 0; s <= steps; s++)
                {
                    double t = s / (double)steps;
                    double ph = (t * cycles * ratio) % 1.0;
                    var pt = new Point(x0 + t * span, mid - Osc(wave, ph, pw) * amp);
                    if (s == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
                }
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);
        }
        if (uni > 1)
        {
            var ghostPen = new Pen(Ghost, 1.0, lineJoin: PenLineJoin.Round);
            for (int u = 0; u < uni; u++)
            {
                double t = 2.0 * u / (uni - 1) - 1.0;
                if (Math.Abs(t) < 1e-6) continue;                  // the centre copy is the main curve
                Curve(Math.Pow(2.0, cents * t / 1200.0), ghostPen);
            }
        }
        Curve(1.0, new Pen(Brass, NotaGraph.PrimaryWidth, lineJoin: PenLineJoin.Round));

        string left = $"{WaveWords[wave]} · {cycles} cycles · unison {uni}";
        if (wave == 1) left += $" · pw {(pw * 100).ToString("0", NotaNum.Culture)} %";
        Text(ctx, left, x0 + 1, Pad - 3, TextTertiary);
        string right = uni == 1 && Math.Abs(cents) < 0.5 ? "one shape, no detune"
            : $"detune {Signed(cents)} c";
        Text(ctx, right, x1 - 1 - MeasureW(right), Pad - 3, Axis);
    }

    private void RenderEnvelope(DrawingContext ctx, double x0, double x1, double top, double bot)
    {
        var gridPen = new Pen(NotaGraph.Grid, 1);
        for (int i = 1; i <= 3; i++) { double gy = top + (bot - top) * i / 4.0; ctx.DrawLine(gridPen, new Point(x0, gy), new Point(x1, gy)); }

        var (xa, xd, sy, xh, xr) = EnvNodes();
        var pen = new Pen(Brass, NotaGraph.PrimaryWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        ctx.DrawLine(pen, new Point(x0, bot), new Point(xa, top));
        ctx.DrawLine(pen, new Point(xa, top), new Point(xd, sy));
        ctx.DrawLine(pen, new Point(xd, sy), new Point(xh, sy));
        ctx.DrawLine(pen, new Point(xh, sy), new Point(xr, bot));

        // Note-off: where the key is released and the release stage begins.
        ctx.DrawLine(new Pen(Ghost, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) }, new Point(xh, top - 5), new Point(xh, bot));

        NotaGraph.Node(ctx, new Point(xa, top), active: true);
        NotaGraph.Node(ctx, new Point(xd, sy), active: true);
        NotaGraph.Node(ctx, new Point(xr, bot), active: true);

        double a = ExpMap(G("attack"), 0.001, 2.0), d = ExpMap(G("decay"), 0.002, 2.0), r = ExpMap(G("release"), 0.002, 3.0);
        string left = $"{NotaNum.Time(a)} · {NotaNum.Time(d)} · {NotaNum.Db(AudioMath.LinToDb(G("sustain")))} · {NotaNum.Time(r)}";
        Text(ctx, left, x0 + 1, Pad - 3, TextTertiary);
        string right = $"{NotaNum.Time(a + d)} + rel";
        Text(ctx, right, x1 - 1 - MeasureW(right), Pad - 3, Axis);
        // Left of the marker: the release ramp leaves that corner empty, the one to its right not.
        Text(ctx, "note off", Math.Max(x0 + 1, xh - 4 - MeasureW("note off")), bot - 10, Axis);
    }

    private void RenderFilter(DrawingContext ctx, double x0, double x1, double top, double bot)
    {
        double span = x1 - x0;
        var gridPen = new Pen(NotaGraph.Grid, 1);
        for (int i = 1; i <= 2; i++) { double gy = top + (bot - top) * i / 3.0; ctx.DrawLine(gridPen, new Point(x0, gy), new Point(x1, gy)); }
        for (int i = 1; i <= 4; i++) { double gx = x0 + span * i / 5.0; ctx.DrawLine(gridPen, new Point(gx, top), new Point(gx, bot)); }

        int type = FilType();
        double cut = G("cutoff"), res = G("resonance");
        double flatY = top + (bot - top) * 0.34;

        // Off: the signal passes untouched — one flat line and nothing else to read.
        if (type == 0)
        {
            ctx.DrawLine(new Pen(NotaPalette.BorderStrong, NotaGraph.PrimaryWidth), new Point(x0, flatY), new Point(x1, flatY));
            Text(ctx, "filter off · signal passes", x0 + 1, Pad - 3, TextTertiary);
            HzAxis(ctx, x0, x1, bot);
            return;
        }

        // The envelope's reach on the cutoff, drawn as the curve it sweeps to.
        double env = (G("filenv") - 0.5) * 2.0;
        if (Math.Abs(env) > 0.01)
        {
            double modCut = Math.Clamp(cut + env * 4.0 / Math.Log2(18000.0 / 20.0), 0, 1);
            DrawCurve(ctx, x0, x1, top, bot, modCut, res, type, NotaGraph.SecondaryPen(Teal, NotaGraph.SecondaryWidth));
        }
        DrawCurve(ctx, x0, x1, top, bot, cut, res, type, new Pen(Brass, NotaGraph.PrimaryWidth, lineJoin: PenLineJoin.Round));

        double cx = x0 + cut * span;
        ctx.DrawLine(new Pen(Ghost, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) }, new Point(cx, top - 5), new Point(cx, bot));
        NotaGraph.Node(ctx, new Point(cx, CurveTop(top, bot, res)), active: true);

        double hz = ExpMap(cut, 20, 18000);
        string[] names = { "Off", "LP", "HP", "BP" };
        Text(ctx, $"{names[type]} 12 dB/oct · {NotaNum.Hz(hz)} · Q {res.ToString("0.00", NotaNum.Culture)}", x0 + 1, Pad - 3, TextTertiary);
        if (Math.Abs(env) > 0.01)
        {
            string e = $"env {Signed(env * 100)} %";
            Text(ctx, e, x1 - 1 - MeasureW(e), Pad - 3, Teal);
        }
        HzAxis(ctx, x0, x1, bot);
    }

    // Where the curve peaks at the cutoff — the handle sits there for every type.
    private static double CurveTop(double top, double bot, double res)
    { double flat = top + (bot - top) * 0.34; return Math.Max(top + 2, flat - res * (flat - top) * 0.92); }

    // The magnitude curve as a polyline, by filter type (1 LP · 2 HP · 3 BP).
    private static void DrawCurve(DrawingContext ctx, double x0, double x1, double top, double bot,
        double cut, double res, int type, IPen pen)
    {
        double span = x1 - x0, cx = x0 + cut * span;
        double flatY = top + (bot - top) * 0.34;
        double peakY = Math.Max(top + 2, flatY - res * (flatY - top) * 0.92);
        double kx = Math.Max(x0, cx - 14), rx = Math.Min(x1, cx + 14);
        double mid = (peakY + bot) / 2 + (bot - peakY) * 0.15;
        var p = new List<Point>();
        switch (type)
        {
            case 2:  p.Add(new(x0, bot)); p.Add(new((x0 + cx) / 2, mid)); p.Add(new(cx, peakY)); p.Add(new(rx, flatY)); p.Add(new(x1, flatY)); break;
            case 3:  p.Add(new(x0, bot)); p.Add(new(kx, mid)); p.Add(new(cx, peakY)); p.Add(new(rx, mid)); p.Add(new(x1, bot)); break;
            default: p.Add(new(x0, flatY)); p.Add(new(kx, flatY)); p.Add(new(cx, peakY)); p.Add(new((cx + x1) / 2, mid)); p.Add(new(x1, bot)); break;
        }
        for (int i = 1; i < p.Count; i++) ctx.DrawLine(pen, p[i - 1], p[i]);
    }

    private void HzAxis(DrawingContext ctx, double x0, double x1, double bot)
    {
        string[] ticks = { "20", "100", "1 k", "10 k", "20 k" };
        double span = x1 - x0;
        for (int i = 0; i < ticks.Length; i++)
        {
            double tx = x0 + span * i / (ticks.Length - 1);
            if (i == ticks.Length - 1) tx -= MeasureW(ticks[i]);
            else if (i > 0) tx -= MeasureW(ticks[i]) / 2;
            Text(ctx, ticks[i], tx, bot + 1, Axis);
        }
    }
}
