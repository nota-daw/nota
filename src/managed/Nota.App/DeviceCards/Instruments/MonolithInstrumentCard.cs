// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Monolith editor (instrument kind 13), a faithful build
// of the "Nota Monolith" mockup (700×260): a Minimoog Model-D layout as three regions —
// an always-visible WHEELS column (pitch / mod), a centre tabbed panel (Controllers · Osc
// Bank / Modifiers) and a right tabbed panel (Mixer / Output). Oscillator ranges/detune,
// six waveform chips, the ladder filter response and both contours are all shown; every
// control is a plugin-param so automation / MIDI-learn / persist come for free. The card is
// BodyOnly — the shared shell provides the header (name / preset / A-B / bypass / meter).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class MonolithInstrumentCard : IInstrumentCard
{
    private static readonly IBrush CardBg = NotaPalette.BgApp;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Panel = NotaPalette.TextOnAccent; // dark ink over an engaged fill
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush BorderIn = NotaPalette.GraphBorder;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush TabBg = NotaPalette.SurfaceCard;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush RedC = NotaPalette.Accent;   // a state, not an alert: red is kept for recording and overload
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush Handle = NotaPalette.TextSecondary;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);

    private static readonly string[] Feet = { "LO", "32′", "16′", "8′", "4′", "2′" };

    public bool BodyOnly => true;
    public string Subtitle => "MONO · MODEL-D";

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        void SetP(string id, float v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); }
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        var filtCurve = new MonolithFilterCurve();
        var fEnvCurve = new MonolithEnvCurve { Accent = Amber };
        var aEnvCurve = new MonolithEnvCurve { Accent = TealC };

        void Refresh()
        {
            filtCurve.Set(G("cutoff"), G("emph")); filtCurve.InvalidateVisual();
            fEnvCurve.Set(G("fattack"), G("fdecay"), G("fsustain")); fEnvCurve.InvalidateVisual();
            aEnvCurve.Set(G("aattack"), G("adecay"), G("asustain")); aEnvCurve.InvalidateVisual();
            foreach (var a in readouts) a();
        }

        // ---- shared builders --------------------------------------------------
        Control Lbl(string t, double fs, IBrush c, FontWeight w = FontWeight.Bold)
            => new TextBlock { Text = t, FontSize = fs, FontWeight = w, Foreground = c, VerticalAlignment = VerticalAlignment.Center };
        Control Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }

        Control K(string id, string name, Func<float, string>? fmt = null, bool mod = false, double sz = 32, double cw = 46)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, fmt, sz, cw, mod ? TealC : null);

        // On/off pill toggle backed by a param (>0.5 = on).
        Control Toggle(string id, string label)
        {
            var wrap = Switch(label, () => G(id) > 0.5f, () => { SetP(id, G(id) > 0.5f ? 0f : 1f); Refresh(); }, out var sync);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(wrap, MidiTarget.PluginParam(track, -1, pi), label);
            return wrap;
        }

        // Param-backed chip strip (writes id = pick/(n-1)).
        Control Chips(string id, string[] names, double fs = 7)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Sel(id, n), iv => { SetP(id, iv / (float)(n - 1)); Refresh(); }, out var sync);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }

        // Six waveform icon chips (osc3 swaps shark-tooth → reverse saw).
        Control WaveChips(string id, bool osc3)
        {
            var arr = new Border[6]; var ic = new MonolithWaveIcon[6];
            void Hi() { int cur = Sel(id, 6); for (int i = 0; i < 6; i++) { bool on = i == cur; arr[i].Background = on ? NotaPalette.Accent : Brushes.Transparent; arr[i].BorderBrush = on ? NotaPalette.Accent : Border2; ic[i].Stroke = on ? NotaPalette.TextOnAccent : MutedC; ic[i].InvalidateVisual(); } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 6; i++)
            { int iv = i; var wi = new MonolithWaveIcon(i, osc3) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; var b = new Border { Width = 22, Height = 16, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Cursor = new Cursor(StandardCursorType.Hand), Child = wi }; b.PointerPressed += (_, _) => { SetP(id, iv / 5f); Hi(); Refresh(); }; arr[i] = b; ic[i] = wi; row.Children.Add(b); }
            readouts.Add(Hi); Hi();
            if (I(id) is var pi and >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), id);
            return row;
        }

        // Horizontal fill slider bound to a param.
        Control HSlider(string id, string name, Func<double, string> fmt, double nameW = 30, double valW = 20, IBrush? col = null)
        {
            int pi = I(id);
            var row = DeviceCardKit.SliderRow(name, () => G(id), n => { if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)n); Refresh(); }, () => fmt(G(id)), out var sync,
                begin: () => { if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); },
                end: () => { if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); },
                labelWidth: nameW, valueWidth: valW);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), name);
            return row;
        }

        // Vertical wheel slider bound to a param (pitch / mod). spring = auto-return to centre.
        Control VWheel(string id, string name, Func<double, string> fmt, IBrush? col = null, bool spring = false)
        {
            int pi = I(id); var lit = col ?? Amber;
            IBrush grad = NotaPalette.BgSunken;
            var trackBar = new Border { Width = 16, Background = grad, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Pill, VerticalAlignment = VerticalAlignment.Stretch };
            var mark = new Border { Height = 2, Background = lit, CornerRadius = NotaRadius.Bar, Margin = new Thickness(2, 0) };
            var lay = new Canvas { Width = 16, VerticalAlignment = VerticalAlignment.Stretch };
            var host = new Panel { Width = 16, VerticalAlignment = VerticalAlignment.Stretch, Children = { trackBar, lay } };
            lay.Children.Add(mark); mark.Width = 12;
            bool drag = false; DispatcherTimer? springT = null;
            void Vis(double v) { double h = host.Bounds.Height; if (h <= 0) h = 60; Canvas.SetTop(mark, (1 - v) * (h - 2)); }
            void From(PointerEventArgs e) { double h = host.Bounds.Height; double v = h > 0 ? Math.Clamp(1 - e.GetPosition(host).Y / h, 0, 1) : 0.5; if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)v); Vis(v); Refresh(); }
            void Spring()
            {
                springT?.Stop();
                springT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                springT.Tick += (_, _) =>
                {
                    double v = G(id); v += (0.5 - v) * 0.32;
                    if (Math.Abs(v - 0.5) < 0.002) { v = 0.5; springT!.Stop(); }
                    if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)v); Vis(v); Refresh();
                };
                springT.Start();
            }
            host.PointerPressed += (_, e) => { springT?.Stop(); drag = true; if (pi >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); e.Pointer.Capture(host); From(e); };
            host.PointerMoved += (_, e) => { if (drag) From(e); };
            host.PointerReleased += (_, e) => { if (drag) { drag = false; if (pi >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); e.Pointer.Capture(null); if (spring) Spring(); } };
            host.SizeChanged += (_, _) => Vis(G(id));
            var valTxt = new TextBlock { FontSize = 7, Foreground = lit, HorizontalAlignment = HorizontalAlignment.Center };
            valTxt.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            readouts.Add(() => { if (!drag) Vis(G(id)); valTxt.Text = fmt(G(id)); });
            var col2 = new DockPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var nm = new TextBlock { Text = name, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center };
            DockPanel.SetDock(nm, Dock.Bottom); DockPanel.SetDock(valTxt, Dock.Bottom);
            col2.Children.Add(valTxt); col2.Children.Add(nm); col2.Children.Add(host);
            if (pi >= 0) MidiLearn.Bind(col2, MidiTarget.PluginParam(track, -1, pi), name);
            return col2;
        }

        // ---- formatters -------------------------------------------------------
        string StFmt(float v) => $"{(v - 0.5f) * 5f:+0.0;−0.0;0.0}\u2009st";
        string GlideFmt(float v) => v <= 0.001f ? "off" : (Sec(0.002, 10, v) is var s && s >= 1 ? $"{s:0.0}\u2009s" : $"{s * 1000:0}\u2009ms");
        string DetFmt(float v) => $"{(v - 0.5f) * 14f:+0.00;−0.00;0.00}";
        string HzFmt(float v) { double hz = 16 * Math.Pow(1250, v); return hz >= 1000 ? $"{hz / 1000:0.0}k" : $"{hz:0}"; }
        string EmphFmt(float v) => $"{v * 10f:0.0}";
        string TimeFmt(double lo, double hi, float v) { double s = Sec(lo, hi, v); return s >= 1 ? $"{s:0.0}\u2009s" : $"{s * 1000:0}\u2009ms"; }
        string VolFmt(float v) => v <= 0.001f ? "−∞" : $"{20 * Math.Log10(v):0.0}\u2009dB";
        static double Sec(double lo, double hi, double v) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));

        // ======================================================================
        // LEFT — wheels column (always visible)
        // ======================================================================
        var wheels = new Border { Width = 56, Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5),
            Child = new DockPanel { LastChildFill = true, Children = {
                Head(Lbl("WHEELS", 7, MutedC)),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Children = {
                    VWheel("bend", "PITCH", v => $"{(v - 0.5) * 2 * (2 + G("bendrange") * 10):+0.0;−0.0;0.0}", null, spring: true),
                    VWheel("modwheel", "MOD", v => $"{v * 100:0}\u2009%", AmberLit) } } } } };
        DockPanel.SetDock(wheels, Dock.Left);
        Control Head(Control c) { DockPanel.SetDock(c, Dock.Top); ((Control)c).HorizontalAlignment = HorizontalAlignment.Center; return c; }

        // ======================================================================
        // CENTRE — Osc Bank / Modifiers tabs
        // ======================================================================
        // -- Osc Bank tab --
        Control ControllersStrip()
        {
            var toggles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("*,*,*"), ColumnSpacing = 8, RowSpacing = 2, VerticalAlignment = VerticalAlignment.Center };
            void T(string id, string lbl, int r, int c) { var t = Toggle(id, lbl); Grid.SetRow(t, r); Grid.SetColumn(t, c); toggles.Children.Add(t); }
            T("glideon", "Glide", 0, 0); T("legato", "Legato", 0, 1);
            T("decayon", "Decay", 1, 0); T("oscmodon", "Osc mod", 1, 1);
            T("filtmodon", "Filter mod", 2, 0); T("osc3kbd", "Osc 3 kbd", 2, 1);
            var knobs = Row(6, K("tune", "TUNE", StFmt, false, 32, 40), K("glide", "GLIDE", GlideFmt, false, 32, 40), K("modmix", "MOD MIX", v => $"{v * 100:0}\u2009%", false, 32, 40));
            var strip = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Height = 56 };
            Grid.SetColumn((Control)knobs, 0); Grid.SetColumn(toggles, 1);
            strip.Children.Add((Control)knobs); strip.Children.Add(toggles);
            return new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(6, 2), Child = strip };
        }
        Control OscRow(int n)
        {
            string p = $"o{n}";
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("14,50,54,*,40"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
            g.Children.Add(Cell(Lbl(n.ToString(), 10, TxtC, FontWeight.SemiBold), 0));
            g.Children.Add(Cell(K($"{p}range", "", v => Feet[Math.Clamp((int)Math.Round(v * 5), 0, 5)], false, 24, 46), 1));
            Control freq = n == 1
                ? new TextBlock { Text = "master", FontSize = 8, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                : K($"{p}tune", "", DetFmt, false, 24, 52);
            g.Children.Add(Cell(freq, 2));
            g.Children.Add(Cell(WaveChips($"{p}wave", n == 3), 3));
            string modTxt = n == 3 ? "free" : "kbd";
            var mt = new TextBlock { Text = modTxt, FontSize = 8, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            mt.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            if (n == 3) readouts.Add(() => mt.Text = G("osc3kbd") > 0.5f ? "kbd" : "free");
            g.Children.Add(Cell(mt, 4));
            return new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 0, n < 3 ? 1 : 0), Height = 44, Child = g };
        }
        Control Cell(Control c, int col) { var w = new Panel { Children = { c } }; c.HorizontalAlignment = col == 4 ? HorizontalAlignment.Right : (col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center); c.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(w, col); return w; }
        Control OscTab()
        {
            var hdr = new Grid { ColumnDefinitions = new ColumnDefinitions("14,50,54,*,40"), Height = 12, Margin = new Thickness(4, 0) };
            string[] h = { "#", "RANGE", "FREQ", "WAVEFORM", "MOD" };
            for (int i = 0; i < 5; i++) { var t = new TextBlock { Text = h[i], FontSize = 7, FontWeight = FontWeight.Bold, Foreground = NotaPalette.TextDisabled, HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : (i == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Center) }; Grid.SetColumn(t, i); hdr.Children.Add(t); }
            var table = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(hdr, Dock.Top);
            table.Children.Add(hdr);
            table.Children.Add(new StackPanel { Children = { OscRow(1), OscRow(2), OscRow(3) } });
            return new DockPanel { LastChildFill = true, Children = { WithDock(ControllersStrip(), Dock.Top), new Border { Padding = new Thickness(2, 2), Child = table } } };
        }

        // -- Modifiers tab --
        Control FilterPanel()
        {
            filtCurve.VerticalAlignment = VerticalAlignment.Stretch;
            filtCurve.Changed = (c, r) => {
                if (I("cutoff") is var ci and >= 0) engine.PluginParamSet(track, -1, ci, (float)c);
                if (I("emph") is var ri and >= 0) engine.PluginParamSet(track, -1, ri, (float)r);
                Refresh();
            };
            filtCurve.DragStarted += () => { engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "cutoff"); };
            filtCurve.DragEnded += () => { engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, "cutoff"); };
            var graph = new Border { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Child = filtCurve, Margin = new Thickness(0, 3, 0, 4) };
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,Auto"), ColumnSpacing = 2, VerticalAlignment = VerticalAlignment.Bottom };
            void C(Control c, int col) { Grid.SetColumn(c, col); knobs.Children.Add(c); }
            C(K("cutoff", "CUTOFF", HzFmt, false, 32, 44), 0);
            C(K("emph", "EMPHASIS", EmphFmt, false, 32, 40), 1);
            C(K("contour", "CONTOUR", v => $"{v * 100:0}\u2009%", true, 32, 44), 2);
            var kbd = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = {
                new TextBlock { Text = "KBD CTRL", FontSize = 7, FontWeight = FontWeight.Bold, Foreground = MutedC },
                Row(3, Toggle("kbd1", "1/3"), Toggle("kbd2", "2/3")) } };
            C(kbd, 3);
            var body = new DockPanel { LastChildFill = true, Children = { WithDock(Lbl("FILTER", 7, MutedC), Dock.Top), WithDock(knobs, Dock.Bottom), graph } };
            return new Border { Width = 210, BorderBrush = BorderIn, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(6, 4), Child = body };
        }
        Control ContourPanel(string title, string route, MonolithEnvCurve curve, string pre, IBrush col)
        {
            curve.VerticalAlignment = VerticalAlignment.Stretch;
            var graph = new Border { Background = Inset, BorderBrush = BorderIn, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Child = curve };
            var knobs = Row(1, K($"{pre}attack", "A", v => TimeFmt(0.001, 10, v), false, 26, 34), K($"{pre}decay", "D", v => TimeFmt(0.004, 20, v), false, 26, 34), K($"{pre}sustain", "S", v => $"{v * 100:0}\u2009%", false, 26, 34));
            DockPanel.SetDock((Control)knobs, Dock.Right);
            ((Control)knobs).Margin = new Thickness(6, 0, 0, 0);
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Height = 11, Margin = new Thickness(0, 0, 0, 3), Children = { Lbl(title, 7, MutedC), new TextBlock { Text = route, FontSize = 7, Foreground = col, VerticalAlignment = VerticalAlignment.Center } } };
            DockPanel.SetDock(head, Dock.Top);
            var inner = new DockPanel { LastChildFill = true, Children = { (Control)knobs, graph } };
            return new DockPanel { LastChildFill = true, Children = { head, inner } };
        }
        Control ModifiersTab()
        {
            var contours = new Grid { RowDefinitions = new RowDefinitions("*,*"), RowSpacing = 4, Margin = new Thickness(6, 2) };
            var c1 = ContourPanel("FILTER CONTOUR", "→ cutoff", fEnvCurve, "f", Amber);
            var c2 = ContourPanel("LOUDNESS CONTOUR", "→ amp", aEnvCurve, "a", TealC);
            Grid.SetRow((Control)c2, 1); contours.Children.Add((Control)c1); contours.Children.Add((Control)c2);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var fp = FilterPanel(); Grid.SetColumn(fp, 0); Grid.SetColumn(contours, 1);
            g.Children.Add(fp); g.Children.Add(contours);
            return g;
        }

        // ======================================================================
        // RIGHT — Mixer / Output tabs
        // ======================================================================
        Control MixRow(string onId, string lvlId, string name, IBrush? col = null)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,36,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var tog = Toggle(onId, ""); Grid.SetColumn(tog, 0);
            var lbl = new TextBlock { Text = name, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(lbl, 1);
            var sl = HSlider(lvlId, "", v => $"{v * 10:0.0}", 0, 22, col); sl.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetColumn(sl, 2);
            g.Children.Add(tog); g.Children.Add(lbl); g.Children.Add(sl);
            return g;
        }
        Control MixerTab()
        {
            var g = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"), RowSpacing = 6, Margin = new Thickness(2, 4), VerticalAlignment = VerticalAlignment.Top };
            void R(Control c, int r) { Grid.SetRow(c, r); g.Children.Add(c); }
            R(MixRow("mix1on", "mix1lvl", "OSC 1"), 0);
            R(MixRow("mix2on", "mix2lvl", "OSC 2"), 1);
            R(MixRow("mix3on", "mix3lvl", "OSC 3"), 2);
            R(MixRow("noiseon", "noiselvl", "NOISE"), 3);
            var nrow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var chips = Chips("noisetype", new[] { "White", "Pink" }); chips.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(chips, 0);
            var ext = HSlider("extlvl", "EXT", v => $"{v * 10:0.0}", 26, 22); ext.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetColumn(ext, 1);
            nrow.Children.Add(chips); nrow.Children.Add(ext);
            R(nrow, 4);
            var fb = new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0), Child = MixRow("exton", "feedback", "FEEDBACK", RedC) };
            R(fb, 5);
            return g;
        }
        Control OutputTab()
        {
            var vol = K("volume", "VOLUME", VolFmt, false, 44, 60);
            var sp = new StackPanel { Spacing = 6, Margin = new Thickness(2, 2), Children = {
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { vol } },
                new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = new StackPanel { Spacing = 4, Children = {
                    Toggle("a440", "A-440 tone"), Toggle("basscomp", "Bass compensation") } } },
                new Border { BorderBrush = BorderIn, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = new StackPanel { Spacing = 5, Children = {
                    LblRow("UNISON", Row(4, K("unison", "", v => $"{1 + (int)Math.Round(v * 6)}", false, 24, 30), K("unidetune", "", v => $"{v * 50:0}c", true, 24, 34))),
                    LblRow("PRIORITY", Chips("priority", new[] { "Low", "High", "Last" })),
                    LblRow("BEND", Chips("bendrange", new[] { "±2", "±4", "±5", "±7", "±9", "±12" })) } } } } };
            return sp;
        }
        Control LblRow(string t, Control c) { c.HorizontalAlignment = HorizontalAlignment.Left; return new Grid { ColumnDefinitions = new ColumnDefinitions("48,*"), Children = { WithCol(new TextBlock { Text = t, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center }, 0), WithCol(c, 1) } }; }

        // ---- tab shells -------------------------------------------------------
        var centreHost = new ContentControl { VerticalAlignment = VerticalAlignment.Stretch };
        var rightHost = new ContentControl { VerticalAlignment = VerticalAlignment.Stretch };
        Control? oscTabC = null, modTabC = null, mixTabC = null, outTabC = null;
        Control CentreBody(int t) => t == 1 ? (modTabC ??= ModifiersTab()) : (oscTabC ??= OscTab());
        Control RightBody(int t) => t == 1 ? (outTabC ??= OutputTab()) : (mixTabC ??= MixerTab());

        var drift = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Width = 96, Children = { new TextBlock { Text = "DRIFT", FontSize = 7, FontWeight = FontWeight.Bold, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center }, WithStretch(HSlider("drift", "", v => $"{v * 100:0}\u2009%", 0, 22)) } };

        var centre = TabPanel(new[] { "Controllers · Osc Bank", "Modifiers" }, drift, centreHost, CentreBody);
        var right = new Border { Width = 186, Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Child = TabInner(new[] { "Mixer", "Output" }, null, rightHost, RightBody, true) };
        DockPanel.SetDock(right, Dock.Right);

        // ---- assemble ---------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { wheels, right, centre } };
        var root = new Border { Background = RailBg, Child = bodyRow };

        ctx.SetInstLiveViz(Refresh);
        Refresh();
        return root;

        // local: build the centre tab panel (flex) with an extras control in the tab bar
        Control TabPanel(string[] tabs, Control? extras, ContentControl host, Func<int, Control> body)
            => new Border { Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Margin = new Thickness(5, 0), Child = TabInner(tabs, extras, host, body, false) };
        Control TabInner(string[] tabs, Control? extras, ContentControl host, Func<int, Control> body, bool centered)
        {
            int sel = 0; var btns = new Border[tabs.Length];
            void Hi() { for (int i = 0; i < tabs.Length; i++) { bool on = i == sel; btns[i].Background = on ? TabBg : Brushes.Transparent; btns[i].BorderBrush = on ? Amber : Brushes.Transparent; ((TextBlock)btns[i].Child!).Foreground = on ? AmberLit : MutedC; } }
            var bar = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < tabs.Length; i++)
            { int iv = i; var b = new Border { Padding = new Thickness(10, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = centered ? HorizontalAlignment.Stretch : HorizontalAlignment.Left, Child = new TextBlock { Text = tabs[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } }; if (centered) b.Width = double.NaN; b.PointerPressed += (_, _) => { sel = iv; Hi(); host.Content = body(iv); }; btns[i] = b; bar.Children.Add(b); }
            if (centered) { bar.HorizontalAlignment = HorizontalAlignment.Stretch; for (int i = 0; i < btns.Length; i++) { btns[i].Width = 92; } }
            var barDock = new DockPanel { Height = 20, LastChildFill = false, Children = { WithDock(bar, Dock.Left) } };
            if (extras != null) { DockPanel.SetDock(extras, Dock.Right); barDock.Children.Add(extras); }
            var barBorder = new Border { BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock };
            DockPanel.SetDock(barBorder, Dock.Top);
            Hi(); host.Content = body(sel);
            return new DockPanel { LastChildFill = true, Children = { barBorder, new Border { Padding = new Thickness(2), Child = host } } };
        }
        static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
        static Control WithCol(Control c, int col) { Grid.SetColumn(c, col); return c; }
        static Control WithStretch(Control c) { c.HorizontalAlignment = HorizontalAlignment.Stretch; return c; }
    }
}
