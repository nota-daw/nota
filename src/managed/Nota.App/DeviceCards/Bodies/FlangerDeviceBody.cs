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
// with the mode the feedback makes, and a status strip. The live τ comes from the engine
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
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

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

        RuleSection Island(Control head, Control body, double width = double.NaN)
        {
            var headBar = new Border
            {
                Height = 20, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 0, 3, 0), Child = head,
            };
            return new RuleSection
            {
                Width = width, Child = new DockPanel { LastChildFill = true, Children = { Docked(headBar, Dock.Top), body } },
            };
        }
        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        // A legend sample: a drawn 8px stroke and its word (never a typed dash).
        static Control LegendItem(string word, IBrush ink) => new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center,
            Children = { new Border { Width = 8, Height = 1.5, Background = ink, VerticalAlignment = VerticalAlignment.Center }, Mono(word, 7, ink) },
        };

        // ======================================================================
        // LEFT — LFO: waveform · Hz / Sync · the rate · Rate · Stereo
        // ======================================================================
        var syncSeg = Segments(new[] { "Hz", "Sync" }, () => Synced() ? 1 : 0, i => { SetP(Sync, i); RefreshAll(); }, out var syncSync, fill: true, padX: 0);
        syncSeg.Width = 76;
        readouts.Add(syncSync);
        Learn(syncSeg, Sync);
        ToolTip.SetTip(syncSeg, Loc.T("common.hz_runs_the_lfo_free"));

        var waveSeg = Segments(FlangerMath.Waves, WaveI, i => { SetP(Waveform, i / 2.0); RefreshAll(); }, out var waveSync, fill: true, padX: 0);
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
        var bigBlock = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { big, bigSub, period } };
        ToolTip.SetTip(bigBlock, Loc.T("common.the_lfo_rate_in_hz"));

        var rateRow = Slider("RATE", Rate, v => HzF(FlangerMath.FreeHz(v)) + " Hz", Loc.T("common.rate_the_free_lfo_rate"), 38, 46);
        var divRow = Slider("RATE", Division, v => FlangerMath.DivNames[FlangerMath.DivIndex(v)],
            Loc.T("device.flanger.rate_the_note_value_of"), 38, 46,
            snap: v => FlangerMath.DivNorm(FlangerMath.DivIndex(v)));
        readouts.Add(() => { rateRow.IsVisible = !Synced(); divRow.IsVisible = Synced(); });
        var stereoRow = Slider("STEREO", Stereo, v => NotaNum.F($"{v * 180:0}°"),
            Loc.T("device.flanger.stereo_the_right_lfo_against"), 38, 46,
            modulation: true, snap: v => Math.Round(v * 180) / 180);

        var leftBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 6, Margin = new Thickness(8, 6) };
        leftBody.Children.Add(waveSeg);
        leftBody.Children.Add(GRow(bigBlock, 1));
        leftBody.Children.Add(GRow(new Panel { Children = { rateRow, divRow } }, 2));
        leftBody.Children.Add(GRow(stereoRow, 3));
        var left = Island(HeadRow(Caps("LFO"), syncSeg), leftBody, 176);
        DockPanel.SetDock(left, Dock.Left);

        // ======================================================================
        // CENTRE — COMB RESPONSE, and τ over two cycles
        // ======================================================================
        var tauWord = Mono("", 7, TextTertiary);
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 5, 0),
            Children = { LegendItem("L", NotaPalette.VizInk), LegendItem("R", NotaPalette.TealBright), tauWord },
        };
        var comb = new FlangerCombView { Value = () => (P(Delay), P(Feedback)) };
        comb.GestureBegin += () => { Begin(Delay); Begin(Feedback); };
        comb.GestureEnd += () => { End(Delay); End(Feedback); };
        comb.Changed += (d, f) => { Raw(Delay, d); Raw(Feedback, f); RefreshAll(); };
        comb.ResetRequested += () => { Reset(Delay); Reset(Feedback); RefreshAll(); };
        Learn(comb, Delay);
        ToolTip.SetTip(comb, MenuKit.Keys(Loc.T("device.flanger.the_comb_the_delay_carves")));
        ValueEntry.AttachAll(comb, ValueEntry.DeviceParam(engine, track, di, Delay, "Delay", v => FlangerMath.Ms(FlangerMath.BaseMs(v)) + " ms", RefreshAll), ValueEntry.DeviceParam(engine, track, di, Feedback, "Feedback", v => FbF(FlangerMath.Fb(v)), RefreshAll));

        var sweep = new FlangerSweepView { Margin = new Thickness(0, 4, 0, 0) };
        ToolTip.SetTip(sweep, Loc.T("device.flanger.the_delay_of_both_channels"));
        var centreBody = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { Docked(sweep, Dock.Bottom), comb } };
        var centre = Island(HeadRow(Caps("COMB RESPONSE"), legend), centreBody);
        centre.Margin = new Thickness(5, 0);

        // ======================================================================
        // RIGHT — DELAY LINE: Delay · Depth · Feedback · Mix · snaps · notch readouts
        // ======================================================================
        var modeWord = Mono("", 7, TextSecondary);
        modeWord.Margin = new Thickness(0, 0, 5, 0);
        readouts.Add(() =>
        {
            string m = Mode();
            modeWord.Text = m;
            modeWord.Foreground = m == "resonant" ? TextPrimary : m == "negative" ? NotaPalette.TealBright : TextSecondary;
        });
        ToolTip.SetTip(modeWord, Loc.T("device.flanger.what_the_feedback_makes_positive"));

        var delayRow = Slider("DELAY", Delay, v => FlangerMath.Ms(FlangerMath.BaseMs(v)) + " ms", Loc.T("device.flanger.delay_the_centre_of_the"));
        var depthRow = Slider("DEPTH", Depth, v => PctF(v), Loc.T("device.flanger.depth_how_far_the_lfo"));
        var fbRow = Slider("FEEDBACK", Feedback, v => FbF(FlangerMath.Fb(v)),
            Loc.T("device.flanger.feedback_the_delayed_signal_fed"), modulation: true, bipolar: true,
            snap: v => FlangerMath.FbNorm(Math.Round(FlangerMath.Fb(v) * 100) / 100));
        var mixRow = Slider("MIX", Mix, v => PctF(v), Loc.T("device.flanger.mix_50_gives_the_deepest"));

        Border Snap(string text, double fb)
        {
            var tb = Mono(text, 7, TextTertiary); tb.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new Border
            {
                BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(0, 1),
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
        var snaps = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 1, Margin = new Thickness(46, 0, 0, 0) };
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
        var nullBar = new FlangerBar { Ink = NotaPalette.TealBright, VerticalAlignment = VerticalAlignment.Center };
        var nullVal = Mono("", 8, TextPrimary);
        var reads = new Border
        {
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

        var rightBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,*,Auto,*,Auto,*,Auto"), Margin = new Thickness(8, 6) };
        rightBody.Children.Add(delayRow);
        rightBody.Children.Add(GRow(depthRow, 2));
        rightBody.Children.Add(GRow(fbRow, 4));
        rightBody.Children.Add(GRow(mixRow, 6));
        rightBody.Children.Add(GRow(snaps, 8));
        rightBody.Children.Add(GRow(reads, 10));
        var right = Island(HeadRow(Caps("DELAY LINE"), modeWord), rightBody, 176);
        DockPanel.SetDock(right, Dock.Right);

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, TextSecondary);
        readouts.Add(() =>
        {
            double fb = Fb(), afb = Math.Abs(fb);
            bool warn = false;
            string foot;
            if (!On()) foot = Loc.T("device.flanger.flanger_bypassed_the_signal_passes");
            else if (P(Mix) < 0.01f) { foot = Loc.T("common.mix_at_zero_only_the"); warn = true; }
            else if (afb >= 0.85)
            {
                foot = "Feedback " + FbF(fb) + Loc.F("device.flanger.peaks_up_to_db_the", FlangerMath.PeakDb(P(Mix), fb));
                warn = true;
            }
            else if (P(Depth) < 0.01f) foot = Loc.T("device.flanger.depth_at_zero_the_comb");
            else
            {
                var (th, _) = FlangerMath.Notch(P(Mix), fb);
                double lo = th / (2 * Math.PI) / (TauMax() / 1000), hi = th / (2 * Math.PI) / (TauMin() / 1000);
                foot = "Notch sweeps " + FlangerMath.HzF(lo) + " ↔ " + FlangerMath.HzF(hi) + " Hz · τ "
                    + FlangerMath.Ms(TauMin()) + "–" + FlangerMath.Ms(TauMax()) + " ms";
            }
            statusLeft.Text = foot;
            statusLeft.Foreground = warn ? NotaPalette.Warning : TextSecondary;
            double sr = Sc(S_SampleRate);
            statusRight.Text = (sr > 0 ? NotaNum.F($"{sr / 1000:0.#} kHz · ") : "") + NotaNum.F($"{Bpm():0.##} BPM · ") + (Synced() ? "retrig bar" : "free run");
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { left, right, centre } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceCard, Children = { status, bodyRow } };

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
