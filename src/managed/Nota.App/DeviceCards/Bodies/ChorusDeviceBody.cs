// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Chorus body (modulated delay voices, device kind 25), a build
// of the "Nota Chorus" mockup (700 × 260) in the Flanger / Phaser language: an LFO column (Hz or
// Sync, the mode — Classic / Ensemble / Vibrato — the rate as a big readout with its period, Rate,
// the Offset between the channels and the HPF on the wet input), a centre panel with every voice
// over two cycles — its delay τ or its detune in cents (τ / ct) — with a running head (drag up /
// down for Amount) and, under it, the voices' stereo field, and a VOICE column (Amount, the bipolar
// Feedback, Width, Warmth, Mix, and the detune and delay ranges) with the state it is in. In Ensemble the Offset is fixed (3 × 120°), in Vibrato the Mix (wet only). The
// live LFO phase comes from the engine (Chorus.h scopeRead); the curves are drawn from the same
// formulas the DSP runs. Every control is a device param (normalized 0..1), so automation / MIDI
// learn / presets / A-B / persistence come for free. FullBleed — the shared shell draws the header
// (name · preset · badge · bypass).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

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
        static TextBlock Caps(string t, IBrush? c = null) => FxParts.Label(t, c);
        // A section's title: 12 / semibold, ink 1.
        static TextBlock Title(string t) => new()
        { Text = t, FontSize = NotaType.DeviceTab, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

        // Slider row: label · value bar · value. `locked` draws it in ink 4 and stops the hand
        // (Offset in Ensemble, Mix in Vibrato).
        Grid Slider(string label, int p, Func<double, string> fmt, string tip, double labelW, double valueW,
            bool modulation = false, bool bipolar = false, Func<double, double>? snap = null, Func<bool>? locked = null)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, snap is null ? v : snap(v)); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: valueW, modulation: modulation, bipolar: bipolar,
                dim: locked);
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

        // A pressed-in section: a 26px title row over the body.
        RuleSection Island(Control head, Control body, double width = double.NaN, double padBottom = 9)
        {
            var headBar = new Border { Height = 26, Child = head };
            return new RuleSection
            {
                Width = width, Padding = new Thickness(SectionPad, SectionPad, SectionPad, padBottom),
                Child = new DockPanel { LastChildFill = true, Children = { Docked(headBar, Dock.Top), body } },
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
            Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
            Children = { stroke, word },
        };

        // ======================================================================
        // LEFT — LFO: Hz / Sync · mode · the rate · Rate · Offset · HPF
        // ======================================================================
        var syncSeg = Segments(new[] { "Hz", "Sync" }, () => Synced() ? 1 : 0, i => { SetP(Sync, i); RefreshAll(); }, out var syncSync, padX: 6);
        syncSeg.VerticalAlignment = VerticalAlignment.Center;
        readouts.Add(syncSync);
        Learn(syncSeg, Sync);
        ToolTip.SetTip(syncSeg, "Hz runs the LFO free; Sync takes its rate from the tempo and locks it to the bar while the transport plays");

        var modeSeg = Segments(ChorusMath.Modes, ModeI, i => { SetP(Mode, i / 2.0); RefreshAll(); }, out var modeSync, fill: true, padX: 0);
        readouts.Add(modeSync);
        Learn(modeSeg, Mode);
        ToolTip.SetTip(modeSeg, "Classic — two voices, left and right; Ensemble — three voices 120° apart, a string-machine shimmer; Vibrato — the pitch wobble alone, no dry signal");

        var big = FxParts.Value(); big.FontSize = 26; big.FontWeight = FontWeight.Bold; big.LineHeight = 28;
        var bigSub = FxParts.Info("", TextSecondary);
        var period = FxParts.Info(); period.Margin = new Thickness(0, 2, 0, 0);
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
            big.Foreground = On() ? TextPrimary : TextDisabled;
        });
        var bigBlock = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0), Children = { big, bigSub, period } };
        ToolTip.SetTip(bigBlock, "The LFO rate: in Hz, or the note value it takes from the tempo");

        var rateRow = Slider("Rate", Rate, v => HzF(ChorusMath.FreeHz(v)) + " Hz", "Rate — the free LFO rate, 0.02 … 8 Hz", 48, 44);
        var divRow = Slider("Rate", Division, v => ChorusMath.DivNames[ChorusMath.DivIndex(v)],
            "Rate — the note value of one LFO cycle, 4/1 … 1/16 with triplet (T) values", 48, 44,
            snap: v => ChorusMath.DivNorm(ChorusMath.DivIndex(v)));
        readouts.Add(() => { rateRow.IsVisible = !Synced(); divRow.IsVisible = Synced(); });
        var offsetRow = Slider("Offset", Offset, v => Ens() ? "3 × 120°" : NotaNum.F($"{v * 180:0}°"),
            "Offset — the right voice's LFO behind the left: 0° moves both together, 180° opposite. Ensemble spreads its three voices 120° apart on its own",
            48, 44, modulation: true, snap: v => Math.Round(v * 180) / 180, locked: Ens);
        var hpfRow = Slider("HPF", Hpf, v => ChorusMath.HpHz(v) <= 0 ? "off" : ChorusMath.HzF(ChorusMath.HpHz(v)) + " Hz",
            "HPF — a high-pass on the wet input (20 Hz … 2 kHz), so the lows stay dry and centred; far left is off", 48, 44,
            modulation: true, snap: v => Math.Round(v / 0.005) * 0.005);

        var leftBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,22,22,22"), Margin = new Thickness(0, 6, 0, 0) };
        leftBody.Children.Add(modeSeg);
        leftBody.Children.Add(GRow(bigBlock, 1));
        leftBody.Children.Add(GRow(new Panel { Children = { rateRow, divRow } }, 2));
        leftBody.Children.Add(GRow(offsetRow, 3));
        leftBody.Children.Add(GRow(hpfRow, 4));
        var left = Island(HeadRow(Title("LFO"), syncSeg), leftBody, 186);
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
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 3; i++)
        {
            legendWords[i] = FxParts.Info("", TextTertiary);
            legendStrokes[i] = new Border { Width = 10, Height = 2, CornerRadius = NotaRadius.Fine, Background = ChorusMath.VoiceInk(i), VerticalAlignment = VerticalAlignment.Center };
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
        var plotRead = FxParts.Info("", TextSecondary);
        plotRead.Margin = new Thickness(0, 0, 8, 0);
        readouts.Add(() => plotRead.Text = Cents()
            ? "pitch ±" + ChorusMath.Ct(MaxCt()) + " ct"
            : "τ " + ChorusMath.Ms(TauMin()) + "–" + ChorusMath.Ms(TauMax()) + " ms");
        var viewSeg = Segments(new[] { "τ", "ct" }, () => Cents() ? 1 : 0, i => { viewCents = i == 1; RefreshAll(); }, out var viewSync, padX: 6);
        readouts.Add(viewSync);
        ToolTip.SetTip(viewSeg, "τ draws each voice's delay; ct its detune — how far the moving delay bends the pitch");
        var headRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { plotRead, viewSeg } };
        var centreHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        centreHead.Children.Add(Title("Voices"));
        centreHead.Children.Add(Col(legend, 1));
        centreHead.Children.Add(Col(headRight, 3));

        var plot = new ChorusVoicesView { Value = () => P(Amount) };
        plot.GestureBegin += () => Begin(Amount);
        plot.GestureEnd += () => End(Amount);
        plot.Changed += a => { Raw(Amount, Math.Round(a * 100) / 100); RefreshAll(); };
        plot.ResetRequested += () => { Reset(Amount); RefreshAll(); };
        Learn(plot, Amount);
        ToolTip.SetTip(plot, "Each voice over two LFO cycles — its delay (τ) or its detune (ct) — and where it is now. Drag up / down for Amount; double-click resets it.");

        var field = new ChorusFieldView { Margin = new Thickness(0, 6, 0, 0), Height = 50 };
        ToolTip.SetTip(field, "Where the voices sit in the stereo field (Width) and how far each is detuned right now");
        var centreBody = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 0), Children = { Docked(field, Dock.Bottom), plot } };
        var centre = Island(centreHead, centreBody, padBottom: 6);
        centre.Margin = new Thickness(SectionGap, 0);

        // ======================================================================
        // RIGHT — VOICE: Amount · Feedback · Width · Warmth · Mix · detune / delay readouts
        // ======================================================================
        var stateWord = FxParts.Info();
        readouts.Add(() =>
        {
            string s = State();
            stateWord.Text = s;
            stateWord.Foreground = s == "resonant" ? TextPrimary : s == "bypass" ? TextDisabled : TextTertiary;
        });
        ToolTip.SetTip(stateWord, "How many voices sing, or that the feedback makes it ring (past 60 %), or that Vibrato plays wet only");

        const double LW = 62, VW = 44;
        var amountRow = Slider("Amount", Amount, v => PctF(v), "Amount — how far the LFO swings each voice's delay: more is wider detune", LW, VW,
            snap: v => Math.Round(v * 100) / 100);
        var fbRow = Slider("Feedback", Feedback, v => FbF(ChorusMath.Fb(v)),
            "Feedback — the voices fed back into the line, ±90 %: a little thickens, past 60 % it rings metallic like a flanger", LW, VW,
            modulation: true, bipolar: true, snap: v => ChorusMath.FbNorm(Math.Round(ChorusMath.Fb(v) * 100) / 100));
        var widthRow = Slider("Width", Spread, v => NotaNum.F($"{ChorusMath.WidthPct(v):0} %"),
            "Width — the voices' stereo spread: 0 % mono, 100 % as they are, 200 % lifts the side by +6 dB", LW, VW,
            modulation: true, snap: v => Math.Round(v * 200) / 200);
        var warmRow = Slider("Warmth", Warmth, v => PctF(v),
            "Warmth — a bucket-brigade flavour on the delay line: darker (low-pass down to 2.5 kHz) and gently saturated", LW, VW,
            snap: v => Math.Round(v * 100) / 100);
        var mixRow = Slider("Mix", Mix, v => Vib() ? "wet" : PctF(v), "Mix — 0 % dry, 50 % dry and wet equal, 100 % the voices alone. Vibrato is always wet", LW, VW,
            snap: v => Math.Round(v * 100) / 100, locked: Vib);

        Control ReadRow(string label, FlangerBar bar, TextBlock value, string tip)
        {
            value.TextAlignment = TextAlignment.Right; value.Width = 56;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LW},*,Auto") };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            g.Children.Add(Col(new Border { Margin = new Thickness(4, 0, 0, 0), Child = value }, 2));
            ToolTip.SetTip(g, tip);
            return g;
        }
        var detuneBar = new FlangerBar { Ink = NotaPalette.VizInk, VerticalAlignment = VerticalAlignment.Center };
        var detuneVal = FxParts.Value();
        var delayBar = new FlangerBar { Ink = NotaPalette.BorderStrong, VerticalAlignment = VerticalAlignment.Center };
        var delayVal = FxParts.Value();

        var rightBody = new Grid { RowDefinitions = new RowDefinitions("22,22,22,22,22,6,22,22"), Margin = new Thickness(0, 4, 0, 0) };
        rightBody.Children.Add(amountRow);
        rightBody.Children.Add(GRow(fbRow, 1));
        rightBody.Children.Add(GRow(widthRow, 2));
        rightBody.Children.Add(GRow(warmRow, 3));
        rightBody.Children.Add(GRow(mixRow, 4));
        rightBody.Children.Add(GRow(new Border { Height = 1, Background = NotaPalette.Rule, VerticalAlignment = VerticalAlignment.Center }, 5));
        rightBody.Children.Add(GRow(ReadRow("Detune", detuneBar, detuneVal, "The largest detune the sweep reaches, ± cents (0 … 60 on the bar)"), 6));
        rightBody.Children.Add(GRow(ReadRow("Delay", delayBar, delayVal, "The range each voice's delay sweeps, ms (0 … 24 on the bar)"), 7));
        var right = Island(HeadRow(Title("Voice"), stateWord), rightBody, 186);
        DockPanel.SetDock(right, Dock.Right);

        // ---- assemble ---------------------------------------------------------------
        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(0, CardPad, 0, 0), Children = { left, right, centre } };

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
