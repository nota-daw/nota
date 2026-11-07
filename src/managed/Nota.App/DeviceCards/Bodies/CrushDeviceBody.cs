// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Crush body (bit crusher, device kind 12), a build of the
// "Nota Crush" mockup (700 × 260) on the Ceiling / Beat Repeat frame: a centre panel with
// Quantiser / Spectrum / Transfer tabs — the held and quantised steps over a sine (drag: Bits
// up / down, Rate left / right), the output spectrum from the engine's FFT with what the crush
// added and the images above the reduced Nyquist (drag the Nyquist and the filter lines), the
// transfer curve with the form it makes (drag: Drive) — laid out the #531 way (CardLayout): Bits
// and Rate beside the quantiser with Anti-alias under it, Drive beside the transfer with the mode
// under it; a right panel with no tabs of its own that follows them — Grit (Dither, Jitter, Noise,
// the result box) on Quantiser, Output (Auto gain, the measurements box, DC filter, the images and
// THD+N) on the others — over Filter · Dry/wet · Gain. S is the quantiser over the mode and Bits,
// Rate, Drive, Dry/wet. The
// spectrum comes from the engine (Crush.h scopeRead). Every control is a device param
// (normalized 0..1), so automation / MIDI learn / presets / A-B / persistence come for free.
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

internal sealed class CrushDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Crush.h) ───────────────────────────────
    private const int Bits = 0, Rate = 1, Mode = 2, Dither = 3, Jitter = 4, NoiseFloor = 5, PostFilter = 6, DryWet = 7,
        AntiAlias = 8, Output = 9, Drive = 10, AutoGain = 11, DcFilter = 12, kParams = 13;
    // Scope layout (Crush::S_* / kTele / kBands).
    private const int S_InPeak = 0, S_OutPeak = 1, S_InRms = 2, S_OutRms = 3, S_Crest = 4, S_SampleRate = 5, S_RateHz = 6,
        S_Hold = 7, S_Bits = 8, S_Levels = 9, S_QuantNoise = 10, S_ThdN = 11, S_Images = 12, S_AutoGain = 13, S_Cpu = 14,
        S_Latency = 15, S_DrivenPeak = 16, S_Folds = 17, S_Nyquist = 20, S_FilterHz = 21, S_Analysed = 22, S_Signal = 23,
        kTele = 32, kBands = 40, kScope = kTele + 2 * kBands;

    private static readonly string[] ModeWords = { "hard truncate", "soft clip", "wavefold" };

    public double Width => 700;
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex);
    public bool FullBleed => true;
    public string? Subtitle => "Bitcrusher";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, double v) => engine.DeviceSetParam(track, di, p, (float)Math.Clamp(v, 0, 1));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        double Def(int p) => engine.DeviceParamDefault(track, di, p);
        bool On(int p) => P(p) >= 0.5f;
        int ModeI() => Math.Clamp((int)Math.Round(P(Mode) * 2), 0, 2);

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        // A typeable value for a graph (⌥-click it, specs/035).
        ValueField F(int p, string name, Func<double, string> fmt) => ValueEntry.DeviceParam(engine, track, di, p, name, fmt, RefreshAll);
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;
        double ScDb(int i) => scN > i ? scope[i] : -120;

        // ---- mappings (Crush.h) and units -------------------------------------------------
        double Sr() => Sc(S_SampleRate) > 0 ? Sc(S_SampleRate) : 44100;
        double BitsOf(double v) => 1 + Math.Clamp(v, 0, 1) * 23;
        double RateHz(double v) => 500 * Math.Pow(Sr() * 0.48 / 500, Math.Clamp(v, 0, 1));
        double RateNorm(double hz) => Math.Log(Math.Clamp(hz, 500, Sr() * 0.48) / 500) / Math.Log(Sr() * 0.48 / 500);
        static double FiltHz(double v) => 200 * Math.Pow(100, Math.Clamp(v, 0, 1));
        static double FiltNorm(double hz) => Math.Log(Math.Clamp(hz, 200, 20000) / 200) / Math.Log(100);
        static double DriveDb(double v) => -12 + v * 36;
        static double GainDb(double v) => (v - 0.5) * 24;
        double Hold() => Math.Max(1, Sr() / RateHz(P(Rate)));
        double Levels() => Math.Pow(2, BitsOf(P(Bits)));

        static string Sgn(double v) => NotaNum.F($"{v:+0.0;−0.0;0.0}");
        static string Lvl(double db) => db <= -119 ? "—" : Sgn(db);
        static string Hz(double hz) => CrushMath.Hz(hz);
        static string KHz(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.#}\u2009k") : NotaNum.F($"{hz:0}");
        static string Pct(double v) => NotaNum.F($"{v * 100:0}\u2009%");
        static string LvF(double lv) => lv >= 1e6 ? NotaNum.F($"{lv / 1e6:0.0}\u2009M") : lv >= 1e4 ? NotaNum.F($"{lv / 1e3:0.0}\u2009k") : NotaNum.F($"{lv:0}");
        string BitsF(double v) => NotaNum.F($"{BitsOf(v):0.0}");
        string RateF(double v) => Hz(RateHz(v));
        static string DriveF(double v) => Sgn(DriveDb(v)) + " dB";
        static string GainF(double v) => Sgn(GainDb(v)) + " dB";
        static string FiltF(double v) => Hz(FiltHz(v));

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        // `lit` brightens the label and value (the knob the current tab is about).
        Control K(int p, string name, Func<double, string> fmt, string tip, Func<bool>? lit = null, bool teal = false, double cellW = 50, double size = 34, bool bipolar = false)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Bipolar = bipolar, Default = Def(p), Width = size, Height = size };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
            if (teal) knob.ArcColor = DeviceCardKit.ModInk;   // the modulation mark: a dashed arc
            knob.ValueChanged += v => { Raw(p, v); val.Text = fmt(P(p)); RefreshAll(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            ToolTip.SetTip(knob, tip);
            var cell = KnobCell(name, knob, val, cellW);
            TextBlock? label = null;
            if (cell is Panel pl) foreach (var c in pl.Children) if (c is TextBlock tb && tb != val) { label = tb; break; }
            readouts.Add(() =>
            {
                if (!knob.Dragging) { double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                val.Text = fmt(P(p));
                if (lit is null) return;
                bool on = lit();
                val.Foreground = on ? TextPrimary : TextSecondary;
                if (label is not null) label.Foreground = on ? TextSecondary : TextTertiary;
            });
            return cell;
        }

        // On/off switch bound to a param (>= 0.5 = on), with an optional live word.
        Control Toggle(int p, string label, string tip, Func<string>? live = null)
        {
            var wrap = Switch(label, () => On(p), () => { SetP(p, On(p) ? 0f : 1f); RefreshAll(); }, out var sync, liveLabel: live);
            readouts.Add(sync);
            Learn(wrap, p);
            ToolTip.SetTip(wrap, tip);
            return wrap;
        }

        // A latching chip bound to an on/off param.
        Border ParamChip(int p, string text, string tip)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            void Hi()
            {
                bool on = On(p);
                b.Background = on ? NotaPalette.AccentSubtle : Brushes.Transparent;
                b.BorderBrush = on ? Brass : NotaPalette.BorderStrong;
                tb.Foreground = on ? AccentBright : TextSecondary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; SetP(p, On(p) ? 0f : 1f); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }

        // Slider row: caps label · track · mono value.
        Control SliderRow(string label, int p, Func<double, string> fmt, string tip, bool modulation = false, bool bipolar = false)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, v); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: 38, modulation: modulation, bipolar: bipolar);
            readouts.Add(sync);
            Learn(bar, p);
            ToolTip.SetTip(bar, tip);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            return g;
        }

        int centreTab = 0;
        Control ModeSeg()
        {
            var seg = Segments(CrushMath.Modes, ModeI, i => { SetP(Mode, i / 2f); RefreshAll(); }, out var sync, padX: 6);
            readouts.Add(sync);
            Learn(seg, Mode);
            ToolTip.SetTip(seg, Loc.T("device.crush.mode_digital_hard_truncation_to"));
            return seg;
        }
        Control Info(Func<string> text)
        {
            var t = Mono("", 8, TextSecondary);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => t.Text = text());
            return t;
        }
        Control BitsKnob(double cellW) => K(Bits, "BITS", BitsF, Loc.T("device.crush.bits_the_bit_depth_1"), cellW: cellW);
        Control RateKnob(double cellW) => K(Rate, "RATE", RateF, Loc.T("device.crush.rate_the_reduced_sample_rate"), cellW: cellW);
        Control DriveKnob(double cellW) => K(Drive, "DRIVE", DriveF, Loc.T("device.crush.drive_12_24_db_into"), cellW: cellW);
        Control WetKnob(double cellW, double size) => K(DryWet, "DRY/WET", v => Pct(v), Loc.T("device.crush.wet_the_crushed_signal_against"), cellW: cellW, size: size);

        // ======================================================================
        // CENTRE — Quantiser (Bits · Rate beside it, Anti-alias under it) / Spectrum (drag the
        // Nyquist and the filter) / Transfer (Drive beside it, the mode under it)
        // ======================================================================
        CrQuantView? quant = null;
        CrQuantView NewQuant()
        {
            quant = new CrQuantView { Value = () => (P(Bits), P(Rate)) };
            quant.GestureBegin += () => { Begin(Bits); Begin(Rate); };
            quant.GestureEnd += () => { End(Bits); End(Rate); };
            quant.Changed += (b, r) => { Raw(Bits, b); Raw(Rate, r); RefreshAll(); };
            quant.ResetRequested += () => { Reset(Bits); Reset(Rate); RefreshAll(); };
            Learn(quant, Bits);
            ToolTip.SetTip(quant, MenuKit.Keys(Loc.T("device.crush.10_ms_of_a_sine")));
            ValueEntry.AttachAll(quant, F(Bits, "Bits", BitsF), F(Rate, "Rate", RateF));
            return quant;
        }
        Control QuantTab() => CardLayout.View(NewQuant(), CardLayout.KnobColumn(BitsKnob(50), RateKnob(50)),
            ParamChip(AntiAlias, "Anti-alias", Loc.T("device.crush.anti_alias_a_4th_order")));

        CrSpectrumView? spectrum = null;
        Control SpectrumTab()
        {
            spectrum = new CrSpectrumView { NyquistToNorm = hz => RateNorm(hz * 2), FilterToNorm = FiltNorm };
            int[] map = { Rate, PostFilter };
            spectrum.Value = h => P(map[h]);
            spectrum.Changed += (h, v) => { Raw(map[h], v); RefreshAll(); };
            spectrum.GestureBegin += h => Begin(map[h]);
            spectrum.GestureEnd += h => End(map[h]);
            spectrum.ResetRequested += h => { Reset(map[h]); RefreshAll(); };
            Learn(spectrum, Rate);
            ToolTip.SetTip(spectrum, MenuKit.Keys(Loc.T("device.crush.the_output_spectrum_neutral_ink")));
            ValueEntry.AttachHandles(spectrum, [F(Rate, "Rate", RateF)], [F(PostFilter, "Post filter", FiltF)]);
            return CardLayout.View(spectrum);
        }

        CrTransferView? curve = null;
        CrWaveView? wave = null;
        Control TransferTab()
        {
            curve = new CrTransferView { Width = 120 };
            curve.Value = _ => P(Drive);
            curve.Changed += (_, v) => { Raw(Drive, v); RefreshAll(); };
            curve.GestureBegin += _ => Begin(Drive);
            curve.GestureEnd += _ => End(Drive);
            curve.ResetRequested += _ => { Reset(Drive); RefreshAll(); };
            Learn(curve, Drive);
            ToolTip.SetTip(curve, MenuKit.Keys(Loc.T("device.crush.input_output_through_drive_the")));
            ValueEntry.AttachHandles(curve, [F(Drive, "Drive", DriveF)]);
            wave = new CrWaveView();
            ToolTip.SetTip(wave, Loc.T("device.crush.two_cycles_of_a_sine"));
            var pair = new DockPanel { LastChildFill = true, Children = { Docked(curve, Dock.Left), new Border { Margin = new Thickness(5, 0, 0, 0), Child = wave } } };
            return CardLayout.View(pair, CardLayout.KnobColumn(DriveKnob(50)), ModeSeg());
        }

        // ======================================================================
        // RIGHT — follows the centre tab: Grit (Quantiser) · Output (Spectrum, Transfer)
        // ======================================================================
        bool Crushing() => Sc(S_Signal) > 0.5 && P(DryWet) > 0.01f;
        (Control, Control) GritPanel()
        {
            var t = Caps("RESULT");
            var l1 = Mono("", 9, TextPrimary);
            var l2 = Mono("", 8, TextSecondary);
            l1.TextTrimming = l2.TextTrimming = TextTrimming.CharacterEllipsis;
            var box = CardLayout.ReadoutBox(new StackPanel { Spacing = 2, Children = { t, l1, l2 } });
            readouts.Add(() =>
            {
                bool hot = Crushing();
                double nyq = RateHz(P(Rate)) / 2, filt = FiltHz(P(PostFilter));
                string tail = (ModeI() switch { 1 => " · soft", 2 => " · fold", _ => "" }) + (On(AntiAlias) ? " · AA" : "");
                double rate = RateHz(P(Rate));
                l1.Text = NotaNum.F($"{BitsOf(P(Bits)):0.0} bit · ") + (rate >= 1000 ? NotaNum.F($"{rate / 1000:0.0} kHz") : NotaNum.F($"{rate:0} Hz")) + tail;
                l2.Text = filt < nyq ? Loc.T("device.crush.the_filter_cuts_the_images")
                    : NotaNum.F($"Nyquist {Hz(nyq)} · filter above");
                t.Foreground = hot ? AccentBright : TextTertiary;
                l1.Foreground = hot ? AccentBright : TextPrimary;
                box.BorderBrush = hot ? NotaPalette.BorderBrass : NotaPalette.GraphBorder;
            });
            ToolTip.SetTip(box, Loc.T("device.crush.what_comes_out_the_bit"));
            return (CardLayout.Head(CardLayout.Title(Loc.T("device.crush.grit"))), CardLayout.Rows(
                SliderRow("DITHER", Dither, v => Pct(v), Loc.T("device.crush.dither_up_to_lsb_of")),
                SliderRow("JITTER", Jitter, v => Pct(v), Loc.T("device.crush.jitter_the_hold_length_wanders")),
                SliderRow("NOISE", NoiseFloor, v => Pct(v), Loc.T("device.crush.noise_a_white_noise_floor")),
                CardLayout.Rule(),
                box));
        }

        (Control, Control)? outputPanel = null;
        (Control, Control) OutputPanel()
        {
            if (outputPanel is { } built) return built;
            Control MRow(string key, Func<string> val, Func<IBrush> ink)
            {
                var k = new TextBlock { Text = key, FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
                var v = Mono("", 8, TextPrimary); v.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => { v.Text = val(); v.Foreground = ink(); });
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(k); g.Children.Add(Col(v, 1));
                return g;
            }
            var meters = CardLayout.ReadoutBox(new StackPanel
            {
                Spacing = 2, Children =
                {
                    Caps("MEASUREMENTS"),
                    MRow("in", () => Lvl(ScDb(S_InPeak)) + " dB", () => TextPrimary),
                    MRow("out", () => Lvl(ScDb(S_OutPeak)) + " dB", () => ScDb(S_OutPeak) > 0 ? NotaPalette.DangerBright : TextPrimary),
                    MRow("crest", () => ScDb(S_OutRms) <= -119 ? "—" : NotaNum.F($"{Sc(S_Crest):0.0} dB"), () => TextPrimary),
                },
            });
            ToolTip.SetTip(meters, Loc.T("device.crush.peaks_in_and_out_and"));
            outputPanel = (CardLayout.Head(Toggle(AutoGain, "Auto gain", Loc.T("device.crush.auto_gain_rides_the_output"),
                    () => On(AutoGain) ? "Auto gain " + Sgn(Sc(S_AutoGain)) + " dB" : "Auto gain")),
                CardLayout.Rows(
                    meters,
                    Toggle(DcFilter, "DC filter", Loc.T("device.crush.dc_filter_a_10_hz")),
                    CardLayout.Rule(),
                    Info(() => Sc(S_Analysed) < 0.5 ? Loc.T("common.no_signal") : ScDb(S_Images) <= -119 ? NotaNum.F($"no images above {KHz(Sc(S_Nyquist))}")
                        : NotaNum.F($"images above {KHz(Sc(S_Nyquist))} {ScDb(S_Images):0} dB")),
                    Info(() => Sc(S_Analysed) < 0.5 ? "THD+N —" : NotaNum.F($"THD+N {Sc(S_ThdN) * 100:0.0} %"))));
            return outputPanel.Value;
        }

        // ======================================================================
        // Frames: L (tabs, the panel that follows them, Filter · Dry/wet · Gain under it) · S (the
        // quantiser over the mode and Bits · Rate · Drive · Dry/wet)
        // ======================================================================
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            string DriveX() => NotaNum.F($"×{Math.Pow(10, DriveDb(P(Drive)) / 20):0.0}");
            extras.Text = centreTab switch
            {
                1 => "20 Hz … 20 kHz",
                2 => (ModeI() switch
                {
                    2 => Sc(S_Signal) > 0.5 ? NotaNum.F($"drive {DriveX()} → {Sc(S_Folds):0} folds") : NotaNum.F($"drive {DriveX()} · no signal"),
                    1 => NotaNum.F($"drive {DriveX()} → soft clip"),
                    _ => NotaNum.F($"drive {DriveX()} · straight"),
                }) + " · " + LvF(Levels()) + (ModeI() == 2 ? Loc.T("device.crush.levels_after_the_fold") : " levels"),
                _ => NotaNum.F($"{KHz(Sr())} → {KHz(RateHz(P(Rate)))} · 1 of {Hold():0.#} · {LvF(Levels())} levels · quant. noise {-(6.02 * BitsOf(P(Bits)) + 1.76):0} dB"),
            };
        });
        Control root = CardLayout.IsMini(ctx, di)
            ? CardLayout.Mini(NewQuant(), ModeSeg(), BitsKnob(56), RateKnob(56), DriveKnob(56), WetKnob(56, 34))
            : CardLayout.Large(new[] { "Quantiser", "Spectrum", "Transfer" }, 0,
                t => t switch { 1 => SpectrumTab(), 2 => TransferTab(), _ => QuantTab() },
                t => t == 0 ? GritPanel() : OutputPanel(),
                extras,
                CardLayout.Output(K(PostFilter, "FILTER", FiltF, Loc.T("device.crush.post_filter_a_low_pass"), cellW: 54),
                    WetKnob(58, 44),
                    K(Output, "GAIN", GainF, Loc.T("device.crush.output_gain_12_db"), cellW: 54, bipolar: true)),
                t => { centreTab = t; Refresh(); });

        void Refresh()
        {
            // Only the Spectrum tab pays for the FFT; the others read the telemetry.
            scN = engine.DeviceScope(track, di, scope, centreTab == 1 ? kScope : kTele);
            double drive = Math.Pow(10, DriveDb(P(Drive)) / 20);
            if (centreTab == 0 && quant is not null)
                quant.Set(BitsOf(P(Bits)), Hold(), Sr(), ModeI(), drive);
            if (centreTab == 1 && spectrum is not null)
                spectrum.Set(scope, kTele, kTele + kBands, scN >= kScope ? kBands : 0, RateHz(P(Rate)) / 2, FiltHz(P(PostFilter)), Sc(S_Analysed) > 0.5);
            if (centreTab == 2 && curve is not null && wave is not null)
            {
                curve.Set(ModeI(), drive, Levels(), Sc(S_Signal) > 0.5 ? Sc(S_DrivenPeak) : 0);
                wave.Set(ModeI(), drive, DriveDb(P(Drive)), Levels(), Hold(), Sr());
            }
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

    }
}
