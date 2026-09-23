// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Dynamic EQ-8 (device kind 13) response graph, a build of the "Nota Dynamic EQ" mockup.
// A dynamic EQ has two answers at once — where a band sits and where it is right now — so the
// window draws the live response (static gain + each band's momentary dynamic gain) as the
// brass primary curve and the static response as a teal dashed line, shown only while the two
// differ. Under them: the output spectrum (the engine's FFT, a dim line) and the selected
// band's own curve (a faint brass line). Every band is a numbered node — selected brass, the
// rest Ink 3, an off band Ink 6. A dynamic band grows a whisker from its node to where Range
// can take it (teal for a cut, rose for a boost), with a dot riding it at the gain it has now.
// A tooltip beside the selected node reads the band, and for a dynamic band its gain now with
// a 2-second history of it.
//
// Drag a node — frequency and gain; the wheel over a node — Q; double-click a node turns the
// band on / off, double-click empty space switches on a free band there as a bell;
// right-click a node — type, mode, on and solo. The magnitude maths mirrors DynamicEq.h.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

/// <summary>The Dynamic EQ-8 parameter layout (mirrors DynamicEq.h) and the maths the card and
/// the graph share.</summary>
internal static class DynEq
{
    public const int Bands = 8, PerBand = 10;
    public const int On = 0, TypeF = 1, FreqF = 2, GainF = 3, QF = 4, ModeF = 5, ThrF = 6, RangeF = 7, AtkF = 8, RelF = 9;
    public const int OutputP = 80, SidechainP = 81, SoloP = 82, DynamicsP = 83, KeyBase = 84, ParamCount = 92;
    public const int LowCut = 0, LowShelf = 1, Bell = 2, Notch = 3, HighShelf = 4, HighCut = 5;
    public const int Static = 0, Duck = 1, Lift = 2;
    // Telemetry (DynamicEq::S_* / kTele / kSpec).
    public const int S_Gain = 0, S_Level = 8, S_SampleRate = 16, S_Cpu = 17, S_Analysed = 18, S_InPeak = 19, S_OutPeak = 20,
        S_KeyLive = 21, S_Latency = 23, kTele = 32, kSpec = 96, kScope = kTele + kSpec;
    public const double Fmin = 20, Fmax = 20000;

    public static readonly string[] TypeNames = { "Low cut", "Low shelf", "Bell", "Notch", "High shelf", "High cut" };
    public static readonly string[] TypeShort = { "HP", "LS", "Bell", "Nt", "HS", "LP" };
    public static readonly string[] TypeSeg = { "HP", "LS", "Bell", "Ntch", "HS", "LP" };
    // Segment order HP · LS · Bell · Ntch · HS · LP is the type order itself.
    public static readonly string[] ModeNames = { "Static", "Duck", "Lift" };

    public static bool HasGain(int type) => type is LowShelf or Bell or HighShelf;
    public static int P(int band, int field) => band * PerBand + field;

    public static double FreqToN(double f) => Math.Log(Math.Clamp(f, Fmin, Fmax) / Fmin) / Math.Log(Fmax / Fmin);
    public static double NToFreq(double n) => Fmin * Math.Pow(Fmax / Fmin, Math.Clamp(n, 0, 1));
    /// <summary>Gain → 0..1 from the top: ±18 dB span 5 % … 95 % of the height (the mockup's yF).</summary>
    public static double GainToN(double db) => 0.5 - Math.Clamp(db, -20, 20) / 18.0 * 0.45;
    public static double NToGain(double n) => Math.Clamp((0.5 - n) / 0.45 * 18.0, -18, 18);

    public static string Hz(double f) => f < 1000 ? NotaNum.F($"{f:0} Hz") : NotaNum.F($"{f / 1000:0.0} kHz");
    public static string HzShort(double f) => f < 1000 ? NotaNum.F($"{f:0}") : f < 10000 ? NotaNum.F($"{f / 1000:0.0}k") : NotaNum.F($"{f / 1000:0}k");
    public static string Db(double g) => NotaNum.F($"{g:+0.0;−0.0;0.0}");
    public static string Ms(double v) => v < 10 ? NotaNum.F($"{v:0.0} ms") : NotaNum.F($"{v:0} ms");

    public record struct Bq(double B0, double B1, double B2, double A1, double A2);

    public static Bq Coeffs(int type, double sr, double f0, double gDb, double q)
    {
        double w0 = 2 * Math.PI * Math.Clamp(f0, 20, sr * 0.49) / sr, cw = Math.Cos(w0), sw = Math.Sin(w0);
        double a = sw / (2 * Math.Max(q, 0.1));
        static Bq N(double b0, double b1, double b2, double a0, double a1, double a2) => new(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
        switch (type)
        {
            case LowCut: return N((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + a, -2 * cw, 1 - a);
            case HighCut: return N((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + a, -2 * cw, 1 - a);
            case Notch: return N(1, -2 * cw, 1, 1 + a, -2 * cw, 1 - a);
            case LowShelf:
            {
                double A = Math.Pow(10, gDb / 40), al = sw / 2 * Math.Sqrt(2), t = 2 * Math.Sqrt(A) * al;
                return N(A * ((A + 1) - (A - 1) * cw + t), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - t),
                         (A + 1) + (A - 1) * cw + t, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - t);
            }
            case HighShelf:
            {
                double A = Math.Pow(10, gDb / 40), al = sw / 2 * Math.Sqrt(2), t = 2 * Math.Sqrt(A) * al;
                return N(A * ((A + 1) + (A - 1) * cw + t), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - t),
                         (A + 1) - (A - 1) * cw + t, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - t);
            }
            default:
            {
                double A = Math.Pow(10, gDb / 40);
                return N(1 + a * A, -2 * cw, 1 - a * A, 1 + a / A, -2 * cw, 1 - a / A);
            }
        }
    }

    public static double MagDb(in Bq k, double w)
    {
        double cw = Math.Cos(w), sw = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
        double nr = k.B0 + k.B1 * cw + k.B2 * c2, ni = -(k.B1 * sw + k.B2 * s2);
        double dr = 1 + k.A1 * cw + k.A2 * c2, di = -(k.A1 * sw + k.A2 * s2);
        return 10 * Math.Log10(Math.Max(1e-12, (nr * nr + ni * ni) / Math.Max(1e-12, dr * dr + di * di)));
    }
}

public sealed class DynamicEqCurve : Control
{
    public const int HistLen = 120;   // 2 s at 60 Hz
    private const int Pts = 150;
    private const double NodeD = 11, HitR = 9;

    private static readonly IPen GridFaint = new Pen(NotaPalette.GridSubBeat, 1);
    private static readonly IPen GridMid = new Pen(NotaPalette.GridBeat, 1);
    private static readonly IPen ZeroPen = new Pen(NotaPalette.TrackOff, 1);
    private static readonly IPen SpecPen = new Pen(NotaPalette.BorderStrong, 1);
    private static readonly IPen SelPen = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0x5A), 1);
    private static readonly IPen StaticPen = new Pen(NotaPalette.TealBright, 1.2) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
    private static readonly IPen RingPen = new Pen(NotaPalette.BgSunken, 2);

    private readonly IAudioEngine _engine;
    private readonly int _track, _device;
    private int _selected = 4, _drag = -1;
    private bool _gestFreq, _gestGain;

    private readonly float[] _gr = new float[DynEq.Bands];
    private readonly float[] _lvl = new float[DynEq.Bands];
    private readonly float[] _spec = new float[DynEq.kSpec];
    private bool _specOk;
    private readonly float[] _hist = new float[DynEq.Bands * HistLen];
    private int _histHead;

    public event Action? SelectionChanged;
    /// <summary>Raised after a gesture on the graph wrote params (so the card repaints).</summary>
    public event Action? Edited;

    public DynamicEqCurve(IAudioEngine engine, int track, int device)
    {
        _engine = engine; _track = track; _device = device;
        ClipToBounds = true;
        MinHeight = 60;
        for (int b = 0; b < DynEq.Bands; b++) _lvl[b] = -120;
    }

    public int SelectedBand
    {
        get => _selected;
        set { int v = Math.Clamp(value, 0, DynEq.Bands - 1); if (v == _selected) return; _selected = v; SelectionChanged?.Invoke(); InvalidateVisual(); }
    }
    public float Gr(int b) => b is >= 0 and < DynEq.Bands ? _gr[b] : 0f;
    public float Level(int b) => b is >= 0 and < DynEq.Bands ? _lvl[b] : -120f;

    /// <summary>Feed the telemetry read by the card (DynamicEq scopeRead layout).</summary>
    public void Update(float[] scope, int n)
    {
        for (int b = 0; b < DynEq.Bands; b++)
        {
            _gr[b] = n > DynEq.S_Gain + b ? scope[DynEq.S_Gain + b] : 0f;
            _lvl[b] = n > DynEq.S_Level + b ? scope[DynEq.S_Level + b] : -120f;
        }
        _specOk = n >= DynEq.kScope && scope[DynEq.S_Analysed] > 0.5f;
        if (_specOk) Array.Copy(scope, DynEq.kTele, _spec, 0, DynEq.kSpec);
        _histHead = (_histHead + 1) % HistLen;
        for (int b = 0; b < DynEq.Bands; b++) _hist[b * HistLen + _histHead] = IsDyn(b) ? _gr[b] : 0f;
        InvalidateVisual();
    }

    // ---- params ----
    private float P(int band, int field) => _engine.DeviceGetParam(_track, _device, DynEq.P(band, field));
    private void SetP(int band, int field, double v) => _engine.DeviceSetParam(_track, _device, DynEq.P(band, field), (float)v);
    private void Gesture(int p, Action a)
    {
        _engine.BeginAutomationWrite(_track, AutomationTarget.DeviceParam, _device, p, "");
        a();
        _engine.EndAutomationWrite(_track, AutomationTarget.DeviceParam, _device, p, "");
    }
    private bool BandOn(int b) => P(b, DynEq.On) > 0.5f;
    private int BandType(int b) => Math.Clamp((int)Math.Round(P(b, DynEq.TypeF)), 0, 5);
    private int BandMode(int b) => Math.Clamp((int)Math.Round(P(b, DynEq.ModeF)), 0, 2);
    private bool DynMaster => _engine.DeviceGetParam(_track, _device, DynEq.DynamicsP) >= 0.5f;
    private int SoloBand => Math.Clamp((int)Math.Round(_engine.DeviceGetParam(_track, _device, DynEq.SoloP)), 0, DynEq.Bands) - 1;
    private bool IsDyn(int b) => DynMaster && BandOn(b) && DynEq.HasGain(BandType(b)) && BandMode(b) != DynEq.Static;
    private double Sr => _engine.SampleRate > 0 ? _engine.SampleRate : 48000;

    // ---- geometry: the plot is the whole control ----
    private double X(double f) => DynEq.FreqToN(f) * Bounds.Width;
    private double Y(double db) => DynEq.GainToN(db) * Bounds.Height;
    private Point NodeAt(int b) => new(X(P(b, DynEq.FreqF)), Y(DynEq.HasGain(BandType(b)) ? P(b, DynEq.GainF) : 0));

    private int Hit(Point p)
    {
        int best = -1; double bestD = HitR * HitR;
        for (int k = 0; k < DynEq.Bands; k++)
        {
            int b = (_selected + k) % DynEq.Bands;   // the selected node wins a tie
            var n = NodeAt(b);
            double d = (p.X - n.X) * (p.X - n.X) + (p.Y - n.Y) * (p.Y - n.Y);
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    // ---- interaction ----
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag < 0) { Cursor = Hit(p) >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default; return; }
        SetP(_drag, DynEq.FreqF, DynEq.NToFreq(p.X / Math.Max(1, Bounds.Width)));
        if (DynEq.HasGain(BandType(_drag))) SetP(_drag, DynEq.GainF, DynEq.NToGain(p.Y / Math.Max(1, Bounds.Height)));
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
            if (hit >= 0) Gesture(DynEq.P(hit, DynEq.On), () => SetP(hit, DynEq.On, BandOn(hit) ? 0 : 1));
            else AddBandAt(p);
            Edited?.Invoke(); InvalidateVisual(); e.Handled = true;
            return;
        }
        if (hit < 0) return;
        SelectedBand = hit;
        _drag = hit;
        e.Pointer.Capture(this);
        _gestFreq = true;
        _engine.BeginAutomationWrite(_track, AutomationTarget.DeviceParam, _device, DynEq.P(hit, DynEq.FreqF), "");
        if (DynEq.HasGain(BandType(hit)))
        {
            _gestGain = true;
            _engine.BeginAutomationWrite(_track, AutomationTarget.DeviceParam, _device, DynEq.P(hit, DynEq.GainF), "");
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
            if (_gestFreq) _engine.EndAutomationWrite(_track, AutomationTarget.DeviceParam, _device, DynEq.P(_drag, DynEq.FreqF), "");
            if (_gestGain) _engine.EndAutomationWrite(_track, AutomationTarget.DeviceParam, _device, DynEq.P(_drag, DynEq.GainF), "");
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
        double q = Math.Clamp(P(b, DynEq.QF) * (e.Delta.Y > 0 ? step : 1 / step), 0.1, 18);
        Gesture(DynEq.P(b, DynEq.QF), () => SetP(b, DynEq.QF, q));
        Edited?.Invoke(); InvalidateVisual();
        e.Handled = true;
    }

    private void AddBandAt(Point p)
    {
        for (int b = 0; b < DynEq.Bands; b++)
        {
            if (BandOn(b)) continue;
            double f = DynEq.NToFreq(p.X / Math.Max(1, Bounds.Width)), g = DynEq.NToGain(p.Y / Math.Max(1, Bounds.Height));
            Gesture(DynEq.P(b, DynEq.TypeF), () => SetP(b, DynEq.TypeF, DynEq.Bell));
            Gesture(DynEq.P(b, DynEq.FreqF), () => SetP(b, DynEq.FreqF, f));
            Gesture(DynEq.P(b, DynEq.GainF), () => SetP(b, DynEq.GainF, g));
            Gesture(DynEq.P(b, DynEq.QF), () => SetP(b, DynEq.QF, 1.0));
            Gesture(DynEq.P(b, DynEq.ModeF), () => SetP(b, DynEq.ModeF, DynEq.Static));
            Gesture(DynEq.P(b, DynEq.On), () => SetP(b, DynEq.On, 1));
            SelectedBand = b;
            return;
        }
    }

    private void ShowBandMenu(int b)
    {
        var fly = new MenuFlyout();
        int curT = BandType(b);
        for (int t = 0; t < DynEq.TypeNames.Length; t++)
        {
            int tt = t;
            var mi = new MenuItem { Header = DynEq.TypeNames[t], ToggleType = MenuItemToggleType.Radio, IsChecked = t == curT };
            mi.Click += (_, _) => { Gesture(DynEq.P(b, DynEq.TypeF), () => SetP(b, DynEq.TypeF, tt)); Edited?.Invoke(); InvalidateVisual(); };
            fly.Items.Add(mi);
        }
        fly.Items.Add(new Separator());
        int curM = BandMode(b);
        string[] modeWords = { "Static", "Duck — acts above the threshold", "Lift — acts below the threshold" };
        for (int m = 0; m < 3; m++)
        {
            int mm = m;
            var mi = new MenuItem { Header = modeWords[m], ToggleType = MenuItemToggleType.Radio, IsChecked = m == curM, IsEnabled = DynEq.HasGain(curT) || m == 0 };
            mi.Click += (_, _) => { SetMode(_engine, _track, _device, b, mm); Edited?.Invoke(); InvalidateVisual(); };
            fly.Items.Add(mi);
        }
        fly.Items.Add(new Separator());
        var on = new MenuItem { Header = "Band on", ToggleType = MenuItemToggleType.CheckBox, IsChecked = BandOn(b) };
        on.Click += (_, _) => { Gesture(DynEq.P(b, DynEq.On), () => SetP(b, DynEq.On, BandOn(b) ? 0 : 1)); Edited?.Invoke(); InvalidateVisual(); };
        fly.Items.Add(on);
        var solo = new MenuItem { Header = "Solo", ToggleType = MenuItemToggleType.CheckBox, IsChecked = SoloBand == b };
        solo.Click += (_, _) =>
        {
            float v = SoloBand == b ? 0 : b + 1;
            Gesture(DynEq.SoloP, () => _engine.DeviceSetParam(_track, _device, DynEq.SoloP, v));
            Edited?.Invoke(); InvalidateVisual();
        };
        fly.Items.Add(solo);
        fly.ShowAt(this, showAtPointer: true);
    }

    /// <summary>Switch a band's mode. Duck seeds a cut and Lift a boost (6 dB when Range is
    /// empty); a range already flipped the other way (upward expansion, downward expansion) is
    /// kept by magnitude only when the mode actually changes.</summary>
    internal static void SetMode(IAudioEngine engine, int track, int device, int band, int mode)
    {
        int pm = DynEq.P(band, DynEq.ModeF), pr = DynEq.P(band, DynEq.RangeF);
        int cur = Math.Clamp((int)Math.Round(engine.DeviceGetParam(track, device, pm)), 0, 2);
        if (mode != DynEq.Static && mode != cur)
        {
            float r = Math.Abs(engine.DeviceGetParam(track, device, pr));
            if (r < 0.05f) r = 6f;
            float nr = mode == DynEq.Duck ? -r : r;
            engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, device, pr, "");
            engine.DeviceSetParam(track, device, pr, nr);
            engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, device, pr, "");
        }
        engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, device, pm, "");
        engine.DeviceSetParam(track, device, pm, mode);
        engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, device, pm, "");
    }

    // ---- render ----
    private static FormattedText Text(string s, IBrush ink, double size, bool bold = false, bool mono = true)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            mono ? (bold ? NotaFonts.MonoBold : NotaFonts.Mono) : bold ? NotaFonts.SansBold : NotaFonts.Sans, size, ink);

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

        // band state + coefficients
        int solo = SoloBand;
        var on = new bool[DynEq.Bands];
        var live = new DynEq.Bq[DynEq.Bands];
        var stat = new DynEq.Bq[DynEq.Bands];
        for (int b = 0; b < DynEq.Bands; b++)
        {
            on[b] = solo >= 0 ? b == solo : BandOn(b);
            if (!on[b]) continue;
            int ty = BandType(b);
            double f = P(b, DynEq.FreqF), g = P(b, DynEq.GainF), q = P(b, DynEq.QF);
            stat[b] = DynEq.Coeffs(ty, sr, f, g, q);
            live[b] = IsDyn(b) ? DynEq.Coeffs(ty, sr, f, g + _gr[b], q) : stat[b];
        }
        int sel = _selected;
        int selT = BandType(sel);
        var selK = DynEq.Coeffs(selT, sr, P(sel, DynEq.FreqF), P(sel, DynEq.GainF) + (IsDyn(sel) ? _gr[sel] : 0), P(sel, DynEq.QF));

        // spectrum (output, 0 dBFS at the top … −80 at the bottom)
        if (_specOk)
        {
            var sg = new StreamGeometry();
            using (var g = sg.Open())
                for (int i = 0; i < DynEq.kSpec; i++)
                {
                    double x = (i + 0.5) / DynEq.kSpec * w;
                    double y = Math.Clamp(-_spec[i] / 80.0, 0, 1) * h;
                    if (i == 0) g.BeginFigure(new Point(x, y), false); else g.LineTo(new Point(x, y));
                }
            ctx.DrawGeometry(null, SpecPen, sg);
        }

        // curves
        var gl = new StreamGeometry(); var gs = new StreamGeometry(); var gx = new StreamGeometry();
        double diff = 0;
        using (var cl = gl.Open())
        using (var cs = gs.Open())
        using (var cx = gx.Open())
            for (int i = 0; i < Pts; i++)
            {
                double n = i / (double)(Pts - 1), x = n * w;
                double wv = 2 * Math.PI * DynEq.NToFreq(n) / sr;
                double l = 0, s = 0;
                for (int b = 0; b < DynEq.Bands; b++)
                {
                    if (!on[b]) continue;
                    l += DynEq.MagDb(live[b], wv);
                    s += DynEq.MagDb(stat[b], wv);
                }
                diff = Math.Max(diff, Math.Abs(l - s));
                var pl = new Point(x, Math.Clamp(Y(l), -2, h + 2));
                var ps = new Point(x, Math.Clamp(Y(s), -2, h + 2));
                var px = new Point(x, Math.Clamp(Y(DynEq.MagDb(selK, wv)), -2, h + 2));
                if (i == 0) { cl.BeginFigure(pl, false); cs.BeginFigure(ps, false); cx.BeginFigure(px, false); }
                else { cl.LineTo(pl); cs.LineTo(ps); cx.LineTo(px); }
            }
        if (BandOn(sel)) ctx.DrawGeometry(null, SelPen, gx);
        if (diff > 0.05) ctx.DrawGeometry(null, StaticPen, gs);
        ctx.DrawGeometry(null, NotaGraph.PrimaryPen, gl);

        // axis labels
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "20");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "20k Hz");
        var k1 = NotaGraph.AxisText("1k");
        ctx.DrawText(k1, new Point(X(1000) + 3, h - 4 - k1.Height + 1));
        var p12 = NotaGraph.AxisText("+12", null, 6); var m12 = NotaGraph.AxisText("−12", null, 6);
        ctx.DrawText(p12, new Point(w - 5 - p12.Width, Y(12) - p12.Height - 1));
        ctx.DrawText(m12, new Point(w - 5 - m12.Width, Y(-12) + 1));

        // title + legend
        var title = solo >= 0 ? NotaNum.F($"SOLO · B{solo + 1}") : "RESPONSE";
        ctx.DrawText(Text(title, solo >= 0 ? NotaPalette.AccentBright : NotaGraph.TitleInk, 7, bold: true, mono: false), new Point(5, 3));
        NotaGraph.Legend(ctx, w - 5, 2, ("static", NotaPalette.TealBright, NotaGraph.Mark.Dashed), ("live", NotaPalette.AccentBright, NotaGraph.Mark.Line));

        // whiskers (dynamic bands): node → where Range can take it, a dot at the gain it has now
        for (int b = 0; b < DynEq.Bands; b++)
        {
            if (!IsDyn(b) || !on[b]) continue;
            double g = P(b, DynEq.GainF), r = P(b, DynEq.RangeF);
            var ink = r >= 0 ? NotaPalette.Rose : NotaPalette.TealBright;
            double x = Math.Round(X(P(b, DynEq.FreqF))) + 0.5, y0 = Y(g), y1 = Y(g + r);
            ctx.DrawLine(new Pen(ink, 1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) }, new Point(x, y0), new Point(x, y1));
            ctx.DrawLine(new Pen(ink, 1), new Point(x - 3.5, y1), new Point(x + 3.5, y1));
            ctx.DrawEllipse(ink, null, new Point(x, Y(g + _gr[b])), 2.5, 2.5);
        }

        // nodes (selected last, on top)
        for (int k = 1; k <= DynEq.Bands; k++)
        {
            int b = (sel + k) % DynEq.Bands;
            bool bOn = BandOn(b), isSel = b == sel, dimmed = solo >= 0 && !isSel;
            var c = NodeAt(b);
            IBrush fill = !bOn || dimmed ? NotaPalette.BorderStrong : isSel ? NotaPalette.AccentBright : NotaPalette.TextSecondary;
            IBrush num = bOn && !dimmed ? NotaPalette.TextOnAccent : NotaPalette.TextTertiary;
            double r = NodeD / 2;
            ctx.DrawEllipse(fill, RingPen, c, r + 1, r + 1);
            if (IsDyn(b) && on[b])
                ctx.DrawEllipse(null, new Pen(P(b, DynEq.RangeF) >= 0 ? NotaPalette.Rose : NotaPalette.TealBright, 1), c, r + 2.5, r + 2.5);
            var t = Text(NotaNum.F($"{b + 1}"), num, 7, bold: true);
            ctx.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
        }

        DrawTip(ctx, w, h);
    }

    // The selected band's reading beside its node, clear of the node and inside the window.
    private void DrawTip(DrawingContext ctx, double w, double h)
    {
        int b = _selected, ty = BandType(b);
        bool bOn = BandOn(b), g = DynEq.HasGain(ty), dyn = IsDyn(b);
        double r = P(b, DynEq.RangeF);
        var a = Text(NotaNum.F($"B{b + 1} {DynEq.TypeNames[ty]}") + (bOn ? "" : " · off"), NotaPalette.AccentBright, 8, bold: true, mono: false);
        var l2 = Text(DynEq.Hz(P(b, DynEq.FreqF)) + (g ? " · " + DynEq.Db(P(b, DynEq.GainF)) + " dB" : "") + NotaNum.F($" · Q {P(b, DynEq.QF):0.00}"),
            NotaPalette.TextPrimary, 7);
        IBrush dynInk = r >= 0 ? NotaPalette.RoseBright : NotaPalette.TealBright;
        string c = dyn ? (r >= 0 ? "↑ " : "↓ ") + DynEq.Db(_gr[b]) + " dB of " + DynEq.Db(r)
            : !g ? "no dynamics" : BandMode(b) != DynEq.Static && !DynMaster ? "dynamics off" : "static";
        var l3 = Text(c, dyn ? dynInk : NotaPalette.TextTertiary, 7);
        double histH = dyn ? 12 : 0;
        double tw = Math.Max(a.Width, Math.Max(l2.Width, l3.Width)) + 12, th = a.Height + l2.Height + l3.Height + 6 + histH;
        var n = NodeAt(b);
        double x = n.X > w * 0.62 ? n.X - 12 - tw : n.X + 12;
        double y = n.Y < h * 0.34 ? n.Y + 8 : n.Y - 8 - th;
        x = Math.Clamp(x, 3, Math.Max(3, w - tw - 3)); y = Math.Clamp(y, 3, Math.Max(3, h - th - 3));
        var box = new Rect(x, y, tw, th);
        ctx.DrawRectangle(NotaPalette.SurfaceCard, new Pen(NotaPalette.BorderStrong, 1), new RoundedRect(box.Deflate(0.5), 3));
        double ty0 = y + 3;
        ctx.DrawText(a, new Point(x + 6, ty0)); ty0 += a.Height;
        ctx.DrawText(l2, new Point(x + 6, ty0)); ty0 += l2.Height;
        ctx.DrawText(l3, new Point(x + 6, ty0)); ty0 += l3.Height;
        if (!dyn) return;
        // 2-second history of the band's dynamic gain, 0 dB at the top for a cut, the bottom for a boost.
        double hx0 = x + 6, hx1 = x + tw - 6, hy0 = ty0 + 1, hy1 = ty0 + histH - 2, span = Math.Max(1, Math.Abs(r));
        ctx.DrawLine(new Pen(NotaPalette.GridBeat, 1), new Point(hx0, r >= 0 ? hy1 : hy0), new Point(hx1, r >= 0 ? hy1 : hy0));
        var sg = new StreamGeometry();
        using (var gg = sg.Open())
            for (int i = 0; i < HistLen; i++)
            {
                float v = _hist[b * HistLen + (_histHead + 1 + i) % HistLen];
                double t = Math.Clamp(Math.Abs(v) / span, 0, 1);
                var pt = new Point(hx0 + (hx1 - hx0) * i / (HistLen - 1), r >= 0 ? hy1 - t * (hy1 - hy0) : hy0 + t * (hy1 - hy0));
                if (i == 0) gg.BeginFigure(pt, false); else gg.LineTo(pt);
            }
        ctx.DrawGeometry(null, new Pen(dynInk, 1), sg);
    }
}
