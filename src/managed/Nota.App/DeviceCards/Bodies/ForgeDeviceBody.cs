// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Forge body (multi-stage saturator, device kind 17), a build of
// the "Nota Forge" mockup (700 × 260): on the left the routing (Serial / Parallel / M/S / Multi)
// over the three stages, rows split by rules — each with its on switch, its number (tinted when
// selected), its type, a draggable output trim (drag the dB up / down), Drive and Feedback; a
// click selects the stage, and a stage that is off is drawn in ink 4. In the centre Amount / Wet /
// Out over the transfer curve (the whole device — the selected stage in M/S and Multi — every
// stage alone in a lighter grey, the LFO-modulated curve dashed, a node where the input sits;
// drag it for Amount) and the harmonics of a −6 dB sine with THD. On the right the selected
// stage's shape (type, Bias, Tone, Width), the modulation (LFO → Drive, Env → Tone, Rate, Sync)
// and Oversampling. Curves, harmonics and meters come from the engine (Forge.h scopeRead). Every
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
    public string? Subtitle => "Saturation";

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
        static TextBlock Caps(string t, IBrush? c = null) => FxParts.Label(t, c);
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static RuleSection Island(Control child, double width, Thickness pad) => new() { Width = width, Padding = pad, Child = child };
        static Border Rule() => new() { Height = 1, Background = NotaPalette.Rule, VerticalAlignment = VerticalAlignment.Center };
        // A stage number: a 16px chip, tinted when the stage is the selected one.
        static (Border chip, TextBlock text) NumChip()
        {
            var t = new TextBlock { FontSize = NotaType.KnobValue, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return (new Border { Width = 16, Height = 16, CornerRadius = NotaRadius.Cell, VerticalAlignment = VerticalAlignment.Center, Child = t }, t);
        }
        static TextBlock StageName() => new() { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };

        // Slider row: label · value bar · value. The param is chosen at paint time — the selected
        // stage's, or a fixed one. `dim` draws the row in ink 4 (its stage is off). Double-click resets.
        Control Slider(string label, Func<int> param, Func<double, string> fmt, string tip, double labelW, double valueW,
            bool bipolar = false, Func<bool>? dim = null)
        {
            var lbl = Caps(label);
            lbl.TextTrimming = TextTrimming.None;
            var track = new SliderTrack { Bipolar = bipolar, Reset = () => { Reset(param()); Refresh(); }, VerticalAlignment = VerticalAlignment.Center };
            track.Changed += v => { Raw(param(), v); Refresh(); };
            track.GestureBegin += () => Begin(param());
            track.GestureEnd += () => End(param());
            var val = FxParts.Value();
            val.TextAlignment = TextAlignment.Right;
            val.HorizontalAlignment = HorizontalAlignment.Right;
            ValueEntry.Attach(val, () => P(param()), v => Raw(param(), v), () => fmt(P(param())), () => Begin(param()), () => End(param()), Refresh, control: track);
            static ColumnDefinition Fixed(double w) => new(w > 0 ? new GridLength(w) : GridLength.Auto);
            var g = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(Fixed(labelW));
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            g.ColumnDefinitions.Add(Fixed(valueW));
            g.Children.Add(lbl); g.Children.Add(Col(track, 1)); g.Children.Add(Col(val, 2));
            readouts.Add(() =>
            {
                int p = param();
                bool off = dim?.Invoke() ?? false;
                if (!track.Dragging) track.Norm = P(p);
                track.IsDim = off;
                val.Text = fmt(P(p));
                val.Foreground = off ? TextDisabled : TextPrimary;
                lbl.Foreground = off ? TextDisabled : TextTertiary;
            });
            LearnFollow(g, param);
            ToolTip.SetTip(g, tip);
            return g;
        }

        // ======================================================================
        // LEFT — routing + the three stages
        // ======================================================================
        var routeSeg = Segments(RouteNames, RouteI, i => { SetP(Routing, i / 3f); Refresh(); }, out var routeSync, fill: true, padX: 2);
        routeSeg.VerticalAlignment = VerticalAlignment.Top;
        readouts.Add(routeSync);
        Learn(routeSeg, Routing);
        ToolTip.SetTip(routeSeg, "Routing — Serial: 1 → 2 → 3. Parallel: all three from the same input, averaged. M/S: stage 1 drives the Mid, stage 2 the Side, stage 3 the recombined stereo. Multi: stage 1 the lows (< 180 Hz), 2 the mids, 3 the highs (> 2.4 kHz)");

        Control StageTile(int s)
        {
            var dot = new SwitchTrack { Cursor = new Cursor(StandardCursorType.Hand) };
            var (numChip, num) = NumChip();
            num.Text = (s + 1).ToString();
            var name = StageName();
            var role = FxParts.Info();
            var outTxt = FxParts.Value();
            var outBox = new Border
            {
                Background = NotaGraph.Ground, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 0), Height = 16, VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = outTxt,
            };
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"), ColumnSpacing = 8, Height = 18 };
            head.Children.Add(dot); head.Children.Add(Col(numChip, 1)); head.Children.Add(Col(name, 2)); head.Children.Add(Col(role, 3)); head.Children.Add(Col(outBox, 4));

            bool Lit() => On(OnP(s)) && Active();
            var drive = Slider("Drive", () => DriveP(s), PctF, $"Stage {s + 1} drive — how hard the stage is pushed (gain 1 … ×21, the level made up)", 48, 30, dim: () => !Lit());
            var fb = Slider("Feedback", () => FbP(s), FbF, $"Stage {s + 1} feedback — its output back into its input (0 … 85 %): thicker, and on the edge of ringing with Fold / Fuzz", 48, 30, dim: () => !Lit());
            var body = new Grid { RowDefinitions = new RowDefinitions("18,17,17") };
            body.Children.Add(head); body.Children.Add(GRow(drive, 1)); body.Children.Add(GRow(fb, 2));
            var tile = new Border { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = body };

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
                if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { Reset(OutP(s)); Refresh(); e.Handled = true; return; }
                od = true; oy = e.GetPosition(outBox).Y; ov = P(OutP(s)); Begin(OutP(s)); e.Pointer.Capture(outBox); e.Handled = true; Refresh();
            };
            outBox.PointerMoved += (_, e) =>
            {
                if (!od) return;
                Raw(OutP(s), ov + (oy - e.GetPosition(outBox).Y) / 120.0); Refresh();
            };
            outBox.PointerReleased += (_, e) => { if (od) { od = false; End(OutP(s)); e.Pointer.Capture(null); } };
            outBox.PointerCaptureLost += (_, _) => { if (od) { od = false; End(OutP(s)); } };
            Learn(outBox, OutP(s));
            ToolTip.SetTip(outBox, $"Stage {s + 1} output, −12 … +12 dB — drag up / down, double-click resets");

            readouts.Add(() =>
            {
                bool lit = Lit(), isSel = s == sel;
                dot.IsOn = On(OnP(s));
                dot.IsDim = !Active();
                numChip.Background = isSel ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                num.Foreground = isSel ? AccentBright : lit ? TextSecondary : TextDisabled;
                name.Text = ForgeMath.AlgoNames[TypeI(s)];
                name.Foreground = lit ? TextPrimary : TextDisabled;
                role.Text = NotaText.Label(ForgeMath.Role(RouteI(), s));
                role.Foreground = lit ? TextTertiary : TextDisabled;
                outTxt.Text = TrimF(P(OutP(s)));
                outTxt.Foreground = lit ? TextPrimary : TextDisabled;
            });
            return tile;
        }
        var stageGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,6,52,1,52,1,52,*") };
        stageGrid.Children.Add(routeSeg);
        for (int s = 0; s < 3; s++) stageGrid.Children.Add(GRow(StageTile(s), 2 + s * 2));
        stageGrid.Children.Add(GRow(Rule(), 3));
        stageGrid.Children.Add(GRow(Rule(), 5));
        var left = Island(stageGrid, 196, new Thickness(SectionPad, SectionPad, SectionPad, 8));
        DockPanel.SetDock(left, Dock.Left);

        // ======================================================================
        // CENTRE — Amount / Wet / Out over the transfer curve and the harmonics
        // ======================================================================
        var macros = new Grid { RowDefinitions = new RowDefinitions("19,19,19") };
        macros.Children.Add(Slider("Amount", () => Amount, AmtF, "Amount — 0 … +30 dB into the stages. Drag the curve up / down for the same", 46, 54));
        macros.Children.Add(GRow(Slider("Wet", () => Wet, PctF, "Wet — the forged signal against the dry one", 46, 54), 1));
        macros.Children.Add(GRow(Slider("Out", () => Output, OutF, "Output, −24 … +24 dB", 46, 54, bipolar: true), 2));

        var transfer = new FgTransferView { Value = _ => P(Amount) };
        transfer.GestureBegin += _ => Begin(Amount);
        transfer.GestureEnd += _ => End(Amount);
        transfer.Changed += (_, v) => { Raw(Amount, v); Refresh(); };
        transfer.ResetRequested += _ => { Reset(Amount); Refresh(); };
        Learn(transfer, Amount);
        ToolTip.SetTip(transfer, "Input → output through the device (in M/S and Multi through the selected stage): each stage alone in a lighter grey, the LFO-modulated curve dashed, the node where the input sits now. Drag up / down for Amount; double-click resets.");
        var harm = new FgHarmonicsView { Height = 46 };
        ToolTip.SetTip(harm, "Harmonics 2 … 9 of a −6 dB sine through the curve: odd partials in full ink (hard, buzzy), even ones dimmer (warm, round), and the total harmonic distortion");
        var graphs = new DockPanel
        {
            Margin = new Thickness(0, 6, 0, 0), LastChildFill = true,
            Children = { Docked(new Border { Margin = new Thickness(0, 9, 0, 0), Child = harm }, Dock.Bottom), transfer },
        };
        var centre = Island(new DockPanel { Children = { Docked(macros, Dock.Top), graphs } }, double.NaN, new Thickness(SectionPad, 10, SectionPad, SectionPad));
        centre.Margin = new Thickness(SectionGap, 0);

        // ======================================================================
        // RIGHT — the selected stage's shape, modulation, oversampling
        // ======================================================================
        var (badge, badgeTxt) = NumChip();
        badge.Background = NotaPalette.AccentSoft;
        var stName = StageName();
        var shapeHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        shapeHead.Children.Add(badge); shapeHead.Children.Add(Col(stName, 1)); shapeHead.Children.Add(Col(Caps("Shape"), 2));
        readouts.Add(() =>
        {
            bool lit = On(OnP(sel));
            badgeTxt.Text = (sel + 1).ToString();
            badgeTxt.Foreground = lit ? AccentBright : TextDisabled;
            stName.Text = ForgeMath.AlgoNames[TypeI(sel)];
            stName.Foreground = lit ? TextPrimary : TextDisabled;
        });

        var typeSeg = Segments(ForgeMath.ChipNames, () => Array.IndexOf(ForgeMath.ChipAlgo, TypeI(sel)),
            i => { SetP(TypeP(sel), ForgeMath.ChipAlgo[i] / 5f); Refresh(); }, out var typeSync, fill: true, padX: 1);
        readouts.Add(typeSync);
        ToolTip.SetTip(typeSeg, "The selected stage's type — Tube: soft, asymmetric, warm; Tape: soft and even; Diode: hard on one side; Fuzz: squared-off, dense; Fold: folds the peaks back, bright and metallic; Digital: a hard clip");

        // Gauge knob on the selected stage's param.
        Control StageKnob(Func<int> param, string label, Func<double, string> fmt, string tip, bool bipolar = false)
        {
            var val = FxParts.Value(fmt(P(param())));
            var knob = new Knob(P(param()), 1.0) { Accent = true, Inline = true, Bipolar = bipolar, Default = Def(param()) };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
            knob.ValueChanged += v => { Raw(param(), v); val.Text = fmt(P(param())); Refresh(); };
            knob.GestureBegin += () => Begin(param());
            knob.GestureEnd += () => End(param());
            LearnFollow(knob, param);
            ToolTip.SetTip(knob, tip);
            readouts.Add(() =>
            {
                if (!knob.Dragging) { double c = P(param()); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                val.Text = fmt(P(param()));
            });
            return KnobCell(label, knob, val, 60);
        }
        var knobs = new UniformGrid
        {
            Rows = 1,
            Children =
            {
                StageKnob(() => BiasP(sel), "Bias", BiasF, "Bias — the stage's asymmetry, −100 … +100 %: pushes the curve off-centre, adding even harmonics", bipolar: true),
                StageKnob(() => ToneP(sel), "Tone", ToneF, "Tone — a tilt after the stage, −12 … +12 dB around 800 Hz: darker or brighter distortion", bipolar: true),
                StageKnob(() => WidthSP(sel), "Width", WidthF, "Width — the stage's stereo image, 0 … 200 % (in M/S, stage 2's width scales the side)"),
            },
        };

        var syncSw = Switch("Sync", () => On(LfoSync), () => { SetP(LfoSync, On(LfoSync) ? 0f : 1f); Refresh(); }, out var syncSync);
        readouts.Add(syncSync);
        Learn(syncSw, LfoSync);
        ToolTip.SetTip(syncSw, "Sync — the LFO rate as a note division locked to the song, or free in Hz");
        var modHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        modHead.Children.Add(Caps("Modulation", TextSecondary)); modHead.Children.Add(Col(syncSw, 1));
        var lfoDrive = Slider("LFO → drive", () => LfoDrive, PctF, "LFO → Drive — the LFO sweeps every stage's drive, up to ±30 %", 62, 50);
        var envTone = Slider("Env → tone", () => EnvTone, PctF, "Env → Tone — loud parts push the tone brighter", 62, 50);
        var rate = Slider("Rate", () => LfoRate, RateF, "LFO rate — free 0.05 … 20 Hz, or with Sync a division from 2 bars to 1/64", 62, 50);
        var osSeg = Segments(OsNames, OsI, i => { SetP(OS, i / 3f); Refresh(); }, out var osSync, fill: true, padX: 2);
        readouts.Add(osSync);
        Learn(osSeg, OS);
        ToolTip.SetTip(osSeg, "Oversampling — runs the stages at 2 / 4 / 8 × the rate so the harmonics above Nyquist don't fold back (minimum phase, no latency)");
        var osRow = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*") };
        osRow.Children.Add(Caps("Oversample")); osRow.Children.Add(Col(osSeg, 1));

        var rightBody = new Grid { RowDefinitions = new RowDefinitions("18,6,Auto,30,10,20,18,18,18,*,Auto") };
        rightBody.Children.Add(shapeHead);
        rightBody.Children.Add(GRow(typeSeg, 2));
        rightBody.Children.Add(GRow(knobs, 3));
        rightBody.Children.Add(GRow(Rule(), 4));
        rightBody.Children.Add(GRow(modHead, 5));
        rightBody.Children.Add(GRow(lfoDrive, 6));
        rightBody.Children.Add(GRow(envTone, 7));
        rightBody.Children.Add(GRow(rate, 8));
        rightBody.Children.Add(GRow(osRow, 10));
        var right = Island(rightBody, 206, new Thickness(SectionPad));
        DockPanel.SetDock(right, Dock.Right);

        // ---- assemble ---------------------------------------------------------------
        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(0, CardPad, 0, 0), Children = { left, right, centre } };

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
