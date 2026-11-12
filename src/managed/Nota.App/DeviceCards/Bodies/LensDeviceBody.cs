// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Lens body (device kind 22, analyzer), a build of the
// "Nota Lens" mockup (700 × 260) on the #531 card layout: one large graph behind Spectrum / Scope /
// Waterfall tabs (its knobs beside it, its choices under it) and a right panel that follows the
// tab; the source and Freeze sit in the tab bar. An analyzer has no output stage, so there is no
// output strip. S is the spectrum over the source and four knobs. The scope behaves like a bench
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
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class LensDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Lens.h) ────────────────────────────────
    private const int GraphP = 0, FreezeP = 1, SourceP = 2, MidSideP = 3,
        FftSizeP = 4, WindowP = 5, AverageP = 6, SmoothP = 7, DecayP = 8, TiltP = 9, FloorP = 10,
        PeakHoldP = 11, PeakTimeP = 12, ScaleTopP = 13, ScaleRangeP = 14,
        TimeDivP = 15, VoltDivP = 16, TrigModeP = 17, TrigEdgeP = 18, TrigLevelP = 19, HoldoffP = 20,
        PersistP = 21, PersistTimeP = 22, TracesP = 23, BrightP = 24,
        CursorsP = 25, CursorAP = 26, CursorBP = 27, CursorSnapP = 28,
        WfSpeedP = 29, WfGainP = 30, WfFloorP = 31, WfContrastP = 32, WfOverlapP = 33,
        LogFreqP = 34, NoteGridP = 35, RateP = 36,
        SyncP = 37, SpanP = 38, KeyShowP = 39, KeyGainP = 40, LayoutP = 41;   // specs/050
    // View (42) is the card size (#531), read through DeviceView.

    // ── Scope telemetry (Lens::M_*) ──────────────────────────────────────────
    private const int M_PeakHz = 0, M_PeakDb = 1, M_RmsDb = 2, M_LevelDb = 3, M_CrestDb = 4, M_Corr = 5,
        M_Lufs = 6, M_TrigOk = 7, M_PeriodMs = 8, M_FreqHz = 9, M_Vpp = 10, M_Vrms = 11, M_WindowMs = 12,
        M_BinHz = 13, M_FftN = 14, M_FftMs = 15, M_CursorAv = 16, M_CursorBv = 17, M_SampleRate = 18,
        M_Held = 19, M_KeyLive = 20, M_Instance = 21, kScope = 22;

    private const int L_Spectrum = 0, L_Peak = 1, L_Trace = 2, L_Raw = 3, L_Waterfall = 4, L_KeyTrace = 5, L_Sync = 6;
    private static readonly string[] SpanNames = { "¼ b", "½ b", "1 b", "2 b", "1 bar", "2 bar", "4 bar" };
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
    private static readonly IBrush TealC = DeviceCardKit.ModInk;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush Txt2 = NotaPalette.TextSecondary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;

    public double Width => 700;
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex, xl: true);
    // XL (specs/085): the L body stretched in the panel above the chain; the pop-out key in the
    // header opens it in its own window (the chain view adds it, as for every XL device).
    public bool SupportsXl => true;
    public bool FullBleed => true;
    public string? Subtitle => "Analyzer";

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
        double PersistS() => Exp(P(PersistTimeP), 0.05, 2.0);
        int TraceCount() => (int)Math.Round(1 + P(TracesP) * 7);
        double WfSpan() => WfSpans[Sel(WfSpeedP, 3)];
        static double KeyDb(double v) => -24 + 48 * Math.Clamp(v, 0, 1);
        bool scOk = engine.DeviceAcceptsSidechain(track, di);
        int KeySrc() => scOk ? engine.DeviceSidechainSource(track, di) : -1;
        string SrcName(int id) { string n = TrackNames.Of(engine, id); return n == "—" ? Loc.T("common.none") : n; }

        static string Ms(double sec) => sec >= 1 ? Inv($"{sec:0.00} s") : sec >= 0.001 ? Inv($"{sec * 1000:0.00} ms") : Inv($"{sec * 1e6:0} µs");
        static string Sec(double s) => s >= 1 ? Inv($"{s:0.0} s") : Inv($"{s * 1000:0} ms");
        static string Hz(double hz) => hz >= 1000 ? Inv($"{hz / 1000:0.00} kHz") : Inv($"{hz:0} Hz");
        static string Pc(double v) => Inv($"{v * 100:0} %");

        // ---- small builders (same kit as Prism / Chamber) -----------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? MutedC, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Measure"); return tb; }
        static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
        static Control Col(Control c, int col) { Grid.SetColumn(c, col); return c; }
        Control Learn(Control c, int p) { MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p)); return c; }

        Control K(int p, string name, Func<double, string> fmt, double size = 34, double cellW = 52, IBrush? arc = null)
        {
            var val = Mono(fmt(P(p)), 7, TxtC);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = Def(p), Width = size, Height = size };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
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
            var a = Mono(hintA, 9, NotaPalette.TextPrimary);
            var b = Mono(hintB, 8, Txt2);
            var box = new Border
            {
                Background = Inset, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Field, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 1, Children = { Caps(title), a, b } },
            };
            return (box, a, b);
        }

        Control Button(string text, string tip, Action click)
        {
            var b = new Border
            {
                Background = NotaPalette.SurfaceRaised, BorderBrush = NotaPalette.BorderStrong, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Group, Padding = new Thickness(8, 3), Cursor = new Cursor(StandardCursorType.Hand),
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

        // A two-line mono summary (the panel's read-out box).
        (Control Ctl, TextBlock A, TextBlock B) SideNote()
        {
            var a = Mono("", 8, TxtC);
            var b = Mono("", 8, Txt2);
            return (new StackPanel { Spacing = 1, Children = { a, b } }, a, b);
        }

        var specNote = SideNote();
        var scopeNote = SideNote();
        var wfNote = SideNote();
        var specInfo = Mono("", 7, MutedC);
        var scopeInfo = Mono("", 7, MutedC);
        var wfInfo = Mono("", 7, MutedC);
        Control SpecK(int p) => p switch
        {
            SmoothP => K(SmoothP, "Smooth", v => Pc(v), 34, 50, TealC),
            DecayP => K(DecayP, "Decay", v => Sec(Exp(v, 0.05, 8)), 34, 50),
            TiltP => K(TiltP, "Tilt", v => Inv($"{v * 9:0.0} dB"), 34, 50, TealC),
            _ => K(FloorP, "Floor", v => Inv($"{-120 + v * 80:0} dB"), 34, 50),
        };

        Control SpectrumTab() => CardLayout.View(spectrum,
            CardLayout.KnobColumn(SpecK(SmoothP), SpecK(DecayP), SpecK(TiltP)),
            CardLayout.Line(6, Caps("FFT"), Seg(FftSizeP, new[] { "512", "2048", "4096", "16384" }), Toggle(MidSideP, Loc.T("device.lens.mid_side"))));

        Control ScopeTab()
        {
            // Sync swaps the trigger chips for the span on the song's grid (specs/050).
            var trig = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
                Children = { Seg(TrigModeP, new[] { "Auto", "Normal", "Single" }), Seg(TrigEdgeP, new[] { "↑", "↓" }) },
            };
            var span = Seg(SpanP, SpanNames);
            ToolTip.SetTip(span, Loc.T("device.lens.span_how_much_of_the"));
            var sync = Toggle(SyncP, "Sync");
            ToolTip.SetTip(sync, Loc.T("device.lens.sync_line_the_scope_up"));
            var caps = Caps("TRIG");
            readouts.Add(() =>
            {
                bool on = On(SyncP);
                trig.IsVisible = !on; span.IsVisible = on;
                caps.Text = NotaText.Label(on ? "SPAN" : "TRIG");
            });

            // Time base and trigger knobs rest while Sync draws the beat grid.
            var timeK = K(TimeDivP, "Time/Div", v => Ms(Exp(v, 0.00002, 0.02)), 34, 50);
            var trigK = K(TrigLevelP, "Trig Lvl", v => Inv($"{(v - 0.5) * 2:+0.00;−0.00;0.00}"), 34, 50, TealC);
            readouts.Add(() =>
            {
                bool on = On(SyncP);
                foreach (var c in new[] { timeK, trigK }) { c.IsEnabled = !on; c.Opacity = on ? 0.4 : 1; }
            });
            return CardLayout.View(scopeView,
                CardLayout.KnobColumn(timeK, K(VoltDivP, "Volt/Div", v => Inv($"{Exp(v, 0.02, 2.0):0.00}"), 34, 50), trigK),
                CardLayout.Line(6, sync, caps, new Panel { Children = { trig, span } }, Toggle(PersistP, "Afterglow")));
        }

        Control WaterfallTab() => CardLayout.View(waterfall,
            CardLayout.KnobColumn(
                K(WfGainP, "Gain", v => Inv($"{-24 + v * 48:+0;−0;0} dB"), 34, 50),
                K(WfFloorP, "Floor", v => Inv($"{-120 + v * 80:0} dB"), 34, 50),
                K(WfContrastP, "Contrast", v => Pc(v), 34, 50, TealC)),
            CardLayout.Line(6, Caps("SPEED"), Seg(WfSpeedP, new[] { "4 s", "12 s", "60 s" }, () => data.ClearWaterfall())));

        // ---- right panel --------------------------------------------------------------
        var cursorBox = ReadBox("CURSOR", "—", Loc.T("device.lens.move_over_the_graph"));
        var captureBox = ReadBox("CAPTURE");
        var measureBox = ReadBox("A → B");
        var wfCursorBox = ReadBox("CURSOR", "—", Loc.T("device.lens.move_over_the_graph"));

        spectrum.CursorMoved += () =>
        {
            double hz = spectrum.CursorHz;
            if (hz <= 0) { cursorBox.A.Text = "—"; cursorBox.B.Text = Loc.T("device.lens.move_over_the_graph"); return; }
            cursorBox.A.Text = Inv($"{Hz(hz)} · {LensInk.Note(hz)}");
            cursorBox.B.Text = Loc.F("device.lens.db_bin", spectrum.CursorDb, (Sc(M_BinHz) > 0 ? hz / Sc(M_BinHz) : 0));
        };
        waterfall.CursorMoved += () =>
        {
            double hz = waterfall.CursorHz;
            if (hz <= 0) { wfCursorBox.A.Text = "—"; wfCursorBox.B.Text = Loc.T("device.lens.move_over_the_graph"); return; }
            wfCursorBox.A.Text = Inv($"{Hz(hz)} · {LensInk.Note(hz)}");
            wfCursorBox.B.Text = Inv($"−{waterfall.CursorAgeSec:0.0} s");
        };

        static Control Tip(Control c, string tip) { ToolTip.SetTip(c, tip); return c; }
        Control WindowSeg() => Seg(WindowP, new[] { "Hann", "B-H", "Flat" });
        Control LogNote() => CardLayout.Pair(Toggle(LogFreqP, "Log frequency"), Toggle(NoteGridP, "Note grid"));

        (Control, Control) SpectrumPanel() => (CardLayout.Head(Toggle(PeakHoldP, "Peak hold"), WindowSeg()), CardLayout.Rows(
            Row("PEAK", PeakTimeP, v => Sec(Exp(v, 0.5, 30))),
            Row("AVG", AverageP, v => Inv($"{1 + v * 15:0}")),
            Row("FLOOR", FloorP, v => Inv($"{-120 + v * 80:0} dB")),
            Row("TOP", ScaleTopP, v => Inv($"{24 - v * 60:0} dB")),
            Row("RANGE", ScaleRangeP, v => Inv($"{30 + v * 90:0} dB")),
            CardLayout.ReadoutBox(specNote.Ctl),
            cursorBox.Box,
            LogNote()));

        // The sidechain key (specs/050): None, or any other track's post-FX audio.
        Control KeyRow()
        {
            var name = new TextBlock { FontSize = 8, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            readouts.Add(() =>
            {
                int src = KeySrc();
                name.Text = !scOk ? Loc.T("device.lens.n_a_in_a_rack") : src < 0 ? Loc.T("device.lens.no_key") : SrcName(src);
                name.Foreground = src >= 0 ? TxtC : Txt2;
            });
            var box = new Border
            {
                Height = 16, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell, Padding = new Thickness(6, 0),
                Cursor = new Cursor(StandardCursorType.Hand), IsEnabled = scOk,
                Child = new DockPanel { Children = { Docked(new Glyph(GlyphKind.ChevronDown, 8) { Foreground = MutedC, Margin = new Thickness(5, 0, 0, 0) }, Dock.Right), name } },
            };
            ToolTip.SetTip(box, Loc.T("device.lens.key_draw_another_tracks_audio"));
            box.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed || !scOk) return;
                var fly = new MenuFlyout();
                int cur = KeySrc();
                void Item(string n, int id)
                {
                    var mi = new MenuItem { Header = n, ToggleType = MenuItemToggleType.Radio, IsChecked = id == cur };
                    mi.Click += (_, _) => { engine.SetDeviceSidechainSource(track, di, id); ctx.NotifyChanged(); foreach (var a in readouts) a(); };
                    fly.Items.Add(mi);
                }
                Item(Loc.T("common.none"), -1);
                fly.Items.Add(new Separator());
                for (int i = 0; i < engine.TrackCount; i++)
                    if (engine.TryGetTrackInfo(i, out var ti) && ti.Id != track) Item(SrcName(ti.Id), ti.Id);
                fly.ShowAt(box);
                e.Handled = true;
            };
            var show = Switch("", () => On(KeyShowP), () => SetP(KeyShowP, On(KeyShowP) ? 0f : 1f), out var showSync);
            readouts.Add(showSync);
            Learn(show, KeyShowP);
            ToolTip.SetTip(show, Loc.T("device.lens.show_the_keys_trace"));
            show.Margin = new Thickness(5, 0, 0, 0);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*,Auto"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps("KEY")); g.Children.Add(Col(box, 1)); g.Children.Add(Col(show, 2));
            return g;
        }

        // The scope's panel: the trace settings, or the cursors' measurements while Cursors is on.
        (Control, Control) ScopePanel()
        {
            var host = new ContentControl();
            Control Traces() => CardLayout.Rows(
                Row("HOLDOFF", HoldoffP, v => Inv($"{v * 200:0} ms")),
                KeyRow(),
                Tip(Row("Key Gain", KeyGainP, v => Inv($"{KeyDb(v):+0.#;−0.#;0}\u2009dB")), Loc.T("device.lens.key_gain_scales_only_the")),
                Row("PERSIST", PersistTimeP, v => Sec(Exp(v, 0.05, 2.0))),
                Row("TRACES", TracesP, v => Inv($"{1 + v * 7:0}")),
                Row("BRIGHT", BrightP, v => Pc(v)),
                captureBox.Box);
            Control Cursors() => CardLayout.Rows(
                measureBox.Box,
                Row("A POS", CursorAP, v => Ms(v * TimeDiv() * 8)),
                Row("B POS", CursorBP, v => Ms(v * TimeDiv() * 8)),
                CardLayout.Pair(
                    Button("Re-arm", Loc.T("device.lens.arm_the_next_single_capture"), () => { engine.DeviceAction(track, di, A_Rearm, 0, 0f); foreach (var a in readouts) a(); }),
                    Button(Loc.T("device.lens.export_wav"), Loc.T("device.lens.write_the_captured_scope_window"), ExportWav)),
                Toggle(CursorSnapP, Loc.T("device.lens.snap_to_peaks")));
            // Both panels are built here: their rows register readouts, and a refresh that built
            // the other one lazily would add to `readouts` while walking it.
            var tracesPanel = Traces();
            var cursorsPanel = Cursors();
            readouts.Add(() => { var want = On(CursorsP) ? cursorsPanel : tracesPanel; if (!ReferenceEquals(host.Content, want)) host.Content = want; });
            host.Content = On(CursorsP) ? cursorsPanel : tracesPanel;
            return (CardLayout.Head(Toggle(CursorsP, "Cursors"), Seg(LayoutP, new[] { "Overlay", "Stacked" })), host);
        }

        (Control, Control) WaterfallPanel() => (CardLayout.Head(CardLayout.Title(Loc.T("device.lens.analysis")), WindowSeg()), CardLayout.Rows(
            Row("FFT", FftSizeP, v => Inv($"{FftSizes[Math.Clamp((int)Math.Round(v * 3), 0, 3)]}")),
            Row("OVERLAP", WfOverlapP, v => Inv($"{Overlaps[Math.Clamp((int)Math.Round(v * 3), 0, 3)]:0.#} %")),
            Row("AVG", AverageP, v => Inv($"{1 + v * 15:0}")),
            Row("RATE", RateP, v => Inv($"{10 + v * 50:0} fps")),
            wfCursorBox.Box,
            CardLayout.ReadoutBox(wfNote.Ctl),
            LogNote()));

        bool mini = CardLayout.IsMini(ctx, di);
        int centreTab = mini ? 0 : Sel(GraphP, 3);
        // ---- assembly -----------------------------------------------------------------
        var freezeToggle = Switch("Freeze", () => On(FreezeP), () => SetP(FreezeP, On(FreezeP) ? 0f : 1f), out var freezeSync);
        readouts.Add(freezeSync);
        Learn(freezeToggle, FreezeP);
        ToolTip.SetTip(freezeToggle, Loc.T("device.lens.freeze_stop_capturing_and_hold"));

        var headNote = Mono("", 7, MutedC);
        var extras = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children = { Seg(SourceP, new[] { "L+R", "L", "R" }), freezeToggle, headNote } };
        // The pop-out window (specs/050) is the header's pop-out key now, shared by every XL device (specs/085).

        Action<int>? selectCentre = null;
        Control root = mini
            ? CardLayout.Mini(spectrum, Seg(SourceP, new[] { "L+R", "L", "R" }), SpecK(SmoothP), SpecK(DecayP), SpecK(TiltP), SpecK(FloorP))
            : CardLayout.Large(new[] { "Spectrum", "Scope", "Waterfall" }, centreTab,
                t => t switch { 1 => ScopeTab(), 2 => WaterfallTab(), _ => SpectrumTab() },
                t => t switch { 1 => ScopePanel(), 2 => WaterfallPanel(), _ => SpectrumPanel() },
                extras,
                new Border(),
                t => { centreTab = t; SetP(GraphP, t / 2f); foreach (var a in readouts) a(); },
                sel => selectCentre = sel);

        // ---- WAV export of the captured scope window --------------------------------------
        async void ExportWav()
        {
            var top = TopLevel.GetTopLevel(scopeView);
            if (top is null) return;
            var raw = new float[65536];
            int n = engine.DeviceLayerWave(track, di, L_Raw, raw, raw.Length);
            if (n <= 0) return;
            var file = await top.PickSaveFileAsync(new FilePickerSaveOptions
            {
                Title = Loc.T("device.lens.export_the_lens_capture_to"),
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

            // The graph moved elsewhere (the pop-out window or the card, undo, MCP): follow it.
            if (!mini && Sel(GraphP, 3) is var graph && graph != centreTab)
            {
                centreTab = graph;
                selectCentre?.Invoke(graph);
                foreach (var a in readouts) a();
            }

            if (centreTab == 0)
            {
                engine.DeviceLayerWave(track, di, L_Spectrum, data.Spec, LensData.Curve);
                engine.DeviceLayerWave(track, di, L_Peak, data.Peak, LensData.Curve);
                spectrum.Tick();
            }
            else if (centreTab == 1)
            {
                data.SyncOn = On(SyncP);
                data.KeyOn = On(KeyShowP) && KeySrc() >= 0 && Sc(M_KeyLive) > 0.5;
                data.KeyGain = Math.Pow(10, KeyDb(P(KeyGainP)) / 20);
                data.Stacked = On(LayoutP);
                data.KeyName = KeySrc() >= 0 ? SrcName(KeySrc()) : "key";
                if (data.SyncOn) engine.DeviceLayerWave(track, di, L_Sync, data.Sync, data.Sync.Length);
                else if (data.KeyOn) engine.DeviceLayerWave(track, di, L_KeyTrace, data.KeyTrace, LensData.TraceN);
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


            int fftN = (int)Sc(M_FftN);
            string winName = new[] { "Hann", "B-H", "Flat" }[Sel(WindowP, 3)];
            bool synced = data.SyncOn && data.Sync[LensData.S_Mode] > 0.5f;
            headNote.Text = centreTab == 1
                ? (data.Held ? "Held" : data.SyncOn ? (synced ? "Sync" : "Free") : data.TrigOk ? "Trig’d" : "Free")
                : centreTab == 2 ? Inv($"{LensData.WfBins} bins") : Inv($"FFT {fftN}");

            specInfo.Text = Loc.F("device.lens.window_ms_slope_db_oct", Sc(M_FftMs), P(TiltP) * 9);
            scopeInfo.Text = data.SyncOn
                ? Inv($"{LensScopeView.SpanName(data.Sync[LensData.S_SpanBeats], data.Sync[LensData.S_BeatsPerBar])} · {data.Sync[LensData.S_Bpm]:0.#} BPM{(synced ? "" : " · stopped")}")
                : Loc.F("device.lens.level_holdoff_ms", (P(TrigLevelP) - 0.5) * 2, P(HoldoffP) * 200);
            wfInfo.Text = Inv($"floor {-120 + P(WfFloorP) * 80:0} dB · {(On(LogFreqP) ? "log" : "linear")} freq");

            specNote.A.Text = Inv($"peak {Sc(M_PeakDb):0.0} dB @ {Hz(Sc(M_PeakHz))}");
            specNote.B.Text = Loc.F("device.lens.crest_db_lufs", Sc(M_CrestDb), Sc(M_Lufs));
            scopeNote.A.Text = Sc(M_PeriodMs) > 0 ? Loc.F("device.lens.period_ms_hz", Sc(M_PeriodMs), Sc(M_FreqHz)) : "period —";
            scopeNote.B.Text = Loc.F("device.lens.vpp_rms", Sc(M_Vpp), Sc(M_Vrms));
            wfNote.A.Text = Loc.F("device.lens.row_ms_bins", WfSpan() / LensData.WfRows * 1000, LensData.WfBins);
            wfNote.B.Text = Loc.F("device.lens.peak_db", Hz(Sc(M_PeakHz)), Sc(M_PeakDb));

            captureBox.A.Text = (data.Held ? "held" : data.TrigOk ? "stable" : "free-run") + " · " + scopeNote.A.Text;
            captureBox.B.Text = scopeNote.B.Text;

            double dtSec = Math.Abs(P(CursorBP) - P(CursorAP)) * TimeDiv() * 8;
            measureBox.A.Text = Inv($"Δt {dtSec * 1000:0.000} ms → {(dtSec > 1e-9 ? 1 / dtSec : 0):0.0} Hz");
            measureBox.B.Text = Inv($"ΔV {Sc(M_CursorBv) - Sc(M_CursorAv):0.000} · {LensInk.Note(dtSec > 1e-9 ? 1 / dtSec : 0)}");
        }

        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
