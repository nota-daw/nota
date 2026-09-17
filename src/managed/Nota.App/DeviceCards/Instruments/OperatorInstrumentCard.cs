// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Operator editor (instrument kind 9), mockup 3g: a
// 4-operator FM synth in the shared 700×260 shell (header 26 · contextual strip 34 · body).
// A left tab rail switches three views:
//   OPS   — four operator rows stating each op's ROLE in words (CARRIER / MOD→X) in its
//           colour, with wave · coarse ratio · fine · level, beside a live harmonic SPECTRUM.
//   ALGO  — the 11 algorithms as drawn sketches (picker), the current routing full-width
//           with real connection lines + drag-to-re-route, and two per-op envelopes.
//   FILTER— the subtractive LP/HP/BP filter response + output.
// The strip carries the four performance controls (FM depth · Feedback · Tone · Glide) + the
// voice mode. Everything is a plugin param → automation / persist / clone.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class OperatorInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "FM";
    public double CardWidth => 700;

    // Coarse ratio table — mirrors OperatorSynth::kRatio.
    private static readonly double[] Ratios = { 0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 16 };
    private static readonly string[] WaveShort = { "Sin", "Tri", "Saw", "Sqr" };
    private static readonly string[] OpId = { "a", "b", "c", "d" };
    private static readonly IBrush[] OpColor = { NotaPalette.Accent, NotaPalette.Accent, NotaPalette.Teal, NotaPalette.Teal };

    private static readonly IBrush Strip = NotaPalette.SurfaceCard;
    private static readonly IBrush Rail = NotaPalette.SurfaceInset;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Ink = NotaPalette.TextOnAccent;

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        float G(string id) => I(id) is var i and >= 0 ? engine.PluginParamGet(track, -1, i) : 0f;
        void SetId(string id, float v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); }
        void Begin(string id) { if (I(id) >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        void End(string id) { if (I(id) >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        int AlgoIdx() => Math.Clamp((int)Math.Round(G("algo") * (OperatorTopo.Count - 1)), 0, OperatorTopo.Count - 1);

        var readouts = new List<Action>();
        static string NoteName(int m) { string[] n = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" }; return n[((m % 12) + 12) % 12] + (m / 12 - 1); }
        static TextBlock Mono(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static TextBlock Cap(string t, IBrush? c = null, double fs = 8) => new() { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };

        // ---- viz instances ----
        var spectrum = new OperatorSpectrumViz { VerticalAlignment = VerticalAlignment.Stretch };
        var specBins = new float[32];
        var held = new int[8];
        var routing = new OperatorRoutingViz { VerticalAlignment = VerticalAlignment.Stretch };
        var minis = new OperatorAlgoMini[OperatorTopo.Count];
        var envA = new VoltEnv(engine, track) { Accent = Brass, MinHeight = 40, VerticalAlignment = VerticalAlignment.Stretch };
        var envB = new VoltEnv(engine, track) { Accent = Teal, MinHeight = 40, VerticalAlignment = VerticalAlignment.Stretch };
        int envAop = 3, envBop = 2;   // default: D carrier · C modulator
        Action applyEnv = () => { };

        void SyncSpectrum()
        {
            int n = engine.InstrumentScope(track, specBins);
            int hn = engine.InstrumentHeldNotes(track, held);
            spectrum.Set(specBins, n, hn > 0 ? NoteName(held[0]) : "—");
        }
        void Refresh()
        {
            int a = AlgoIdx();
            routing.Set(a);
            for (int i = 0; i < minis.Length; i++) minis[i].SetActive(i == a);
            envA.Refresh(); envB.Refresh();
            SyncSpectrum();
            foreach (var r in readouts) r();
        }
        routing.AlgoPicked += a => { SetId("algo", a / (float)(OperatorTopo.Count - 1)); Refresh(); };
        ctx.AddDeviceRefresher(SyncSpectrum);

        // ---- generic building blocks ----------------------------------------
        Control MiniSlider(string label, string id, Func<double, string> fmt, double w)
        {
            var row = DeviceCardKit.SliderRow(label, () => G(id), n => { SetId(id, (float)n); Refresh(); }, () => fmt(G(id)), out var sync,
                begin: () => Begin(id), end: () => End(id), trackWidth: w, valueWidth: 22);
            readouts.Add(sync);
            return row;
        }
        Control Slider(string label, string id, IBrush fillB, Func<double, string>? fmt = null, double labelW = 44)
        {
            var f = fmt ?? (v => $"{v * 100:0}\u2009%");
            var row = DeviceCardKit.SliderRow(label, () => G(id), n => { SetId(id, (float)n); Refresh(); }, () => f(G(id)), out var sync,
                begin: () => Begin(id), end: () => End(id), labelWidth: labelW, valueWidth: 44);
            readouts.Add(sync);
            return row;
        }
        Control Chips(string id, string[] names, double fs = 9)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1), iv => { if (n > 1) { SetId(id, iv / (float)(n - 1)); Refresh(); } }, out var sync);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }
        Control Toggle(string label, string id, IBrush accent)
        {
            var host = Switch(label, () => G(id) >= 0.5f, () => { SetId(id, G(id) >= 0.5f ? 0f : 1f); Refresh(); }, out var sync);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(host, MidiTarget.PluginParam(track, -1, pi), label);
            return host;
        }
        Control DragVal(string id, Func<double, string> fmt, double w)
        {
            var tb = Mono(fmt(G(id)), TextPrimary); tb.Width = w; tb.Cursor = new Cursor(StandardCursorType.SizeNorthSouth); tb.Background = Brushes.Transparent;
            bool drag = false; double startY = 0, startV = 0;
            tb.PointerPressed += (_, e) => { drag = true; startY = e.GetPosition(tb).Y; startV = G(id); Begin(id); e.Pointer.Capture(tb); };
            tb.PointerMoved += (_, e) => { if (drag) { double dv = (startY - e.GetPosition(tb).Y) / 120.0; SetId(id, (float)Math.Clamp(startV + dv, 0, 1)); tb.Text = fmt(G(id)); Refresh(); } };
            tb.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); End(id); } };
            if (I(id) is var mli and >= 0) MidiLearn.Bind(tb, MidiTarget.PluginParam(track, -1, mli), id);
            readouts.Add(() => { if (!drag) tb.Text = fmt(G(id)); });
            return tb;
        }

        // ---- OPS view: operator rows ----------------------------------------
        Border OpRow(int op)
        {
            string pre = OpId[op];
            var letter = new TextBlock { Text = OperatorTopo.Names[op], FontSize = 9, FontWeight = FontWeight.Bold, Foreground = Teal, Width = 12, VerticalAlignment = VerticalAlignment.Center };
            var role = new TextBlock { FontSize = 8, FontWeight = FontWeight.Bold, Width = 58, VerticalAlignment = VerticalAlignment.Center };
            var wave = Chips(pre + "wave", WaveShort, 8);
            var coarse = DragVal(pre + "coarse", v => { int i = Math.Clamp((int)Math.Round(v * 15), 0, 15); return "×" + Ratios[i].ToString(Ratios[i] < 1 ? "0.0#" : "0"); }, 30);
            var fine = DragVal(pre + "fine", v => { int c = (int)Math.Round((v - 0.5) * 100); return (c >= 0 ? "+" : "") + c; }, 24);
            var accentBar = new Border { Width = 2, Background = Teal };
            string LvlF(double v) => v <= 1e-3 ? "−∞" : $"{20 * Math.Log10(v):0.0}";
            string lid = pre + "level";
            var lvlVal = Mono("", TextPrimary); lvlVal.Width = 44; lvlVal.TextAlignment = TextAlignment.Right;
            var lvlSlot = new SliderTrack();
            lvlSlot.Changed += v => { SetId(lid, (float)v); lvlVal.Text = LvlF(v); Refresh(); };
            lvlSlot.GestureBegin += () => Begin(lid);
            lvlSlot.GestureEnd += () => End(lid);
            readouts.Add(() =>
            {
                var tgt = OperatorTopo.Algo[AlgoIdx()];
                bool carr = tgt[op] == 4;
                role.Text = carr ? "CARRIER" : "MOD → " + OperatorTopo.Names[tgt[op]];
                var rc = carr ? Brass : Teal;
                role.Foreground = rc; letter.Foreground = rc; accentBar.Background = rc;
                if (!lvlSlot.Dragging) { double v = G(lid); lvlSlot.Norm = v; lvlVal.Text = LvlF(v); }
            });

            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto"), ColumnSpacing = 7, VerticalAlignment = VerticalAlignment.Center };
            var cells = new Control[] { letter, role, wave, coarse, fine, lvlSlot, lvlVal };
            for (int c = 0; c < cells.Length; c++) { Grid.SetColumn(cells[c], c); g.Children.Add(cells[c]); }
            var inner = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(accentBar, Dock.Left); inner.Children.Add(accentBar);
            inner.Children.Add(new Border { Padding = new Thickness(6, 0), Child = g });
            return new Border { Background = NotaPalette.BgApp, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, ClipToBounds = true, Height = 34, Child = inner };
        }

        var opsHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 11 };
        var oh1 = Cap("wave · coarse · fine · level"); oh1.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(oh1, 1);
        opsHeader.Children.Add(Cap("OPERATORS")); opsHeader.Children.Add(oh1);
        var opRows = new StackPanel { Spacing = 3 };
        opRows.Children.Add(opsHeader);
        for (int o = 0; o < 4; o++) opRows.Children.Add(OpRow(o));
        var opsMain = new Border { Padding = new Thickness(8, 6), Child = opRows };

        // ---- OPS view: spectrum rail ----------------------------------------
        var specHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 11 };
        var sh1 = Mono("", AccentBright); sh1.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(sh1, 1);
        specHead.Children.Add(Cap("SPECTRUM")); specHead.Children.Add(sh1);
        readouts.Add(() => { int hn = engine.InstrumentHeldNotes(track, held); sh1.Text = hn > 0 ? NoteName(held[0]) : ""; });
        var velFm = Toggle("VEL → FM", "veltofm", Teal);
        var specDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(specHead, Dock.Top); DockPanel.SetDock(velFm, Dock.Bottom);
        specDock.Children.Add(specHead); specDock.Children.Add(velFm); specDock.Children.Add(spectrum);
        var specRail = new Border { Width = 170, Background = Rail, BorderBrush = BorderDef, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6), Child = specDock };
        var opsRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(specRail, Dock.Right); opsRow.Children.Add(specRail); opsRow.Children.Add(opsMain);

        // ---- ALGO view: picker + routing ------------------------------------
        var pickerGrid = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
        for (int i = 0; i < OperatorTopo.Count; i++) pickerGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        for (int i = 0; i < OperatorTopo.Count; i++)
        {
            var mini = new OperatorAlgoMini(i) { Margin = new Thickness(1.5, 0) };
            mini.Clicked += a => { SetId("algo", a / (float)(OperatorTopo.Count - 1)); Refresh(); };
            minis[i] = mini; Grid.SetColumn(mini, i); pickerGrid.Children.Add(mini);
        }
        var algoDock = new DockPanel { LastChildFill = true };
        var routingBox = new Border { Height = 84, Child = routing };
        DockPanel.SetDock(routingBox, Dock.Bottom); algoDock.Children.Add(routingBox);
        algoDock.Children.Add(new Border { Child = pickerGrid, Margin = new Thickness(0, 0, 0, 5) });
        var algoMain = new Border { Padding = new Thickness(8, 7), Child = algoDock };

        // ---- ALGO view: per-op env rail -------------------------------------
        applyEnv = () =>
        {
            string pa = OpId[envAop], pb = OpId[envBop];
            var tgt = OperatorTopo.Algo[AlgoIdx()];
            string RoleTag(int op) => tgt[op] == 4 ? "carrier" : "mod";
            envA.Target($"{OperatorTopo.Names[envAop]} · {RoleTag(envAop)}", (I(pa + "atk"), pa + "atk"), (I(pa + "dec"), pa + "dec"), (I(pa + "sus"), pa + "sus"), (I(pa + "rel"), pa + "rel"));
            envB.Target($"{OperatorTopo.Names[envBop]} · {RoleTag(envBop)}", (I(pb + "atk"), pb + "atk"), (I(pb + "dec"), pb + "dec"), (I(pb + "sus"), pb + "sus"), (I(pb + "rel"), pb + "rel"));
        };
        Control EnvSelector(Func<int> get, Action<int> set)
        {
            var arr = new Border[4];
            void Hi() { int cur = get(); for (int i = 0; i < 4; i++) { bool on = i == cur; arr[i].Background = on ? NotaPalette.Accent : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? OpColor[i] : TextTertiary; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < 4; i++)
            {
                int iv = i;
                var chip = new Border { CornerRadius = NotaRadius.Clip, Padding = new Thickness(5, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = OperatorTopo.Names[i], FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary } };
                chip.PointerPressed += (_, _) => { set(iv); applyEnv(); Refresh(); };
                arr[i] = chip; row.Children.Add(chip);
            }
            readouts.Add(Hi); Hi();
            return new Border { Background = Inset, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Child = row };
        }
        var slotA = new DockPanel { LastChildFill = true, Height = 62 };
        var selA = EnvSelector(() => envAop, v => envAop = v); DockPanel.SetDock(selA, Dock.Top); slotA.Children.Add(selA); slotA.Children.Add(envA);
        var slotB = new DockPanel { LastChildFill = true, Height = 62 };
        var selB = EnvSelector(() => envBop, v => envBop = v); DockPanel.SetDock(selB, Dock.Top); slotB.Children.Add(selB); slotB.Children.Add(envB);
        var envStack = new StackPanel { Spacing = 4, Children = { Cap("PER-OP ENV", Teal), slotA, slotB, new Border { Height = 1 }, Slider("KEY→LVL", "keylevel", Teal) } };
        var envRail = new Border { Width = 176, Background = Rail, BorderBrush = BorderDef, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6), Child = envStack };
        var algoRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(envRail, Dock.Right); algoRow.Children.Add(envRail); algoRow.Children.Add(algoMain);

        // ---- FILTER view ----------------------------------------------------
        var filtCurve = new VoltFilter(engine, track) { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 90 };
        readouts.Add(() => filtCurve.Target("FILTER", (I("filfreq"), "filfreq"), (I("filreso"), "filreso"), Math.Clamp((int)Math.Round(G("filtype") * 2), 0, 2)));
        var filtLeft = new StackPanel { Spacing = 7, Width = 210, VerticalAlignment = VerticalAlignment.Top, Children =
        {
            Cap("FILTER"), Chips("filtype", new[] { "LP", "HP", "BP" }),
            Slider("FREQ", "filfreq", Brass, v => $"{60 * Math.Pow(300, v):0}\u2009Hz"), Slider("RESO", "filreso", Brass),
            new Border { Height = 6 }, Cap("OUTPUT"), Slider("VOLUME", "volume", Brass),
        } };
        var filtRight = new DockPanel { LastChildFill = true };
        var frHead = Cap("RESPONSE"); DockPanel.SetDock(frHead, Dock.Top); filtRight.Children.Add(frHead); filtRight.Children.Add(filtCurve);
        var filtRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(filtLeft, Dock.Left); filtRow.Children.Add(filtLeft);
        filtRow.Children.Add(new Border { Padding = new Thickness(12, 0, 0, 0), Child = filtRight });
        var filtMain = new Border { Padding = new Thickness(8, 6), Child = filtRow };

        // ---- contextual strips ----------------------------------------------
        Control LiveStrip()
        {
            var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Children =
            {
                Cap("LIVE", TextTertiary),
                MiniSlider("FM", "fmdepth", v => $"{v * 200:0}\u2009%", 46),
                MiniSlider("FEEDBACK", "feedback", v => $"{v * 100:0}\u2009%", 40),
                MiniSlider("TONE", "filfreq", v => $"{60 * Math.Pow(300, v):0}", 42),
                MiniSlider("GLIDE", "glide", v => v <= 0 ? "off" : $"{5 * Math.Pow(240, v):0}\u2009ms", 42),
            } };
            var mode = Toggle("MONO", "mono", Brass); mode.HorizontalAlignment = HorizontalAlignment.Right;
            var wrap = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(mode, 1); wrap.Children.Add(host); wrap.Children.Add(mode);
            return new Border { Height = 34, Background = Strip, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child = wrap };
        }
        Control AlgoStrip()
        {
            var num = Mono("", AccentBright); num.Margin = new Thickness(4, 0, 0, 0);
            var desc = Cap("", TextTertiary); desc.Margin = new Thickness(8, 0, 0, 0);
            readouts.Add(() => { int a = AlgoIdx(); num.Text = $"{a + 1} of {OperatorTopo.Count}"; desc.Text = OperatorTopo.Desc[a]; });
            var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("ALGORITHM", TextTertiary), num, desc } };
            var fbSlider = MiniSlider("FEEDBACK", "feedback", v => $"{v * 100:0}\u2009%", 46); fbSlider.HorizontalAlignment = HorizontalAlignment.Right;
            var wrap = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(fbSlider, 1); wrap.Children.Add(host); wrap.Children.Add(fbSlider);
            return new Border { Height = 34, Background = Strip, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child = wrap };
        }

        // ---- assemble: strip host + tab rail + swapping body ----------------
        var strips = new Control[] { LiveStrip(), AlgoStrip(), LiveStrip() };
        var mains = new Control[] { opsRow, algoRow, filtMain };
        var stripHost = new ContentControl();
        var mainHost = new ContentControl();

        string[] tabNames = { "OPS", "ALGO", "FILTER" };
        var tabBtns = new Border[3];
        void SelectTab(int t)
        {
            stripHost.Content = strips[t];
            mainHost.Content = mains[t];
            for (int i = 0; i < 3; i++)
            {
                bool on = i == t;
                tabBtns[i].Background = on ? AccentSubtleB : Brushes.Transparent;
                tabBtns[i].BorderBrush = on ? Brass : Brushes.Transparent;
                ((TextBlock)tabBtns[i].Child!).Foreground = on ? AccentBright : TextTertiary;
            }
            if (t == 1) applyEnv();
            Refresh();
        }
        var railStack = new StackPanel { Spacing = 3 };
        for (int i = 0; i < 3; i++)
        {
            int iv = i;
            var b = new Border { Height = 22, CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(8, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = tabNames[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center } };
            b.PointerPressed += (_, _) => SelectTab(iv);
            tabBtns[i] = b; railStack.Children.Add(b);
        }
        var rail = new Border { Width = 70, Background = Rail, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(5), Child = railStack };

        var bodyRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(rail, Dock.Left); bodyRow.Children.Add(rail); bodyRow.Children.Add(mainHost);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp };
        DockPanel.SetDock(stripHost, Dock.Top); root.Children.Add(stripHost); root.Children.Add(bodyRow);

        ctx.SetInstLiveViz(Refresh);
        applyEnv();
        SelectTab(0);
        return root;
    }
}
