// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Compressor body (device kind 1), a build of the "Nota
// Compressor" mockup (700 × 260) on the Lens / Delay / Reverb frame: an always-visible LEVEL
// column (threshold against the live key level · make-up, or the key gain on the Sidechain
// tab), a centre tabbed graph that fills its panel — Curve (transfer curve + reduction
// history), Motion (the reduction envelope against the input, so attack and release are seen,
// not guessed) and Sidechain (the key's spectrum under its filters) — a right panel holding the
// controls of the centre tab (Dynamics / Timing / Key) and a status strip. The graphs are
// controls: drag the transfer curve for threshold / ratio, the key filter handles for HP / LP / Q. Every control is a device
// param (raw units), so automation / MIDI learn / presets / A-B / persistence come for free;
// the key source and its pre/post tap are the device's sidechain routing, not params.
// FullBleed — the shared shell draws the header (name · preset · badge · GR meter · bypass).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class CompressorDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Compressor.h) ─────────────────────────────
    private const int Threshold = 0, Ratio = 1, Attack = 2, Release = 3, Makeup = 4, Knee = 5, Mix = 6,
        Lookahead = 7, Detection = 8, AutoRelease = 9, AutoGain = 10, Range = 11, Character = 12,
        ScHP = 13, ScLP = 14, ScListen = 15, ScGain = 16, Hold = 17, ScQ = 18, External = 19, StereoLink = 20;
    // Scope telemetry layout (Compressor::S_*), then the envelope rings and the key samples.
    private const int S_InPk = 0, S_OutPk = 1, S_InRms = 2, S_OutRms = 3, S_Gr = 4, S_KeyPk = 5, S_AtkMs = 6,
        S_RelMs = 7, S_SampleRate = 8, S_Bpm = 9, S_Latency = 10, S_Cpu = 11, S_ExtKey = 12, kScope = 14;
    private const int EnvN = 2048, KeyN = 2048, ScopeLen = kScope + 2 * EnvN + KeyN;

    private static readonly string[] Chars = { "Clean", "Glue", "Punch", "Opto", "FET" };
    private static readonly string[] Detects = { "Peak", "RMS", "Auto" };
    private static readonly double[] Windows = { 300, 600, 1000, 1500, 2000 };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "Dynamics";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        float Mn(int p) => engine.DeviceParamMin(track, di, p);
        float Mx(int p) => engine.DeviceParamMax(track, di, p);
        float Def(int p) => engine.DeviceParamDefault(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, double v) => engine.DeviceSetParam(track, di, p, (float)Math.Clamp(v, Mn(p), Mx(p)));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, double v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) => SetP(p, Def(p));
        bool On(int p) => P(p) >= 0.5f;
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p)), 0, n - 1);

        var readouts = new List<Action>();
        var scope = new float[ScopeLen];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;
        var envIn = new float[EnvN];
        var envGr = new float[EnvN];

        // ---- value maps (raw ↔ 0..1 for knobs and sliders) ------------------------------
        (Func<double, double> N, Func<double, double> V) Lin(int p)
        { double a = Mn(p), b = Mx(p); return (v => (v - a) / (b - a), n => a + Math.Clamp(n, 0, 1) * (b - a)); }
        (Func<double, double> N, Func<double, double> V) Log(int p)
        { double a = Mn(p), b = Mx(p); return (v => Math.Log(Math.Clamp(v, a, b) / a) / Math.Log(b / a), n => a * Math.Pow(b / a, Math.Clamp(n, 0, 1))); }
        (Func<double, double> N, Func<double, double> V) Sq(int p)
        { double b = Mx(p); return (v => Math.Sqrt(Math.Clamp(v, 0, b) / b), n => b * Math.Clamp(n, 0, 1) * Math.Clamp(n, 0, 1)); }
        // Range reads as the limit it puts on the reduction: empty = off (48 dB), full = 1 dB.
        (Func<double, double> N, Func<double, double> V) RangeMap() =>
            (v => v >= 47.5 ? 0 : Math.Clamp((48 - v) / 47, 0, 1), n => n <= 0.005 ? 48 : 48 - 47 * Math.Clamp(n, 0, 1));

        // ---- formatters -------------------------------------------------------------------
        static string Ms(double ms) => ms >= 100 ? NotaNum.F($"{ms:0} ms") : NotaNum.F($"{ms:0.0} ms");
        static string MsShort(double ms) => ms >= 100 ? NotaNum.F($"{ms:0}") : NotaNum.F($"{ms:0.0}");
        static string Hz(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0} k") : NotaNum.F($"{hz:0} Hz");
        static string Db(double db) => NotaNum.F($"{db:0.0} dB");
        static string SDb(double db) => NotaNum.F($"{db:+0.0;−0.0;0.0} dB");
        static string RatioF(double r) => r >= 9.95 ? NotaNum.F($"{r:0}:1") : NotaNum.F($"{r:0.#}:1");
        static string PctF(double v) => NotaNum.F($"{v:0} %");
        static string RangeF(double v) => v >= 47.5 ? "off" : NotaNum.F($"{v:0} dB");
        static double LinDb(double lin) => lin > 1e-6 ? 20 * Math.Log10(lin) : -120;
        static string Lvl(double db) => db <= -99 ? "−∞" : NotaNum.F($"{db:0.0}");
        string HpF(double v) => v <= 20.5 ? "off" : Hz(v);
        string LpF(double v) => v >= 19999 ? "off" : Hz(v);

        // ---- small builders ---------------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static Border Rule() => new() { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 1) };

        // On/off switch bound to a param (≥ 0.5 = on).
        Control Toggle(int p, string label, string tip)
        {
            var wrap = Switch(label, () => On(p), () => SetP(p, On(p) ? 0 : 1), out var sync);
            readouts.Add(sync);
            ToolTip.SetTip(wrap, tip);
            MidiLearn.Bind(wrap, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return wrap;
        }

        // Segmented chips over a discrete raw param (0 … n−1).
        Control Seg(int p, string[] names, bool fill = false, double padX = 6)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(p, n), iv => SetP(p, iv), out var sync, fill: fill, padX: padX);
            readouts.Add(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }

        // A latching chip over a toggle param (Listen · Auto gain).
        Border Latch(int p, string text, string tip)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            void Hi()
            {
                bool on = On(p);
                b.Background = on ? NotaPalette.AccentSubtle : Raised;
                b.BorderBrush = on ? NotaPalette.BorderBrass : Brushes.Transparent;
                tb.Foreground = on ? NotaPalette.AccentHover : TextPrimary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; SetP(p, On(p) ? 0 : 1); Hi(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            readouts.Add(Hi); Hi();
            return b;
        }

        // A caps label + slider row over a raw param.
        Control SliderRow(string label, int p, Func<double, string> fmt, (Func<double, double> N, Func<double, double> V) map, bool teal = false, double valW = 34)
        {
            var row = DeviceCardKit.SliderRow("", () => map.N(P(p)), n => Raw(p, map.V(n)), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: valW, modulation: teal);
            readouts.Add(sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(row, 1));
            return g;
        }

        // ---- live measurements ------------------------------------------------------------
        // Peaks held over ~300 ms of UI ticks, for the crest factors.
        var inPkHist = new double[18]; var outPkHist = new double[18]; int pkW = 0;
        double inPkHeld = 0, outPkHeld = 0;
        double KeyDb() => LinDb(Sc(S_KeyPk));
        double Bpm() => Sc(S_Bpm) > 1 ? Sc(S_Bpm) : 120;
        double WindowMs()
        {
            double want = 4 * (P(Attack) + P(Release) + P(Hold));
            foreach (double w in Windows) if (w >= want) return w;
            return Windows[^1];
        }
        // Hits: rises of the reduction through 1 dB (re-armed below 0.5 dB) over the last n ms.
        int Hits(int n)
        {
            int hits = 0; bool armed = true;
            for (int i = Math.Max(0, EnvN - n); i < EnvN; i++)
            {
                if (armed && envGr[i] > 1) { hits++; armed = false; }
                else if (!armed && envGr[i] < 0.5) armed = true;
            }
            return hits;
        }
        // The last hit in the window (the reduction rising through 1 dB from below 0.5 dB):
        // onset → its first peak (transient) and that peak → 37 % of it (recovery), in ms;
        // NaN when there is nothing to measure.
        (double Transient, double Recovery) Timings(int n)
        {
            int start = Math.Max(1, EnvN - n), onset = -1;
            bool armed = false;
            for (int i = start; i < EnvN; i++)
            {
                if (envGr[i] < 0.5) armed = true;
                else if (armed && envGr[i] > 1) { onset = i; armed = false; }
            }
            if (onset < 0) return (double.NaN, double.NaN);
            while (onset > start && envGr[onset - 1] >= 0.5) onset--;     // back to where it left 0.5 dB
            int peak = onset;
            while (peak + 1 < EnvN && envGr[peak + 1] >= envGr[peak]) peak++;
            if (peak + 1 >= EnvN) return (Math.Max(peak - onset, 0.5), double.NaN);   // still attacking
            double pv = envGr[peak];
            int reach = onset;
            while (reach < peak && envGr[reach] < 0.9 * pv) reach++;
            int k = peak;
            while (k + 1 < EnvN && envGr[k] > 0.37 * pv && !(envGr[k + 1] > envGr[k] + 0.5)) k++;
            double recovery = envGr[k] <= 0.37 * pv ? k - peak : double.NaN;
            return (Math.Max(reach - onset, 0.5), recovery);
        }

        // ======================================================================
        // LEFT — LEVEL column (threshold · make-up / key gain)
        // ======================================================================
        int centreTab = 0;
        var thrFader = new CompFader { Ink = NotaPalette.VizInk, Handle = NotaPalette.VizInk, VerticalAlignment = VerticalAlignment.Stretch };
        var thrMap = Lin(Threshold);
        thrFader.Default = thrMap.N(Def(Threshold));
        thrFader.Changed += v => Raw(Threshold, thrMap.V(v));
        thrFader.GestureBegin += () => Begin(Threshold);
        thrFader.GestureEnd += () => End(Threshold);
        MidiLearn.Bind(thrFader, MidiTarget.DeviceParam(track, di, Threshold), "Thresh");
        ToolTip.SetTip(thrFader, "Threshold — the tick is the key's level right now. Double-click resets.");

        // The second fader is make-up on Curve / Motion and the key's gain on Sidechain.
        int F2() => centreTab == 2 ? ScGain : Makeup;
        var f2 = new CompFader { Ink = DeviceCardKit.ModInk, Handle = DeviceCardKit.ModInkBright, VerticalAlignment = VerticalAlignment.Stretch };
        f2.Changed += v => Raw(F2(), Lin(F2()).V(v));
        f2.GestureBegin += () => Begin(F2());
        f2.GestureEnd += () => End(F2());
        MidiLearn.Bind(f2, MidiTarget.DeviceParam(track, di, Makeup), "Makeup");

        var thrLbl = Caps("THR"); thrLbl.HorizontalAlignment = HorizontalAlignment.Center;
        var f2Lbl = Caps("MKP"); f2Lbl.HorizontalAlignment = HorizontalAlignment.Center;
        var thrVal = Mono("", 7, TextPrimary); thrVal.HorizontalAlignment = HorizontalAlignment.Center;
        var f2Val = Mono("", 7, TextPrimary); f2Val.HorizontalAlignment = HorizontalAlignment.Center;
        void RefreshAll() { foreach (var r in readouts) r(); }
        Typeable(thrFader, "Thresh", () => thrMap.N(P(Threshold)), n => { Raw(Threshold, thrMap.V(n)); RefreshAll(); },
            () => Db(P(Threshold)), () => Begin(Threshold), () => End(Threshold), RefreshAll,
            () => { Begin(Threshold); Raw(Threshold, Def(Threshold)); End(Threshold); RefreshAll(); }, thrVal);
        Typeable(f2, "Makeup", () => Lin(F2()).N(P(F2())), n => { Raw(F2(), Lin(F2()).V(n)); RefreshAll(); },
            () => SDb(P(F2())), () => Begin(F2()), () => End(F2()), RefreshAll,
            () => { int id = F2(); Begin(id); Raw(id, Def(id)); End(id); RefreshAll(); }, f2Val);
        readouts.Add(() =>
        {
            double keyDb = KeyDb();
            thrFader.Set(thrMap.N(P(Threshold)), keyDb > -60 ? (keyDb + 60) / 60 : double.NaN);
            int p2 = F2();
            f2.Default = Lin(p2).N(Def(p2));
            f2.Set(Lin(p2).N(P(p2)));
            thrVal.Text = Db(P(Threshold));
            f2Val.Text = SDb(P(p2));
            f2Lbl.Text = NotaText.Label(p2 == ScGain ? "SC IN" : "MKP");
            ToolTip.SetTip(f2, p2 == ScGain ? "Key gain — drives the detector harder or softer. Double-click resets." : "Make-up gain. Double-click resets.");
        });
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 3) };
        faders.Children.Add(new DockPanel { Children = { Docked(thrLbl, Dock.Bottom), thrFader } });
        faders.Children.Add(Col(new DockPanel { Children = { Docked(f2Lbl, Dock.Bottom), f2 } }, 1));
        var levelTitle = Caps("LEVEL"); levelTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var levelCol = new RuleSection
        {
            Width = 56, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(levelTitle, Dock.Top), Docked(f2Val, Dock.Bottom), Docked(thrVal, Dock.Bottom), faders } },
        };
        DockPanel.SetDock(levelCol, Dock.Left);

        // ======================================================================
        // CENTRE — Curve
        // ======================================================================
        var grHist = new CompGrHistoryView();
        ToolTip.SetTip(grHist, "Gain reduction over the last four seconds; the dashed line is the peak. Click to clear it.");
        Control CurveTab()
        {
            var transfer = new CompTransferView();
            ToolTip.SetTip(transfer, "Drag sideways for the threshold, up / down for the ratio. Double-click resets both. The dot is the key level right now.");
            transfer.GestureBegin += () => { Begin(Threshold); Begin(Ratio); };
            transfer.GestureEnd += () => { End(Threshold); End(Ratio); };
            transfer.Changed += (thr, ratio) => { Raw(Threshold, thr); Raw(Ratio, ratio); };
            transfer.Reset += () => { Reset(Threshold); Reset(Ratio); };
            MidiLearn.Bind(transfer, MidiTarget.DeviceParam(track, di, Threshold), "Thresh");
            readouts.Add(() => { if (!transfer.Dragging) transfer.Set(P(Threshold), P(Ratio), P(Knee), P(Range), KeyDb()); });

            var graphs = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, Margin = new Thickness(8, 6) };
            graphs.Children.Add(transfer);
            graphs.Children.Add(Col(grHist, 1));
            return graphs;
        }

        // ======================================================================
        // CENTRE — Motion
        // ======================================================================
        Control MotionTab()
        {
            var motion = new CompMotionView();
            ToolTip.SetTip(motion, "The reduction (neutral ink, hanging from 0 dB) against the input level (teal) — attack is the fall, release the climb back. The dashed line is the threshold.");
            readouts.Add(() => motion.Set(envIn, envGr, scN >= kScope + 2 * EnvN ? EnvN : 0, P(Threshold), WindowMs()));
            return new Border { Margin = new Thickness(8, 6), Child = motion };
        }

        // ======================================================================
        // CENTRE — Sidechain
        // ======================================================================
        var srcIds = new List<int> { -1 };
        var srcNames = new List<string> { "Internal" };
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == track) continue;
            srcIds.Add(ti.Id);
            srcNames.Add(TrackNames.Of(engine, ti));
        }
        string SourceName() { int k = srcIds.IndexOf(engine.DeviceSidechainSource(track, di)); return k > 0 ? srcNames[k] : "Internal"; }
        bool ExtActive() => Sc(S_ExtKey) > 0.5;

        var key = new CompKeyView();
        Control SidechainTab()
        {
            ToolTip.SetTip(key, "The key's spectrum under its filters — drag a handle sideways to move HP / LP, up / down for Q. Double-click a handle to open it.");
            int KParam(int h) => h == 0 ? ScHP : h == 1 ? ScLP : ScQ;
            key.GestureBegin += h => Begin(KParam(h));
            key.GestureEnd += h => End(KParam(h));
            key.FreqChanged += (h, hz) => Raw(h == 0 ? ScHP : ScLP, hz);
            key.QChanged += q => Raw(ScQ, q);
            key.Reset += h => Reset(h == 0 ? ScHP : ScLP);
            MidiLearn.Bind(key, MidiTarget.DeviceParam(track, di, ScHP), "SC HP");
            readouts.Add(() =>
            {
                if (!key.Dragging) key.SetFilter(P(ScHP), P(ScLP), P(ScQ), On(ScListen));
                if (scN >= ScopeLen) key.SetKey(scope, kScope + 2 * EnvN, KeyN, Sc(S_SampleRate));
            });
            return new Border { Margin = new Thickness(8, 6), Child = key };
        }

        // ======================================================================
        // RIGHT — the controls of the centre tab (Curve · Motion · Sidechain)
        // ======================================================================
        // Rows spread evenly down the panel; Auto rows (rules) keep their height.
        Control Rows(params Control[] rows)
        {
            var grid = new Grid { RowSpacing = 2 };
            for (int r = 0; r < rows.Length; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition(rows[r] is Border { Height: 1 } ? GridLength.Auto : GridLength.Star));
                grid.Children.Add(GRow(rows[r], r));
            }
            return new Border { Padding = new Thickness(8, 4, 8, 5), Child = grid };
        }
        Control Labelled(string label, Control c)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(c, 1));
            return g;
        }

        Control CurvePanel()
        {
            var charSeg = Segments(Chars, () => Sel(Character, 5), iv => SetP(Character, iv), out var charSync, fill: true, padX: 1, fontSize: 8);
            readouts.Add(charSync);
            MidiLearn.Bind(charSeg, MidiTarget.DeviceParam(track, di, Character), engine.DeviceParamName(track, di, Character));
            ToolTip.SetTip(charSeg, "Character — the voicing: Clean as set, Glue slower and softer, Punch faster, Opto slow and program-soft, FET very fast");
            var detSeg = Seg(Detection, Detects, fill: true);
            ToolTip.SetTip(detSeg, "Detector — Peak reacts to every transient, RMS to the body, Auto follows the body and still catches the spikes");
            return Rows(
                charSeg,
                Labelled("DETECT", detSeg),
                SliderRow("RATIO", Ratio, RatioF, Log(Ratio)),
                SliderRow("KNEE", Knee, Db, Lin(Knee)),
                SliderRow("MIX", Mix, PctF, Lin(Mix)),
                Rule(),
                SliderRow("RANGE", Range, RangeF, RangeMap(), teal: true),
                Toggle(AutoGain, "Auto gain", "Auto gain — make-up that follows the threshold and ratio"));
        }

        Control MotionPanel() => Rows(
            SliderRow("ATTACK", Attack, Ms, Log(Attack)),
            SliderRow("RELEASE", Release, Ms, Log(Release)),
            SliderRow("HOLD", Hold, v => v < 0.5 ? "off" : Ms(v), Sq(Hold), teal: true),
            SliderRow("AHEAD", Lookahead, v => v < 0.05 ? "off" : Ms(v), Lin(Lookahead)),
            Rule(),
            Toggle(AutoRelease, "Auto-release", "Auto-release — a slower second release once the reduction runs deep, so sustained material does not pump"));

        Control KeyPanel()
        {
            var combo = new ComboBox { FontSize = 8, Height = 18, MinHeight = 18, Padding = new Thickness(6, 0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
            foreach (var n in srcNames) combo.Items.Add(n);
            combo.SelectedIndex = Math.Max(0, srcIds.IndexOf(engine.DeviceSidechainSource(track, di)));
            bool syncing = false;
            // The source can change elsewhere (MCP, undo): follow it while the list is closed.
            readouts.Add(() =>
            {
                if (combo.IsDropDownOpen) return;
                int want = Math.Max(0, srcIds.IndexOf(engine.DeviceSidechainSource(track, di)));
                if (combo.SelectedIndex != want) { syncing = true; combo.SelectedIndex = want; syncing = false; }
            });
            combo.SelectionChanged += (_, _) =>
            {
                int s = combo.SelectedIndex;
                if (syncing || s < 0 || s >= srcIds.Count) return;
                engine.SetDeviceSidechainSource(track, di, srcIds[s]);
                // Picking a source means using it.
                if (srcIds[s] >= 0 && !On(External)) SetP(External, 1);
                ctx.NotifyChanged();
            };
            ToolTip.SetTip(combo, "Key source — Internal keys off this track; pick another track to duck or pump against it");
            var tap = Segments(new[] { "Pre", "Post" }, () => engine.DeviceSidechainTapPre(track, di) ? 0 : 1,
                i => { engine.SetDeviceSidechainTapPre(track, di, i == 0); ctx.NotifyChanged(); }, out var tapSync, fill: true);
            readouts.Add(tapSync);
            ToolTip.SetTip(tap, "Where the source track is tapped: before its effects and fader, or after");
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var listen = Latch(ScListen, "Listen", "Listen — hear the filtered key instead of the output, to set HP / LP by ear");
            var ext = Latch(External, "External", "External key — key off the source above; off, the track keys itself");
            listen.HorizontalAlignment = HorizontalAlignment.Stretch; ext.HorizontalAlignment = HorizontalAlignment.Stretch;
            btns.Children.Add(ext); btns.Children.Add(Col(listen, 1));
            return Rows(
                Labelled("SOURCE", combo),
                Labelled("TAP", tap),
                btns,
                SliderRow("HP", ScHP, HpF, Log(ScHP)),
                SliderRow("LP", ScLP, LpF, Log(ScLP)),
                SliderRow("Q", ScQ, v => NotaNum.F($"{v:0.00}"), Log(ScQ)),
                Rule(),
                Toggle(StereoLink, "Stereo link", "Stereo link — one reduction for both channels; off, left and right compress on their own"));
        }

        // ======================================================================
        // Tab frame + the panel that follows it
        // ======================================================================
        var centreHost = new ContentControl();
        var centreBodies = new Control?[3];
        var panelHost = new ContentControl();
        var panelBodies = new Control?[3];
        string Crest(double pk, double rms) => rms > 1e-5 && pk > 1e-5 ? NotaNum.F($"{LinDb(pk) - LinDb(rms):0.0}") : "–";
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            switch (centreTab)
            {
                case 1:
                    var (tr, rec) = Timings((int)WindowMs());
                    int hits = Hits((int)WindowMs());
                    double held = 0;
                    for (int i = Math.Max(0, EnvN - (int)WindowMs()); i < EnvN; i++) held = Math.Max(held, envGr[i]);
                    extras.Text = !double.IsNaN(tr)
                        ? NotaNum.F($"transient {Ms(tr)} · recovery {(double.IsNaN(rec) ? "–" : Ms(rec))} · {hits} {(hits == 1 ? "hit" : "hits")}")
                        : held > 1 ? NotaNum.F($"reduction held near −{held:0.0}\u2009dB · no separate hits to time")
                        : NotaNum.F($"attack {Ms(Sc(S_AtkMs))} · release {Ms(Sc(S_RelMs))} · no reduction to measure");
                    extras.Foreground = TextTertiary; break;
                case 2:
                    double kdb = KeyDb(), trig = Hits(EnvN) * (4 * 60000.0 / Bpm()) / EnvN;
                    extras.Text = On(ScListen) ? "key audible · output muted"
                        : kdb <= -99 ? "key silent"
                        : NotaNum.F($"key {Lvl(kdb)} dB{(key.PeakHz > 0 ? NotaNum.F($" · peak {Hz(key.PeakHz)}") : "")} · {trig:0.#} {(Math.Abs(trig - 1) < 0.05 ? "trigger" : "triggers")} / bar");
                    extras.Foreground = On(ScListen) ? AccentBright : TextTertiary; break;
                default:
                    extras.Text = NotaNum.F($"in {Lvl(LinDb(Sc(S_InRms)))} · out {Lvl(LinDb(Sc(S_OutRms)))} dB · crest {Crest(inPkHeld, Sc(S_InRms))} → {Crest(outPkHeld, Sc(S_OutRms))}");
                    extras.Foreground = TextTertiary; break;
            }
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => MotionTab(), 2 => SidechainTab(), _ => CurveTab() };
        Control PanelBody(int t) => panelBodies[t] ??= t switch { 1 => MotionPanel(), 2 => KeyPanel(), _ => CurvePanel() };
        string[] panelTitles = { "Dynamics", "Timing", "Key" };
        var panelTitle = new TextBlock { FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        void ShowPanel(int t) { panelHost.Content = PanelBody(t); panelTitle.Text = panelTitles[t]; }

        var centre = TabFrame(new[] { "Curve", "Motion", "Sidechain" }, centreHost, CentreBody, false, t => { centreTab = t; ShowPanel(t); Refresh(); }, extras);
        ShowPanel(0);
        var panelHead = new Border { Height = 20, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = panelTitle };
        var right = new RuleSection { Width = 186, Child = new DockPanel { LastChildFill = true, Children = { Docked(panelHead, Dock.Top), panelHost } } };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new RuleSection { Margin = new Thickness(5, 0), Child = centre };

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, TextSecondary);
        string StatusText()
        {
            if (On(ScListen))
                return NotaNum.F($"Listen: only the key is heard · {SourceName()} {(engine.DeviceSidechainTapPre(track, di) ? "pre" : "post")} · HP {HpF(P(ScHP))} · LP {LpF(P(ScLP))}");
            string range = P(Range) >= 47.5 ? "" : NotaNum.F($" · range {RangeF(P(Range))}");
            string ext = ExtActive() ? NotaNum.F($" · key {SourceName()}") : "";
            return NotaNum.F($"{Chars[Sel(Character, 5)]} · thresh {Db(P(Threshold))} · {RatioF(P(Ratio))} · knee {P(Knee):0.0} · {MsShort(P(Attack))} / {MsShort(P(Release))} ms · mix {PctF(P(Mix))}{range}{ext}");
        }
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            statusLeft.Foreground = On(ScListen) ? AccentBright : TextSecondary;
            double sr = Sc(S_SampleRate);
            statusRight.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#} kHz · latency {Sc(S_Latency):0} smp · CPU {Sc(S_Cpu) * 100:0.0} %") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { levelCol, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceCard, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, ScopeLen);
            if (scN >= kScope + 2 * EnvN)
            {
                Array.Copy(scope, kScope, envIn, 0, EnvN);
                Array.Copy(scope, kScope + EnvN, envGr, 0, EnvN);
            }
            inPkHist[pkW] = Sc(S_InPk); outPkHist[pkW] = Sc(S_OutPk); pkW = (pkW + 1) % inPkHist.Length;
            inPkHeld = 0; outPkHeld = 0;
            for (int i = 0; i < inPkHist.Length; i++) { inPkHeld = Math.Max(inPkHeld, inPkHist[i]); outPkHeld = Math.Max(outPkHeld, outPkHist[i]); }
            foreach (var a in readouts) a();
        }
        void Tick() { grHist.Push(Sc(S_Gr)); }
        ctx.AddDeviceRefresher(() => { Refresh(); Tick(); });
        Refresh();
        return root;

        // local: a tab frame (bar + swapping body), optional extras on the right of the bar.
        Control TabFrame(string[] tabs, ContentControl host, Func<int, Control> body, bool centered, Action<int>? changed, Control? extrasCtl)
        {
            int sel = 0;
            var btns = new Border[tabs.Length];
            void Hi()
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool on = i == sel;
                    btns[i].Background = on ? Card2 : Brushes.Transparent;
                    btns[i].BorderBrush = on ? Brass : Brushes.Transparent;
                    var tb = (TextBlock)btns[i].Child!;
                    tb.Foreground = on ? NotaPalette.TextPrimary : TextTertiary;
                    tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            var bar = centered ? (Avalonia.Controls.Panel)new UniformGrid { Rows = 1 } : new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < tabs.Length; i++)
            {
                int iv = i;
                var b = new Border
                {
                    Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = tabs[i], FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
                };
                b.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                    sel = iv; Hi(); host.Content = body(iv); changed?.Invoke(iv);
                };
                btns[i] = b;
                bar.Children.Add(b);
            }
            var barDock = new DockPanel { Height = 20, LastChildFill = centered };
            if (extrasCtl != null) { var ex = new Border { Padding = new Thickness(0, 0, 8, 0), Child = extrasCtl }; DockPanel.SetDock(ex, Dock.Right); barDock.Children.Add(ex); }
            if (!centered) DockPanel.SetDock(bar, Dock.Left);
            barDock.Children.Add(bar);
            var barBorder = new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock };
            DockPanel.SetDock(barBorder, Dock.Top);
            Hi();
            host.Content = body(sel);
            return new DockPanel { LastChildFill = true, Children = { barBorder, host } };
        }
    }
}
