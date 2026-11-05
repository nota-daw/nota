// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Chorus body (modulated delay voices, device kind 25), a build
// of the "Nota Chorus" mockup (700 × 260) in the Flanger / Phaser language: an LFO column (Hz or
// Sync, the mode — Classic / Ensemble / Vibrato — the rate as a big readout with its period, Rate,
// the Offset between the channels and the HPF on the wet input), a centre panel with every voice
// over two cycles — its delay τ or its detune in cents (τ / ct) — with a running head (drag up /
// down for Amount) and, under it, the voices' stereo field, and a VOICE column (Amount, the bipolar
// Feedback, Width, Warmth, Mix, and the detune and delay ranges) with the state it is in, and a
// status strip. In Ensemble the Offset is fixed (3 × 120°), in Vibrato the Mix (wet only). The
// live LFO phase comes from the engine (Chorus.h scopeRead); the curves are drawn from the same
// formulas the DSP runs. Every control is a device param (normalized 0..1), so automation / MIDI
// learn / presets / A-B / persistence come for free. FullBleed — the shared shell draws the header
// (name · preset · badge · bypass).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class ChorusDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Chorus.h) ──────────────────────────────
    private const int Rate = 0, Mode = 1, Sync = 2, Division = 3, Offset = 4, Hpf = 5, Amount = 6, Feedback = 7, Spread = 8, Warmth = 9, Mix = 10;
    // Scope layout (Chorus::S_* / kTele).
    private const int S_Cents1 = 6, S_WinPhase = 9, S_SampleRate = 14, kTele = 32;

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

        // ---- state and units (Chorus.h) --------------------------------------------------
        bool On() => !engine.DeviceBypassed(track, di);
        bool Synced() => P(Sync) >= 0.5f;
        int ModeI() => ChorusMath.ModeIndex(P(Mode));
        bool Vib() => ModeI() == 2;
        bool Ens() => ModeI() == 1;
        int DivI() => ChorusMath.DivIndex(P(Division));
        double Bpm() => engine.Bpm > 0 ? engine.Bpm : 120;
        double RateHz() => Synced() ? Bpm() / 60 / ChorusMath.DivBeats[DivI()] : ChorusMath.FreeHz(P(Rate));
        double PeriodMs() => 1000 / Math.Max(1e-3, RateHz());
        double Dep() => ChorusMath.Dep(ModeI(), P(Amount));
        double TauMin() => ChorusMath.BaseMs[ModeI()] - Dep();
        double TauMax() => ChorusMath.BaseMs[ModeI()] + Dep();
        double MaxCt() => ChorusMath.MaxCents(Dep(), RateHz());
        double Fb() => ChorusMath.Fb(P(Feedback));
        double WidthPct() => ChorusMath.WidthPct(P(Spread));
        string State()
        {
            if (!On()) return "bypass";
            if (Vib()) return "wet only";
            if (Math.Abs(Fb()) >= 0.6) return "resonant";
            return Ens() ? "3 voices" : "2 voices";
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
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

        // Slider row: caps label · track · mono value. `locked` greys it out and stops the hand
        // (Offset in Ensemble, Mix in Vibrato); `ink` recolours the fill (Warmth in rose).
        Grid Slider(string label, int p, Func<double, string> fmt, string tip, double labelW, double valueW,
            bool modulation = false, bool bipolar = false, Func<double, double>? snap = null, Func<bool>? locked = null, IBrush? ink = null)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, snap is null ? v : snap(v)); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: valueW, modulation: modulation, bipolar: bipolar,
                dim: locked);
            if (ink is not null) foreach (var t in bar.Children.OfType<SliderTrack>()) t.Ink = ink;
            var lbl = Caps(label);
            readouts.Add(sync);
            if (locked is not null)
                readouts.Add(() => { bool l = locked(); bar.IsHitTestVisible = !l; lbl.Foreground = l ? TextDisabled : TextTertiary; });
            Learn(bar, p);
            ToolTip.SetTip(bar, tip);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelW},*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(lbl);
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
        static Control LegendItem(TextBlock word, Border stroke) => new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center,
            Children = { stroke, word },
        };

        // ======================================================================
        // LEFT — LFO: Hz / Sync · mode · the rate · Rate · Offset · HPF
        // ======================================================================
        var syncSeg = Segments(new[] { "Hz", "Sync" }, () => Synced() ? 1 : 0, i => { SetP(Sync, i); RefreshAll(); }, out var syncSync, fill: true, padX: 0);
        syncSeg.Width = 76;
        readouts.Add(syncSync);
        Learn(syncSeg, Sync);
        ToolTip.SetTip(syncSeg, Loc.T("common.hz_runs_the_lfo_free"));

        var modeSeg = Segments(ChorusMath.Modes, ModeI, i => { SetP(Mode, i / 2.0); RefreshAll(); }, out var modeSync, fill: true, padX: 0);
        readouts.Add(modeSync);
        Learn(modeSeg, Mode);
        ToolTip.SetTip(modeSeg, Loc.T("device.chorus.classic_two_voices_left_and"));

        var big = Mono("", 26, TextPrimary); big.FontWeight = FontWeight.Bold; big.LineHeight = 28;
        var bigSub = new TextBlock { FontSize = 8, Foreground = TextSecondary };
        var period = Mono("", 7, TextTertiary); period.Margin = new Thickness(0, 2, 0, 0);
        readouts.Add(() =>
        {
            double hz = RateHz();
            if (Synced())
            {
                big.Text = ChorusMath.DivNames[DivI()];
                bigSub.Text = NotaNum.F($"note · {Bpm():0.##} BPM");
                period.Text = HzF(hz) + " Hz · period " + PeriodF(PeriodMs());
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

        var rateRow = Slider("RATE", Rate, v => HzF(ChorusMath.FreeHz(v)) + " Hz", Loc.T("common.rate_the_free_lfo_rate"), 38, 46);
        var divRow = Slider("RATE", Division, v => ChorusMath.DivNames[ChorusMath.DivIndex(v)],
            Loc.T("device.chorus.rate_the_note_value_of"), 38, 46,
            snap: v => ChorusMath.DivNorm(ChorusMath.DivIndex(v)));
        readouts.Add(() => { rateRow.IsVisible = !Synced(); divRow.IsVisible = Synced(); });
        var offsetRow = Slider("OFFSET", Offset, v => Ens() ? "3 × 120°" : NotaNum.F($"{v * 180:0}°"),
            Loc.T("device.chorus.offset_the_right_voices_lfo"),
            38, 46, modulation: true, snap: v => Math.Round(v * 180) / 180, locked: Ens);
        var hpfRow = Slider("HPF", Hpf, v => ChorusMath.HpHz(v) <= 0 ? "off" : ChorusMath.HzF(ChorusMath.HpHz(v)) + " Hz",
            Loc.T("device.chorus.hpf_a_high_pass_on"), 38, 46,
            modulation: true, snap: v => Math.Round(v / 0.005) * 0.005);

        var leftBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"), RowSpacing = 6, Margin = new Thickness(8, 6) };
        leftBody.Children.Add(modeSeg);
        leftBody.Children.Add(GRow(bigBlock, 1));
        leftBody.Children.Add(GRow(new Panel { Children = { rateRow, divRow } }, 2));
        leftBody.Children.Add(GRow(offsetRow, 3));
        leftBody.Children.Add(GRow(hpfRow, 4));
        var left = Island(HeadRow(Caps("LFO"), syncSeg), leftBody, 176);
        DockPanel.SetDock(left, Dock.Left);

        // ======================================================================
        // CENTRE — VOICES over two cycles (τ / ct), and the stereo field
        // ======================================================================
        // τ or ct: a view choice, not a param. Until the hand picks one, Vibrato shows cents.
        bool? viewCents = null;
        bool Cents() => viewCents ?? Vib();

        var legendWords = new TextBlock[3];
        var legendStrokes = new Border[3];
        var legendItems = new Control[3];
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 3; i++)
        {
            legendWords[i] = Mono("", 7, ChorusMath.VoiceInk(i));
            legendStrokes[i] = new Border { Width = 8, Height = 1.5, Background = ChorusMath.VoiceInk(i), VerticalAlignment = VerticalAlignment.Center };
            legendItems[i] = LegendItem(legendWords[i], legendStrokes[i]);
            legend.Children.Add(legendItems[i]);
        }
        readouts.Add(() =>
        {
            var vs = ChorusMath.Voices(ModeI(), 0);
            for (int i = 0; i < 3; i++)
            {
                legendItems[i].IsVisible = i < vs.Length;
                if (i < vs.Length) legendWords[i].Text = vs[i].Name;
            }
        });
        var plotRead = Mono("", 7, TextTertiary);
        plotRead.Margin = new Thickness(0, 0, 5, 0);
        readouts.Add(() => plotRead.Text = Cents()
            ? "pitch ±" + ChorusMath.Ct(MaxCt()) + " ct"
            : "τ " + ChorusMath.Ms(TauMin()) + "–" + ChorusMath.Ms(TauMax()) + " ms");
        var viewSeg = Segments(new[] { "τ", "ct" }, () => Cents() ? 1 : 0, i => { viewCents = i == 1; RefreshAll(); }, out var viewSync, fill: true, padX: 0);
        viewSeg.Width = 52;
        readouts.Add(viewSync);
        ToolTip.SetTip(viewSeg, Loc.T("device.chorus.draws_each_voices_delay_ct"));
        var headRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { plotRead, viewSeg } };
        var centreHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        centreHead.Children.Add(Caps("VOICES"));
        centreHead.Children.Add(Col(legend, 1));
        centreHead.Children.Add(Col(headRight, 3));

        var plot = new ChorusVoicesView { Value = () => P(Amount) };
        plot.GestureBegin += () => Begin(Amount);
        plot.GestureEnd += () => End(Amount);
        plot.Changed += a => { Raw(Amount, Math.Round(a * 100) / 100); RefreshAll(); };
        plot.ResetRequested += () => { Reset(Amount); RefreshAll(); };
        Learn(plot, Amount);
        ToolTip.SetTip(plot, MenuKit.Keys(Loc.T("device.chorus.each_voice_over_two_lfo")));
        ValueEntry.AttachAll(plot, ValueEntry.DeviceParam(engine, track, di, Amount, "Amount", PctF, RefreshAll));

        var field = new ChorusFieldView { Margin = new Thickness(0, 4, 0, 0) };
        ToolTip.SetTip(field, Loc.T("device.chorus.where_the_voices_sit_in"));
        var centreBody = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { Docked(field, Dock.Bottom), plot } };
        var centre = Island(centreHead, centreBody);
        centre.Margin = new Thickness(5, 0);

        // ======================================================================
        // RIGHT — VOICE: Amount · Feedback · Width · Warmth · Mix · detune / delay readouts
        // ======================================================================
        var stateWord = Mono("", 7, TextSecondary);
        stateWord.Margin = new Thickness(0, 0, 5, 0);
        readouts.Add(() =>
        {
            string s = State();
            stateWord.Text = s;
            stateWord.Foreground = s == "resonant" ? TextPrimary : s == "bypass" ? NotaPalette.TextAxis : TextSecondary;
        });
        ToolTip.SetTip(stateWord, Loc.T("device.chorus.how_many_voices_sing_or"));

        const double LW = 46, VW = 36;
        var amountRow = Slider("AMOUNT", Amount, v => PctF(v), Loc.T("device.chorus.amount_how_far_the_lfo"), LW, VW,
            snap: v => Math.Round(v * 100) / 100);
        var fbRow = Slider("FEEDBACK", Feedback, v => FbF(ChorusMath.Fb(v)),
            Loc.T("device.chorus.feedback_the_voices_fed_back"), LW, VW,
            modulation: true, bipolar: true, snap: v => ChorusMath.FbNorm(Math.Round(ChorusMath.Fb(v) * 100) / 100));
        var widthRow = Slider("WIDTH", Spread, v => NotaNum.F($"{ChorusMath.WidthPct(v):0} %"),
            Loc.T("device.chorus.width_the_voices_stereo_spread"), LW, VW,
            modulation: true, snap: v => Math.Round(v * 200) / 200);
        var warmRow = Slider("WARMTH", Warmth, v => PctF(v),
            Loc.T("device.chorus.warmth_a_bucket_brigade_flavour"), LW, VW,
            snap: v => Math.Round(v * 100) / 100, ink: DeviceCardKit.ModInkBright);
        var mixRow = Slider("MIX", Mix, v => Vib() ? "wet" : PctF(v), Loc.T("device.chorus.mix_0_dry_50_dry"), LW, VW,
            snap: v => Math.Round(v * 100) / 100, locked: Vib);

        Control ReadRow(string label, FlangerBar bar, TextBlock value, string tip)
        {
            value.TextAlignment = TextAlignment.Right; value.Width = 44;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LW},*,Auto") };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            g.Children.Add(Col(new Border { Margin = new Thickness(6, 0, 0, 0), Child = value }, 2));
            ToolTip.SetTip(g, tip);
            return g;
        }
        var detuneBar = new FlangerBar { Ink = NotaPalette.VizInk, VerticalAlignment = VerticalAlignment.Center };
        var detuneVal = Mono("", 8, TextPrimary);
        var delayBar = new FlangerBar { Ink = NotaPalette.BorderStrong, VerticalAlignment = VerticalAlignment.Center };
        var delayVal = Mono("", 8, TextPrimary);
        var reads = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0),
            Child = new StackPanel
            {
                Spacing = 4, Children =
                {
                    ReadRow("DETUNE", detuneBar, detuneVal, Loc.T("device.chorus.the_largest_detune_the_sweep")),
                    ReadRow("DELAY", delayBar, delayVal, Loc.T("device.chorus.the_range_each_voices_delay")),
                },
            },
        };

        var rightBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,*,Auto,*,Auto,*,Auto"), Margin = new Thickness(8, 6) };
        rightBody.Children.Add(amountRow);
        rightBody.Children.Add(GRow(fbRow, 2));
        rightBody.Children.Add(GRow(widthRow, 4));
        rightBody.Children.Add(GRow(warmRow, 6));
        rightBody.Children.Add(GRow(mixRow, 8));
        rightBody.Children.Add(GRow(reads, 10));
        var right = Island(HeadRow(Caps("VOICE"), stateWord), rightBody, 176);
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
            if (!On()) foot = Loc.T("device.chorus.chorus_bypassed_the_signal_passes");
            else if (!Vib() && P(Mix) < 0.01f) { foot = Loc.T("common.mix_at_zero_only_the"); warn = true; }
            else if (P(Amount) < 0.01f) foot = Loc.T("device.chorus.amount_at_zero_the_voices") + ChorusMath.Ms(ChorusMath.BaseMs[ModeI()]) + " ms delay remains";
            else if (afb >= 0.6) { foot = "Feedback " + FbF(fb) + ": a metallic ring creeps in, like a flanger"; warn = true; }
            else if (Vib()) foot = Loc.T("device.chorus.vibrato_no_dry_signal_the") + ChorusMath.Ct(MaxCt()) + " ct at " + HzF(RateHz()) + " Hz";
            else if (WidthPct() < 1) foot = Loc.T("device.chorus.width_0_the_voices_fold");
            else
                foot = Loc.T("device.chorus.detune_up_to") + ChorusMath.Ct(MaxCt()) + " ct · τ " + ChorusMath.Ms(TauMin()) + "–" + ChorusMath.Ms(TauMax())
                    + " ms · " + (Ens() ? "3" : "2") + " voices";
            statusLeft.Text = foot;
            statusLeft.Foreground = warn ? NotaPalette.Warning : TextSecondary;
            double sr = Sc(S_SampleRate);
            statusRight.Text = (sr > 0 ? NotaNum.F($"{sr / 1000:0.#} kHz · ") : "") + NotaNum.F($"{Bpm():0.##} BPM · ")
                + (P(Warmth) > 0.01f ? NotaNum.F($"warm {P(Warmth) * 100:0} %") : "clean");
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { left, right, centre } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceCard, Children = { status, bodyRow } };

        var centsNow = new double[3];
        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kTele);
            bool on = On();
            int mode = ModeI();
            double dep = Dep(), rate = RateHz(), mc = MaxCt(), off = P(Offset) * 0.5;
            double head = Sc(S_WinPhase);
            var vs = ChorusMath.Voices(mode, off);
            // The detune now: the engine's, or the formula at the head before the first block.
            for (int i = 0; i < 3; i++)
                centsNow[i] = i >= vs.Length ? 0 : scN > S_Cents1 + i && scope[S_Cents1 + i] != 0 ? scope[S_Cents1 + i]
                    : ChorusMath.Cents(dep, rate, head + vs[i].Phase);
            plot.Set(mode, dep, off, rate, head, PeriodMs(), Cents(), on);
            field.Set(mode, off, WidthPct(), centsNow, ChorusMath.CentsRange(mc), on);

            detuneBar.Set(0, on ? Math.Clamp(mc / 60, 0, 1) : 0);
            detuneVal.Text = on ? "±" + ChorusMath.Ct(mc) + " ct" : "—";
            delayBar.Set(on ? TauMin() / 24 : 0, on ? (TauMax() - TauMin()) / 24 : 0);
            delayVal.Text = on ? ChorusMath.Ms(TauMin()) + "–" + ChorusMath.Ms(TauMax()) : "—";
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
