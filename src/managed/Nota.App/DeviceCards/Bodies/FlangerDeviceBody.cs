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
        static TextBlock Caps(string t, IBrush? c = null) => FxParts.Label(t, c);
        // A section's title: 12 / semibold, ink 1.
        static TextBlock Title(string t) => new()
        { Text = t, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

        // Slider row: label · value bar · value.
        Grid Slider(string label, int p, Func<double, string> fmt, string tip, double labelW = 60, double valueW = 46,
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

        // A pressed-in section: a 22px title row over the body.
        RuleSection Island(Control head, Control body, double width = double.NaN)
        {
            var headBar = new Border { Height = 22, Child = head };
            return new RuleSection
            {
                Width = width, Padding = new Thickness(SectionPad, SectionPad, SectionPad, 9),
                Child = new DockPanel { LastChildFill = true, Children = { Docked(headBar, Dock.Top), body } },
            };
        }
        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        // A legend sample: a drawn 10px stroke and its word (never a typed dash).
        static Control LegendItem(string word, IBrush ink) => new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
            Children = { new Border { Width = 10, Height = 2, CornerRadius = NotaRadius.Fine, Background = ink, VerticalAlignment = VerticalAlignment.Center }, FxParts.Info(word) },
        };

        // ======================================================================
        // LEFT — LFO: waveform · Hz / Sync · the rate · Rate · Stereo
        // ======================================================================
        var syncSeg = Segments(new[] { "Hz", "Sync" }, () => Synced() ? 1 : 0, i => { SetP(Sync, i); RefreshAll(); }, out var syncSync, padX: 6);
        syncSeg.VerticalAlignment = VerticalAlignment.Center;
        readouts.Add(syncSync);
        Learn(syncSeg, Sync);
        ToolTip.SetTip(syncSeg, "Hz runs the LFO free; Sync takes its rate from the tempo and locks it to the bar while the transport plays");

        var waveSeg = Segments(FlangerMath.Waves, WaveI, i => { SetP(Waveform, i / 2.0); RefreshAll(); }, out var waveSync, fill: true, padX: 0);
        readouts.Add(waveSync);
        Learn(waveSeg, Waveform);
        ToolTip.SetTip(waveSeg, "The LFO shape that sweeps the delay: Sine glides, Tri sweeps evenly, Saw rises and drops back");

        var big = FxParts.Value(); big.FontSize = 26; big.FontWeight = FontWeight.Bold; big.LineHeight = 28;
        var bigSub = FxParts.Info("", TextSecondary);
        var period = FxParts.Info(); period.Margin = new Thickness(0, 2, 0, 0);
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
            big.Foreground = On() ? TextPrimary : TextDisabled;
        });
        var bigBlock = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 16, 0, 0), Children = { big, bigSub, period } };
        ToolTip.SetTip(bigBlock, "The LFO rate: in Hz, or the note value it takes from the tempo");

        var rateRow = Slider("Rate", Rate, v => HzF(FlangerMath.FreeHz(v)) + " Hz", "Rate — the free LFO rate, 0.02 … 8 Hz", 48, 46);
        var divRow = Slider("Rate", Division, v => FlangerMath.DivNames[FlangerMath.DivIndex(v)],
            "Rate — the note value of one LFO cycle, 2/1 … 1/16 with dotted (D) and triplet (T) values", 48, 46,
            snap: v => FlangerMath.DivNorm(FlangerMath.DivIndex(v)));
        readouts.Add(() => { rateRow.IsVisible = !Synced(); divRow.IsVisible = Synced(); });
        var stereoRow = Slider("Stereo", Stereo, v => NotaNum.F($"{v * 180:0}°"),
            "Stereo — the right LFO against the left: 0° sweeps both together, 90° spreads them, 180° moves them opposite", 48, 46,
            modulation: true, snap: v => Math.Round(v * 180) / 180);

        var leftBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,26,26"), Margin = new Thickness(0, 3, 0, 0) };
        leftBody.Children.Add(waveSeg);
        leftBody.Children.Add(GRow(bigBlock, 1));
        leftBody.Children.Add(GRow(new Panel { Children = { rateRow, divRow } }, 2));
        leftBody.Children.Add(GRow(stereoRow, 3));
        var left = Island(HeadRow(Title("LFO"), syncSeg), leftBody, 176);
        DockPanel.SetDock(left, Dock.Left);

        // ======================================================================
        // CENTRE — COMB RESPONSE, and τ over two cycles
        // ======================================================================
        var tauWord = FxParts.Info();
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children = { LegendItem("L", NotaPalette.VizInk), LegendItem("R", NotaPalette.TealBright), tauWord },
        };
        var comb = new FlangerCombView { Value = () => (P(Delay), P(Feedback)) };
        comb.GestureBegin += () => { Begin(Delay); Begin(Feedback); };
        comb.GestureEnd += () => { End(Delay); End(Feedback); };
        comb.Changed += (d, f) => { Raw(Delay, d); Raw(Feedback, f); RefreshAll(); };
        comb.ResetRequested += () => { Reset(Delay); Reset(Feedback); RefreshAll(); };
        Learn(comb, Delay);
        ToolTip.SetTip(comb, "The comb the delay carves into the spectrum, L neutral and R teal, with the first notch dashed. Drag left / right to move the notch (Delay), up / down for Feedback; double-click resets both.");

        var sweep = new FlangerSweepView { Margin = new Thickness(0, 8, 0, 0), Height = 50 };
        ToolTip.SetTip(sweep, "The delay τ of both channels over two LFO cycles, and where each is now");
        var centreBody = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 0), Children = { Docked(sweep, Dock.Bottom), comb } };
        var centre = Island(HeadRow(Title("Comb response"), legend), centreBody);
        centre.Margin = new Thickness(SectionGap, 0);

        // ======================================================================
        // RIGHT — DELAY LINE: Delay · Depth · Feedback · Mix · snaps · notch readouts
        // ======================================================================
        var modeWord = FxParts.Info("", TextSecondary);
        readouts.Add(() =>
        {
            string m = Mode();
            modeWord.Text = m;
            modeWord.Foreground = m == "resonant" ? TextPrimary : TextSecondary;
        });
        ToolTip.SetTip(modeWord, "What the feedback makes: positive sharpens the peaks, negative hollows the tone, past 85\u2009% the comb rings");

        var delayRow = Slider("Delay", Delay, v => FlangerMath.Ms(FlangerMath.BaseMs(v)) + " ms", "Delay — the centre of the sweep, 0.1 … 8 ms: shorter puts the notches higher");
        var depthRow = Slider("Depth", Depth, v => PctF(v), "Depth — how far the LFO swings the delay around its centre");
        var fbRow = Slider("Feedback", Feedback, v => FbF(FlangerMath.Fb(v)),
            "Feedback — the delayed signal fed back in, ±95 %: positive for the classic jet, negative for a hollow tone", modulation: true, bipolar: true,
            snap: v => FlangerMath.FbNorm(Math.Round(FlangerMath.Fb(v) * 100) / 100));
        var mixRow = Slider("Mix", Mix, v => PctF(v), "Mix — 50 % gives the deepest notches; 100 % is the delayed signal alone, a vibrato");

        // The − / 0 / + snaps: cells in one groove, the one Feedback sits on raised in the soft tint.
        Border Snap(string text, double fb)
        {
            var tb = FxParts.Value(text, TextSecondary); tb.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new Border
            {
                Height = NotaSize.Seg, CornerRadius = NotaRadius.Cell, Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                SetP(Feedback, FlangerMath.FbNorm(fb)); RefreshAll(); e.Handled = true;
            };
            readouts.Add(() =>
            {
                bool on = Math.Abs(Fb() - fb) < 0.005;
                b.Background = on ? NotaPalette.AccentSoft : Brushes.Transparent;
                if (on) b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted"); else b.BoxShadow = default;
                tb.Foreground = on ? AccentBright : TextSecondary;
            });
            Learn(b, Feedback);
            ToolTip.SetTip(b, fb < 0 ? "Feedback −70\u2009%: hollow" : fb > 0 ? "Feedback +70\u2009%: the classic jet" : "Feedback 0: a plain comb");
            return b;
        }
        var snapCells = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        snapCells.Children.Add(Snap("−", -0.7));
        snapCells.Children.Add(Col(Snap("0", 0), 1));
        snapCells.Children.Add(Col(Snap("+", 0.7), 2));
        var snaps = new Border
        {
            Background = NotaPalette.Groove, CornerRadius = NotaRadius.Group, Padding = new Thickness(2), Margin = new Thickness(60, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Child = snapCells,
        };
        snaps.BindResource(Border.BoxShadowProperty, "Shadow.Groove");

        double PctAxis(double f) => Math.Clamp(Math.Log(Math.Max(f, 1e-3) / 20) / Math.Log(1000), 0, 1);
        Control ReadRow(string label, FlangerBar bar, TextBlock value, string tip)
        {
            value.TextAlignment = TextAlignment.Right; value.Width = 54;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("60,*,Auto") };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            g.Children.Add(Col(new Border { Child = value }, 2));
            ToolTip.SetTip(g, tip);
            return g;
        }
        var notchBar = new FlangerBar { Ink = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var notchVal = FxParts.Value();
        var nullBar = new FlangerBar { Ink = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        var nullVal = FxParts.Value();

        var rightBody = new Grid { RowDefinitions = new RowDefinitions("24,24,24,24,26,6,20,20") };
        rightBody.Children.Add(delayRow);
        rightBody.Children.Add(GRow(depthRow, 1));
        rightBody.Children.Add(GRow(fbRow, 2));
        rightBody.Children.Add(GRow(mixRow, 3));
        rightBody.Children.Add(GRow(snaps, 4));
        rightBody.Children.Add(GRow(new Border { Height = 1, Background = NotaPalette.Rule, VerticalAlignment = VerticalAlignment.Center }, 5));
        rightBody.Children.Add(GRow(ReadRow("Notch", notchBar, notchVal, "The first notch now, over the range the sweep moves it (20 Hz … 20 kHz)"), 6));
        rightBody.Children.Add(GRow(ReadRow("Null", nullBar, nullVal, "How deep the notches cut, dB (0 … −36 on the bar)"), 7));
        var right = Island(HeadRow(Title("Delay line"), modeWord), rightBody, 196);
        DockPanel.SetDock(right, Dock.Right);

        // ---- assemble ---------------------------------------------------------------
        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(0, CardPad, 0, 0), Children = { left, right, centre } };

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
