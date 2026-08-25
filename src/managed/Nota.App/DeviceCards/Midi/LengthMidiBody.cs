// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — the Nota Length editor (MIDI effect kind 3), mockup 3b (700×260 on the
// shared shell): a LIVE strip (length Mode — Sync / ms / Gate % — with its rate + LENGTH
// control, and the On note-on / On note-off trigger) over a body of the GATE viz (the forced
// length drawn on a time axis) beside a MODIFIERS rail (Vel→Len · Key→Len · Random, Legato,
// Clip length limit). Was "Nota Note Length".

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal sealed class LengthMidiBody : IMidiDeviceBody
{
    // Param indices — mirror MidiNoteLength.h.
    private const int PRate = 0, PGate = 1, PMode = 2, PMs = 3, PPercent = 4, PTrigger = 5,
                      PVelToLen = 6, PKeyToLen = 7, PRandom = 8, PLegato = 9, PClipLimit = 10;
    private static readonly double[] Div = { 0.25, 0.5, 0.75, 1.0 };
    private static readonly string[] RateLbl = { "1/16", "1/8", "1/8D", "1/4" };
    private static readonly string[] Modes = { "Sync", "ms", "Gate %" };

    private static readonly IBrush HdrBg = new SolidColorBrush(Color.Parse("#1E1C18"));
    private static readonly IBrush RailBg = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush Bd = new SolidColorBrush(Color.Parse("#2C2923"));
    private static readonly IBrush Inset = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush AmberLit = new SolidColorBrush(Color.Parse("#F0C060"));
    private static readonly IBrush Teal = new SolidColorBrush(Color.Parse("#5B9E9C"));
    private static readonly IBrush Txt = new SolidColorBrush(Color.Parse("#E9E4D8"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush Sub = new SolidColorBrush(Color.Parse("#A39D8F"));
    private static readonly IBrush Card = new SolidColorBrush(Color.Parse("#26231E"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#171613"));
    private static readonly IBrush AmberSubtle = new SolidColorBrush(Color.FromArgb(0x28, 0xD8, 0xA0, 0x3D));

    public double Width => 700;
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, mi = index;
        float G(int p) => engine.MidiEffectGetParam(track, mi, p);
        void S(int p, double v) => engine.MidiEffectSetParam(track, mi, p, (float)v);
        int Gi(int p) => (int)Math.Round(G(p));
        double Bpm() => engine.Bpm > 0 ? engine.Bpm : 120;

        var readouts = new List<Action>();
        static TextBlock Cap(string t, IBrush? c = null, double fs = 8) => new() { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? Muted, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        void Refresh() { foreach (var r in readouts) r(); }

        // Resulting length (ms) for the current params, and its modifier spread.
        double BaseMs()
        {
            int mode = Gi(PMode); double bpm = Bpm();
            if (mode == 1) return G(PMs);
            if (mode == 2) return (60000.0 / bpm) * (G(PPercent) / 100.0);         // % of a 1-beat reference
            return Div[Math.Clamp(Gi(PRate), 0, 3)] * Math.Clamp(G(PGate), 0.05, 2) * (60000.0 / bpm);
        }

        // ---- generic controls ----
        Control RawSlider(int p, double min, double max, Func<double, string> fmt, double w, IBrush fill, double valW = 42)
        {
            var val = Mono(fmt(G(p)), Txt); val.MinWidth = valW; val.TextAlignment = TextAlignment.Right;
            var fillB = new Border { Height = 3, Background = fill, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 7, Height = 9, Background = Sub, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 9, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            if (w > 0) slot.Width = w;
            slot.Children.Add(new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center });
            slot.Children.Add(fillB); slot.Children.Add(handle);
            void Vis(double v) { double W = slot.Bounds.Width, n = (v - min) / (max - min); fillB.Width = n * W; handle.Margin = new Thickness(Math.Clamp(n * W - 3.5, 0, Math.Max(0, W - 7)), 0, 0, 0); }
            bool drag = false;
            void SetX(double x) { double n = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); double v = min + n * (max - min); S(p, v); Vis(v); val.Text = fmt(v); Refresh(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); SetX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); } };
            MidiLearn.Bind(slot, MidiTarget.MidiDeviceParam(track, mi, p), engine.MidiEffectParamName(track, mi, p));
            slot.SizeChanged += (_, _) => Vis(G(p));
            readouts.Add(() => { if (!drag) { double v = G(p); Vis(v); val.Text = fmt(v); } });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(w > 0 ? "Auto,Auto" : "*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(val, 1); g.Children.Add(slot); g.Children.Add(val);
            return g;
        }
        Control Seg(int p, string[] names, Action<int>? extra = null, double padX = 7)
        {
            var arr = new Border[names.Length];
            void Hi() { int cur = Math.Clamp(Gi(p), 0, names.Length - 1); for (int i = 0; i < names.Length; i++) { bool on = i == cur; arr[i].Background = on ? Amber : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? Ink : Muted; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < names.Length; i++) { int iv = i; var c = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(padX, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = Muted } }; c.PointerPressed += (_, _) => { S(p, iv); extra?.Invoke(iv); Refresh(); }; arr[i] = c; row.Children.Add(c); }
            readouts.Add(Hi); Hi();
            var seg = new Border { Background = Inset, BorderBrush = Bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
            MidiLearn.Bind(seg, MidiTarget.MidiDeviceParam(track, mi, p), engine.MidiEffectParamName(track, mi, p));
            return seg;
        }
        // Outlined pill buttons (radio-style) bound to a 0/1 param — for the trigger choice.
        Control TrigSeg()
        {
            var opts = new[] { "On note-on", "On note-off" }; var arr = new Border[2];
            void Hi() { int cur = G(PTrigger) >= 0.5f ? 1 : 0; for (int i = 0; i < 2; i++) { bool on = i == cur; arr[i].Background = on ? AmberSubtle : Card; arr[i].BorderBrush = on ? Amber : Bd; ((TextBlock)arr[i].Child!).Foreground = on ? AmberLit : Sub; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            for (int i = 0; i < 2; i++) { int iv = i; var b = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = opts[i], FontSize = 9, Foreground = Sub } }; b.PointerPressed += (_, _) => { S(PTrigger, iv); Refresh(); }; arr[i] = b; row.Children.Add(b); }
            readouts.Add(Hi); Hi();
            MidiLearn.Bind(row, MidiTarget.MidiDeviceParam(track, mi, PTrigger), engine.MidiEffectParamName(track, mi, PTrigger));
            return row;
        }
        Control Toggle(int p, string label, IBrush accent)
        {
            var trk = new Border { Width = 18, Height = 10, CornerRadius = new CornerRadius(5), Background = Inset };
            var knob = new Ellipse { Width = 6, Height = 6, Fill = Muted, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            trk.Child = knob;
            var lbl = Cap(label, accent);
            void Sync() { bool on = G(p) >= 0.5f; trk.Background = on ? accent : Inset; knob.Fill = on ? Ink : Muted; knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left; knob.Margin = new Thickness(on ? 0 : 2, 0, on ? 2 : 0, 0); lbl.Foreground = on ? Sub : Muted; }
            var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Children = { trk, lbl } };
            host.PointerPressed += (_, _) => { S(p, G(p) >= 0.5f ? 0 : 1); Refresh(); };
            readouts.Add(Sync); Sync();
            MidiLearn.Bind(host, MidiTarget.MidiDeviceParam(track, mi, p), label);
            return host;
        }
        // Sync-mode rate chips.
        Control RateChips()
        {
            var arr = new Border[RateLbl.Length];
            void Hi() { int cur = Math.Clamp(Gi(PRate), 0, 3); for (int i = 0; i < RateLbl.Length; i++) { bool on = i == cur; arr[i].Background = on ? AmberSubtle : Card; arr[i].BorderBrush = on ? Amber : Bd; ((TextBlock)arr[i].Child!).Foreground = on ? AmberLit : Sub; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            for (int i = 0; i < RateLbl.Length; i++) { int iv = i; var c = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(6, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = Mono(RateLbl[i], Sub, 9) }; c.PointerPressed += (_, _) => { S(PRate, iv); Refresh(); }; arr[i] = c; row.Children.Add(c); }
            readouts.Add(Hi); Hi();
            MidiLearn.Bind(row, MidiTarget.MidiDeviceParam(track, mi, PRate), engine.MidiEffectParamName(track, mi, PRate));
            return row;
        }

        // ---- LIVE strip (Mode + contextual length + Trigger) ----
        var lenHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        void BuildLen()
        {
            lenHost.Children.Clear();
            int mode = Gi(PMode);
            lenHost.Children.Add(Cap("LENGTH"));
            if (mode == 1)
                lenHost.Children.Add(RawSlider(PMs, 10, 2000, v => $"{v:0} ms", 64, Amber));
            else if (mode == 2)
                lenHost.Children.Add(RawSlider(PPercent, 5, 200, v => $"{v:0} %", 64, Amber));
            else
            {
                lenHost.Children.Insert(0, RateChips());   // rate chips before LENGTH (Sync only)
                lenHost.Children.Add(RawSlider(PGate, 0.05, 2.0, v => $"{Div[Math.Clamp(Gi(PRate), 0, 3)] * v * (60000.0 / Bpm()):0} ms", 64, Amber));
            }
        }
        var modeSeg = Seg(PMode, Modes, _ => BuildLen());
        var liveL = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { modeSeg, lenHost } };
        var liveR = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { TrigSeg() } };
        var liveGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(liveR, 1); liveGrid.Children.Add(liveL); liveGrid.Children.Add(liveR);
        var liveStrip = new Border { Height = 34, Background = HdrBg, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child = liveGrid };

        // ---- GATE viz ----
        var gate = new LengthGateViz { VerticalAlignment = VerticalAlignment.Stretch };
        readouts.Add(() =>
        {
            double b = BaseMs();
            double vt = G(PVelToLen), rnd = G(PRandom);
            double min = b * Math.Max(0.05, 1 - vt) * Math.Max(0.05, 1 - rnd);
            double max = b * (1 + vt) * (1 + rnd);
            gate.Set(min, max, Bpm());
        });
        var gateHost = new Border { Padding = new Thickness(8, 7), Child = gate };

        // ---- MODIFIERS rail ----
        Control ModRow(string label, int p, Func<double, string> fmt)
        {
            var lbl = Cap(label); lbl.Width = 52;
            var sld = RawSlider(p, 0, 1, _ => "", 0, Teal, 0);
            var val = Mono(fmt(G(p)), Txt); val.Width = 34; val.TextAlignment = TextAlignment.Right;
            readouts.Add(() => val.Text = fmt(G(p)));
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(sld, 1); Grid.SetColumn(val, 2);
            g.Children.Add(lbl); g.Children.Add(sld); g.Children.Add(val);
            return g;
        }
        var modStack = new StackPanel { Spacing = 6, Children =
        {
            Cap("MODIFIERS"),
            ModRow("VEL → LEN", PVelToLen, v => $"{v * 100:0} %"),
            ModRow("KEY → LEN", PKeyToLen, v => $"{v * 100:0} %"),
            ModRow("RANDOM", PRandom, v => $"±{v * 100:0} %"),
            new Border { Height = 1, Background = Card, Margin = new Thickness(0, 2) },
            Toggle(PLegato, "Legato · retrigger held", Amber),
            Toggle(PClipLimit, "Clip length limit", Amber),
        } };
        var modRail = new Border { Width = 186, Background = RailBg, BorderBrush = Bd, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6), Child = modStack };

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(modRail, Dock.Right); body.Children.Add(modRail); body.Children.Add(gateHost);
        var root = new DockPanel { LastChildFill = true, Background = Ink };
        DockPanel.SetDock(liveStrip, Dock.Top); root.Children.Add(liveStrip); root.Children.Add(body);

        BuildLen();
        ctx.AddDeviceRefresher(Refresh);   // GATE viz follows tempo/param changes
        Refresh();
        return root;
    }
}
