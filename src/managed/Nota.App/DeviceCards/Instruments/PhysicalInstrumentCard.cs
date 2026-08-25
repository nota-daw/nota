// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Physical editor (instrument kind 2): a modal
// physical-modeling voice laid out as its signal chain across two tabs — EXCITER
// (Mallet → Noise, the noise burst carrying an interactive ADSR graph) and RESONATOR
// (Res 1 / Res 2 banks with a material Type chip row, a live modal-partial spectrum,
// and the tuning/damping knobs) plus a global OUTPUT panel. Follows automation live
// via RefreshSynthLive. Uses the shared gauge Knob and the DeviceCardKit tokens.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class PhysicalInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "MODAL";
    public double CardWidth => 720;

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        (int, string) P(string id) => (I(id), id);

        int rsel = 0;                              // which resonator bank the Type/knobs edit
        var readouts = new List<Action>();
        var viz = new CollisionViz();
        var noiseEnv = new VoltEnv(engine, track);
        noiseEnv.Target("NOISE ENV", P("noisea"), P("noised"), P("noises"), P("noiser"));

        void Refresh()
        {
            string pre = rsel == 0 ? "r1" : "r2";
            int rtype = Math.Clamp((int)Math.Round(G(pre + "type") * 5), 0, 5);
            viz.Set(rtype, G(pre + "decay"), G(pre + "material"), G(pre + "bright"), G(pre + "inharm"), G(pre + "ratio"), G(pre + "hit"));
            noiseEnv.Refresh();
            foreach (var a in readouts) a();
        }

        // ---- shared builders (mirror the Volt card's vocabulary) --------------
        Control K(string id, string name, bool mod = false) => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, 40, 52, mod ? Teal : null);
        Control KnobGrid(int cols, params Control[] ks)
        {
            var col = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < ks.Length; i += cols)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
                for (int j = i; j < Math.Min(i + cols, ks.Length); j++) row.Children.Add(ks[j]);
                col.Children.Add(row);
            }
            return col;
        }
        Control Arrow() => new TextBlock { Text = "▸", FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#3E3A31")), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0) };
        Control Cap(string t, IBrush? c = null) => new TextBlock { Text = t, FontSize = 9, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        Control Head(string title, params Control[] extras)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center });
            foreach (var e in extras) sp.Children.Add(e);
            return sp;
        }
        Border Panel(double width, Control header, Control body)
            => new Border { Width = width, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 8),
                Child = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { header, body } } };
        // A panel whose body fills the remaining height (for graphs / viz).
        Border GraphPanel(double width, Control header, Control body)
        {
            DockPanel.SetDock(header, Dock.Top);
            header.Margin = new Thickness(0, 0, 0, 8);
            return new Border { Width = width, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 8),
                Child = new DockPanel { LastChildFill = true, Children = { header, body } } };
        }

        // Text chips writing a normalized selector param.
        Control TextChips(string id, string[] names)
        {
            int n = names.Length; var arr = new Border[n];
            void Hi() { int cur = Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1); for (int i = 0; i < n; i++) { bool on = i == cur; arr[i].Background = on ? AccentSubtleB : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? AccentBright : TextSecondary; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            for (int i = 0; i < n; i++)
            {
                int iv = i;
                var chip = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = 9, Foreground = TextSecondary } };
                chip.PointerPressed += (_, _) => { if (I(id) is var ci and >= 0 && n > 1) { engine.PluginParamSet(track, -1, ci, iv / (float)(n - 1)); Hi(); Refresh(); } };
                arr[i] = chip; row.Children.Add(chip);
            }
            var container = new Border { Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(2), Child = row };
            Hi(); readouts.Add(Hi);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(container, MidiTarget.PluginParam(track, -1, pi), id);
            return container;
        }

        // Local (non-param) segmented toggle.
        Control Seg(string[] names, int initial, Action<int> onPick, double fs = 10)
        {
            var arr = new Border[names.Length]; int cur = initial;
            void Hi() { for (int i = 0; i < arr.Length; i++) { bool on = i == cur; arr[i].Background = on ? AccentSubtleB : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? AccentBright : TextTertiary; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            for (int i = 0; i < names.Length; i++)
            {
                int iv = i;
                var chip = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(9, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = fs, Foreground = TextTertiary } };
                chip.PointerPressed += (_, _) => { cur = iv; Hi(); onPick(iv); };
                arr[i] = chip; row.Children.Add(chip);
            }
            var container = new Border { Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(2), Child = row };
            Hi(); return container;
        }

        // ===== EXCITER tab: Mallet → Noise ==================================
        var malletBox = Panel(132, Head("MALLET"),
            KnobGrid(2, K("malletvol", "VOL"), K("malletstiff", "STIFF"), K("malletnoise", "NOISE"), K("malletcolor", "COLOR")));

        noiseEnv.VerticalAlignment = VerticalAlignment.Stretch; noiseEnv.MinHeight = 96;
        var noiseCap = new TextBlock { FontSize = 9, Foreground = Teal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        noiseCap.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        readouts.Add(() => { int en = (int)Math.Round((G("noiseenv") - 0.5) * 200); noiseCap.Text = "env " + en.ToString("+0;-0;0"); });
        var noiseHeadLeft = Head("NOISE", TextChips("noisetype", new[] { "LP", "BP", "HP" }));
        DockPanel.SetDock(noiseCap, Dock.Right);
        var noiseHeader = new DockPanel { Children = { noiseCap, noiseHeadLeft } };
        var noiseBody = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Stretch };
        noiseBody.Children.Add(noiseEnv);
        var noiseKnobs = KnobGrid(2, K("noisevol", "VOL"), K("noiseenv", "ENV", true), K("noisefreq", "FREQ"), K("noisereso", "RESO"));
        Grid.SetColumn(noiseKnobs, 1); noiseBody.Children.Add(noiseKnobs);
        var noiseBox = GraphPanel(356, noiseHeader, noiseBody);

        var exciterTab = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Left,
            Children = { malletBox, Arrow(), noiseBox } };

        // ===== RESONATOR tab: Res 1 / Res 2 + spectrum + OUTPUT =============
        var typeHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var knobHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        void ApplyResSel()
        {
            string pre = rsel == 0 ? "r1" : "r2";
            typeHost.Content = TextChips(pre + "type", new[] { "Beam", "Marimba", "String", "Membrane", "Plate", "Pipe" });
            knobHost.Content = KnobGrid(4,
                K(pre + "decay", "DECAY"), K(pre + "material", "MATERIAL"), K(pre + "bright", "BRIGHT"), K(pre + "inharm", "INHARM"),
                K(pre + "ratio", "RATIO"), K(pre + "hit", "HIT"), K(pre + "tune", "TUNE"));
            Refresh();
        }
        viz.VerticalAlignment = VerticalAlignment.Stretch; viz.MinHeight = 96; viz.Width = 190;
        var resTypeRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(typeHost, Dock.Left);
        resTypeRow.Children.Add(typeHost);
        var resInner = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(resTypeRow, Dock.Top);
        var resGraphRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Stretch };
        resGraphRow.Children.Add(viz);
        Grid.SetColumn(knobHost, 1); resGraphRow.Children.Add(knobHost);
        resInner.Children.Add(resTypeRow);
        resInner.Children.Add(resGraphRow);

        var onChips = TextChips("r2on", new[] { "Off", "On" });
        var resHeadLeft = Head("RESONATOR", Seg(new[] { "1", "2" }, 0, i => { rsel = i; ApplyResSel(); }), onChips);
        var structChips = TextChips("structure", new[] { "1▸2", "1+2" });
        var structWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            Children = { Cap("STRUCTURE"), structChips } };
        DockPanel.SetDock(structWrap, Dock.Right);
        var resHeader = new DockPanel { Children = { structWrap, resHeadLeft } };
        var resBox = GraphPanel(500, resHeader, resInner);

        var resonatorTab = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Left,
            Children = { resBox } };

        // Global OUTPUT panel — always visible on the right, both tabs.
        var outputBox = Panel(178, Head("OUTPUT"),
            KnobGrid(2, K("tune", "TUNE"), K("fine", "FINE"), K("noteoff", "NOTE OFF"), K("pan", "PAN"), K("volume", "VOLUME")));

        // ===== tabs + body (shared shell provides the header + frame) ======
        var tabHost = new ContentControl { VerticalAlignment = VerticalAlignment.Stretch, Content = exciterTab };
        var tabs = Seg(new[] { "Exciter", "Resonator" }, 0, i => tabHost.Content = i == 0 ? exciterTab : resonatorTab, 11);
        var tabStrip = new Border { Height = 26, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { tabs } } };
        DockPanel.SetDock(tabStrip, Dock.Top);
        DockPanel.SetDock(outputBox, Dock.Right);
        outputBox.Margin = new Thickness(6, 0, 0, 0);
        var contentBody = new DockPanel { LastChildFill = true, Children = { outputBox, tabHost } };
        var contentHost = new Border { Padding = new Thickness(10, 8), Child = contentBody };
        var dockRoot = new DockPanel { LastChildFill = true, Background = Raised, Children = { tabStrip, contentHost } };

        ctx.SetInstLiveViz(Refresh);
        ApplyResSel();
        Refresh();
        return dockRoot;
    }
}
