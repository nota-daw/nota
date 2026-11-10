// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Reverb body (device kind 2), a build of the "Nota Reverb"
// mockup, laid out the #531 way (CardLayout): a centre panel with Space / Tone · Mod tabs and a
// right panel with no tabs of its own that follows them, over Dry · Dry/wet · Out. Space is the
// decay window (the set tail in dB with your audio's measured tail under it, #377) with Decay,
// Pre and Size beside it and the algorithm under it; its panel holds Freeze, Kill tail, Diffuse,
// early reflections, wet only and the wet meter. Tone · Mod is the decay across frequency (HF
// damp, the cut handles, the tail's live spectrum) with Low cut, High cut and HF damp beside it
// and Vintage and Mod on tail under it; its panel holds Rate, Depth, Width, Mono f and latency
// comp. S is the decay window over the algorithm and Decay, Pre, Size, Dry/wet. The graphs are
// controls, not decoration (drag the pre-delay and decay handles, drag the cuts). Every control
// is a device param, so automation / MIDI learn / presets / A-B / persistence come for free;
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
using Nota.Application.Localization;

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

    public double Width => 700;
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex);
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
        var scope = new float[kScope + SpaceDraw.Floats];   // the telemetry, then the level history (#377)
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
        // The highs' decay (8 kHz) under HF damp: the combs' one-pole damping (coefficient
        // damp × 0.4, Reverb.cpp) takes a little more off them on every ~30 ms loop.
        double RtHigh(double rt)
        {
            double d = P(HFDamp) * 0.4, wv = 2 * Math.PI * 8000 / 48000;
            double g = (1 - d) / Math.Sqrt(1 - 2 * d * Math.Cos(wv) + d * d);
            double perLoop = -6.9 * 0.03 / rt;   // ln of the loop gain that gives this RT60
            return rt * perLoop / (perLoop + Math.Log(Math.Max(g, 1e-6)));
        }
        static string Secs(double s) => s >= 10 ? NotaNum.F($"{s:0.0} s") : NotaNum.F($"{s:0.00} s");
        static double LowHz(double v) => Exp(v, 20, 1000);
        static double HighHz(double v) => Exp(v, 1000, 20000);
        static string RateF(double v) { double hz = Exp(v, 0.05, 5); return hz >= 1 ? NotaNum.F($"{hz:0.0} Hz") : NotaNum.F($"{hz:0.00} Hz"); }
        static string PreF(double v) => NotaNum.F($"{v * 200:0} ms");
        string DecayF(double v) => On(Freeze) ? "HOLD" : Secs(Rt60(v));

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, double size = 34, double cellW = 50, IBrush? arc = null, Func<bool>? dim = null)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
            knob.ValueChanged += v => { Raw(p, (float)v); val.Text = fmt(v); SyncTail(); };
            if (dim is not null) readouts.Add(() => knob.IsDim = dim());
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
            var b = new Border { Height = 16, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Cell, Background = Raised, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = tb };
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
        ReverbTailView NewTail()
        {
            var t = new ReverbTailView();
            t.Changed += (target, v) => { Raw(target == 0 ? Decay : PreDelay, (float)v); SyncTail(); };
            t.GestureBegin += target => Begin(target == 0 ? Decay : PreDelay);
            t.GestureEnd += target => End(target == 0 ? Decay : PreDelay);
            t.Reset += target => { Reset(target == 0 ? Decay : PreDelay); SyncTail(); };
            ToolTip.SetTip(t, MenuKit.Keys(Loc.T("device.reverb.the_decay_tail_the_impulse")));
            ValueEntry.AttachHandles(t, [ValueEntry.DeviceParam(engine, track, di, Decay, "Decay", DecayF, SyncTail)],
                [ValueEntry.DeviceParam(engine, track, di, PreDelay, "Pre-delay", PreF, SyncTail)]);
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
            double rt = Rt60(P(Decay));
            foreach (var t in tails) t.Set(rt, RtHigh(rt), freeze, P(PreDelay) * 200, er, ErGain);
        }
        readouts.Add(() =>
        {
            SyncTail();
            // Your audio under the set line: the level history after the telemetry.
            bool has = scN >= kScope + SpaceDraw.Floats;
            foreach (var t in tails) t.SetLive(scope, kScope, has);
        });

        // ======================================================================
        // The decay across frequency — the Tone · Mod tab's graph
        // ======================================================================
        var tone = new ReverbToneView();
        tone.Changed += (hd, v) => { Raw(hd == 0 ? LowCut : HighCut, (float)v); SyncTone(); };
        tone.GestureBegin += hd => Begin(hd == 0 ? LowCut : HighCut);
        tone.GestureEnd += hd => End(hd == 0 ? LowCut : HighCut);
        tone.Reset += hd => { Reset(hd == 0 ? LowCut : HighCut); SyncTone(); };
        ToolTip.SetTip(tone, MenuKit.Keys(Loc.T("device.reverb.decay_by_frequency_how_long")));
        ValueEntry.AttachHandles(tone, [ValueEntry.DeviceParam(engine, track, di, LowCut, Loc.T("device.reverb.low_cut"), v => Hz(LowHz(v)), SyncTone)],
            [ValueEntry.DeviceParam(engine, track, di, HighCut, Loc.T("device.reverb.high_cut"), v => Hz(HighHz(v)), SyncTone)]);
        MidiLearn.Bind(tone, MidiTarget.DeviceParam(track, di, HighCut), Loc.T("device.reverb.high_cut"));
        // The spectrum is only worked out while the Tone · Mod graph shows (as Delay's Filter does).
        void SyncTone()
            => tone.Set(Rt60(P(Decay)), P(HFDamp), P(Size), Sel(Algorithm, 4), On(Freeze), LowHz(P(LowCut)), HighHz(P(HighCut)), P(LowCut) > 0.001f,
                scope, kScope + 2 * SpaceDraw.Points, scN >= kScope + SpaceDraw.Floats && tone.IsEffectivelyVisible, Sc(S_SampleRate));
        readouts.Add(SyncTone);

        // ======================================================================
        // CENTRE — Space (the tail: Decay · Pre · Size beside it, the algorithm under it) /
        // Tone · Mod (Low cut · High cut · Width beside it, Vintage and Mod on tail under it)
        // ======================================================================
        Control AlgoSeg()
        {
            var seg = Seg(Algorithm, Algos);
            ToolTip.SetTip(seg, Loc.T("device.reverb.algorithm_hall_large_spaced_reflections"));
            return seg;
        }
        Control DecayKnob(double cellW) => K(Decay, "DECAY", DecayF, 34, cellW);
        Control PreKnob(double cellW) => K(PreDelay, "PRE", PreF, 34, cellW);
        Control SizeKnob(double cellW) => K(Size, "SIZE", Pct, 34, cellW);
        Control MixKnob(double cellW, double size) => K(DryWet, "DRY/WET", Pct, size, cellW, dim: () => On(WetOnly));
        Control SpaceTab() => CardLayout.View(NewTail(), CardLayout.KnobColumn(DecayKnob(50), PreKnob(50), SizeKnob(50)), AlgoSeg());
        Control ToneTab()
        {
            var mt = Toggle(ModOnTail, "Mod on tail");
            ToolTip.SetTip(mt, Loc.T("device.reverb.mod_on_tail_the_modulation"));
            var vi = Toggle(Vintage, "Vintage");
            ToolTip.SetTip(vi, Loc.T("device.reverb.vintage_an_early_digital_colour"));
            // The knobs the graph draws beside it: the two cuts (its handles) and HF damp (its curve).
            return CardLayout.View(tone,
                CardLayout.KnobColumn(K(LowCut, "LOW CUT", v => Hz(LowHz(v)), 34, 50), K(HighCut, "HIGH CUT", v => Hz(HighHz(v)), 34, 52), K(HFDamp, "HF DAMP", Pct, 34, 50)),
                CardLayout.Line(14, vi, mt));
        }

        // ======================================================================
        // RIGHT — follows the centre tab: Space (Space) · Mod (Tone · Mod)
        // ======================================================================
        (Control, Control) SpacePanel()
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
            var fr = Toggle(Freeze, "Freeze");
            ToolTip.SetTip(fr, Loc.T("device.reverb.freeze_hold_the_tail_forever"));
            var er = Toggle(EarlyRefl, "Early reflections");
            ToolTip.SetTip(er, Loc.T("device.reverb.early_reflections_the_first_discrete"));
            var wetOnly = Toggle(WetOnly, Loc.T("common.wet_only_send"));
            ToolTip.SetTip(wetOnly, Loc.T("device.reverb.wet_only_the_tail_with"));
            return (CardLayout.Head(fr, Btn("Kill tail", () => engine.DeviceAction(track, di, A_KillTail, 0, 0), Loc.T("device.reverb.kill_tail_empty_the_reverb"))),
                CardLayout.Rows(
                    SliderRow("DIFFUSE", Diffusion, Pct),
                    CardLayout.Rule(),
                    er,
                    wetOnly,
                    wet));
        }
        (Control, Control) ModPanel()
        {
            var mono = SliderRow("Mono f", BassMono, v => v <= 0.001 ? "off" : NotaNum.F($"{Exp(v, 30, 500):0} Hz"));
            ToolTip.SetTip(mono, Loc.T("device.reverb.bass_mono_below_this_frequency"));
            var comp = Toggle(LatencyComp, Loc.T("device.reverb.latency_comp"));
            ToolTip.SetTip(comp, Loc.T("device.reverb.the_input_diffuser_delays_the"));
            return (CardLayout.Head(CardLayout.Title(Loc.T("device.reverb.mod"))), CardLayout.Rows(
                SliderRow("RATE", ModRate, RateF),
                SliderRow("DEPTH", ModDepth, Pct),
                CardLayout.Rule(),
                SliderRow("WIDTH", WidthP, Pct),
                mono,
                comp));
        }

        // ======================================================================
        // Frames: L (Space / Tone · Mod, the panel that follows them, Dry · Dry/wet · Out under
        // it) · S (the tail over the algorithm and Decay · Pre · Size · Dry/wet)
        // ======================================================================
        int centreTab = 0;
        var extras = Mono("", 7, TextTertiary);
        readouts.Add(() =>
        {
            if (On(Freeze))
            {
                extras.Text = Loc.T("device.reverb.input_muted_tail_held");
                extras.Foreground = AccentBright;
                return;
            }
            extras.Foreground = TextTertiary;
            extras.Text = centreTab == 0
                ? NotaNum.F($"RT60 {Secs(Rt60(P(Decay)))} · {P(PreDelay) * 200:0} ms pre · ") + (Sel(Algorithm, 4) == 2
                    ? NotaNum.F($"maximum density · size {Pct(P(Size))}") : NotaNum.F($"size {Pct(P(Size))} · diffuse {Pct(P(Diffusion))}"))
                : NotaNum.F($"decay {Secs(tone.DecayAt(250))} at {Hz(250)} · {Secs(tone.DecayAt(4000))} at {Hz(4000)}");
        });
        Control root = CardLayout.IsMini(ctx, di)
            ? CardLayout.Mini(NewTail(), AlgoSeg(), DecayKnob(56), PreKnob(56), SizeKnob(56), MixKnob(56, 34))
            : CardLayout.Large(new[] { "Space", "Tone · Mod" }, 0,
                t => t == 1 ? ToneTab() : SpaceTab(),
                t => t == 1 ? ModPanel() : SpacePanel(),
                extras,
                CardLayout.Output(K(DryLevel, "DRY", v => ShortDb(DryDb(v)), 34, 54, dim: () => On(WetOnly)), MixKnob(58, 44), K(Output, "OUT", v => Db(OutDb(v)), 34, 54)),
                t => { centreTab = t; foreach (var a in readouts) a(); });

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, scope.Length);
            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

    }
}
