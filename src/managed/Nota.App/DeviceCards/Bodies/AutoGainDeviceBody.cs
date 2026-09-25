// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Level body (loudness leveler, device kind 18), a build of the
// "Nota Level" mockup (700 × 260) in the EQ-8 / Forge language. On the left the CORRECTION:
// Manual / Auto, a fader around 0 dB (Auto rides it in teal; in Manual drag it), the gain applied
// in large type with why it is held (max gain, true-peak), MATCH (Manual: set the gain to the
// distance to the target) / RESET (Auto: restart the measurement from now), Target and Trim.
// Click the Target value for the standards or a reference track — routed, the target follows
// that track's loudness (the device's sidechain). In the centre the Scale (Mom / Short / Integ)
// over 8 s of loudness — input, output and the target with its ±1 LU band (drag the line for
// Target) — and a readout row. On the right the METERS (IN / OUT against the target, TP against
// the ceiling, the correction), RESPONSE (Fast / Slow, Window, Max Gain) and TRUE-PEAK SAFE with
// its ceiling (drag the dBTP). A status strip says what the leveler is doing. Every control is a
// device param (normalized 0..1), so automation / MIDI learn / presets / A-B / persistence come
// for free; double-click resets. FullBleed — the shared shell draws the header (name · preset ·
// badge · bypass).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;
using static Nota.App.LevelMath;

namespace Nota.App;

internal sealed class AutoGainDeviceBody : IDeviceBody
{
    // Target standards offered by the Target value's menu.
    private static readonly (string Label, double Lufs)[] Standards =
    {
        ("−9\u2009LUFS · club", -9), ("−11\u2009LUFS · loud master", -11), ("−14\u2009LUFS · streaming", -14), ("−16\u2009LUFS · podcast, Apple", -16),
        ("−18\u2009LUFS · audiobook", -18), ("−23\u2009LUFS · EBU R128", -23), ("−24\u2009LUFS · ATSC A/85", -24), ("−27\u2009LUFS · cinema dialogue", -27),
    };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "LEVELER";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        int pc = engine.DeviceParamCount(track, di);
        float P(int p) => p < pc ? engine.DeviceGetParam(track, di, p) : 0.5f;
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, double v) { if (p < pc) engine.DeviceSetParam(track, di, p, (float)Math.Clamp(v, 0, 1)); }
        void SetP(int p, double v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        double Def(int p) => engine.DeviceParamDefault(track, di, p);
        bool On(int p) => P(p) >= 0.5f;
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var sc = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? sc[i] : 0;
        bool Active() => !engine.DeviceBypassed(track, di);
        bool Manual() => !On(Auto);
        bool Primed() => Sc(S_Primed) > 0.5;
        double MaxG() => MaxGainDb(P(MaxGain));

        // ---- reference track (the device's sidechain) ---------------------------------------
        string TrackName(int id) => TrackNames.Of(engine, id);
        int Ref() => engine.DeviceSidechainSource(track, di);
        bool HasRef() => Ref() >= 0;

        // ---- units ---------------------------------------------------------------------------
        static string TargetF(double v) => Lufs(TargetLufs(v)) + "\u2009LUFS";
        static string TrimF(double v) => Sgn(TrimDb(v)) + "\u2009dB";
        static string WindowF(double v) => NotaNum.F($"{WindowS(v):0.0}\u2009s");
        static string MaxF(double v) => NotaNum.F($"{MaxGainDb(v):0}\u2009dB");

        // ---- small builders ------------------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
        { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static Border Island(Control child, double width = double.NaN) => new()
        {
            Width = width, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
            ClipToBounds = true, Child = child,
        };
        static Border Bar(Control child, Thickness pad) => new()
        { Height = 20, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = pad, Child = child };
        static Control Rule(Control c) => new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = c };

        // Slider row: caps 7 label · 3px track · mono 8 value (brass-light once off its default).
        // `valueHost` replaces the plain value (the Target's menu); `dim` greys the track.
        Control Slider(string label, int p, Func<double, string> fmt, string tip, double labelW, double valueW,
            bool bipolar = false, Func<bool>? dim = null, Func<(string Text, IBrush? Ink)>? valueOverride = null, Action<Control>? valueClick = null)
        {
            var lbl = Caps(label);
            lbl.TextTrimming = TextTrimming.None;
            var trk = new SliderTrack { Bipolar = bipolar, Reset = () => { Reset(p); RefreshAll(); }, VerticalAlignment = VerticalAlignment.Center };
            trk.Changed += v => { Raw(p, v); RefreshAll(); };
            trk.GestureBegin += () => Begin(p);
            trk.GestureEnd += () => End(p);
            var val = Mono("", 8, TextPrimary);
            val.TextAlignment = TextAlignment.Right;
            val.HorizontalAlignment = HorizontalAlignment.Right;
            Control valCtl = val;
            if (valueClick is not null)
            {
                var box = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = val };
                box.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                    valueClick(box); e.Handled = true;
                };
                valCtl = box;
            }
            var g = new Grid { ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(labelW)));
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(valueW)));
            g.Children.Add(lbl); g.Children.Add(Col(trk, 1)); g.Children.Add(Col(valCtl, 2));
            readouts.Add(() =>
            {
                if (!trk.Dragging) trk.Norm = P(p);
                bool d = dim?.Invoke() ?? false;
                trk.IsDim = d;
                var ov = valueOverride?.Invoke();
                if (ov is { } o && o.Text.Length > 0) { val.Text = o.Text; val.Foreground = o.Ink ?? TextPrimary; }
                else
                {
                    val.Text = fmt(P(p));
                    val.Foreground = d ? TextTertiary : Math.Abs(P(p) - Def(p)) > 0.003 ? AccentBright : TextPrimary;
                }
            });
            Learn(g, p);
            ToolTip.SetTip(g, tip);
            return g;
        }

        Border Seg(string[] names, Func<int> cur, Action<int> pick, int p, string tip, double width)
        {
            var s = Segments(names, cur, i => { pick(i); RefreshAll(); }, out var sync, fill: true, padX: 2);
            s.Width = width;
            readouts.Add(sync);
            Learn(s, p);
            ToolTip.SetTip(s, tip);
            return s;
        }

        // A dropdown menu under a control.
        void Menu(Control at, IEnumerable<(string Label, bool Current, Action Pick)?> items)
        {
            var fly = new MenuFlyout();
            foreach (var it in items)
            {
                if (it is not { } x) { fly.Items.Add(new Separator()); continue; }
                var mi = new MenuItem { Header = x.Label };
                if (x.Current) mi.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brass };
                var pk = x.Pick;
                mi.Click += (_, _) => { pk(); RefreshAll(); };
                fly.Items.Add(mi);
            }
            fly.ShowAt(at);
        }

        void TargetMenu(Control at)
        {
            var list = new List<(string, bool, Action)?>();
            foreach (var (label, lufs) in Standards)
            {
                double v = TargetNorm(lufs);
                list.Add((label, !HasRef() && Math.Abs(TargetLufs(P(Target)) - lufs) < 0.05, () =>
                {
                    if (HasRef()) { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); }
                    SetP(Target, v);
                }));
            }
            list.Add(null);
            list.Add(("Fixed target — no reference", !HasRef(), () => { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); }));
            for (int i = 0; i < engine.TrackCount; i++)
                if (engine.TryGetTrackInfo(i, out var ti) && ti.Id != track)
                {
                    int id = ti.Id;
                    list.Add(("Match track: " + TrackName(id), Ref() == id, () => { engine.SetDeviceSidechainSource(track, di, id); ctx.NotifyChanged(); }));
                }
            Menu(at, list);
        }

        // ======================================================================
        // LEFT — correction
        // ======================================================================
        var modeSeg = Seg(new[] { "Manual", "Auto" }, () => Manual() ? 0 : 1, i =>
        {
            if (i == 0 && !Manual())
            {
                // Hand over: Manual starts from the gain Auto was riding.
                double g = Math.Round(Math.Clamp(Sc(S_Level), -MaxG(), MaxG()) * 10) / 10;
                SetP(Gain, GainNorm(g));
                SetP(Auto, 0);
            }
            else if (i == 1) SetP(Auto, 1);
        }, Auto, "Auto rides the gain to the target over Window; Manual holds the fader where you put it (Match sets it to the distance to the target)", 84);
        var corrHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        corrHead.Children.Add(Caps("CORRECTION")); corrHead.Children.Add(Col(modeSeg, 1));

        var fader = new LvGainFader { Value = () => GainDb(P(Gain)), VerticalAlignment = VerticalAlignment.Stretch };
        fader.GestureBegin += () => Begin(Gain);
        fader.GestureEnd += () => End(Gain);
        fader.Changed += db => { Raw(Gain, GainNorm(db)); RefreshAll(); };
        fader.ResetRequested += () => { Reset(Gain); RefreshAll(); };
        Learn(fader, Gain);
        ToolTip.SetTip(fader, "The correction around 0\u2009dB, ±Max Gain. In Manual drag it (Shift for fine steps); double-click resets to 0\u2009dB. In Auto it shows the ride");

        var maxTop = Mono("", 7, TextDisabled);
        var modeTag = Mono("", 7, TextDisabled);
        var maxBot = Mono("", 7, TextDisabled);
        var big = Mono("", 26, AccentBright); big.FontWeight = FontWeight.Bold; big.LineHeight = 28; big.LetterSpacing = -0.5;
        var bigSub = new TextBlock { Text = "dB applied", FontSize = 8, Foreground = TextSecondary };
        var reason = Mono("", 7, TextTertiary); reason.Margin = new Thickness(0, 2, 0, 0);
        var matchTxt = new TextBlock { FontSize = 8, FontWeight = FontWeight.Bold, LetterSpacing = 0.5, Foreground = OnAccent };
        var match = new Border
        {
            Background = Brass, CornerRadius = NotaRadius.Badge, Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Bottom, Child = matchTxt,
        };
        match.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(match).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (Manual())
            {
                if (!Primed()) return;
                double want = Math.Clamp(Sc(S_Target) - Sc(S_Measured) - TrimDb(P(Trim)), -MaxG(), MaxG());
                SetP(Gain, GainNorm(Math.Round(want * 10) / 10));
            }
            else engine.DeviceAction(track, di, 0, 0, 0);
            RefreshAll();
        };
        readouts.Add(() =>
        {
            matchTxt.Text = Manual() ? "MATCH" : "RESET";
            ToolTip.SetTip(match, Manual()
                ? "Match — set the manual gain to the distance between the measured input and the target"
                : "Reset — restart the measurement from the loudness now, so the ride catches up at once");
        });

        var topLine = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        topLine.Children.Add(maxTop); topLine.Children.Add(Col(modeTag, 1));
        var botLine = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = VerticalAlignment.Bottom };
        botLine.Children.Add(maxBot); botLine.Children.Add(Col(match, 1));
        maxBot.VerticalAlignment = VerticalAlignment.Bottom;
        var numCol = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto") };
        numCol.Children.Add(GRow(topLine, 0));
        numCol.Children.Add(GRow(new StackPanel { Spacing = 1, Children = { big, bigSub, reason } }, 2));
        numCol.Children.Add(GRow(botLine, 4));
        var gainArea = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
        gainArea.Children.Add(fader); gainArea.Children.Add(Col(numCol, 1));

        var targetRow = Slider("TARGET", Target, TargetF,
            "Target loudness, −36 … 0 LUFS. Click the value for the standards or to match another track's loudness (a reference); drag the dashed line in the graph for the same",
            38, 50, dim: HasRef,
            valueOverride: () => HasRef() ? ("REF " + TrackName(Ref()), TextSecondary) : ("", null),
            valueClick: TargetMenu);
        var trimRow = Slider("TRIM", Trim, TrimF, "Trim — a fixed offset after the correction, −12 … +12\u2009dB (the output then sits that far off the target)", 38, 50, bipolar: true);

        var leftBody = new DockPanel { Margin = new Thickness(8, 6), LastChildFill = true };
        leftBody.Children.Add(Docked(new StackPanel { Spacing = 5, Margin = new Thickness(0, 6, 0, 0), Children = { targetRow, trimRow } }, Dock.Bottom));
        leftBody.Children.Add(gainArea);
        var left = Island(new DockPanel { Children = { Docked(Bar(corrHead, new Thickness(8, 0, 3, 0)), Dock.Top), leftBody } }, 176);
        DockPanel.SetDock(left, Dock.Left);

        // ======================================================================
        // CENTRE — scale + loudness history
        // ======================================================================
        var scaleSeg = Seg(ScaleNames, () => ScaleIndex(P(Scale)), i => SetP(Scale, i / 2.0), Scale,
            "Scale — what the correction reads: Momentary (400\u2009ms, fast and nervous), Short-term (3\u2009s) or Integrated (12\u2009s with BS.1770 gating, the steadiest)", 118);
        var legend = new LvLegend { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var centreHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8 };
        centreHead.Children.Add(Caps("SCALE")); centreHead.Children.Add(Col(scaleSeg, 1)); centreHead.Children.Add(Col(legend, 2));

        var hist = new LvHistoryView { Value = () => P(Target) };
        hist.GestureBegin += () => Begin(Target);
        hist.GestureEnd += () => End(Target);
        hist.Changed += v => { Raw(Target, v); RefreshAll(); };
        hist.ResetRequested += () => { Reset(Target); RefreshAll(); };
        Learn(hist, Target);
        ToolTip.SetTip(hist, "The last 8\u2009s: input, output and the target with its ±1\u2009LU band (momentary loudness). Drag up / down to move the target; double-click resets it");

        IBrush Dim() => TextDisabled;
        var rIn = Mono("", 8, TextSecondary); var rOut = Mono("", 8, AccentBright); var rDelta = Mono("", 8, TextSecondary); var rTp = Mono("", 8, TextSecondary);
        var readRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Height = 14, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { rIn, Mono("·", 8, Dim()), rOut, Mono("·", 8, Dim()), rDelta, Mono("·", 8, Dim()), rTp },
        };
        var centreBody = new DockPanel { Margin = new Thickness(5), LastChildFill = true };
        centreBody.Children.Add(Docked(new Border { Margin = new Thickness(0, 4, 0, 0), Child = readRow }, Dock.Bottom));
        centreBody.Children.Add(hist);
        var centre = Island(new DockPanel { Children = { Docked(Bar(centreHead, new Thickness(6, 0)), Dock.Top), centreBody } });
        centre.Margin = new Thickness(5, 0);

        // ======================================================================
        // RIGHT — meters, response, true-peak safe
        // ======================================================================
        var scaleName = Mono("", 7, TextTertiary);
        var metersHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        metersHead.Children.Add(Caps("METERS")); metersHead.Children.Add(Col(scaleName, 1));

        (Control Row, LvMeter M, TextBlock V) MeterRow(string label, string tip)
        {
            var m = new LvMeter { VerticalAlignment = VerticalAlignment.Center };
            var v = Mono("", 8, TextPrimary); v.TextAlignment = TextAlignment.Right; v.HorizontalAlignment = HorizontalAlignment.Right;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,34"), ColumnSpacing = 6, Background = Brushes.Transparent };
            var l = Caps(label); l.LetterSpacing = 0.4;
            g.Children.Add(l); g.Children.Add(Col(m, 1)); g.Children.Add(Col(v, 2));
            ToolTip.SetTip(g, tip);
            return (g, m, v);
        }
        var mIn = MeterRow("IN", "Input loudness on the chosen scale (LUFS); the teal mark is the target");
        var mOut = MeterRow("OUT", "Output loudness on the chosen scale (LUFS); the teal mark is the target");
        var mTp = MeterRow("TP", "Output true peak (dBTP); the teal mark is the ceiling while True-peak safe is on");
        var mCor = MeterRow("COR", "The correction applied, ±Max Gain around 0\u2009dB (trim not included)");

        var respSeg = Seg(new[] { "Fast", "Slow" }, () => On(Response) ? 1 : 0, i => SetP(Response, i), Response,
            "Response — Slow glides over the whole Window with a 20\u2009ms look-ahead; Fast over a quarter of it with 5\u2009ms", 76);
        var respRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        respRow.Children.Add(Caps("RESPONSE", Teal)); respRow.Children.Add(Col(respSeg, 1));
        var winRow = Slider("WINDOW", WindowP, WindowF, "Window — how long the ride takes to reach the target, 0.4 … 10\u2009s (Fast: a quarter of it)", 48, 36);
        var maxRow = Slider("MAX GAIN", MaxGain, MaxF, "Max gain — the most the correction may boost or cut, 0 … 24\u2009dB", 48, 36);

        var safeSw = Switch("True-peak safe", () => On(Safe), () => { SetP(Safe, On(Safe) ? 0 : 1); RefreshAll(); }, out var safeSync);
        readouts.Add(safeSync);
        Learn(safeSw, Safe);
        ToolTip.SetTip(safeSw, "True-peak safe — a look-ahead limiter keeps the output under the ceiling (adds 5 / 20\u2009ms of latency, compensated)");
        var ceilTxt = Mono("", 8, TextPrimary);
        var ceilBox = new Border
        {
            Background = Sunken, CornerRadius = NotaRadius.Clip, Padding = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = ceilTxt,
        };
        bool cd = false; double cy0 = 0, cv0 = 0;
        ceilBox.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(ceilBox).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { Reset(Ceiling); RefreshAll(); e.Handled = true; return; }
            cd = true; cy0 = e.GetPosition(ceilBox).Y; cv0 = P(Ceiling); Begin(Ceiling); e.Pointer.Capture(ceilBox); e.Handled = true;
        };
        ceilBox.PointerMoved += (_, e) =>
        {
            if (!cd) return;
            bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            double v = cv0 + (cy0 - e.GetPosition(ceilBox).Y) / 140.0 * (fine ? 0.1 : 1);
            Raw(Ceiling, Math.Round(Math.Clamp(v, 0, 1) * 60) / 60);   // 0.1 dB steps
            RefreshAll();
        };
        ceilBox.PointerReleased += (_, e) => { if (cd) { cd = false; End(Ceiling); e.Pointer.Capture(null); } };
        Learn(ceilBox, Ceiling);
        ToolTip.SetTip(ceilBox, "Ceiling, −6 … 0\u2009dBTP — drag up / down (Shift for fine steps), double-click resets to −1.0");
        readouts.Add(() =>
        {
            ceilTxt.Text = NotaNum.F($"{CeilingDb(P(Ceiling)):0.0;−0.0;0.0}\u2009dBTP");
            ceilTxt.Foreground = !On(Safe) ? TextDisabled : Math.Abs(P(Ceiling) - Def(Ceiling)) > 0.003 ? AccentBright : TextPrimary;
        });
        var safeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        safeRow.Children.Add(safeSw); safeRow.Children.Add(Col(ceilBox, 1));

        var rightBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,*,Auto,*,Auto,Auto,Auto,*,Auto"), Margin = new Thickness(8, 6) };
        rightBody.Children.Add(GRow(mIn.Row, 0));
        rightBody.Children.Add(GRow(mOut.Row, 2));
        rightBody.Children.Add(GRow(mTp.Row, 4));
        rightBody.Children.Add(GRow(mCor.Row, 6));
        rightBody.Children.Add(GRow(Rule(respRow), 8));
        rightBody.Children.Add(GRow(new Border { Margin = new Thickness(0, 4, 0, 0), Child = winRow }, 9));
        rightBody.Children.Add(GRow(new Border { Margin = new Thickness(0, 3, 0, 0), Child = maxRow }, 10));
        rightBody.Children.Add(GRow(Rule(safeRow), 12));
        var right = Island(new DockPanel { Children = { Docked(Bar(metersHead, new Thickness(8, 0)), Dock.Top), rightBody } }, 176);
        DockPanel.SetDock(right, Dock.Right);

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, TextSecondary);
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);
        ToolTip.SetTip(statusLeft, "What the leveler is doing now");

        // ---- live state ---------------------------------------------------------------------
        static double MeterX(double lufs) => Math.Clamp((lufs - LevelMath.LBot) / (LevelMath.LTop - LevelMath.LBot), 0, 1);
        readouts.Add(() =>
        {
            bool on = Active(), manual = Manual(), primed = Primed(), silent = Sc(S_Silent) > 0.5;
            double maxG = MaxG(), trim = TrimDb(P(Trim));
            double level = Sc(S_Level), lim = Sc(S_LimGr), g = level - lim;   // the correction as heard (no trim)
            double target = Sc(S_Target), measured = Sc(S_Measured), outMeas = Sc(S_OutMeasured), tp = Sc(S_TruePeak);
            double delta = Sc(S_Delta);
            bool locked = on && primed && Math.Abs(delta) < 1;
            bool tpOver = tp > CeilingDb(P(Ceiling)) + 0.05 && On(Safe);
            int clamp = (int)Math.Round(Sc(S_Clamp));
            bool tpClamp = on && On(Safe) && Sc(S_LimHold) > 0.3;
            if (!primed) target = HasRef() ? target : TargetLufs(P(Target));

            // Correction column.
            fader.Set(!on ? 0 : manual ? Math.Clamp(GainDb(P(Gain)), -maxG, maxG) : g, maxG, manual, on);
            maxTop.Text = NotaNum.F($"+{maxG:0}");
            maxBot.Text = NotaNum.F($"−{maxG:0}");
            modeTag.Text = manual ? "fader" : "auto";
            big.Text = on ? Sgn(g) : "off";
            big.Foreground = on ? AccentBright : TextDisabled;
            string why = !on ? "" : !primed ? "waiting for signal" : silent ? "silence · holding"
                : tpClamp ? "limited by true-peak" : clamp == 1 && !manual ? "limited by max gain" : clamp == 2 && !manual ? "limited by max cut"
                : manual ? "manual" : "auto · " + (On(Response) ? "slow" : "fast");
            reason.Text = why;
            reason.Foreground = on && (tpClamp || (clamp > 0 && !manual)) ? AccentBright : TextTertiary;

            // Graph.
            string tl = HasRef() ? "REF " + Lufs(target) : Lufs(target) + "\u2009LUFS";
            hist.SetState(on, locked, !on ? "bypass" : !primed ? "waiting" : silent ? "holding" : locked ? "locked ±1\u2009LU" : "tracking", tl, !HasRef());
            rIn.Text = "IN " + Lufs(measured);
            rOut.Text = "OUT " + Lufs(outMeas);
            rDelta.Text = "Δ " + (primed ? Sgn(delta) : "—") + "\u2009LU";
            rDelta.Foreground = locked || !primed ? TextSecondary : AccentBright;
            rTp.Text = "TP " + Lufs(tp) + "\u2009dB";
            rTp.Foreground = tpOver ? Danger : TextSecondary;

            // Meters.
            int si = ScaleIndex(P(Scale));
            scaleName.Text = ScaleLong[si];
            double tx = MeterX(target);
            mIn.M.Set(0, MeterX(measured), TextTertiary, tx); mIn.V.Text = Lufs(measured);
            mOut.M.Set(0, MeterX(outMeas), on ? Brass : TextTertiary, tx); mOut.V.Text = Lufs(outMeas);
            mOut.V.Foreground = locked || !primed ? TextPrimary : AccentBright;
            mTp.M.Set(0, MeterX(tp), tpOver ? Danger : TextSecondary, On(Safe) ? MeterX(CeilingDb(P(Ceiling))) : double.NaN);
            mTp.V.Text = Lufs(tp); mTp.V.Foreground = tpOver ? Danger : TextPrimary;
            double cf = Math.Clamp(g / Math.Max(0.01, maxG), -1, 1) / 2;
            mCor.M.Set(0.5, 0.5 + cf, Teal, 0.5); mCor.V.Text = Sgn(on ? g : 0);

            // Status.
            string foot; IBrush footInk = TextSecondary;
            double matchG = Math.Clamp(target - measured - trim, -maxG, maxG);
            if (!on) foot = "Leveler bypassed: the output equals the input";
            else if (!primed) foot = "Waiting for signal — the gain holds until the input has been measured";
            else if (silent) foot = "Input silent: the measurement and the gain hold";
            else if (manual && Math.Abs(delta) >= 1) { foot = $"Output {Sgn(delta)}\u2009LU from the target · Match sets {Sgn(matchG)}\u2009dB"; footInk = AccentBright; }
            else if (tpClamp) { foot = $"True-peak limits the gain to the {NotaNum.F($"{CeilingDb(P(Ceiling)):0.0;−0.0;0.0}")}\u2009dBTP ceiling"; footInk = AccentBright; }
            else if (clamp > 0 && !manual) { foot = NotaNum.F($"Needs more than {maxG:0} dB: raise Max gain or Trim"); footInk = AccentBright; }
            else
            {
                foot = (locked ? "On target" : "Catching up") + " · " + ScaleLong[si] + " · window " + WindowF(P(WindowP));
                if (HasRef()) foot += " · matching " + TrackName(Ref());
                else if (Math.Abs(trim) > 0.05) foot += " · trim " + Sgn(trim) + "\u2009dB";
            }
            statusLeft.Text = foot; statusLeft.Foreground = footInk;
            double sr = Sc(S_SampleRate);
            string la = On(Safe) ? (On(Response) ? "look-ahead 20\u2009ms" : "look-ahead 5\u2009ms") : "look-ahead off";
            statusRight.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#}\u2009kHz · {ScaleNames[si].ToLowerInvariant()} · {la} · corr {Sc(S_Corr):+0.00;−0.00;0.00}") : la;
        });

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { left, right, centre } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

        // One history sample per UI tick (the graph spans N ticks ≈ 8 s); edits only repaint.
        void Tick()
        {
            scN = engine.DeviceScope(track, di, sc, kScope);
            bool on = Active();
            float tgt = Primed() || HasRef() ? (float)Sc(S_Target) : (float)TargetLufs(P(Target));
            hist.Push(on ? (float)Sc(S_InMom) : -120f, (float)(on ? Sc(S_OutMom) : Sc(S_InMom)), tgt);
            RefreshAll();
            hist.InvalidateVisual();
        }
        ctx.AddDeviceRefresher(Tick);
        Tick();
        return root;
    }
}
