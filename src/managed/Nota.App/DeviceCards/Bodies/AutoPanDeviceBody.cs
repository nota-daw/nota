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
// pan), and a status strip. The live picture comes from the engine (AutoPan.h scopeRead). Every
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
    public string? Subtitle => "PANNER";   // the processing type, shown as the header badge

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
        static TextBlock Caps(string t, IBrush? c = null) => new()
        { Text = t, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, LetterSpacing = 0.7, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

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

        Border Island(Control head, Control body, double width = double.NaN)
        {
            var headBar = new Border
            {
                Height = 20, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 0, 3, 0), Child = head,
            };
            return new Border
            {
                Width = width, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile,
                ClipToBounds = true, Child = new DockPanel { LastChildFill = true, Children = { Docked(headBar, Dock.Top), body } },
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
        // LEFT — LFO: waveform · Hz / Sync · the rate · Rate · Shape
        // ======================================================================
        var syncSeg = Segments(new[] { "Hz", "Sync" }, () => Synced() ? 1 : 0, i => { SetP(Sync, i); RefreshAll(); }, out var syncSync, fill: true, padX: 0);
        syncSeg.Width = 76;
        readouts.Add(syncSync);
        Learn(syncSeg, Sync);
        ToolTip.SetTip(syncSeg, "Hz runs the LFO free; Sync takes its rate from the tempo and locks it to the bar while the transport plays");

        var waveSeg = Segments(OrbitMath.Waves, WaveI, i => { SetP(Waveform, i / 4.0); RefreshAll(); }, out var waveSync, fill: true, padX: 0);
        readouts.Add(waveSync);
        Learn(waveSeg, Waveform);
        ToolTip.SetTip(waveSeg, "The LFO shape. S&H draws a random step every cycle; the right channel plays the same steps Phase later");

        var big = Mono("", 26, AccentBright); big.FontWeight = FontWeight.Bold; big.LineHeight = 28;
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
            big.Foreground = On() ? AccentBright : NotaPalette.TextAxis;
        });
        var bigBlock = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { big, bigSub, period } };
        ToolTip.SetTip(bigBlock, "The LFO rate: in Hz, or the note value it takes from the tempo");

        var rateRow = Slider("RATE", Rate, v => HzF(OrbitMath.FreeHz(v)) + "\u2009Hz", "Rate — the free LFO rate, 0.01 … 40\u2009Hz", out _, 38, 46);
        var divRow = Slider("RATE", Division, v => OrbitMath.DivNames[OrbitMath.DivIndex(v)],
            "Rate — the note value of one LFO cycle, 4/1 … 1/32 with dotted (D) and triplet (T) values", out _, 38, 46,
            snap: v => OrbitMath.DivNorm(OrbitMath.DivIndex(v)));
        readouts.Add(() => { rateRow.IsVisible = !Synced(); divRow.IsVisible = Synced(); });
        var shapeRow = Slider("SHAPE", Shape, v => PctF(v), "Shape — sharpens Sine, Tri and Saw toward a square; on S&H it is Glide, the slide from one step to the next", out var shapeLbl, 38, 46);
        readouts.Add(() => shapeLbl.Text = WaveI() == 4 ? "GLIDE" : "SHAPE");

        var leftBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 6, Margin = new Thickness(8, 6) };
        leftBody.Children.Add(waveSeg);
        leftBody.Children.Add(GRow(bigBlock, 1));
        leftBody.Children.Add(GRow(new Panel { Children = { rateRow, divRow } }, 2));
        leftBody.Children.Add(GRow(shapeRow, 3));
        var left = Island(HeadRow(Caps("LFO"), syncSeg), leftBody, 176);
        DockPanel.SetDock(left, Dock.Left);

        // ======================================================================
        // CENTRE — GAIN · 2 CYCLES, and the pan line
        // ======================================================================
        var waveWord = Mono("", 7, TextTertiary);
        readouts.Add(() => waveWord.Text = OrbitMath.WaveNames[WaveI()]);
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 5, 0),
            Children = { LegendItem("L", AccentBright), LegendItem("R", NotaPalette.TealBright), waveWord },
        };
        var graph = new OrbitGainView { Value = () => (P(Amount), P(Phase)) };
        graph.GestureBegin += () => { Begin(Amount); Begin(Phase); };
        graph.GestureEnd += () => { End(Amount); End(Phase); };
        graph.Changed += (a, ph) => { Raw(Amount, a); Raw(Phase, ph); RefreshAll(); };
        graph.ResetRequested += () => { Reset(Amount); Reset(Phase); RefreshAll(); };
        Learn(graph, Amount);
        ToolTip.SetTip(graph, "Both channels' gain over two LFO cycles — L brass, R teal, Phase later — with the floor the depth reaches. Drag up / down for Amount, left / right for Phase; double-click resets both.");

        var panBar = new OrbitPanBar { VerticalAlignment = VerticalAlignment.Center };
        var panTxt = Mono("", 8, TextPrimary); panTxt.TextAlignment = TextAlignment.Right; panTxt.Width = 28;
        var lTag = Mono("L", 7, AccentBright); lTag.FontWeight = FontWeight.Bold;
        var rTag = Mono("R", 7, NotaPalette.TealBright); rTag.FontWeight = FontWeight.Bold;
        var panRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 6, Height = 14, Margin = new Thickness(0, 4, 0, 0) };
        panRow.Children.Add(lTag);
        panRow.Children.Add(Col(panBar, 1));
        panRow.Children.Add(Col(rTag, 2));
        panRow.Children.Add(Col(panTxt, 3));
        ToolTip.SetTip(panRow, "Where the sound sits now between left and right, over the swing the LFO covers");
        var centreBody = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { Docked(panRow, Dock.Bottom), graph } };
        var centre = Island(HeadRow(Caps("GAIN · 2 CYCLES"), legend), centreBody);
        centre.Margin = new Thickness(5, 0);

        // ======================================================================
        // RIGHT — MOTION: Amount · Phase · Mix · phase snaps · output meters
        // ======================================================================
        var modeWord = Mono("", 7, TextSecondary);
        modeWord.Margin = new Thickness(0, 0, 5, 0);
        readouts.Add(() =>
        {
            string m = Mode();
            modeWord.Text = m;
            modeWord.Foreground = m == "auto-pan" ? AccentBright : m == "tremolo" ? NotaPalette.TealBright : TextSecondary;
        });
        ToolTip.SetTip(modeWord, "What the phase makes: tremolo near 0°, auto-pan near 180°, an offset pan between");

        var amountRow = Slider("AMOUNT", Amount, v => PctF(v), "Amount — the depth: each channel dips to 1 − Amount at the LFO's low point", out _, 44, 36, modulation: true);
        var phaseRow = Slider("PHASE", Phase, v => NotaNum.F($"{v * 360:0}°"), "Phase — the right LFO against the left: 0° tremolo, 90° a circling pan, 180° auto-pan", out _, 44, 36, modulation: true,
            snap: v => Math.Round(v * 360) / 360);
        var mixRow = Slider("MIX", Mix, v => PctF(v), "Mix — the moving signal against the dry one", out _, 44, 36);

        Border Snap(string text, double deg)
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
                SetP(Phase, deg / 360); RefreshAll(); e.Handled = true;
            };
            readouts.Add(() =>
            {
                bool on = Math.Abs(Math.Round(Deg()) - deg) < 0.5;
                b.Background = on ? NotaPalette.TrackOff : Brushes.Transparent;
                tb.Foreground = on ? TextPrimary : TextTertiary;
            });
            Learn(b, Phase);
            ToolTip.SetTip(b, deg == 0 ? "0° — tremolo: both channels together" : deg == 90 ? "90° — a quarter cycle apart: the sound circles" : "180° — auto-pan: one side up while the other is down");
            return b;
        }
        var snaps = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 1, Margin = new Thickness(44, 0, 0, 0) };
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
            ToolTip.SetTip(g, "The channel's output peak, −60 … 0\u2009dBFS, and the gain the LFO gives it now, dB");
            return g;
        }
        var meters = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0),
            Child = new StackPanel
            {
                Spacing = 5, Children =
                {
                    MeterRow("OUT L", AccentBright, Brass, S_OutPeakL, S_GainL),
                    MeterRow("OUT R", NotaPalette.TealBright, NotaPalette.TealBright, S_OutPeakR, S_GainR),
                },
            },
        };

        var rightBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,*,Auto,*,Auto,*,Auto"), Margin = new Thickness(8, 6) };
        rightBody.Children.Add(amountRow);
        rightBody.Children.Add(GRow(phaseRow, 2));
        rightBody.Children.Add(GRow(mixRow, 4));
        rightBody.Children.Add(GRow(snaps, 6));
        rightBody.Children.Add(GRow(meters, 8));
        var right = Island(HeadRow(Caps("MOTION"), modeWord), rightBody, 176);
        DockPanel.SetDock(right, Dock.Right);

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, TextSecondary);
        readouts.Add(() =>
        {
            string m = Mode();
            double hz = RateHz();
            bool warn = false;
            string foot;
            if (!On()) foot = "Panner bypassed: the signal passes unchanged";
            else if (P(Amount) < 0.01f || P(Mix) < 0.01f) { foot = "Amount or Mix at zero: nothing moves"; warn = true; }
            else if (hz > 8) { foot = "Above 8\u2009Hz the movement reads as a flutter, not as panning"; warn = true; }
            else if (m == "tremolo") foot = "Phase 0°: L and R move together, the level dips to " + OrbitMath.Db(Floor()) + "\u2009dB";
            else if (m == "auto-pan")
            {
                double mn = Sc(S_PanMin), mx = Sc(S_PanMax);
                foot = "Swing " + (mn < -0.02 ? PanF(mn) : "C") + " → " + (mx > 0.02 ? PanF(mx) : "C") + " · period " + Ms(PeriodMs());
            }
            else foot = NotaNum.F($"Phase {Deg():0}°: the channels are offset, the pan moves unevenly");
            statusLeft.Text = foot;
            statusLeft.Foreground = warn ? AccentBright : TextSecondary;
            double sr = Sc(S_SampleRate);
            statusRight.Text = (sr > 0 ? NotaNum.F($"{sr / 1000:0.#}\u2009kHz · ") : "") + NotaNum.F($"{Bpm():0.##}\u2009BPM · ") + (Synced() ? "retrig bar" : "free run");
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { left, right, centre } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

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
