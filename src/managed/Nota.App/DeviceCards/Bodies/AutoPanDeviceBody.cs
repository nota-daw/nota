// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Orbit body (auto-pan / tremolo, device kind 9), a build of
// the "Nota Orbit" mockup (700 × 260) in the Level / EQ-8 language: an LFO column (waveform,
// Hz or Sync, the rate as a big readout with its period, Rate and Shape — Glide on S&H), a
// centre panel with both channels' gain over two cycles, a running head with the gain dots and
// the floor the depth reaches (drag: Amount up / down, Phase left / right), the stereo position
// on an L—R line over the swing it covers, a MOTION column (Amount, Phase, Mix, the 0° / 90° /
// 180° phase snaps and the output meters) with the mode it makes (tremolo, auto-pan, offset
// pan). The live picture comes from the engine (AutoPan.h scopeRead). Every
// control is a device param (normalized 0..1), so automation / MIDI learn / presets / A-B /
// persistence come for free. FullBleed — the shared shell draws the header (name · preset ·
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
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class AutoPanDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match AutoPan.h) ─────────────────────────────
    private const int Rate = 0, Amount = 1, Waveform = 2, Shape = 3, Phase = 4, Mix = 5, Sync = 6, Division = 7;
    // Scope layout (AutoPan::S_* / kTele).
    private const int S_InPeak = 0, S_OutPeakL = 1, S_OutPeakR = 2, S_GainL = 3, S_GainR = 4, S_Pan = 5, S_WinPhase = 6,
        S_SampleRate = 11, S_Sh0 = 12, S_PanMin = 16, S_PanMax = 17, kTele = 32;

    public double Width => 700;
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex);
    public bool FullBleed => true;
    public string? Subtitle => "Panner";   // the processing type, shown as the header badge

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, double v) => engine.DeviceSetParam(track, di, p, (float)Math.Clamp(v, 0, 1));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, double v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var scope = new float[kTele];
        int scN = 0;
        double Sc(int i, double dflt = 0) => scN > i ? scope[i] : dflt;

        // ---- state and units (AutoPan.h) ---------------------------------------------------
        bool On() => !engine.DeviceBypassed(track, di);
        bool Synced() => P(Sync) >= 0.5f;
        int WaveI() => OrbitMath.WaveIndex(P(Waveform));
        int DivI() => OrbitMath.DivIndex(P(Division));
        double Bpm() => engine.Bpm > 0 ? engine.Bpm : 120;
        double RateHz() => Synced() ? Bpm() / 60 / OrbitMath.DivBeats[DivI()] : OrbitMath.FreeHz(P(Rate));
        double PeriodMs() => 1000 / Math.Max(1e-3, RateHz());
        double Deg() => P(Phase) * 360;
        string Mode()
        {
            if (!On()) return "bypass";
            double d = Deg();
            return d <= 15 || d >= 345 ? "tremolo" : Math.Abs(d - 180) <= 15 ? "auto-pan" : "offset pan";
        }

        static string HzF(double hz) => hz >= 10 ? NotaNum.F($"{hz:0.0}") : NotaNum.F($"{hz:0.00}");
        static string PctF(double v) => NotaNum.F($"{v * 100:0}\u2009%");
        static string PanF(double pan) => Math.Abs(pan) < 0.02 ? "C" : (pan < 0 ? "L" : "R") + NotaNum.F($"{Math.Abs(pan) * 100:0}");
        static string Ms(double ms) => NotaNum.F($"{ms:0}\u2009ms");

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null) => new()
        { Text = NotaText.Label(t), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Measure"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }

        // Slider row: caps label · track · mono value. `label` is returned so a row can rename itself.
        Grid Slider(string label, int p, Func<double, string> fmt, string tip, out TextBlock lbl, double labelW = 40, double valueW = 40,
            bool modulation = false, Func<double, double>? snap = null)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, snap is null ? v : snap(v)); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: valueW, modulation: modulation);
            readouts.Add(sync);
            Learn(bar, p);
            ToolTip.SetTip(bar, tip);
            lbl = Caps(label);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelW},*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(lbl);
            g.Children.Add(Col(bar, 1));
            return g;
        }

        // Gauge knob over a normalized param (automation gesture, MIDI learn, live follow).
        Control K(int p, string name, Func<double, string> fmt, string tip, double cellW = 50, double size = 34, Func<double, double>? snap = null)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.Format = fmt;
            knob.ValueChanged += v => { Raw(p, snap is null ? v : snap(v)); RefreshAll(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            ToolTip.SetTip(knob, tip);
            readouts.Add(() => { if (!knob.Dragging) { double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; } val.Text = fmt(P(p)); });
            return KnobCell(name, knob, val, cellW);
        }

        // A legend sample: a drawn 8px stroke and its word (never a typed dash).

        // ======================================================================
        // LEFT — LFO: waveform · Hz / Sync · the rate · Rate · Shape
        // ======================================================================
        var syncSeg = Segments(new[] { "Hz", "Sync" }, () => Synced() ? 1 : 0, i => { SetP(Sync, i); RefreshAll(); }, out var syncSync, fill: true, padX: 0);
        syncSeg.Width = 76;
        readouts.Add(syncSync);
        Learn(syncSeg, Sync);
        ToolTip.SetTip(syncSeg, Loc.T("common.hz_runs_the_lfo_free"));

        var waveSeg = Segments(OrbitMath.Waves, WaveI, i => { SetP(Waveform, i / 4.0); RefreshAll(); }, out var waveSync, padX: 5);
        readouts.Add(waveSync);
        Learn(waveSeg, Waveform);
        ToolTip.SetTip(waveSeg, Loc.T("device.auto_pan.the_lfo_shape_s_h"));

        var big = Mono("", 26, TextPrimary); big.FontWeight = FontWeight.Bold; big.LineHeight = 28;
        var bigSub = new TextBlock { FontSize = 8, Foreground = TextSecondary };
        var period = Mono("", 7, TextTertiary); period.Margin = new Thickness(0, 2, 0, 0);
        readouts.Add(() =>
        {
            double hz = RateHz();
            if (Synced())
            {
                big.Text = OrbitMath.DivNames[DivI()];
                bigSub.Text = NotaNum.F($"note · {Bpm():0.##}\u2009BPM");
                period.Text = HzF(hz) + "\u2009Hz · " + Ms(PeriodMs());
            }
            else
            {
                big.Text = HzF(hz);
                bigSub.Text = "Hz · free";
                period.Text = "period " + Ms(PeriodMs());
            }
            big.Foreground = On() ? TextPrimary : NotaPalette.TextAxis;
        });
        var bigBlock = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { big, bigSub } };   // the period is in the tab bar
        ToolTip.SetTip(bigBlock, Loc.T("common.the_lfo_rate_in_hz"));

        var rateRow = Slider("RATE", Rate, v => HzF(OrbitMath.FreeHz(v)) + "\u2009Hz", Loc.T("device.auto_pan.rate_the_free_lfo_rate"), out _, 38, 46);
        var divRow = Slider("RATE", Division, v => OrbitMath.DivNames[OrbitMath.DivIndex(v)],
            Loc.T("device.auto_pan.rate_the_note_value_of"), out _, 38, 46,
            snap: v => OrbitMath.DivNorm(OrbitMath.DivIndex(v)));
        readouts.Add(() => { rateRow.IsVisible = !Synced(); divRow.IsVisible = Synced(); });
        var shapeRow = Slider("SHAPE", Shape, v => PctF(v), Loc.T("device.auto_pan.shape_sharpens_sine_tri_and"), out var shapeLbl, 38, 46);
        readouts.Add(() => shapeLbl.Text = WaveI() == 4 ? "Glide" : "Shape");


        // ======================================================================
        // CENTRE — GAIN · 2 CYCLES, and the pan line
        // ======================================================================
        var graph = new OrbitGainView { Value = () => (P(Amount), P(Phase)) };
        graph.GestureBegin += () => { Begin(Amount); Begin(Phase); };
        graph.GestureEnd += () => { End(Amount); End(Phase); };
        graph.Changed += (a, ph) => { Raw(Amount, a); Raw(Phase, ph); RefreshAll(); };
        graph.ResetRequested += () => { Reset(Amount); Reset(Phase); RefreshAll(); };
        Learn(graph, Amount);
        ToolTip.SetTip(graph, MenuKit.Keys(Loc.T("device.auto_pan.both_channels_gain_over_two")));
        ValueEntry.AttachAll(graph, ValueEntry.DeviceParam(engine, track, di, Amount, "Amount", PctF, RefreshAll), ValueEntry.DeviceParam(engine, track, di, Phase, "Phase", v => NotaNum.F($"{v * 360:0}°"), RefreshAll));

        var panBar = new OrbitPanBar { VerticalAlignment = VerticalAlignment.Center };
        var panTxt = Mono("", 8, TextPrimary); panTxt.TextAlignment = TextAlignment.Right; panTxt.Width = 28;
        var lTag = Mono("L", 7, NotaPalette.VizInk); lTag.FontWeight = FontWeight.Medium;
        var rTag = Mono("R", 7, DeviceCardKit.ModInkBright); rTag.FontWeight = FontWeight.Medium;
        var panRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 6, Height = 14, Margin = new Thickness(0, 4, 0, 0) };
        panRow.Children.Add(lTag);
        panRow.Children.Add(Col(panBar, 1));
        panRow.Children.Add(Col(rTag, 2));
        panRow.Children.Add(Col(panTxt, 3));
        ToolTip.SetTip(panRow, Loc.T("device.auto_pan.where_the_sound_sits_now"));
        // The graph with the pan line under it: one view.
        var graphView = new DockPanel { LastChildFill = true, Children = { Docked(panRow, Dock.Bottom), graph } };

        // ======================================================================
        // RIGHT — MOTION: Amount · Phase · Mix · phase snaps · output meters
        // ======================================================================
        var modeWord = Mono("", 7, TextSecondary);
        modeWord.Margin = new Thickness(0, 0, 5, 0);
        readouts.Add(() =>
        {
            string m = Mode();
            modeWord.Text = m;
            modeWord.Foreground = m == "auto-pan" ? TextPrimary : m == "tremolo" ? DeviceCardKit.ModInkBright : TextSecondary;
        });
        ToolTip.SetTip(modeWord, Loc.T("device.auto_pan.what_the_phase_makes_tremolo"));

        var amountRow = Slider("AMOUNT", Amount, v => PctF(v), Loc.T("device.auto_pan.amount_the_depth_each_channel"), out _, 44, 36, modulation: true);
        var phaseRow = Slider("PHASE", Phase, v => NotaNum.F($"{v * 360:0}°"), Loc.T("device.auto_pan.phase_the_right_lfo_against"), out _, 44, 36, modulation: true,
            snap: v => Math.Round(v * 360) / 360);
        var mixRow = Slider("MIX", Mix, v => PctF(v), Loc.T("device.auto_pan.mix_the_moving_signal_against"), out _, 44, 36);

        Border Snap(string text, double deg)
        {
            var tb = Mono(text, 7, TextTertiary); tb.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new Border
            {
                BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell, Padding = new Thickness(0, 1),
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                SetP(Phase, deg / 360); RefreshAll(); e.Handled = true;
            };
            readouts.Add(() =>
            {
                bool on = Math.Abs(Math.Round(Deg()) - deg) < 0.5;
                b.Background = on ? NotaPalette.TrackOff : Brushes.Transparent;
                tb.Foreground = on ? TextPrimary : TextTertiary;
            });
            Learn(b, Phase);
            ToolTip.SetTip(b, deg == 0 ? Loc.T("device.auto_pan.0_tremolo_both_channels_together") : deg == 90 ? Loc.T("device.auto_pan.90_a_quarter_cycle_apart") : Loc.T("device.auto_pan.180_auto_pan_one_side"));
            return b;
        }
        var snaps = new Grid { ColumnDefinitions = new ColumnDefinitions("34,34,34"), ColumnSpacing = 1, VerticalAlignment = VerticalAlignment.Center };
        snaps.Children.Add(Snap("0°", 0));
        snaps.Children.Add(Col(Snap("90°", 90), 1));
        snaps.Children.Add(Col(Snap("180°", 180), 2));

        Control MeterRow(string label, IBrush labelInk, IBrush ink, int peakSlot, int gainSlot)
        {
            var m = new OrbitMeter { Ink = ink, VerticalAlignment = VerticalAlignment.Center };
            var v = Mono("", 8, TextPrimary); v.TextAlignment = TextAlignment.Right; v.Width = 36;
            readouts.Add(() =>
            {
                double pk = Sc(peakSlot, -120);
                m.Set(On() ? (pk + 60) / 60 : 0);
                v.Text = On() ? OrbitMath.Db(Sc(gainSlot, 1)) : "0.0";
            });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*,Auto"), ColumnSpacing = 0 };
            g.Children.Add(Caps(label, labelInk));
            g.Children.Add(Col(m, 1));
            g.Children.Add(Col(new Border { Margin = new Thickness(6, 0, 0, 0), Child = v }, 2));
            ToolTip.SetTip(g, Loc.T("device.auto_pan.the_channels_output_peak_60"));
            return g;
        }
        var meters = new Border
        {
            Background = Brushes.Transparent,   // a fixed-height row in the panel
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0),
            Child = new StackPanel
            {
                Spacing = 5, Children =
                {
                    MeterRow("OUT L", NotaPalette.VizInk, NotaPalette.VizInk, S_OutPeakL, S_GainL),
                    MeterRow("OUT R", DeviceCardKit.ModInkBright, DeviceCardKit.ModInkBright, S_OutPeakR, S_GainR),
                },
            },
        };

        // ======================================================================
        // Frames: L (the motion view, the LFO panel, Mix under it) · S (the graph over the
        // waveform and Rate · Amount · Phase · Mix)
        // ======================================================================
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            extras.Text = Mode() + " · " + OrbitMath.WaveNames[WaveI()] + " · " + Ms(PeriodMs());
            extras.Foreground = Mode() == "tremolo" ? DeviceCardKit.ModInkBright : TextTertiary;
        });
        Control RateKnob(double cellW)
        {
            var free = K(Rate, "RATE", v => HzF(OrbitMath.FreeHz(v)) + "\u2009Hz", Loc.T("device.auto_pan.rate_the_free_lfo_rate"), cellW);
            var div = K(Division, "RATE", v => OrbitMath.DivNames[OrbitMath.DivIndex(v)], Loc.T("device.auto_pan.rate_the_note_value_of"), cellW,
                snap: v => OrbitMath.DivNorm(OrbitMath.DivIndex(v)));
            readouts.Add(() => { free.IsVisible = !Synced(); div.IsVisible = Synced(); });
            return new Panel { Children = { free, div } };
        }
        Control PhaseKnob(double cellW) => K(Phase, "PHASE", v => NotaNum.F($"{v * 360:0}°"), Loc.T("device.auto_pan.phase_the_right_lfo_against"), cellW,
            snap: v => Math.Round(v * 360) / 360);
        Control AmountKnob(double cellW) => K(Amount, "AMOUNT", v => PctF(v), Loc.T("device.auto_pan.amount_the_depth_each_channel"), cellW);
        Control MixKnob(double cellW, double size) => K(Mix, "DRY/WET", v => PctF(v), Loc.T("device.auto_pan.mix_the_moving_signal_against"), cellW, size);
        Control root = CardLayout.IsMini(ctx, di)
            ? CardLayout.Mini(graphView, waveSeg, RateKnob(56), AmountKnob(56), PhaseKnob(56), MixKnob(56, 34))
            : CardLayout.Large(new[] { Loc.T("device.auto_pan.tab_motion") }, 0,
                _ => CardLayout.View(graphView, CardLayout.KnobColumn(AmountKnob(50), PhaseKnob(50)), CardLayout.Line(8, waveSeg, snaps)),
                _ => (CardLayout.Head(CardLayout.Title("LFO"), syncSeg), CardLayout.Rows(
                        new Border { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 3), Child = bigBlock },   // keeps its height
                        new Panel { Children = { rateRow, divRow } },
                        shapeRow,
                        meters)),
                extras,
                CardLayout.Output(null, MixKnob(58, 44), null),
                _ => RefreshAll());

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kTele);
            bool on = On();
            double gl = Sc(S_GainL, 1), gr = Sc(S_GainR, 1);
            graph.Set(WaveI(), P(Shape), P(Amount), P(Phase), P(Mix), Sc(S_WinPhase), gl, gr,
                scN > S_Sh0 + 3 ? scope.AsSpan(S_Sh0, 4) : ReadOnlySpan<float>.Empty, PeriodMs(), on);
            double pan = on ? Sc(S_Pan) : 0;
            panBar.Set(pan, on ? Sc(S_PanMin) : 0, on ? Sc(S_PanMax) : 0);
            panTxt.Text = PanF(pan);
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
