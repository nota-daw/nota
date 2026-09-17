// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Grain editor (instrument kind 10), built to the 2d
// mockup (700×260): header (26) · sample strip (74) · always-visible playhead strip
// (30: Scan mode + Position/Scan/Spray sliders) · vertical tab rail (82) + swapped body
// (Grain shape icons + knobs / Pitch / Variation / Filter / Amp). Drop a sample from the
// browser to replace the procedural default. All controls are plugin params.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class GrainInstrumentCard : IInstrumentCard
{
    // Exact mockup palette.
    private static readonly IBrush CardBg = NotaPalette.BgApp;
    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush Handle = NotaPalette.TextSecondary;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);
    private static readonly Typeface Mono = NotaFonts.Mono;

    public bool BodyOnly => true;
    public string Subtitle => "GRANULAR";

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pcount = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pcount; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        void SetP(string id, float v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); }
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));

        var readouts = new List<Action>();
        var wave = new GrainWaveViz();
        if (engine.TryGetGrainInfo(track, out var gi) && gi.SampleId != 0
            && engine.TryGetSampleInfo(gi.SampleId, out var sinfo) && sinfo.Channels > 0)
        {
            var raw = engine.ReadSample(gi.SampleId);
            int ch = sinfo.Channels; long fr = sinfo.Frames;
            const int buckets = 340;
            var peaks = new float[buckets * 2];
            for (int b = 0; b < buckets; b++)
            {
                long a = (long)((double)b / buckets * fr), e = (long)((double)(b + 1) / buckets * fr);
                float mn = 0, mx = 0;
                for (long i = a; i < e; i++) for (int c = 0; c < ch; c++) { long k = i * ch + c; if (k < raw.Length) { float v = raw[k]; if (v < mn) mn = v; if (v > mx) mx = v; } }
                peaks[b * 2] = mn; peaks[b * 2 + 1] = mx;
            }
            wave.SetPeaks(peaks, sinfo.SampleRate > 0 ? fr / sinfo.SampleRate : 0);
        }

        var headBuf = new float[8];
        void Refresh()
        {
            double overlap = 1 + G("density") * 7, sizeSec = Exp(G("grainsize"), 0.004, 0.4);
            wave.SetState(G("position"), G("spray"), (int)Math.Round(G("scanmode") * 2), overlap / sizeSec);
            int hn = engine.GrainPlayPositions(track, headBuf);
            wave.SetPlayheads(headBuf, hn);
            foreach (var a in readouts) a();
        }

        // Header (dot / name / subtitle) is provided by the shared shell.

        // ---- horizontal param slider (playhead strip) ----
        Control Slider(string id, double trackW, Func<double, string> fmt)
        {
            int pi = I(id);
            string label = id == "position" ? "POSITION" : id == "scan" ? "SCAN" : "SPRAY";
            var row = DeviceCardKit.SliderRow(label, () => G(id), n => { if (pi >= 0) engine.PluginParamSet(track, -1, pi, (float)n); Refresh(); }, () => fmt(G(id)), out var sync,
                trackWidth: trackW, valueWidth: 40);
            readouts.Add(sync);
            return row;
        }

        // Scan-mode segmented (param-backed).
        Control ScanSeg()
        {
            string[] names = { "Scan", "Freeze", "Key" }; var arr = new Border[3];
            void Hi() { int cur = Math.Clamp((int)Math.Round(G("scanmode") * 2), 0, 2); for (int i = 0; i < 3; i++) { bool on = i == cur; arr[i].Background = on ? NotaPalette.Accent : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? NotaPalette.TextOnAccent : MutedC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < 3; i++) { int iv = i; var c = new Border { CornerRadius = NotaRadius.Badge, Padding = new Thickness(7, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = MutedC } }; c.PointerPressed += (_, _) => { SetP("scanmode", iv / 2f); Hi(); Refresh(); }; arr[i] = c; row.Children.Add(c); }
            readouts.Add(Hi); Hi();
            return new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
        }

        var strip = new Border { Height = 30, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0), Children = {
                ScanSeg(),
                Slider("position", 74, v => $"{v * 100:0}\u2009%"),
                Slider("scan", 60, v => $"{(v - 0.5) * 8:+0.0;−0.0;0.0}×"),
                Slider("spray", 52, v => $"{v * 100:0}\u2009%") } } };

        // ---- knobs (gauge) ----
        Control K(string id, string name, bool mod = false) => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, 40, 62, mod ? Teal : null);
        Control KnobRow(params Control[] ks) { var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; foreach (var k in ks) sp.Children.Add(k); return sp; }

        // Grain-shape icon selector (GrainShape param).
        Control ShapeIcons()
        {
            var boxes = new Border[4]; var icons = new GrainShapeIcon[4];
            void Hi() { int cur = Math.Clamp((int)Math.Round(G("grainshape") * 3), 0, 3); for (int i = 0; i < 4; i++) { bool on = i == cur; boxes[i].Background = on ? NotaPalette.Accent : Inset; boxes[i].BorderBrush = on ? NotaPalette.Accent : Border2; icons[i].Stroke = on ? NotaPalette.TextOnAccent : MutedC; icons[i].InvalidateVisual(); } }
            var col = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 4; i++)
            {
                int iv = i; var ic = new GrainShapeIcon(i) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Width = 34, Height = 22, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Cursor = new Cursor(StandardCursorType.Hand), Child = ic };
                b.PointerPressed += (_, _) => { SetP("grainshape", iv / 3f); Hi(); };
                boxes[i] = b; icons[i] = ic; col.Children.Add(b);
            }
            readouts.Add(Hi); Hi();
            return col;
        }

        // ---- vertical tab rail + swapped body ----
        string[] tabs = { "Grain", "Pitch", "Variation", "Filter", "Amp" };
        bool[] tealTab = { false, false, true, false, true };
        var tabHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        Control TabBody(int t) => t switch
        {
            1 => KnobRow(K("coarse", "COARSE"), K("fine", "FINE")),
            2 => KnobRow(K("posrand", "POSITION", true), K("pitchrand", "PITCH", true), K("panrand", "PAN", true)),
            3 => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { FilterTypeChips(), KnobRow(K("filfreq", "FREQ"), K("filreso", "RESO")) } },
            4 => KnobRow(K("attack", "ATTACK"), K("decay", "DECAY"), K("sustain", "SUSTAIN"), K("release", "RELEASE")),
            _ => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { ShapeIcons(), KnobRow(K("grainsize", "SIZE"), K("density", "DENSITY"), K("spread", "SPREAD", true)) } },
        };
        Control FilterTypeChips()
        {
            string[] names = { "LP", "HP", "BP" }; var arr = new Border[3];
            void Hi() { int cur = Math.Clamp((int)Math.Round(G("filtype") * 2), 0, 2); for (int i = 0; i < 3; i++) { bool on = i == cur; arr[i].Background = on ? NotaPalette.Accent : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? NotaPalette.TextOnAccent : MutedC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < 3; i++) { int iv = i; var c = new Border { CornerRadius = NotaRadius.Badge, Padding = new Thickness(7, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = 9, Foreground = MutedC } }; c.PointerPressed += (_, _) => { SetP("filtype", iv / 2f); Hi(); }; arr[i] = c; row.Children.Add(c); }
            readouts.Add(Hi); Hi();
            var seg = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
            if (I("filtype") is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), "filtype");
            return seg;
        }

        int tabSel = 0; var tabBtns = new Border[tabs.Length];
        void HiTabs() { for (int i = 0; i < tabs.Length; i++) { bool on = i == tabSel; tabBtns[i].Background = on ? NotaPalette.SurfaceRaised : Brushes.Transparent; tabBtns[i].BorderBrush = on ? (tealTab[i] ? TealC : Amber) : Brushes.Transparent; ((TextBlock)tabBtns[i].Child!).Foreground = on ? TxtC : MutedC; } }
        var railCol = new StackPanel { Spacing = 2 };
        for (int i = 0; i < tabs.Length; i++)
        {
            int iv = i; var b = new Border { Height = 20, CornerRadius = NotaRadius.Control, Padding = new Thickness(7, 0), BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Brushes.Transparent, Child = new TextBlock { Text = tabs[i], FontSize = 9, FontWeight = FontWeight.Medium, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center } };
            b.PointerPressed += (_, _) => { tabSel = iv; HiTabs(); tabHost.Content = TabBody(iv); };
            tabBtns[i] = b; railCol.Children.Add(b);
        }
        HiTabs(); tabHost.Content = TabBody(0);
        var rail = new Border { Width = 82, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(5), Child = railCol };
        DockPanel.SetDock(rail, Dock.Left);
        var body = new DockPanel { LastChildFill = true, Children = { rail, new Border { Padding = new Thickness(10, 0), Child = tabHost } } };

        // ---- assemble ----
        wave.Height = 74; DockPanel.SetDock(wave, Dock.Top);
        DockPanel.SetDock(strip, Dock.Top);
        var dockRoot = new DockPanel { LastChildFill = true, Background = CardBg, Children = { wave, strip, body } };

        ctx.SetInstLiveViz(Refresh);
        Refresh();
        return dockRoot;
    }
}
