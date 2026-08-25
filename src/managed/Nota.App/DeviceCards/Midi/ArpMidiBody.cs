// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — the Nota Arp editor (MIDI effect kind 0), rebuilt to mockup 2k
// (700×260 on the shared shell): a LIVE strip (Sync/Free · rate divisions · Gate · Swing
// · Hold/Retrig) over a body of lane-rail (78: Velocity/Length/Chance/Ratchet/Transpose
// as the tab rail + step count) | the step sequencer (velocities, playhead, ratchets,
// muted wells) | a right rail (120: Order · Octaves · Direction · Transpose · CC→lane).
// Toggles are outlined; brass fill encodes only a value or the active choice.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class ArpMidiBody : IMidiDeviceBody
{
    // Param layout (mirrors Arpeggiator.h): 13 globals then 7 lanes × 16 steps.
    internal const int GRate = 0, GSync = 1, GFreeRate = 2, GGate = 3, GOctaves = 4, GOctaveMode = 5,
                       GOrder = 6, GSwing = 7, GHold = 8, GRetrig = 9, GTranspose = 10, GLoop = 11;
    internal const int LVel = 13, LLen = 29, LChance = 45, LRatchet = 61, LTransp = 77, LOn = 93, LCC = 109;

    private static readonly IBrush HdrBg = new SolidColorBrush(Color.Parse("#1E1C18"));
    private static readonly IBrush RailBg = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush Border2 = new SolidColorBrush(Color.Parse("#2C2923"));
    private static readonly IBrush Inset = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush AmberLit = new SolidColorBrush(Color.Parse("#F0C060"));
    private static readonly IBrush TealC = new SolidColorBrush(Color.Parse("#5B9E9C"));
    private static readonly IBrush TxtC = new SolidColorBrush(Color.Parse("#E9E4D8"));
    private static readonly IBrush MutedC = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush AmberSubtle = new SolidColorBrush(Color.FromArgb(0x28, 0xD8, 0xA0, 0x3D));

    private static readonly string[] RateNames = { "1/1", "1/2", "1/4", "1/8", "1/8T", "1/16", "1/16T", "1/32" };
    private static readonly string[] OrderNames = { "Up", "Down", "Up-Dn", "Converge", "As played", "Chord", "Random" };
    private static readonly string[] DirGlyphs = { "↑", "↓", "↕", "?" };
    internal static readonly double[] RateDivisions = { 4.0, 2.0, 1.0, 0.5, 1.0 / 3.0, 0.25, 1.0 / 6.0, 0.125 };

    public double Width => 700;
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine; int track = ctx.TrackId, mi = index;
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        void S(int p, double v) => engine.MidiEffectSetParam(track, mi, p, (float)v);
        int GI(int p) => (int)Math.Round(G(p));

        var readouts = new System.Collections.Generic.List<Action>();

        // Param-backed segmented control (fill = active choice).
        Control Seg(int p, string[] names, double fs = 9, double padX = 6, int off = 0)
        {
            var arr = new Border[names.Length];
            void Hi() { int cur = Math.Clamp(GI(p) - off, 0, names.Length - 1); for (int i = 0; i < names.Length; i++) { bool on = i == cur; arr[i].Background = on ? AmberSubtle : Brushes.Transparent; arr[i].BorderBrush = on ? Amber : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? AmberLit : MutedC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < names.Length; i++) { int iv = i; var c = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(padX, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = fs, Foreground = MutedC } }; c.PointerPressed += (_, _) => { S(p, iv + off); Hi(); }; arr[i] = c; row.Children.Add(c); }
            readouts.Add(Hi); Hi();
            var seg = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
            MidiLearn.Bind(seg, MidiTarget.MidiDeviceParam(track, mi, p), engine.MidiEffectParamName(track, mi, p));
            return seg;
        }
        // Outlined toggle: filled only when on.
        Control OutToggle(int p, string label)
        {
            var b = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(9, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.SemiBold } };
            void Hi() { bool on = G(p) > 0.5f; b.Background = on ? AmberSubtle : Brushes.Transparent; b.BorderBrush = on ? Amber : Border2; ((TextBlock)b.Child!).Foreground = on ? AmberLit : MutedC; }
            b.PointerPressed += (_, _) => { S(p, G(p) > 0.5f ? 0 : 1); Hi(); };
            readouts.Add(Hi); Hi();
            MidiLearn.Bind(b, MidiTarget.MidiDeviceParam(track, mi, p), label);
            return b;
        }
        // Compact drag value (label above a field you drag; double-click resets).
        Control DragVal(int p, string name, double min, double max, Func<double, string> fmt)
        {
            var val = new TextBlock { Text = fmt(G(p)), FontSize = 10, Foreground = TxtC, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var field = new Border { MinWidth = 44, Height = 17, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(3, 0), Cursor = new Cursor(StandardCursorType.SizeWestEast), Child = val };
            bool drag = false; double sx = 0, sv = 0;
            field.PointerPressed += (_, e) =>
            {
                if (e.ClickCount == 2) { float d = engine.MidiEffectParamDefault(track, mi, p); S(p, d); val.Text = fmt(d); e.Handled = true; return; }
                drag = true; sx = e.GetPosition(field).X; sv = G(p); e.Pointer.Capture(field); e.Handled = true;
            };
            field.PointerMoved += (_, e) => { if (!drag) return; double v = Math.Clamp(sv + (e.GetPosition(field).X - sx) / 120.0 * (max - min), min, max); S(p, v); val.Text = fmt(v); };
            field.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
            MidiLearn.Bind(field, MidiTarget.MidiDeviceParam(track, mi, p), name);
            readouts.Add(() => { if (!drag) val.Text = fmt(G(p)); });
            return new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Children = { new TextBlock { Text = name, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center }, field } };
        }
        Control Stepper(int p, int min, int max)
        {
            var val = new TextBlock { Text = GI(p).ToString(), FontSize = 11, Foreground = TxtC, MinWidth = 18, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            Border Btn(string t, int d) { var b = new Border { Width = 18, Height = 18, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = t, FontSize = 11, Foreground = TxtC, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }; b.PointerPressed += (_, _) => { int v = Math.Clamp(GI(p) + d, min, max); S(p, v); val.Text = v.ToString(); }; return b; }
            readouts.Add(() => val.Text = GI(p).ToString());
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { Btn("−", -1), val, Btn("+", +1) } };
        }

        // ---- LIVE strip ----
        string Pct(double v) => $"{v * 100:0}%";
        var live = new Border { Height = 34, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0), Children = {
                Seg(GSync, new[] { "Free", "Sync" }, 9, 7),
                Seg(GRate, RateNames, 8, 4),
                DragVal(GGate, "GATE", 0, 2, Pct), DragVal(GSwing, "SWING", 0, 1, Pct),
                OutToggle(GHold, "Hold"), OutToggle(GRetrig, "Retrig") } } };

        // ---- step grid + lane rail ----
        var grid = new ArpGrid(engine, track, mi);
        (string name, int b, double min, double max, bool vel, bool isInt)[] lanes =
        {
            ("Velocity", LVel, 0, 1, true, false), ("Length", LLen, 0, 2, false, false), ("Chance", LChance, 0, 1, false, false),
            ("Ratchet", LRatchet, 1, 8, false, true), ("Transpose", LTransp, -24, 24, false, true),
        };
        var laneName = new TextBlock { Text = "VELOCITY", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center };
        var stepRead = new TextBlock { FontSize = 9, Foreground = TealC, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        stepRead.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        int laneSel = 0;
        var laneBtns = new Border[lanes.Length];
        void SelectLane(int li)
        {
            var l = lanes[li]; laneSel = li;
            grid.SetLane(l.b, l.min, l.max, l.vel, l.isInt);
            laneName.Text = l.name.ToUpperInvariant();
            for (int i = 0; i < lanes.Length; i++) { bool on = i == li; laneBtns[i].Background = on ? new SolidColorBrush(Color.Parse("#26231E")) : Brushes.Transparent; laneBtns[i].BorderBrush = on ? Amber : Brushes.Transparent; ((TextBlock)laneBtns[i].Child!).Foreground = on ? TxtC : MutedC; }
        }
        var railCol = new StackPanel { Spacing = 2 };
        for (int i = 0; i < lanes.Length; i++)
        {
            int li = i;
            var b = new Border { Height = 22, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 0), BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = lanes[i].name, FontSize = 10, FontWeight = FontWeight.Medium, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center } };
            b.PointerPressed += (_, _) => SelectLane(li);
            laneBtns[i] = b; railCol.Children.Add(b);
        }
        var stepsBox = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Children = { new TextBlock { Text = "STEPS", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center }, Stepper(GLoop, 1, 16) } };
        DockPanel.SetDock(railCol, Dock.Top); DockPanel.SetDock(stepsBox, Dock.Bottom);
        var laneRail = new Border { Width = 78, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(5, 8),
            Child = new DockPanel { LastChildFill = false, Children = { railCol, stepsBox } } };

        var gridHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4), LastChildFill = false, Children = { laneName,
            new TextBlock { Text = "drag to draw · ⌥ ramp · click # to mute", FontSize = 8, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) } } };
        DockPanel.SetDock(stepRead, Dock.Right); gridHeader.Children.Add(stepRead);
        grid.VerticalAlignment = VerticalAlignment.Stretch;
        DockPanel.SetDock(gridHeader, Dock.Top);
        var stepArea = new Border { Padding = new Thickness(8, 6), Child = new DockPanel { LastChildFill = true, Children = { gridHeader, grid } } };

        // ---- right rail ----
        var orderGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 2, RowSpacing = 2 };
        for (int i = 0; i < 4; i++) orderGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var orderChips = new Border[OrderNames.Length];
        void HiOrder() { int cur = Math.Clamp(GI(GOrder), 0, OrderNames.Length - 1); for (int i = 0; i < OrderNames.Length; i++) { bool on = i == cur; orderChips[i].Background = on ? AmberSubtle : Inset; orderChips[i].BorderBrush = on ? Amber : Border2; ((TextBlock)orderChips[i].Child!).Foreground = on ? AmberLit : MutedC; } }
        for (int i = 0; i < OrderNames.Length; i++)
        {
            int iv = i; var c = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(2, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = OrderNames[i], FontSize = 8, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center } };
            c.PointerPressed += (_, _) => { S(GOrder, iv); HiOrder(); };
            Grid.SetColumn(c, i % 2); Grid.SetRow(c, i / 2); orderChips[i] = c; orderGrid.Children.Add(c);
        }
        readouts.Add(HiOrder); HiOrder();

        Control Block(string label, IBrush lc, Control ctl) => new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = lc, VerticalAlignment = VerticalAlignment.Center }, ctl } };
        // Full-width segmented control: equal-star columns so the buttons fill the rail evenly.
        Control SegFill(int p, string[] names, int off = 0)
        {
            int n = names.Length; var arr = new Border[n];
            var grid = new Grid { ColumnSpacing = 2 };
            void Hi() { int cur = Math.Clamp(GI(p) - off, 0, n - 1); for (int i = 0; i < n; i++) { bool on = i == cur; arr[i].Background = on ? AmberSubtle : Inset; arr[i].BorderBrush = on ? Amber : Border2; ((TextBlock)arr[i].Child!).Foreground = on ? AmberLit : MutedC; } }
            for (int i = 0; i < n; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                int iv = i; var c = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(0, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center } };
                c.PointerPressed += (_, _) => { S(p, iv + off); Hi(); }; arr[i] = c; Grid.SetColumn(c, i); grid.Children.Add(c);
            }
            readouts.Add(Hi); Hi();
            MidiLearn.Bind(grid, MidiTarget.MidiDeviceParam(track, mi, p), engine.MidiEffectParamName(track, mi, p));
            return grid;
        }
        var rightRail = new Border { Width = 120, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 7),
            Child = new StackPanel { Spacing = 5, Children = {
                Block("ORDER", TxtC, orderGrid),
                Block("OCT", MutedC, SegFill(GOctaves, new[] { "1", "2", "3", "4" }, off: 1)),
                Block("DIR", MutedC, SegFill(GOctaveMode, DirGlyphs)),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "TRANSP", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center }, DragVal(GTranspose, "", -24, 24, v => $"{v:+0;-0;0} st") } } } } };

        // ---- assemble ----
        DockPanel.SetDock(laneRail, Dock.Left); DockPanel.SetDock(rightRail, Dock.Right);
        var body = new DockPanel { LastChildFill = true, Children = { laneRail, rightRail, stepArea } };
        DockPanel.SetDock(live, Dock.Top);
        var root = new DockPanel { LastChildFill = true, Background = new SolidColorBrush(Color.Parse("#171613")), Children = { live, body } };

        SelectLane(0);
        // Live playhead + step readout.
        ctx.AddDeviceRefresher(() =>
        {
            int loop = Math.Clamp(GI(GLoop), 1, 16);
            double stepBeats = G(GSync) > 0.5f ? RateDivisions[Math.Clamp(GI(GRate), 0, 7)] : 0.25;
            int cur = -1;
            if (engine.IsPlaying && stepBeats > 1e-6) cur = (int)(Math.Floor(engine.PositionBeats / stepBeats) % loop);
            grid.SetPlayhead(cur);
            int rs = cur >= 0 ? cur : 0;
            stepRead.Text = $"step {rs + 1} · {(int)Math.Round(G(LVel + rs) * 127)}";
            foreach (var a in readouts) a();
        });
        foreach (var a in readouts) a();
        return root;
    }
}
