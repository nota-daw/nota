// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Auto Filter body (device kind 7), a build of the "Nota Auto
// Filter" mockup (700 × 260) on the Lens / Compressor / Delay frame: a centre panel with
// Filter / Envelope / LFO tabs — each one window, filling the panel, where the cutoff's
// movement is seen (the response, the cutoff over time, the LFO over two bars). The Filter
// tab holds its own controls: Freq, Res and Morph right of the response, the type, slope and
// circuit in a row under it. The right column follows the centre tab — the modulation on
// Filter (target, amounts, smoothing, key), the Envelope or LFO controls on theirs — over the
// output knobs (Drive · Dry/wet · Gain), which stay. Each param has one home; the Envelope and
// LFO graph titles name the modulation target and pick it. Every
// control is a device param, so automation / MIDI learn / presets / A-B / persistence come
// for free; the sidechain source is routing.
// FullBleed — the shared shell draws the header (name · preset · badge · bypass).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Localization;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class AutoFilterDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match AutoFilter.h) ──────────────────────────
    private const int Freq = 0, Res = 1, Type = 2, Slope = 3, Morph = 4, EnvAmt = 5, EnvAtt = 6,
        EnvRel = 7, EnvHold = 8, LfoAmt = 9, LfoRate = 10, LfoWave = 11, LfoMorph = 12,
        Drive = 13, DryWet = 14, EnvOn = 15, LfoOn = 16, Gain = 17, Circuit = 18,
        LfoSync = 19, LfoPhase = 20, ModTarget = 21, EnvHoldTime = 22, ModSmooth = 23,
        LfoOffset = 24, LfoRetrig = 25;
    // Scope layout (AutoFilter::S_* / kHist / kScope).
    private const int S_Cut = 0, S_CutR = 1, S_Res = 2, S_Env = 3, S_Lfo = 4, S_LfoPhase = 5, S_InPk = 6,
        S_OutPk = 7, S_Cpu = 8, S_SampleRate = 9, S_Bpm = 10, S_Onsets = 11, S_EnvHeld = 12, kTele = 16;
    private const int kHist = 2048, kRing = AutoFilterCurve.FftN, kScope = kTele + 2 * kHist + kRing;
    private const double OnsetLin = 0.063;   // −24 dBFS, the engine's onset threshold

    private static readonly string[] TypeNames = { "LP", "BP", "HP", "Notch" };
    private static readonly string[] WaveNames = { "Sine", "Tri", "Saw", "Sqr", "S&H" };
    private static readonly string[] DivNames = { "2/1", "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" };
    private static readonly double[] DivBeats = { 8.0, 4.0, 2.0, 1.0, 0.5, 0.25, 0.125, 0.0625 };
    private static readonly double[] EnvWindows = { 600, 800, 1000, 1500, 2000 };
    private static readonly string[] NoteNames = { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" };

    public double Width => 700;
    // S / L / XL (#531, #540): the size is the device's View param (DeviceView); S is a 260 card,
    // XL the L body stretched in the panel above the chain (or the pop-out window).
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex, xl: true);
    public bool SupportsXl => true;
    public bool FullBleed => true;
    public string? Subtitle => "Filter";

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
        void RefreshAll() { foreach (var a in readouts) a(); }
        // A typeable value for a graph handle (⌥-click it, specs/035).
        ValueField F(int p, string name, Func<double, string> fmt) => ValueEntry.DeviceParam(engine, track, di, p, name, fmt, RefreshAll);
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;
        var envHist = new float[kHist];
        var cutHist = new float[kHist];

        // ---- units ----------------------------------------------------------------
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static double HzOf(double norm) => Exp(norm, 30, 18000);
        static string Hz(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.00} k") : NotaNum.F($"{hz:0} Hz");
        static string HzLong(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.00} kHz") : NotaNum.F($"{hz:0} Hz");
        static string Pct(double v) => NotaNum.F($"{v * 100:0} %");
        static string Bip(double v) => NotaNum.F($"{(v - 0.5) * 200:+0;−0;0} %");
        static string Ms(double ms) => ms >= 100 ? NotaNum.F($"{ms:0} ms") : ms >= 10 ? NotaNum.F($"{ms:0.0} ms") : NotaNum.F($"{ms:0.0#} ms");
        static string GainF(double v) => NotaNum.F($"{(v - 0.5) * 48:+0.0;−0.0;0.0}\u2009dB");
        static double AttMs(double v) => Exp(v, 0.1, 500);
        static double RelMs(double v) => Exp(v, 1, 2000);
        static bool HoldInf(double v) => v >= 0.995;
        static double HoldMs(double v) => 400 * v * v;
        static string HoldF(double v) => HoldInf(v) ? "∞" : Ms(HoldMs(v));
        static double SmoothMs(double v) => 120 * v * v;
        static string SmoothF(double v) => v <= 0.001 ? "off" : Ms(SmoothMs(v));
        static string Deg(double v, double full) => NotaNum.F($"{v * full:0}°");
        string NoteOf(double hz)
        {
            double midi = 69 + 12 * Math.Log2(hz / 440.0);
            int n = (int)Math.Round(midi);
            int cents = (int)Math.Round((midi - n) * 100);
            return NotaNum.F($"{NoteNames[((n % 12) + 12) % 12]}{n / 12 - 1} {cents:+0;−0;0} ¢");
        }

        double Bpm() => Sc(S_Bpm) > 1 ? Sc(S_Bpm) : 120;
        bool Synced() => On(LfoSync);
        double LfoHz() => Synced() ? Bpm() / 60.0 / DivBeats[Sel(LfoRate, 8)] : Exp(P(LfoRate), 0.01, 40);
        string RateF(double v) => Synced() ? DivNames[Math.Clamp((int)Math.Round(v * 7), 0, 7)] : LfoHzF(Exp(v, 0.01, 40));
        static string LfoHzF(double hz) => hz >= 10 ? NotaNum.F($"{hz:0.0} Hz") : NotaNum.F($"{hz:0.00} Hz");
        int Target() => Sel(ModTarget, 3);   // 0 Freq, 1 Reso, 2 Both
        bool ToFreq() => Target() != 1;
        bool ToRes() => Target() != 0;
        double EnvBip() => On(EnvOn) ? (P(EnvAmt) - 0.5) * 2 : 0;
        double LfoDepth() => On(LfoOn) ? P(LfoAmt) : 0;
        bool EnvAssigned() => Math.Abs(EnvBip()) > 0.004;
        bool LfoAssigned() => LfoDepth() > 0.004;
        bool Assigned() => EnvAssigned() || LfoAssigned();
        double BaseHz() => HzOf(P(Freq));
        double LiveHz() => scN > S_Cut ? HzOf(Sc(S_Cut)) : BaseHz();
        // The cutoff range the settings allow (ENV one way, LFO both ways).
        (double Lo, double Hi) RangeOct()
        {
            if (!ToFreq()) return (0, 0);
            double e = EnvBip() * 6, l = LfoDepth() * 4;
            return (Math.Min(0, e) - l, Math.Max(0, e) + l);
        }

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // On/off switch bound to a param (> 0.5 = on).
        Control Toggle(int p, string label, Func<string>? live = null, Func<bool>? dim = null)
        {
            var wrap = Switch(label, () => On(p), () => { SetP(p, On(p) ? 0f : 1f); RefreshAll(); }, out var sync, dim, live);
            readouts.Add(sync);
            Learn(wrap, p);
            return wrap;
        }

        // Segmented pill over a discrete param (n options spread over 0..1).
        Control Seg(int p, string[] names, bool fill = false)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); RefreshAll(); }, out var sync, fill: fill, padX: 6);
            readouts.Add(sync);
            Learn(seg, p);
            return seg;
        }

        // Outlined chips (the mockup's Type / Wave / target row): the chosen one = accent edge + wash.
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
            Learn(row, learnParam);
            return row;
        }

        // A latching chip over a toggle param; its text can follow the value.
        Border Latch(int p, Func<string> text, string tip, bool fill = false)
        {
            var tb = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            void Hi()
            {
                bool on = On(p);
                b.Background = on ? NotaPalette.AccentSubtle : fill ? Raised : Brushes.Transparent;
                b.BorderBrush = on ? Brass : fill ? Brushes.Transparent : NotaPalette.BorderStrong;
                tb.Foreground = on ? AccentBright : fill ? TextPrimary : TextSecondary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                tb.Text = text();
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; SetP(p, On(p) ? 0f : 1f); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }

        // Slider row: caps label · track · mono value.
        Control SliderRow(string label, int p, Func<double, string> fmt, bool bipolar = false, bool modulation = false, Func<bool>? dim = null, double labW = 40, double valW = 34)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => Raw(p, (float)v), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), bipolar: bipolar, dim: dim, valueWidth: valW, modulation: modulation);
            readouts.Add(sync);
            Learn(bar, p);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            return g;
        }

        // Gauge knob over a normalized param (automation gesture, MIDI learn, live follow).
        Control K(int p, string name, Func<double, string> fmt, double cellW = 50, double size = 34, bool bipolar = false)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Bipolar = bipolar, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.Format = fmt;
            knob.ValueChanged += v => { Raw(p, (float)v); val.Text = fmt(P(p)); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            readouts.Add(() => { if (knob.Dragging) return; double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(P(p)); });
            return KnobCell(name, knob, val, cellW);
        }
        // A column of knob cells spread evenly down the height.
        static Control KnobColumn(params Control[] cells)
        {
            var g = new Grid { Width = 50 };
            for (int i = 0; i < cells.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition(GridLength.Star));
                cells[i].VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(GRow(cells[i], i));
            }
            return g;
        }

        // ======================================================================
        // CENTRE — Filter: Freq · Res · Morph right of the response (Morph blends the type
        // toward the next one), the type, slope and circuit in one row under it.
        // ======================================================================
        int centreTab = 0;
        AutoFilterCurve? curve = null;
        // The response graph, a control: drag for the cutoff and the resonance.
        AutoFilterCurve NewCurve()
        {
            var c = new AutoFilterCurve();
            c.GestureBegin += () => { Begin(Freq); Begin(Res); };
            c.GestureEnd += () => { End(Freq); End(Res); };
            c.CutoffChanged += v => Raw(Freq, (float)v);
            c.ResChanged += v => Raw(Res, (float)v);
            ToolTip.SetTip(c, Loc.T("device.auto_filter.drag_sideways_for_the_cutoff"));
            Learn(c, Freq);
            ValueEntry.AttachAll(c, F(Freq, "Cutoff", v => Hz(HzOf(v))), F(Res, "Resonance", Pct));
            return c;
        }
        Control FilterTab()
        {
            curve = NewCurve();

            var right = KnobColumn(K(Freq, "FREQ", v => Hz(HzOf(v))), K(Res, "RES", Pct), K(Morph, "MORPH", Pct));

            var types = Chips(TypeNames, i => Sel(Type, 4) == i, i => SetP(Type, i / 3f), Type, padX: 6);
            var slope = Seg(Slope, new[] { "12", "24" });
            ToolTip.SetTip(slope, Loc.T("device.auto_filter.slope_12_or_24_db"));
            var circuit = Seg(Circuit, new[] { "Clean", "Analog" });
            ToolTip.SetTip(circuit, Loc.T("device.auto_filter.analog_a_gentle_saturation_after"));
            var switches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0),
                Children = { types, slope, circuit } };

            return new DockPanel
            {
                Margin = new Thickness(6, 6, 6, 5),
                Children =
                {
                    Docked(switches, Dock.Bottom),
                    Docked(new Border { Margin = new Thickness(6, 0, 0, 0), Child = right }, Dock.Right),
                    curve,
                },
            };
        }

        // ======================================================================
        // CENTRE — Envelope
        // ======================================================================
        AfEnvView? envView = null;
        double envWindowMs = 600;
        int envEvents = 0;
        double envPeakHz = 0;
        Control EnvelopeTab()
        {
            envView = new AfEnvView();
            envView.StartV = () => P(EnvAmt);
            envView.DragV += v => Raw(EnvAmt, (float)v);
            envView.GestureBegin += () => Begin(EnvAmt);
            envView.GestureEnd += () => End(EnvAmt);
            ToolTip.SetTip(envView, Loc.T("device.auto_filter.the_inputs_envelope_teal_and"));
            Learn(envView, EnvAmt);
            ValueEntry.AttachAll(envView, F(EnvAmt, "Env amount", Bip));
            envView.TitleClicked += _ => PickTarget(envView);
            return new Border { Margin = new Thickness(8, 6), Child = envView };
        }
        void SyncEnvView()
        {
            double span = AttMs(P(EnvAtt)) + RelMs(P(EnvRel)) + (On(EnvHold) && !HoldInf(P(EnvHoldTime)) ? HoldMs(P(EnvHoldTime)) : 0);
            envWindowMs = EnvWindows[^1];
            foreach (var wv in EnvWindows) if (wv >= span * 2.5) { envWindowMs = wv; break; }
            int n = Math.Clamp((int)envWindowMs, 2, kHist);
            if (scN >= kTele + 2 * kHist)
            {
                Array.Copy(scope, kTele + kHist - n, envHist, 0, n);
                Array.Copy(scope, kTele + 2 * kHist - n, cutHist, 0, n);
            }
            else { Array.Clear(envHist); Array.Clear(cutHist); }
            envEvents = 0; double peak = 0; bool armed = true;
            for (int i = 0; i < n; i++)
            {
                if (armed && envHist[i] >= OnsetLin) { envEvents++; armed = false; }
                else if (!armed && envHist[i] < OnsetLin / 2) armed = true;
                peak = Math.Max(peak, cutHist[i]);
            }
            envPeakHz = scN >= kTele + 2 * kHist ? HzOf(peak) : BaseHz();
            if (envView is not null) envView.Title = "Envelope → " + TargetName();
            envView?.Set(envHist, cutHist, n, P(Freq), NotaNum.F($"base {Hz(BaseHz())}"), envWindowMs, envPeakHz);
        }

        // ======================================================================
        // CENTRE — LFO
        // ======================================================================
        AfLfoView? lfoView = null;
        double LfoCycles() => Synced() ? 8.0 / DivBeats[Sel(LfoRate, 8)] : Math.Clamp(LfoHz() * 2, 1, 16);
        Control LfoTab()
        {
            lfoView = new AfLfoView();
            lfoView.StartV = () => P(LfoAmt);
            lfoView.StartH = () => P(LfoOffset);
            lfoView.DragV += v => Raw(LfoAmt, (float)v);
            lfoView.DragH += v => Raw(LfoOffset, (float)v);
            lfoView.GestureBegin += () => { Begin(LfoAmt); Begin(LfoOffset); };
            lfoView.GestureEnd += () => { End(LfoAmt); End(LfoOffset); };
            ToolTip.SetTip(lfoView, Loc.T("device.auto_filter.the_lfos_movement_over_the"));
            Learn(lfoView, LfoAmt);
            ValueEntry.AttachAll(lfoView, F(LfoAmt, "LFO amount", Pct), F(LfoOffset, "LFO phase", v => Deg(v, 360)));
            lfoView.TitleClicked += _ => PickTarget(lfoView);
            return new Border { Margin = new Thickness(8, 6), Child = lfoView };
        }
        void SyncLfoView()
        {
            if (lfoView is null) return;
            double cycles = LfoCycles();
            double hz = LfoHz();
            double smoothCycles = SmoothMs(P(ModSmooth)) / 1000.0 * hz;
            string mid, end;
            if (Synced()) { mid = "2"; end = "bar"; }
            else { double sec = cycles / hz; mid = NotaNum.Time(sec / 2); end = NotaNum.Time(sec); }
            string title = "LFO → " + TargetName();
            double phase = scN > S_LfoPhase && On(LfoOn) && Sc(S_Cpu) > 0 ? Sc(S_LfoPhase) : double.NaN;
            lfoView.Set(On(LfoOn), Sel(LfoWave, 5), P(LfoMorph), P(LfoAmt), P(LfoOffset), P(LfoPhase) * 0.5, smoothCycles, cycles, phase,
                title, NotaNum.F($"±{Pct(P(LfoAmt))}"), P(LfoMorph) > 0.004 ? NotaNum.F($"morph {Pct(P(LfoMorph))}") : "", mid, end);
        }

        // ======================================================================
        // RIGHT — the controls of the centre tab (Filter · Envelope · LFO)
        // ======================================================================
        // Sidechain key: the switch picks a source (a flyout with the tracks — and the pads / chains
        // / voices of a track's instrument, indented under it, specs/050 — and the key gain).
        SidechainKey Key() => engine.DeviceSidechainKey(track, di);
        string KeyName() => SidechainSourcePicker.Name(engine, Key());
        Control SidechainSwitch()
        {
            Border? host = null;
            void ShowPicker()
            {
                var list = new StackPanel { Spacing = 1 };
                var fly = new Flyout();
                void Item(string name, SidechainKey k, bool indent = false, bool enabled = true, string? tip = null)
                {
                    bool cur = Key() == k;
                    var tb = new TextBlock { Text = name, FontSize = 10, Foreground = !enabled ? TextDisabled : cur ? AccentBright : TextPrimary, FontWeight = cur ? FontWeight.SemiBold : FontWeight.Normal };
                    var b = new Border { Padding = new Thickness(indent ? 20 : 8, 3, 8, 3), CornerRadius = NotaRadius.Cell, Background = cur ? NotaPalette.AccentSubtle : Brushes.Transparent, Cursor = enabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default, Child = tb };
                    if (tip != null) ToolTip.SetTip(b, tip);
                    if (enabled)
                        b.PointerPressed += (_, e) => { SidechainSourcePicker.SetDeviceKey(engine, track, di, k); ctx.NotifyChanged(); RefreshAll(); fly.Hide(); e.Handled = true; };
                    list.Children.Add(b);
                }
                Item(Loc.T("device.auto_filter.none_this_tracks_input"), SidechainKey.None);
                foreach (var en in SidechainSourcePicker.Entries(engine, track, Key()))
                    Item(en.Label, en.Key, en.IsPart, en.Enabled, en.Tip);
                var gain = DeviceCardKit.SliderRow("KEY GAIN", () => (engine.DeviceSidechainGain(track, di) + 24) / 48.0,
                    v => engine.SetDeviceSidechainGain(track, di, (float)(v * 48 - 24)),
                    () => NotaNum.F($"{engine.DeviceSidechainGain(track, di):+0.0;−0.0;0.0} dB"), out _, bipolar: true,
                    reset: () => engine.SetDeviceSidechainGain(track, di, 0), valueWidth: 46);
                fly.Content = new StackPanel { Width = 200, Spacing = 6, Children = { Caps(Loc.T("device.auto_filter.sidechain_key_envelope_source_caps"), null, 8), list, new Border { Height = 1, Background = BorderDef }, gain } };
                fly.ShowAt(host!);
            }
            var sw = Switch("", () => engine.DeviceSidechainSource(track, di) >= 0, () =>
            {
                if (engine.DeviceSidechainSource(track, di) >= 0) { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); RefreshAll(); }
                else ShowPicker();
            }, out var sync, liveLabel: () =>
            {
                return engine.DeviceSidechainSource(track, di) >= 0 ? "Key: " + KeyName() : "Sidechain key";
            });
            host = sw;
            readouts.Add(sync);
            // a right-click (or a click on the name while on) re-opens the picker
            sw.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Right) { ShowPicker(); e.Handled = true; } };
            ToolTip.SetTip(sw, Loc.T("device.auto_filter.sidechain_key_the_envelope_follows"));
            return sw;
        }

        // Rows spread evenly down the L panel; a 1px rule keeps its height. Stretched (XL, the
        // pop-out window) the panel is tall: there each row is at most 26, together at the top.
        bool tall = ctx.Popout || DeviceView.Size(engine, track, di) == CardSize.XL;
        Control Rows(params Control[] rows)
        {
            var grid = new Grid { RowSpacing = 2, VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Stretch };
            for (int r = 0; r < rows.Length; r++)
            {
                grid.RowDefinitions.Add(rows[r] is Border { Height: 1 } ? new RowDefinition(GridLength.Auto)
                    : tall ? new RowDefinition(new GridLength(26)) : new RowDefinition(GridLength.Star));
                grid.Children.Add(GRow(rows[r], r));
            }
            return new Border { Padding = new Thickness(8, 4, 8, 5), Child = grid };
        }
        static Border Rule() => new() { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 1) };
        // Two controls side by side, each half the panel.
        static Control Pair(Control a, Control b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            a.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalAlignment = HorizontalAlignment.Stretch;
            g.Children.Add(a); g.Children.Add(Col(b, 1));
            return g;
        }

        // The panel's head: the bar over it (the height of the centre's tab bar) holds its
        // on-switch on the left and its mode on the right.
        static Control Head(Control left, Control? right = null)
        {
            var d = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Center };
            if (right != null) d.Children.Add(Docked(right, Dock.Right));
            d.Children.Add(Docked(left, Dock.Left));
            return d;
        }

        // The modulation target (Freq · Reso · Freq + Reso), shared by the envelope and the LFO;
        // the Envelope and LFO graphs name it in their title, a click there picks it.
        string TargetName() => Target() switch { 0 => "Freq", 1 => "Reso", _ => "Freq + Reso" };
        void PickTarget(Control anchor)
        {
            var fly = new Flyout();
            var list = new StackPanel { Spacing = 1 };
            string[] names = { "Freq", "Reso", "Freq + Reso" };
            for (int i = 0; i < names.Length; i++)
            {
                int iv = i;
                bool cur = Target() == i;
                var tb = new TextBlock { Text = names[i], FontSize = 10, Foreground = cur ? AccentBright : TextPrimary, FontWeight = cur ? FontWeight.SemiBold : FontWeight.Normal };
                var b = new Border { Padding = new Thickness(8, 3), CornerRadius = NotaRadius.Cell, Background = cur ? NotaPalette.AccentSubtle : Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerPressed += (_, e) => { SetP(ModTarget, iv / 2f); fly.Hide(); Refresh(); e.Handled = true; };
                list.Children.Add(b);
            }
            fly.Content = new StackPanel { Width = 120, Spacing = 6, Children = { Caps(Loc.T("device.auto_filter.modulation_target_caps"), null, 8), list } };
            fly.ShowAt(anchor, true);
        }

        // Filter tab: what moves the filter, in one place — the target, the two sources'
        // amounts, the smoothing on their sum and the envelope's key.
        (Control, Control) ModPanel()
        {
            var title = new TextBlock { Text = Loc.T("device.auto_filter.modulation"), FontSize = 9, FontWeight = FontWeight.Medium, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
            var target = Chips(new[] { "Freq", "Reso", "Both" }, i => Target() == i, i => SetP(ModTarget, i / 2f), ModTarget, h: 15, padX: 6);
            var tRow = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*") };
            tRow.Children.Add(Caps("TARGET")); tRow.Children.Add(Col(target, 1));
            var smooth = SliderRow("SMOOTH", ModSmooth, SmoothF);
            ToolTip.SetTip(smooth, Loc.T("device.auto_filter.smooth_a_slew_on_the"));
            return (Head(title), Rows(
                tRow,
                SliderRow("ENV", EnvAmt, Bip, bipolar: true, dim: () => !On(EnvOn)),
                SliderRow("LFO", LfoAmt, Pct, modulation: true, dim: () => !On(LfoOn)),
                smooth,
                Rule(),
                SidechainSwitch()));
        }

        (Control, Control) EnvelopePanel()
        {
            // Up / Down: the sign of the bipolar amount.
            var dir = Segments(new[] { "Up", "Down" }, () => P(EnvAmt) >= 0.5f ? 0 : 1, iv =>
            {
                double mag = Math.Abs(P(EnvAmt) - 0.5);
                if (mag < 0.004) mag = 0.19;
                SetP(EnvAmt, (float)(iv == 0 ? 0.5 + mag : 0.5 - mag));
                RefreshAll();
            }, out var dirSync, padX: 6);
            readouts.Add(dirSync);
            Learn(dir, EnvAmt);
            var envSw = Toggle(EnvOn, "Envelope", live: () => engine.DeviceSidechainSource(track, di) >= 0 ? "Envelope on key" : "Envelope");
            ToolTip.SetTip(envSw, Loc.T("device.auto_filter.envelope_follower_on_off"));
            var hold = Latch(EnvHold, () => Loc.T("device.auto_filter.hold"), Loc.T("device.auto_filter.hold_keep_the_envelope_at"), fill: true);
            var holdRow = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*"), ColumnSpacing = 0, VerticalAlignment = VerticalAlignment.Center };
            var holdTime = DeviceCardKit.SliderRow("", () => P(EnvHoldTime), v => Raw(EnvHoldTime, (float)v), () => HoldF(P(EnvHoldTime)), out var holdSync,
                begin: () => Begin(EnvHoldTime), end: () => End(EnvHoldTime), reset: () => Reset(EnvHoldTime), dim: () => !On(EnvHold), valueWidth: 34);
            readouts.Add(holdSync);
            Learn(holdTime, EnvHoldTime);
            hold.Margin = new Thickness(0, 0, 6, 0);
            holdRow.Children.Add(hold);
            holdRow.Children.Add(Col(holdTime, 1));
            return (Head(envSw, dir), Rows(
                SliderRow("AMOUNT", EnvAmt, Bip, bipolar: true, dim: () => !On(EnvOn)),
                SliderRow("ATTACK", EnvAtt, v => Ms(AttMs(v))),
                SliderRow("RELEASE", EnvRel, v => Ms(RelMs(v))),
                holdRow,
                Rule(),
                SidechainSwitch()));
        }

        (Control, Control) LfoPanel()
        {
            var lfoSw = Toggle(LfoOn, "LFO");
            ToolTip.SetTip(lfoSw, Loc.T("device.auto_filter.lfo_on_off"));
            var sync = Seg(LfoSync, new[] { "Free", "Sync" });
            var waves = Chips(WaveNames, i => Sel(LfoWave, 5) == i, i => SetP(LfoWave, i / 4f), LfoWave, padX: 4);
            // Stereo phase: on sets 90°, a right-click on it steps 45° → 180°.
            var stereo = Switch("", () => P(LfoPhase) > 0.001f, () => SetP(LfoPhase, P(LfoPhase) > 0.001f ? 0f : 0.5f), out var stSync,
                liveLabel: () => P(LfoPhase) > 0.001f ? NotaNum.F($"Stereo {P(LfoPhase) * 180:0}°") : Loc.T("device.auto_filter.stereo"));
            readouts.Add(stSync);
            Learn(stereo, LfoPhase);
            stereo.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Right || P(LfoPhase) <= 0.001f) return;
                float nx = P(LfoPhase) + 0.25f; if (nx > 1.001f) nx = 0.25f;
                SetP(LfoPhase, nx); RefreshAll(); e.Handled = true;
            };
            ToolTip.SetTip(stereo, Loc.T("device.auto_filter.stereo_phase_the_right_channels"));
            var retrig = Toggle(LfoRetrig, "Retrig");
            ToolTip.SetTip(retrig, Loc.T("device.auto_filter.retrig_restart_the_lfo_on"));
            return (Head(lfoSw, sync), Rows(
                waves,
                SliderRow("RATE", LfoRate, RateF),
                SliderRow("AMOUNT", LfoAmt, Pct, modulation: true, dim: () => !On(LfoOn)),
                SliderRow("MORPH", LfoMorph, Pct),
                SliderRow("PHASE", LfoOffset, v => Deg(v, 360)),
                Pair(retrig, stereo)));
        }

        // Under every panel: the output — Drive · Dry/wet (the main knob) · Gain.
        Control OutputStrip()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(4, 5, 4, 4) };
            Control Cell(Control c, int col) { c.HorizontalAlignment = HorizontalAlignment.Center; c.VerticalAlignment = VerticalAlignment.Bottom; return Col(c, col); }
            g.Children.Add(Cell(K(Drive, "DRIVE", v => v <= 0.001 ? "off" : Pct(v), 54), 0));
            g.Children.Add(Cell(K(DryWet, "DRY/WET", Pct, 58, size: 44), 1));
            g.Children.Add(Cell(K(Gain, "GAIN", GainF, 54, bipolar: true), 2));
            return new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(8, 0), Child = g };
        }

        // ======================================================================
        // S: the response over Freq · Res · Drive · Dry/wet
        // ======================================================================
        Control MiniBody()
        {
            curve = NewCurve();
            var knobs = new UniformGrid { Rows = 1, Margin = new Thickness(0, 6, 0, 0), Children = {
                K(Freq, "FREQ", v => Hz(HzOf(v)), 56), K(Res, "RES", Pct, 56),
                K(Drive, "DRIVE", v => v <= 0.001 ? "off" : Pct(v), 56), K(DryWet, "DRY/WET", Pct, 56) } };
            var body = new DockPanel { Margin = new Thickness(6), Children = { Docked(knobs, Dock.Bottom), curve } };
            return new Border { Background = NotaPalette.SurfaceCard, Child = new RuleSection { Margin = new Thickness(5), Child = body } };
        }

        Control root = CardLayout.IsMini(ctx, di) ? MiniBody() : LargeBody();

        // ======================================================================
        // L: tab frame + the panel that follows it
        // ======================================================================
        Control LargeBody()
        {
            var centreHost = new ContentControl();
            var centreBodies = new Control?[3];
            var panelHost = new ContentControl();
            var headHost = new ContentControl();
            var panels = new (Control Head, Control Body)?[3];
            var extras = Mono("", 7, TextTertiary);
            readouts.Add(() =>
            {
                string Range()
                {
                    if (!Assigned() || !ToFreq()) return Loc.T("device.auto_filter.no_modulation");
                    var (lo, hi) = RangeOct();
                    return NotaNum.F($"range {Hz(Math.Min(18000, BaseHz() * Math.Pow(2, lo)))}…{Hz(Math.Min(18000, BaseHz() * Math.Pow(2, hi)))}");
                }
                // The LFO's reach when it drives the resonance (Mod → Reso / Both).
                string LfoRange()
                {
                    if (!Assigned()) return Loc.T("device.auto_filter.no_modulation");
                    var parts = new List<string>();
                    if (ToFreq()) parts.Add(Range());
                    if (ToRes() && LfoAssigned()) parts.Add(NotaNum.F($"reso {Pct(P(Res))} ±{Pct(LfoDepth() * 0.5)}"));
                    return parts.Count > 0 ? string.Join(" · ", parts) : Loc.T("device.auto_filter.no_modulation");
                }
                string EnvReach()
                {
                    var parts = new List<string>();
                    if (ToFreq()) parts.Add(NotaNum.F($"cutoff {Hz(BaseHz())} → {Hz(envPeakHz)}"));
                    if (ToRes()) parts.Add(NotaNum.F($"reso {Pct(P(Res))} {Bip(P(EnvAmt))}"));
                    return string.Join(" · ", parts);
                }
                extras.Text = centreTab switch
                {
                    1 => NotaNum.F($"{envEvents} {(envEvents == 1 ? "event" : "events")} · threshold −24 dB · {EnvReach()} · window {(envWindowMs >= 1000 ? NotaNum.F($"{envWindowMs / 1000:0.#} s") : NotaNum.F($"{envWindowMs:0} ms"))}"),
                    2 => Synced() ? NotaNum.F($"{DivNames[Sel(LfoRate, 8)]} at {Bpm():0} BPM = {LfoHzF(LfoHz())} · {LfoRange()}")
                                  : NotaNum.F($"free · period {Ms(1000 / LfoHz())} · {LfoRange()}"),
                    _ => NotaNum.F($"cutoff {HzLong(LiveHz())} · {NoteOf(LiveHz())} · {Range()}"),
                };
            });
            Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => EnvelopeTab(), 2 => LfoTab(), _ => FilterTab() };
            // The right column follows the centre tab — Filter: the modulation, Envelope / LFO:
            // its controls — over the output, which stays.
            void ShowPanel(int t)
            {
                var (head, body) = panels[t] ??= t switch { 1 => EnvelopePanel(), 2 => LfoPanel(), _ => ModPanel() };
                headHost.Content = head;
                panelHost.Content = body;
                RefreshAll();
            }

            var centre = TabFrame(new[] { "Filter", "Envelope", "LFO" }, centreHost, CentreBody, false, t => { centreTab = t; ShowPanel(t); Refresh(); }, extras);
            ShowPanel(centreTab);
            var headBar = new Border { Height = 20, Padding = new Thickness(8, 0), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = headHost };
            var rightCol = new DockPanel { LastChildFill = true, Children = { Docked(headBar, Dock.Top), Docked(OutputStrip(), Dock.Bottom), panelHost } };
            var right = new RuleSection { Width = 186, Child = rightCol };
            DockPanel.SetDock(right, Dock.Right);
            var centreBox = new RuleSection { Margin = new Thickness(0, 0, 5, 0), Child = centre };

            // ---- assemble ---------------------------------------------------------------
            var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { right, centreBox } };
            return new Border { Background = NotaPalette.SurfaceCard, Child = bodyRow };
        }

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            if (centreTab == 0 && curve is not null)
            {
                if (!curve.Dragging) curve.Set(P(Freq), P(Res), Sel(Type, 4), P(Morph), On(Slope));
                var (lo, hi) = RangeOct();
                double b = P(Freq), oct = Math.Log2(18000.0 / 30.0);
                curve.SetLive(scN > S_Cut ? Sc(S_Cut) : b, scN > S_Res ? Sc(S_Res) : P(Res), b + lo / oct, b + hi / oct, Assigned() && ToFreq());
                curve.FeedSpectrum(scope, kTele + 2 * kHist, scN - (kTele + 2 * kHist), Sc(S_SampleRate));
            }
            if (centreTab == 1) SyncEnvView();
            if (centreTab == 2) SyncLfoView();
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
                    tb.Foreground = on ? NotaPalette.TextPrimary : TextTertiary;
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
