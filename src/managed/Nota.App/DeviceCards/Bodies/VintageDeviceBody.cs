// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Vintage body (device kind 8), drawn to the style guide's
// picture of it (790 × 260): an always-visible State column (drive · wear faders), a centre panel with Curve / Wear / Output
// tabs — the transfer curve the engine runs, the pitch drift over the last four seconds,
// the harmonics against the hiss floor — each with the character list in its head row, a
// right panel with Tone / Output tabs. Every control is a device param,
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

    public double Width => 790;
    public bool FullBleed => true;
    public string? Subtitle => "Character";

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
        static string ThdF(double v) => v < 0.001 ? "< 0.1 %" : NotaNum.F($"{v * 100:0.0} %");
        static string CentsF(double c) => NotaNum.F($"±{c:0} ¢");
        bool Synced() => On(WowSync);
        string WowRateF(double v) => Synced() ? SyncNames[Math.Clamp((int)Math.Round(v * 5), 0, 5)] : HzF(Exp(v, 0.1, 4));
        static string FlutRateF(double v) => HzF(Exp(v, 2, 20));
        static string HissHpF(double v) => HzF(Exp(v, 20, 2000));
        string ModeName() => Modes[Sel(Mode, 6)];
        string OsText() => Sel(OS, 4) == 0 ? "OS off" : NotaNum.F($"{OsNames[Sel(OS, 4)]} OS");
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
        static TextBlock Caps(string t, IBrush? c = null, double fs = 0) => DeviceBodyKit.Label(t, c);
        // An acronym NotaText.Label does not know (OS, GR) keeps its case: the label as written.
        static TextBlock Acr(string t, IBrush? c = null) { var tb = Caps(t, c); tb.Text = t; return tb; }
        // One value size (11); `fs` is kept for the call sites. Ink 1 = a value, else a note line.
        static TextBlock Mono(string t, double fs, IBrush c) => c == TextPrimary ? DeviceBodyKit.Value(t, c) : DeviceBodyKit.Note(t, c);
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
            var knob = new Knob(P(p), 1.0) { Accent = true, Default = engine.DeviceParamDefault(track, di, p) };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
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

        // A single choice drawn as segments (the picked one raised).
        Control Chips(string[] names, Func<int, bool> lit, Action<int> pick, int learnParam, double h = 16, double padX = 7)
        {
            int Cur() { for (int i = 0; i < names.Length; i++) if (lit(i)) return i; return -1; }
            var row = Segments(names, Cur, iv => { pick(iv); RefreshAll(); }, out var sync, padX: padX);
            readouts.Add(sync);
            
            if (learnParam >= 0) Learn(row, learnParam);
            return row;
        }

        // A latching button (raised; lit = the soft orange): a click runs `click`; its text can follow the value.
        Border Latch(int p, Func<bool> lit, Action click, Func<string> text, string tip, bool fill = false)
        {
            var b = TextButton(text());
            var tb = (TextBlock)b.Child!;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            b.VerticalAlignment = VerticalAlignment.Center;
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            void Hi()
            {
                bool on = lit();
                b.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                tb.Foreground = on ? AccentBright : TextPrimary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                tb.Text = text();
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            if (p >= 0) Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }
        Border ToggleLatch(int p, Func<string> text, string tip, bool fill = false)
            => Latch(p, () => On(p), () => SetP(p, On(p) ? 0f : 1f), text, tip, fill);

        // Slider row: caps label · track · mono value.
        Control SliderRow(string label, int p, Func<double, string> fmt, bool bipolar = false, bool modulation = false, double labW = 52)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => Raw(p, (float)v), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), bipolar: bipolar, valueWidth: 48);
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
            var name = new TextBlock { FontSize = NotaType.DeviceCaption, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            var box = new Border
            {
                Height = 22, MinWidth = 108, Background = NotaPalette.SurfaceHover, CornerRadius = NotaRadius.Field,
                Padding = new Thickness(8, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(new Glyph(GlyphKind.ChevronDown, 8) { Foreground = TextSecondary, Margin = new Thickness(6, 0, 0, 0) }, Dock.Right), name } },
            };
            box.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
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
                    mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = i == cur;
                    mi.Click += (_, _) => { SetP(Mode, iv / 5f); RefreshAll(); };
                    fly.Items.Add(mi);
                }
                fly.ShowAt(box);
                e.Handled = true;
            };
            box.PointerWheelChanged += (wt, e) =>
            {
                if (WheelInput.Step(e, wt) is not { } s) return;
                e.Handled = true; if (s == 0) return;
                int m = Math.Clamp(Sel(Mode, 6) - s, 0, 5);
                SetP(Mode, m / 5f); RefreshAll();
            };
            return box;
        }

        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 26, ColumnSpacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        Control KnobRow(Control[] knobs, Func<string> info1, Func<string> info2)
        {
            var i1 = Mono("", 11, TextSecondary); i1.HorizontalAlignment = HorizontalAlignment.Right; i1.TextTrimming = TextTrimming.CharacterEllipsis;
            var i2 = Mono("", 11, TextSecondary); i2.HorizontalAlignment = HorizontalAlignment.Right; i2.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 56 };   // knob 26 + value + label
            g.Children.Add(Row(9, knobs));
            g.Children.Add(Col(new StackPanel { Spacing = 8, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { i1, i2 } }, 1));
            return g;
        }
        static Control TabBody(Control head, Control window, Control knobs) => new DockPanel
        {
            LastChildFill = true,
            Children = { Docked(head, Dock.Top), Docked(new Border { Margin = new Thickness(0, 6, 0, 0), Child = knobs }, Dock.Bottom), new Border { Margin = new Thickness(0, 8, 0, 0), Child = window } },
        };

        // ======================================================================
        // LEFT — STATE column (drive · wear)
        // ======================================================================
        DeviceBodyKit.RailFader StateFader(int p, string label, Func<string> text, string tip)
        {
            var f = DeviceBodyKit.Upright(label, () => P(p), v => { Raw(p, (float)v); RefreshAll(); }, text, out var val, out var sync,
                () => Begin(p), () => End(p), () => Reset(p));
            Learn(f, p);
            ToolTip.SetTip(f, tip);
            readouts.Add(sync);
            return new DeviceBodyKit.RailFader(label, f, val);
        }
        var stateRail = DeviceBodyKit.FaderRail("State", true,
            StateFader(Drive, "Drv", () => Pct(P(Drive)), "Drive — how hard the signal hits the saturation. Double-click resets."),
            StateFader(Wear, "Wear", () => Pct(P(Wear)), "Wear — the age of the medium: a narrower band, more hiss and crackle, a wandering flutter. Double-click resets."));
        var stateCol = DeviceBodyKit.Sec(stateRail, new Thickness(8, 8, 8, 6), 75);
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
            ToolTip.SetTip(curve, "The transfer curve the saturation runs — input level across, output up; dashed is the dry line, the solid line what you hear with Mix and Output, the dot where the input peak sits. Drag up or down for the drive.");
            Learn(curve, Drive);

            var thd = Mono("", 11, TextSecondary);
            readouts.Add(() => thd.Text = "THD " + ThdF(Sc(S_Thd)));
            var count = Mono("", 11, TextSecondary);
            readouts.Add(() => count.Text = NotaNum.F($"{Sel(Mode, 6) + 1} / 6"));
            var head = HeadRow(Row(8, Caps("Char"), CharDrop(), count, new Border { Width = 4 }, Acr("OS"), Seg(OS, OsNames)), thd);
            var knobs = KnobRow(new[]
            {
                K(Drive, "DRIVE", Pct),
                K(Tone, "TONE", ToneF),
                K(Mix, "MIX", Pct),
                K(Output, "OUTPUT", GainF),
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
            ToolTip.SetTip(wearView, "The pitch drift over the last four seconds — wow in neutral ink, flutter in grey. Drag up or down for the depth of what is picked above, sideways for its rate.");
            Learn(wearView, Wow);

            var show = Chips(new[] { "Wow", "Flutter", "Both" }, i => wearShow == i, i => wearShow = i, -1);
            ToolTip.SetTip(show, "What the window shows and a drag in it edits");
            var sync = Latch(WowSync, () => On(WowSync), () => SetP(WowSync, On(WowSync) ? 0f : 1f),
                () => Synced() ? "Sync " + SyncNames[Sel(WowRate, 6)] : "Sync off",
                "Sync — the wow follows the tempo: one sway every 4 bars … 1/8 (RATE picks it)");
            var drift = Mono("", 7, TextSecondary);
            readouts.Add(() => drift.Text = "drift " + HzF(WowHz()));
            var head = HeadRow(Row(8, Caps("Char"), CharDrop(), show, sync), drift);
            var knobs = KnobRow(new[]
            {
                K(Wow, "WOW", Pct, null, 40),
                K(WowRate, "RATE", WowRateF, null, 40),
                K(Flutter, "FLUTTER", Pct, null, 40),
                K(FlutterRate, "F·RATE", FlutRateF, null, 40),
                K(Crackle, "CRACKLE", Pct, null, 40),
                K(Wear, "WEAR", Pct, null, 40),
            },
            // The two rates are on their knobs; these lines carry what the knobs add up to.
            () => NotaNum.F($"{CentsF(Sc(8) + Sc(9))} of pitch drift"),
            () => (Sc(S_BandHz) > 0 ? "band " + (Sc(S_BandHz) >= 10000 ? NotaNum.F($"{Sc(S_BandHz) / 1000:0} kHz") : HzF(Sc(S_BandHz))) : "band open")
                  + (Sc(S_HissDb) > -119 ? NotaNum.F($" · hiss {Sc(S_HissDb):0} dB") : " · no hiss"));
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
            ToolTip.SetTip(harm, "The harmonics the saturation adds at the input's level (the fundamental at the top), against the hiss floor. Drag up or down for the drive.");
            Learn(harm, Drive);

            var comp = ToggleLatch(AutoComp, () => Sc(S_CompDb) != 0 && On(AutoComp) ? NotaNum.F($"Auto-comp {Sc(S_CompDb):+0.0;−0.0;0.0}") : "Auto-comp",
                "Auto-comp — the output level follows the input's, however hard the drive");
            var head = HeadRow(Row(8, Caps("Char"), CharDrop(), new Border { Width = 4 }, Acr("OS"), Seg(OS, OsNames), comp), new Border());   // the output peak is the Output panel's in · out
            var knobs = KnobRow(new[]
            {
                K(Drive, "DRIVE", Pct),
                K(Tone, "TONE", ToneF),
                K(Wear, "WEAR", Pct),
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
            var l1 = Mono("", 11, TextPrimary);
            var l2 = Mono("", 11, TextSecondary);
            var b = DeviceBodyKit.Well(new StackPanel { Spacing = 2, Children = { Row(8, t, l1), l2 } }, new Thickness(10, 4));
            readouts.Add(() =>
            {
                l1.Text = line1(); l2.Text = line2();
                bool on = lit?.Invoke() ?? false;   // engaged: a stronger frame, not the accent
                t.Foreground = on ? TextSecondary : TextTertiary;
            });
            return b;
        }
        Control ModelRow()
        {
            var chips = Chips(new[] { "Warm", "Flat", "Dark" }, i => Sel(ToneModel, 3) == i, i => SetP(ToneModel, i / 2f), ToneModel, padX: 6);
            ToolTip.SetTip(chips, "Tone model — Warm: the era's own tilt and head bump; Flat: the TONE knob alone; Dark: a lower, darker tilt");
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("52,*") };
            g.Children.Add(Caps("Model")); g.Children.Add(Col(chips, 1));
            return g;
        }
        static Control Rule(Control c) => new Border { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0), Child = c };
        static Control Spread(params Control[] rows)
        {
            var defs = new List<string>();
            for (int i = 0; i < rows.Length; i++) { if (i > 0) defs.Add("*"); defs.Add("Auto"); }
            var g = new Grid { RowDefinitions = new RowDefinitions(string.Join(",", defs)) };
            for (int i = 0; i < rows.Length; i++) g.Children.Add(GRow(rows[i], i * 2));
            return new Border { Padding = new Thickness(0, 4, 0, 0), Child = g };
        }

        // Tone for the Curve / Output tabs: the model, the shelves, the character.
        Control ToneCharBody() => Spread(
            ModelRow(),
            SliderRow("LOW", ToneLow, ShelfF, bipolar: true),
            SliderRow("HIGH", ToneHigh, ShelfF, bipolar: true),
            Rule(SliderRow("MIX", Mix, Pct)),
            Box("CHARACTER", () => NotaNum.F($"{ModeName()} · asym {Pct(Sc(S_Asym))}"),
                () => P(Wear) > 0.004f ? NotaNum.F($"wear {Pct(P(Wear))} · {Loudest()}") : "no wear · " + Loudest(), () => On(Character)),
            new StackPanel { Spacing = 3, Children = {
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
            new StackPanel { Spacing = 3, Children = {
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
                g.Children.Add(Caps(k, TextSecondary));
                g.Children.Add(Col(val, 1));
                return g;
            }
            var box = new Border
            {
                Background = NotaPalette.Groove, CornerRadius = NotaRadius.Field, Padding = new Thickness(10, 4),
                // One line: the tab is named Output and THD is in the centre's head row.
                Child = Line("in · out", () => DbF(Sc(S_InPk)).Replace("\u2009dB", "").Replace(" dB", "") + " · " + DbF(Sc(S_OutPk))),
            };
            var sliders = new Grid { RowDefinitions = new RowDefinitions("26,26,26") };
            sliders.Children.Add(SliderRow("MIX", Mix, Pct));
            sliders.Children.Add(GRow(SliderRow("NOISE", Noise, Pct, modulation: true), 1));
            sliders.Children.Add(GRow(SliderRow("GAIN", Output, ShelfF, bipolar: true), 2));
            // Output stage: Tube / Analog, a click on the lit one switches the stage off.
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
            btns.Children.Add(Latch(OutputStage, () => Sel(OutputStage, 3) == 1, () => SetP(OutputStage, Sel(OutputStage, 3) == 1 ? 0f : 0.5f), () => "Tube",
                "Tube stage — a warm, asymmetric valve stage after the saturation (click again for none)", fill: true));
            btns.Children.Add(Col(Latch(OutputStage, () => Sel(OutputStage, 3) == 2, () => SetP(OutputStage, Sel(OutputStage, 3) == 2 ? 0f : 1f), () => "Analog",
                "Analog stage — a console-style soft clip after the saturation (click again for none)", fill: true), 1));
            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(box, Dock.Top),
                Docked(new Border { Margin = new Thickness(0, 4, 0, 0), Child = sliders }, Dock.Top),
                Docked(new StackPanel { Spacing = 0, Children = { Toggle(AutoComp, "Auto-compensation"), Toggle(EvenOnly, "Even harmonics only") } }, Dock.Bottom),
                Docked(new Border { Padding = new Thickness(0, 0, 0, 4), Child = btns }, Dock.Bottom) } };
            return new Border { Padding = new Thickness(0, 4, 0, 0), Child = body };   // 166 of 190
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[2];
        var extras = Mono("", 11, TextSecondary);
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

        var centre = DeviceBodyKit.TabFrame(new[] { "Curve", "Wear", "Output" }, 0, centreHost, CentreBody, t => { centreTab = t; Refresh(); }, extras);
        var rightFrame = DeviceBodyKit.TabFrame(new[] { "Tone", "Output" }, 0, rightHost, RightBody, _ => RefreshAll());
        var right = DeviceBodyKit.Sec(rightFrame, SectionPad, 230);
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = DeviceBodyKit.Sec(centre);
        centreBox.Margin = new Thickness(SectionGap, 0);

        // ---- assemble ---------------------------------------------------------------
        var root = DeviceBodyKit.Frame(new DockPanel { LastChildFill = true, Children = { stateCol, right, centreBox } });

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
    }
}
