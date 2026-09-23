// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota EQ-8 (device kind 0) response graph, a build of the "Nota EQ-8" mockup. The window
// draws the analyzer behind everything (the input spectrum dim, the output spectrum a step
// brighter — Pre shows the input alone, Post both, Off neither), the selected band's own curve
// as a faint brass line, the Side path as a teal line when any band works on the Side, and
// the main response (stereo — or the Mid when the Side has its own curve) as the brass primary
// curve. Every band is a numbered node at its effective gain (Scale applied) — selected brass,
// a Side band teal, the rest Ink 3, an off band Ink 6; a Mid band wears a brass ring and a
// left / right band a rose one. A tooltip beside the selected node reads the band.
//
// Drag a node — frequency and gain; the wheel over a node — Q (Shift for fine); double-click a
// node turns the band on / off, double-click empty space switches on a free band there as a
// stereo bell; right-click a node — type, slope, channel and on. The maths mirrors Eq.h.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

/// <summary>The EQ-8 parameter layout (mirrors Eq.h) and the maths the card and the graph share.</summary>
internal static class Eq8
{
    public const int Bands = 8, PerBand = 5;
    public const int On = 0, TypeF = 1, FreqF = 2, GainF = 3, QF = 4;
    public const int SlopeBase = 40, ChannelBase = 48, ScaleP = 56, OutputP = 57, AutoGainP = 58, AnalyzerP = 59, ParamCount = 60;
    public const int LowCut = 0, LowShelf = 1, Bell = 2, Notch = 3, HighShelf = 4, HighCut = 5;
    public const int St = 0, Mid = 1, Side = 2, Left = 3, Right = 4;
    public const int AnaPre = 0, AnaPost = 1, AnaOff = 2;
    // Telemetry (Eq::S_* / kTele / kSpec).
    public const int S_InPeak = 0, S_OutPeak = 1, S_AutoGain = 2, S_SampleRate = 3, S_Cpu = 4, S_Analysed = 5, S_Latency = 6,
        kTele = 16, kSpec = 96, PreAt = kTele, PostAt = kTele + kSpec, kScope = kTele + 2 * kSpec;
    public const double Fmin = 20, Fmax = 20000;

    public static readonly string[] TypeNames = DynEq.TypeNames;
    public static readonly string[] TypeShort = DynEq.TypeShort;
    public static readonly string[] TypeSeg = DynEq.TypeSeg;
    public static readonly string[] ChannelNames = { "Stereo", "Mid", "Side", "Left", "Right" };
    public static readonly string[] ChannelSeg = { "St", "Mid", "Side", "L", "R" };
    public static readonly string[] ChannelShort = { "St", "M", "S", "L", "R" };
    public static readonly string[] SlopeSeg = { "12", "24", "48\u2009dB" };
    public static readonly string[] AnalyzerSeg = { "Pre", "Post", "Off" };

    public static bool HasGain(int type) => DynEq.HasGain(type);
    public static bool IsCut(int type) => type is LowCut or HighCut;
    public static int P(int band, int field) => band * PerBand + field;
    public static int SlopeDb(int slope) => 12 << Math.Clamp(slope, 0, 2);

    public static double FreqToN(double f) => DynEq.FreqToN(f);
    public static double NToFreq(double n) => DynEq.NToFreq(n);
    public static double GainToN(double db) => DynEq.GainToN(db);
    public static double NToGain(double n) => DynEq.NToGain(n);
    public static string Hz(double f) => DynEq.Hz(f);
    public static string HzShort(double f) => DynEq.HzShort(f);
    public static string Db(double g) => DynEq.Db(g);

    /// <summary>Ink of a band's channel: Mid brass, Side teal, L / R rose, stereo none.</summary>
    public static IBrush? ChannelInk(int ch, bool bright = false) => ch switch
    {
        Mid => bright ? NotaPalette.AccentBright : NotaPalette.Accent,
        Side => bright ? NotaPalette.TealBright : NotaPalette.Teal,
        Left or Right => bright ? NotaPalette.RoseBright : NotaPalette.Rose,
        _ => null,
    };

    private static readonly double[] Q24 = { 0.54119610, 1.30656296 };
    private static readonly double[] Q48 = { 0.50979558, 0.60134489, 0.89997622, 2.56291545 };

    /// <summary>A band's sections (Eq::cascade): one biquad, or for a 24 / 48 dB cut a
    /// Butterworth cascade whose last section carries the resonance.</summary>
    public static int Cascade(Span<DynEq.Bq> into, int type, int slope, double sr, double f0, double gDb, double q)
    {
        if (IsCut(type) && slope > 0)
        {
            var bq = slope == 1 ? Q24 : Q48;
            double res = q / 0.70710678;
            for (int k = 0; k < bq.Length; k++)
                into[k] = DynEq.Coeffs(type, sr, f0, 0, k == bq.Length - 1 ? Math.Clamp(bq[k] * res, 0.1, 40) : bq[k]);
            return bq.Length;
        }
        into[0] = DynEq.Coeffs(type, sr, f0, gDb, q);
        return 1;
    }

    public static double MagDb(ReadOnlySpan<DynEq.Bq> secs, int n, double w)
    {
        double s = 0;
        for (int k = 0; k < n; k++) s += DynEq.MagDb(secs[k], w);
        return s;
    }
}

public sealed class Eq8Curve : Control
{
    private const int Pts = 150;
    private const double NodeD = 11, HitR = 9;

    private static readonly IPen GridFaint = new Pen(NotaPalette.GridSubBeat, 1);
    private static readonly IPen GridMid = new Pen(NotaPalette.GridBeat, 1);
    private static readonly IPen ZeroPen = new Pen(NotaPalette.TrackOff, 1);
    private static readonly IPen PrePen = new Pen(NotaPalette.BorderDefault, 1);
    private static readonly IPen PreDimPen = new Pen(NotaPalette.Wash(NotaPalette.BorderDefault, 0x99), 1);
    private static readonly IPen PostPen = new Pen(NotaPalette.TextAxis, 1);
    private static readonly IPen SelPen = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0x5A), 1);
    private static readonly IPen SidePen = new Pen(NotaPalette.Wash(NotaPalette.Teal, 0xE6), NotaGraph.SecondaryWidth, lineJoin: PenLineJoin.Round);
    private static readonly IPen RingPen = new Pen(NotaPalette.BgSunken, 2);

    private readonly IAudioEngine _engine;
    private readonly int _track, _device;
    private int _selected, _drag = -1;
    private bool _gestFreq, _gestGain;

    private readonly float[] _pre = new float[Eq8.kSpec];
    private readonly float[] _post = new float[Eq8.kSpec];
    private bool _specOk;

    public event Action? SelectionChanged;
    /// <summary>Raised after a gesture on the graph wrote params (so the card repaints).</summary>
    public event Action? Edited;

    public Eq8Curve(IAudioEngine engine, int track, int device)
    {
        _engine = engine; _track = track; _device = device;
        ClipToBounds = true;
        MinHeight = 60;
        // Open on the first band that shapes the sound (a cut, a notch or a gain), else the first on.
        _selected = -1;
        int firstOn = -1;
        for (int b = 0; b < Eq8.Bands && _selected < 0; b++)
        {
            if (!BandOn(b)) continue;
            if (firstOn < 0) firstOn = b;
            if (!Eq8.HasGain(BandType(b)) || Math.Abs(P(b, Eq8.GainF)) > 0.05f) _selected = b;
        }
        if (_selected < 0) _selected = Math.Max(0, firstOn);
    }

    public int SelectedBand
    {
        get => _selected;
        set { int v = Math.Clamp(value, 0, Eq8.Bands - 1); if (v == _selected) return; _selected = v; SelectionChanged?.Invoke(); InvalidateVisual(); }
    }

    /// <summary>Feed the telemetry read by the card (Eq scopeRead layout).</summary>
    public void Update(float[] scope, int n)
    {
        _specOk = n >= Eq8.kScope && scope[Eq8.S_Analysed] > 0.5f;
        if (_specOk)
        {
            Array.Copy(scope, Eq8.PreAt, _pre, 0, Eq8.kSpec);
            Array.Copy(scope, Eq8.PostAt, _post, 0, Eq8.kSpec);
        }
        InvalidateVisual();
    }

    // ---- params ----
    private float Pd(int p) => p < _engine.DeviceParamCount(_track, _device) ? _engine.DeviceGetParam(_track, _device, p) : 0f;
    private float P(int band, int field) => _engine.DeviceGetParam(_track, _device, Eq8.P(band, field));
    private void SetRaw(int p, double v) => _engine.DeviceSetParam(_track, _device, p, (float)v);
    private void SetP(int band, int field, double v) => SetRaw(Eq8.P(band, field), v);
    private void Gesture(int p, Action a)
    {
        _engine.BeginAutomationWrite(_track, AutomationTarget.DeviceParam, _device, p, "");
        a();
        _engine.EndAutomationWrite(_track, AutomationTarget.DeviceParam, _device, p, "");
    }
    private bool BandOn(int b) => P(b, Eq8.On) > 0.5f;
    private int BandType(int b) => Math.Clamp((int)Math.Round(P(b, Eq8.TypeF)), 0, 5);
    private int Slope(int b) => Math.Clamp((int)Math.Round(Pd(Eq8.SlopeBase + b)), 0, 2);
    private int Chan(int b) => Math.Clamp((int)Math.Round(Pd(Eq8.ChannelBase + b)), 0, 4);
    private double ScaleK => _engine.DeviceParamCount(_track, _device) > Eq8.ScaleP ? Math.Clamp(Pd(Eq8.ScaleP), 0, 200) / 100.0 : 1.0;
    private int Analyzer => _engine.DeviceParamCount(_track, _device) > Eq8.AnalyzerP ? Math.Clamp((int)Math.Round(Pd(Eq8.AnalyzerP)), 0, 2) : Eq8.AnaPost;
    private double Sr => _engine.SampleRate > 0 ? _engine.SampleRate : 48000;

    // ---- geometry: the plot is the whole control ----
    private double X(double f) => Eq8.FreqToN(f) * Bounds.Width;
    private double Y(double db) => Eq8.GainToN(db) * Bounds.Height;
    private Point NodeAt(int b) => new(X(P(b, Eq8.FreqF)), Y(Eq8.HasGain(BandType(b)) ? P(b, Eq8.GainF) * ScaleK : 0));

    private int Hit(Point p)
    {
        int best = -1; double bestD = HitR * HitR;
        for (int k = 0; k < Eq8.Bands; k++)
        {
            int b = (_selected + k) % Eq8.Bands;   // the selected node wins a tie
            var n = NodeAt(b);
            double d = (p.X - n.X) * (p.X - n.X) + (p.Y - n.Y) * (p.Y - n.Y);
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    // The node sits at gain × Scale; dragging it asks for the gain that lands it there.
    private double GainAtY(double y)
    {
        double g = Eq8.NToGain(y / Math.Max(1, Bounds.Height)), s = ScaleK;
        return s > 0.01 ? Math.Clamp(g / s, -18, 18) : 0;
    }

    // ---- interaction ----
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag < 0) { Cursor = Hit(p) >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default; return; }
        SetP(_drag, Eq8.FreqF, Eq8.NToFreq(p.X / Math.Max(1, Bounds.Width)));
        if (_gestGain) SetP(_drag, Eq8.GainF, GainAtY(p.Y));
        Edited?.Invoke();
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        int hit = Hit(p);
        if (props.IsRightButtonPressed)
        {
            if (hit >= 0) { SelectedBand = hit; ShowBandMenu(hit); e.Handled = true; }
            return;
        }
        if (!props.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            if (hit >= 0) Gesture(Eq8.P(hit, Eq8.On), () => SetP(hit, Eq8.On, BandOn(hit) ? 0 : 1));
            else AddBandAt(p);
            Edited?.Invoke(); InvalidateVisual(); e.Handled = true;
            return;
        }
        if (hit < 0) return;
        SelectedBand = hit;
        _drag = hit;
        e.Pointer.Capture(this);
        _gestFreq = true;
        _engine.BeginAutomationWrite(_track, AutomationTarget.DeviceParam, _device, Eq8.P(hit, Eq8.FreqF), "");
        if (Eq8.HasGain(BandType(hit)) && ScaleK > 0.01)
        {
            _gestGain = true;
            _engine.BeginAutomationWrite(_track, AutomationTarget.DeviceParam, _device, Eq8.P(hit, Eq8.GainF), "");
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        EndDrag();   // never leave an automation session dangling
        base.OnPointerCaptureLost(e);
    }

    private void EndDrag()
    {
        if (_drag >= 0)
        {
            if (_gestFreq) _engine.EndAutomationWrite(_track, AutomationTarget.DeviceParam, _device, Eq8.P(_drag, Eq8.FreqF), "");
            if (_gestGain) _engine.EndAutomationWrite(_track, AutomationTarget.DeviceParam, _device, Eq8.P(_drag, Eq8.GainF), "");
        }
        _drag = -1; _gestFreq = _gestGain = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        int b = Hit(e.GetPosition(this));
        if (b < 0) b = _selected;
        SelectedBand = b;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double step = fine ? 1.03 : 1.15;
        double q = Math.Clamp(P(b, Eq8.QF) * (e.Delta.Y > 0 ? step : 1 / step), 0.1, 18);
        Gesture(Eq8.P(b, Eq8.QF), () => SetP(b, Eq8.QF, q));
        Edited?.Invoke(); InvalidateVisual();
        e.Handled = true;
    }

    private void AddBandAt(Point p)
    {
        for (int b = 0; b < Eq8.Bands; b++)
        {
            if (BandOn(b)) continue;
            double f = Eq8.NToFreq(p.X / Math.Max(1, Bounds.Width)), g = GainAtY(p.Y);
            Gesture(Eq8.P(b, Eq8.TypeF), () => SetP(b, Eq8.TypeF, Eq8.Bell));
            Gesture(Eq8.P(b, Eq8.FreqF), () => SetP(b, Eq8.FreqF, f));
            Gesture(Eq8.P(b, Eq8.GainF), () => SetP(b, Eq8.GainF, g));
            Gesture(Eq8.P(b, Eq8.QF), () => SetP(b, Eq8.QF, 1.0));
            if (_engine.DeviceParamCount(_track, _device) > Eq8.ChannelBase + b)
                Gesture(Eq8.ChannelBase + b, () => SetRaw(Eq8.ChannelBase + b, Eq8.St));
            Gesture(Eq8.P(b, Eq8.On), () => SetP(b, Eq8.On, 1));
            SelectedBand = b;
            return;
        }
    }

    private void ShowBandMenu(int b)
    {
        var fly = new MenuFlyout();
        int curT = BandType(b);
        for (int t = 0; t < Eq8.TypeNames.Length; t++)
        {
            int tt = t;
            var mi = new MenuItem { Header = Eq8.TypeNames[t], ToggleType = MenuItemToggleType.Radio, IsChecked = t == curT };
            mi.Click += (_, _) => { Gesture(Eq8.P(b, Eq8.TypeF), () => SetP(b, Eq8.TypeF, tt)); Edited?.Invoke(); InvalidateVisual(); };
            fly.Items.Add(mi);
        }
        bool extended = _engine.DeviceParamCount(_track, _device) >= Eq8.ParamCount;
        if (extended)
        {
            fly.Items.Add(new Separator());
            var slope = new MenuItem { Header = "Slope", IsEnabled = Eq8.IsCut(curT) };
            for (int s = 0; s < 3; s++)
            {
                int ss = s;
                var mi = new MenuItem { Header = NotaNum.F($"{Eq8.SlopeDb(s)}\u2009dB/oct"), ToggleType = MenuItemToggleType.Radio, IsChecked = s == Slope(b) };
                mi.Click += (_, _) => { Gesture(Eq8.SlopeBase + b, () => SetRaw(Eq8.SlopeBase + b, ss)); Edited?.Invoke(); InvalidateVisual(); };
                slope.Items.Add(mi);
            }
            fly.Items.Add(slope);
            var chan = new MenuItem { Header = "Channel" };
            for (int c = 0; c < Eq8.ChannelNames.Length; c++)
            {
                int cc = c;
                var mi = new MenuItem { Header = Eq8.ChannelNames[c], ToggleType = MenuItemToggleType.Radio, IsChecked = c == Chan(b) };
                mi.Click += (_, _) => { Gesture(Eq8.ChannelBase + b, () => SetRaw(Eq8.ChannelBase + b, cc)); Edited?.Invoke(); InvalidateVisual(); };
                chan.Items.Add(mi);
            }
            fly.Items.Add(chan);
        }
        fly.Items.Add(new Separator());
        var on = new MenuItem { Header = "Band on", ToggleType = MenuItemToggleType.CheckBox, IsChecked = BandOn(b) };
        on.Click += (_, _) => { Gesture(Eq8.P(b, Eq8.On), () => SetP(b, Eq8.On, BandOn(b) ? 0 : 1)); Edited?.Invoke(); InvalidateVisual(); };
        fly.Items.Add(on);
        fly.ShowAt(this, showAtPointer: true);
    }

    // ---- render ----
    private static FormattedText Text(string s, IBrush ink, double size, bool bold = false, bool mono = true)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            mono ? (bold ? NotaFonts.MonoBold : NotaFonts.Mono) : bold ? NotaFonts.SansBold : NotaFonts.Sans, size, ink);

    private StreamGeometry SpecLine(float[] spec, double w, double h)
    {
        var sg = new StreamGeometry();
        using var g = sg.Open();
        for (int i = 0; i < Eq8.kSpec; i++)
        {
            double x = (i + 0.5) / Eq8.kSpec * w;
            double y = Math.Clamp(-spec[i] / 80.0, 0, 1) * h;   // 0 dBFS at the top … −80 at the bottom
            if (i == 0) g.BeginFigure(new Point(x, y), false); else g.LineTo(new Point(x, y));
        }
        return sg;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height, sr = Sr;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        using var clip = ctx.PushClip(new RoundedRect(frame.Deflate(1), NotaGraph.Radius));

        // grid: 100 Hz · 1 kHz (stronger) · 10 kHz; ±6 / ±12 dB, 0 dB stronger
        foreach (var f in new[] { 100.0, 10000.0 }) ctx.DrawLine(GridFaint, new Point(X(f), 0), new Point(X(f), h));
        ctx.DrawLine(GridMid, new Point(X(1000), 0), new Point(X(1000), h));
        foreach (var db in new[] { -12.0, -6.0, 6.0, 12.0 }) ctx.DrawLine(GridFaint, new Point(0, Y(db)), new Point(w, Y(db)));
        ctx.DrawLine(ZeroPen, new Point(0, Y(0)), new Point(w, Y(0)));

        // analyzer: Pre — the input; Post — the output over a dimmer input; Off — neither
        int ana = Analyzer;
        if (_specOk && ana != Eq8.AnaOff)
        {
            ctx.DrawGeometry(null, ana == Eq8.AnaPre ? PrePen : PreDimPen, SpecLine(_pre, w, h));
            if (ana == Eq8.AnaPost) ctx.DrawGeometry(null, PostPen, SpecLine(_post, w, h));
        }

        // band coefficients (effective gain = gain × Scale)
        double sc = ScaleK;
        var secs = new DynEq.Bq[Eq8.Bands * 4];
        var nSec = new int[Eq8.Bands];
        var ch = new int[Eq8.Bands];
        bool anySide = false;
        for (int b = 0; b < Eq8.Bands; b++)
        {
            int ty = BandType(b);
            nSec[b] = Eq8.Cascade(secs.AsSpan(b * 4, 4), ty, Slope(b), sr, P(b, Eq8.FreqF), P(b, Eq8.GainF) * sc, P(b, Eq8.QF));
            ch[b] = Chan(b);
            if (BandOn(b) && ch[b] == Eq8.Side) anySide = true;
        }
        int sel = _selected;

        // curves: main (all but Side bands), side (all but Mid bands), the selected band alone
        var gm = new StreamGeometry(); var gs = new StreamGeometry(); var gx = new StreamGeometry();
        using (var cm = gm.Open())
        using (var cs = gs.Open())
        using (var cx = gx.Open())
            for (int i = 0; i < Pts; i++)
            {
                double n = i / (double)(Pts - 1), x = n * w;
                double wv = 2 * Math.PI * Eq8.NToFreq(n) / sr;
                double m = 0, s = 0;
                for (int b = 0; b < Eq8.Bands; b++)
                {
                    if (!BandOn(b)) continue;
                    double v = Eq8.MagDb(secs.AsSpan(b * 4, 4), nSec[b], wv);
                    if (ch[b] != Eq8.Side) m += v;
                    if (ch[b] != Eq8.Mid) s += v;
                }
                var pm = new Point(x, Math.Clamp(Y(m), -2, h + 2));
                var ps = new Point(x, Math.Clamp(Y(s), -2, h + 2));
                var px = new Point(x, Math.Clamp(Y(Eq8.MagDb(secs.AsSpan(sel * 4, 4), nSec[sel], wv)), -2, h + 2));
                if (i == 0) { cm.BeginFigure(pm, false); cs.BeginFigure(ps, false); cx.BeginFigure(px, false); }
                else { cm.LineTo(pm); cs.LineTo(ps); cx.LineTo(px); }
            }
        if (BandOn(sel)) ctx.DrawGeometry(null, SelPen, gx);
        if (anySide) ctx.DrawGeometry(null, SidePen, gs);
        ctx.DrawGeometry(null, NotaGraph.PrimaryPen, gm);

        // axis labels
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "20");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "20k Hz");
        var k1 = NotaGraph.AxisText("1k");
        ctx.DrawText(k1, new Point(X(1000) + 3, h - 4 - k1.Height + 1));
        var p12 = NotaGraph.AxisText("+12", null, 6); var m12 = NotaGraph.AxisText("−12", null, 6);
        ctx.DrawText(p12, new Point(w - 5 - p12.Width, Y(12) - p12.Height - 1));
        ctx.DrawText(m12, new Point(w - 5 - m12.Width, Y(-12) + 1));

        // legend (the ANALYZER selector sits over the top-left corner, placed by the card)
        if (anySide)
            NotaGraph.Legend(ctx, w - 5, 2, ("mid", NotaPalette.AccentBright, NotaGraph.Mark.Line), ("side", NotaPalette.TealBright, NotaGraph.Mark.Line));
        else
            NotaGraph.Legend(ctx, w - 5, 2, ("stereo", NotaPalette.AccentBright, NotaGraph.Mark.Line));

        // nodes (selected last, on top)
        for (int k = 1; k <= Eq8.Bands; k++)
        {
            int b = (sel + k) % Eq8.Bands;
            bool bOn = BandOn(b), isSel = b == sel;
            var c = NodeAt(b);
            IBrush fill = !bOn ? NotaPalette.BorderStrong : isSel ? NotaPalette.AccentBright : ch[b] == Eq8.Side ? NotaPalette.TealBright : NotaPalette.TextSecondary;
            IBrush num = bOn ? NotaPalette.TextOnAccent : NotaPalette.TextTertiary;
            double r = NodeD / 2;
            ctx.DrawEllipse(fill, RingPen, c, r + 1, r + 1);
            if (bOn && ch[b] != Eq8.Side && Eq8.ChannelInk(ch[b]) is { } ring)   // Mid brass, L / R rose
                ctx.DrawEllipse(null, new Pen(ring, 1), c, r + 2.5, r + 2.5);
            var t = Text(NotaNum.F($"{b + 1}"), num, 7, bold: true);
            ctx.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
        }

        DrawTip(ctx, w, h);
    }

    // The selected band's reading beside its node, clear of the node and inside the window.
    private void DrawTip(DrawingContext ctx, double w, double h)
    {
        int b = _selected, ty = BandType(b), chn = Chan(b);
        bool bOn = BandOn(b), g = Eq8.HasGain(ty);
        var a = Text(NotaNum.F($"B{b + 1} {Eq8.TypeNames[ty]}") + (chn != Eq8.St ? " · " + Eq8.ChannelShort[chn] : "") + (bOn ? "" : " · off"),
            NotaPalette.AccentBright, 8, bold: true, mono: false);
        var l2 = Text(Eq8.Hz(P(b, Eq8.FreqF)) + (g ? " · " + Eq8.Db(P(b, Eq8.GainF)) + " dB" : "") + NotaNum.F($" · Q {P(b, Eq8.QF):0.00}")
            + (Eq8.IsCut(ty) ? NotaNum.F($" · {Eq8.SlopeDb(Slope(b))}\u2009dB/oct") : ""), NotaPalette.TextPrimary, 7);
        double tw = Math.Max(a.Width, l2.Width) + 12, th = a.Height + l2.Height + 6;
        var n = NodeAt(b);
        double x = n.X > w * 0.62 ? n.X - 12 - tw : n.X + 12;
        double y = n.Y < h * 0.34 ? n.Y + 8 : n.Y - 8 - th;
        x = Math.Clamp(x, 3, Math.Max(3, w - tw - 3)); y = Math.Clamp(y, 3, Math.Max(3, h - th - 3));
        var box = new Rect(x, y, tw, th);
        ctx.DrawRectangle(NotaPalette.SurfaceCard, new Pen(NotaPalette.BorderStrong, 1), new RoundedRect(box.Deflate(0.5), 3));
        ctx.DrawText(a, new Point(x + 6, y + 3));
        ctx.DrawText(l2, new Point(x + 6, y + 3 + a.Height));
    }
}
