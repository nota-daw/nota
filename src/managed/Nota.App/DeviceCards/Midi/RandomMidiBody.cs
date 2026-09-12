// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Random editor (MIDI effect kind 5), mockup 3b (700×260 on the
// shared shell): a LIVE strip (Chance · Distribution Gauss/Even/Walk · Lock seed / Re-roll)
// over a body of WHAT VARIES (one row per dimension — Note · Velocity · Timing · Skip ·
// Octave — with its amount, coloured by category: pitch brass, timing teal, skip red) beside
// a DISTRIBUTION rail (the drawn shape, Rate Per note / Per bar, Stay in scale).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal sealed class RandomMidiBody : IMidiDeviceBody
{
    private const int PChance = 0, PNoteRange = 1, PVelAmt = 2, PTimeAmt = 3, PSkip = 4, POctAmt = 5,
                      PDist = 6, PRate = 7, PStayInScale = 8, PLocked = 9, PSeed = 10;
    private static readonly string[] Dists = { "Gauss", "Even", "Walk" };
    private static readonly string[] Rates = { "Per note", "Per bar" };

    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush Red = NotaPalette.Ink("#D0603F");
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Muted = NotaPalette.TextTertiary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Card = NotaPalette.SurfaceRaised;
    private static readonly IBrush Ink = NotaPalette.TextOnAccent;
    private static readonly IBrush TealSubtle = NotaPalette.Wash(NotaPalette.Teal, 0x24);

    public double Width => 700;
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, mi = index;
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        void S(int p, double v) => engine.MidiEffectSetParam(track, mi, p, (float)v);
        int Gi(int p) => (int)Math.Round(G(p));

        var readouts = new List<Action>();
        static TextBlock Cap(string t, IBrush? c = null, double fs = 8) => new() { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? Muted, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        void Refresh() { foreach (var r in readouts) r(); }

        // ---- generic controls ----
        Control MiniSlider(string label, int p, double min, double max, Func<double, string> fmt, double w)
        {
            var val = Mono(fmt(G(p)), Txt); val.MinWidth = 30;
            var fill = new Border { Height = 3, Background = Amber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 8, Height = 9, Background = Sub, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Width = w, Height = 9, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            slot.Children.Add(new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center });
            slot.Children.Add(fill); slot.Children.Add(handle);
            void Vis(double v) { double n = (v - min) / (max - min); fill.Width = n * w; handle.Margin = new Thickness(Math.Clamp(n * w - 4, 0, w - 8), 0, 0, 0); }
            bool drag = false;
            void SetX(double x) { double n = Math.Clamp(x / w, 0, 1); double v = min + n * (max - min); S(p, v); Vis(v); val.Text = fmt(v); Refresh(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); SetX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
            MidiLearn.Bind(slot, MidiTarget.MidiDeviceParam(track, mi, p), engine.MidiEffectParamName(track, mi, p));
            readouts.Add(() => { if (!drag) { double v = G(p); Vis(v); val.Text = fmt(v); } });
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap(label), slot, val } };
        }
        Control Seg(int p, string[] names, Action<int>? extra = null, double fs = 9, double padX = 7)
        {
            var arr = new Border[names.Length];
            void Hi() { int cur = Math.Clamp(Gi(p), 0, names.Length - 1); for (int i = 0; i < names.Length; i++) { bool on = i == cur; arr[i].Background = on ? Amber : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? Ink : Muted; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < names.Length; i++) { int iv = i; var c = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(padX, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = fs, FontWeight = FontWeight.SemiBold, Foreground = Muted } }; c.PointerPressed += (_, _) => { S(p, iv); extra?.Invoke(iv); Refresh(); }; arr[i] = c; row.Children.Add(c); }
            readouts.Add(Hi); Hi();
            var seg = new Border { Background = Inset, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
            MidiLearn.Bind(seg, MidiTarget.MidiDeviceParam(track, mi, p), engine.MidiEffectParamName(track, mi, p));
            return seg;
        }
        Control Toggle(int p, string label, IBrush accent)
        {
            var trk = new Border { Width = 18, Height = 10, CornerRadius = new CornerRadius(5), Background = Inset };
            var knob = new Ellipse { Width = 6, Height = 6, Fill = Muted, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            trk.Child = knob;
            var lbl = Cap(label, accent);
            void Sync() { bool on = G(p) >= 0.5f; trk.Background = on ? accent : Inset; knob.Fill = on ? Ink : Muted; knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left; knob.Margin = new Thickness(on ? 0 : 2, 0, on ? 2 : 0, 0); lbl.Foreground = on ? accent : Muted; }
            var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Children = { trk, lbl } };
            host.PointerPressed += (_, _) => { S(p, G(p) >= 0.5f ? 0 : 1); Refresh(); };
            readouts.Add(Sync); Sync();
            MidiLearn.Bind(host, MidiTarget.MidiDeviceParam(track, mi, p), label);
            return host;
        }
        // One "what varies" row: name · amount bar (category colour) · range text.
        Control VaryRow(string name, int p, double min, double max, Func<double, string> fmt, IBrush colour)
        {
            var nm = new TextBlock { Text = name, FontSize = 9, Foreground = Txt, Width = 56, VerticalAlignment = VerticalAlignment.Center };
            var val = Mono(fmt(G(p)), Sub); val.Width = 46; val.TextAlignment = TextAlignment.Right;
            var fill = new Border { Background = colour, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
            var slot = new Panel { Height = 9, Cursor = new Cursor(StandardCursorType.Hand), Background = Inset };
            slot.Children.Add(fill);
            var slotClip = new Border { Height = 9, CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = slot, VerticalAlignment = VerticalAlignment.Center };
            void Vis(double v) { fill.Width = (v - min) / (max - min) * slot.Bounds.Width; }
            bool drag = false;
            void SetX(double x) { double n = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); double v = min + n * (max - min); S(p, v); Vis(v); val.Text = fmt(v); Refresh(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); SetX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
            MidiLearn.Bind(slot, MidiTarget.MidiDeviceParam(track, mi, p), engine.MidiEffectParamName(track, mi, p));
            slot.SizeChanged += (_, _) => Vis(G(p));
            readouts.Add(() => { if (!drag) { Vis(G(p)); val.Text = fmt(G(p)); } });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 7, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slotClip, 1); Grid.SetColumn(val, 2);
            g.Children.Add(nm); g.Children.Add(slotClip); g.Children.Add(val);
            return new Border { Height = 26, Child = g };
        }

        // ---- LIVE strip ----
        var chance = MiniSlider("CHANCE", PChance, 0, 1, v => $"{v * 100:0} %", 110);
        var distSeg = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("DIST"), Seg(PDist, Dists) } };
        var seedTxt = Mono("", Teal, 9);
        readouts.Add(() => seedTxt.Text = $"seed {Gi(PSeed)}" + (G(PLocked) >= 0.5f ? " · locked" : ""));
        var lockBtn = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = "Lock seed", FontSize = 9 } };
        void LockSync() { bool on = G(PLocked) >= 0.5f; lockBtn.Background = on ? TealSubtle : Card; lockBtn.BorderBrush = on ? Teal : Bd; ((TextBlock)lockBtn.Child!).Foreground = on ? Teal : Sub; }
        lockBtn.PointerPressed += (_, _) => { S(PLocked, G(PLocked) >= 0.5f ? 0 : 1); Refresh(); };
        readouts.Add(LockSync); LockSync();
        var reroll = new Border { Background = Card, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = "Re-roll", FontSize = 9, Foreground = Sub } };
        reroll.PointerPressed += (_, _) => { long s = (long)Gi(PSeed); s = (s * 1103515245L + 12345L) % 1000L; if (s <= 0) s += 1000; S(PSeed, s); Refresh(); };
        var liveR = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Children = { seedTxt, lockBtn, reroll } };
        var liveL = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { chance, distSeg } };
        var liveGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(liveR, 1); liveGrid.Children.Add(liveL); liveGrid.Children.Add(liveR);
        var liveStrip = new Border { Height = 34, Background = HdrBg, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child = liveGrid };

        // ---- WHAT VARIES ----
        var vHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 11 };
        var vh1 = Cap("amount · range"); vh1.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(vh1, 1);
        vHead.Children.Add(Cap("WHAT VARIES")); vHead.Children.Add(vh1);
        var varyStack = new StackPanel { Spacing = 3, Children =
        {
            vHead,
            VaryRow("Note", PNoteRange, 0, 12, v => v < 0.5 ? "off" : $"±{v:0} st", Amber),
            VaryRow("Velocity", PVelAmt, 0, 1, v => v < 0.01 ? "off" : $"±{v * 0.5 * 127:0}", Amber),
            VaryRow("Timing", PTimeAmt, 0, 1, v => v < 0.01 ? "off" : $"±{v * 50:0} ms", Teal),
            VaryRow("Skip", PSkip, 0, 1, v => v < 0.01 ? "off" : $"{v * 100:0} %", Red),
            VaryRow("Octave", POctAmt, 0, 1, v => { int n = (int)Math.Round(v * 2); return n <= 0 ? "off" : $"±{n} oct"; }, Amber),
        } };
        var varyPanel = new Border { Padding = new Thickness(8, 6), Child = varyStack };

        // ---- DISTRIBUTION rail ----
        var distViz = new DistributionViz { Height = 56 };
        distViz.Set(Gi(PDist));
        readouts.Add(() => distViz.Set(Gi(PDist)));
        var distAxis = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var da1 = Mono("centre", Muted, 8); da1.HorizontalAlignment = HorizontalAlignment.Center;
        var da2 = Mono("+max", NotaPalette.TextDisabled, 8); da2.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(da1, 1); Grid.SetColumn(da2, 2);
        distAxis.Children.Add(Mono("−max", NotaPalette.TextDisabled, 8)); distAxis.Children.Add(da1); distAxis.Children.Add(da2);
        var rateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("RATE"), Seg(PRate, Rates, null, 8, 8) } };
        var railStack = new StackPanel { Spacing = 5, Children =
        {
            Cap("DISTRIBUTION"), distViz, distAxis,
            new Border { Height = 1, Background = Card, Margin = new Thickness(0, 1) },
            rateRow, Toggle(PStayInScale, "STAY IN SCALE", Teal),
        } };
        var rail = new Border { Width = 210, Background = RailBg, BorderBrush = Bd, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6), Child = railStack };

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(rail, Dock.Right); body.Children.Add(rail); body.Children.Add(varyPanel);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp };
        DockPanel.SetDock(liveStrip, Dock.Top); root.Children.Add(liveStrip); root.Children.Add(body);

        Refresh();
        return root;
    }
}
