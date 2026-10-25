// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Reverb body (device kind 2), a build of the "Nota Reverb"
// picture of the style guide (700 × 260): an always-visible Tail rail (Decay · HF Damp as
// upright value bars), a centre tabbed panel (Space / Tone · Mod) over the live decay-tail
// window and a right tabbed panel (Levels / Output), each a pressed-in section. The tail window is a control,
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
    public string? Subtitle => "Reverb";

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
        static TextBlock Caps(string t, IBrush? c = null) => DeviceBodyKit.Label(t, c);
        static TextBlock Mono(string t, IBrush c) => DeviceBodyKit.Value(t, c);
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static Border Divider(Control child, double top = 6) => new()
        { BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, double cellW = 64, bool inline = false)
        {
            var val = Mono(fmt(P(p)), TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Inline = inline, Default = engine.DeviceParamDefault(track, di, p) };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
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

        // A raised text button (an action, or a latch drawn as a button).
        Border Btn(string text, Action click, string? tip = null)
        {
            var b = TextButton(text);
            b.HorizontalAlignment = HorizontalAlignment.Stretch; b.VerticalAlignment = VerticalAlignment.Center;
            ((TextBlock)b.Child!).HorizontalAlignment = HorizontalAlignment.Center;
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); e.Handled = true; };
            if (tip != null) ToolTip.SetTip(b, tip);
            return b;
        }

        // Horizontal bar slider over a param, with a caps label.
        Control SliderRow(string label, int p, Func<double, string> fmt, Func<bool>? dim = null, Func<bool>? hot = null)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, (float)v); SyncTail(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), dim: dim, valueWidth: 52);
            readouts.Add(sync);
            MidiLearn.Bind(bar, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("54,*"), VerticalAlignment = VerticalAlignment.Center };
            var lb = Caps(label);
            if (hot is not null) readouts.Add(() => lb.Foreground = hot() ? TextPrimary : TextTertiary);
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
            ToolTip.SetTip(t, "The decay tail — the impulse at the pre-delay, the early reflections (drawn magnified), then the RT60 curve over 4 s. Drag the pre-delay marker, drag the tail for the decay; double-click to reset.");
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
        // LEFT — Tail column (Decay · HF Damp)
        // ======================================================================
        DeviceBodyKit.RailFader TailFader(int p, string label, Func<string> text, string tip)
        {
            var f = DeviceBodyKit.Upright(label, () => On(Freeze) && p == Decay ? 1 : P(p), v => { Raw(p, (float)v); SyncTail(); }, text, out var val, out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => { Reset(p); SyncTail(); });
            MidiLearn.Bind(f, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            ToolTip.SetTip(f, tip);
            readouts.Add(sync);
            return new DeviceBodyKit.RailFader(label, f, val);
        }
        var tailBody = DeviceBodyKit.FaderRail("Tail", true, out var tailLbls,
            TailFader(Decay, "Decay", () => DecayF(P(Decay)), "Decay — RT60, the time the tail takes to fall 60\u2009dB (0.2 … 12\u2009s); Freeze holds it forever. Double-click resets."),
            TailFader(HFDamp, "Damp", () => Pct(P(HFDamp)), "HF Damp — how much faster the highs die than the lows. Double-click resets."));
        var tailTitle = (TextBlock)((DockPanel)tailBody).Children[0];
        // Freeze shows on the rail's title and the Decay label (live, orange), not on a frame.
        readouts.Add(() =>
        {
            tailTitle.Foreground = On(Freeze) ? AccentBright : TextTertiary;
            tailLbls[0].Foreground = On(Freeze) ? AccentBright : TextTertiary;
        });
        var tailCol = DeviceBodyKit.Sec(tailBody, new Thickness(8, 8, 8, 6), 80);   // wide enough that "Decay" and "Damp" do not touch
        DockPanel.SetDock(tailCol, Dock.Left);

        // ======================================================================
        // CENTRE — the mode row, shared by both tabs
        // ======================================================================
        Control ModeRow()
        {
            var hint = Caps("", TextSecondary); hint.HorizontalAlignment = HorizontalAlignment.Right;
            readouts.Add(() => hint.Text = Sel(Algorithm, 4) == 2
                ? NotaNum.F($"maximum density · size {Pct(P(Size))}")
                : NotaNum.F($"size {Pct(P(Size))} · diffuse {Pct(P(Diffusion))}"));
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 10, Margin = new Thickness(0, 6, 0, 0) };
            head.Children.Add(Caps("Mode"));
            var seg = Seg(Algorithm, Algos);
            ToolTip.SetTip(seg, "Algorithm — Hall (large, spaced reflections) · Room (small, close walls) · Plate (dense, no room at all) · Chamber (between)");
            head.Children.Add(Col(seg, 1));
            head.Children.Add(Col(hint, 2));
            return head;
        }

        Control CentreTab(Control knobs)
        {
            var bottom = new Border { Margin = new Thickness(0, 6, 0, 0), Child = knobs };
            return new DockPanel { LastChildFill = true, Children = {
                Docked(ModeRow(), Dock.Top),
                Docked(bottom, Dock.Bottom),
                new Border { Margin = new Thickness(0, 6, 0, 0), Child = NewTail() } } };
        }

        Control SpaceTab()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto") };
            // 58-wide cells leave the switches' labels room beside four knobs.
            g.Children.Add(K(PreDelay, "Pre", PreF, 58));
            g.Children.Add(Col(K(Size, "Size", Pct, 58), 1));
            g.Children.Add(Col(K(Diffusion, "Diffuse", Pct, 58), 2));
            g.Children.Add(Col(K(Decay, "Decay", DecayF, 58), 3));
            var er = Toggle(EarlyRefl, "Early reflections");
            ToolTip.SetTip(er, "Early reflections — the first discrete echoes off the walls, spaced by the algorithm and the size");
            var fr = Toggle(Freeze, "Freeze");
            ToolTip.SetTip(fr, "Freeze — hold the tail forever; the input is muted and nothing decays");
            g.Children.Add(Col(new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { er, fr } }, 5));
            return CentreTab(g);
        }

        Control ToneTab()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto") };
            g.Children.Add(K(LowCut, "Low cut", v => Hz(LowHz(v)), 56));
            g.Children.Add(Col(K(HighCut, "High cut", v => Hz(HighHz(v)), 56), 1));
            g.Children.Add(Col(K(WidthP, "Width", Pct, 56), 2));
            g.Children.Add(Col(K(ModRate, "Mod rate", RateF, 56), 3));
            g.Children.Add(Col(K(ModDepth, "Mod depth", Pct, 56), 4));
            var mt = Toggle(ModOnTail, "Mod on tail");
            ToolTip.SetTip(mt, "Mod on tail — the modulation wobbles the tail itself (a chorused, evolving decay); off, it moves only the early part and the tail stays still");
            var vi = Toggle(Vintage, "Vintage");
            ToolTip.SetTip(vi, "Vintage — an early-digital colour: a band-limited input, 12-bit wet and a deeper wobble");
            g.Children.Add(Col(new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { mt, vi } }, 6));
            return CentreTab(g);
        }

        // ======================================================================
        // RIGHT — Levels / Output
        // ======================================================================
        Control LevelsTab()
        {
            var wetBar = new PentadLevelBar { Height = 3, VerticalAlignment = VerticalAlignment.Center };
            var wetDb = Mono("", TextPrimary); wetDb.Width = 52; wetDb.TextAlignment = TextAlignment.Right;
            readouts.Add(() =>
            {
                double pk = Sc(S_WetPk);
                wetBar.SetLinear(pk);
                double db = pk > 1e-5 ? 20 * Math.Log10(pk) : -100;
                wetDb.Text = ShortDb(db);
                wetDb.Foreground = db > -6 ? NotaPalette.Warning : TextPrimary;
            });
            var wet = new Grid { ColumnDefinitions = new ColumnDefinitions("54,*,Auto"), ColumnSpacing = 0, VerticalAlignment = VerticalAlignment.Center };
            wet.Children.Add(Caps("Wet")); wetDb.Margin = new Thickness(8, 0, 0, 0); wet.Children.Add(Col(wetBar, 1)); wet.Children.Add(Col(wetDb, 2));

            var btns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
            var freezeBtn = Btn("Freeze", () => { SetP(Freeze, On(Freeze) ? 0f : 1f); SyncTail(); }, "Freeze — hold the tail forever; the input is muted and nothing decays");
            var freezeTxt = (TextBlock)freezeBtn.Child!;
            readouts.Add(() =>
            {
                bool on = On(Freeze);
                freezeBtn.Background = on ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
                freezeTxt.Foreground = on ? AccentBright : TextPrimary;
                freezeTxt.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            });
            MidiLearn.Bind(freezeBtn, MidiTarget.DeviceParam(track, di, Freeze), "Freeze");
            btns.Children.Add(freezeBtn);
            btns.Children.Add(Col(Btn("Kill tail", () => engine.DeviceAction(track, di, A_KillTail, 0, 0), "Kill tail — empty the reverb at once; whatever is still ringing (or frozen) stops"), 1));

            var rows = new Control[]
            {
                SliderRow("Mix", DryWet, Pct, () => On(WetOnly), () => On(WetOnly)),
                SliderRow("Dry", DryLevel, v => ShortDb(DryDb(v)), () => On(WetOnly)),
                SliderRow("Out", Output, v => ShortDb(OutDb(v))),
                DeviceBodyKit.Rule(3),
                SliderRow("Decay", Decay, DecayF, null, () => On(Freeze)),
                SliderRow("HF damp", HFDamp, Pct),
                DeviceBodyKit.Rule(3),
                btns, wet,
            };
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,*,*,Auto,*,*,Auto,*,*") };
            for (int r = 0; r < rows.Length; r++) grid.Children.Add(GRow(rows[r], r));
            return new Border { Padding = new Thickness(0, 4, 0, 0), Child = grid };
        }

        Control OutputTab()
        {
            // 190 high: the knob and meter in one 36 row, three bars, two switches.
            var top = DeviceBodyKit.OutputHead(K(Output, "Out", v => Db(OutDb(v)), 96, inline: true), () => Sc(S_OutL), () => Sc(S_OutR), readouts.Add);
            top.Margin = new Thickness(0, 4, 0, 0);

            var mid = new Grid { RowDefinitions = new RowDefinitions("26,26,26"), Children = {
                SliderRow("Mix", DryWet, Pct, () => On(WetOnly)),
                GRow(SliderRow("Width", WidthP, Pct), 1),
                GRow(SliderRow("Mono f", BassMono, v => v <= 0.001 ? "off" : NotaNum.F($"{Exp(v, 30, 500):0}")), 2) } };
            ToolTip.SetTip(mid.Children[2], "Bass mono — below this frequency the tail is mono, so a wide reverb does not smear the low end (off at the far left)");

            var wetOnly = Toggle(WetOnly, "Wet only (send)");
            ToolTip.SetTip(wetOnly, "Wet only — the tail with no dry, for a return / send track");
            var comp = Toggle(LatencyComp, "Latency comp.");
            ToolTip.SetTip(comp, "The input diffuser delays the tail by a fixed group delay; on, the pre-delay is shortened by exactly that so the tail starts on the set pre-delay (it cannot go below zero).");
            var bottom = new StackPanel { Spacing = 3, Children = { wetOnly, comp } };

            var body = new DockPanel { LastChildFill = false, Children = {
                Docked(top, Dock.Top), Docked(Divider(mid), Dock.Top), Docked(Divider(bottom), Dock.Bottom) } };
            ((Control)body.Children[1]).Margin = new Thickness(0, 4, 0, 0);
            return body;
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[2];
        var rightBodies = new Control?[2];
        int centreTab = 0;
        var extras = Caps("", TextSecondary);
        readouts.Add(() =>
        {
            if (On(Freeze))
            {
                extras.Text = "input muted · tail held";
                extras.Foreground = AccentBright;
                return;
            }
            extras.Foreground = TextSecondary;
            extras.Text = centreTab == 0
                ? NotaNum.F($"RT60 {Secs(Rt60(P(Decay)))} · {P(PreDelay) * 200:0} ms pre")
                : NotaNum.F($"low cut {Hz(LowHz(P(LowCut)))} · high cut {Hz(HighHz(P(HighCut)))}");
        });
        Control CentreBody(int t) => centreBodies[t] ??= t == 1 ? ToneTab() : SpaceTab();
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? OutputTab() : LevelsTab();

        var centre = DeviceBodyKit.TabFrame(new[] { "Space", "Tone · Mod" }, 0, centreHost, CentreBody, t => { centreTab = t; foreach (var a in readouts) a(); }, extras);
        var rightFrame = DeviceBodyKit.TabFrame(new[] { "Levels", "Output" }, 0, rightHost, RightBody, _ => { foreach (var a in readouts) a(); });
        var right = DeviceBodyKit.Sec(rightFrame, SectionPad, 196);
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = DeviceBodyKit.Sec(centre);
        centreBox.Margin = new Thickness(SectionGap, 0);

        // ---- assemble ---------------------------------------------------------------
        var root = DeviceBodyKit.Frame(new DockPanel { LastChildFill = true, Children = { tailCol, right, centreBox } });

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kScope);
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

    }
}
