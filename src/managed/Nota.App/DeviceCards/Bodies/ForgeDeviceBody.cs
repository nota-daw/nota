// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Forge body (multi-stage saturator, device kind 17), a build of
// the "Nota Forge" mockup (700 × 260) in the EQ-8 language: on the left the routing (Serial /
// Parallel / M/S / Multi) over the three stages — each with its on dot, its type and the role it
// plays in the routing, a draggable output trim (drag the dB up / down), Drive and a teal
// Feedback; a click selects the stage. In the centre Amount / Wet / Out over the transfer curve
// (the whole device — the selected stage in M/S and Multi — every stage alone faintly, the
// LFO-modulated curve dashed teal, a node where the input sits; drag it for Amount) and the
// harmonics of a −6 dB sine with THD. On the right the selected stage's shape (type, Bias, Tone,
// Width), the modulation (LFO → Drive, Env → Tone, Rate, Sync) and Oversampling; a status strip
// under it all. Curves, harmonics and meters come from the engine (Forge.h scopeRead). Every
// control is a device param (normalized 0..1), so automation / MIDI learn / presets / A-B /
// persistence come for free. Double-click resets a control.
// FullBleed — the shared shell draws the header (name · preset · badge · bypass).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class ForgeDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Forge.h) ───────────────────────────────
    private const int Amount = 0, Tone = 1, Wet = 2, Output = 3, Bias = 4, WidthP = 5, Routing = 6,
        LfoDrive = 7, EnvTone = 8, LfoRate = 9, LfoSync = 10, S1Type = 11, OS = 26, S1Bias = 27;
    private static int TypeP(int s) => S1Type + s * 5;
    private static int DriveP(int s) => S1Type + 1 + s * 5;
    private static int OutP(int s) => S1Type + 2 + s * 5;
    private static int FbP(int s) => S1Type + 3 + s * 5;
    private static int OnP(int s) => S1Type + 4 + s * 5;
    private static int BiasP(int s) => S1Bias + s * 3;
    private static int ToneP(int s) => S1Bias + 1 + s * 3;
    private static int WidthSP(int s) => S1Bias + 2 + s * 3;

    private static readonly string[] RouteNames = { "Serial", "Parallel", "M/S", "Multi" };
    private static readonly string[] OsNames = { "Off", "2×", "4×", "8×" };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "SATURATION";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        int pc = engine.DeviceParamCount(track, di);
        float P(int p) => p < pc ? engine.DeviceGetParam(track, di, p) : 0.5f;
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, double v) { if (p < pc) engine.DeviceSetParam(track, di, p, (float)Math.Clamp(v, 0, 1)); }
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        double Def(int p) => engine.DeviceParamDefault(track, di, p);
        bool On(int p) => P(p) >= 0.5f;
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        var readouts = new List<Action>();
        // MIDI learn on a control whose param follows the selected stage: rebinds when it moves.
        void LearnFollow(Control c, Func<int> param)
        {
            int bound = param();
            Learn(c, bound);
            readouts.Add(() => { int p = param(); if (p != bound) { bound = p; Learn(c, p); } });
        }
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var scope = new float[ForgeMath.kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        int sel = 0;   // the stage the SHAPE panel edits (UI state)
        Action refreshNow = () => { };
        void Refresh() => refreshNow();
        int RouteI() => Math.Clamp((int)Math.Round(P(Routing) * 3), 0, 3);
        int TypeI(int s) => Math.Clamp((int)Math.Round(P(TypeP(s)) * 5), 0, 5);
        bool Single() => RouteI() >= 2;
        bool Active() => !engine.DeviceBypassed(track, di);
        int OsI() => Math.Clamp((int)Math.Round(P(OS) * 3), 0, 3);

        // ---- units (Forge.h) ----------------------------------------------------------------
        static string Sgn(double v) => NotaNum.F($"{v:+0.0;−0.0;0.0}");
        static string SgnI(double v) => NotaNum.F($"{v:+0;−0;0}");
        static string AmtF(double v) => Sgn(v * 30) + " dB";
        static string OutF(double v) => Sgn((v - 0.5) * 48) + " dB";
        static string TrimF(double v) => Sgn((v - 0.5) * 24) + " dB";
        static string PctF(double v) => NotaNum.F($"{v * 100:0} %");
        static string FbF(double v) => v < 0.005 ? "—" : PctF(v);
        static string BiasF(double v) => SgnI((v - 0.5) * 200) + " %";
        static string ToneF(double v) => Sgn((v - 0.5) * 24) + " dB";
        static string WidthF(double v) => NotaNum.F($"{v * 200:0} %");
        string RateF(double v) => On(LfoSync) ? ForgeMath.DivNames[Math.Clamp((int)Math.Round(v * 7), 0, 7)] + " sync"
            : NotaNum.F($"{0.05 * Math.Pow(400, v):0.00} Hz");

        // ---- small builders -----------------------------------------------------------------
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

        // Slider row, as the mockup draws it inside the card: caps 7 label · 3px track · mono 8 value
        // (brass-light once moved off its default). The param is chosen at paint time — the selected
        // stage's, or a fixed one. Double-click resets.
        Control Slider(string label, Func<int> param, Func<double, string> fmt, string tip, double labelW, double valueW,
            bool teal = false, bool bipolar = false)
        {
            var lbl = Caps(label, teal ? Teal : TextTertiary);
            lbl.TextTrimming = TextTrimming.None;
            var track = new SliderTrack { Bipolar = bipolar, Modulation = teal, Reset = () => { Reset(param()); Refresh(); }, VerticalAlignment = VerticalAlignment.Center };
            track.Changed += v => { Raw(param(), v); Refresh(); };
            track.GestureBegin += () => Begin(param());
            track.GestureEnd += () => End(param());
            var val = Mono("", 8, TextPrimary);
            val.TextAlignment = TextAlignment.Right;
            val.HorizontalAlignment = HorizontalAlignment.Right;
            static ColumnDefinition Fixed(double w) => new(w > 0 ? new GridLength(w) : GridLength.Auto);
            var g = new Grid { ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(Fixed(labelW));
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            g.ColumnDefinitions.Add(Fixed(valueW));
            g.Children.Add(lbl); g.Children.Add(Col(track, 1)); g.Children.Add(Col(val, 2));
            readouts.Add(() =>
            {
                int p = param();
                if (!track.Dragging) track.Norm = P(p);
                val.Text = fmt(P(p));
                val.Foreground = Math.Abs(P(p) - Def(p)) > 0.003 ? AccentBright : TextPrimary;
            });
            LearnFollow(g, param);
            ToolTip.SetTip(g, tip);
            return g;
        }

        // ======================================================================
        // LEFT — routing + the three stages
        // ======================================================================
        var routeSeg = Segments(RouteNames, RouteI, i => { SetP(Routing, i / 3f); Refresh(); }, out var routeSync, fill: true, padX: 2);
        readouts.Add(routeSync);
        Learn(routeSeg, Routing);
        ToolTip.SetTip(routeSeg, "Routing — Serial: 1 → 2 → 3. Parallel: all three from the same input, averaged. M/S: stage 1 drives the Mid, stage 2 the Side, stage 3 the recombined stereo. Multi: stage 1 the lows (< 180 Hz), 2 the mids, 3 the highs (> 2.4 kHz)");

        Control StageTile(int s)
        {
            var dot = new Border { Width = 7, Height = 7, CornerRadius = NotaRadius.Pill, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
            var num = Mono((s + 1).ToString(), 8, TextSecondary); num.FontWeight = FontWeight.Bold;
            var name = new TextBlock { FontSize = 9, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var role = new TextBlock { FontSize = 7, FontWeight = FontWeight.Bold, LetterSpacing = 0.5, VerticalAlignment = VerticalAlignment.Center };
            var outTxt = Mono("", 8, TextSecondary);
            var outBox = new Border
            {
                Background = Sunken, CornerRadius = NotaRadius.Clip, Padding = new Thickness(2, 0), VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = outTxt,
            };
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"), ColumnSpacing = 5 };
            head.Children.Add(dot); head.Children.Add(Col(num, 1)); head.Children.Add(Col(name, 2)); head.Children.Add(Col(role, 3)); head.Children.Add(Col(outBox, 4));

            bool Lit() => On(OnP(s)) && Active();
            var drive = Slider("DRIVE", () => DriveP(s), PctF, $"Stage {s + 1} drive — how hard the stage is pushed (gain 1 … ×21, the level made up)", 42, 32);
            var fb = Slider("FEEDBACK", () => FbP(s), FbF, $"Stage {s + 1} feedback — its output back into its input (0 … 85 %): thicker, and on the edge of ringing with Fold / Fuzz", 44, 30, teal: true);
            var rows = new StackPanel { Spacing = 1, Children = { drive, fb } };
            var body = new DockPanel { Children = { Docked(head, Dock.Top), new Border { VerticalAlignment = VerticalAlignment.Bottom, Child = rows } } };
            var tile = new Border
            {
                BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 3), Background = Sunken,
                Cursor = new Cursor(StandardCursorType.Hand), Child = body,
            };

            tile.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) return;
                if (sel != s) { sel = s; Refresh(); }
            };
            dot.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(dot).Properties.IsLeftButtonPressed) return;
                SetP(OnP(s), On(OnP(s)) ? 0f : 1f); Refresh(); e.Handled = true;
            };
            Learn(dot, OnP(s));
            ToolTip.SetTip(dot, $"Stage {s + 1} on / off");

            // Output trim: drag the dB up / down; double-click resets.
            bool od = false; double oy = 0, ov = 0;
            outBox.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(outBox).Properties.IsLeftButtonPressed) return;
                sel = s;
                if (e.ClickCount == 2) { Reset(OutP(s)); Refresh(); e.Handled = true; return; }
                od = true; oy = e.GetPosition(outBox).Y; ov = P(OutP(s)); Begin(OutP(s)); e.Pointer.Capture(outBox); e.Handled = true; Refresh();
            };
            outBox.PointerMoved += (_, e) =>
            {
                if (!od) return;
                Raw(OutP(s), ov + (oy - e.GetPosition(outBox).Y) / 120.0); Refresh();
            };
            outBox.PointerReleased += (_, e) => { if (od) { od = false; End(OutP(s)); e.Pointer.Capture(null); } };
            Learn(outBox, OutP(s));
            ToolTip.SetTip(outBox, $"Stage {s + 1} output, −12 … +12 dB — drag up / down, double-click resets");

            readouts.Add(() =>
            {
                bool lit = Lit(), isSel = s == sel;
                dot.Background = lit ? Brass : BorderStrong;
                num.Foreground = lit ? TextSecondary : TextDisabled;
                name.Text = ForgeMath.AlgoNames[TypeI(s)];
                name.Foreground = lit ? (isSel ? AccentBright : TextPrimary) : TextTertiary;
                role.Text = ForgeMath.Role(RouteI(), s);
                role.Foreground = lit ? NotaPalette.TealBright : TextDisabled;
                double trim = (P(OutP(s)) - 0.5) * 24;
                outTxt.Text = TrimF(P(OutP(s)));
                outTxt.Foreground = Math.Abs(trim) > 0.05 ? AccentBright : lit ? TextSecondary : TextDisabled;
                tile.BorderBrush = isSel ? Brass : NotaPalette.GraphBorder;
                tile.Background = isSel ? Raised : Sunken;
                rows.Opacity = lit ? 1 : 0.45;
            });
            return tile;
        }
        var stageGrid = new Grid { RowDefinitions = new RowDefinitions("*,*,*"), RowSpacing = 4, Margin = new Thickness(4) };
        for (int s = 0; s < 3; s++) stageGrid.Children.Add(GRow(StageTile(s), s));
        var left = Island(new DockPanel { Children = { Docked(Bar(routeSeg, new Thickness(3, 0)), Dock.Top), stageGrid } }, 186);
        DockPanel.SetDock(left, Dock.Left);

        // ======================================================================
        // CENTRE — Amount / Wet / Out over the transfer curve and the harmonics
        // ======================================================================
        var macros = new Grid { ColumnDefinitions = new ColumnDefinitions("13*,10*,10*"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        macros.Children.Add(Slider("AMOUNT", () => Amount, AmtF, "Amount — 0 … +30 dB into the stages. Drag the curve up / down for the same", 0, 0));
        macros.Children.Add(Col(Slider("WET", () => Wet, PctF, "Wet — the forged signal against the dry one", 0, 0), 1));
        macros.Children.Add(Col(Slider("OUT", () => Output, OutF, "Output, −24 … +24 dB", 0, 0, bipolar: true), 2));

        var transfer = new FgTransferView { Value = _ => P(Amount) };
        transfer.GestureBegin += _ => Begin(Amount);
        transfer.GestureEnd += _ => End(Amount);
        transfer.Changed += (_, v) => { Raw(Amount, v); Refresh(); };
        transfer.ResetRequested += _ => { Reset(Amount); Refresh(); };
        Learn(transfer, Amount);
        ToolTip.SetTip(transfer, "Input → output through the device (in M/S and Multi through the selected stage): each stage alone faint, the LFO-modulated curve dashed teal, the node where the input sits now. Drag up / down for Amount; double-click resets.");
        var harm = new FgHarmonicsView { Height = 50 };
        ToolTip.SetTip(harm, "Harmonics 2 … 9 of a −6 dB sine through the curve: odd partials in brass (hard, buzzy), even ones in ink (warm, round), and the total harmonic distortion");
        var graphs = new DockPanel
        {
            Margin = new Thickness(5), LastChildFill = true,
            Children = { Docked(new Border { Margin = new Thickness(0, 4, 0, 0), Child = harm }, Dock.Bottom), transfer },
        };
        var centre = Island(new DockPanel { Children = { Docked(Bar(macros, new Thickness(6, 0)), Dock.Top), graphs } });
        centre.Margin = new Thickness(5, 0);

        // ======================================================================
        // RIGHT — the selected stage's shape, modulation, oversampling
        // ======================================================================
        var badgeTxt = Mono("", 8, OnAccent); badgeTxt.FontWeight = FontWeight.Bold; badgeTxt.HorizontalAlignment = HorizontalAlignment.Center;
        var badge = new Border { Width = 13, Height = 13, CornerRadius = NotaRadius.Pill, VerticalAlignment = VerticalAlignment.Center, Child = badgeTxt };
        var stName = new TextBlock { FontSize = 9, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var shapeHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
        shapeHead.Children.Add(badge); shapeHead.Children.Add(Col(stName, 1)); shapeHead.Children.Add(Col(Caps("SHAPE"), 2));
        readouts.Add(() =>
        {
            bool lit = On(OnP(sel));
            badgeTxt.Text = (sel + 1).ToString();
            badge.Background = lit ? AccentBright : TextDisabled;
            stName.Text = ForgeMath.AlgoNames[TypeI(sel)].ToUpperInvariant();
            stName.Foreground = lit ? AccentBright : TextTertiary;
        });

        var typeSeg = Segments(ForgeMath.ChipNames, () => Array.IndexOf(ForgeMath.ChipAlgo, TypeI(sel)),
            i => { SetP(TypeP(sel), ForgeMath.ChipAlgo[i] / 5f); Refresh(); }, out var typeSync, fill: true, padX: 1);
        readouts.Add(typeSync);
        ToolTip.SetTip(typeSeg, "The selected stage's type — Tube: soft, asymmetric, warm; Tape: soft and even; Diode: hard on one side; Fuzz: squared-off, dense; Fold: folds the peaks back, bright and metallic; Digital: a hard clip");

        // Gauge knob on the selected stage's param.
        Control StageKnob(Func<int> param, string label, Func<double, string> fmt, string tip)
        {
            var val = Mono(fmt(P(param())), 7, TextPrimary);
            var knob = new Knob(P(param()), 1.0) { Accent = true, Default = Def(param()), Width = 30, Height = 30 };
            knob.ValueChanged += v => { Raw(param(), v); val.Text = fmt(P(param())); Refresh(); };
            knob.GestureBegin += () => Begin(param());
            knob.GestureEnd += () => End(param());
            LearnFollow(knob, param);
            ToolTip.SetTip(knob, tip);
            readouts.Add(() =>
            {
                if (!knob.Dragging) { double c = P(param()); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                val.Text = fmt(P(param()));
                val.Foreground = Math.Abs(P(param()) - Def(param())) > 0.003 ? AccentBright : TextPrimary;
            });
            return KnobCell(label, knob, val, 46);
        }
        var knobs = new UniformGrid
        {
            Rows = 1,
            Children =
            {
                StageKnob(() => BiasP(sel), "BIAS", BiasF, "Bias — the stage's asymmetry, −100 … +100 %: pushes the curve off-centre, adding even harmonics"),
                StageKnob(() => ToneP(sel), "TONE", ToneF, "Tone — a tilt after the stage, −12 … +12 dB around 800 Hz: darker or brighter distortion"),
                StageKnob(() => WidthSP(sel), "WIDTH", WidthF, "Width — the stage's stereo image, 0 … 200 % (in M/S, stage 2's width scales the side)"),
            },
        };

        var syncSw = Switch("Sync", () => On(LfoSync), () => { SetP(LfoSync, On(LfoSync) ? 0f : 1f); Refresh(); }, out var syncSync);
        readouts.Add(syncSync);
        Learn(syncSw, LfoSync);
        ToolTip.SetTip(syncSw, "Sync — the LFO rate as a note division locked to the song, or free in Hz");
        var modHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        modHead.Children.Add(Caps("MODULATION", Teal)); modHead.Children.Add(Col(syncSw, 1));
        var mods = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Slider("LFO → DRIVE", () => LfoDrive, PctF, "LFO → Drive — the LFO sweeps every stage's drive, up to ±30 %", 50, 40, teal: true),
                Slider("ENV → TONE", () => EnvTone, PctF, "Env → Tone — loud parts push the tone brighter", 50, 40, teal: true),
                Slider("RATE", () => LfoRate, RateF, "LFO rate — free 0.05 … 20 Hz, or with Sync a division from 2 bars to 1/64", 50, 40, teal: true),
            },
        };
        var osSeg = Segments(OsNames, OsI, i => { SetP(OS, i / 3f); Refresh(); }, out var osSync, fill: true, padX: 2);
        readouts.Add(osSync);
        Learn(osSeg, OS);
        ToolTip.SetTip(osSeg, "Oversampling — runs the stages at 2 / 4 / 8 × the rate so the harmonics above Nyquist don't fold back (minimum phase, no latency)");
        var osRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
        osRow.Children.Add(Caps("OVERSAMPLE")); osRow.Children.Add(Col(osSeg, 1));
        static Control Rule(Control c) => new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 4, 0, 0), Child = c };

        var rightBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,Auto,*,Auto"), Margin = new Thickness(8, 6) };
        rightBody.Children.Add(GRow(typeSeg, 0));
        rightBody.Children.Add(GRow(knobs, 2));
        rightBody.Children.Add(GRow(Rule(modHead), 4));
        rightBody.Children.Add(GRow(new Border { Margin = new Thickness(0, 3, 0, 0), Child = mods }, 5));
        rightBody.Children.Add(GRow(Rule(osRow), 7));
        var right = Island(new DockPanel { Children = { Docked(Bar(shapeHead, new Thickness(8, 0)), Dock.Top), rightBody } }, 184);
        DockPanel.SetDock(right, Dock.Right);

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, TextSecondary);
        string StatusText()
        {
            if (Sc(ForgeMath.S_Alias) > 0.5) return "Digital / Fold without oversampling alias: switch on 2× or more";
            int on = 0; for (int s = 0; s < 3; s++) if (On(OnP(s))) on++;
            var parts = new List<string>
            {
                $"{on} of 3 stages",
                new[] { "serial", "parallel", "mid/side", "multiband" }[RouteI()],
                $"S{sel + 1} {ForgeMath.AlgoNames[TypeI(sel)]} {PctF(P(DriveP(sel)))}",
            };
            if (P(LfoDrive) > 0.005f) parts.Add($"LFO {PctF(P(LfoDrive))} {RateF(P(LfoRate))}");
            if (P(EnvTone) > 0.005f) parts.Add($"env → tone {PctF(P(EnvTone))}");
            // The master offsets have no knob on the card (their per-stage successors do) — say so when set.
            if (Math.Abs(P(Tone) - 0.5f) > 0.002f) parts.Add("master tone " + SgnI((P(Tone) - 0.5) * 200) + " %");
            if (Math.Abs(P(Bias) - 0.5f) > 0.002f) parts.Add("master bias " + BiasF(P(Bias)));
            if (Math.Abs(P(WidthP) - 0.5f) > 0.002f) parts.Add("master width " + WidthF(P(WidthP)));
            return string.Join(" · ", parts);
        }
        readouts.Add(() =>
        {
            bool alias = Sc(ForgeMath.S_Alias) > 0.5;
            statusLeft.Text = StatusText();
            statusLeft.Foreground = alias ? AccentBright : TextSecondary;
            double sr = Sc(ForgeMath.S_SampleRate);
            statusRight.Text = sr > 0
                ? NotaNum.F($"{sr / 1000:0.#} kHz · OS {(OsI() == 0 ? "off" : OsNames[OsI()])} · latency {Sc(ForgeMath.S_Latency):0} smp · CPU {Sc(ForgeMath.S_Cpu) * 100:0.0} %")
                : "";
        });
        ToolTip.SetTip(statusLeft, "What the device is doing; the master Tone / Bias / Width offsets of older projects show here when set");
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { left, right, centre } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

        var onArr = new bool[3];
        refreshNow = () =>
        {
            scN = engine.DeviceScope(track, di, scope, ForgeMath.kScope);
            for (int s = 0; s < 3; s++) onArr[s] = On(OnP(s));
            bool single = Single(), active = Active();
            string mainLbl = single ? $"stage {sel + 1} · {ForgeMath.Role(RouteI(), sel).Split(' ')[0].ToLowerInvariant()}" : "static";
            transfer.Set(scope, scN, onArr, sel, single, active, P(LfoDrive) > 0.005f, mainLbl);
            int set = single ? sel + 1 : 0;
            int thdSlot = single ? ForgeMath.S_ThdS1 + sel : ForgeMath.S_ThdChain;
            int flSlot = single ? ForgeMath.S_FlavorS1 + sel : ForgeMath.S_FlavorChain;
            harm.Set(scope, scN, set, !active ? "bypass"
                : NotaNum.F($"THD {Sc(thdSlot):0.0} % · {ForgeMath.Flavors[Math.Clamp((int)Sc(flSlot), 0, 3)]}"));
            RefreshAll();
        };
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
