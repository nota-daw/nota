// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Reverb body (device kind 2), a build of the "Nota Reverb"
// mockup (700 × 260) on the Chamber / Delay frame: an always-visible TAIL column (Decay ·
// HF Damp), a centre tabbed panel (Space / Tone · Mod) over the live decay-tail window, a
// right tabbed panel (Levels / Output) and a status strip. The tail window is a control,
// not decoration (drag the pre-delay marker, drag the tail for the decay). Every control is
// a device param, so automation / MIDI learn / presets / A-B / persistence come for free;
// Kill tail is the one action that is not (it empties the reverb's buffers).
// FullBleed — the shared shell draws the header (name · preset · badge · bypass).

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

internal sealed class ReverbDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Reverb.h) ──────────────────────────────
    private const int Decay = 0, HFDamp = 1, PreDelay = 2, Size = 3, Diffusion = 4, LowCut = 5, HighCut = 6,
        WidthP = 7, ModRate = 8, ModDepth = 9, Algorithm = 10, Freeze = 11, DryWet = 12, Output = 13,
        DryLevel = 14, EarlyRefl = 15, ModOnTail = 16, Vintage = 17, BassMono = 18, WetOnly = 19, LatencyComp = 20;
    // Scope telemetry layout (Reverb::S_*).
    private const int S_WetPk = 0, S_OutL = 1, S_OutR = 2, S_Cpu = 3, S_SampleRate = 4, S_Latency = 5,
        S_DiffuseSmp = 8, kScope = 11;   // 6 RT60 · 7 effective pre-delay ms · 9 frozen · 10 early on
    private const int A_KillTail = 0;

    private static readonly string[] Algos = { "Hall", "Room", "Plate", "Chamber" };
    // The early-reflection pattern — mirrors Reverb.h (kErMs · kErGain · kErScale).
    private static readonly double[] ErMs = { 7.3, 11.9, 16.1, 23.4, 29.7, 37.9, 47.3, 61.1 };
    private static readonly float[] ErGain = { 1.0f, 0.78f, 0.86f, 0.62f, 0.66f, 0.5f, 0.42f, 0.34f };
    private static readonly double[] ErScale = { 1.0, 0.45, 0.28, 0.7 };
    private const double LoopSec = 3.4;   // one flash-and-fade of the tail window

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "REVERB";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) => engine.DeviceSetParam(track, di, p, Math.Clamp(v, 0f, 1f));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        bool On(int p) => P(p) >= 0.5f;
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        // ---- formatters -------------------------------------------------------------
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string Pct(double v) => NotaNum.F($"{v * 100:0} %");
        static string Hz(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0} kHz") : NotaNum.F($"{hz:0} Hz");
        static string ShortDb(double db) => db <= -99 ? "−∞" : NotaNum.F($"{db:0.0;−0.0;0.0}");
        static string Db(double db) => db <= -99 ? "−∞" : NotaNum.F($"{db:+0.0;−0.0;0.0} dB");
        static double OutDb(double v) => v <= 0.0005 ? -100 : 20 * Math.Log10(v * 2);
        static double DryDb(double v) { double g = 2 * v * v; return g <= 1e-5 ? -100 : 20 * Math.Log10(g); }
        static double Rt60(double v) => Exp(v, 0.2, 12);
        static string Secs(double s) => s >= 10 ? NotaNum.F($"{s:0.0} s") : NotaNum.F($"{s:0.00} s");
        static double LowHz(double v) => Exp(v, 20, 1000);
        static double HighHz(double v) => Exp(v, 1000, 20000);
        static string RateF(double v) { double hz = Exp(v, 0.05, 5); return hz >= 1 ? NotaNum.F($"{hz:0.0} Hz") : NotaNum.F($"{hz:0.00} Hz"); }
        static string PreF(double v) => NotaNum.F($"{v * 200:0} ms");
        string DecayF(double v) => On(Freeze) ? "HOLD" : Secs(Rt60(v));
        string Algo() => Algos[Sel(Algorithm, 4)];

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
        { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static Border Divider(Control child, double top = 5) => new()
        { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, double size = 34, double cellW = 50, IBrush? arc = null)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.ValueChanged += v => { Raw(p, (float)v); val.Text = fmt(v); SyncTail(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            readouts.Add(() => { if (knob.Dragging) return; float c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(c); });
            return KnobCell(name, knob, val, cellW);
        }

        // On/off switch bound to a param (> 0.5 = on).
        Control Toggle(int p, string label)
        {
            var wrap = Switch(label, () => On(p), () => { SetP(p, On(p) ? 0f : 1f); SyncTail(); }, out var sync);
            readouts.Add(sync);
            MidiLearn.Bind(wrap, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return wrap;
        }

        // Segmented chips over a discrete param (n options spread over 0..1).
        Control Seg(int p, string[] names)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); SyncTail(); }, out var sync, padX: 8);
            readouts.Add(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }

        // A small text button (an action, or a latch drawn as a button).
        Border Btn(string text, Action click, string? tip = null)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { Height = 16, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Badge, Background = Raised, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); e.Handled = true; };
            if (tip != null) ToolTip.SetTip(b, tip);
            return b;
        }

        // Horizontal bar slider over a param, with a caps label.
        Control SliderRow(string label, int p, Func<double, string> fmt, Func<bool>? dim = null, Func<bool>? hot = null)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, (float)v); SyncTail(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), dim: dim, valueWidth: 34);
            readouts.Add(sync);
            MidiLearn.Bind(bar, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*"), VerticalAlignment = VerticalAlignment.Center };
            var lb = Caps(label);
            if (hot is not null) readouts.Add(() => lb.Foreground = hot() ? AccentBright : TextTertiary);
            g.Children.Add(lb);
            g.Children.Add(Col(bar, 1));
            return g;
        }

        // ======================================================================
        // The decay-tail window — shared by both centre tabs
        // ======================================================================
        // One view per centre tab — a control has one parent, and both tabs show the window.
        var tails = new List<ReverbTailView>();
        var start = DateTime.UtcNow;
        ReverbTailView NewTail()
        {
            var t = new ReverbTailView { DecayValue = () => P(Decay) };
            t.Changed += (target, v) => { Raw(target == 0 ? Decay : PreDelay, (float)v); SyncTail(); };
            t.GestureBegin += target => Begin(target == 0 ? Decay : PreDelay);
            t.GestureEnd += target => End(target == 0 ? Decay : PreDelay);
            t.Reset += target => { Reset(target == 0 ? Decay : PreDelay); SyncTail(); };
            ToolTip.SetTip(t, "The decay tail — the impulse at the pre-delay (teal), the early reflections (drawn magnified), then the RT60 curve over 4 s. Drag the teal marker for the pre-delay, drag the tail for the decay; double-click to reset.");
            MidiLearn.Bind(t, MidiTarget.DeviceParam(track, di, Decay), "Decay");
            tails.Add(t);
            return t;
        }
        double[] erBuf = new double[ErMs.Length];
        void SyncTail()
        {
            if (tails.Count == 0) return;
            bool freeze = On(Freeze);
            // Frozen, the reflections stay drawn (held, as the mockup shows) even though the
            // muted input feeds them nothing new.
            bool early = On(EarlyRefl);
            double scale = ErScale[Sel(Algorithm, 4)] * (0.5 + P(Size) * 0.8);
            for (int k = 0; k < ErMs.Length; k++) erBuf[k] = ErMs[k] * scale;
            double[] er = early ? erBuf : Array.Empty<double>();
            foreach (var t in tails) t.Set(freeze ? -1 : Rt60(P(Decay)), freeze, P(PreDelay), P(PreDelay) * 200, er, ErGain);
        }
        readouts.Add(() =>
        {
            SyncTail();
            // The window loops only while the reverb is actually making sound (or is held).
            bool sounding = Sc(S_WetPk) > 2e-4 || On(Freeze);
            double ph = sounding ? (DateTime.UtcNow - start).TotalSeconds % LoopSec / LoopSec : -1;
            foreach (var t in tails) t.SetPhase(ph);
        });

        // ======================================================================
        // LEFT — TAIL column (Decay · HF Damp)
        // ======================================================================
        Control TailFader(int p, string label, IBrush ink, IBrush handle, string tip)
        {
            var f = new DelayFader { Ink = ink, Handle = handle, Width = 16, VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Center, Default = engine.DeviceParamDefault(track, di, p) };
            f.Changed += v => { Raw(p, (float)v); SyncTail(); };
            f.GestureBegin += () => Begin(p);
            f.GestureEnd += () => End(p);
            MidiLearn.Bind(f, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            ToolTip.SetTip(f, tip);
            var lb = Caps(label); lb.HorizontalAlignment = HorizontalAlignment.Center; lb.Margin = new Thickness(0, 3, 0, 0);
            readouts.Add(() =>
            {
                bool hold = On(Freeze) && p == Decay;
                f.Set(hold ? 1 : P(p));
                lb.Foreground = hold ? AccentBright : TextTertiary;
            });
            return new DockPanel { Width = 29, Children = { Docked(lb, Dock.Bottom), f } };
        }
        var tailTitle = Caps("TAIL"); tailTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var faders = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 1, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 3) };
        faders.Children.Add(TailFader(Decay, "DECAY", NotaPalette.Accent, NotaPalette.AccentBright,
            "Decay — RT60, the time the tail takes to fall 60 dB (0.2 … 12 s); Freeze holds it forever. Double-click resets."));
        faders.Children.Add(Col(TailFader(HFDamp, "DAMP", NotaPalette.Teal, NotaPalette.TealBright,
            "HF Damp — how much faster the highs die than the lows. Double-click resets."), 1));
        var decVal = Mono("", 7, AccentBright); decVal.HorizontalAlignment = HorizontalAlignment.Center;
        var dmpVal = Mono("", 7, TextPrimary); dmpVal.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() =>
        {
            decVal.Text = DecayF(P(Decay));
            dmpVal.Text = Pct(P(HFDamp));
            tailTitle.Foreground = On(Freeze) ? AccentBright : TextTertiary;
        });
        var tailCol = new Border
        {
            Width = 64, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(tailTitle, Dock.Top), Docked(dmpVal, Dock.Bottom), Docked(decVal, Dock.Bottom), faders } },
        };
        readouts.Add(() => tailCol.BorderBrush = On(Freeze) ? NotaPalette.BorderBrass : BorderDef);
        DockPanel.SetDock(tailCol, Dock.Left);

        // ======================================================================
        // CENTRE — the mode row, shared by both tabs
        // ======================================================================
        Control ModeRow()
        {
            var hint = Mono("", 7, TextTertiary); hint.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => hint.Text = Sel(Algorithm, 4) == 2
                ? NotaNum.F($"maximum density · size {Pct(P(Size))}")
                : NotaNum.F($"size {Pct(P(Size))} · diffuse {Pct(P(Diffusion))}"));
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6, Height = 18 };
            head.Children.Add(Caps("MODE"));
            var seg = Seg(Algorithm, Algos);
            ToolTip.SetTip(seg, "Algorithm — Hall (large, spaced reflections) · Room (small, close walls) · Plate (dense, no room at all) · Chamber (between)");
            head.Children.Add(Col(seg, 1));
            head.Children.Add(Col(hint, 2));
            return head;
        }

        Control CentreTab(Control knobs)
        {
            var bottom = new Border { Height = 54, Margin = new Thickness(0, 4, 0, 0), Child = knobs };
            return new DockPanel { LastChildFill = true, Margin = new Thickness(8, 5, 8, 3), Children = {
                Docked(ModeRow(), Dock.Top),
                Docked(bottom, Dock.Bottom),
                new Border { Margin = new Thickness(0, 5, 0, 0), Child = NewTail() } } };
        }

        Control SpaceTab()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto") };
            g.Children.Add(K(PreDelay, "PRE", PreF));
            g.Children.Add(Col(K(Size, "SIZE", Pct), 1));
            g.Children.Add(Col(K(Diffusion, "DIFFUSE", Pct), 2));
            g.Children.Add(Col(K(Decay, "DECAY", DecayF), 3));
            var er = Toggle(EarlyRefl, "Early reflections");
            ToolTip.SetTip(er, "Early reflections — the first discrete echoes off the walls, spaced by the algorithm and the size");
            var fr = Toggle(Freeze, "Freeze");
            ToolTip.SetTip(fr, "Freeze — hold the tail forever; the input is muted and nothing decays");
            g.Children.Add(Col(new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { er, fr } }, 5));
            return CentreTab(g);
        }

        Control ToneTab()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto") };
            g.Children.Add(K(LowCut, "LOW CUT", v => Hz(LowHz(v)), arc: Teal));
            g.Children.Add(Col(K(HighCut, "HIGH CUT", v => Hz(HighHz(v)), arc: Teal), 1));
            g.Children.Add(Col(K(WidthP, "WIDTH", Pct), 2));
            g.Children.Add(Col(K(ModRate, "MOD RATE", RateF, arc: NotaPalette.Rose), 3));
            g.Children.Add(Col(K(ModDepth, "MOD DEPTH", Pct, arc: NotaPalette.Rose), 4));
            var mt = Toggle(ModOnTail, "Mod on tail");
            ToolTip.SetTip(mt, "Mod on tail — the modulation wobbles the tail itself (a chorused, evolving decay); off, it moves only the early part and the tail stays still");
            var vi = Toggle(Vintage, "Vintage");
            ToolTip.SetTip(vi, "Vintage — an early-digital colour: a band-limited input, 12-bit wet and a deeper wobble");
            g.Children.Add(Col(new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { mt, vi } }, 6));
            return CentreTab(g);
        }

        // ======================================================================
        // RIGHT — Levels / Output
        // ======================================================================
        Control LevelsTab()
        {
            var wetBar = new PentadLevelBar { Height = 4, VerticalAlignment = VerticalAlignment.Center };
            var wetDb = Mono("", 8, TextPrimary); wetDb.Width = 30; wetDb.TextAlignment = TextAlignment.Right;
            readouts.Add(() =>
            {
                double pk = Sc(S_WetPk);
                wetBar.SetLinear(pk);
                double db = pk > 1e-5 ? 20 * Math.Log10(pk) : -100;
                wetDb.Text = ShortDb(db);
                wetDb.Foreground = db > -6 ? NotaPalette.Warning : TextPrimary;
            });
            var wet = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            wet.Children.Add(Caps("WET")); wet.Children.Add(Col(wetBar, 1)); wet.Children.Add(Col(wetDb, 2));

            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var freezeBtn = Btn("Freeze", () => { SetP(Freeze, On(Freeze) ? 0f : 1f); SyncTail(); }, "Freeze — hold the tail forever; the input is muted and nothing decays");
            var freezeTxt = (TextBlock)freezeBtn.Child!;
            readouts.Add(() =>
            {
                bool on = On(Freeze);
                freezeBtn.Background = on ? NotaPalette.AccentSubtle : Raised;
                freezeBtn.BorderBrush = on ? NotaPalette.BorderBrass : Brushes.Transparent;
                freezeTxt.Foreground = on ? NotaPalette.AccentHover : TextPrimary;
                freezeTxt.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            });
            MidiLearn.Bind(freezeBtn, MidiTarget.DeviceParam(track, di, Freeze), "Freeze");
            btns.Children.Add(freezeBtn);
            btns.Children.Add(Col(Btn("Kill tail", () => engine.DeviceAction(track, di, A_KillTail, 0, 0), "Kill tail — empty the reverb at once; whatever is still ringing (or frozen) stops"), 1));

            var rows = new Control[]
            {
                SliderRow("MIX", DryWet, Pct, () => On(WetOnly), () => On(WetOnly)),
                SliderRow("DRY", DryLevel, v => ShortDb(DryDb(v)), () => On(WetOnly)),
                SliderRow("OUT", Output, v => ShortDb(OutDb(v))),
                new Border { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 2) },
                SliderRow("DECAY", Decay, DecayF, null, () => On(Freeze)),
                SliderRow("HF DAMP", HFDamp, Pct),
                new Border { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 2) },
                btns, wet,
            };
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,*,*,Auto,*,*,Auto,*,*") };
            for (int r = 0; r < rows.Length; r++) grid.Children.Add(GRow(rows[r], r));
            return new Border { Padding = new Thickness(8, 4), Child = grid };
        }

        Control OutputTab()
        {
            var meterL = new PentadLevelBar { Vertical = true, Width = 11, Height = 56 };
            var meterR = new PentadLevelBar { Vertical = true, Width = 11, Height = 56 };
            readouts.Add(() => { meterL.SetLinear(Sc(S_OutL)); meterR.SetLinear(Sc(S_OutR)); });
            static Control Centre(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }
            var scale = new Grid { RowDefinitions = new RowDefinitions("*,*,*"), Height = 56, Children = { Mono("0", 7, TextDisabled), GRow(Mono("−12", 7, TextDisabled), 1), GRow(Mono("−48", 7, TextDisabled), 2) } };
            var meters = Row(4,
                new StackPanel { Spacing = 2, Children = { meterL, Centre(Caps("L")) } },
                new StackPanel { Spacing = 2, Children = { meterR, Centre(Caps("R")) } },
                scale);
            meters.VerticalAlignment = VerticalAlignment.Top;
            var top = Row(8, K(Output, "OUT", v => Db(OutDb(v)), 40, 54), meters);

            var mid = new StackPanel { Spacing = 6, Children = {
                SliderRow("MIX", DryWet, Pct, () => On(WetOnly)),
                SliderRow("WIDTH", WidthP, Pct),
                SliderRow("MONO f", BassMono, v => v <= 0.001 ? "off" : NotaNum.F($"{Exp(v, 30, 500):0}")) } };
            ToolTip.SetTip(mid.Children[2], "Bass mono — below this frequency the tail is mono, so a wide reverb does not smear the low end (off at the far left)");

            var wetOnly = Toggle(WetOnly, "Wet only (send)");
            ToolTip.SetTip(wetOnly, "Wet only — the tail with no dry, for a return / send track");
            var comp = Toggle(LatencyComp, "Latency comp.");
            ToolTip.SetTip(comp, "The input diffuser delays the tail by a fixed group delay; on, the pre-delay is shortened by exactly that so the tail starts on the set pre-delay (it cannot go below zero).");
            var bottom = new StackPanel { Spacing = 5, Children = { wetOnly, comp } };

            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(top, Dock.Top), Docked(Divider(mid), Dock.Top), Docked(Divider(bottom), Dock.Bottom) } };
            ((Control)body.Children[1]).Margin = new Thickness(0, 5, 0, 0);
            return new Border { Padding = new Thickness(8, 5), Child = body };
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[2];
        var rightBodies = new Control?[2];
        int centreTab = 0;
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            if (On(Freeze))
            {
                extras.Text = "input muted · tail held";
                extras.Foreground = AccentBright;
                return;
            }
            extras.Foreground = TextTertiary;
            extras.Text = centreTab == 0
                ? NotaNum.F($"RT60 {Secs(Rt60(P(Decay)))} · {P(PreDelay) * 200:0} ms pre")
                : NotaNum.F($"low cut {Hz(LowHz(P(LowCut)))} · high cut {Hz(HighHz(P(HighCut)))}");
        });
        Control CentreBody(int t) => centreBodies[t] ??= t == 1 ? ToneTab() : SpaceTab();
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : LevelsTab();

        var centre = TabFrame(new[] { "Space", "Tone · Mod" }, centreHost, CentreBody, false, t => { centreTab = t; foreach (var a in readouts) a(); }, extras);
        var rightFrame = TabFrame(new[] { "Levels", "Output" }, rightHost, RightBody, true, _ => { foreach (var a in readouts) a(); }, null);
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
            if (On(Freeze))
                return NotaNum.F($"Freeze: tail held, input muted · damp {Pct(P(HFDamp))}");
            string rt = NotaNum.F($"{Algo()} · RT60 {Secs(Rt60(P(Decay)))}");
            string vin = On(Vintage) ? " · vintage" : "";
            return centreTab == 0
                ? NotaNum.F($"{rt} · pre {P(PreDelay) * 200:0} ms · diffuse {Pct(P(Diffusion))}{(On(EarlyRefl) ? "" : " · no early reflections")}")
                : NotaNum.F($"{rt} · mod {RateF(P(ModRate))} / {Pct(P(ModDepth))}{(On(ModOnTail) ? "" : " (early part)")} · width {Pct(P(WidthP))}{vin}");
        }
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            statusLeft.Foreground = On(Freeze) ? AccentBright : TextSecondary;
            double sr = Sc(S_SampleRate), diff = Sc(S_DiffuseSmp);
            // How much of the diffuser delay the pre-delay could actually absorb.
            double preSmp = P(PreDelay) * 0.2 * sr;
            string comp = diff < 1 ? "" : On(LatencyComp)
                ? NotaNum.F($" · diffuser −{Math.Min(diff, preSmp):0} smp compensated")
                : NotaNum.F($" · diffuser +{diff:0} smp");
            statusRight.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#} kHz · latency {Sc(S_Latency):0} smp{comp} · CPU {Sc(S_Cpu) * 100:0.0} %") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { tailCol, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
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
                b.PointerPressed += (_, _) => { sel = iv; Hi(); host.Content = body(iv); changed?.Invoke(iv); };
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
