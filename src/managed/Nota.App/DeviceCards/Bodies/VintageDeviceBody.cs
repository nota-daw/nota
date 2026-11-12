// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Vintage body (device kind 8), a build of the "Nota
// Vintage" mockup (700 × 260) on the Auto Filter / Lens / Compressor frame: a centre panel
// with Curve / Wear / Output tabs, laid out the #531 way (CardLayout) — the transfer curve the
// engine runs (Drive and Tone beside it, the character and OS under it), the pitch drift over the
// last four seconds (Wow, Flutter and Crackle beside it), the harmonics against the hiss floor
// (Even only under it) — and a right panel with no tabs of its own that follows them: Character
// (on, the tone model, the shelves, the character box), Wear (sync, the rates, Wear, follow,
// stereo drift), Output (auto-comp, the measurements box, noise, hiss HP, Tube / Analog) — over
// Dry/wet · Output. S is the curve over the character and Drive, Tone, Wow, Dry/wet. Every control is a device param,
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
using Nota.Application.Localization;

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
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex);
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
        // A typeable value for a fader or a graph (⌥-click it, specs/035).
        ValueField F(int p, string name, Func<double, string> fmt) => ValueEntry.DeviceParam(engine, track, di, p, name, fmt, RefreshAll);
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
        string OsText() => Sel(OS, 4) == 0 ? Loc.T("device.vintage.no_oversampling") : NotaNum.F($"{OsNames[Sel(OS, 4)]} OS");
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
        string Loudest() { int best = 2; for (int k = 3; k <= 7; k++) if (H(k) > H(best)) best = k; return H(best) > -100 ? Ordinal(best) + " harmonic" : Loc.T("device.vintage.no_harmonics"); }

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        // An acronym NotaText.Label does not know (OS, GR) keeps its case: the label as written.
        static TextBlock Acr(string t, IBrush? c = null) { var tb = Caps(t, c); tb.Text = t; return tb; }
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Measure"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, double cellW = 46, double size = 34, bool bipolar = false)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Bipolar = bipolar, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
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

        // Outlined chips: the chosen one = accent edge + wash.
        Control Chips(string[] names, Func<int, bool> lit, Action<int> pick, int learnParam, double h = 16, double padX = 7)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var cells = new Border[names.Length];
            var texts = new TextBlock[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int iv = i;
                var tb = new TextBlock { Text = names[i], FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var b = new Border { Height = h, Padding = new Thickness(padX, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
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
            var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
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
            var name = new TextBlock { FontSize = 8, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            var box = new Border
            {
                Height = 16, MinWidth = 74, Background = Sunken, BorderBrush = NotaPalette.BorderStrong, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell,
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(new Glyph(GlyphKind.ChevronDown, 7) { Foreground = TextTertiary, Margin = new Thickness(6, 0, 0, 0) }, Dock.Right), name } },
            };
            readouts.Add(() => name.Text = ModeName());
            ToolTip.SetTip(box, Loc.T("device.vintage.character_the_era_the_signal"));
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

        int centreTab = 0;
        Control OsSeg() { var s = Seg(OS, OsNames); ToolTip.SetTip(s, Loc.T("device.vintage.no_oversampling")); return s; }

        // ======================================================================
        // CENTRE — Curve (drag for Drive; Drive · Tone beside it, the character and OS under it)
        // ======================================================================
        VtCurveView? curve = null;
        VtCurveView NewCurve()
        {
            curve = new VtCurveView();
            curve.StartV = () => P(Drive);
            curve.DragV += v => Raw(Drive, (float)v);
            curve.GestureBegin += () => Begin(Drive);
            curve.GestureEnd += () => End(Drive);
            ToolTip.SetTip(curve, MenuKit.Keys(Loc.T("device.vintage.the_transfer_curve_the_saturation")));
            ValueEntry.AttachAll(curve, F(Drive, "Drive", Pct));
            Learn(curve, Drive);
            return curve;
        }
        Control CurveTab() => CardLayout.View(NewCurve(),
            CardLayout.KnobColumn(K(Drive, "DRIVE", Pct, cellW: 50), K(Tone, "TONE", ToneF, cellW: 50)),
            CardLayout.Line(6, CharDrop(), Acr("OS"), OsSeg()));

        // ======================================================================
        // CENTRE — Wear (the pitch drift: Wow · Flutter · Crackle beside it, what it shows under it)
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
            ToolTip.SetTip(wearView, MenuKit.Keys(Loc.T("device.vintage.the_pitch_drift_over_the")));
            ValueEntry.AttachAll(wearView,
                new ValueField("Depth", () => P(wearShow == 1 ? Flutter : Wow), Pct, v => Raw(wearShow == 1 ? Flutter : Wow, (float)v),
                    () => Begin(wearShow == 1 ? Flutter : Wow), () => End(wearShow == 1 ? Flutter : Wow)) { After = RefreshAll },
                new ValueField("Rate", () => P(wearShow == 1 ? FlutterRate : WowRate), v => wearShow == 1 ? FlutRateF(v) : WowRateF(v),
                    v => Raw(wearShow == 1 ? FlutterRate : WowRate, (float)v),
                    () => Begin(wearShow == 1 ? FlutterRate : WowRate), () => End(wearShow == 1 ? FlutterRate : WowRate)) { After = RefreshAll });
            Learn(wearView, Wow);
            var show = Chips(new[] { "Wow", "Flutter", "Both" }, i => wearShow == i, i => wearShow = i, -1);
            ToolTip.SetTip(show, Loc.T("device.vintage.what_the_window_shows_and"));
            return CardLayout.View(wearView,
                CardLayout.KnobColumn(K(Wow, "WOW", Pct, cellW: 50), K(Flutter, "FLUTTER", Pct, Teal, 50), K(Crackle, "CRACKLE", Pct, cellW: 50)),
                show);
        }
        void SyncWearView()
        {
            if (wearView is null) return;
            int n = scN >= kScope ? (int)Math.Min(kHist, Sc(S_HistLen)) : 0;
            double secs = kHist * (Sc(S_HistMs) > 0 ? Sc(S_HistMs) : 4) / 1000.0;
            string peak = wearShow switch { 0 => "+" + CentsF(Sc(S_WowCents))[1..], 1 => "+" + CentsF(Sc(S_FlutCents))[1..], _ => "+" + CentsF(Sc(S_WowCents) + Sc(S_FlutCents))[1..] };
            wearView.Set(scope, kTele + kCurve, kTele + kCurve + kHist, n, wearShow, peak,
                NotaNum.F($"wow {HzF(WowHz())}"), NotaNum.F($"{secs:0} s"));
        }

        // ======================================================================
        // CENTRE — Output (the harmonics; drag for Drive, Even only under it)
        // ======================================================================
        VtHarmView? harm = null;
        Control OutputTab()
        {
            harm = new VtHarmView();
            harm.StartV = () => P(Drive);
            harm.DragV += v => Raw(Drive, (float)v);
            harm.GestureBegin += () => Begin(Drive);
            harm.GestureEnd += () => End(Drive);
            ToolTip.SetTip(harm, MenuKit.Keys(Loc.T("device.vintage.the_harmonics_the_saturation_adds")));
            ValueEntry.AttachAll(harm, F(Drive, "Drive", Pct));
            Learn(harm, Drive);
            return CardLayout.View(harm, null, ToggleLatch(EvenOnly, () => Loc.T("device.vintage.even_only"), Loc.T("device.vintage.even_harmonics_only")));
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
        // RIGHT — follows the centre tab: Character (Curve) · Wear (Wear) · Output (Output)
        // ======================================================================
        Control Box(string title, Func<string> line1, Func<string> line2)
        {
            var t = Caps(title);
            var l1 = Mono("", 9, TextPrimary);
            var l2 = Mono("", 8, TextSecondary);
            l1.TextTrimming = l2.TextTrimming = TextTrimming.CharacterEllipsis;
            var b = CardLayout.ReadoutBox(new StackPanel { Spacing = 2, Children = { t, l1, l2 } });
            readouts.Add(() => { l1.Text = line1(); l2.Text = line2(); });
            return b;
        }
        (Control, Control) CharacterPanel()
        {
            var chips = Chips(new[] { "Warm", "Flat", "Dark" }, i => Sel(ToneModel, 3) == i, i => SetP(ToneModel, i / 2f), ToneModel, h: 15, padX: 6);
            ToolTip.SetTip(chips, Loc.T("device.vintage.tone_model_warm_the_eras"));
            return (CardLayout.Head(Toggle(Character, "Character", () => On(Character) ? "Character: " + ModeName() : "Character off")),
                CardLayout.Rows(
                    CardLayout.LabelRow("MODEL", chips),
                    SliderRow("LOW", ToneLow, ShelfF, bipolar: true),
                    SliderRow("HIGH", ToneHigh, ShelfF, bipolar: true),
                    CardLayout.Rule(),
                    Box("CHARACTER", () => NotaNum.F($"{ModeName()} · asym {Pct(Sc(S_Asym))}"),
                        () => P(Wear) > 0.004f ? NotaNum.F($"wear {Pct(P(Wear))} · {Loudest()}") : "no wear · " + Loudest())));
        }
        (Control, Control) WearPanel()
        {
            var sync = Latch(WowSync, () => On(WowSync), () => SetP(WowSync, On(WowSync) ? 0f : 1f),
                () => Synced() ? "Sync " + SyncNames[Sel(WowRate, 6)] : "Sync off",
                Loc.T("device.vintage.sync_the_wow_follows_the"));
            return (CardLayout.Head(CardLayout.Title(Loc.T("device.vintage.wear")), sync), CardLayout.Rows(
                SliderRow("RATE", WowRate, WowRateF),
                SliderRow("F·RATE", FlutterRate, FlutRateF, modulation: true),
                SliderRow("WEAR", Wear, Pct),
                CardLayout.Rule(),
                Toggle(WearFollow, Loc.T("device.vintage.wear_follows_input")),
                Toggle(StereoDrift, "Stereo drift")));
        }
        (Control, Control) OutputPanel()
        {
            Control Line(string k, Func<string> v)
            {
                var val = Mono("", 8, TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => val.Text = v());
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(new TextBlock { Text = k, FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center });
                g.Children.Add(Col(val, 1));
                return g;
            }
            var box = CardLayout.ReadoutBox(new StackPanel { Spacing = 2, Children = {
                Caps("OUTPUT"),
                Line("in", () => DbF(Sc(S_InPk))),
                Line("out", () => DbF(Sc(S_OutPk))),
                Line("THD", () => ThdF(Sc(S_Thd))) } });
            // Output stage: Tube / Analog, a click on the lit one switches the stage off.
            var btns = CardLayout.Pair(
                Latch(OutputStage, () => Sel(OutputStage, 3) == 1, () => SetP(OutputStage, Sel(OutputStage, 3) == 1 ? 0f : 0.5f), () => "Tube",
                    Loc.T("device.vintage.tube_stage_a_warm_asymmetric"), fill: true),
                Latch(OutputStage, () => Sel(OutputStage, 3) == 2, () => SetP(OutputStage, Sel(OutputStage, 3) == 2 ? 0f : 1f), () => "Analog",
                    Loc.T("device.vintage.analog_stage_a_console_style"), fill: true));
            var comp = Toggle(AutoComp, "Auto-compensation",
                    () => On(AutoComp) && Sc(S_CompDb) != 0 ? NotaNum.F($"Auto-comp {Sc(S_CompDb):+0.0;−0.0;0.0} dB") : "Auto-compensation");
            ToolTip.SetTip(comp, Loc.T("device.vintage.auto_comp_the_output_level"));
            return (CardLayout.Head(comp),
                CardLayout.Rows(box, SliderRow("NOISE", Noise, Pct, modulation: true), SliderRow("HISS HP", HissHp, HissHpF, modulation: true), btns));
        }

        // ======================================================================
        // Frames: L (tabs, the panel that follows them, Dry/wet · Output under it) · S (the curve
        // over the character and Drive · Tone · Wow · Dry/wet)
        // ======================================================================
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            string m = ModeName().ToLowerInvariant();
            extras.Text = centreTab switch
            {
                1 => NotaNum.F($"{CentsF(Sc(8) + Sc(9))} · wow {HzF(WowHz())} · flut {HzF(FlutHz())} · ")
                     + (Sc(S_BandHz) > 0 ? "band " + HzF(Sc(S_BandHz)) : "band open") + (Sc(S_HissDb) > -119 ? NotaNum.F($" · hiss {Sc(S_HissDb):0} dB") : " · no hiss"),
                2 => H(2) > -100 || H(3) > -100 ? NotaNum.F($"2nd {H(2):0} dB · 3rd {H(3):0} dB · {Dominant()} · {DbfsF(Sc(S_OutPk))}") : "no harmonics — clean",
                _ => NotaNum.F($"{m} · asym {Pct(Sc(S_Asym))} · {Dominant()} · {OsText()} · peak {DbF(Sc(S_InPk))} → {DbF(Sc(S_OutPk))}"),
            };
        });
        Control root = CardLayout.IsMini(ctx, di)
            ? CardLayout.Mini(NewCurve(), CharDrop(),
                K(Drive, "DRIVE", Pct, cellW: 56), K(Tone, "TONE", ToneF, cellW: 56), K(Wow, "WOW", Pct, cellW: 56), K(Mix, "DRY/WET", Pct, cellW: 56))
            : CardLayout.Large(new[] { "Curve", "Wear", "Output" }, 0,
                t => t switch { 1 => WearTab(), 2 => OutputTab(), _ => CurveTab() },
                t => t switch { 1 => WearPanel(), 2 => OutputPanel(), _ => CharacterPanel() },
                extras,
                CardLayout.Output(null, K(Mix, "DRY/WET", Pct, cellW: 58, size: 44), K(Output, "OUTPUT", GainF, cellW: 54, bipolar: true)),
                t => { centreTab = t; Refresh(); });

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
