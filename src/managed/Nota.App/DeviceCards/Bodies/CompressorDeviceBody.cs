// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Compressor body (device kind 1), a build of the "Nota
// Compressor" mockup (700 × 260) on the Lens / Delay / Reverb frame: an always-visible LEVEL
// column (threshold against the live key level · make-up, or the key gain on the Sidechain
// tab), a centre tabbed panel — Curve (transfer curve + reduction history), Motion (the
// reduction envelope against the input, so attack and release are seen, not guessed) and
// Sidechain (key source, the key's spectrum under its filters, Listen) — a right tabbed panel
// (Dynamics / Output) and a status strip. The graphs are controls: drag the transfer curve
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
    public string? Subtitle => "DYNAMICS";

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
        static string Gr(double g) => g > 0.05 ? NotaNum.F($"−{g:0.0}") : "0.0";
        static double LinDb(double lin) => lin > 1e-6 ? 20 * Math.Log10(lin) : -120;
        static string Lvl(double db) => db <= -99 ? "−∞" : NotaNum.F($"{db:0.0}");
        string HpF(double v) => v <= 20.5 ? "off" : Hz(v);
        string LpF(double v) => v >= 19999 ? "off" : Hz(v);

        // ---- small builders ---------------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
        { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static Border Rule() => new() { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 1) };

        // Gauge knob over a raw param through a value map (gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, (Func<double, double> N, Func<double, double> V) map,
                  IBrush? arc = null, bool emphasised = false, double cellW = 50)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(map.N(P(p)), 1.0) { Accent = true, ArcColor = arc, Default = map.N(Def(p)), Width = 34, Height = 34 };
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
            var tb = new TextBlock { Text = text, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
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
        var thrFader = new CompFader { Ink = NotaPalette.Accent, Handle = NotaPalette.AccentBright, VerticalAlignment = VerticalAlignment.Stretch };
        var thrMap = Lin(Threshold);
        thrFader.Default = thrMap.N(Def(Threshold));
        thrFader.Changed += v => Raw(Threshold, thrMap.V(v));
        thrFader.GestureBegin += () => Begin(Threshold);
        thrFader.GestureEnd += () => End(Threshold);
        MidiLearn.Bind(thrFader, MidiTarget.DeviceParam(track, di, Threshold), "Thresh");
        ToolTip.SetTip(thrFader, "Threshold — the tick is the key's level right now. Double-click resets.");

        // The second fader is make-up on Curve / Motion and the key's gain on Sidechain.
        int F2() => centreTab == 2 ? ScGain : Makeup;
        var f2 = new CompFader { Ink = NotaPalette.Teal, Handle = NotaPalette.TealBright, VerticalAlignment = VerticalAlignment.Stretch };
        f2.Changed += v => Raw(F2(), Lin(F2()).V(v));
        f2.GestureBegin += () => Begin(F2());
        f2.GestureEnd += () => End(F2());
        MidiLearn.Bind(f2, MidiTarget.DeviceParam(track, di, Makeup), "Makeup");

        var thrLbl = Caps("THR"); thrLbl.HorizontalAlignment = HorizontalAlignment.Center;
        var f2Lbl = Caps("MKP"); f2Lbl.HorizontalAlignment = HorizontalAlignment.Center;
        var thrVal = Mono("", 7, AccentBright); thrVal.HorizontalAlignment = HorizontalAlignment.Center;
        var f2Val = Mono("", 7, TextPrimary); f2Val.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() =>
        {
            double keyDb = KeyDb();
            thrFader.Set(thrMap.N(P(Threshold)), keyDb > -60 ? (keyDb + 60) / 60 : double.NaN);
            int p2 = F2();
            f2.Default = Lin(p2).N(Def(p2));
            f2.Set(Lin(p2).N(P(p2)));
            thrVal.Text = Db(P(Threshold));
            f2Val.Text = SDb(P(p2));
            f2Lbl.Text = p2 == ScGain ? "SC IN" : "MKP";
            ToolTip.SetTip(f2, p2 == ScGain ? "Key gain — drives the detector harder or softer. Double-click resets." : "Make-up gain. Double-click resets.");
        });
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 3) };
        faders.Children.Add(new DockPanel { Children = { Docked(thrLbl, Dock.Bottom), thrFader } });
        faders.Children.Add(Col(new DockPanel { Children = { Docked(f2Lbl, Dock.Bottom), f2 } }, 1));
        var levelTitle = Caps("LEVEL"); levelTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var levelCol = new Border
        {
            Width = 56, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(levelTitle, Dock.Top), Docked(f2Val, Dock.Bottom), Docked(thrVal, Dock.Bottom), faders } },
        };
        DockPanel.SetDock(levelCol, Dock.Left);

        // ======================================================================
        // CENTRE — shared pieces
        // ======================================================================
        Control CharRow(Func<string> right, Func<IBrush> rightInk)
        {
            var info = Mono("", 7, TextTertiary); info.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => { info.Text = right(); info.Foreground = rightInk(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6, Height = 18 };
            g.Children.Add(Caps("CHAR"));
            var seg = Seg(Character, Chars);
            ToolTip.SetTip(seg, "Character — the voicing: Clean as set, Glue slower and softer, Punch faster, Opto slow and program-soft, FET very fast");
            g.Children.Add(Col(seg, 1));
            g.Children.Add(Col(info, 2));
            return g;
        }
        // The four timing knobs + two lines of measurements on the right.
        Control TimingRow(bool motion, Func<string> stat1, Func<string> stat2)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("56,56,56,56,*"), ColumnSpacing = 2, Height = 52 };
            g.Children.Add(K(Attack, "ATTACK", Ms, Log(Attack), emphasised: motion, cellW: 56));
            g.Children.Add(Col(K(Release, "RELEASE", Ms, Log(Release), emphasised: motion, cellW: 56), 1));
            g.Children.Add(Col(K(Knee, "KNEE", Db, Lin(Knee), cellW: 56), 2));
            g.Children.Add(Col(K(Lookahead, "LOOKAHEAD", v => v < 0.05 ? "off" : Ms(v), Lin(Lookahead), cellW: 56), 3));
            var s1 = Mono("", 7, TextTertiary); s1.HorizontalAlignment = HorizontalAlignment.Right;
            var s2 = Mono("", 7, TextTertiary); s2.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => { s1.Text = stat1(); s2.Text = stat2(); });
            g.Children.Add(Col(new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { s1, s2 } }, 4));
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

            var graphs = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, Margin = new Thickness(0, 5, 0, 5) };
            graphs.Children.Add(transfer);
            graphs.Children.Add(Col(grHist, 1));

            string Crest(double pk, double rms) => rms > 1e-5 && pk > 1e-5 ? NotaNum.F($"{LinDb(pk) - LinDb(rms):0.0}") : "–";
            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 5, 8, 4), Children = {
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
            ToolTip.SetTip(motion, "The reduction (brass, hanging from 0 dB) against the input level (teal) — attack is the fall, release the climb back. The dashed line is the threshold.");
            readouts.Add(() => motion.Set(envIn, envGr, scN >= kScope + 2 * EnvN ? EnvN : 0, P(Threshold), WindowMs()));
            string Measured()
            {
                var (tr, rec) = Timings((int)WindowMs());
                if (double.IsNaN(tr)) return "no reduction to measure";
                return NotaNum.F($"transient {Ms(tr)} · recovery {(double.IsNaN(rec) ? "–" : Ms(rec))}");
            }
            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 5, 8, 4), Children = {
                Docked(CharRow(() => NotaNum.F($"attack {Ms(Sc(S_AtkMs))} · release {Ms(Sc(S_RelMs))}"), () => AccentBright), Dock.Top),
                Docked(TimingRow(true,
                    () => NotaNum.F($"GR peak {Gr(grHist.Peak)} dB · {Hits((int)WindowMs())} hits / window"),
                    Measured), Dock.Bottom),
                new Border { Margin = new Thickness(0, 5, 0, 5), Child = motion } } };
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
            srcNames.Add(NotaNum.F($"{i + 1} · {(ti.IsReturn ? "Return" : ti.IsInstrument ? "Instr" : "Audio")}"));
        }
        string SourceName() { int k = srcIds.IndexOf(engine.DeviceSidechainSource(track, di)); return k > 0 ? srcNames[k] : "Internal"; }
        bool ExtActive() => Sc(S_ExtKey) > 0.5;

        Control SidechainTab()
        {
            var combo = new ComboBox { FontSize = 8, Height = 18, MinHeight = 18, MinWidth = 92, Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
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
            var info = Mono("", 7, TextTertiary); info.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => info.Text = NotaNum.F($"HP {HpF(P(ScHP))} · LP {LpF(P(ScLP))} · Q {P(ScQ):0.0}"));
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 6, Height = 18 };
            head.Children.Add(Caps("KEY"));
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

            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("46,46,46,46,46,*"), ColumnSpacing = 3, Height = 52 };
            g.Children.Add(K(ScHP, "HP", HpF, Log(ScHP), emphasised: true, cellW: 46));
            g.Children.Add(Col(K(ScLP, "LP", LpF, Log(ScLP), emphasised: true, cellW: 46), 1));
            g.Children.Add(Col(K(ScQ, "Q", v => NotaNum.F($"{v:0.00}"), Log(ScQ), cellW: 46), 2));
            g.Children.Add(Col(K(ScGain, "SC GAIN", SDb, Lin(ScGain), Teal, cellW: 46), 3));
            g.Children.Add(Col(K(Hold, "HOLD", v => v < 0.5 ? "off" : Ms(v), Sq(Hold), Teal, cellW: 46), 4));
            var s1 = Mono("", 7, TextTertiary); s1.HorizontalAlignment = HorizontalAlignment.Right;
            var s2 = Mono("", 7, TextTertiary); s2.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() =>
            {
                double kdb = KeyDb();
                s1.Text = kdb <= -99 ? "key silent" : key.PeakHz > 0 ? NotaNum.F($"key {Lvl(kdb)} dB · peak {Hz(key.PeakHz)}") : NotaNum.F($"key {Lvl(kdb)} dB");
                double barMs = 4 * 60000.0 / Bpm();
                s2.Text = NotaNum.F($"{Hits(EnvN) * barMs / EnvN:0.#} triggers / bar");
            });
            g.Children.Add(Col(new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { s1, s2 } }, 5));

            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 5, 8, 4), Children = {
                Docked(head, Dock.Top), Docked(g, Dock.Bottom), new Border { Margin = new Thickness(0, 5, 0, 5), Child = key } } };
        }

        // ======================================================================
        // RIGHT — Dynamics / Output
        // ======================================================================
        Border GrBox(bool levels)
        {
            var title = Caps(levels ? "OUTPUT" : "REDUCTION");
            var stack = new StackPanel { Spacing = 2, Children = { title } };
            var box = new Border { Background = Sunken, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(NotaRadius.ControlValue), Padding = new Thickness(6, 4), Child = stack };
            if (levels)
            {
                Control Line(string name, Func<string> v, Func<IBrush> ink)
                {
                    var val = Mono("", 8, TextPrimary); val.HorizontalAlignment = HorizontalAlignment.Right;
                    readouts.Add(() => { val.Text = v(); val.Foreground = ink(); });
                    var gg = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                    gg.Children.Add(new TextBlock { Text = name, FontSize = 8, Foreground = TextSecondary });
                    gg.Children.Add(Col(val, 1));
                    return gg;
                }
                stack.Children.Add(Line("in", () => NotaNum.F($"{Lvl(LinDb(Sc(S_InRms)))} dB"), () => TextPrimary));
                stack.Children.Add(Line("out", () => NotaNum.F($"{Lvl(LinDb(Sc(S_OutRms)))} dB"), () => Sc(S_OutPk) > 0.5 ? NotaPalette.Warning : TextPrimary));
                stack.Children.Add(Line("GR", () => NotaNum.F($"{Gr(Sc(S_Gr))} dB"), () => AccentBright));
            }
            else
            {
                var now = Mono("", 9, AccentBright);
                var set = Mono("", 8, TextSecondary);
                stack.Children.Add(now); stack.Children.Add(set);
                readouts.Add(() =>
                {
                    now.Text = NotaNum.F($"{Gr(Sc(S_Gr))} dB · peak {Gr(grHist.Peak)}");
                    set.Text = NotaNum.F($"thresh {P(Threshold):0.0} · {RatioF(P(Ratio))} · knee {P(Knee):0.#}");
                    bool hot = grHist.Peak >= 10;
                    box.BorderBrush = hot ? NotaPalette.BorderBrass : NotaPalette.GraphBorder;
                    title.Foreground = hot ? AccentBright : TextTertiary;
                });
            }
            return box;
        }

        Control DynamicsTab()
        {
            var det = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*"), VerticalAlignment = VerticalAlignment.Center };
            det.Children.Add(Caps("DETECT"));
            var detSeg = Seg(Detection, Detects);
            ToolTip.SetTip(detSeg, "Detector — Peak reacts to every transient, RMS to the body, Auto follows the body and still catches the spikes");
            det.Children.Add(Col(detSeg, 1));
            var rows = new Control[]
            {
                det,
                SliderRow("RATIO", Ratio, RatioF, Log(Ratio)),
                SliderRow("MIX", Mix, PctF, Lin(Mix)),
                Rule(),
                SliderRow("RANGE", Range, RangeF, RangeMap(), teal: true),
                GrBox(false),
                new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
                    Toggle(AutoRelease, "Auto-release", "Auto-release — a slower second release once the reduction runs deep, so sustained material does not pump"),
                    Toggle(AutoGain, "Auto gain", "Auto gain — make-up that follows the threshold and ratio") } },
            };
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,*,*,Auto,*,Auto,*"), RowSpacing = 2 };
            for (int r = 0; r < rows.Length; r++) grid.Children.Add(GRow(rows[r], r));
            return new Border { Padding = new Thickness(8, 5), Child = grid };
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
                SliderRow("MAKEUP", Makeup, v => NotaNum.F($"{v:+0.0;−0.0;0.0}"), Lin(Makeup)),
                SliderRow("MIX", Mix, PctF, Lin(Mix)),
                SliderRow("RANGE", Range, RangeF, RangeMap(), teal: true),
                new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 4, 0, 0), Child = btns },
                new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
                    Toggle(External, "External key", "External key — key off the source picked on the Sidechain tab; off, the track keys itself"),
                    Toggle(StereoLink, "Stereo link", "Stereo link — one reduction for both channels; off, left and right compress on their own") } },
            };
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,*,*,Auto,*"), RowSpacing = 3 };
            for (int r = 0; r < rows.Length; r++) grid.Children.Add(GRow(rows[r], r));
            return new Border { Padding = new Thickness(8, 5), Child = grid };
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[2];
        var extras = Mono("", 7, TextTertiary);
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

        var centre = TabFrame(new[] { "Curve", "Motion", "Sidechain" }, centreHost, CentreBody, false, t => { centreTab = t; Refresh(); }, extras);
        var rightFrame = TabFrame(new[] { "Dynamics", "Output" }, rightHost, RightBody, true, _ => Refresh(), null);
        var right = new Border { Width = 186, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = rightFrame };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Margin = new Thickness(5, 0), ClipToBounds = true, Child = centre };

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
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

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
                    tb.Foreground = on ? AccentBright : TextTertiary;
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
