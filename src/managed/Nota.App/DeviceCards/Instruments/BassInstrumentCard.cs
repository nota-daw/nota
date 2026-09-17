// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Bass editor (instrument kind 7): a mono-first
// subtractive bass synth laid out as a signal chain over two tabs — SIGNAL PATH
// (Osc → Sub → Filter → Output) and MODULATION (Amp/Filter envelopes → LFO →
// Global/glide/mono). The filter response and both envelopes are interactive drag
// graphs (VoltFilter / VoltEnv); the main oscillator uses a continuous Shape/PW
// morph; sub-wave, filter type/slope, LFO wave and mono/poly are chips.
// Modulation depths use teal knobs. Follows automation live via SetInstLiveViz.

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

internal sealed class BassInstrumentCard : IInstrumentCard
{
    // Mockup palette (matches the Nota Bass Mockup.html and VoltInstrumentCard).
    private static readonly IBrush CardBg = NotaPalette.BgApp;
    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TealLit = NotaPalette.TealBright;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush Handle = NotaPalette.TextSecondary;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);
    private static readonly IBrush ArrowC = NotaPalette.BorderStrong;

    public bool BodyOnly => true;
    public string Subtitle => "BASS SYNTH";
    public double CardWidth => 1060;

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
        (int, string) P(string id) => (I(id), id);
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        var filtGraph = new VoltFilter(engine, track);
        var ampEnv = new VoltEnv(engine, track);
        var filtEnv = new VoltEnv(engine, track) { Accent = TealLit };

        void Refresh()
        {
            int ft = Sel("filtype", 4);
            filtGraph.Target("FILTER", P("filfreq"), P("filreso"), ft);
            filtGraph.Refresh(); ampEnv.Refresh(); filtEnv.Refresh();
            foreach (var a in readouts) a();
        }

        // ---- shared builders ----
        Control K(string id, string name, bool mod = false, double sz = 34, double cw = 44)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, sz, cw, mod ? TealC : null);
        Control Row(double spacing, params Control[] cs)
        { var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) sp.Children.Add(c); return sp; }
        Control Lbl(string t, double fs, IBrush c) => new TextBlock { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c, VerticalAlignment = VerticalAlignment.Center };
        Control Cap(string t, IBrush? c = null) => new TextBlock { Text = t, FontSize = 9, Foreground = c ?? MutedC, VerticalAlignment = VerticalAlignment.Center };
        Control Arrow() => new Glyph(GlyphKind.ChevronRight, 9) { Foreground = ArrowC, Margin = new Thickness(1, 0) };

        // Param-backed chip strip.
        Control Chips(string id, string[] names, double fs = 9)
        {
            int n = names.Length;
            var seg = DeviceCardKit.Segments(names, () => Sel(id, n), iv => { SetP(id, iv / (float)(n - 1)); Refresh(); }, out var sync);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }

        // Waveform icon chips for sub oscillator (Sine=3, Square=1, Triangle=2).
        Control SubWaveChips()
        {
            int[] waveIcon = { 3, 1, 2 }; int n = 3; var arr = new Border[n]; var ic = new WaveIcon[n];
            void Hi() { int cur = Sel("subwave", 3); for (int i = 0; i < n; i++) { bool on = i == cur; arr[i].Background = on ? Amber : Inset; arr[i].BorderBrush = on ? Amber : Border2; ic[i].Stroke = on ? NotaPalette.TextOnAccent : MutedC; ic[i].InvalidateVisual(); } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < n; i++)
            { int iv = i; var wi = new WaveIcon(waveIcon[i]) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; var b = new Border { Width = 30, Height = 22, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Cursor = new Cursor(StandardCursorType.Hand), Child = wi }; b.PointerPressed += (_, _) => { SetP("subwave", iv / 2f); Hi(); Refresh(); }; arr[i] = b; ic[i] = wi; row.Children.Add(b); }
            readouts.Add(Hi); Hi();
            if (I("subwave") is var pi and >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), "subwave");
            return row;
        }

        // Local (non-param) segmented toggle.
        Control Seg(string[] names, int initial, Action<int> onPick, double fs = 9)
        {
            int cur = initial;
            return DeviceCardKit.Segments(names, () => cur, iv => { cur = iv; onPick(iv); }, out _);
        }

        // Panel frame: header + body in a bordered box. Set stretchBody=true when the
        // body should fill (e.g. filter graph); otherwise the body is centered.
        Border Panel(double width, Control header, Control body, bool stretchBody = false)
        {
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            grid.Children.Add(header);
            var host = new Border { Child = body };
            if (!stretchBody) { host.VerticalAlignment = VerticalAlignment.Center; host.HorizontalAlignment = HorizontalAlignment.Center; }
            Grid.SetRow(host, 1); grid.Children.Add(host);
            return new Border { Width = width, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, Padding = new Thickness(9, 8), Child = grid };
        }

        // ---- readout formatters ----
        string ShapeName(float s) => s < 0.17f ? "sine" : s < 0.42f ? "tri" : s < 0.75f ? "saw" : "pulse";
        string OctLabel(float v) { int o = (int)Math.Round((v - 0.5f) * 4f); return o.ToString("+0;−0;0"); }
        string ModLabel(float v) { int m = (int)Math.Round((v - 0.5f) * 200f); return m.ToString("+0;−0;0"); }

        // ---- SIGNAL PATH tab ----
        // OSC: continuous Shape + PW + tuning + level.
        var oscCap = new TextBlock { FontSize = 9, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        oscCap.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        readouts.Add(() => oscCap.Text = ShapeName(G("oscshape")) + " · " + OctLabel(G("oscoctave")) + "\u2009oct");
        var oscHead = new DockPanel { Children = { oscCap, Lbl("OSC", 10, TxtC) } };
        DockPanel.SetDock(oscCap, Dock.Right);
        var oscBody = Row(2, K("oscshape", "SHAPE"), K("oscpw", "PW"), K("oscoctave", "OCTAVE"), K("oscsemi", "SEMI"), K("osclevel", "LEVEL"));
        var oscBox = Panel(300, oscHead, oscBody);

        // SUB: wave icon chips + octave + level (stacked vertically).
        var subBody = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = {
            SubWaveChips(),
            new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, Children = { new TextBlock { Text = "OCT", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center }, Chips("suboctave", new[] { "-1", "-2" }) } },
            K("sublevel", "LEVEL") } };
        var subBox = Panel(168, Lbl("SUB", 10, TxtC), subBody);

        // FILTER: type/slope chips + response graph + knobs.
        var filtKnobHost = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        filtKnobHost.Children.Add(Row(2, K("filfreq", "CUTOFF"), K("filreso", "RESO")));
        filtKnobHost.Children.Add(Row(2, K("fildrive", "DRIVE"), K("filenv", "ENV", true), K("filkey", "KEY", true)));
        filtGraph.VerticalAlignment = VerticalAlignment.Stretch; filtGraph.MinHeight = 90;
        var filterBody = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Stretch };
        filterBody.Children.Add(filtGraph);
        Grid.SetColumn(filtKnobHost, 1); filterBody.Children.Add(filtKnobHost);
        var filtCap = new TextBlock { FontSize = 9, Foreground = TealC, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        filtCap.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        readouts.Add(() => filtCap.Text = "← env " + ModLabel(G("filenv")) + " · lfo " + ModLabel(G("fillfo")));
        var filtHeadLeft = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = {
            Lbl("FILTER", 10, TxtC), Chips("filtype", new[] { "LP", "HP", "BP", "Notch" }),
            new TextBlock { Text = "dB", FontSize = 8, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center },
            Chips("filslope", new[] { "12", "24" }) } };
        DockPanel.SetDock(filtCap, Dock.Right);
        var filterHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 6), Children = { filtCap, filtHeadLeft } };
        DockPanel.SetDock(filterHeader, Dock.Top);
        var filterBox = new Border { Width = 404, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, Padding = new Thickness(9, 8), Child = new DockPanel { LastChildFill = true, Children = { filterHeader, filterBody } } };

        // OUTPUT: drive + volume.
        var outBody = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { K("drive", "DRIVE"), K("volume", "VOLUME") } };
        var outBox = Panel(90, new TextBlock { Text = "OUTPUT", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TxtC, HorizontalAlignment = HorizontalAlignment.Center }, outBody);

        var signalPath = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Stretch,
            Children = { oscBox, Arrow(), subBox, Arrow(), filterBox, Arrow(), outBox } };

        // ---- MODULATION tab ----
        // Envelope panel: interactive graph + A/D/S/R readout cells.
        Border EnvPanel(string title, string route, VoltEnv gView, string[] ids, IBrush accent)
        {
            gView.Target(title, P(ids[0]), P(ids[1]), P(ids[2]), P(ids[3]));
            gView.Accent = accent;
            gView.VerticalAlignment = VerticalAlignment.Stretch; gView.MinHeight = 84;
            string[] lbl = { "A", "D", "S", "R" };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 4, Margin = new Thickness(0, 5, 0, 0) };
            for (int k = 0; k < 4; k++)
            {
                var v = new TextBlock { Text = Pct(G(ids[k])), FontSize = 9, Foreground = TxtC, HorizontalAlignment = HorizontalAlignment.Center };
                v.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
                string pid = ids[k]; var tv = v; readouts.Add(() => tv.Text = Pct(G(pid)));
                var cell = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(2, 2),
                    Child = new StackPanel { Children = { new TextBlock { Text = lbl[k], FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center }, v } } };
                Grid.SetColumn(cell, k); grid.Children.Add(cell);
            }
            DockPanel.SetDock(grid, Dock.Bottom);
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Lbl(title, 10, TxtC), new TextBlock { Text = route, FontSize = 9, Foreground = TealC, VerticalAlignment = VerticalAlignment.Center } } };
            DockPanel.SetDock(head, Dock.Top);
            var body = new DockPanel { LastChildFill = true, Children = { head, grid, gView } };
            return new Border { Width = 300, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, Padding = new Thickness(9, 8), Child = body };
        }

        var ampEnvBox = EnvPanel("AMP ENV", "→ Level", ampEnv, new[] { "attack", "decay", "sustain", "release" }, AmberLit);
        var filtEnvBox = EnvPanel("FILTER ENV", "→ Cutoff", filtEnv, new[] { "fattack", "fdecay", "fsustain", "frelease" }, TealLit);

        // LFO: wave chips + rate + targets.
        var lfoBody = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = {
            Chips("lfowave", new[] { "Sin", "Tri", "Saw", "Sqr", "S&H" }),
            Row(2, K("lforate", "RATE"), K("fillfo", "→ FLT", true), K("lfopitch", "→ PIT", true)) } };
        var lfoHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Lbl("LFO", 10, TxtC), Cap("→ Cut/Pitch", TealC) } };
        var lfoBox = Panel(196, lfoHead, lfoBody);

        // GLOBAL: glide, unison, velocity, voice mode.
        var globalBody = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = {
            Row(2, K("glide", "GLIDE"), K("unison", "UNISON"), K("velamp", "VEL→A", true), K("velfilter", "VEL→F", true)),
            new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "VOICE", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center }, Chips("mono", new[] { "Poly", "Mono" }) } } } };
        var globalBox = Panel(226, Lbl("GLOBAL", 10, TxtC), globalBody);

        var modulation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Stretch,
            Children = { ampEnvBox, filtEnvBox, lfoBox, globalBox } };

        // ---- tabs + body ----
        var tabHost = new ContentControl { VerticalAlignment = VerticalAlignment.Stretch };
        // Cache tab bodies to avoid re-parenting shared controls (VoltFilter, VoltEnv).
        Control?[] tabCache = new Control?[2];
        Control TabBody(int t) => tabCache[t] ??= t == 0 ? signalPath : modulation;
        int tabSel = 0;
        var tabs = Seg(new[] { "Signal Path", "Modulation" }, 0, i => { tabSel = i; tabHost.Content = TabBody(i); }, 11);
        var tabStrip = new Border { Height = 26, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { tabs } } };
        DockPanel.SetDock(tabStrip, Dock.Top);
        var contentHost = new Border { Padding = new Thickness(10, 8), Child = tabHost };
        var dockRoot = new DockPanel { LastChildFill = true, Background = CardBg, Children = { tabStrip, contentHost } };

        tabHost.Content = TabBody(tabSel);
        ampEnv.Target("AMP ENV", P("attack"), P("decay"), P("sustain"), P("release"));
        filtEnv.Target("FILTER ENV", P("fattack"), P("fdecay"), P("fsustain"), P("frelease"));
        ctx.SetInstLiveViz(Refresh);
        Refresh();
        return dockRoot;
    }
}
