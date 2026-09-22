// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Lens body (device kind 22, analyzer), a build of the
// "Nota Lens" mockup (700 × 260) on the Prism/Chamber frame: a permanent SCALE rail on the
// left, one large graph in the middle behind Spectrum / Scope / Waterfall tabs, an
// Analyze / Display panel on the right and a status strip. The scope behaves like a bench
// instrument — the trace is pinned to the trigger point and stands still while older passes
// fade out. Every control is a device param, so automation / MIDI learn / presets / A-B /
// persistence come for free. FullBleed — the shared shell draws the header.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class LensDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Lens.h) ────────────────────────────────
    private const int ViewP = 0, FreezeP = 1, SourceP = 2, MidSideP = 3,
        FftSizeP = 4, WindowP = 5, AverageP = 6, SmoothP = 7, DecayP = 8, TiltP = 9, FloorP = 10,
        PeakHoldP = 11, PeakTimeP = 12, ScaleTopP = 13, ScaleRangeP = 14,
        TimeDivP = 15, VoltDivP = 16, TrigModeP = 17, TrigEdgeP = 18, TrigLevelP = 19, HoldoffP = 20,
        PersistP = 21, PersistTimeP = 22, TracesP = 23, BrightP = 24,
        CursorsP = 25, CursorAP = 26, CursorBP = 27, CursorSnapP = 28,
        WfSpeedP = 29, WfGainP = 30, WfFloorP = 31, WfContrastP = 32, WfOverlapP = 33,
        LogFreqP = 34, NoteGridP = 35, RateP = 36;

    // ── Scope telemetry (Lens::M_*) ──────────────────────────────────────────
    private const int M_PeakHz = 0, M_PeakDb = 1, M_RmsDb = 2, M_LevelDb = 3, M_CrestDb = 4, M_Corr = 5,
        M_Lufs = 6, M_TrigOk = 7, M_PeriodMs = 8, M_FreqHz = 9, M_Vpp = 10, M_Vrms = 11, M_WindowMs = 12,
        M_BinHz = 13, M_FftN = 14, M_FftMs = 15, M_CursorAv = 16, M_CursorBv = 17, M_SampleRate = 18,
        M_Held = 19, kScope = 20;

    private const int L_Spectrum = 0, L_Peak = 1, L_Trace = 2, L_Raw = 3, L_Waterfall = 4;
    private const int A_Rearm = 0, A_ResetPeak = 1, A_ResetTrigger = 2;

    private static readonly int[] FftSizes = { 512, 2048, 4096, 16384 };
    private static readonly double[] WfSpans = { 4, 12, 60 };
    private static readonly double[] Overlaps = { 0, 50, 75, 87.5 };

    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush PanelBg = NotaPalette.SurfaceCard;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush Txt2 = NotaPalette.TextSecondary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;

    /// <summary>The right panel's tab survives a card rebuild (keyed by track + device slot).</summary>
    private static readonly Dictionary<(int, int), int> RightTab = new();

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "ANALYZER";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;

        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) => engine.DeviceSetParam(track, di, p, Math.Clamp(v, 0f, 1f));
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        bool On(int p) => P(p) >= 0.5f;
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);
        float Def(int p) => engine.DeviceParamDefault(track, di, p);

        var readouts = new List<Action>();
        var data = new LensData();
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        // ---- unit maps (mirror Lens.h) ------------------------------------------------
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string Inv(FormattableString f) => NotaNum.F(f);
        double TimeDiv() => Exp(P(TimeDivP), 0.00002, 0.02);
        double VoltDiv() => Exp(P(VoltDivP), 0.02, 2.0);
        double TopDb() => 24 - P(ScaleTopP) * 60;
        double RangeDb() => 30 + P(ScaleRangeP) * 90;
        int Avg() => (int)Math.Round(1 + P(AverageP) * 15);
        double DecayS() => Exp(P(DecayP), 0.05, 8);
        double PeakS() => Exp(P(PeakTimeP), 0.5, 30);
        double PersistS() => Exp(P(PersistTimeP), 0.05, 2.0);
        int TraceCount() => (int)Math.Round(1 + P(TracesP) * 7);
        double WfSpan() => WfSpans[Sel(WfSpeedP, 3)];

        static string Ms(double sec) => sec >= 1 ? Inv($"{sec:0.00} s") : sec >= 0.001 ? Inv($"{sec * 1000:0.00} ms") : Inv($"{sec * 1e6:0} µs");
        static string Sec(double s) => s >= 1 ? Inv($"{s:0.0} s") : Inv($"{s * 1000:0} ms");
        static string Hz(double hz) => hz >= 1000 ? Inv($"{hz / 1000:0.00} kHz") : Inv($"{hz:0} Hz");
        static string Db(double db) => Inv($"{db:0.0} dB");
        static string Pc(double v) => Inv($"{v * 100:0} %");

        // ---- small builders (same kit as Prism / Chamber) -----------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
        { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? MutedC, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
        static Control Col(Control c, int col) { Grid.SetColumn(c, col); return c; }
        Control Learn(Control c, int p) { MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p)); return c; }

        Control K(int p, string name, Func<double, string> fmt, double size = 34, double cellW = 52, IBrush? arc = null)
        {
            var val = Mono(fmt(P(p)), 7, TxtC);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = Def(p), Width = size, Height = size };
            knob.ValueChanged += v => { Raw(p, (float)v); val.Text = fmt(v); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            readouts.Add(() => { if (knob.Dragging || !knob.IsEffectivelyVisible) return; float c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(c); });
            return KnobCell(name, knob, val, cellW);
        }

        Control Toggle(int p, string label)
        {
            var wrap = Switch(label, () => On(p), () => SetP(p, On(p) ? 0f : 1f), out var sync);
            readouts.Add(sync);
            return Learn(wrap, p);
        }

        Control Seg(int p, string[] names, Action? changed = null)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); changed?.Invoke(); }, out var sync);
            readouts.Add(sync);
            return Learn(seg, p);
        }

        Control Row(string label, int p, Func<double, string> fmt, double labW = 48, double valW = 34)
        {
            var bar = DeviceCardKit.SliderRow("", () => Math.Clamp(P(p), 0, 1), v => Raw(p, (float)v), () => fmt(Math.Clamp(P(p), 0, 1)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => SetP(p, Def(p)), valueWidth: valW);
            readouts.Add(() => { if (bar.IsEffectivelyVisible) sync(); });
            Learn(bar, p);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Inv($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label)); g.Children.Add(Col(bar, 1));
            return g;
        }

        // A sunken readout box: a caps eyebrow over one or two mono lines.
        (Border Box, TextBlock A, TextBlock B) ReadBox(string title, string hintA = "—", string hintB = "")
        {
            var a = Mono(hintA, 9, AmberLit);
            var b = Mono(hintB, 8, Txt2);
            var box = new Border
            {
                Background = Inset, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Control, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 1, Children = { Caps(title), a, b } },
            };
            return (box, a, b);
        }

        Control Button(string text, string tip, Action click)
        {
            var b = new Border
            {
                Background = NotaPalette.SurfaceRaised, BorderBrush = NotaPalette.BorderStrong, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Tile, Padding = new Thickness(8, 3), Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = new TextBlock { Text = text, FontSize = 9, Foreground = TxtC, HorizontalAlignment = HorizontalAlignment.Center },
            };
            ToolTip.SetTip(b, tip);
            b.PointerPressed += (_, e) => { if (e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) { e.Handled = true; click(); } };
            return b;
        }

        // ---- the three graphs ---------------------------------------------------------
        var spectrum = new LensSpectrumView(data);
        var scopeView = new LensScopeView(data);
        var waterfall = new LensWaterfallView(data);

        scopeView.TrigLevelChanged += v => { SetP(TrigLevelP, (float)(v * 0.5 + 0.5)); foreach (var a in readouts) a(); };
        scopeView.CursorChanged += (which, v) => { SetP(which == 0 ? CursorAP : CursorBP, (float)v); foreach (var a in readouts) a(); };

        // ---- centre tabs --------------------------------------------------------------
        var curve = new float[LensData.Curve];
        var wfRow = new float[LensData.WfBins];
        var wfPend = new float[LensData.WfBins];   // the row being accumulated (see Overlap)

        Control GraphHost(Control topRow, Control graph, Control knobRow) => new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(6, 5),
            Children = { Docked(topRow, Dock.Top), Docked(knobRow, Dock.Bottom), graph },
        };

        Control KnobRow(params Control[] cells)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Height = 52, Margin = new Thickness(0, 4, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in cells) sp.Children.Add(c);
            return sp;
        }

        // A two-line mono summary that sits to the right of a knob row.
        (Control Ctl, TextBlock A, TextBlock B) SideNote()
        {
            var a = Mono("", 7, MutedC);
            var b = Mono("", 7, MutedC);
            return (new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Children = { a, b } }, a, b);
        }

        var specNote = SideNote();
        var scopeNote = SideNote();
        var wfNote = SideNote();
        var specInfo = Mono("", 7, MutedC);
        var scopeInfo = Mono("", 7, MutedC);
        var wfInfo = Mono("", 7, MutedC);

        Control TopRow(string label, Control chips, TextBlock info)
        {
            var d = new DockPanel { Height = 18, LastChildFill = false, Margin = new Thickness(0, 0, 0, 4) };
            d.Children.Add(Docked(Caps(label), Dock.Left));
            var holder = new Border { Margin = new Thickness(6, 0, 0, 0), Child = chips };
            d.Children.Add(Docked(holder, Dock.Left));
            info.TextAlignment = TextAlignment.Right;
            d.Children.Add(Docked(info, Dock.Right));
            return d;
        }

        Control SpectrumTab() => GraphHost(
            TopRow("FFT", Seg(FftSizeP, new[] { "512", "2048", "4096", "16384" }), specInfo),
            spectrum,
            KnobRow(K(SmoothP, "Smooth", v => Pc(v), 34, 48, TealC),
                    K(DecayP, "Decay", v => Sec(Exp(v, 0.05, 8)), 34, 48),
                    K(TiltP, "Tilt", v => Inv($"{v * 9:0.0} dB"), 34, 48, TealC),
                    K(FloorP, "Floor", v => Inv($"{-120 + v * 80:0} dB"), 34, 48),
                    specNote.Ctl));

        Control ScopeTab()
        {
            var trig = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
                Children = { Seg(TrigModeP, new[] { "Auto", "Normal", "Single" }), Seg(TrigEdgeP, new[] { "↑", "↓" }) },
            };
            return GraphHost(
                TopRow("TRIG", trig, scopeInfo),
                scopeView,
                KnobRow(K(TimeDivP, "Time/Div", v => Ms(Exp(v, 0.00002, 0.02)), 34, 48),
                        K(VoltDivP, "Volt/Div", v => Inv($"{Exp(v, 0.02, 2.0):0.00}"), 34, 48),
                        K(TrigLevelP, "Trig Lvl", v => Inv($"{(v - 0.5) * 2:+0.00;−0.00;0.00}"), 34, 48, TealC),
                        K(HoldoffP, "Holdoff", v => Inv($"{v * 200:0} ms"), 34, 48, TealC),
                        scopeNote.Ctl));
        }

        Control WaterfallTab() => GraphHost(
            TopRow("SPEED", Seg(WfSpeedP, new[] { "4 s", "12 s", "60 s" }, () => data.ClearWaterfall()), wfInfo),
            waterfall,
            KnobRow(K(WfGainP, "Gain", v => Inv($"{-24 + v * 48:+0;−0;0} dB"), 34, 48),
                    K(WfFloorP, "Floor", v => Inv($"{-120 + v * 80:0} dB"), 34, 48),
                    K(WfContrastP, "Contrast", v => Pc(v), 34, 48, TealC),
                    K(WfOverlapP, "Overlap", v => Inv($"{Overlaps[Math.Clamp((int)Math.Round(v * 3), 0, 3)]:0.#} %"), 34, 48, TealC),
                    wfNote.Ctl));

        // ---- right panel --------------------------------------------------------------
        var cursorBox = ReadBox("CURSOR", "—", "move over the graph");
        var captureBox = ReadBox("CAPTURE");
        var measureBox = ReadBox("A → B");
        var wfCursorBox = ReadBox("CURSOR", "—", "move over the graph");

        spectrum.CursorMoved += () =>
        {
            double hz = spectrum.CursorHz;
            if (hz <= 0) { cursorBox.A.Text = "—"; cursorBox.B.Text = "move over the graph"; return; }
            cursorBox.A.Text = Inv($"{Hz(hz)} · {LensInk.Note(hz)}");
            cursorBox.B.Text = Inv($"{spectrum.CursorDb:0.0} dB · bin {(Sc(M_BinHz) > 0 ? hz / Sc(M_BinHz) : 0):0}");
        };
        waterfall.CursorMoved += () =>
        {
            double hz = waterfall.CursorHz;
            if (hz <= 0) { wfCursorBox.A.Text = "—"; wfCursorBox.B.Text = "move over the graph"; return; }
            wfCursorBox.A.Text = Inv($"{Hz(hz)} · {LensInk.Note(hz)}");
            wfCursorBox.B.Text = Inv($"−{waterfall.CursorAgeSec:0.0} s");
        };

        Control Stack(params Control[] cs)
        {
            var sp = new StackPanel { Spacing = 5, Margin = new Thickness(7, 6) };
            foreach (var c in cs) sp.Children.Add(c);
            return sp;
        }

        Control SpectrumAnalyze() => Stack(
            new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), Children = { Caps("WINDOW"), Col(Seg(WindowP, new[] { "Hann", "B-H", "Flat" }), 1) } },
            Row("AVG", AverageP, v => Inv($"{1 + v * 15:0}")),
            Row("PEAK", PeakTimeP, v => Sec(Exp(v, 0.5, 30))),
            Row("RATE", RateP, v => Inv($"{10 + v * 50:0} fps")),
            cursorBox.Box,
            Toggle(PeakHoldP, "Peak hold"),
            Toggle(MidSideP, "Mid / Side"));

        Control ScopeAnalyze() => Stack(
            Row("PERSIST", PersistTimeP, v => Sec(Exp(v, 0.05, 2.0))),
            Row("TRACES", TracesP, v => Inv($"{1 + v * 7:0}")),
            Row("BRIGHT", BrightP, v => Pc(v)),
            new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), Children = { Caps("SOURCE"), Col(Seg(SourceP, new[] { "L+R", "L", "R" }), 1) } },
            captureBox.Box,
            Toggle(PersistP, "Afterglow"),
            Toggle(CursorsP, "Cursors"));

        Control CursorAnalyze() => Stack(
            measureBox.Box,
            Row("A POS", CursorAP, v => Ms(v * TimeDiv() * 8)),
            Row("B POS", CursorBP, v => Ms(v * TimeDiv() * 8)),
            new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5,
                Children =
                {
                    new Border { Width = 82, Child = Button("Re-arm", "Arm the next Single capture", () => { engine.DeviceAction(track, di, A_Rearm, 0, 0f); foreach (var a in readouts) a(); }) },
                    new Border { Width = 82, Child = Button("Export WAV", "Write the captured scope window to a WAV file", ExportWav) },
                },
            },
            Toggle(CursorSnapP, "Snap to peaks"),
            Toggle(CursorsP, "Cursors"));

        Control WaterfallAnalyze() => Stack(
            new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), Children = { Caps("WINDOW"), Col(Seg(WindowP, new[] { "Hann", "B-H", "Flat" }), 1) } },
            Row("FFT", FftSizeP, v => Inv($"{FftSizes[Math.Clamp((int)Math.Round(v * 3), 0, 3)]}")),
            Row("OVERLAP", WfOverlapP, v => Inv($"{Overlaps[Math.Clamp((int)Math.Round(v * 3), 0, 3)]:0.#} %")),
            Row("AVG", AverageP, v => Inv($"{1 + v * 15:0}")),
            wfCursorBox.Box,
            Toggle(LogFreqP, "Log frequency"),
            Toggle(NoteGridP, "Note grid"));

        Control DisplayTab() => Stack(
            new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), Children = { Caps("SOURCE"), Col(Seg(SourceP, new[] { "L+R", "L", "R" }), 1) } },
            Row("TOP", ScaleTopP, v => Inv($"{24 - v * 60:0} dB")),
            Row("RANGE", ScaleRangeP, v => Inv($"{30 + v * 90:0} dB")),
            Row("RATE", RateP, v => Inv($"{10 + v * 50:0} fps")),
            Toggle(MidSideP, "Mid / Side"),
            Toggle(LogFreqP, "Log frequency"),
            Toggle(NoteGridP, "Note grid"),
            Toggle(PeakHoldP, "Peak hold"));

        // ---- SCALE rail ---------------------------------------------------------------
        var railTopVal = Mono("", 7, AmberLit);
        var railRngVal = Mono("", 7, TxtC);

        // The slider stretches to the rail's height, its caption keeps its own line, and both
        // retarget when the centre tab changes (dB scale for Spectrum / Waterfall, V and T for Scope).
        Control RailSlider(Func<int> param, SolidColorBrush ink, Func<string> caption)
        {
            var s = new LensVSlider(P(param()), ink) { Default = Def(param()), VerticalAlignment = VerticalAlignment.Stretch };
            var lbl = Caps(caption(), MutedC);
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            s.GestureBegin += () => Begin(param());
            s.GestureEnd += () => End(param());
            s.ValueChanged += v => Raw(param(), (float)v);
            readouts.Add(() =>
            {
                lbl.Text = caption();
                s.Default = Def(param());
                if (!s.Dragging) { float c = P(param()); if (Math.Abs(c - s.Value) > 1e-4) s.Value = c; }
            });
            var g = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 3, Width = 16 };
            g.Children.Add(s);
            Grid.SetRow(lbl, 1); g.Children.Add(lbl);
            return g;
        }

        int centreTab = Sel(ViewP, 3);
        var railA = RailSlider(() => centreTab == 1 ? VoltDivP : ScaleTopP, NotaPalette.Accent, () => centreTab == 1 ? "V/D" : "TOP");
        var railB = RailSlider(() => centreTab == 1 ? TimeDivP : ScaleRangeP, NotaPalette.Teal, () => centreTab == 1 ? "T/D" : "RNG");
        var rail = new Border
        {
            Width = 56, Background = PanelBg, BorderBrush = Border2, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    Docked(new TextBlock { Text = "SCALE", FontSize = 7, FontWeight = FontWeight.Bold, LetterSpacing = 0.9, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) }, Dock.Top),
                    Docked(new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Children = { railTopVal, railRngVal } }, Dock.Bottom),
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 9, HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { railA, railB },
                    },
                },
            },
        };
        DockPanel.SetDock(rail, Dock.Left);

        // ---- assembly -----------------------------------------------------------------
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[4];   // 0 spectrum · 1 scope · 2 waterfall · 3 display

        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => ScopeTab(), 2 => WaterfallTab(), _ => SpectrumTab() };
        bool cursorPanel = false;
        Control RightBody(int t)
        {
            if (t == 3) return rightBodies[3] ??= DisplayTab();
            if (t == 1)
            {
                bool want = On(CursorsP);
                if (rightBodies[1] is null || want != cursorPanel) { cursorPanel = want; rightBodies[1] = want ? CursorAnalyze() : ScopeAnalyze(); }
                return rightBodies[1]!;
            }
            return rightBodies[t] ??= t == 2 ? WaterfallAnalyze() : SpectrumAnalyze();
        }

        int rightTab = RightTab.TryGetValue((track, di), out var rt) ? rt : 0;   // 0 = Analyze, 1 = Display

        var freezeToggle = Switch("Freeze", () => On(FreezeP), () => SetP(FreezeP, On(FreezeP) ? 0f : 1f), out var freezeSync);
        readouts.Add(freezeSync);
        Learn(freezeToggle, FreezeP);
        ToolTip.SetTip(freezeToggle, "Freeze — stop capturing and hold what is on screen");

        var headNote = Mono("", 7, MutedC);
        var extras = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { freezeToggle, headNote } };

        Control TabFrame(string[] tabs, int initial, ContentControl host, Func<int, Control> body, bool centered, Action<int>? changed, Control? extrasCtl)
        {
            int sel = Math.Clamp(initial, 0, tabs.Length - 1);
            var btns = new Border[tabs.Length];
            void Hi()
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool on = i == sel;
                    btns[i].Background = on ? PanelBg : Brushes.Transparent;
                    btns[i].BorderBrush = on ? Amber : Brushes.Transparent;
                    var tb = (TextBlock)btns[i].Child!;
                    tb.Foreground = on ? AmberLit : MutedC;
                    tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            var bar = centered ? (Panel)new UniformGrid { Rows = 1 } : new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < tabs.Length; i++)
            {
                int iv = i;
                var b = new Border
                {
                    Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = tabs[i], FontSize = 9, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
                };
                b.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                    e.Handled = true; sel = iv; Hi(); host.Content = body(iv); changed?.Invoke(iv);
                };
                btns[i] = b; bar.Children.Add(b);
            }
            var barDock = new DockPanel { Height = 20, LastChildFill = centered };
            if (extrasCtl != null) { var ex = new Border { Padding = new Thickness(0, 0, 8, 0), Child = extrasCtl }; DockPanel.SetDock(ex, Dock.Right); barDock.Children.Add(ex); }
            if (!centered) DockPanel.SetDock(bar, Dock.Left);
            barDock.Children.Add(bar);
            var barBorder = new Border { BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock };
            DockPanel.SetDock(barBorder, Dock.Top);
            Hi(); host.Content = body(sel);
            return new DockPanel { LastChildFill = true, Children = { barBorder, host } };
        }

        var centre = TabFrame(new[] { "Spectrum", "Scope", "Waterfall" }, centreTab, centreHost, CentreBody, false, t =>
        {
            centreTab = t;
            SetP(ViewP, t / 2f);
            rightHost.Content = RightBody(rightTab == 1 ? 3 : t);   // Display is shared; Analyze follows the view
            foreach (var a in readouts) a();
        }, extras);

        var rightFrame = TabFrame(new[] { "Analyze", "Display" }, rightTab, rightHost, t =>
        {
            rightTab = t; RightTab[(track, di)] = t;
            return RightBody(t == 1 ? 3 : centreTab);
        }, true, null, null);

        var right = new Border
        {
            Width = 186, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = rightFrame,
        };
        DockPanel.SetDock(right, Dock.Right);

        var centreBox = new Border
        {
            Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Tile, Margin = new Thickness(5, 0), ClipToBounds = true, Child = centre,
        };

        // ---- status strip ---------------------------------------------------------------
        var statusLeft = new TextBlock { FontSize = 8, Foreground = Txt2, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, Txt2);
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft); statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border
        {
            Height = 18, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(8, 0), Child = statusGrid,
        };
        DockPanel.SetDock(status, Dock.Bottom);

        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { rail, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp, Children = { status, bodyRow } };

        // ---- WAV export of the captured scope window --------------------------------------
        async void ExportWav()
        {
            var top = TopLevel.GetTopLevel(scopeView);
            if (top is null) return;
            var raw = new float[65536];
            int n = engine.DeviceLayerWave(track, di, L_Raw, raw, raw.Length);
            if (n <= 0) return;
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export the Lens capture to WAV",
                DefaultExtension = "wav",
                SuggestedFileName = "Nota Lens capture.wav",
                FileTypeChoices = new[] { new FilePickerFileType("WAV audio") { Patterns = new[] { "*.wav" } } },
            });
            string? path = file?.TryGetLocalPath();
            if (path is null) return;
            int sr = (int)Math.Round(Sc(M_SampleRate) > 0 ? Sc(M_SampleRate) : 48000);
            using var w = new Nota.Infrastructure.WavWriter(path, sr, 1, WavBitDepth.Float32);
            w.WriteFrames(raw, n);
        }

        // ---- the 60 Hz tick ----------------------------------------------------------------
        var clock = Stopwatch.StartNew();
        double lastT = 0, wfAcc = 0, ghostAcc = 0;

        void Refresh()
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.25, now - lastT);
            lastT = now;

            scN = engine.DeviceScope(track, di, scope, kScope);
            data.SampleRate = Sc(M_SampleRate) > 0 ? Sc(M_SampleRate) : 48000;
            data.TopDb = TopDb(); data.RangeDb = RangeDb();
            data.VoltDiv = VoltDiv(); data.TimeDivSec = TimeDiv();
            data.LogFreq = On(LogFreqP); data.NoteGrid = On(NoteGridP);
            data.PeakOn = On(PeakHoldP); data.Bright = P(BrightP);
            data.TrigLevel = (P(TrigLevelP) - 0.5) * 2; data.TrigRising = !On(TrigEdgeP);
            data.TrigOk = Sc(M_TrigOk) > 0.5; data.Held = Sc(M_Held) > 0.5;
            data.CursorsOn = On(CursorsP); data.CursorA = P(CursorAP); data.CursorB = P(CursorBP);
            data.CursorSnap = On(CursorSnapP);
            data.WfSpanSec = WfSpan();

            if (centreTab == 0)
            {
                engine.DeviceLayerWave(track, di, L_Spectrum, data.Spec, LensData.Curve);
                engine.DeviceLayerWave(track, di, L_Peak, data.Peak, LensData.Curve);
                spectrum.Tick();
            }
            else if (centreTab == 1)
            {
                engine.DeviceLayerWave(track, di, L_Trace, data.Trace, LensData.TraceN);
                data.AgeGhosts(dt);
                if (On(PersistP))
                {
                    ghostAcc += dt;
                    double every = PersistS() / Math.Max(1, TraceCount());
                    if (ghostAcc >= every) { ghostAcc = 0; data.PushGhost(PersistS(), TraceCount()); }
                }
                else data.Ghosts.Clear();
                scopeView.GhostLife = PersistS();
                scopeView.Tick();
            }
            else
            {
                engine.DeviceLayerWave(track, di, L_Spectrum, data.Spec, LensData.Curve);
                engine.DeviceLayerWave(track, di, L_Waterfall, wfRow, LensData.WfBins);
                // Overlap = how much of the time between rows the row actually covers: at 0 %
                // a row is the single frame that ends it, at 87.5 % it peak-holds nearly the
                // whole interval, so a transient survives a 60-second window.
                wfAcc += dt;
                double rowEvery = WfSpan() / LensData.WfRows;
                double cover = Overlaps[Sel(WfOverlapP, 4)] / 100.0;
                if (wfAcc >= rowEvery * (1 - cover))
                    for (int i = 0; i < LensData.WfBins; i++) wfPend[i] = Math.Max(wfPend[i], wfRow[i]);
                if (wfAcc >= rowEvery)
                {
                    wfAcc = 0;
                    data.PushWaterfallRow(wfPend);
                    Array.Clear(wfPend);
                }
                waterfall.Tick();
            }

            foreach (var a in readouts) a();

            // Rail readouts + captions follow the active view.
            railTopVal.Text = centreTab == 1 ? Inv($"{VoltDiv():0.00} /d") : Inv($"{TopDb():0} dB");
            railRngVal.Text = centreTab == 1 ? Ms(TimeDiv()) : Inv($"{RangeDb():0} dB");

            int fftN = (int)Sc(M_FftN);
            string winName = new[] { "Hann", "B-H", "Flat" }[Sel(WindowP, 3)];
            headNote.Text = centreTab == 1
                ? (data.Held ? "HELD" : data.TrigOk ? "TRIG’D" : "FREE")
                : centreTab == 2 ? Inv($"{LensData.WfBins} bins") : Inv($"FFT {fftN}");

            specInfo.Text = Inv($"window {Sc(M_FftMs):0} ms · slope {P(TiltP) * 9:0.0} dB/oct");
            scopeInfo.Text = Inv($"level {(P(TrigLevelP) - 0.5) * 2:0.00} · holdoff {P(HoldoffP) * 200:0} ms");
            wfInfo.Text = Inv($"floor {-120 + P(WfFloorP) * 80:0} dB · {(On(LogFreqP) ? "log" : "linear")} freq");

            specNote.A.Text = Inv($"peak {Sc(M_PeakDb):0.0} dB @ {Hz(Sc(M_PeakHz))}");
            specNote.B.Text = Inv($"crest {Sc(M_CrestDb):0.0} dB · LUFS {Sc(M_Lufs):0.0}");
            scopeNote.A.Text = Sc(M_PeriodMs) > 0 ? Inv($"period {Sc(M_PeriodMs):0.000} ms · {Sc(M_FreqHz):0.0} Hz") : "period —";
            scopeNote.B.Text = Inv($"Vpp {Sc(M_Vpp):0.000} · RMS {Sc(M_Vrms):0.000}");
            wfNote.A.Text = Inv($"row {WfSpan() / LensData.WfRows * 1000:0} ms · {LensData.WfBins} bins");
            wfNote.B.Text = Inv($"peak {Hz(Sc(M_PeakHz))} · {Sc(M_PeakDb):0.0} dB");

            captureBox.A.Text = data.Held ? "held" : data.TrigOk ? "stable" : "free-run";
            captureBox.B.Text = Inv($"holdoff {P(HoldoffP) * 200:0} ms · edge {(On(TrigEdgeP) ? "↓" : "↑")}");

            double dtSec = Math.Abs(P(CursorBP) - P(CursorAP)) * TimeDiv() * 8;
            measureBox.A.Text = Inv($"Δt {dtSec * 1000:0.000} ms → {(dtSec > 1e-9 ? 1 / dtSec : 0):0.0} Hz");
            measureBox.B.Text = Inv($"ΔV {Sc(M_CursorBv) - Sc(M_CursorAv):0.000} · {LensInk.Note(dtSec > 1e-9 ? 1 / dtSec : 0)}");

            statusLeft.Text = centreTab switch
            {
                1 => Inv($"Scope · {Ms(TimeDiv())}/div · {VoltDiv():0.00}/div · trig {(On(TrigEdgeP) ? "↓" : "↑")} {(P(TrigLevelP) - 0.5) * 2:0.00} {new[] { "auto", "normal", "single" }[Sel(TrigModeP, 3)]}{(On(PersistP) ? Inv($" · persist {Sec(PersistS())}") : "")}"),
                2 => Inv($"Waterfall · {LensData.WfBins} bins · {winName} · overlap {Overlaps[Sel(WfOverlapP, 4)]:0.#} % · window {WfSpan():0} s · floor {-120 + P(WfFloorP) * 80:0} dB"),
                _ => Inv($"Spectrum · FFT {fftN} · {winName} · avg {Avg()} · slope {P(TiltP) * 9:0.0} dB/oct{(On(MidSideP) ? " · Mid/Side" : "")}"),
            };
            statusLeft.Foreground = On(FreezeP) ? AmberLit : Txt2;
            statusRight.Text = Inv($"{data.SampleRate / 1000:0.#} kHz · latency 0 smp · {(On(FreezeP) ? "frozen" : Inv($"{10 + P(RateP) * 50:0} fps"))} · corr {Sc(M_Corr):+0.00;−0.00;0.00}");

            // The right Analyze panel follows the centre tab (and the Cursors switch).
            if (rightTab == 0)
            {
                var want = RightBody(centreTab);
                if (!ReferenceEquals(rightHost.Content, want)) rightHost.Content = want;
            }
        }

        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
