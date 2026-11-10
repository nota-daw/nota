// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Phaser body (a chain of LFO-swept all-pass stages, device kind
// 24), a build of the "Nota Phaser" mockup (700 × 260) in the Flanger / Orbit language: an LFO
// column (waveform, Hz or Sync, the rate as a big readout with its period, Rate and the Stereo phase
// offset between the channels), a centre panel with the live all-pass response of L and R with the
// notches ticked (drag: left / right = Center, up / down = Feedback) and, under it, the corner
// frequency of both channels over two cycles with a running head, a STAGES column (2 / 4 / 6 / 8 /
// 12, Center, Depth in octaves, the bipolar Feedback, Mix, and the first notch's sweep and null
// depth) with the mode the feedback makes. The live fc comes from the engine
// (Phaser.h scopeRead); the response is drawn from the same formula the DSP runs. Every control is
// a device param (normalized 0..1), so automation / MIDI learn / presets / A-B / persistence come
// for free. FullBleed — the shared shell draws the header (name · preset · badge · bypass).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class PhaserDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Phaser.h) ──────────────────────────────
    private const int Rate = 0, Center = 1, Depth = 2, Feedback = 3, Mix = 4, Waveform = 5, Sync = 6, Division = 7, Stereo = 8, Stages = 9;
    // Scope layout (Phaser::S_* / kTele).
    private const int S_FcL = 3, S_FcR = 4, S_WinPhase = 5, S_SampleRate = 10, kTele = 32;

    public double Width => 700;
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex);
    public bool FullBleed => true;
    public string? Subtitle => "Modulation";   // the processing type, shown as the header badge

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

        // ---- state and units (Phaser.h) ---------------------------------------------------
        bool On() => !engine.DeviceBypassed(track, di);
        bool Synced() => P(Sync) >= 0.5f;
        int WaveI() => PhaserMath.WaveIndex(P(Waveform));
        int DivI() => PhaserMath.DivIndex(P(Division));
        int StagesN() => PhaserMath.StagesOf(P(Stages));
        double Bpm() => engine.Bpm > 0 ? engine.Bpm : 120;
        double Sr() => Sc(S_SampleRate) > 0 ? Sc(S_SampleRate) : 48000;
        double RateHz() => Synced() ? Bpm() / 60 / PhaserMath.DivBeats[DivI()] : PhaserMath.FreeHz(P(Rate));
        double PeriodMs() => 1000 / Math.Max(1e-3, RateHz());
        double CenterHz() => PhaserMath.CenterHz(P(Center));
        double Fb() => PhaserMath.Fb(P(Feedback));
        double FcMin() => CenterHz() * Math.Pow(2, -PhaserMath.DepthOct * P(Depth));
        double FcMax() => CenterHz() * Math.Pow(2, PhaserMath.DepthOct * P(Depth));
        // fc now: the engine's, or the centre before the first block.
        double FcL() => Sc(S_FcL) > 0 ? Sc(S_FcL) : CenterHz();
        double FcR() => Sc(S_FcR) > 0 ? Sc(S_FcR) : CenterHz();
        string Mode()
        {
            if (!On()) return "bypass";
            double fb = Fb(), a = Math.Abs(fb);
            return a >= 0.85 ? "resonant" : fb <= -0.15 ? "negative" : fb >= 0.15 ? "positive" : "clean";
        }

        static string HzF(double hz) => hz >= 10 ? NotaNum.F($"{hz:0.0}") : NotaNum.F($"{hz:0.00}");
        static string PctF(double v) => NotaNum.F($"{v * 100:0} %");
        static string FbF(double fb) => (fb > 0.005 ? "+" : fb < -0.005 ? "−" : "") + NotaNum.F($"{Math.Abs(fb) * 100:0} %");
        static string PeriodF(double ms) => ms >= 1000 ? NotaNum.F($"{ms / 1000:0.00} s") : NotaNum.F($"{ms:0} ms");

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null) => new()
        { Text = NotaText.Label(t), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }

        // Slider row: caps label · track · mono value.
        Grid Slider(string label, int p, Func<double, string> fmt, string tip, double labelW = 44, double valueW = 44,
            bool modulation = false, bool bipolar = false, Func<double, double>? snap = null)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, snap is null ? v : snap(v)); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: valueW, modulation: modulation, bipolar: bipolar);
            readouts.Add(sync);
            Learn(bar, p);
            ToolTip.SetTip(bar, tip);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelW},*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            return g;
        }

        // Gauge knob over a normalized param (automation gesture, MIDI learn, live follow).
        Control K(int p, string name, Func<double, string> fmt, string tip, double cellW = 50, double size = 34, Func<double, double>? snap = null, bool bipolar = false)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Bipolar = bipolar, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
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
        // LEFT — LFO: waveform · Hz / Sync · the rate · Rate · Stereo
        // ======================================================================
        var syncSeg = Segments(new[] { "Hz", "Sync" }, () => Synced() ? 1 : 0, i => { SetP(Sync, i); RefreshAll(); }, out var syncSync, fill: true, padX: 0);
        syncSeg.Width = 76;
        readouts.Add(syncSync);
        Learn(syncSeg, Sync);
        ToolTip.SetTip(syncSeg, Loc.T("common.hz_runs_the_lfo_free"));

        var waveSeg = Segments(PhaserMath.Waves, WaveI, i => { SetP(Waveform, i / 2.0); RefreshAll(); }, out var waveSync, padX: 5);
        readouts.Add(waveSync);
        Learn(waveSeg, Waveform);
        ToolTip.SetTip(waveSeg, Loc.T("device.phaser.the_lfo_shape_that_sweeps"));

        var big = Mono("", 26, TextPrimary); big.FontWeight = FontWeight.Bold; big.LineHeight = 28;
        var bigSub = new TextBlock { FontSize = 8, Foreground = TextSecondary };
        var period = Mono("", 7, TextTertiary); period.Margin = new Thickness(0, 2, 0, 0);
        readouts.Add(() =>
        {
            double hz = RateHz();
            if (Synced())
            {
                big.Text = PhaserMath.DivNames[DivI()];
                bigSub.Text = NotaNum.F($"note · {Bpm():0.##} BPM");
                period.Text = HzF(hz) + " Hz · period " + PeriodF(PeriodMs());
            }
            else
            {
                big.Text = HzF(hz);
                bigSub.Text = "Hz · free";
                period.Text = "period " + PeriodF(PeriodMs());
            }
            big.Foreground = On() ? TextPrimary : NotaPalette.TextAxis;
        });
        var bigBlock = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { big, bigSub } };   // the period is in the tab bar
        ToolTip.SetTip(bigBlock, Loc.T("common.the_lfo_rate_in_hz"));

        var rateRow = Slider("RATE", Rate, v => HzF(PhaserMath.FreeHz(v)) + " Hz", Loc.T("common.rate_the_free_lfo_rate"), 38, 46);
        var divRow = Slider("RATE", Division, v => PhaserMath.DivNames[PhaserMath.DivIndex(v)],
            Loc.T("device.phaser.rate_the_note_value_of"), 38, 46,
            snap: v => PhaserMath.DivNorm(PhaserMath.DivIndex(v)));
        readouts.Add(() => { rateRow.IsVisible = !Synced(); divRow.IsVisible = Synced(); });
        var stereoRow = Slider("STEREO", Stereo, v => NotaNum.F($"{v * 180:0}°"),
            Loc.T("device.phaser.stereo_the_right_lfo_against"), 38, 46,
            modulation: true, snap: v => Math.Round(v * 180) / 180);

        // ======================================================================
        // CENTRE — ALL-PASS RESPONSE, and fc over two cycles
        // ======================================================================
        var fcWord = Mono("", 7, TextTertiary);
        var response = new PhaserResponseView { Value = () => (P(Center), P(Feedback)) };
        response.GestureBegin += () => { Begin(Center); Begin(Feedback); };
        response.GestureEnd += () => { End(Center); End(Feedback); };
        response.Changed += (c, f) => { Raw(Center, c); Raw(Feedback, f); RefreshAll(); };
        response.ResetRequested += () => { Reset(Center); Reset(Feedback); RefreshAll(); };
        Learn(response, Center);
        ToolTip.SetTip(response, MenuKit.Keys(Loc.T("device.phaser.the_notches_the_stages_cut")));
        ValueEntry.AttachAll(response, ValueEntry.DeviceParam(engine, track, di, Center, "Center", v => PhaserMath.HzF(PhaserMath.CenterHz(v)) + "\u2009Hz", RefreshAll), ValueEntry.DeviceParam(engine, track, di, Feedback, "Feedback", v => FbF(PhaserMath.Fb(v)), RefreshAll));

        var sweep = new PhaserSweepView { Margin = new Thickness(0, 4, 0, 0) };
        ToolTip.SetTip(sweep, Loc.T("device.phaser.the_corner_frequency_of_both"));
        // The response with fc over two cycles under it: one view.
        var responseView = new DockPanel { LastChildFill = true, Children = { Docked(sweep, Dock.Bottom), response } };

        // ======================================================================
        // RIGHT — STAGES: 2…12 · Center · Depth · Feedback · Mix · sweep / null readouts
        // ======================================================================
        var modeWord = Mono("", 7, TextSecondary);
                readouts.Add(() =>
        {
            string m = Mode();
            modeWord.Text = m;
            modeWord.Foreground = m == "resonant" ? TextPrimary : m == "negative" ? DeviceCardKit.ModInkBright : TextSecondary;
        });
        ToolTip.SetTip(modeWord, Loc.T("device.phaser.what_the_feedback_makes_positive"));

        var stageSeg = Segments(PhaserMath.StageCounts.Select(n => n.ToString()).ToArray(), () => PhaserMath.StageIndex(P(Stages)),
            i => { SetP(Stages, i / (double)(PhaserMath.StageCounts.Length - 1)); RefreshAll(); }, out var stageSync, padX: 5);
        foreach (var tb in stageSeg.GetLogicalDescendants().OfType<TextBlock>()) tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI");
        readouts.Add(stageSync);
        Learn(stageSeg, Stages);
        ToolTip.SetTip(stageSeg, Loc.T("device.phaser.all_pass_stages_every_two"));

        double PctAxis(double f) => Math.Clamp(Math.Log(Math.Max(f, 1e-3) / 20) / Math.Log(1000), 0, 1);
        Control ReadRow(string label, FlangerBar bar, TextBlock value, string tip)
        {
            value.TextAlignment = TextAlignment.Right; value.Width = 44;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*,Auto") };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            g.Children.Add(Col(new Border { Margin = new Thickness(6, 0, 0, 0), Child = value }, 2));
            ToolTip.SetTip(g, tip);
            return g;
        }
        var sweepBar = new FlangerBar { Ink = NotaPalette.BorderStrong, VerticalAlignment = VerticalAlignment.Center };
        var sweepVal = Mono("", 8, TextPrimary);
        var nullBar = new FlangerBar { Ink = DeviceCardKit.ModInkBright, VerticalAlignment = VerticalAlignment.Center };
        var nullVal = Mono("", 8, TextPrimary);
        var reads = new Border
        {
            Background = Brushes.Transparent,   // a fixed-height row in the panel
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0),
            Child = new StackPanel
            {
                Spacing = 4, Children =
                {
                    ReadRow("SWEEP", sweepBar, sweepVal, Loc.T("device.phaser.the_range_the_corner_sweeps")),
                    ReadRow("NULL", nullBar, nullVal, Loc.T("device.phaser.how_deep_the_notches_cut")),
                },
            },
        };

        // ======================================================================
        // Frames: L (the response with Center · Feedback · Depth beside it and the waveform and
        // the stages under it; the LFO panel; Dry/wet under it) · S (the response over the stages
        // and Rate · Center · Feedback · Dry/wet)
        // ======================================================================
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() => { extras.Text = modeWord.Text + " · " + fcWord.Text + " · period " + PeriodF(PeriodMs()); });
        Control RateKnob(double cellW)
        {
            var free = K(Rate, "RATE", v => HzF(PhaserMath.FreeHz(v)) + "\u2009Hz", Loc.T("common.rate_the_free_lfo_rate"), cellW);
            var div = K(Division, "RATE", v => PhaserMath.DivNames[PhaserMath.DivIndex(v)], Loc.T("device.phaser.rate_the_note_value_of"), cellW,
                snap: v => PhaserMath.DivNorm(PhaserMath.DivIndex(v)));
            readouts.Add(() => { free.IsVisible = !Synced(); div.IsVisible = Synced(); });
            return new Panel { Children = { free, div } };
        }
        Control CenterKnob(double cellW) => K(Center, "CENTER", v => PhaserMath.HzF(PhaserMath.CenterHz(v)) + "\u2009Hz", Loc.T("device.phaser.center_the_middle_of_the"), cellW);
        Control FbKnob(double cellW) => K(Feedback, "FEEDBACK", v => FbF(PhaserMath.Fb(v)), Loc.T("device.phaser.feedback_the_chains_output_fed"), cellW,
            snap: v => PhaserMath.FbNorm(Math.Round(PhaserMath.Fb(v) * 100) / 100), bipolar: true);
        Control MixKnob(double cellW, double size) => K(Mix, "DRY/WET", v => PctF(v), Loc.T("device.phaser.mix_50_gives_the_deepest"), cellW, size);
        Control root = CardLayout.IsMini(ctx, di)
            ? CardLayout.Mini(responseView, stageSeg, RateKnob(56), CenterKnob(56), FbKnob(56), MixKnob(56, 34))
            : CardLayout.Large(new[] { Loc.T("device.phaser.tab_response") }, 0,
                _ => CardLayout.View(responseView, CardLayout.KnobColumn(CenterKnob(50), FbKnob(50),
                        K(Depth, "DEPTH", v => NotaNum.F($"±{v * PhaserMath.DepthOct:0.0}\u2009oct"), Loc.T("device.phaser.depth_how_far_the_lfo"), 50)),
                    CardLayout.Line(8, waveSeg, stageSeg)),
                _ => (CardLayout.Head(CardLayout.Title("LFO"), syncSeg), CardLayout.Rows(
                        new Border { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 3), Child = bigBlock },
                        new Panel { Children = { rateRow, divRow } },
                        stereoRow,
                        reads)),
                extras,
                CardLayout.Output(null, MixKnob(58, 44), null),
                _ => RefreshAll());

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kTele);
            bool on = On();
            double fl = FcL(), fr = FcR(), fb = Fb(), mix = P(Mix), sr = Sr();
            int n = StagesN();
            fcWord.Text = on ? "fcL " + PhaserMath.HzF(fl) + " · fcR " + PhaserMath.HzF(fr) + " Hz" : "bypass";
            response.Set(fl, fr, n, mix, fb, sr, on);
            sweep.Set(CenterHz(), P(Depth), WaveI(), P(Stereo) * 0.5, Sc(S_WinPhase), fl, fr, PeriodMs(), on);

            double nf = PhaserMath.FirstNotch(fl, n, fb, sr);
            double nullDb = PhaserMath.NullDb(mix, fb);
            sweepBar.Set(on ? PctAxis(FcMin()) : 0, on ? PctAxis(FcMax()) - PctAxis(FcMin()) : 0);
            sweepVal.Text = on && nf > 0 ? PhaserMath.HzF(nf) + " Hz" : "—";
            nullBar.Set(0, on ? Math.Clamp(-nullDb / 36, 0, 1) : 0);
            nullVal.Text = on ? NotaNum.F($"{nullDb:0.0} dB") : "—";
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
