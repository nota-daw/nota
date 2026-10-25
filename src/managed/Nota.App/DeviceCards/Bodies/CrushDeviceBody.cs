// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Crush body (bit crusher, device kind 12), a build of the
// "Nota Crush" mockup (700 × 260) on the Ceiling / Beat Repeat frame: an always-visible CRUSH
// column (input · output), a centre panel with Quantiser / Spectrum / Transfer tabs — the held
// and quantised steps over a sine (drag: Bits up / down, Rate left / right), the output
// spectrum from the engine's FFT with what the crush added and the images above the reduced
// Nyquist (drag the Nyquist and the filter lines), the transfer curve with the form it makes
// (drag: Drive) — with Mode and Anti-alias over the graph and Bits / Rate / Drive / Wet under
// it, and a right panel with Grit / Output tabs. The meters and the spectrum
// come from the engine (Crush.h scopeRead). Every control is a device param (normalized 0..1),
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
        static TextBlock Caps(string t, IBrush? c = null, double fs = 0) => FxParts.Label(t, c);
        static TextBlock Mono(string t, double fs, IBrush c) { var tb = FxParts.Info(t, c); tb.TextTrimming = TextTrimming.None; return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, string tip)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Default = Def(p), Width = 34, Height = 34 };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
            knob.ValueChanged += v => { Raw(p, v); val.Text = fmt(P(p)); RefreshAll(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            ToolTip.SetTip(knob, tip);
            var cell = KnobCell(name, knob, val, 48);
            readouts.Add(() =>
            {
                if (!knob.Dragging) { double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                val.Text = fmt(P(p));
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

        // A latching button bound to an on/off param: raised at rest; on, the soft tint with orange text.
        Border ParamChip(int p, string text, string tip)
        {
            var tb = new TextBlock { Text = text, FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = NotaSize.Dropdown, Padding = new Thickness(9, 0), CornerRadius = NotaRadius.Field, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            void Hi()
            {
                bool on = On(p);
                b.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                tb.Foreground = on ? AccentBright : TextPrimary;
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; SetP(p, On(p) ? 0f : 1f); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            Learn(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }

        // Slider row: label · value bar · value.
        Control SliderRow(string label, int p, Func<double, string> fmt, string tip, bool modulation = false, bool bipolar = false)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, v); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: 42, modulation: modulation, bipolar: bipolar);
            readouts.Add(sync);
            Learn(bar, p);
            ToolTip.SetTip(bar, tip);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            return g;
        }

        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 24, ColumnSpacing = 8 };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        Control ModeHead()
        {
            var seg = Segments(CrushMath.Modes, ModeI, i => { SetP(Mode, i / 2f); RefreshAll(); }, out var sync, padX: 6);
            seg.VerticalAlignment = VerticalAlignment.Center;
            readouts.Add(sync);
            Learn(seg, Mode);
            ToolTip.SetTip(seg, "Mode — Digital: hard truncation to the levels; Analog: a soft tanh clip before them (+4\u2009dB, rounder); Fold: a wavefolder — more Drive folds the peaks back more times");
            var words = FxParts.Info();
            readouts.Add(() => words.Text = ModeWords[ModeI()]);
            var aa = ParamChip(AntiAlias, "Anti-alias", "Anti-alias — a 4th-order low-pass at the reduced rate's Nyquist before the hold, so less folds back as images");
            return HeadRow(Row(8, Caps("Mode"), seg, words), aa);
        }

        // The knob row under every graph: Bits, Rate, Drive, Wet and two info lines.
        int centreTab = 0;
        Control KnobRow(Func<string> info1, Func<string> info2)
        {
            var i1 = Mono("", 7, TextTertiary); i1.HorizontalAlignment = HorizontalAlignment.Right;
            var i2 = Mono("", 7, TextTertiary); i2.HorizontalAlignment = HorizontalAlignment.Right;
            i1.TextTrimming = i2.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var knobs = Row(10,
                K(Bits, "Bits", BitsF, "Bits — the bit depth, 1 … 24: fewer bits, fewer levels, more grit"),
                K(Rate, "Rate", RateF, "Rate — the reduced sample rate, 500\u2009Hz … full: lower holds each value longer and adds images above its Nyquist"),
                K(Drive, "Drive", DriveF, "Drive — −12 … +24\u2009dB into the crusher: pushes the signal up the levels, into the Analog clip or into more folds"),
                K(DryWet, "Wet", v => Pct(v), "Wet — the crushed signal against the dry one"));
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 54, ColumnSpacing = 8 };
            g.Children.Add(knobs);
            g.Children.Add(Col(new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2), Children = { i1, i2 } }, 1));
            return g;
        }
        static Control TabBody(Control head, Control window, Control knobs) => new DockPanel
        {
            LastChildFill = true, Margin = new Thickness(0, 6, 0, 0),
            Children = { Docked(head, Dock.Top), Docked(knobs, Dock.Bottom), new Border { Margin = new Thickness(0, 6, 0, 6), Child = window } },
        };

        // ======================================================================
        // LEFT — CRUSH column (input · output)
        // ======================================================================
        var colTitle = Caps("Crush"); colTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var inPill = new FxLevel { VerticalAlignment = VerticalAlignment.Stretch };
        var outPill = new FxLevel { VerticalAlignment = VerticalAlignment.Stretch };
        ToolTip.SetTip(inPill, "The input peak, −60 … 0\u2009dBFS");
        ToolTip.SetTip(outPill, "The output peak, −60 … 0\u2009dBFS");
        var colIn = Mono("", 7, TextPrimary); colIn.HorizontalAlignment = HorizontalAlignment.Center;
        var colOut = Mono("", 7, TextPrimary); colOut.HorizontalAlignment = HorizontalAlignment.Center;
        static double NormLvl(double db) => Math.Clamp((db + 60) / 60, 0, 1);
        bool Crushing() => Sc(S_Signal) > 0.5 && P(DryWet) > 0.01f;
        readouts.Add(() =>
        {
            inPill.Set(NormLvl(ScDb(S_InPeak)));
            outPill.Set(NormLvl(ScDb(S_OutPeak)));
            colIn.Text = Lvl(ScDb(S_InPeak));
            colOut.Text = Lvl(ScDb(S_OutPeak));
            colTitle.Foreground = Crushing() ? AccentBright : TextTertiary;   // lit while it crushes
        });
        Control PillCol(FxLevel pill, string lbl)
        {
            var l = Caps(lbl); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 4, 0, 0);
            return new DockPanel { Children = { Docked(l, Dock.Bottom), new Border { HorizontalAlignment = HorizontalAlignment.Center, Child = pill } } };
        }
        var pills = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 5) };
        pills.Children.Add(PillCol(inPill, "In"));
        pills.Children.Add(Col(PillCol(outPill, "Out"), 1));
        var leftCol = new RuleSection
        {
            Width = 64, Padding = new Thickness(0, 8, 0, 6),
            Child = new DockPanel { Children = { Docked(colTitle, Dock.Top), Docked(colOut, Dock.Bottom), Docked(colIn, Dock.Bottom), pills } },
        };
        DockPanel.SetDock(leftCol, Dock.Left);

        // ======================================================================
        // CENTRE — Quantiser / Spectrum / Transfer
        // ======================================================================
        CrQuantView? quant = null;
        Control QuantTab()
        {
            quant = new CrQuantView { Value = () => (P(Bits), P(Rate)) };
            quant.GestureBegin += () => { Begin(Bits); Begin(Rate); };
            quant.GestureEnd += () => { End(Bits); End(Rate); };
            quant.Changed += (b, r) => { Raw(Bits, b); Raw(Rate, r); RefreshAll(); };
            quant.ResetRequested += () => { Reset(Bits); Reset(Rate); RefreshAll(); };
            Learn(quant, Bits);
            ToolTip.SetTip(quant, "10\u2009ms of a sine through the crusher: the source dashed, the held and quantised steps in neutral ink. Drag up / down for Bits, left / right for Rate; double-click resets both.");
            var knobs = KnobRow(
                () => NotaNum.F($"{KHz(Sr())} → {KHz(RateHz(P(Rate)))} · 1 of {Hold():0.#}"),
                () => NotaNum.F($"quant. noise {-(6.02 * BitsOf(P(Bits)) + 1.76):0}\u2009dB"));
            return TabBody(ModeHead(), quant, knobs);
        }

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
            ToolTip.SetTip(spectrum, "The output spectrum: neutral ink is the signal, teal what the crush added — bright above the reduced Nyquist (the images). Drag the Nyquist line for Rate, the filter line for the post filter; double-click resets.");
            var knobs = KnobRow(
                () => Sc(S_Analysed) < 0.5 ? "no signal" : ScDb(S_Images) <= -119 ? NotaNum.F($"no images above {KHz(Sc(S_Nyquist))}")
                    : NotaNum.F($"images above {KHz(Sc(S_Nyquist))} {ScDb(S_Images):0}\u2009dB"),
                () => Sc(S_Analysed) < 0.5 ? "THD+N —" : NotaNum.F($"THD+N {Sc(S_ThdN) * 100:0.0}\u2009%"));
            return TabBody(ModeHead(), spectrum, knobs);
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
            ToolTip.SetTip(curve, "Input → output through Drive, the mode and the levels; the dot is where the signal peaks. Drag up / down for Drive; double-click resets.");
            wave = new CrWaveView();
            ToolTip.SetTip(wave, "Two cycles of a sine at the driven level (dashed) and what comes out");
            var pair = new DockPanel { LastChildFill = true, Children = { Docked(curve, Dock.Left), new Border { Margin = new Thickness(5, 0, 0, 0), Child = wave } } };
            string DriveX() => NotaNum.F($"×{Math.Pow(10, DriveDb(P(Drive)) / 20):0.0}");
            var knobs = KnobRow(
                () => ModeI() switch
                {
                    2 => Sc(S_Signal) > 0.5 ? NotaNum.F($"drive {DriveX()} → {Sc(S_Folds):0} folds") : NotaNum.F($"drive {DriveX()} · no signal"),
                    1 => NotaNum.F($"drive {DriveX()} → soft clip"),
                    _ => NotaNum.F($"drive {DriveX()} · straight"),
                },
                () => ModeI() == 2 ? LvF(Levels()) + " levels after the fold" : LvF(Levels()) + " levels");
            return TabBody(ModeHead(), pair, knobs);
        }

        // ======================================================================
        // RIGHT — Grit / Output
        // ======================================================================
        // Rows on a fixed grid ("19" rows, "1" / "3" rules, "Auto" boxes, "*" the rest); null = a rule.
        static Control Stack(string defs, params Control?[] rows)
        {
            var g = new Grid { RowDefinitions = new RowDefinitions(defs), Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 0; i < rows.Length; i++)
                g.Children.Add(GRow(rows[i] ?? new Border { Height = 1, Background = NotaPalette.Rule, VerticalAlignment = VerticalAlignment.Center }, i));
            return g;
        }
        Control FilterRow() => SliderRow("Filter", PostFilter, FiltF, "Post filter — a low-pass after the crush, 200\u2009Hz … 20 kHz: under the Nyquist it smooths the steps", modulation: true);
        Control GainRow() => SliderRow("Gain", Output, GainF, "Output gain, ±12\u2009dB", bipolar: true);

        Control GritTab()
        {
            var t = Caps("Result"); t.Height = 14;
            var l1 = FxParts.Value(); l1.Height = 14;
            var l2 = FxParts.Info(); l2.Height = 14;
            l1.TextTrimming = TextTrimming.CharacterEllipsis;
            var box = new Border
            {
                Background = NotaGraph.Ground, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 3), Margin = new Thickness(0, 4, 0, 0),
                Child = new StackPanel { Children = { t, l1, l2 } },
            };
            readouts.Add(() =>
            {
                bool hot = Crushing();
                double nyq = RateHz(P(Rate)) / 2, filt = FiltHz(P(PostFilter));
                string tail = (ModeI() switch { 1 => " · soft", 2 => " · fold", _ => "" }) + (On(AntiAlias) ? " · AA" : "");
                double rate = RateHz(P(Rate));
                l1.Text = NotaNum.F($"{BitsOf(P(Bits)):0.0} bit · ") + (rate >= 1000 ? NotaNum.F($"{rate / 1000:0.0}\u2009kHz") : NotaNum.F($"{rate:0}\u2009Hz")) + tail;
                l2.Text = filt < nyq ? "the filter cuts the images"
                    : NotaNum.F($"Nyquist {Hz(nyq)} · filter above");
                t.Foreground = hot ? AccentBright : TextTertiary;
                l1.Foreground = hot ? AccentBright : TextPrimary;
            });
            ToolTip.SetTip(box, "What comes out: the bit depth and the rate, and whether the post filter cuts the images above the reduced Nyquist or lets them through");
            return Stack("19,19,19,7,19,19,*",
                SliderRow("Dither", Dither, v => Pct(v), "Dither — up to ±½ LSB of triangular noise before the levels: softens the steps into hiss"),
                SliderRow("Jitter", Jitter, v => Pct(v), "Jitter — the hold length wanders up to ±50 %: an unsteady, broken clock"),
                SliderRow("Noise", NoiseFloor, v => Pct(v), "Noise — a white noise floor under the crush, up to −50\u2009dBFS"),
                null, FilterRow(), GainRow(), box);
        }

        Control OutputTab()
        {
            Control MRow(string key, Func<string> val, Func<IBrush> ink)
            {
                var k = Caps(key);
                var v = FxParts.Value(); v.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => { v.Text = val(); v.Foreground = ink(); });
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 16 };
                g.Children.Add(k); g.Children.Add(Col(v, 1));
                return g;
            }
            var meters = new Border
            {
                Background = NotaGraph.Ground, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 4),
                Child = new StackPanel
                {
                    Children =
                    {
                        new Border { Height = 16, Child = Caps("Measurements") },
                        MRow("in", () => Lvl(ScDb(S_InPeak)) + " dB", () => TextPrimary),
                        MRow("out", () => Lvl(ScDb(S_OutPeak)) + " dB", () => ScDb(S_OutPeak) > 0 ? NotaPalette.DangerBright : TextPrimary),
                        MRow("crest", () => ScDb(S_OutRms) <= -119 ? "—" : NotaNum.F($"{Sc(S_Crest):0.0} dB"), () => TextPrimary),
                    },
                },
            };
            ToolTip.SetTip(meters, "Peaks in and out, and the output's crest factor (peak over RMS) — the crush flattens it");
            var toggles = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Children =
                {
                    Toggle(AutoGain, "Auto gain", "Auto gain — rides the output back to the input's loudness (300\u2009ms RMS, ±24\u2009dB), so turning Bits or Drive doesn't jump in level",
                        () => On(AutoGain) ? "Auto gain " + Sgn(Sc(S_AutoGain)) + "\u2009dB" : "Auto gain"),
                    Toggle(DcFilter, "DC filter", "DC filter — a 10\u2009Hz high-pass on the crushed signal: takes out the offset a coarse, folded or noisy crush can leave"),
                },
            };
            return Stack("Auto,2,19,19,3,*", meters, new Border(), FilterRow(), GainRow(), null, toggles);
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[2];
        var extras = FxParts.Info();
        readouts.Add(() =>
        {
            extras.Text = centreTab switch
            {
                1 => "20\u2009Hz … 20\u2009kHz",
                2 => "in → out",
                _ => NotaNum.F($"{LvF(Levels())} levels · hold {Hold():0.#} smp"),
            };
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => SpectrumTab(), 2 => TransferTab(), _ => QuantTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : GritTab();

        var centre = FxParts.Tabs(new[] { "Quantiser", "Spectrum", "Transfer" }, centreHost, CentreBody, t => { centreTab = t; Refresh(); }, extras);
        var rightFrame = FxParts.Tabs(new[] { "Grit", "Output" }, rightHost, RightBody, _ => RefreshAll());
        var right = FxParts.Section(rightFrame); right.Width = 196;
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = FxParts.Section(centre); centreBox.Margin = new Thickness(SectionGap, 0);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, CardPad, 0, 0), Children = { leftCol, right, centreBox } };
        var root = bodyRow;

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
