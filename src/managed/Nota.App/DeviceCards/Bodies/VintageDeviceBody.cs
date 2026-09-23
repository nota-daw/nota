// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Vintage body (device kind 8), a build of the "Nota
// Vintage" mockup (700 × 260) on the Auto Filter / Lens / Compressor frame: an always-
// visible STATE column (drive · wear faders), a centre panel with Curve / Wear / Output
// tabs — the transfer curve the engine runs, the pitch drift over the last four seconds,
// the harmonics against the hiss floor — each with the character list in its head row, a
// right panel with Tone / Output tabs, and a status strip. Every control is a device param,
// so automation / MIDI learn / presets / A-B / persistence come for free.
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

internal sealed class VintageDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Vintage.h) ─────────────────────────────
    private const int Mode = 0, Drive = 1, Tone = 2, Wow = 3, Flutter = 4, Noise = 5, Crackle = 6, Wear = 7, Mix = 8,
        Output = 9, OS = 10, ToneLow = 11, ToneHigh = 12, ToneModel = 13, Character = 14, AutoComp = 15, WowRate = 16,
        FlutterRate = 17, WowSync = 18, HissHp = 19, WearFollow = 20, StereoDrift = 21, OutputStage = 22, EvenOnly = 23;
    // Scope layout (Vintage::S_* / kTele / kCurve / kHist).
    private const int S_InPk = 0, S_OutPk = 1, S_Cpu = 2, S_SampleRate = 3, S_Delay = 5, S_WowHz = 6,
        S_FlutHz = 7, S_WowCents = 8, S_FlutCents = 9, S_BandHz = 10, S_HissDb = 11, S_Thd = 12, S_H1 = 13, S_TestLvl = 20,
        S_Asym = 21, S_CompDb = 22, S_HistLen = 25, S_HistMs = 26, kTele = 32, kCurve = 48, kHist = 1024;
    private const int kScope = kTele + kCurve + 2 * kHist;

    private static readonly string[] Modes = { "Vinyl", "Cassette", "Reel", "VHS", "Tube", "Analog" };
    private static readonly string[] OsNames = { "Off", "2×", "4×", "8×" };
    private static readonly string[] SyncNames = { "4 bars", "2 bars", "1 bar", "1/2", "1/4", "1/8" };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "CHARACTER";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) => engine.DeviceSetParam(track, di, p, Math.Clamp(v, 0f, 1f));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        bool On(int p) => P(p) >= 0.5f;
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        // by index: a panel built lazily inside a readout (the Tone tab's two bodies) adds its own
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        // ---- units ----------------------------------------------------------------
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string Pct(double v) => NotaNum.F($"{v * 100:0} %");
        static string ToneF(double v) => NotaNum.F($"{(v - 0.5) * 10:+0.0;−0.0;0}");
        static string GainF(double v) => NotaNum.F($"{(v - 0.5) * 24:+0.0;−0.0;0.0} dB");
        static string ShelfF(double v) => NotaNum.F($"{(v - 0.5) * 24:+0.0;−0.0;0.0}");
        static string HzF(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0#} kHz") : hz >= 10 ? NotaNum.F($"{hz:0} Hz") : hz >= 1 ? NotaNum.F($"{hz:0.0} Hz") : NotaNum.F($"{hz:0.00} Hz");
        static string DbF(double lin) => lin > 1e-5 ? NotaNum.F($"{20 * Math.Log10(lin):0.0;−0.0} dB") : "−∞ dB";
        static string DbfsF(double lin) => lin > 1e-5 ? NotaNum.F($"{20 * Math.Log10(lin):0.0;−0.0} dBFS") : "−∞ dBFS";
        static string ThdF(double v) => v < 0.001 ? "< 0.1 %" : NotaNum.F($"{v * 100:0.0} %");
        static string CentsF(double c) => NotaNum.F($"±{c:0} ¢");
        bool Synced() => On(WowSync);
        string WowRateF(double v) => Synced() ? SyncNames[Math.Clamp((int)Math.Round(v * 5), 0, 5)] : HzF(Exp(v, 0.1, 4));
        static string FlutRateF(double v) => HzF(Exp(v, 2, 20));
        static string HissHpF(double v) => HzF(Exp(v, 20, 2000));
        string ModeName() => Modes[Sel(Mode, 6)];
        string OsText() => Sel(OS, 4) == 0 ? "no oversampling" : NotaNum.F($"{OsNames[Sel(OS, 4)]} OS");
        double WowHz() => Sc(S_WowHz) > 0 ? Sc(S_WowHz) : Exp(P(WowRate), 0.1, 4);
        double FlutHz() => Sc(S_FlutHz) > 0 ? Sc(S_FlutHz) : Exp(P(FlutterRate), 2, 20);
        double H(int k) => Sc(S_H1 + k - 1);   // harmonic k, dB re the fundamental
        string Dominant()
        {
            double even = 0, odd = 0;
            for (int k = 2; k <= 7; k++) { double pw = Math.Pow(10, H(k) / 10); if (k % 2 == 0) even += pw; else odd += pw; }
            if (even + odd < 1e-9) return "clean";
            return even > odd * 1.5 ? "even dominant" : odd > even * 1.5 ? "odd dominant" : "even + odd";
        }
        static string Ordinal(int k) => k switch { 2 => "2nd", 3 => "3rd", _ => NotaNum.F($"{k}th") };
        string Loudest() { int best = 2; for (int k = 3; k <= 7; k++) if (H(k) > H(best)) best = k; return H(best) > -100 ? Ordinal(best) + " harmonic" : "no harmonics"; }
        string StageName() => Sel(OutputStage, 3) switch { 1 => "tube stage", 2 => "analog stage", _ => "" };

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
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, double cellW = 46)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = 34, Height = 34 };
            knob.ValueChanged += v => { Raw(p, (float)v); val.Text = fmt(v); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            readouts.Add(() => { if (knob.Dragging) return; float c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(c); });
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

        // Segmented pill over a discrete param (n options spread over 0..1).
        Control Seg(int p, string[] names)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); RefreshAll(); }, out var sync, padX: 6);
            readouts.Add(sync);
            Learn(seg, p);
            return seg;
        }

        // Outlined chips: lit = brass edge + wash.
        Control Chips(string[] names, Func<int, bool> lit, Action<int> pick, int learnParam, double h = 16, double padX = 7)
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
            if (learnParam >= 0) Learn(row, learnParam);
            return row;
        }

        // A latching chip: lit when `lit()`, a click runs `click`; its text can follow the value.
        Border Latch(int p, Func<bool> lit, Action click, Func<string> text, string tip, bool fill = false)
        {
            var tb = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
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
            Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }
        Border ToggleLatch(int p, Func<string> text, string tip, bool fill = false)
            => Latch(p, () => On(p), () => SetP(p, On(p) ? 0f : 1f), text, tip, fill);

        // Slider row: caps label · track · mono value.
        Control SliderRow(string label, int p, Func<double, string> fmt, bool bipolar = false, bool modulation = false, double labW = 40)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => Raw(p, (float)v), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), bipolar: bipolar, valueWidth: 34, modulation: modulation);
            readouts.Add(sync);
            Learn(bar, p);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            return g;
        }

        // The character list: [name ▾] opens the six voicings; the wheel steps through them.
        Control CharDrop()
        {
            var name = new TextBlock { FontSize = 8, FontWeight = FontWeight.SemiBold, Foreground = AccentBright, VerticalAlignment = VerticalAlignment.Center };
            var box = new Border
            {
                Height = 16, MinWidth = 74, Background = Sunken, BorderBrush = Brass, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge,
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(new Glyph(GlyphKind.ChevronDown, 7) { Foreground = NotaPalette.AccentDim, Margin = new Thickness(6, 0, 0, 0) }, Dock.Right), name } },
            };
            readouts.Add(() => name.Text = ModeName());
            ToolTip.SetTip(box, "Character — the era the signal passes through: its saturation, tone, band-limit and how much hiss, crackle, wow and flutter it brings. Scroll to step.");
            Learn(box, Mode);
            box.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                var fly = new MenuFlyout();
                int cur = Sel(Mode, 6);
                for (int i = 0; i < Modes.Length; i++)
                {
                    int iv = i;
                    var mi = new MenuItem { Header = Modes[i] };
                    if (i == cur) mi.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brass };
                    mi.Click += (_, _) => { SetP(Mode, iv / 5f); RefreshAll(); };
                    fly.Items.Add(mi);
                }
                fly.ShowAt(box);
                e.Handled = true;
            };
            box.PointerWheelChanged += (_, e) =>
            {
                int m = Math.Clamp(Sel(Mode, 6) + (e.Delta.Y < 0 ? 1 : -1), 0, 5);
                SetP(Mode, m / 5f); RefreshAll(); e.Handled = true;
            };
            return box;
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
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 52 };
            g.Children.Add(Row(4, knobs));
            g.Children.Add(Col(new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { i1, i2 } }, 1));
            return g;
        }
        static Control TabBody(Control head, Control window, Control knobs) => new DockPanel
        {
            LastChildFill = true, Margin = new Thickness(8, 5, 8, 0),
            Children = { Docked(head, Dock.Top), Docked(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 5, 0, 0), Child = window } },
        };

        // ======================================================================
        // LEFT — STATE column (drive · wear)
        // ======================================================================
        Control StateFader(int p, string label, IBrush ink, string tip)
        {
            var f = new AfFader { Ink = ink, Width = 16, VerticalAlignment = VerticalAlignment.Stretch, Default = engine.DeviceParamDefault(track, di, p) };
            f.Changed += v => { Raw(p, (float)v); RefreshAll(); };
            f.GestureBegin += () => Begin(p);
            f.GestureEnd += () => End(p);
            Learn(f, p);
            ToolTip.SetTip(f, tip);
            readouts.Add(() => f.Set(P(p), double.NaN));
            var lb = Caps(label); lb.HorizontalAlignment = HorizontalAlignment.Center;
            return new DockPanel { Children = { Docked(lb, Dock.Bottom), new Border { Margin = new Thickness(0, 0, 0, 3), Child = f } } };
        }
        var stateTitle = Caps("STATE"); stateTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var stateDrv = Mono("", 7, AccentBright); stateDrv.HorizontalAlignment = HorizontalAlignment.Center;
        var stateWear = Mono("", 7, TextPrimary); stateWear.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() =>
        {
            stateDrv.Text = Pct(P(Drive));
            stateWear.Text = Pct(P(Wear));
            stateTitle.Foreground = Sc(S_InPk) > 0.01 ? AccentBright : TextTertiary;   // lit while signal passes
        });
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        faders.Children.Add(StateFader(Drive, "DRV", Brass, "Drive — how hard the signal hits the saturation. Double-click resets."));
        faders.Children.Add(Col(StateFader(Wear, "WEAR", Teal, "Wear — the age of the medium: a narrower band, more hiss and crackle, a wandering flutter. Double-click resets."), 1));
        var stateCol = new Border
        {
            Width = 56, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(stateTitle, Dock.Top), Docked(stateWear, Dock.Bottom), Docked(stateDrv, Dock.Bottom), faders } },
        };
        DockPanel.SetDock(stateCol, Dock.Left);

        // ======================================================================
        // CENTRE — Curve
        // ======================================================================
        VtCurveView? curve = null;
        Control CurveTab()
        {
            curve = new VtCurveView();
            curve.StartV = () => P(Drive);
            curve.DragV += v => Raw(Drive, (float)v);
            curve.GestureBegin += () => Begin(Drive);
            curve.GestureEnd += () => End(Drive);
            ToolTip.SetTip(curve, "The transfer curve the saturation runs — input level across, output up; dashed is the dry line, teal what you hear with Mix and Output, the dot where the input peak sits. Drag up or down for the drive.");
            Learn(curve, Drive);

            var thd = Mono("", 7, TextTertiary);
            readouts.Add(() => thd.Text = "THD " + ThdF(Sc(S_Thd)));
            var count = Mono("", 7, TextTertiary);
            readouts.Add(() => count.Text = NotaNum.F($"{Sel(Mode, 6) + 1} / 6"));
            var head = HeadRow(Row(6, Caps("CHAR"), CharDrop(), count, new Border { Width = 4 }, Caps("OS"), Seg(OS, OsNames)), thd);
            var knobs = KnobRow(new[]
            {
                K(Drive, "DRIVE", Pct),
                K(Tone, "TONE", ToneF),
                K(Mix, "MIX", Pct),
                K(Output, "OUTPUT", GainF, Teal),
            },
            () => NotaNum.F($"peak {DbF(Sc(S_InPk))} → {DbF(Sc(S_OutPk))}"),
            () => NotaNum.F($"{OsText()} · asym {Pct(Sc(S_Asym))} · {Dominant()}"));
            return TabBody(head, curve, knobs);
        }

        // ======================================================================
        // CENTRE — Wear
        // ======================================================================
        VtWearView? wearView = null;
        int wearShow = 2;   // 0 wow, 1 flutter, 2 both — what the window shows and a drag edits
        Control WearTab()
        {
            wearView = new VtWearView();
            wearView.StartV = () => wearShow == 1 ? P(Flutter) : P(Wow);
            wearView.StartH = () => wearShow == 1 ? P(FlutterRate) : P(WowRate);
            wearView.DragV += v =>
            {
                if (wearShow != 1) { double d = v - P(Wow); Raw(Wow, (float)v); if (wearShow == 2) Raw(Flutter, (float)(P(Flutter) + d)); }
                else Raw(Flutter, (float)v);
            };
            wearView.DragH += v => Raw(wearShow == 1 ? FlutterRate : WowRate, (float)v);
            wearView.GestureBegin += () => { Begin(Wow); Begin(Flutter); Begin(WowRate); Begin(FlutterRate); };
            wearView.GestureEnd += () => { End(Wow); End(Flutter); End(WowRate); End(FlutterRate); };
            ToolTip.SetTip(wearView, "The pitch drift over the last four seconds — wow in brass, flutter in teal. Drag up or down for the depth of what is picked above, sideways for its rate.");
            Learn(wearView, Wow);

            var show = Chips(new[] { "Wow", "Flutter", "Both" }, i => wearShow == i, i => wearShow = i, -1);
            ToolTip.SetTip(show, "What the window shows and a drag in it edits");
            var sync = Latch(WowSync, () => On(WowSync), () => SetP(WowSync, On(WowSync) ? 0f : 1f),
                () => Synced() ? "Sync " + SyncNames[Sel(WowRate, 6)] : "Sync off",
                "Sync — the wow follows the tempo: one sway every 4 bars … 1/8 (RATE picks it)");
            var drift = Mono("", 7, AccentBright);
            readouts.Add(() => drift.Text = "drift " + HzF(WowHz()));
            var head = HeadRow(Row(6, Caps("CHAR"), CharDrop(), show, sync), drift);
            var knobs = KnobRow(new[]
            {
                K(Wow, "WOW", Pct, null, 40),
                K(WowRate, "RATE", WowRateF, null, 40),
                K(Flutter, "FLUTTER", Pct, Teal, 40),
                K(FlutterRate, "F·RATE", FlutRateF, Teal, 40),
                K(Crackle, "CRACKLE", Pct, null, 40),
                K(Wear, "WEAR", Pct, null, 40),
            },
            () => NotaNum.F($"{CentsF(Sc(8) + Sc(9))} · wow {HzF(WowHz())} · flut {HzF(FlutHz())}"),
            () => (Sc(S_BandHz) > 0 ? "band " + HzF(Sc(S_BandHz)) : "band open") + (Sc(S_HissDb) > -119 ? NotaNum.F($" · hiss {Sc(S_HissDb):0} dB") : " · no hiss"));
            return TabBody(head, wearView, knobs);
        }
        void SyncWearView()
        {
            if (wearView is null) return;
            int n = scN >= kScope ? (int)Math.Min(kHist, Sc(S_HistLen)) : 0;
            double secs = kHist * (Sc(S_HistMs) > 0 ? Sc(S_HistMs) : 4) / 1000.0;
            string peak = wearShow switch { 0 => "+" + CentsF(Sc(S_WowCents))[1..], 1 => "+" + CentsF(Sc(S_FlutCents))[1..], _ => "+" + CentsF(Sc(S_WowCents) + Sc(S_FlutCents))[1..] };
            wearView.Set(scope, kTele + kCurve, kTele + kCurve + kHist, n, wearShow, peak,
                NotaNum.F($"wow {HzF(WowHz())}"), NotaNum.F($"{secs:0} s"));
        }

        // ======================================================================
        // CENTRE — Output
        // ======================================================================
        VtHarmView? harm = null;
        Control OutputTab()
        {
            harm = new VtHarmView();
            harm.StartV = () => P(Drive);
            harm.DragV += v => Raw(Drive, (float)v);
            harm.GestureBegin += () => Begin(Drive);
            harm.GestureEnd += () => End(Drive);
            ToolTip.SetTip(harm, "The harmonics the saturation adds at the input's level (the fundamental at the top), against the hiss floor in teal. Drag up or down for the drive.");
            Learn(harm, Drive);

            var peak = Mono("", 7, AccentBright);
            readouts.Add(() => peak.Text = DbfsF(Sc(S_OutPk)));
            var comp = ToggleLatch(AutoComp, () => Sc(S_CompDb) != 0 && On(AutoComp) ? NotaNum.F($"Auto-comp {Sc(S_CompDb):+0.0;−0.0;0.0}") : "Auto-comp",
                "Auto-comp — the output level follows the input's, however hard the drive");
            var head = HeadRow(Row(6, Caps("CHAR"), CharDrop(), new Border { Width = 4 }, Caps("OS"), Seg(OS, OsNames), comp), peak);
            var knobs = KnobRow(new[]
            {
                K(Drive, "DRIVE", Pct),
                K(Tone, "TONE", ToneF),
                K(Wear, "WEAR", Pct, Teal),
                K(Output, "OUTPUT", GainF),
            },
            () => H(2) > -100 || H(3) > -100 ? NotaNum.F($"2nd {H(2):0} dB · 3rd {H(3):0} dB · {Dominant()}") : "no harmonics — clean",
            () => NotaNum.F($"{OsText()}{(On(AutoComp) ? NotaNum.F($" · comp {Sc(S_CompDb):+0.0;−0.0;0.0} dB") : "")}"));
            return TabBody(head, harm, knobs);
        }
        void SyncHarm()
        {
            if (harm is null) return;
            // the hiss floor on the harmonics' scale: relative to the fundamental at the test level
            double lvl = Math.Max(1e-4, Sc(S_TestLvl));
            double floor = Sc(S_HissDb) > -119 ? Sc(S_HissDb) - 20 * Math.Log10(lvl) : -200;
            harm.Set(scope, S_H1, floor, "THD " + ThdF(Sc(S_Thd)), Sc(S_HissDb) > -119 ? NotaNum.F($"hiss floor {Sc(S_HissDb):0} dB") : "");
        }

        // ======================================================================
        // RIGHT — Tone / Output
        // ======================================================================
        int centreTab = 0;
        Control Box(string title, Func<string> line1, Func<string> line2, Func<bool>? lit = null)
        {
            var t = Caps(title);
            var l1 = Mono("", 9, AccentBright);
            var l2 = Mono("", 8, TextSecondary);
            var b = new Border { Background = Sunken, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 2, Children = { t, l1, l2 } } };
            readouts.Add(() =>
            {
                l1.Text = line1(); l2.Text = line2();
                bool on = lit?.Invoke() ?? false;
                b.BorderBrush = on ? NotaPalette.BorderBrass : NotaPalette.GraphBorder;
                t.Foreground = on ? AccentBright : TextTertiary;
            });
            return b;
        }
        Control ModelRow()
        {
            var chips = Chips(new[] { "Warm", "Flat", "Dark" }, i => Sel(ToneModel, 3) == i, i => SetP(ToneModel, i / 2f), ToneModel, h: 15, padX: 6);
            ToolTip.SetTip(chips, "Tone model — Warm: the era's own tilt and head bump; Flat: the TONE knob alone; Dark: a lower, darker tilt");
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*") };
            g.Children.Add(Caps("MODEL")); g.Children.Add(Col(chips, 1));
            return g;
        }
        static Control Rule(Control c) => new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = c };
        static Control Spread(params Control[] rows)
        {
            var defs = new List<string>();
            for (int i = 0; i < rows.Length; i++) { if (i > 0) defs.Add("*"); defs.Add("Auto"); }
            var g = new Grid { RowDefinitions = new RowDefinitions(string.Join(",", defs)) };
            for (int i = 0; i < rows.Length; i++) g.Children.Add(GRow(rows[i], i * 2));
            return new Border { Padding = new Thickness(8, 6), Child = g };
        }

        // Tone for the Curve / Output tabs: the model, the shelves, the character.
        Control ToneCharBody() => Spread(
            ModelRow(),
            SliderRow("LOW", ToneLow, ShelfF, bipolar: true),
            SliderRow("HIGH", ToneHigh, ShelfF, bipolar: true),
            Rule(SliderRow("MIX", Mix, Pct)),
            Box("CHARACTER", () => NotaNum.F($"{ModeName()} · asym {Pct(Sc(S_Asym))}"),
                () => P(Wear) > 0.004f ? NotaNum.F($"wear {Pct(P(Wear))} · {Loudest()}") : "no wear · " + Loudest(), () => On(Character)),
            new StackPanel { Spacing = 5, Children = {
                Toggle(Character, "Character", () => On(Character) ? "Character: " + ModeName() : "Character off"),
                Toggle(AutoComp, "Auto-compensation") } });
        // Tone for the Wear tab: the model, the hiss and its high-pass, the wear box.
        Control ToneWearBody() => Spread(
            ModelRow(),
            SliderRow("NOISE", Noise, Pct, modulation: true),
            SliderRow("HISS HP", HissHp, HissHpF, modulation: true),
            Rule(SliderRow("MIX", Mix, Pct)),
            Box("WEAR", () => NotaNum.F($"{CentsF(Sc(S_WowCents) + Sc(S_FlutCents))} · peak drift"),
                () => NotaNum.F($"wow {Pct(P(Wow))} + flutter {Pct(P(Flutter))}"), () => P(Wow) + P(Flutter) > 0.01f),
            new StackPanel { Spacing = 5, Children = {
                Toggle(WearFollow, "Wear follows input"),
                Toggle(StereoDrift, "Stereo drift") } });
        Control ToneTab()
        {
            var host = new ContentControl();
            Control? a = null, b = null;
            readouts.Add(() =>
            {
                Control want = centreTab == 1 ? (b ??= ToneWearBody()) : (a ??= ToneCharBody());
                if (!ReferenceEquals(host.Content, want)) host.Content = want;
            });
            return host;
        }

        Control OutTab()
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
                Background = Sunken, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 2, Children = {
                    Caps("OUTPUT"),
                    Line("in", () => DbF(Sc(S_InPk))),
                    Line("out", () => DbF(Sc(S_OutPk))),
                    Line("THD", () => ThdF(Sc(S_Thd)), AccentBright) } },
            };
            var sliders = new StackPanel { Spacing = 6, Children = {
                SliderRow("MIX", Mix, Pct),
                SliderRow("NOISE", Noise, Pct, modulation: true),
                SliderRow("GAIN", Output, ShelfF, bipolar: true) } };
            // Output stage: Tube / Analog, a click on the lit one switches the stage off.
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
            btns.Children.Add(Latch(OutputStage, () => Sel(OutputStage, 3) == 1, () => SetP(OutputStage, Sel(OutputStage, 3) == 1 ? 0f : 0.5f), () => "Tube",
                "Tube stage — a warm, asymmetric valve stage after the saturation (click again for none)", fill: true));
            btns.Children.Add(Col(Latch(OutputStage, () => Sel(OutputStage, 3) == 2, () => SetP(OutputStage, Sel(OutputStage, 3) == 2 ? 0f : 1f), () => "Analog",
                "Analog stage — a console-style soft clip after the saturation (click again for none)", fill: true), 1));
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(box, Dock.Top),
                Docked(new Border { Margin = new Thickness(0, 7, 0, 0), Child = sliders }, Dock.Top),
                Docked(new StackPanel { Spacing = 5, Children = { Toggle(AutoComp, "Auto-compensation"), Toggle(EvenOnly, "Even harmonics only") } }, Dock.Bottom),
                Docked(new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 5), Child = btns }, Dock.Bottom) } };
            return new Border { Padding = new Thickness(8, 6), Child = body };
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[2];
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            string m = ModeName().ToLowerInvariant();
            extras.Text = centreTab switch
            {
                1 => NotaNum.F($"window {kHist * (Sc(S_HistMs) > 0 ? Sc(S_HistMs) : 4) / 1000:0} s · {m}"),
                2 => NotaNum.F($"{m} · {(Sel(OS, 4) == 0 ? "no OS" : OsNames[Sel(OS, 4)] + " OS")} · THD {ThdF(Sc(S_Thd))}"),
                _ => NotaNum.F($"{m} · asym {Pct(Sc(S_Asym))} · {Loudest()}"),
            };
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => WearTab(), 2 => OutputTab(), _ => CurveTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutTab() : ToneTab();

        var centre = TabFrame(new[] { "Curve", "Wear", "Output" }, centreHost, CentreBody, false, t => { centreTab = t; Refresh(); }, extras);
        var rightFrame = TabFrame(new[] { "Tone", "Output" }, rightHost, RightBody, true, _ => RefreshAll(), null);
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
            var parts = new List<string> { On(Character) ? ModeName() : ModeName() + " (voicing off)" };
            switch (centreTab)
            {
                case 1:
                    parts.Add(NotaNum.F($"wow {Pct(P(Wow))}{(Synced() ? " " + SyncNames[Sel(WowRate, 6)] : "")}"));
                    parts.Add(NotaNum.F($"flutter {Pct(P(Flutter))}"));
                    parts.Add(NotaNum.F($"crackle {Pct(P(Crackle))}"));
                    parts.Add(NotaNum.F($"wear {Pct(P(Wear))}"));
                    parts.Add(NotaNum.F($"noise {Pct(P(Noise))}"));
                    if (On(WearFollow)) parts.Add("follows input");
                    if (On(StereoDrift)) parts.Add("stereo drift");
                    break;
                case 2:
                    parts.Add(NotaNum.F($"drive {Pct(P(Drive))}"));
                    parts.Add("tone " + ToneF(P(Tone)));
                    parts.Add(NotaNum.F($"wear {Pct(P(Wear))}"));
                    parts.Add(NotaNum.F($"mix {Pct(P(Mix))}"));
                    parts.Add("out " + GainF(P(Output)));
                    if (StageName().Length > 0) parts.Add(StageName());
                    if (On(EvenOnly)) parts.Add("even only");
                    parts.Add(Sel(OS, 4) == 0 ? "OS off" : OsNames[Sel(OS, 4)] + " OS");
                    break;
                default:
                    parts.Add(NotaNum.F($"drive {Pct(P(Drive))}"));
                    parts.Add("tone " + ToneF(P(Tone)));
                    parts.Add(NotaNum.F($"mix {Pct(P(Mix))}"));
                    parts.Add("out " + GainF(P(Output)));
                    parts.Add(Sel(OS, 4) == 0 ? "OS off" : OsNames[Sel(OS, 4)] + " OS");
                    if (On(AutoComp)) parts.Add("auto-comp");
                    break;
            }
            return string.Join(" · ", parts);
        }
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            double sr = Sc(S_SampleRate), dl = Sc(S_Delay);
            string lat = dl > 0 && sr > 0 ? NotaNum.F($"tape delay {dl / sr * 1000:0.0} ms") : "latency 0 smp";
            statusRight.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#} kHz · {lat} · CPU {Sc(S_Cpu) * 100:0.0} %") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { stateCol, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            if (centreTab == 0 && curve is not null && !curve.Dragging)
                curve.Set(scope, kTele, scN >= kTele + kCurve ? kCurve : 0, P(Mix), Math.Pow(10, ((P(Output) - 0.5) * 24 + Sc(S_CompDb)) / 20),
                    Sc(S_InPk), P(Crackle), NotaNum.F($"{ModeName().ToLowerInvariant()} · asym {Pct(Sc(S_Asym))}"));
            if (centreTab == 1) SyncWearView();
            if (centreTab == 2) SyncHarm();
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

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
