// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Reverb body (device kind 2), 700 × 260: an always-visible TAIL
// column (Decay · HF Damp), a centre tabbed panel whose graph fills it (Space: the live tail
// of your audio against the set RT60; Tone · Mod: decay time across frequency, the cut
// filters and the tail's spectrum), a right panel with the controls of the open centre tab
// (or Output) and a status strip. Both graphs are controls too (drag the tail for the decay,
// the handles for the cuts). Every control is a device param, so automation / MIDI learn /
// presets / A-B / persistence come for free; Kill tail is the one action that is not (it
// empties the reverb's buffers).
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
    // Scope telemetry layout (Reverb::S_*), then the level history (dsp::LevelHistory).
    private const int S_WetPk = 0, S_OutL = 1, S_OutR = 2, S_Cpu = 3, S_SampleRate = 4, S_Latency = 5,
        S_DiffuseSmp = 8, kScope = 11;   // 6 RT60 · 7 effective pre-delay ms · 9 frozen · 10 early on
    private const int ScopeLen = kScope + SpaceDraw.Floats;
    private const int A_KillTail = 0;

    private static readonly string[] Algos = { "Hall", "Room", "Plate", "Chamber" };

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
        var scope = new float[ScopeLen];
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

        // ======================================================================
        // The live graphs — one per centre tab
        // ======================================================================
        int centreTab = 0;
        var tone = new ReverbToneView();
        var space = new ReverbSpaceView { DecayValue = () => P(Decay) };
        space.Changed += v => { Raw(Decay, (float)v); SyncGraphs(); };
        space.GestureBegin += () => Begin(Decay);
        space.GestureEnd += () => End(Decay);
        space.Reset += () => { Reset(Decay); SyncGraphs(); };
        ToolTip.SetTip(space, "The tail of your audio — the input (grey) and the reverb's tail before the mix, over the last six seconds. The dashed line falls from the tail's latest peak at the set RT60. Drag to lengthen or shorten the decay; double-click to reset it.");
        MidiLearn.Bind(space, MidiTarget.DeviceParam(track, di, Decay), "Decay");

        tone.Changed += (hd, v) => { Raw(hd == 0 ? LowCut : HighCut, (float)v); SyncGraphs(); };
        tone.GestureBegin += hd => Begin(hd == 0 ? LowCut : HighCut);
        tone.GestureEnd += hd => End(hd == 0 ? LowCut : HighCut);
        tone.Reset += hd => { Reset(hd == 0 ? LowCut : HighCut); SyncGraphs(); };
        ToolTip.SetTip(tone, "Decay by frequency — how long the tail rings at each pitch (HF Damp shortens the highs), the low and high cut on the tail (drag the handles; double-click resets) and the tail's live spectrum (grey).");
        MidiLearn.Bind(tone, MidiTarget.DeviceParam(track, di, HighCut), "High Cut");

        void SyncGraphs()
        {
            bool has = scN >= ScopeLen;
            if (centreTab == 0) space.Set(scope, kScope, has, Rt60(P(Decay)), On(Freeze));
            else
                tone.Set(Rt60(P(Decay)), P(HFDamp), P(Size), Sel(Algorithm, 4), On(Freeze), LowHz(P(LowCut)), HighHz(P(HighCut)), P(LowCut) > 0.001f,
                    scope, kScope + 2 * SpaceDraw.Points, has, Sc(S_SampleRate));
        }
        readouts.Add(SyncGraphs);

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static Border Divider(Control child, double top = 5) => new()
        { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, top, 0, 0), Child = child };

        // On/off switch bound to a param (> 0.5 = on).
        Control Toggle(int p, string label)
        {
            var wrap = Switch(label, () => On(p), () => { SetP(p, On(p) ? 0f : 1f); SyncGraphs(); }, out var sync);
            readouts.Add(sync);
            MidiLearn.Bind(wrap, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return wrap;
        }

        // Segmented chips over a discrete param (n options spread over 0..1).
        Control Seg(int p, string[] names)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Sel(p, n), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); SyncGraphs(); }, out var sync, fill: true, padX: 1, fontSize: 8);
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
        Control SliderRow(string label, int p, Func<double, string> fmt, Func<bool>? dim = null, Func<bool>? hot = null, bool teal = false)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, (float)v); SyncGraphs(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), dim: dim, valueWidth: 38, modulation: teal);
            readouts.Add(sync);
            MidiLearn.Bind(bar, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("46,*"), VerticalAlignment = VerticalAlignment.Center };
            var lb = Caps(label);
            if (hot is not null) readouts.Add(() => lb.Foreground = hot() ? AccentBright : TextTertiary);
            g.Children.Add(lb);
            g.Children.Add(Col(bar, 1));
            return g;
        }

        // ======================================================================
        // LEFT — Tail column (Decay · HF Damp)
        // ======================================================================
        Control TailFader(int p, string label, IBrush ink, IBrush handle, string tip)
        {
            var f = new DelayFader { Ink = ink, Handle = handle, Width = 16, VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Center, Default = engine.DeviceParamDefault(track, di, p) };
            f.Changed += v => { Raw(p, (float)v); SyncGraphs(); };
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
        faders.Children.Add(TailFader(Decay, "DECAY", NotaPalette.VizInk, NotaPalette.VizInk,
            "Decay — RT60, the time the tail takes to fall 60 dB (0.2 … 12 s); Freeze holds it forever. Double-click resets."));
        faders.Children.Add(Col(TailFader(HFDamp, "DAMP", DeviceCardKit.ModInk, DeviceCardKit.ModInkBright,
            "HF Damp — how much faster the highs die than the lows. Double-click resets."), 1));
        var decVal = Mono("", 7, TextPrimary); decVal.HorizontalAlignment = HorizontalAlignment.Center;
        var dmpVal = Mono("", 7, TextPrimary); dmpVal.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() =>
        {
            decVal.Text = DecayF(P(Decay));
            dmpVal.Text = Pct(P(HFDamp));
            tailTitle.Foreground = On(Freeze) ? AccentBright : TextTertiary;
        });
        // Freeze shows on the column's title (accent), not on a frame: sections have none.
        var tailCol = new RuleSection
        {
            Width = 64, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(tailTitle, Dock.Top), Docked(dmpVal, Dock.Bottom), Docked(decVal, Dock.Bottom), faders } },
        };
        DockPanel.SetDock(tailCol, Dock.Left);

        // ======================================================================
        // RIGHT — the controls of the centre tab (Space · Tone), and Output
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
        static Border Rule() => new() { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 1) };
        static Grid Pair(Control a, Control b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(a); g.Children.Add(Col(b, 1));
            return g;
        }

        Control SpacePanel()
        {
            var mode = Seg(Algorithm, Algos);
            ToolTip.SetTip(mode, "Algorithm — Hall (large, spaced reflections) · Room (small, close walls) · Plate (dense, no room at all) · Chamber (between)");
            var er = Toggle(EarlyRefl, "Early reflections");
            ToolTip.SetTip(er, "Early reflections — the first discrete echoes off the walls, spaced by the algorithm and the size");
            var fr = Toggle(Freeze, "Freeze");
            ToolTip.SetTip(fr, "Freeze — hold the tail forever; the input is muted and nothing decays");
            return Rows(
                mode,
                SliderRow("PRE", PreDelay, PreF),
                SliderRow("SIZE", Size, Pct),
                SliderRow("DIFFUSE", Diffusion, Pct),
                SliderRow("DECAY", Decay, DecayF, null, () => On(Freeze)),
                Rule(),
                er, fr);
        }

        Control TonePanel()
        {
            var mt = Toggle(ModOnTail, "Mod on tail");
            ToolTip.SetTip(mt, "Mod on tail — the modulation wobbles the tail itself (a chorused, evolving decay); off, it moves only the early part and the tail stays still");
            var vi = Toggle(Vintage, "Vintage");
            ToolTip.SetTip(vi, "Vintage — an early-digital colour: a band-limited input, 12-bit wet and a deeper wobble");
            return Rows(
                SliderRow("LOW CUT", LowCut, v => Hz(LowHz(v))),
                SliderRow("HIGH CUT", HighCut, v => Hz(HighHz(v))),
                SliderRow("HF DAMP", HFDamp, Pct),
                Rule(),
                SliderRow("MOD RATE", ModRate, RateF, teal: true),
                SliderRow("DEPTH", ModDepth, Pct, teal: true),
                mt, vi);
        }

        Control OutputPanel()
        {
            var wetBar = new PentadLevelBar { Height = 4, VerticalAlignment = VerticalAlignment.Center };
            var wetDb = Mono("", 8, TextPrimary); wetDb.Width = 26; wetDb.TextAlignment = TextAlignment.Right;
            readouts.Add(() =>
            {
                double pk = Sc(S_WetPk);
                wetBar.SetLinear(pk);
                double db = pk > 1e-5 ? 20 * Math.Log10(pk) : -100;
                wetDb.Text = ShortDb(db);
                wetDb.Foreground = db > -6 ? NotaPalette.Warning : TextPrimary;
            });
            var meter = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            meter.Children.Add(Btn("Kill tail", () => engine.DeviceAction(track, di, A_KillTail, 0, 0), "Kill tail — empty the reverb at once; whatever is still ringing (or frozen) stops"));
            meter.Children.Add(Col(Caps("WET"), 1)); meter.Children.Add(Col(wetBar, 2)); meter.Children.Add(Col(wetDb, 3));

            var wetOnly = Toggle(WetOnly, "Wet only");
            ToolTip.SetTip(wetOnly, "Wet only — the tail with no dry, for a return / send track");
            var comp = Toggle(LatencyComp, "Lat. comp");
            ToolTip.SetTip(comp, "Latency comp — the input diffuser delays the tail by a fixed group delay; on, the pre-delay is shortened by exactly that so the tail starts on the set pre-delay (it cannot go below zero).");
            var mono = SliderRow("MONO", BassMono, v => v <= 0.001 ? "off" : NotaNum.F($"{Exp(v, 30, 500):0} Hz"));
            ToolTip.SetTip(mono, "Bass mono — below this frequency the tail is mono, so a wide reverb does not smear the low end (off at the far left)");
            return Rows(
                SliderRow("MIX", DryWet, Pct, () => On(WetOnly), () => On(WetOnly)),
                SliderRow("DRY", DryLevel, v => Db(DryDb(v)), () => On(WetOnly)),
                SliderRow("OUT", Output, v => Db(OutDb(v))),
                SliderRow("WIDTH", WidthP, Pct),
                mono,
                Rule(),
                Pair(wetOnly, comp),
                meter);
        }

        // ======================================================================
        // Tab frames: the centre graph, and the panel that follows it
        // ======================================================================
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[2];
        var panelBodies = new Control?[2];
        Control? outputBody = null;
        int rightTab = 0;
        string[] panelTitles = { "Space", "Tone · Mod" };
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
                ? NotaNum.F($"{Algo()} · RT60 {Secs(Rt60(P(Decay)))} · pre {P(PreDelay) * 200:0} ms")
                : NotaNum.F($"decay {Secs(tone.DecayAt(250))} at 250 Hz · {Secs(tone.DecayAt(4000))} at 4 kHz");
        });
        Control CentreBody(int t) => centreBodies[t] ??= new Border { Margin = new Thickness(8, 6), Child = t == 1 ? tone : space };
        Control PanelBody(int t) => panelBodies[t] ??= t == 1 ? TonePanel() : SpacePanel();
        Control RightBody(int t) => t == 1 ? outputBody ??= OutputPanel() : PanelBody(centreTab);

        TextBlock[]? rightLabels = null;
        var centre = TabFrame(new[] { "Space", "Tone · Mod" }, centreHost, CentreBody, false, t =>
        {
            centreTab = t;
            rightLabels![0].Text = panelTitles[t];
            if (rightTab == 0) rightHost.Content = PanelBody(t);
            Refresh();
        }, extras, out _);
        var rightFrame = TabFrame(new[] { panelTitles[0], "Output" }, rightHost, RightBody, true, t => { rightTab = t; Refresh(); }, null, out rightLabels);
        var right = new RuleSection { Width = 186, Child = rightFrame };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new RuleSection { Margin = new Thickness(5, 0), Child = centre };

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
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceCard, Children = { status, bodyRow } };

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, ScopeLen);
            for (int i = 0; i < readouts.Count; i++) readouts[i]();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

        // local: a tab frame (bar + swapping body), optional extras on the right of the bar.
        Control TabFrame(string[] tabs, ContentControl host, Func<int, Control> body, bool centered, Action<int>? changed, Control? extrasCtl, out TextBlock[] labels)
        {
            int sel = 0;
            var btns = new Border[tabs.Length];
            labels = new TextBlock[tabs.Length];
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
                b.PointerPressed += (_, _) => { sel = iv; Hi(); host.Content = body(iv); changed?.Invoke(iv); };
                btns[i] = b;
                labels[i] = (TextBlock)b.Child;
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
