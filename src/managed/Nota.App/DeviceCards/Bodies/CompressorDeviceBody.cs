// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Compressor body (device kind 1), a build of the "Nota
// Compressor" mockup (700 × 260) on the Lens / Delay / Reverb frame: an always-visible LEVEL
// column (threshold against the live key level · make-up, or the key gain on the Sidechain
// tab), a centre tabbed panel — Curve (transfer curve + reduction history), Motion (the
// reduction envelope against the input, so attack and release are seen, not guessed) and
// Sidechain (key source, the key's spectrum under its filters, Listen) — a right tabbed panel
// (Dynamics / Output); no status line. The graphs are controls: drag the transfer curve
// for threshold / ratio, the key filter handles for HP / LP / Q. Every control is a device
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
        static string Hz(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0} k") : NotaNum.F($"{hz:0} Hz");
        static string Db(double db) => NotaNum.F($"{db:0.0} dB");
        static string SDb(double db) => NotaNum.F($"{db:+0.0;−0.0;0.0} dB");
        static string RatioF(double r) => r >= 9.95 ? NotaNum.F($"{r:0}:1") : NotaNum.F($"{r:0.#}:1");
        static string PctF(double v) => NotaNum.F($"{v:0} %");
        static string RangeF(double v) => v >= 47.5 ? "off" : NotaNum.F($"{v:0} dB");
        static string Gr(double g) => g > 0.05 ? NotaNum.F($"−{g:0.0}") : "0.0";
        static double LinDb(double lin) => lin > 1e-6 ? 20 * Math.Log10(lin) : -120;
        static string Lvl(double db) => db <= -99 ? "−∞" : NotaNum.F($"{db:0.0}");
        string HpF(double v) => v <= 20.5 ? "off" : Hz(v);
        string LpF(double v) => v >= 19999 ? "off" : Hz(v);

        // ---- small builders ---------------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null) => FxParts.Label(t, c);
        static TextBlock Mono(string t, IBrush c) { var tb = FxParts.Info(t, c); tb.TextTrimming = TextTrimming.None; return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static Border Rule() => new() { Height = 1, Background = NotaPalette.Rule, VerticalAlignment = VerticalAlignment.Center };

        // Gauge knob over a raw param through a value map (gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, (Func<double, double> N, Func<double, double> V) map,
                  bool emphasised = false, double cellW = 50)
        {
            var val = Mono(fmt(P(p)), TextPrimary);
            var knob = new Knob(map.N(P(p)), 1.0) { Accent = true, Default = map.N(Def(p)) };
            knob.Format = v => fmt(map.V(v));   // read-out at any value: typing searches it
            knob.ValueChanged += v => { Raw(p, map.V(v)); val.Text = fmt(P(p)); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            readouts.Add(() => { if (knob.Dragging) return; double c = map.N(P(p)); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(P(p)); });
            return KnobCell(name, knob, val, cellW, emphasised);
        }

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
            // A latching button: raised at rest, the soft tint with orange text while on.
            var tb = new TextBlock { Text = text, FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = NotaSize.Seg, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Cell, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            b.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
            void Hi()
            {
                bool on = On(p);
                b.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                tb.Foreground = on ? AccentBright : TextSecondary;
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; SetP(p, On(p) ? 0 : 1); Hi(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            readouts.Add(Hi); Hi();
            return b;
        }

        // A caps label + slider row over a raw param.
        Control SliderRow(string label, int p, Func<double, string> fmt, (Func<double, double> N, Func<double, double> V) map, double valW = 42)
        {
            var row = DeviceCardKit.SliderRow("", () => map.N(P(p)), n => Raw(p, map.V(n)), () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), valueWidth: valW);
            readouts.Add(sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), VerticalAlignment = VerticalAlignment.Center };
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
        // The last hit in the window (the reduction rising through 1 dB from below 0.5 dB):
        // onset → its first peak (transient) and that peak → 37 % of it (recovery), in ms;
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
            while (onset > start && envGr[onset - 1] >= 0.5) onset--;     // back to where it left 0.5 dB
            int peak = onset;
            while (peak + 1 < EnvN && envGr[peak + 1] >= envGr[peak]) peak++;
            if (peak + 1 >= EnvN) return (Math.Max(peak - onset, 0.5), double.NaN);   // still attacking
            double pv = envGr[peak];
            int k = peak;
            while (k + 1 < EnvN && envGr[k] > 0.37 * pv && !(envGr[k + 1] > envGr[k] + 0.5)) k++;
            double recovery = envGr[k] <= 0.37 * pv ? k - peak : double.NaN;
            return (Math.Max(peak - onset, 0.5), recovery);
        }

        // ======================================================================
        // LEFT — LEVEL column (threshold · make-up / key gain)
        // ======================================================================
        int centreTab = 0;
        var thrMap = Lin(Threshold);
        var thrVal = Mono("", TextPrimary); thrVal.HorizontalAlignment = HorizontalAlignment.Center;
        var f2Val = Mono("", TextPrimary); f2Val.HorizontalAlignment = HorizontalAlignment.Center;
        var thrFader = FxParts.Fader("Thresh", () => thrMap.N(P(Threshold)), n => Raw(Threshold, thrMap.V(n)), () => Db(P(Threshold)),
            () => Begin(Threshold), () => End(Threshold), () => Reset(Threshold), thrVal, out var thrSync);
        MidiLearn.Bind(thrFader, MidiTarget.DeviceParam(track, di, Threshold), "Thresh");
        ToolTip.SetTip(thrFader, "Threshold — the tick is the key's level right now. Double-click resets.");

        // The second fader is make-up on Curve / Motion and the key's gain on Sidechain.
        int F2() => centreTab == 2 ? ScGain : Makeup;
        var f2 = FxParts.Fader("Makeup", () => Lin(F2()).N(P(F2())), n => Raw(F2(), Lin(F2()).V(n)), () => SDb(P(F2())),
            () => Begin(F2()), () => End(F2()), () => Reset(F2()), f2Val, out var f2Sync);
        MidiLearn.Bind(f2, MidiTarget.DeviceParam(track, di, Makeup), "Makeup");

        var thrLbl = Caps("Thr"); thrLbl.HorizontalAlignment = HorizontalAlignment.Center; thrLbl.Margin = new Thickness(0, 4, 0, 0);
        var f2Lbl = Caps("Mkp"); f2Lbl.HorizontalAlignment = HorizontalAlignment.Center; f2Lbl.Margin = new Thickness(0, 4, 0, 0);
        readouts.Add(() =>
        {
            double keyDb = KeyDb();
            thrSync(); f2Sync();
            thrFader.Marker = keyDb > -60 ? (keyDb + 60) / 60 : double.NaN;
            int p2 = F2();
            f2Lbl.Text = p2 == ScGain ? "SC in" : "Mkp";
            ToolTip.SetTip(f2, p2 == ScGain ? "Key gain — drives the detector harder or softer. Double-click resets." : "Make-up gain. Double-click resets.");
        });
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 5) };
        faders.Children.Add(new DockPanel { Children = { Docked(thrLbl, Dock.Bottom), thrFader } });
        faders.Children.Add(Col(new DockPanel { Children = { Docked(f2Lbl, Dock.Bottom), f2 } }, 1));
        var levelTitle = Caps("Level"); levelTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var levelCol = new RuleSection
        {
            Width = 64, Padding = new Thickness(0, 8, 0, 6),
            Child = new DockPanel { Children = { Docked(levelTitle, Dock.Top), Docked(f2Val, Dock.Bottom), Docked(thrVal, Dock.Bottom), faders } },
        };
        DockPanel.SetDock(levelCol, Dock.Left);

        // ======================================================================
        // CENTRE — shared pieces
        // ======================================================================
        Control CharRow(Func<string> right, Func<IBrush> rightInk)
        {
            var info = Mono("", TextTertiary); info.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => { info.Text = right(); info.Foreground = rightInk(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 8, Height = 24 };
            g.Children.Add(Caps("Char"));
            var seg = Seg(Character, Chars);
            ToolTip.SetTip(seg, "Character — the voicing: Clean as set, Glue slower and softer, Punch faster, Opto slow and program-soft, FET very fast");
            g.Children.Add(Col(seg, 1));
            g.Children.Add(Col(info, 2));
            return g;
        }
        // The four timing knobs + two lines of measurements on the right.
        Control TimingRow(bool motion, Func<string> stat1, Func<string> stat2)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("58,58,58,58,*"), ColumnSpacing = 0, Height = 54 };
            g.Children.Add(K(Attack, "Attack", Ms, Log(Attack), emphasised: motion, cellW: 58));
            g.Children.Add(Col(K(Release, "Release", Ms, Log(Release), emphasised: motion, cellW: 58), 1));
            g.Children.Add(Col(K(Knee, "Knee", Db, Lin(Knee), cellW: 58), 2));
            g.Children.Add(Col(K(Lookahead, "Lookahead", v => v < 0.05 ? "off" : Ms(v), Lin(Lookahead), cellW: 58), 3));
            var s1 = Mono("", TextTertiary); s1.HorizontalAlignment = HorizontalAlignment.Right;
            var s2 = Mono("", TextTertiary); s2.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => { s1.Text = stat1(); s2.Text = stat2(); });
            g.Children.Add(Col(new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2), Children = { s1, s2 } }, 4));
            return g;
        }

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

            var graphs = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, Margin = new Thickness(0, 6, 0, 6) };
            graphs.Children.Add(transfer);
            graphs.Children.Add(Col(grHist, 1));

            string Crest(double pk, double rms) => rms > 1e-5 && pk > 1e-5 ? NotaNum.F($"{LinDb(pk) - LinDb(rms):0.0}") : "–";
            return new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0), Children = {
                Docked(CharRow(() => NotaNum.F($"in {Lvl(LinDb(Sc(S_InRms)))} · out {Lvl(LinDb(Sc(S_OutRms)))} dB"), () => TextTertiary), Dock.Top),
                Docked(TimingRow(false,
                    () => NotaNum.F($"GR avg {Gr(grHist.Average)} · peak {Gr(grHist.Peak)} dB"),
                    () => NotaNum.F($"crest {Crest(inPkHeld, Sc(S_InRms))} → {Crest(outPkHeld, Sc(S_OutRms))} dB")), Dock.Bottom),
                graphs } };
        }

        // ======================================================================
        // CENTRE — Motion
        // ======================================================================
        Control MotionTab()
        {
            var motion = new CompMotionView();
            ToolTip.SetTip(motion, "The reduction (neutral ink, hanging from 0 dB) against the input level (teal) — attack is the fall, release the climb back. The dashed line is the threshold.");
            readouts.Add(() => motion.Set(envIn, envGr, scN >= kScope + 2 * EnvN ? EnvN : 0, P(Threshold), WindowMs()));
            string Measured()
            {
                var (tr, rec) = Timings((int)WindowMs());
                if (double.IsNaN(tr)) return "no reduction to measure";
                return NotaNum.F($"transient {Ms(tr)} · recovery {(double.IsNaN(rec) ? "–" : Ms(rec))}");
            }
            return new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0), Children = {
                Docked(CharRow(() => NotaNum.F($"atk {Ms(Sc(S_AtkMs))} · rel {Ms(Sc(S_RelMs))}"), () => TextSecondary), Dock.Top),
                Docked(TimingRow(true,
                    () => NotaNum.F($"GR peak {Gr(grHist.Peak)} dB · {Hits((int)WindowMs())} hits"),   // in the window named in the tab row
                    Measured), Dock.Bottom),
                new Border { Margin = new Thickness(0, 6, 0, 6), Child = motion } } };
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

        Control SidechainTab()
        {
            var combo = new ComboBox { FontSize = NotaType.KnobValue, Height = NotaSize.Dropdown, MinHeight = NotaSize.Dropdown, MinWidth = 100, Padding = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
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
                i => { engine.SetDeviceSidechainTapPre(track, di, i == 0); ctx.NotifyChanged(); }, out var tapSync);
            readouts.Add(tapSync);
            ToolTip.SetTip(tap, "Where the source track is tapped: before its effects and fader, or after");
            var info = Mono("", TextTertiary); info.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => info.Text = NotaNum.F($"HP {HpF(P(ScHP))} · LP {LpF(P(ScLP))} · Q {P(ScQ):0.0}"));
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 8, Height = 24 };
            head.Children.Add(Caps("Key"));
            head.Children.Add(Col(combo, 1));
            head.Children.Add(Col(tap, 2));
            head.Children.Add(Col(info, 3));

            var key = new CompKeyView();
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

            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("54,54,54,54,54,*"), ColumnSpacing = 0, Height = 54 };
            g.Children.Add(K(ScHP, "HP", HpF, Log(ScHP), emphasised: true, cellW: 54));
            g.Children.Add(Col(K(ScLP, "LP", LpF, Log(ScLP), emphasised: true, cellW: 54), 1));
            g.Children.Add(Col(K(ScQ, "Q", v => NotaNum.F($"{v:0.00}"), Log(ScQ), cellW: 54), 2));
            g.Children.Add(Col(K(ScGain, "SC gain", SDb, Lin(ScGain), cellW: 54), 3));
            g.Children.Add(Col(K(Hold, "Hold", v => v < 0.5 ? "off" : Ms(v), Sq(Hold), cellW: 54), 4));
            var s1 = Mono("", TextTertiary); s1.HorizontalAlignment = HorizontalAlignment.Right;
            var s2 = Mono("", TextTertiary); s2.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() =>
            {
                double kdb = KeyDb();
                s1.Text = kdb <= -99 ? "key silent" : key.PeakHz > 0 ? NotaNum.F($"key {Lvl(kdb)} dB · peak {Hz(key.PeakHz)}") : NotaNum.F($"key {Lvl(kdb)} dB");
                double barMs = 4 * 60000.0 / Bpm();
                s2.Text = NotaNum.F($"{Hits(EnvN) * barMs / EnvN:0.#} triggers / bar");
            });
            g.Children.Add(Col(new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2), Children = { s1, s2 } }, 5));

            return new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0), Children = {
                Docked(head, Dock.Top), Docked(g, Dock.Bottom), new Border { Margin = new Thickness(0, 6, 0, 6), Child = key } } };
        }

        // ======================================================================
        // RIGHT — Dynamics / Output
        // ======================================================================
        Border GrBox(bool levels)
        {
            // A read-out well (the graph ground, corner 6): its title and the live figures.
            var title = Caps(levels ? "Output" : "Reduction");
            var stack = new StackPanel { Spacing = 1 };
            var box = new Border { Background = NotaGraph.Ground, CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 4), Child = stack };
            Control Line(TextBlock name, Func<string> v, Func<IBrush> ink)
            {
                var val = Mono("", TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                readouts.Add(() => { val.Text = v(); val.Foreground = ink(); });
                var gg = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 16 };
                gg.Children.Add(name);
                gg.Children.Add(Col(val, 1));
                return gg;
            }
            if (levels)
            {
                // The tab is already named Output: two lines, no title.
                stack.Children.Add(Line(Caps("in · out"), () => NotaNum.F($"{Lvl(LinDb(Sc(S_InRms)))} · {Lvl(LinDb(Sc(S_OutRms)))} dB"), () => Sc(S_OutPk) > 0.5 ? NotaPalette.Warning : TextPrimary));
                stack.Children.Add(Line(Caps("GR"), () => NotaNum.F($"{Gr(Sc(S_Gr))} dB"), () => TextPrimary));
            }
            else
            {
                var set = Mono("", TextTertiary); set.Height = 16;
                stack.Children.Add(Line(title, () => NotaNum.F($"{Gr(Sc(S_Gr))} dB · peak {Gr(grHist.Peak)}"), () => TextPrimary));
                stack.Children.Add(set);
                readouts.Add(() =>
                {
                    set.Text = NotaNum.F($"thresh {P(Threshold):0.0} · {RatioF(P(Ratio))} · knee {P(Knee):0.#}");
                    bool hot = grHist.Peak >= 10;   // heavy reduction: a caution, not the accent
                    title.Foreground = hot ? NotaPalette.Warning : TextTertiary;
                });
            }
            return box;
        }

        Control DynamicsTab()
        {
            var det = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), VerticalAlignment = VerticalAlignment.Center };
            det.Children.Add(Caps("Detect"));
            var detSeg = Seg(Detection, Detects);
            ToolTip.SetTip(detSeg, "Detector — Peak reacts to every transient, RMS to the body, Auto follows the body and still catches the spikes");
            det.Children.Add(Col(detSeg, 1));
            var rows = new Control[]
            {
                det,
                SliderRow("Ratio", Ratio, RatioF, Log(Ratio)),
                SliderRow("Mix", Mix, PctF, Lin(Mix)),
                Rule(),
                SliderRow("Range", Range, RangeF, RangeMap()),
                GrBox(false),
                new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Bottom, Children = {
                    Toggle(AutoRelease, "Auto-release", "Auto-release — a slower second release once the reduction runs deep, so sustained material does not pump"),
                    Toggle(AutoGain, "Auto gain", "Auto gain — make-up that follows the threshold and ratio") } },
            };
            rows[5].Margin = new Thickness(0, 1, 0, 0);
            var grid = new Grid { RowDefinitions = new RowDefinitions("26,26,26,5,26,Auto,*"), Margin = new Thickness(0, 2, 0, 0) };   // 183 of 190
            for (int r = 0; r < rows.Length; r++) grid.Children.Add(GRow(rows[r], r));
            return grid;
        }

        Control OutputTab()
        {
            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var listen = Latch(ScListen, "Listen", "Listen — hear the filtered key instead of the output, to set HP / LP by ear");
            var autoGain = Latch(AutoGain, "Auto gain", "Auto gain — make-up that follows the threshold and ratio");
            listen.HorizontalAlignment = HorizontalAlignment.Stretch; autoGain.HorizontalAlignment = HorizontalAlignment.Stretch;
            btns.Children.Add(listen); btns.Children.Add(Col(autoGain, 1));
            var rows = new Control[]
            {
                GrBox(true),
                SliderRow("Makeup", Makeup, v => NotaNum.F($"{v:+0.0;−0.0;0.0}"), Lin(Makeup)),
                SliderRow("Mix", Mix, PctF, Lin(Mix)),
                SliderRow("Range", Range, RangeF, RangeMap()),
                btns,
                new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Bottom, Children = {
                    Toggle(External, "External key", "External key — key off the source picked on the Sidechain tab; off, the track keys itself"),
                    Toggle(StereoLink, "Stereo link", "Stereo link — one reduction for both channels; off, left and right compress on their own") } },
            };
            // 190 high in all: the read-out, three bars 24 apart, the buttons, two switches.
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,24,24,24,Auto,*"), Margin = new Thickness(0, 4, 0, 0) };
            rows[0].Margin = new Thickness(0, 0, 0, 2);
            btns.Margin = new Thickness(0, 3, 0, 0);
            for (int r = 0; r < rows.Length; r++) grid.Children.Add(GRow(rows[r], r));
            return grid;
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[2];
        var extras = Mono("", TextTertiary);
        readouts.Add(() =>
        {
            switch (centreTab)
            {
                case 1:
                    extras.Text = NotaNum.F($"window {Ms(WindowMs())} · {RatioF(P(Ratio))} · knee {P(Knee):0.0}");
                    extras.Foreground = TextTertiary; break;
                case 2:
                    extras.Text = On(ScListen) ? "key audible · output muted" : ExtActive() ? NotaNum.F($"external key · {SourceName()}") : "internal key";
                    extras.Foreground = On(ScListen) ? AccentBright : TextTertiary; break;
                default:
                    extras.Text = NotaNum.F($"{RatioF(P(Ratio))} · knee {P(Knee):0.0} dB · {Detects[Sel(Detection, 3)].ToLowerInvariant()}");
                    extras.Foreground = TextTertiary; break;
            }
        });
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => MotionTab(), 2 => SidechainTab(), _ => CurveTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : DynamicsTab();

        var centre = FxParts.Tabs(new[] { "Curve", "Motion", "Sidechain" }, centreHost, CentreBody, t => { centreTab = t; Refresh(); }, extras);
        var rightFrame = FxParts.Tabs(new[] { "Dynamics", "Output" }, rightHost, RightBody, _ => Refresh());
        var right = FxParts.Section(rightFrame); right.Width = 196;
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = FxParts.Section(centre); centreBox.Margin = new Thickness(SectionGap, 0);

        // ---- assemble ---------------------------------------------------------------------
        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(0, CardPad, 0, 0), Children = { levelCol, right, centreBox } };

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
    }
}
