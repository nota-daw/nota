// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Phaser body (a chain of LFO-swept all-pass stages, device kind
// 24), a build of the "Nota Phaser" picture of the style guide (712 × 260), three pressed-in sections: an LFO
// column (waveform, Hz or Sync, the rate as a big readout with its period, Rate and the Stereo phase
// offset between the channels), a centre panel with the live all-pass response of L and R with the
// notches ticked (drag: left / right = Center, up / down = Feedback) and, under it, the corner
// frequency of both channels over two cycles with a running head, a Stages column (2 / 4 / 6 / 8 /
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

namespace Nota.App;

internal sealed class PhaserDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Phaser.h) ──────────────────────────────
    private const int Rate = 0, Center = 1, Depth = 2, Feedback = 3, Mix = 4, Waveform = 5, Sync = 6, Division = 7, Stereo = 8, Stages = 9;
    // Scope layout (Phaser::S_* / kTele).
    private const int S_FcL = 3, S_FcR = 4, S_WinPhase = 5, S_SampleRate = 10, kTele = 32;

    public double Width => 712;
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
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

        // Slider row: label · value bar · value.
        Grid Slider(string label, int p, Func<double, string> fmt, string tip, double labelW = 58, double valueW = 56,
            bool bipolar = false, Func<double, double>? snap = null)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, snap is null ? v : snap(v)); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: valueW, bipolar: bipolar);
            readouts.Add(sync);
            Learn(bar, p);
            ToolTip.SetTip(bar, tip);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelW},*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(DeviceBodyKit.Label(label));
            g.Children.Add(Col(bar, 1));
            return g;
        }

        // A section: its title row on top (title · a control or word at the right), the body under it.
        RuleSection Island(Control head, Control body, double width = double.NaN)
        {
            head.Margin = new Thickness(0, 0, 0, 8);
            return DeviceBodyKit.Sec(new DockPanel { LastChildFill = true, Children = { DeviceBodyKit.Dock(head, Avalonia.Controls.Dock.Top), body } },
                new Thickness(12, 10, 12, 10), width);
        }
        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center, MinHeight = 24 };
            right.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        // A legend sample: a drawn 10px stroke and its word (never a typed dash).
        static Control LegendItem(string word, IBrush ink) => new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
            Children = { new Border { Width = 10, Height = 2, Background = ink, VerticalAlignment = VerticalAlignment.Center }, DeviceBodyKit.Label(word, TextSecondary) },
        };

        // ======================================================================
        // LEFT — LFO: waveform · Hz / Sync · the rate · Rate · Stereo
        // ======================================================================
        var syncSeg = Segments(new[] { "Hz", "Sync" }, () => Synced() ? 1 : 0, i => { SetP(Sync, i); RefreshAll(); }, out var syncSync);
        readouts.Add(syncSync);
        Learn(syncSeg, Sync);
        ToolTip.SetTip(syncSeg, "Hz runs the LFO free; Sync takes its rate from the tempo and locks it to the bar while the transport plays");

        var waveSeg = Segments(PhaserMath.Waves, WaveI, i => { SetP(Waveform, i / 2.0); RefreshAll(); }, out var waveSync, fill: true, padX: 0);
        readouts.Add(waveSync);
        Learn(waveSeg, Waveform);
        ToolTip.SetTip(waveSeg, "The LFO shape that sweeps the stages: Sine glides, Tri sweeps evenly, Saw rises and drops back");

        var big = new TextBlock { FontSize = 34, FontWeight = FontWeight.Bold, LineHeight = 36, Foreground = TextPrimary, FontFamily = NotaFonts.UiFamily };
        var bigSub = DeviceBodyKit.Note("", TextSecondary);
        var period = DeviceBodyKit.Label(""); period.Margin = new Thickness(0, 2, 0, 0);
        readouts.Add(() =>
        {
            double hz = RateHz();
            if (Synced())
            {
                big.Text = PhaserMath.DivNames[DivI()];
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
        var bigBlock = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Children = { big, bigSub, period } };
        ToolTip.SetTip(bigBlock, "The LFO rate: in Hz, or the note value it takes from the tempo");

        var rateRow = Slider("Rate", Rate, v => HzF(PhaserMath.FreeHz(v)) + " Hz", "Rate — the free LFO rate, 0.02 … 8 Hz", 48, 52);
        var divRow = Slider("Rate", Division, v => PhaserMath.DivNames[PhaserMath.DivIndex(v)],
            "Rate — the note value of one LFO cycle, 4/1 … 1/16 with dotted (D) and triplet (T) values", 48, 52,
            snap: v => PhaserMath.DivNorm(PhaserMath.DivIndex(v)));
        readouts.Add(() => { rateRow.IsVisible = !Synced(); divRow.IsVisible = Synced(); });
        var stereoRow = Slider("Stereo", Stereo, v => NotaNum.F($"{v * 180:0}°"),
            "Stereo — the right LFO against the left: 0° sweeps both together, 90° spreads them, 180° moves them opposite", 48, 52,
            snap: v => Math.Round(v * 180) / 180);

        var leftBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 10 };
        leftBody.Children.Add(waveSeg);
        leftBody.Children.Add(GRow(bigBlock, 1));
        leftBody.Children.Add(GRow(new Panel { Children = { rateRow, divRow } }, 2));
        leftBody.Children.Add(GRow(stereoRow, 3));
        var left = Island(HeadRow(DeviceBodyKit.Title("LFO"), syncSeg), leftBody, 180);
        DockPanel.SetDock(left, Avalonia.Controls.Dock.Left);

        // ======================================================================
        // CENTRE — all-pass response, and fc over two cycles
        // ======================================================================
        var fcWord = DeviceBodyKit.Label("", TextSecondary);
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { LegendItem("L", NotaPalette.VizInk), LegendItem("R", TextTertiary), fcWord },
        };
        var response = new PhaserResponseView { Value = () => (P(Center), P(Feedback)) };
        response.GestureBegin += () => { Begin(Center); Begin(Feedback); };
        response.GestureEnd += () => { End(Center); End(Feedback); };
        response.Changed += (c, f) => { Raw(Center, c); Raw(Feedback, f); RefreshAll(); };
        response.ResetRequested += () => { Reset(Center); Reset(Feedback); RefreshAll(); };
        Learn(response, Center);
        ToolTip.SetTip(response, "The notches the stages cut into the spectrum, L light and R a darker grey, with the corner dashed and each notch ticked on top. Drag left / right to move Center, up / down for Feedback; double-click resets both.");

        var sweep = new PhaserSweepView { Margin = new Thickness(0, 8, 0, 0) };
        ToolTip.SetTip(sweep, "The corner frequency of both channels over two LFO cycles (20 Hz … 20 kHz), and where each is now");
        var centreBody = new DockPanel { LastChildFill = true, Children = { DeviceBodyKit.Dock(sweep, Avalonia.Controls.Dock.Bottom), response } };
        var centre = Island(HeadRow(DeviceBodyKit.Title("All-pass response"), legend), centreBody);
        centre.Margin = new Thickness(SectionGap, 0);

        // ======================================================================
        // RIGHT — Stages: 2…12 · Center · Depth · Feedback · Mix · sweep / null readouts
        // ======================================================================
        var modeWord = DeviceBodyKit.Value();
        readouts.Add(() =>
        {
            string m = Mode();
            modeWord.Text = m;
            modeWord.Foreground = m == "bypass" ? TextDisabled : TextPrimary;
        });
        ToolTip.SetTip(modeWord, "What the feedback makes: positive sharpens the peaks, negative moves and widens the notches, past 85 % the phaser whistles");

        var stageSeg = Segments(PhaserMath.StageCounts.Select(n => n.ToString()).ToArray(), () => PhaserMath.StageIndex(P(Stages)),
            i => { SetP(Stages, i / (double)(PhaserMath.StageCounts.Length - 1)); RefreshAll(); }, out var stageSync, fill: true, padX: 0);
        readouts.Add(stageSync);
        Learn(stageSeg, Stages);
        ToolTip.SetTip(stageSeg, "All-pass stages: every two cut one notch — 2 is a gentle swirl, 12 a deep, dense sweep");

        var centerRow = Slider("Center", Center, v => PhaserMath.HzF(PhaserMath.CenterHz(v)) + " Hz",
            "Center — the middle of the sweep, 50 Hz … 5 kHz: higher moves the notches up", 62);
        var depthRow = Slider("Depth", Depth, v => NotaNum.F($"±{v * PhaserMath.DepthOct:0.0} oct"),
            "Depth — how far the LFO swings the stages around Center, up to ±3 octaves", 62);
        var fbRow = Slider("Feedback", Feedback, v => FbF(PhaserMath.Fb(v)),
            "Feedback — the chain's output fed back in, ±95 %: positive sharpens the peaks, negative moves and widens the notches", 62, bipolar: true,
            snap: v => PhaserMath.FbNorm(Math.Round(PhaserMath.Fb(v) * 100) / 100));
        var mixRow = Slider("Mix", Mix, v => PctF(v), "Mix — 50 % gives the deepest notches; 100 % is the phase-shifted signal alone", 62);

        double PctAxis(double f) => Math.Clamp(Math.Log(Math.Max(f, 1e-3) / 20) / Math.Log(1000), 0, 1);
        Control ReadRow(string label, FlangerBar bar, TextBlock value, string tip)
        {
            value.TextAlignment = TextAlignment.Right; value.Width = 56;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("62,*,Auto") };
            g.Children.Add(DeviceBodyKit.Label(label));
            g.Children.Add(Col(bar, 1));
            g.Children.Add(Col(new Border { Margin = new Thickness(8, 0, 0, 0), Child = value }, 2));
            ToolTip.SetTip(g, tip);
            return g;
        }
        var sweepBar = new FlangerBar { Ink = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var sweepVal = DeviceBodyKit.Value();
        var nullBar = new FlangerBar { Ink = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var nullVal = DeviceBodyKit.Value();
        var reads = new StackPanel
        {
            Spacing = 8, Children =
            {
                DeviceBodyKit.Rule(),
                ReadRow("Sweep", sweepBar, sweepVal, "The range the corner sweeps (20 Hz … 20 kHz on the bar), and the first notch now"),
                ReadRow("Null", nullBar, nullVal, "How deep the notches cut, dB (0 … −36 on the bar)"),
            },
        };

        var rightBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,*,Auto,*,Auto,*,Auto") };
        rightBody.Children.Add(stageSeg);
        rightBody.Children.Add(GRow(centerRow, 2));
        rightBody.Children.Add(GRow(depthRow, 4));
        rightBody.Children.Add(GRow(fbRow, 6));
        rightBody.Children.Add(GRow(mixRow, 8));
        rightBody.Children.Add(GRow(reads, 10));
        var right = Island(HeadRow(DeviceBodyKit.Title("Stages"), modeWord), rightBody, 194);
        DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);

        // ---- assemble ---------------------------------------------------------------
        var root = DeviceBodyKit.Frame(new DockPanel { LastChildFill = true, Children = { left, right, centre } });

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
