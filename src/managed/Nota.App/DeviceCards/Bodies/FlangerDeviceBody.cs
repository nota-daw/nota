// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Flanger body (comb filter on a short modulated delay, device
// kind 23), a build of the "Nota Flanger" mockup (700 × 260) in the Level / EQ-8 / Orbit
// language: an LFO column (waveform, Hz or Sync, the rate as a big readout with its period, Rate
// and the Stereo phase offset between the channels), a centre panel with the live comb response
// of L and R (drag: the notch left / right = Delay, up / down = Feedback) and, under it, the delay
// τ of both channels over two cycles with a running head, a DELAY LINE column (Delay, Depth, the
// bipolar Feedback with its − / 0 / + snaps, Mix, and the first notch's sweep and null depth)
// with the mode the feedback makes. The live τ comes from the engine
// (Flanger.h scopeRead); the comb is drawn from the same formula the DSP runs. Every control is a
// device param (normalized 0..1), so automation / MIDI learn / presets / A-B / persistence come
// for free. FullBleed — the shared shell draws the header (name · preset · badge · bypass).

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

internal sealed class FlangerDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Flanger.h) ─────────────────────────────
    private const int Rate = 0, Delay = 1, Depth = 2, Feedback = 3, Mix = 4, Waveform = 5, Sync = 6, Division = 7, Stereo = 8;
    // Scope layout (Flanger::S_* / kTele).
    private const int S_TauL = 3, S_TauR = 4, S_WinPhase = 5, S_SampleRate = 10, kTele = 32;

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

        // ---- state and units (Flanger.h) --------------------------------------------------
        bool On() => !engine.DeviceBypassed(track, di);
        bool Synced() => P(Sync) >= 0.5f;
        int WaveI() => FlangerMath.WaveIndex(P(Waveform));
        int DivI() => FlangerMath.DivIndex(P(Division));
        double Bpm() => engine.Bpm > 0 ? engine.Bpm : 120;
        double RateHz() => Synced() ? Bpm() / 60 / FlangerMath.DivBeats[DivI()] : FlangerMath.FreeHz(P(Rate));
        double PeriodMs() => 1000 / Math.Max(1e-3, RateHz());
        double BaseMs() => FlangerMath.BaseMs(P(Delay));
        double Fb() => FlangerMath.Fb(P(Feedback));
        double TauMin() => BaseMs() * (1 - FlangerMath.DepthScale * P(Depth));
        double TauMax() => BaseMs() * (1 + FlangerMath.DepthScale * P(Depth));
        // τ now: the engine's, or the centre before the first block.
        double TauL() => Sc(S_TauL) > 0 ? Sc(S_TauL) : BaseMs();
        double TauR() => Sc(S_TauR) > 0 ? Sc(S_TauR) : BaseMs();
        string Mode()
        {
            if (!On()) return "bypass";
            double fb = Fb(), a = Math.Abs(fb);
            return a >= 0.85 ? "resonant" : fb <= -0.15 ? "negative" : fb >= 0.15 ? "positive" : "through";
        }

        static string HzF(double hz) => hz >= 10 ? NotaNum.F($"{hz:0.0}") : NotaNum.F($"{hz:0.00}");
        static string PctF(double v) => NotaNum.F($"{v * 100:0} %");
        static string FbF(double fb) => (fb > 0.005 ? "+" : fb < -0.005 ? "−" : "") + NotaNum.F($"{Math.Abs(fb) * 100:0} %");
        static string PeriodF(double ms) => ms >= 1000 ? NotaNum.F($"{ms / 1000:0.00} s") : NotaNum.F($"{ms:0} ms");

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null) => new()
        { Text = NotaText.Label(t), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Measure"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }

        // Slider row: caps label · track · mono value.
        Grid Slider(string label, int p, Func<double, string> fmt, string tip, double labelW = 46, double valueW = 36,
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

        var waveSeg = Segments(FlangerMath.Waves, WaveI, i => { SetP(Waveform, i / 2.0); RefreshAll(); }, out var waveSync, padX: 5);
        readouts.Add(waveSync);
        Learn(waveSeg, Waveform);
        ToolTip.SetTip(waveSeg, Loc.T("device.flanger.the_lfo_shape_that_sweeps"));

        var big = Mono("", 26, TextPrimary); big.FontWeight = FontWeight.Bold; big.LineHeight = 28;
        var bigSub = new TextBlock { FontSize = 8, Foreground = TextSecondary };
        var period = Mono("", 7, TextTertiary); period.Margin = new Thickness(0, 2, 0, 0);
        readouts.Add(() =>
        {
            double hz = RateHz();
            if (Synced())
            {
                big.Text = FlangerMath.DivNames[DivI()];
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

        var rateRow = Slider("RATE", Rate, v => HzF(FlangerMath.FreeHz(v)) + " Hz", Loc.T("common.rate_the_free_lfo_rate"), 38, 46);
        var divRow = Slider("RATE", Division, v => FlangerMath.DivNames[FlangerMath.DivIndex(v)],
            Loc.T("device.flanger.rate_the_note_value_of"), 38, 46,
            snap: v => FlangerMath.DivNorm(FlangerMath.DivIndex(v)));
        readouts.Add(() => { rateRow.IsVisible = !Synced(); divRow.IsVisible = Synced(); });
        var stereoRow = Slider("STEREO", Stereo, v => NotaNum.F($"{v * 180:0}°"),
            Loc.T("device.flanger.stereo_the_right_lfo_against"), 38, 46,
            modulation: true, snap: v => Math.Round(v * 180) / 180);

        // ======================================================================
        // CENTRE — COMB RESPONSE, and τ over two cycles
        // ======================================================================
        var tauWord = Mono("", 7, TextTertiary);
        var comb = new FlangerCombView { Value = () => (P(Delay), P(Feedback)) };
        comb.GestureBegin += () => { Begin(Delay); Begin(Feedback); };
        comb.GestureEnd += () => { End(Delay); End(Feedback); };
        comb.Changed += (d, f) => { Raw(Delay, d); Raw(Feedback, f); RefreshAll(); };
        comb.ResetRequested += () => { Reset(Delay); Reset(Feedback); RefreshAll(); };
        Learn(comb, Delay);
        ToolTip.SetTip(comb, MenuKit.Keys(Loc.T("device.flanger.the_comb_the_delay_carves")));
        ValueEntry.AttachAll(comb, ValueEntry.DeviceParam(engine, track, di, Delay, "Delay", v => FlangerMath.Ms(FlangerMath.BaseMs(v)) + " ms", RefreshAll), ValueEntry.DeviceParam(engine, track, di, Feedback, "Feedback", v => FbF(FlangerMath.Fb(v)), RefreshAll));

        var sweep = new FlangerSweepView { Margin = new Thickness(0, 4, 0, 0) };
        ToolTip.SetTip(sweep, Loc.T("device.flanger.the_delay_of_both_channels"));
        // The comb with τ over two cycles under it: one view.
        var combView = new DockPanel { LastChildFill = true, Children = { Docked(sweep, Dock.Bottom), comb } };

        // ======================================================================
        // RIGHT — DELAY LINE: Delay · Depth · Feedback · Mix · snaps · notch readouts
        // ======================================================================
        var modeWord = Mono("", 7, TextSecondary);
                readouts.Add(() =>
        {
            string m = Mode();
            modeWord.Text = m;
            modeWord.Foreground = m == "resonant" ? TextPrimary : m == "negative" ? DeviceCardKit.ModInkBright : TextSecondary;
        });
        ToolTip.SetTip(modeWord, Loc.T("device.flanger.what_the_feedback_makes_positive"));

        Border Snap(string text, double fb)
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
                SetP(Feedback, FlangerMath.FbNorm(fb)); RefreshAll(); e.Handled = true;
            };
            readouts.Add(() =>
            {
                bool on = Math.Abs(Fb() - fb) < 0.005;
                b.Background = on ? NotaPalette.TrackOff : Brushes.Transparent;
                tb.Foreground = on ? TextPrimary : TextTertiary;
            });
            Learn(b, Feedback);
            ToolTip.SetTip(b, fb < 0 ? Loc.T("device.flanger.feedback_70_hollow") : fb > 0 ? Loc.T("device.flanger.feedback_70_the_classic_jet") : Loc.T("device.flanger.feedback_0_a_plain_comb"));
            return b;
        }
        var snaps = new Grid { ColumnDefinitions = new ColumnDefinitions("26,26,26"), ColumnSpacing = 1, VerticalAlignment = VerticalAlignment.Center };
        snaps.Children.Add(Snap("−", -0.7));
        snaps.Children.Add(Col(Snap("0", 0), 1));
        snaps.Children.Add(Col(Snap("+", 0.7), 2));

        double PctAxis(double f) => Math.Clamp(Math.Log(Math.Max(f, 1e-3) / 20) / Math.Log(1000), 0, 1);
        Control ReadRow(string label, FlangerBar bar, TextBlock value, string tip)
        {
            value.TextAlignment = TextAlignment.Right; value.Width = 44;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("46,*,Auto") };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            g.Children.Add(Col(new Border { Margin = new Thickness(6, 0, 0, 0), Child = value }, 2));
            ToolTip.SetTip(g, tip);
            return g;
        }
        var notchBar = new FlangerBar { Ink = NotaPalette.BorderStrong, VerticalAlignment = VerticalAlignment.Center };
        var notchVal = Mono("", 8, TextPrimary);
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
                    ReadRow("NOTCH", notchBar, notchVal, Loc.T("device.flanger.the_first_notch_now_over")),
                    ReadRow("NULL", nullBar, nullVal, Loc.T("device.flanger.how_deep_the_notches_cut")),
                },
            },
        };

        // ======================================================================
        // Frames: L (the comb with Delay · Feedback · Depth beside it and the waveform and the
        // feedback snaps under it; the LFO panel; Dry/wet under it) · S (the comb over the
        // waveform and Rate · Delay · Feedback · Dry/wet)
        // ======================================================================
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() => { extras.Text = modeWord.Text + " · " + tauWord.Text + " · period " + PeriodF(PeriodMs()); });
        Control RateKnob(double cellW)
        {
            var free = K(Rate, "RATE", v => HzF(FlangerMath.FreeHz(v)) + "\u2009Hz", Loc.T("common.rate_the_free_lfo_rate"), cellW);
            var div = K(Division, "RATE", v => FlangerMath.DivNames[FlangerMath.DivIndex(v)], Loc.T("device.flanger.rate_the_note_value_of"), cellW,
                snap: v => FlangerMath.DivNorm(FlangerMath.DivIndex(v)));
            readouts.Add(() => { free.IsVisible = !Synced(); div.IsVisible = Synced(); });
            return new Panel { Children = { free, div } };
        }
        Control DelayKnob(double cellW) => K(Delay, "DELAY", v => FlangerMath.Ms(FlangerMath.BaseMs(v)) + "\u2009ms", Loc.T("device.flanger.delay_the_centre_of_the"), cellW);
        Control FbKnob(double cellW) => K(Feedback, "FEEDBACK", v => FbF(FlangerMath.Fb(v)), Loc.T("device.flanger.feedback_the_delayed_signal_fed"), cellW,
            snap: v => FlangerMath.FbNorm(Math.Round(FlangerMath.Fb(v) * 100) / 100), bipolar: true);
        Control MixKnob(double cellW, double size) => K(Mix, "DRY/WET", v => PctF(v), Loc.T("device.flanger.mix_50_gives_the_deepest"), cellW, size);
        Control root = CardLayout.IsMini(ctx, di)
            ? CardLayout.Mini(combView, waveSeg, RateKnob(56), DelayKnob(56), FbKnob(56), MixKnob(56, 34))
            : CardLayout.Large(new[] { Loc.T("device.flanger.tab_comb") }, 0,
                _ => CardLayout.View(combView, CardLayout.KnobColumn(DelayKnob(50), FbKnob(50), K(Depth, "DEPTH", v => PctF(v), Loc.T("device.flanger.depth_how_far_the_lfo"), 50)),
                    CardLayout.Line(8, waveSeg, snaps)),
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
            double tl = TauL(), tr = TauR(), fb = Fb(), mix = P(Mix);
            tauWord.Text = on ? "τL " + FlangerMath.Ms(tl) + " · τR " + FlangerMath.Ms(tr) + " ms" : "bypass";
            comb.Set(tl, tr, mix, fb, on);
            sweep.Set(BaseMs(), P(Depth), WaveI(), P(Stereo) * 0.5, Sc(S_WinPhase), tl, tr, PeriodMs(), on);

            var (th, nm) = FlangerMath.Notch(mix, fb);
            double nf = th / (2 * Math.PI) / (tl / 1000);
            double lo = th / (2 * Math.PI) / (TauMax() / 1000), hi = th / (2 * Math.PI) / (TauMin() / 1000);
            double nullDb = 20 * Math.Log10(Math.Max(nm, 1e-4));
            notchBar.Set(on ? PctAxis(lo) : 0, on ? PctAxis(hi) - PctAxis(lo) : 0);
            notchVal.Text = on ? FlangerMath.HzF(nf) + " Hz" : "—";
            nullBar.Set(0, on ? Math.Clamp(-nullDb / 36, 0, 1) : 0);
            nullVal.Text = on ? NotaNum.F($"{nullDb:0.0} dB") : "—";
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
