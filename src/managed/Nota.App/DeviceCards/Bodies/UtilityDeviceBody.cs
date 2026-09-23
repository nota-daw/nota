// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Utility body (device kind 4), a build of the "Nota Utility"
// mockup (700 × 260) on the Valve / Vintage / Auto Filter frame: an always-visible LEVEL
// column (input and output meters with their level in the meter's unit), a centre panel with
// Field / Mono / Levels tabs — the stereo field by frequency, the width over frequency with the
// mono region, input and output over the last 8 s against the target — a right panel with
// Routing / Output tabs, and a status strip. Every picture comes from the engine (Utility.h
// scopeRead); every control is a device param, so automation / MIDI learn / presets / A-B /
// persistence come for free. Params are in real units (Gain dB, Width %, Mono Freq Hz …);
// the controls work on 0..1 and map.
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

internal sealed class UtilityDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Utility.h) ─────────────────────────────
    private const int Gain = 0, Balance = 1, Width_ = 2, ChannelMode = 3, MonoFreq = 4, MonoBelow = 5, Mute = 6, InvertL = 7, InvertR = 8,
        WidthMode = 9, MonoSlope = 10, AutoMatch = 11, MatchTo = 12, Target = 13, Meter = 14, TpLimit = 15, TpCeiling = 16, kParams = 17;
    // Scope layout (Utility::S_* / kTele / kHist / kBands / kResp).
    private const int S_InPk = 0, S_OutPk = 1, S_Cpu = 2, S_SampleRate = 3, S_Corr = 4, S_InLufs = 5, S_OutLufs = 6,
        S_TruePeak = 11, S_AutoDb = 14, S_LimDb = 15, S_InLevel = 16, S_OutLevel = 17, S_InMatch = 18,
        S_EnergyBal = 20, S_WidthNow = 21, S_EffWidth = 22, S_MonoHz = 23, S_HistSec = 26, S_BandLo = 28, S_BandHi = 29,
        S_RespLo = 31, S_RespHi = 32, S_Analysed = 33, S_InTruePeak = 34, S_OutPreMatch = 35;
    private const int kTele = 48, kHist = 80, kBands = 32, kResp = 96;
    private const int kHistAt = kTele, kBandAt = kHistAt + 6 * kHist, kRespAt = kBandAt + 5 * kBands, kScope = kRespAt + kResp;
    private const int H_InLufs = 0, H_OutLufs = 1, H_InPeak = 2, H_OutPeak = 3, H_InRms = 4, H_OutRms = 5;
    private const int B_Pan = 0, B_Width = 1, B_Corr = 2, B_Level = 3, B_Set = 4;

    private static readonly string[] Modes = { "Stereo", "Left", "Right", "Swap" };
    private static readonly string[] Meters = { "LUFS-S", "Peak", "RMS" };
    private static readonly string[] Units = { "LUFS", "dB", "dB" };
    private static readonly int[] Slopes = { 6, 12, 24 };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "UTILITY";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        var mn = new float[kParams];
        var mx = new float[kParams];
        for (int p = 0; p < kParams; p++) { mn[p] = engine.DeviceParamMin(track, di, p); mx[p] = engine.DeviceParamMax(track, di, p); if (mx[p] <= mn[p]) mx[p] = mn[p] + 1; }
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) => engine.DeviceSetParam(track, di, p, Math.Clamp(v, mn[p], mx[p]));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        bool On(int p) => P(p) >= 0.5f;
        int Idx(int p) => Math.Clamp((int)Math.Round(P(p)), (int)mn[p], (int)mx[p]);

        // ---- 0..1 mappings per param (the width puts 100 % in the middle, the cutoff is log) ----
        const double FLo = 20, FHi = 2000;
        double ToN(int p, double v) => p switch
        {
            Width_ => v <= 100 ? v / 200 : 0.5 + (v - 100) / 600,
            MonoFreq => Math.Log(Math.Clamp(v, FLo, FHi) / FLo) / Math.Log(FHi / FLo),
            _ => (v - mn[p]) / (mx[p] - mn[p]),
        };
        double FromN(int p, double n)
        {
            n = Math.Clamp(n, 0, 1);
            return p switch
            {
                Width_ => n <= 0.5 ? n * 200 : 100 + (n - 0.5) * 600,
                MonoFreq => FLo * Math.Pow(FHi / FLo, n),
                _ => mn[p] + n * (mx[p] - mn[p]),
            };
        }
        double N(int p) => ToN(p, P(p));
        void SetN(int p, double n) => Raw(p, (float)FromN(p, n));
        double DefN(int p) => ToN(p, engine.DeviceParamDefault(track, di, p));

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;
        double ScDb(int i) => scN > i ? scope[i] : -120;

        // ---- units ----------------------------------------------------------------
        static string Db1(double v) => NotaNum.F($"{v:+0.0;−0.0;0.0}");
        static string GainF(double v) => NotaNum.F($"{v:+0.0;−0.0;0.0} dB");
        static string BalF(double v) => Math.Abs(v) < 0.005 ? "C" : NotaNum.F($"{(v < 0 ? "L" : "R")} {Math.Abs(v) * 100:0}");
        static string WidthF(double v) => NotaNum.F($"{v:0} %");
        static string HzF(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0#} k") : NotaNum.F($"{hz:0} Hz");
        static string CeilF(double v) => NotaNum.F($"{v:0.0} dBTP");
        static string CorrF(double c) => NotaNum.F($"{c:+0.00;−0.00;0.00}");
        static string LvlF(double db) => db <= -119 ? "−∞" : NotaNum.F($"{db:0.0}");
        int MeterI() => Idx(Meter);
        string Unit() => Units[MeterI()];
        string MonoF(double v) => On(MonoBelow) ? HzF(v) : "off";
        string PhaseText() => On(InvertL) && On(InvertR) ? "both phases inverted" : On(InvertL) ? "phase L inverted" : On(InvertR) ? "phase R inverted" : "phase normal";
        string MonoText() => On(MonoBelow) ? NotaNum.F($"mono below {HzF(P(MonoFreq))}") : "bass mono off";
        string BalanceText()
        {
            double b = Sc(S_EnergyBal);
            return Math.Abs(b) < 0.25 ? "energy L=R" : NotaNum.F($"energy {(b > 0 ? "L" : "R")} +{Math.Abs(b):0.0} dB");
        }
        string CorrWords()
        {
            double c = Sc(S_Corr);
            if (Sc(S_OutPk) < 1e-4) return "no signal";
            if (c < 0) return "out of phase — mono risk";
            if (c < 0.3) return "wide — check in mono";
            return P(Width_) > 100.5 ? "wider, still in phase" : "in phase";
        }
        double Delta() => ScDb(S_OutLevel) > -119 && ScDb(S_InLevel) > -119 ? Sc(S_OutLevel) - Sc(S_InLevel) : 0;
        string DeltaText() => ScDb(S_OutLevel) > -119 && ScDb(S_InLevel) > -119 ? NotaNum.F($"Δ {Delta():+0.0;−0.0;0.0} dB") : "Δ —";

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
        { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, Func<bool>? dim = null, double cellW = 44)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(N(p), 1.0) { Accent = true, ArcColor = arc, Default = DefN(p), Width = 34, Height = 34 };
            knob.ValueChanged += v => { SetN(p, v); val.Text = fmt(P(p)); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            readouts.Add(() =>
            {
                if (dim is not null) knob.IsDim = dim();
                if (knob.Dragging) return;
                double c = N(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c;
                val.Text = fmt(P(p));
            });
            return KnobCell(name, knob, val, cellW);
        }

        // On/off switch bound to a param (> 0.5 = on).
        Control Toggle(int p, string label, Func<string>? live = null)
        {
            var wrap = Switch(label, () => On(p), () => { SetP(p, On(p) ? 0f : 1f); RefreshAll(); }, out var sync, null, live);
            readouts.Add(sync);
            Learn(wrap, p);
            return wrap;
        }

        // Segmented pill over an index param (0 … n−1 in raw units).
        Control Seg(int p, string[] names, string tip)
        {
            var seg = Segments(names, () => Idx(p), iv => { SetP(p, iv); RefreshAll(); }, out var sync, padX: 6);
            readouts.Add(sync);
            ToolTip.SetTip(seg, tip);
            Learn(seg, p);
            return seg;
        }

        // Outlined chips: lit = brass edge + wash.
        Control Chips(string[] names, Func<int, bool> lit, Action<int> pick, int learnParam, string tip, double h = 16, double padX = 7)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var cells = new Border[names.Length];
            var texts = new TextBlock[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int iv = i;
                var tb = new TextBlock { Text = names[i], FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var b = new Border { Height = h, Padding = new Thickness(padX, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; pick(iv); RefreshAll(); e.Handled = true; };
                cells[i] = b; texts[i] = tb; row.Children.Add(b);
            }
            readouts.Add(() =>
            {
                for (int i = 0; i < names.Length; i++)
                {
                    bool on = lit(i);
                    cells[i].BorderBrush = on ? Brass : NotaPalette.BorderStrong;
                    cells[i].Background = on ? NotaPalette.AccentSubtle : Brushes.Transparent;
                    texts[i].Foreground = on ? AccentBright : TextSecondary;
                    texts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            });
            ToolTip.SetTip(row, tip);
            if (learnParam >= 0) Learn(row, learnParam);
            return row;
        }
        Control IndexChips(int p, string[] names, string tip, double h = 16, double padX = 7)
            => Chips(names, i => Idx(p) == i, i => SetP(p, i), p, tip, h, padX);

        // A latching chip: lit when `lit()`, a click runs `click`; its text can follow the value.
        Border Latch(int p, Func<bool> lit, Action click, Func<string> text, string tip, bool fill = false, double h = 16, double padX = 7)
        {
            var tb = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = h, Padding = new Thickness(padX, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            void Hi()
            {
                bool on = lit();
                b.Background = on ? NotaPalette.AccentSubtle : fill ? Raised : Brushes.Transparent;
                b.BorderBrush = on ? Brass : fill ? Brushes.Transparent : NotaPalette.BorderStrong;
                tb.Foreground = on ? AccentBright : fill ? TextPrimary : TextSecondary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                tb.Text = text();
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            if (p >= 0) Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }
        Border ToggleLatch(int p, Func<string> text, string tip, bool fill = false, double h = 16, double padX = 7)
            => Latch(p, () => On(p), () => SetP(p, On(p) ? 0f : 1f), text, tip, fill, h, padX);

        // A push button (an action, not a param).
        Border Button(string text, Action click, string tip, bool primary)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = primary ? FontWeight.SemiBold : FontWeight.Normal, Foreground = primary ? OnAccent : TextPrimary };
            var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Badge, Background = primary ? Brass : Raised,
                BorderBrush = primary ? Brass : NotaPalette.BorderStrong, BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            return b;
        }

        // Slider row: caps label · track · mono value (the track works on 0..1).
        Control SliderRow(string label, int p, Func<double, string> fmt, bool bipolar = false, bool modulation = false, Func<bool>? dim = null, double labW = 40)
        {
            var bar = DeviceCardKit.SliderRow("", () => N(p), v => SetN(p, v), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), bipolar: bipolar, dim: dim, valueWidth: 40, modulation: modulation);
            readouts.Add(sync);
            Learn(bar, p);
            var lb = Caps(label);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(lb);
            g.Children.Add(Col(bar, 1));
            if (dim is not null) readouts.Add(() => lb.Foreground = dim() ? TextDisabled : TextTertiary);
            return g;
        }

        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 18, ColumnSpacing = 6 };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        Control KnobRow(Control[] knobs, Func<string> info1, Func<string> info2)
        {
            var i1 = Mono("", 7, TextTertiary); i1.HorizontalAlignment = HorizontalAlignment.Right;
            var i2 = Mono("", 7, TextTertiary); i2.HorizontalAlignment = HorizontalAlignment.Right;
            i1.TextTrimming = i2.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 52, ColumnSpacing = 6 };
            g.Children.Add(Row(4, knobs));
            g.Children.Add(Col(new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { i1, i2 } }, 1));
            return g;
        }
        static Control TabBody(Control head, Control window, Control knobs) => new DockPanel
        {
            LastChildFill = true, Margin = new Thickness(8, 5, 8, 0),
            Children = { Docked(head, Dock.Top), Docked(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 5, 0, 0), Child = window } },
        };

        // ---- actions -----------------------------------------------------------------
        // Gain match: Gain is set so the output — metered before Gain and the auto-match ride —
        // meets the reference (the input's level or Target) over the last 3 s, in the meter's unit.
        void GainMatch()
        {
            scN = engine.DeviceScope(track, di, scope, kBandAt);
            double pre = ScDb(S_OutPreMatch), refL = On(MatchTo) ? P(Target) : ScDb(S_InMatch);
            if (pre <= -70 || refL <= -70) return;
            SetP(Gain, (float)Math.Clamp(refL - pre, -24, 24));
        }
        void ResetLevels()
        {
            SetP(Gain, 0f);
            engine.DeviceAction(track, di, 1, 0, 0);
        }
        const string GainMatchTip = "Gain match — moves Gain once so the output meets the reference (the input's level, or Target) over the last 3 s, in the meter's unit";
        const string ResetTip = "Reset — Gain back to 0 dB, and the meters, holds and the level history start over";

        // ======================================================================
        // LEFT — LEVEL column (input · output)
        // ======================================================================
        static AfFader MeterBar(IBrush ink) => new() { Ink = ink, Width = 16, VerticalAlignment = VerticalAlignment.Stretch, IsHitTestVisible = false };
        var levelTitle = Caps("LEVEL"); levelTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var inMeter = MeterBar(Brass);
        var outMeter = MeterBar(Teal);
        var levelIn = Mono("", 7, AccentBright); levelIn.HorizontalAlignment = HorizontalAlignment.Center;
        var levelOut = Mono("", 7, TextPrimary); levelOut.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(levelIn, "The input's level in the meter's unit (LUFS-S, 1 s peak or 300 ms RMS)");
        ToolTip.SetTip(levelOut, "The output's level in the meter's unit");
        static double MeterN(double lin) => lin > 1e-6 ? Math.Clamp((20 * Math.Log10(lin) + 60) / 60, 0, 1) : 0;
        readouts.Add(() =>
        {
            inMeter.Set(MeterN(Sc(S_InPk)), double.NaN);
            outMeter.Set(MeterN(Sc(S_OutPk)), double.NaN);
            levelIn.Text = LvlF(ScDb(S_InLevel));
            levelOut.Text = NotaNum.F($"{LvlF(ScDb(S_OutLevel))} {Unit()}");
            levelTitle.Foreground = Sc(S_InPk) > 0.01 ? AccentBright : TextTertiary;   // lit while signal passes
        });
        Control MeterCell(AfFader m, string label, string tip)
        {
            var lb = Caps(label); lb.HorizontalAlignment = HorizontalAlignment.Center;
            var d = new DockPanel { Children = { Docked(lb, Dock.Bottom), new Border { Margin = new Thickness(0, 0, 0, 3), Child = m } } };
            ToolTip.SetTip(d, tip);
            return d;
        }
        var meters = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        meters.Children.Add(MeterCell(inMeter, "IN", "Input peak, −60 … 0 dBFS"));
        meters.Children.Add(Col(MeterCell(outMeter, "OUT", "Output peak, −60 … 0 dBFS"), 1));
        var levelCol = new Border
        {
            Width = 56, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(levelTitle, Dock.Top), Docked(levelOut, Dock.Bottom), Docked(levelIn, Dock.Bottom), meters } },
        };
        DockPanel.SetDock(levelCol, Dock.Left);

        Control WidthModeSeg() => Seg(WidthMode, new[] { "L/R", "M/S" },
            "Width law — L/R: the side is scaled, so wider is louder; M/S: mid and side are traded, so the level holds (200 % = side only)");

        // ======================================================================
        // CENTRE — Field
        // ======================================================================
        UtFieldView? field = null;
        Control FieldTab()
        {
            field = new UtFieldView();
            field.StartH = () => N(Balance);
            field.StartV = () => N(Width_);
            field.DragH += v => SetN(Balance, v);
            field.DragV += v => SetN(Width_, v);
            field.GestureBegin += () => { Begin(Balance); Begin(Width_); };
            field.GestureEnd += () => { End(Balance); End(Width_); };
            ToolTip.SetTip(field, "The output's stereo field by frequency over the last second — lows at the bottom, highs at the top. Brass: where each band sits between L and R; teal: how far it spreads; dashed: how far the width setting spreads an uncorrelated band. Drag sideways for the balance, up or down for the width.");
            Learn(field, Width_);

            var corr = Mono("", 7, TextTertiary);
            readouts.Add(() => corr.Text = "corr " + CorrF(Sc(S_Corr)));
            var chips = IndexChips(ChannelMode, Modes, "Channel — Stereo; Left or Right on both sides; Swap the sides");
            var head = HeadRow(Row(6, Caps("CHANNEL"), chips, WidthModeSeg()), corr);
            var knobs = KnobRow(new[]
            {
                K(Gain, "GAIN", GainF),
                K(Balance, "BALANCE", BalF),
                K(Width_, "WIDTH", WidthF),
                K(MonoFreq, "MONO", MonoF, dim: () => !On(MonoBelow)),
            },
            () => NotaNum.F($"correlation {CorrF(Sc(S_Corr))} · {BalanceText()}"),
            () => NotaNum.F($"{PhaseText()} · {MonoText()}"));
            return TabBody(head, field, knobs);
        }

        // ======================================================================
        // CENTRE — Mono
        // ======================================================================
        UtWidthView? widthView = null;
        Control MonoTab()
        {
            widthView = new UtWidthView();
            widthView.StartH = () => N(MonoFreq);
            widthView.StartV = () => N(Width_);
            widthView.DragH += v => SetN(MonoFreq, v);
            widthView.DragV += v => SetN(Width_, v);
            widthView.GestureBegin += () => { Begin(MonoFreq); Begin(Width_); };
            widthView.GestureEnd += () => { End(MonoFreq); End(Width_); };
            ToolTip.SetTip(widthView, "The width over frequency — brass: what the settings do (0 % = mono, 100 % = unchanged), the mono region shaded; teal dashed: the output's measured width (side against mid). Drag sideways for the mono cutoff, up or down for the width.");
            Learn(widthView, MonoFreq);

            var wv = Mono("", 7, AccentBright);
            readouts.Add(() => wv.Text = NotaNum.F($"width {Sc(S_EffWidth):0}"));
            var onOff = ToggleLatch(MonoBelow, () => On(MonoBelow) ? "On" : "Off", "Mono below — removes the side below the cutoff, so the low end is mono");
            float[] quick = { 60, 120, 240 };
            var quickSeg = Segments(new[] { "60", "120", "240" }, () => NearestHz(P(MonoFreq), quick), iv => { SetP(MonoFreq, quick[iv]); if (!On(MonoBelow)) SetP(MonoBelow, 1f); RefreshAll(); }, out var qSync, padX: 6);
            readouts.Add(qSync);
            ToolTip.SetTip(quickSeg, "The usual cutoffs — 60, 120 or 240 Hz (turns Mono below on)");
            Learn(quickSeg, MonoFreq);
            var slope = Latch(MonoSlope, () => Idx(MonoSlope) > 0, () => SetP(MonoSlope, (Idx(MonoSlope) + 1) % 3),
                () => NotaNum.F($"Slope {Slopes[Idx(MonoSlope)]}"), "Slope of the mono cutoff — 6, 12 or 24 dB/oct; click to step");
            var head = HeadRow(Row(6, Caps("MONO"), onOff, quickSeg, WidthModeSeg(), slope), wv);
            var knobs = KnobRow(new[]
            {
                K(MonoFreq, "MONO", MonoF, dim: () => !On(MonoBelow)),
                K(Width_, "WIDTH", WidthF),
                K(Balance, "BALANCE", BalF, Teal),
                K(Gain, "GAIN", GainF, Teal),
            },
            () => On(MonoBelow) ? NotaNum.F($"below {HzF(P(MonoFreq))} width 0 · slope {Slopes[Idx(MonoSlope)]} dB/oct") : NotaNum.F($"full range · width {Sc(S_EffWidth):0} %"),
            () => NotaNum.F($"correlation {CorrF(Sc(S_Corr))} · {(Sc(S_Corr) >= 0.3 || Sc(S_OutPk) < 1e-4 ? "mono-safe" : Sc(S_Corr) >= 0 ? "check in mono" : "mono risk")}"));
            return TabBody(head, widthView, knobs);
        }

        // ======================================================================
        // CENTRE — Levels
        // ======================================================================
        UtLevelView? levelView = null;
        Control LevelsTab()
        {
            levelView = new UtLevelView();
            levelView.StartV = () => N(Target);
            levelView.DragV += v => SetN(Target, v);
            levelView.GestureBegin += () => Begin(Target);
            levelView.GestureEnd += () => End(Target);
            ToolTip.SetTip(levelView, "Input (teal) and output (brass) over the last 8 s in the meter's unit; dashed: the target. Drag up or down for the target.");
            Learn(levelView, Target);

            var delta = Mono("", 7, AccentBright);
            readouts.Add(() => delta.Text = DeltaText());
            var chips = IndexChips(Meter, Meters, "Meter — LUFS-S: short-term loudness (3 s, K-weighted); Peak: sample peak; RMS: 300 ms");
            var head = HeadRow(Row(6, Caps("METER"), chips, Button("Gain match", GainMatch, GainMatchTip, true), Button("Reset", ResetLevels, ResetTip, false)), delta);
            var knobs = KnobRow(new[]
            {
                K(Gain, "GAIN", GainF),
                K(Balance, "BALANCE", BalF, Teal),
                K(Width_, "WIDTH", WidthF, Teal),
                K(Target, "TARGET", v => NotaNum.F($"{v:0.0}"), Teal),
            },
            () => NotaNum.F($"in {LvlF(ScDb(S_InLevel))} → out {LvlF(ScDb(S_OutLevel))} {Meters[MeterI()]}"),
            () => ScDb(S_TruePeak) > -119 ? NotaNum.F($"true peak {Sc(S_TruePeak):0.0} dBTP · headroom {Math.Max(0, -Sc(S_TruePeak)):0.0} dB") : "true peak —");
            return TabBody(head, levelView, knobs);
        }

        // ======================================================================
        // RIGHT — Routing / Output
        // ======================================================================
        static Control Spread(params Control[] rows)
        {
            var defs = new List<string>();
            for (int i = 0; i < rows.Length; i++) { if (i > 0) defs.Add("*"); defs.Add("Auto"); }
            var g = new Grid { RowDefinitions = new RowDefinitions(string.Join(",", defs)) };
            for (int i = 0; i < rows.Length; i++) g.Children.Add(GRow(rows[i], i * 2));
            return new Border { Padding = new Thickness(8, 6), Child = g };
        }
        static Control LabelRow(string label, Control c, double labW = 40)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")) };
            g.Children.Add(new TextBlock { Text = label, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(Col(c, 1));
            return g;
        }
        Control Divided(Control c) => new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = c };

        Control RoutingTab()
        {
            var phase = Row(3,
                ToggleLatch(InvertL, () => "Ø L", "Invert the left channel's phase", h: 15, padX: 6),
                ToggleLatch(InvertR, () => "Ø R", "Invert the right channel's phase", h: 15, padX: 6),
                ToggleLatch(Mute, () => "Mute", "Mute the output", h: 15, padX: 6));
            // Mono on: WIDTH · MONO f | BALANCE; off: WIDTH · BALANCE | GAIN — the mockup's two states.
            var monoRow = SliderRow("MONO f", MonoFreq, HzF);
            var balA = SliderRow("BALANCE", Balance, v => NotaNum.F($"{v:+0.00;−0.00;0.00}"), bipolar: true);
            var balB = SliderRow("BALANCE", Balance, v => NotaNum.F($"{v:+0.00;−0.00;0.00}"), bipolar: true, modulation: true);
            var gainRow = SliderRow("GAIN", Gain, Db1, bipolar: true);
            var slot2 = new Panel { Children = { monoRow, balA } };
            var slot3 = new Panel { Children = { balB, gainRow } };
            readouts.Add(() => { bool m = On(MonoBelow); monoRow.IsVisible = m; balA.IsVisible = !m; balB.IsVisible = m; gainRow.IsVisible = !m; });

            var corrBar = new UtCorrBar();
            var corrTitle = Caps("CORRELATION");
            var corrText = Mono("", 9, TextPrimary);
            corrText.TextTrimming = TextTrimming.CharacterEllipsis;
            var corrBox = new Border { Background = Sunken, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 3, Children = { corrTitle, corrBar, corrText } } };
            ToolTip.SetTip(corrBox, "Output correlation — +1 mono, 0 unrelated, below 0 out of phase (it cancels in mono)");
            readouts.Add(() =>
            {
                double c = Sc(S_Corr);
                corrBar.Set(c);
                corrText.Text = NotaNum.F($"{CorrF(c)} · {CorrWords()}");
                bool lit = Math.Abs(P(Width_) - 100) > 0.5 || On(MonoBelow);
                corrBox.BorderBrush = lit ? NotaPalette.BorderBrass : NotaPalette.GraphBorder;
                corrTitle.Foreground = lit ? AccentBright : TextTertiary;
                corrText.Foreground = c < 0 && Sc(S_OutPk) > 1e-4 ? Danger : lit ? AccentBright : TextPrimary;
            });
            return Spread(
                LabelRow("PHASE", phase, 40),
                SliderRow("WIDTH", Width_, v => NotaNum.F($"{v:0}")),
                slot2,
                Divided(slot3),
                corrBox,
                new StackPanel { Spacing = 5, Children = {
                    Toggle(MonoBelow, "Mono below freq", () => On(MonoBelow) ? NotaNum.F($"Mono below {HzF(P(MonoFreq))}") : "Mono below freq"),
                    Toggle(AutoMatch, "Level match", () => On(AutoMatch) ? NotaNum.F($"Level match {Sc(S_AutoDb):+0.0;−0.0;0.0} dB") : "Level match") } });
        }

        Control OutputTab()
        {
            Control Line(string k, Func<string> v, IBrush? ink = null)
            {
                var val = Mono("", 8, ink ?? TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => val.Text = v());
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(new TextBlock { Text = k, FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center });
                g.Children.Add(Col(val, 1));
                return g;
            }
            var box = new Border
            {
                Background = Sunken, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 3),
                Child = new StackPanel { Spacing = 1, Children = {
                    Caps("MEASUREMENTS"),
                    Line("in", () => NotaNum.F($"{LvlF(ScDb(S_InLevel))} {Unit()}")),
                    Line("out", () => NotaNum.F($"{LvlF(ScDb(S_OutLevel))} {Unit()}")),
                    Line("true peak", () => NotaNum.F($"{LvlF(ScDb(S_TruePeak))} dBTP"), AccentBright) } },
            };
            var matchTo = Seg(MatchTo, new[] { "Input", "Target" }, "What Gain match and Level match aim for — the input's level, or Target");
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
            var gm = Button("Gain match", GainMatch, GainMatchTip, false);
            gm.Background = NotaPalette.AccentSubtle; gm.BorderBrush = Brass; gm.BorderThickness = new Thickness(1);
            ((TextBlock)gm.Child!).Foreground = AccentBright; ((TextBlock)gm.Child!).FontWeight = FontWeight.SemiBold;
            btns.Children.Add(gm);
            btns.Children.Add(Col(Button("Reset", ResetLevels, ResetTip, false), 1));
            return Spread(
                box,
                SliderRow("GAIN", Gain, Db1, bipolar: true),
                SliderRow("TARGET", Target, v => NotaNum.F($"{v:0.0}"), modulation: true),
                SliderRow("CEILING", TpCeiling, v => NotaNum.F($"{v:0.0}"), dim: () => !On(TpLimit)),
                LabelRow("MATCH", matchTo, 40),
                Divided(btns),
                new StackPanel { Spacing = 5, Children = {
                    Toggle(AutoMatch, "Level match", () => On(AutoMatch) ? NotaNum.F($"Level match {Sc(S_AutoDb):+0.0;−0.0;0.0} dB") : "Level match"),
                    Toggle(TpLimit, "True-peak limiter", () => On(TpLimit) ? NotaNum.F($"TP limit {CeilF(P(TpCeiling))}{(Sc(S_LimDb) > 0.05 ? NotaNum.F($" · −{Sc(S_LimDb):0.0}") : "")}") : "True-peak limiter") } });
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        int centreTab = 0;
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[2];
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            extras.Text = centreTab switch
            {
                1 => NotaNum.F($"width by frequency · {(On(WidthMode) ? "M/S" : "L/R")}"),
                2 => NotaNum.F($"{Sc(S_HistSec):0} s window · {Meters[MeterI()]}"),
                _ => "pan by frequency · 1 s window",
            };
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => MonoTab(), 2 => LevelsTab(), _ => FieldTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : RoutingTab();

        var centre = TabFrame(new[] { "Field", "Mono", "Levels" }, centreHost, CentreBody, false, t => { centreTab = t; Refresh(); }, extras);
        var rightFrame = TabFrame(new[] { "Routing", "Output" }, rightHost, RightBody, true, _ => RefreshAll(), null);
        var right = new Border { Width = 186, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = rightFrame };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Margin = new Thickness(5, 0), ClipToBounds = true, Child = centre };

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, TextSecondary);
        string StatusText()
        {
            var parts = new List<string>();
            switch (centreTab)
            {
                case 1:
                    parts.Add(On(WidthMode) ? "M/S" : "L/R");
                    parts.Add(On(MonoBelow) ? NotaNum.F($"mono ↓ {HzF(P(MonoFreq))} · {Slopes[Idx(MonoSlope)]} dB/oct") : "mono off");
                    parts.Add(NotaNum.F($"width {P(Width_):0}"));
                    parts.Add(NotaNum.F($"balance {BalF(P(Balance))}"));
                    if (On(InvertL)) parts.Add("Ø L");
                    if (On(InvertR)) parts.Add("Ø R");
                    parts.Add("gain " + GainF(P(Gain)));
                    break;
                case 2:
                    parts.Add(Meters[MeterI()]);
                    parts.Add("in " + LvlF(ScDb(S_InLevel)));
                    parts.Add("out " + LvlF(ScDb(S_OutLevel)));
                    parts.Add(On(MatchTo) ? NotaNum.F($"target {P(Target):0.0}") : "match to input");
                    parts.Add("gain " + GainF(P(Gain)));
                    if (On(AutoMatch)) parts.Add(NotaNum.F($"auto {Sc(S_AutoDb):+0.0;−0.0;0.0} dB"));
                    if (On(TpLimit)) parts.Add("TP " + CeilF(P(TpCeiling)));
                    parts.Add(NotaNum.F($"width {P(Width_):0}"));
                    break;
                default:
                    parts.Add(Modes[Idx(ChannelMode)]);
                    parts.Add("gain " + Db1(P(Gain)));
                    parts.Add(NotaNum.F($"balance {BalF(P(Balance))}"));
                    parts.Add(NotaNum.F($"width {P(Width_):0}{(On(WidthMode) ? " M/S" : "")}"));
                    parts.Add(On(MonoBelow) ? NotaNum.F($"mono ↓ {HzF(P(MonoFreq))}") : "mono off");
                    parts.Add(PhaseText());
                    if (On(Mute)) parts.Add("muted");
                    break;
            }
            return string.Join(" · ", parts);
        }
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            double sr = Sc(S_SampleRate);
            statusRight.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#} kHz · latency 0 smp · CPU {Sc(S_Cpu) * 100:0.0} %") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { levelCol, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

        void Refresh()
        {
            // The band analysis (an FFT) runs only while a tab that draws it is showing.
            bool bands = centreTab != 2;
            scN = engine.DeviceScope(track, di, scope, bands ? kScope : kBandAt);
            bool haveBands = bands && scN >= kScope && Sc(S_Analysed) > 0.5;
            bool loud = false;
            if (haveBands) for (int b = 0; b < kBands; b++) if (scope[kBandAt + B_Level * kBands + b] > -100) { loud = true; break; }
            if (centreTab == 0 && field is not null && !field.Dragging)
                field.Set(scope, kBandAt + B_Pan * kBands, kBandAt + B_Width * kBands, kBandAt + B_Level * kBands, kBandAt + B_Set * kBands, haveBands ? kBands : 0,
                    haveBands && loud, Sc(S_BandLo), Sc(S_BandHi), NotaNum.F($"width {P(Width_):0} %"));
            if (centreTab == 1 && widthView is not null && !widthView.Dragging)
                widthView.Set(scope, kRespAt, haveBands ? kResp : 0, Sc(S_RespLo), Sc(S_RespHi), kBandAt + B_Width * kBands, kBandAt + B_Level * kBands, haveBands ? kBands : 0,
                    Sc(S_BandLo), Sc(S_BandHi), haveBands && loud, On(MonoBelow) ? P(MonoFreq) : double.NaN, NotaNum.F($"{Sc(S_EffWidth):0} %"));
            if (centreTab == 2 && levelView is not null && !levelView.Dragging)
            {
                int m = MeterI();
                int hin = m == 0 ? H_InLufs : m == 1 ? H_InPeak : H_InRms, hout = m == 0 ? H_OutLufs : m == 1 ? H_OutPeak : H_OutRms;
                levelView.Set(scope, kHistAt + hin * kHist, kHistAt + hout * kHist, scN >= kBandAt ? kHist : 0,
                    On(MatchTo) ? P(Target) : double.NaN, Sc(S_HistSec) > 0 ? Sc(S_HistSec) : 8,
                    NotaNum.F($"{LvlF(ScDb(S_OutLevel))} {Unit()}"),
                    On(MatchTo) ? NotaNum.F($"target {P(Target):0.0}") : "target = input");
            }
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

        static int NearestHz(float v, float[] hz)
        {
            for (int i = 0; i < hz.Length; i++) if (Math.Abs(v - hz[i]) < 0.5f) return i;
            return -1;
        }

        // local: a tab frame (bar + swapping body), optional extras on the right of the bar.
        Control TabFrame(string[] tabs, ContentControl host, Func<int, Control> body, bool centered, Action<int>? changed, Control? extrasCtl)
        {
            int sel = 0;
            var btns = new Border[tabs.Length];
            void Hi()
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool on = i == sel;
                    btns[i].Background = on ? Card2 : Brushes.Transparent;
                    btns[i].BorderBrush = on ? Brass : Brushes.Transparent;
                    var tb = (TextBlock)btns[i].Child!;
                    tb.Foreground = on ? AccentBright : TextTertiary;
                    tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            var bar = centered ? (Avalonia.Controls.Panel)new UniformGrid { Rows = 1 } : new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < tabs.Length; i++)
            {
                int iv = i;
                var b = new Border
                {
                    Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = tabs[i], FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
                };
                b.PointerPressed += (_, _) => { sel = iv; Hi(); host.Content = body(iv); changed?.Invoke(iv); };
                btns[i] = b;
                bar.Children.Add(b);
            }
            var barDock = new DockPanel { Height = 20, LastChildFill = centered };
            if (extrasCtl != null) { var ex = new Border { Padding = new Thickness(0, 0, 8, 0), Child = extrasCtl }; DockPanel.SetDock(ex, Dock.Right); barDock.Children.Add(ex); }
            if (!centered) DockPanel.SetDock(bar, Dock.Left);
            barDock.Children.Add(bar);
            var barBorder = new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock };
            DockPanel.SetDock(barBorder, Dock.Top);
            Hi();
            host.Content = body(sel);
            return new DockPanel { LastChildFill = true, Children = { barBorder, host } };
        }
    }
}
