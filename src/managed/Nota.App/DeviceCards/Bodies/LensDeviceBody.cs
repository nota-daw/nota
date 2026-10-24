// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Lens body (device kind 22, analyzer), a build of the
// "Nota Lens" picture of the style guide (700 × 260): a permanent Scale rail of two upright
// value bars on the left, one large graph in the middle behind Spectrum / Scope / Waterfall
// tabs and an Analyze / Display panel on the right, each a pressed-in section. The scope behaves like a bench
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


    /// <summary>The right panel's tab survives a card rebuild (keyed by track + device slot).</summary>
    private static readonly Dictionary<(int, int), int> RightTab = new();

    public double Width => 700;
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

        // ---- small builders --------------------------------------------------------------
        Control Learn(Control c, int p) { MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p)); return c; }

        Control K(int p, string name, Func<double, string> fmt)
        {
            var val = DeviceBodyKit.Value(fmt(P(p)));
            var knob = new Knob(P(p), 1.0) { Accent = true, Default = Def(p) };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
            knob.ValueChanged += v => { Raw(p, (float)v); val.Text = fmt(v); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            readouts.Add(() => { if (knob.Dragging || !knob.IsEffectivelyVisible) return; float c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(c); });
            return KnobCell(name, knob, val, 52);
        }

        Control Toggle(int p, string label)
        {
            var wrap = Switch(label, () => On(p), () => SetP(p, On(p) ? 0f : 1f), out var sync);
            readouts.Add(sync);
            return Learn(wrap, p);
        }

        // Two switches sharing a row.
        Control TogglePair(Control a, Control b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(a); g.Children.Add(DeviceBodyKit.At(b, 1));
            return g;
        }

        Control Seg(int p, string[] names, Action? changed = null, bool fill = false)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); changed?.Invoke(); }, out var sync, fill: fill);
            readouts.Add(sync);
            return Learn(seg, p);
        }

        // A labelled choice row in the right panel: label · segments.
        Control SegRow(string label, int p, string[] names)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("50,*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(DeviceBodyKit.Label(label));
            g.Children.Add(DeviceBodyKit.At(Seg(p, names, fill: true), 1));
            return g;
        }

        Control Row(string label, int p, Func<double, string> fmt)
        {
            var bar = DeviceCardKit.SliderRow(label, () => Math.Clamp(P(p), 0, 1), v => Raw(p, (float)v), () => fmt(Math.Clamp(P(p), 0, 1)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => SetP(p, Def(p)), labelWidth: 42, valueWidth: 48);
            readouts.Add(() => { if (bar.IsEffectivelyVisible) sync(); });
            return Learn(bar, p);
        }

        // A readout well: its name over one or two lines.
        (Border Box, TextBlock A, TextBlock B) ReadBox(string title, string hintA = "—", string hintB = "")
        {
            var a = DeviceBodyKit.Value(hintA);
            var b = DeviceBodyKit.Note(hintB);
            var box = DeviceBodyKit.Well(new StackPanel
            {
                Spacing = 0, VerticalAlignment = VerticalAlignment.Center,
                Children = { DeviceBodyKit.Label(title), a, b },
            }, new Thickness(8, 2));
            return (box, a, b);
        }

        Control Button(string text, string tip, Action click)
        {
            var b = TextButton(text);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            ((TextBlock)b.Child!).HorizontalAlignment = HorizontalAlignment.Center;
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

        // A tab's body: its choice row, the graph (its summary in the top-right corner) and
        // the knob row with a two-line note beside it.
        Control GraphHost(Control topRow, Control graph, TextBlock info, Control knobRow)
        {
            info.HorizontalAlignment = HorizontalAlignment.Right;
            info.VerticalAlignment = VerticalAlignment.Top;
            info.Margin = new Thickness(0, 4, 8, 0);
            info.IsHitTestVisible = false;
            topRow.Margin = new Thickness(0, 5, 0, 4);
            knobRow.Margin = new Thickness(0, 7, 0, 0);
            return new DockPanel
            {
                LastChildFill = true,
                Children = { DeviceBodyKit.Dock(topRow, Avalonia.Controls.Dock.Top), DeviceBodyKit.Dock(knobRow, Avalonia.Controls.Dock.Bottom), new Panel { Children = { graph, info } } },
            };
        }

        // A two-line summary that sits to the right of a knob row.
        (Control Ctl, TextBlock A, TextBlock B) SideNote()
        {
            var a = DeviceBodyKit.Note(); a.FontSize = NotaType.RowLabel;
            var b = DeviceBodyKit.Note(); b.FontSize = NotaType.RowLabel;
            return (new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 6), Children = { a, b } }, a, b);
        }

        Control KnobRow((Control Ctl, TextBlock A, TextBlock B) note, params Control[] cells)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in cells) sp.Children.Add(c);
            var d = new DockPanel { LastChildFill = true };
            d.Children.Add(DeviceBodyKit.Dock(sp, Avalonia.Controls.Dock.Left));
            note.Ctl.HorizontalAlignment = HorizontalAlignment.Right;
            d.Children.Add(note.Ctl);
            return d;
        }

        var specNote = SideNote();
        var scopeNote = SideNote();
        var wfNote = SideNote();
        TextBlock InfoText() { var t = DeviceBodyKit.Note("", TextSecondary); t.FontSize = NotaType.RowLabel; return t; }
        var specInfo = InfoText();
        var scopeInfo = InfoText();
        var wfInfo = InfoText();

        Control TopRow(string label, Control chips) => new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Height = NotaSize.Seg + 4,
            Children = { DeviceBodyKit.Label(label), chips },
        };

        Control SpectrumTab() => GraphHost(
            TopRow("FFT", Seg(FftSizeP, new[] { "512", "2048", "4096", "16384" })),
            spectrum, specInfo,
            KnobRow(specNote,
                    K(SmoothP, "Smooth", v => Pc(v)),
                    K(DecayP, "Decay", v => Sec(Exp(v, 0.05, 8))),
                    K(TiltP, "Tilt", v => Inv($"{v * 9:0.0} dB")),
                    K(FloorP, "Floor", v => Inv($"{-120 + v * 80:0} dB"))));

        Control ScopeTab()
        {
            var trig = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
                Children = { Seg(TrigModeP, new[] { "Auto", "Normal", "Single" }), Seg(TrigEdgeP, new[] { "↑", "↓" }) },
            };
            return GraphHost(
                TopRow("Trig", trig),
                scopeView, scopeInfo,
                KnobRow(scopeNote,
                        K(TimeDivP, "Time/div", v => Ms(Exp(v, 0.00002, 0.02))),
                        K(VoltDivP, "Volt/div", v => Inv($"{Exp(v, 0.02, 2.0):0.00}")),
                        K(TrigLevelP, "Trig level", v => Inv($"{(v - 0.5) * 2:+0.00;−0.00;0.00}")),
                        K(HoldoffP, "Holdoff", v => Inv($"{v * 200:0} ms"))));
        }

        Control WaterfallTab() => GraphHost(
            TopRow("Speed", Seg(WfSpeedP, new[] { "4 s", "12 s", "60 s" }, () => data.ClearWaterfall())),
            waterfall, wfInfo,
            KnobRow(wfNote,
                    K(WfGainP, "Gain", v => Inv($"{-24 + v * 48:+0;−0;0} dB")),
                    K(WfFloorP, "Floor", v => Inv($"{-120 + v * 80:0} dB")),
                    K(WfContrastP, "Contrast", v => Pc(v)),
                    K(WfOverlapP, "Overlap", v => Inv($"{Overlaps[Math.Clamp((int)Math.Round(v * 3), 0, 3)]:0.#} %"))));

        // ---- right panel --------------------------------------------------------------
        var cursorBox = ReadBox("Cursor", "—", "move over the graph");
        var captureBox = ReadBox("Capture");
        var measureBox = ReadBox("A → B");
        var wfCursorBox = ReadBox("Cursor", "—", "move over the graph");

        spectrum.CursorMoved += () =>
        {
            double hz = spectrum.CursorHz;
            if (hz <= 0) { cursorBox.A.Text = "—"; cursorBox.B.Text = "move over the graph"; return; }
            cursorBox.A.Text = Inv($"{Hz(hz)} · {LensInk.Note(hz)}");
            cursorBox.B.Text = Inv($"{spectrum.CursorDb:0.0} dB · bin {(Sc(M_BinHz) > 0 ? hz / Sc(M_BinHz) : 0):0}");
        };
        waterfall.CursorMoved += () =>
        {
            double hz = waterfall.CursorHz;
            if (hz <= 0) { wfCursorBox.A.Text = "—"; wfCursorBox.B.Text = "move over the graph"; return; }
            wfCursorBox.A.Text = Inv($"{Hz(hz)} · {LensInk.Note(hz)}");
            wfCursorBox.B.Text = Inv($"−{waterfall.CursorAgeSec:0.0} s");
        };

        // The right panel: rows from the top, a switch row at the bottom, the readout well
        // (when there is one) filling between.
        Control Stack(Control? fill, Control bottom, params Control[] top)
        {
            var d = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 5, 0, 0) };
            var sp = new StackPanel { Spacing = 4 };
            foreach (var c in top) sp.Children.Add(c);
            d.Children.Add(DeviceBodyKit.Dock(sp, Avalonia.Controls.Dock.Top));
            bottom.Margin = new Thickness(0, 4, 0, 0);
            d.Children.Add(DeviceBodyKit.Dock(bottom, Avalonia.Controls.Dock.Bottom));
            if (fill is not null) { fill.Margin = new Thickness(0, 4, 0, 0); d.Children.Add(fill); }
            else d.Children.Add(new Panel());
            return d;
        }

        Control SpectrumAnalyze() => Stack(cursorBox.Box,
            TogglePair(Toggle(PeakHoldP, "Peak hold"), Toggle(MidSideP, "Mid / Side")),
            SegRow("Window", WindowP, new[] { "Hann", "B-H", "Flat" }),
            Row("Avg", AverageP, v => Inv($"{1 + v * 15:0}")),
            Row("Peak", PeakTimeP, v => Sec(Exp(v, 0.5, 30))),
            Row("Rate", RateP, v => Inv($"{10 + v * 50:0} fps")));

        Control ScopeAnalyze() => Stack(captureBox.Box,
            TogglePair(Toggle(PersistP, "Afterglow"), Toggle(CursorsP, "Cursors")),
            SegRow("Source", SourceP, new[] { "L+R", "L", "R" }),
            Row("Persist", PersistTimeP, v => Sec(Exp(v, 0.05, 2.0))),
            Row("Traces", TracesP, v => Inv($"{1 + v * 7:0}")),
            Row("Bright", BrightP, v => Pc(v)));

        Control CursorAnalyze() => Stack(measureBox.Box,
            TogglePair(Toggle(CursorSnapP, "Snap to peaks"), Toggle(CursorsP, "Cursors")),
            Row("A pos", CursorAP, v => Ms(v * TimeDiv() * 8)),
            Row("B pos", CursorBP, v => Ms(v * TimeDiv() * 8)),
            new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6,
                Children =
                {
                    Button("Re-arm", "Arm the next Single capture", () => { engine.DeviceAction(track, di, A_Rearm, 0, 0f); foreach (var a in readouts) a(); }),
                    DeviceBodyKit.At(Button("Export WAV", "Write the captured scope window to a WAV file", ExportWav), 1),
                },
            });

        Control WaterfallAnalyze() => Stack(wfCursorBox.Box,
            TogglePair(Toggle(LogFreqP, "Log freq"), Toggle(NoteGridP, "Note grid")),
            SegRow("Window", WindowP, new[] { "Hann", "B-H", "Flat" }),
            Row("FFT", FftSizeP, v => Inv($"{FftSizes[Math.Clamp((int)Math.Round(v * 3), 0, 3)]}")),
            Row("Overlap", WfOverlapP, v => Inv($"{Overlaps[Math.Clamp((int)Math.Round(v * 3), 0, 3)]:0.#} %")),
            Row("Avg", AverageP, v => Inv($"{1 + v * 15:0}")));

        Control DisplayTab() => Stack(null,
            new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    TogglePair(Toggle(MidSideP, "Mid / Side"), Toggle(LogFreqP, "Log freq")),
                    TogglePair(Toggle(NoteGridP, "Note grid"), Toggle(PeakHoldP, "Peak hold")),
                },
            },
            SegRow("Source", SourceP, new[] { "L+R", "L", "R" }),
            Row("Top", ScaleTopP, v => Inv($"{24 - v * 60:0} dB")),
            Row("Range", ScaleRangeP, v => Inv($"{30 + v * 90:0} dB")),
            Row("Rate", RateP, v => Inv($"{10 + v * 50:0} fps")));

        // ---- Scale rail ---------------------------------------------------------------
        // Two upright value bars that retarget when the centre tab changes (dB scale for
        // Spectrum / Waterfall, V and T for Scope); each label sits over its value.
        int centreTab = Sel(ViewP, 3);
        var railLabels = new List<(TextBlock Lbl, Func<string> Text)>();
        DeviceBodyKit.RailFader RailBar(Func<int> param, Func<string> caption, Func<string> text)
        {
            int p0 = param();
            var bar = DeviceBodyKit.Upright(caption(), () => P(param()), v => Raw(param(), (float)v), text, out var val, out var sync,
                begin: () => Begin(param()), end: () => End(param()), reset: () => SetP(param(), Def(param())));
            Learn(bar, p0);
            readouts.Add(sync);
            return new DeviceBodyKit.RailFader(caption(), bar, val);
        }
        var railA = RailBar(() => centreTab == 1 ? VoltDivP : ScaleTopP, () => centreTab == 1 ? "V/div" : "Top",
            () => centreTab == 1 ? Inv($"{VoltDiv():0.00}") : Inv($"{TopDb():0} dB"));
        var railB = RailBar(() => centreTab == 1 ? TimeDivP : ScaleRangeP, () => centreTab == 1 ? "T/div" : "Rng",
            () => centreTab == 1 ? Ms(TimeDiv()) : Inv($"{RangeDb():0} dB"));
        var railBody = DeviceBodyKit.FaderRail("Scale", false, out var railLbls, railA, railB);
        var rail = DeviceBodyKit.Sec(railBody, new Thickness(8, 16, 8, 10), 91);
        DeviceBodyKit.Dock(rail, Avalonia.Controls.Dock.Left);
        TextBlock railLblA = railLbls[0], railLblB = railLbls[1];

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

        var headNote = DeviceBodyKit.Label("");
        var extras = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Children = { freezeToggle, headNote } };

        var centreTabs = DeviceBodyKit.Tabs(new[] { "Spectrum", "Scope", "Waterfall" }, () => centreTab, t =>
        {
            centreTab = t;
            SetP(ViewP, t / 2f);
            centreHost.Content = CentreBody(t);
            rightHost.Content = RightBody(rightTab == 1 ? 3 : t);   // Display is shared; Analyze follows the view
            foreach (var a in readouts) a();
        }, out _, extras);
        centreHost.Content = CentreBody(centreTab);
        var centre = new DockPanel { LastChildFill = true, Children = { DeviceBodyKit.Dock(centreTabs, Avalonia.Controls.Dock.Top), centreHost } };

        var rightTabs = DeviceBodyKit.Tabs(new[] { "Analyze", "Display" }, () => rightTab, t =>
        {
            rightTab = t; RightTab[(track, di)] = t;
            rightHost.Content = RightBody(t == 1 ? 3 : centreTab);
        }, out _);
        rightHost.Content = RightBody(rightTab == 1 ? 3 : centreTab);
        var rightFrame = new DockPanel { LastChildFill = true, Children = { DeviceBodyKit.Dock(rightTabs, Avalonia.Controls.Dock.Top), rightHost } };

        var right = DeviceBodyKit.Sec(rightFrame, SectionPad, 196);
        DeviceBodyKit.Dock(right, Avalonia.Controls.Dock.Right);

        var centreBox = DeviceBodyKit.Sec(centre);
        centreBox.Margin = new Thickness(SectionGap, 0);

        var root = DeviceBodyKit.Frame(new DockPanel { LastChildFill = true, Children = { rail, right, centreBox } });

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

            // The rail labels follow the active view.
            railLblA.Text = centreTab == 1 ? "V/div" : "Top";
            railLblB.Text = centreTab == 1 ? "T/div" : "Rng";

            int fftN = (int)Sc(M_FftN);
            headNote.Text = centreTab == 1
                ? (data.Held ? "Held" : data.TrigOk ? "Trig’d" : "Free")
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
