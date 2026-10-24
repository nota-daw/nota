// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Orbit body (auto-pan / tremolo, device kind 9), a build of
// the "Nota Orbit" picture of the style guide (700 × 260), three pressed-in sections: an LFO column (waveform,
// Hz or Sync, the rate as a big readout with its period, Rate and Shape — Glide on S&H), a
// centre panel with both channels' gain over two cycles, a running head with the gain dots and
// the floor the depth reaches (drag: Amount up / down, Phase left / right), the stereo position
// on an L—R line over the swing it covers, a Motion column (Amount, Phase, Mix, the 0° / 90° /
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

namespace Nota.App;

internal sealed class AutoPanDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match AutoPan.h) ─────────────────────────────
    private const int Rate = 0, Amount = 1, Waveform = 2, Shape = 3, Phase = 4, Mix = 5, Sync = 6, Division = 7;
    // Scope layout (AutoPan::S_* / kTele).
    private const int S_InPeak = 0, S_OutPeakL = 1, S_OutPeakR = 2, S_GainL = 3, S_GainR = 4, S_Pan = 5, S_WinPhase = 6,
        S_SampleRate = 11, S_Sh0 = 12, S_PanMin = 16, S_PanMax = 17, kTele = 32;

    public double Width => 700;
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
        double Floor() => 1 - P(Amount) * P(Mix);
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
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

        // Slider row: label · value bar · value. `lbl` is returned so a row can rename itself.
        Grid Slider(string label, int p, Func<double, string> fmt, string tip, out TextBlock lbl, double labelW = 40, double valueW = 40,
            Func<double, double>? snap = null)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, snap is null ? v : snap(v)); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: valueW);
            readouts.Add(sync);
            Learn(bar, p);
            ToolTip.SetTip(bar, tip);
            lbl = DeviceBodyKit.Label(label);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelW},*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(lbl);
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
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center, MinHeight = 24 };
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
        // LEFT — LFO: waveform · Hz / Sync · the rate · Rate · Shape
        // ======================================================================
        var syncSeg = Segments(new[] { "Hz", "Sync" }, () => Synced() ? 1 : 0, i => { SetP(Sync, i); RefreshAll(); }, out var syncSync);
        readouts.Add(syncSync);
        Learn(syncSeg, Sync);
        ToolTip.SetTip(syncSeg, "Hz runs the LFO free; Sync takes its rate from the tempo and locks it to the bar while the transport plays");

        var waveSeg = Segments(OrbitMath.Waves, WaveI, i => { SetP(Waveform, i / 4.0); RefreshAll(); }, out var waveSync, fill: true, padX: 0);
        readouts.Add(waveSync);
        Learn(waveSeg, Waveform);
        ToolTip.SetTip(waveSeg, "The LFO shape. S&H draws a random step every cycle; the right channel plays the same steps Phase later");

        var big = new TextBlock { FontSize = 34, FontWeight = FontWeight.Bold, LineHeight = 36, Foreground = TextPrimary, FontFamily = NotaFonts.UiFamily };
        var bigSub = DeviceBodyKit.Note("", TextSecondary);
        var period = DeviceBodyKit.Label(""); period.Margin = new Thickness(0, 2, 0, 0);
        readouts.Add(() =>
        {
            double hz = RateHz();
            if (Synced())
            {
                big.Text = OrbitMath.DivNames[DivI()];
                bigSub.Text = NotaNum.F($"note · {Bpm():0.##} BPM");
                period.Text = HzF(hz) + " Hz · " + Ms(PeriodMs());
            }
            else
            {
                big.Text = HzF(hz);
                bigSub.Text = "Hz · free";
                period.Text = "period " + Ms(PeriodMs());
            }
            big.Foreground = On() ? TextPrimary : TextDisabled;
        });
        var bigBlock = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Children = { big, bigSub, period } };
        ToolTip.SetTip(bigBlock, "The LFO rate: in Hz, or the note value it takes from the tempo");

        var rateRow = Slider("Rate", Rate, v => HzF(OrbitMath.FreeHz(v)) + " Hz", "Rate — the free LFO rate, 0.01 … 40 Hz", out _, 46, 56);
        var divRow = Slider("Rate", Division, v => OrbitMath.DivNames[OrbitMath.DivIndex(v)],
            "Rate — the note value of one LFO cycle, 4/1 … 1/32 with dotted (D) and triplet (T) values", out _, 46, 56,
            snap: v => OrbitMath.DivNorm(OrbitMath.DivIndex(v)));
        readouts.Add(() => { rateRow.IsVisible = !Synced(); divRow.IsVisible = Synced(); });
        var shapeRow = Slider("Shape", Shape, v => PctF(v), "Shape — sharpens Sine, Tri and Saw toward a square; on S&H it is Glide, the slide from one step to the next", out var shapeLbl, 46, 56);
        readouts.Add(() => shapeLbl.Text = WaveI() == 4 ? "Glide" : "Shape");

        var leftBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 10 };
        leftBody.Children.Add(waveSeg);
        leftBody.Children.Add(GRow(bigBlock, 1));
        leftBody.Children.Add(GRow(new Panel { Children = { rateRow, divRow } }, 2));
        leftBody.Children.Add(GRow(shapeRow, 3));
        var left = Island(HeadRow(DeviceBodyKit.Title("LFO"), syncSeg), leftBody, 184);
        DockPanel.SetDock(left, Avalonia.Controls.Dock.Left);

        // ======================================================================
        // CENTRE — gain over two cycles, and the pan line
        // ======================================================================
        var waveWord = DeviceBodyKit.Label("", TextSecondary);
        readouts.Add(() => waveWord.Text = OrbitMath.WaveNames[WaveI()]);
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { LegendItem("L", NotaPalette.VizInk), LegendItem("R", TextTertiary), waveWord },
        };
        var graph = new OrbitGainView { Value = () => (P(Amount), P(Phase)) };
        graph.GestureBegin += () => { Begin(Amount); Begin(Phase); };
        graph.GestureEnd += () => { End(Amount); End(Phase); };
        graph.Changed += (a, ph) => { Raw(Amount, a); Raw(Phase, ph); RefreshAll(); };
        graph.ResetRequested += () => { Reset(Amount); Reset(Phase); RefreshAll(); };
        Learn(graph, Amount);
        ToolTip.SetTip(graph, "Both channels' gain over two LFO cycles — L light, R a darker grey, Phase later — with the floor the depth reaches. Drag up / down for Amount, left / right for Phase; double-click resets both.");

        var panBar = new OrbitPanBar { VerticalAlignment = VerticalAlignment.Center };
        var panTxt = DeviceBodyKit.Value(); panTxt.TextAlignment = TextAlignment.Right; panTxt.Width = 30;
        var lTag = DeviceBodyKit.Label("L");
        var rTag = DeviceBodyKit.Label("R", TextTertiary);
        var panRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        panRow.Children.Add(lTag);
        panRow.Children.Add(Col(panBar, 1));
        panRow.Children.Add(Col(rTag, 2));
        panRow.Children.Add(Col(panTxt, 3));
        ToolTip.SetTip(panRow, "Where the sound sits now between left and right, over the swing the LFO covers");
        var centreBody = new DockPanel { LastChildFill = true, Children = { DeviceBodyKit.Dock(panRow, Avalonia.Controls.Dock.Bottom), graph } };
        var centre = Island(HeadRow(DeviceBodyKit.Title("Gain · 2 cycles"), legend), centreBody);
        centre.Margin = new Thickness(SectionGap, 0);

        // ======================================================================
        // RIGHT — Motion: Amount · Phase · Mix · phase snaps · output meters
        // ======================================================================
        var modeWord = DeviceBodyKit.Value();
        readouts.Add(() =>
        {
            string m = Mode();
            modeWord.Text = m;
            modeWord.Foreground = m == "bypass" ? TextDisabled : TextPrimary;
        });
        ToolTip.SetTip(modeWord, "What the phase makes: tremolo near 0°, auto-pan near 180°, an offset pan between");

        var amountRow = Slider("Amount", Amount, v => PctF(v), "Amount — the depth: each channel dips to 1 − Amount at the LFO's low point", out _, 58, 44);
        var phaseRow = Slider("Phase", Phase, v => NotaNum.F($"{v * 360:0}°"), "Phase — the right LFO against the left: 0° tremolo, 90° a circling pan, 180° auto-pan", out _, 58, 44,
            snap: v => Math.Round(v * 360) / 360);
        var mixRow = Slider("Mix", Mix, v => PctF(v), "Mix — the moving signal against the dry one", out _, 58, 44);

        // The 0° / 90° / 180° phase snaps: a segment strip, nothing picked between them.
        double[] snapDeg = { 0, 90, 180 };
        var snaps = Segments(new[] { "0°", "90°", "180°" }, () => Array.FindIndex(snapDeg, d => Math.Abs(Math.Round(Deg()) - d) < 0.5),
            i => { SetP(Phase, snapDeg[i] / 360); RefreshAll(); }, out var snapSync, fill: true, padX: 2);
        readouts.Add(snapSync);
        snaps.Margin = new Thickness(58, 0, 0, 0);
        Learn(snaps, Phase);
        ToolTip.SetTip(snaps, "0° — tremolo: both channels together · 90° — a quarter cycle apart: the sound circles · 180° — auto-pan: one side up while the other is down");

        Control MeterRow(string label, IBrush ink, int peakSlot, int gainSlot)
        {
            var m = new OrbitMeter { Ink = ink, VerticalAlignment = VerticalAlignment.Center };
            var v = DeviceBodyKit.Value(); v.TextAlignment = TextAlignment.Right; v.Width = 44;
            readouts.Add(() =>
            {
                double pk = Sc(peakSlot, -120);
                m.Set(On() ? (pk + 60) / 60 : 0);
                v.Text = On() ? OrbitMath.Db(Sc(gainSlot, 1)) : "0.0";
            });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("58,*,Auto"), ColumnSpacing = 0 };
            g.Children.Add(DeviceBodyKit.Label(label, TextSecondary));
            g.Children.Add(Col(m, 1));
            g.Children.Add(Col(new Border { Margin = new Thickness(8, 0, 0, 0), Child = v }, 2));
            ToolTip.SetTip(g, "The channel's output peak, −60 … 0 dBFS, and the gain the LFO gives it now, dB");
            return g;
        }
        var meters = new StackPanel
        {
            Spacing = 10, Children =
            {
                DeviceBodyKit.Rule(),
                MeterRow("Out L", NotaPalette.VizInk, S_OutPeakL, S_GainL),
                MeterRow("Out R", TextTertiary, S_OutPeakR, S_GainR),
            },
        };

        var rightBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,*,Auto,*,Auto") };
        rightBody.Children.Add(amountRow);
        rightBody.Children.Add(GRow(phaseRow, 2));
        rightBody.Children.Add(GRow(mixRow, 4));
        rightBody.Children.Add(GRow(snaps, 6));
        rightBody.Children.Add(GRow(meters, 8));
        var right = Island(HeadRow(DeviceBodyKit.Title("Motion"), modeWord), rightBody, 196);
        DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);

        // ---- assemble ---------------------------------------------------------------
        var root = DeviceBodyKit.Frame(new DockPanel { LastChildFill = true, Children = { left, right, centre } });

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
