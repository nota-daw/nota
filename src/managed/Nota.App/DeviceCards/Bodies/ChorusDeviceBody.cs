// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Chorus body (modulated delay voices, device kind 25), a build
// of the "Nota Chorus" mockup (700 × 260) in the Flanger / Phaser language: an LFO column (Hz or
// Sync, the mode — Classic / Ensemble / Vibrato — the rate as a big readout with its period, Rate,
// the Offset between the channels and the HPF on the wet input), a centre panel with every voice
// over two cycles — its delay τ or its detune in cents (τ / ct) — with a running head (drag up /
// down for Amount) and, under it, the voices' stereo field, and a VOICE column (Amount, the bipolar
// Feedback, Width, Warmth, Mix, and the detune and delay ranges) with the state it is in. In
// Ensemble the Offset is fixed (3 × 120°), in Vibrato the Mix (wet only). The
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
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Measure"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }

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

        // Gauge knob over a normalized param; `locked` dims it and stops the hand (Offset in Ensemble).
        Control K(int p, string name, Func<double, string> fmt, string tip, double cellW = 50, double size = 34, Func<double, double>? snap = null,
            Func<bool>? locked = null, bool bipolar = false)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Bipolar = bipolar, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.Format = fmt;
            knob.ValueChanged += v => { Raw(p, snap is null ? v : snap(v)); RefreshAll(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            ToolTip.SetTip(knob, tip);
            readouts.Add(() =>
            {
                if (locked is not null) { bool l = locked(); knob.IsDim = l; knob.IsHitTestVisible = !l; }
                if (!knob.Dragging) { double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                val.Text = fmt(P(p));
            });
            return KnobCell(name, knob, val, cellW);
        }

        // A legend sample: a drawn 8px stroke and its word (never a typed dash).

        // ======================================================================
        // LEFT — LFO: Hz / Sync · mode · the rate · Rate · Offset · HPF
        // ======================================================================
        var syncSeg = Segments(new[] { "Hz", "Sync" }, () => Synced() ? 1 : 0, i => { SetP(Sync, i); RefreshAll(); }, out var syncSync, fill: true, padX: 0);
        syncSeg.Width = 76;
        readouts.Add(syncSync);
        Learn(syncSeg, Sync);
        ToolTip.SetTip(syncSeg, Loc.T("common.hz_runs_the_lfo_free"));

        var hpfRow = Slider("HPF", Hpf, v => ChorusMath.HpHz(v) <= 0 ? "off" : ChorusMath.HzF(ChorusMath.HpHz(v)) + " Hz",
            Loc.T("device.chorus.hpf_a_high_pass_on"), 38, 46,
            modulation: true, snap: v => Math.Round(v / 0.005) * 0.005);

        // ======================================================================
        // CENTRE — VOICES over two cycles (τ / ct), and the stereo field
        // ======================================================================
        // τ or ct: a view choice, not a param. Until the hand picks one, Vibrato shows cents.
        bool? viewCents = null;
        bool Cents() => viewCents ?? Vib();

        var plotRead = Mono("", 7, TextTertiary);
        plotRead.Margin = new Thickness(0, 0, 5, 0);
        readouts.Add(() => plotRead.Text = Cents()
            ? "pitch ±" + ChorusMath.Ct(MaxCt()) + " ct"
            : "τ " + ChorusMath.Ms(TauMin()) + "–" + ChorusMath.Ms(TauMax()) + " ms");
        var viewSeg = Segments(new[] { "τ", "ct" }, () => Cents() ? 1 : 0, i => { viewCents = i == 1; RefreshAll(); }, out var viewSync, fill: true, padX: 0);
        viewSeg.Width = 52;
        readouts.Add(viewSync);
        ToolTip.SetTip(viewSeg, Loc.T("device.chorus.draws_each_voices_delay_ct"));

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
        // The voices over two cycles with their stereo field under them: one view.
        var voicesView = new DockPanel { LastChildFill = true, Children = { Docked(field, Dock.Bottom), plot } };

        // ======================================================================
        // RIGHT — VOICE: Amount · Feedback · Width · Warmth · Mix · detune / delay readouts
        // ======================================================================
        var stateWord = Mono("", 7, TextSecondary);
                readouts.Add(() =>
        {
            string s = State();
            stateWord.Text = s;
            stateWord.Foreground = s == "resonant" ? TextPrimary : s == "bypass" ? NotaPalette.TextAxis : TextSecondary;
        });
        ToolTip.SetTip(stateWord, Loc.T("device.chorus.how_many_voices_sing_or"));

        const double LW = 46, VW = 36;
        var fbRow = Slider("FEEDBACK", Feedback, v => FbF(ChorusMath.Fb(v)),
            Loc.T("device.chorus.feedback_the_voices_fed_back"), LW, VW,
            modulation: true, bipolar: true, snap: v => ChorusMath.FbNorm(Math.Round(ChorusMath.Fb(v) * 100) / 100));
        var widthRow = Slider("WIDTH", Spread, v => NotaNum.F($"{ChorusMath.WidthPct(v):0} %"),
            Loc.T("device.chorus.width_the_voices_stereo_spread"), LW, VW,
            modulation: true, snap: v => Math.Round(v * 200) / 200);
        var warmRow = Slider("WARMTH", Warmth, v => PctF(v),
            Loc.T("device.chorus.warmth_a_bucket_brigade_flavour"), LW, VW,
            snap: v => Math.Round(v * 100) / 100, ink: DeviceCardKit.ModInkBright);
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

        // ======================================================================
        // Frames: L (the voices with Amount · Rate · Offset beside them and the mode, Hz / Sync and
        // τ / ct under them; the Voice panel; Dry/wet under it) · S (the voices over the mode and
        // Rate · Amount · Width · Dry/wet)
        // ======================================================================
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() => extras.Text = plotRead.Text + " · period " + PeriodF(PeriodMs()));
        Control RateKnob(double cellW)
        {
            var free = K(Rate, "RATE", v => HzF(ChorusMath.FreeHz(v)) + "\u2009Hz", Loc.T("common.rate_the_free_lfo_rate"), cellW);
            var div = K(Division, "RATE", v => ChorusMath.DivNames[ChorusMath.DivIndex(v)], Loc.T("device.chorus.rate_the_note_value_of"), cellW,
                snap: v => ChorusMath.DivNorm(ChorusMath.DivIndex(v)));
            readouts.Add(() => { free.IsVisible = !Synced(); div.IsVisible = Synced(); });
            return new Panel { Children = { free, div } };
        }
        Control AmountKnob(double cellW) => K(Amount, "AMOUNT", v => PctF(v), Loc.T("device.chorus.amount_how_far_the_lfo"), cellW, snap: v => Math.Round(v * 100) / 100);
        Control MixKnob(double cellW, double size) => K(Mix, "DRY/WET", v => Vib() ? "wet" : PctF(v), Loc.T("device.chorus.mix_0_dry_50_dry"), cellW, size,
            snap: v => Math.Round(v * 100) / 100, locked: Vib);
        Control OffsetKnob() => K(Offset, "OFFSET", v => Ens() ? "3 × 120°" : NotaNum.F($"{v * 180:0}°"), Loc.T("device.chorus.offset_the_right_voices_lfo"), 50,
            snap: v => Math.Round(v * 180) / 180, locked: Ens);
        var modeSel = Segments(ChorusMath.Modes, ModeI, i => { SetP(Mode, i / 2.0); RefreshAll(); }, out var modeSelSync, padX: 5);
        readouts.Add(modeSelSync);
        Learn(modeSel, Mode);
        ToolTip.SetTip(modeSel, Loc.T("device.chorus.classic_two_voices_left_and"));
        reads.Background = Brushes.Transparent;   // a fixed-height row in the panel
        Control root = CardLayout.IsMini(ctx, di)
            ? CardLayout.Mini(voicesView, modeSel, RateKnob(56), AmountKnob(56),
                K(Spread, "WIDTH", v => NotaNum.F($"{ChorusMath.WidthPct(v):0}\u2009%"), Loc.T("device.chorus.width_the_voices_stereo_spread"), 56, snap: v => Math.Round(v * 200) / 200),
                MixKnob(56, 34))
            : CardLayout.Large(new[] { Loc.T("device.chorus.tab_voices") }, 0,
                _ => CardLayout.View(voicesView, CardLayout.KnobColumn(AmountKnob(50), RateKnob(50), OffsetKnob()), CardLayout.Line(8, modeSel, syncSeg, viewSeg)),
                _ => (CardLayout.Head(CardLayout.Title(Loc.T("device.chorus.voice")), stateWord), CardLayout.Rows(fbRow, widthRow, warmRow, hpfRow, reads)),
                extras,
                CardLayout.Output(null, MixKnob(58, 44), null),
                _ => RefreshAll());

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
