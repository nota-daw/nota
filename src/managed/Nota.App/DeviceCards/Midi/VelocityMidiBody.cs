// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Velocity editor (MIDI effect kind 4), mockup 3b (700×260 on the
// shared shell): a LIVE strip (shape Mode — Curve / Compand / Fixed — with its drive/fixed +
// random controls) over a body of the TRANSFER curve (input→output velocity, the last notes
// plotted on it) beside a right rail (Out Range, Random on + direction, a Last-12-notes
// histogram). The transfer maths mirror MidiVelocity.h so picture == sound.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal sealed class VelocityMidiBody : IMidiDeviceBody
{
    private const int PDrive = 0, PFixed = 1, POutLo = 2, PRandom = 3, PMode = 4, POutHi = 5, PRandomDir = 6;
    private static readonly string[] Modes = { "Curve", "Compand", "Fixed" };
    private static readonly string[] Dirs = { "Both", "Up", "Down" };

    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush AmberDim = NotaPalette.Wash(NotaPalette.Accent, 0x73);
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Muted = NotaPalette.TextTertiary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Ink = NotaPalette.TextOnAccent;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);

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
        double lastRnd = 0.2;
        static TextBlock Cap(string t, IBrush? c = null, double fs = 8) => new() { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? Muted, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        void Refresh() { foreach (var r in readouts) r(); }

        // Transfer (mirrors MidiVelocity::transfer + the out-range remap).
        double Transfer(double n)
        {
            int mode = Gi(PMode); double drive = Math.Clamp(G(PDrive), 0.05, 2), fixd = G(PFixed);
            double shaped = mode == 2 ? fixd
                : mode == 1 ? Math.Clamp(0.5 + Math.Sign(2 * n - 1) * Math.Pow(Math.Abs(2 * n - 1), 1 / drive) * 0.5, 0, 1)
                : Math.Pow(Math.Clamp(n, 0, 1), 1 / drive);
            double lo = G(POutLo), hi = G(POutHi); if (lo > hi) (lo, hi) = (hi, lo);
            return lo + shaped * (hi - lo);
        }

        // ---- generic controls ----
        Control MiniSlider(string label, int p, double min, double max, Func<double, string> fmt, double w)
        {
            var val = Mono(fmt(G(p)), Txt); val.MinWidth = 26;
            var fill = new Border { Height = 3, Background = Amber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 7, Height = 9, Background = Sub, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Width = w, Height = 9, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
            slot.Children.Add(new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center });
            slot.Children.Add(fill); slot.Children.Add(handle);
            void Vis(double v) { double n = (v - min) / (max - min); fill.Width = n * w; handle.Margin = new Thickness(Math.Clamp(n * w - 3.5, 0, w - 7), 0, 0, 0); }
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

        // ---- LIVE strip: mode + contextual controls ----
        var stripHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        void BuildStrip()
        {
            stripHost.Children.Clear();
            int mode = Gi(PMode);
            if (mode == 2) stripHost.Children.Add(MiniSlider("FIXED", PFixed, 0, 1, v => $"{v * 127:0}", 54));
            else stripHost.Children.Add(MiniSlider("DRIVE", PDrive, 0.05, 2, v => $"{v:0.00}", 54));
            stripHost.Children.Add(MiniSlider("RANDOM", PRandom, 0, 1, v => $"±{v * 0.5 * 127:0}", 48));
        }
        var modeSeg = Seg(PMode, Modes, _ => BuildStrip());
        var liveStrip = new Border { Height = 34, Background = HdrBg, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { modeSeg, stripHost } } };

        // ---- TRANSFER panel ----
        var viz = new VelocityCurveViz { VerticalAlignment = VerticalAlignment.Stretch };
        var scope = new float[24];
        var readout = Mono("", AmberLit); readout.HorizontalAlignment = HorizontalAlignment.Right;
        var transHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 11 };
        Grid.SetColumn(readout, 1); transHead.Children.Add(Cap("TRANSFER")); transHead.Children.Add(readout);
        var axis = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 10 };
        var ax1 = Mono("64", NotaPalette.TextDisabled, 8); ax1.HorizontalAlignment = HorizontalAlignment.Center;
        var ax2 = Mono("out 127", NotaPalette.TextDisabled, 8); ax2.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(ax1, 1); Grid.SetColumn(ax2, 2);
        axis.Children.Add(Mono("in 1", NotaPalette.TextDisabled, 8)); axis.Children.Add(ax1); axis.Children.Add(ax2);
        var transDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(transHead, Dock.Top); DockPanel.SetDock(axis, Dock.Bottom);
        transDock.Children.Add(transHead); transDock.Children.Add(axis); transDock.Children.Add(viz);
        var transPanel = new Border { Background = Inset, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4), Margin = new Thickness(8, 7), Child = transDock };

        readouts.Add(() =>
        {
            int n = engine.MidiEffectScope(track, mi, scope);
            var dots = new List<(double, double)>();
            for (int k = 0; k + 1 < n; k += 2) dots.Add((scope[k], scope[k + 1]));
            int li = engine.MidiEffectLastIn(track, mi), lo = engine.MidiEffectLastOut(track, mi);
            (double, double)? last = li >= 0 ? (li / 127.0, lo / 127.0) : null;
            viz.Set(Transfer, dots, last);
            double dev = G(PRandom) * 0.5 * 127;
            readout.Text = Gi(PMode) == 2 ? $"fixed {G(PFixed) * 127:0} · ±{dev:0}"
                : $"drive {Math.Clamp(G(PDrive), 0.05, 2):0.00} · ±{dev:0} random";
        });

        // ---- right rail ----
        // OUT RANGE — a dual-handle range bar over [POutLo, POutHi], shown as 1..127.
        var rangeVals = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var loT = Mono("", Txt); var hiT = Mono("", Txt); hiT.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(hiT, 2); rangeVals.Children.Add(loT); rangeVals.Children.Add(hiT);
        var rangeFill = new Border { Background = NotaPalette.Wash(NotaPalette.Accent, 0x73), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
        var rangeBar = new Panel { Height = 8, Background = Inset, Cursor = new Cursor(StandardCursorType.Hand) };
        rangeBar.Children.Add(rangeFill);
        var rangeClip = new Border { Height = 8, CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = rangeBar };
        int rDrag = 0;   // 1 = lo, 2 = hi
        void RangeVis() { double W = rangeBar.Bounds.Width, lo = G(POutLo), hi = G(POutHi); rangeFill.Margin = new Thickness(lo * W, 0, 0, 0); rangeFill.Width = Math.Max(0, (hi - lo) * W); loT.Text = $"{Math.Max(1, lo * 127):0}"; hiT.Text = $"{hi * 127:0}"; }
        void RangeX(double x) { double n = Math.Clamp(x / Math.Max(1, rangeBar.Bounds.Width), 0, 1); if (rDrag == 1) S(POutLo, Math.Min(n, G(POutHi))); else S(POutHi, Math.Max(n, G(POutLo))); RangeVis(); Refresh(); }
        rangeBar.PointerPressed += (_, e) => { double x = e.GetPosition(rangeBar).X, n = x / Math.Max(1, rangeBar.Bounds.Width); rDrag = Math.Abs(n - G(POutLo)) <= Math.Abs(n - G(POutHi)) ? 1 : 2; e.Pointer.Capture(rangeBar); RangeX(x); };
        rangeBar.PointerMoved += (_, e) => { if (rDrag > 0) RangeX(e.GetPosition(rangeBar).X); };
        rangeBar.PointerReleased += (_, e) => { if (rDrag > 0) { rDrag = 0; e.Pointer.Capture(null); } };
        rangeBar.SizeChanged += (_, _) => RangeVis();
        readouts.Add(() => { if (rDrag == 0) RangeVis(); });

        // RANDOM ON toggle (amount lives in the strip; this gates it, remembering the amount).
        var rndTrk = new Border { Width = 18, Height = 10, CornerRadius = new CornerRadius(5), Background = Inset };
        var rndKnob = new Ellipse { Width = 6, Height = 6, Fill = Muted, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        rndTrk.Child = rndKnob;
        var rndLbl = Cap("RANDOM ON", Teal);
        void RndSync() { bool on = G(PRandom) > 0; rndTrk.Background = on ? Teal : Inset; rndKnob.Fill = on ? Ink : Muted; rndKnob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left; rndKnob.Margin = new Thickness(on ? 0 : 2, 0, on ? 2 : 0, 0); rndLbl.Foreground = on ? Teal : Muted; if (on) lastRnd = G(PRandom); }
        var rndToggle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Children = { rndTrk, rndLbl } };
        rndToggle.PointerPressed += (_, _) => { if (G(PRandom) > 0) S(PRandom, 0); else S(PRandom, lastRnd > 0 ? lastRnd : 0.2); Refresh(); };
        readouts.Add(RndSync); RndSync();

        var dirRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("MODE"), Seg(PRandomDir, Dirs, null, 8, 6) } };

        // LAST 12 NOTES histogram.
        var bars = new Border[12];
        var barRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Height = 22, VerticalAlignment = VerticalAlignment.Bottom };
        for (int i = 0; i < 12; i++) { bars[i] = new Border { Width = 9, CornerRadius = new CornerRadius(1), VerticalAlignment = VerticalAlignment.Bottom, Background = AmberDim, Height = 2 }; barRow.Children.Add(bars[i]); }
        readouts.Add(() =>
        {
            for (int i = 0; i < 12; i++)
            {
                double v = i * 2 + 1 < scope.Length ? scope[i * 2 + 1] : 0;   // output velocity
                bars[i].Height = Math.Max(2, v * 22);
                bool newest = i == 11;
                bars[i].Background = v <= 0.001 ? Inset : newest ? AmberLit : AmberDim;
            }
        });
        var histBlock = new StackPanel { Spacing = 3, Children = { Cap("LAST 12 NOTES"), barRow } };

        var rail = new StackPanel { Spacing = 5, Children =
        {
            Cap("OUT RANGE"), rangeClip, rangeVals,
            new Border { Height = 1, Background = NotaPalette.SurfaceRaised, Margin = new Thickness(0, 1) },
            rndToggle, dirRow,
        } };
        var railDock = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Stretch };
        DockPanel.SetDock(rail, Dock.Top); DockPanel.SetDock(histBlock, Dock.Bottom);
        railDock.Children.Add(rail); railDock.Children.Add(histBlock);
        var railBorder = new Border { Width = 160, Background = RailBg, BorderBrush = Bd, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 6), Child = railDock };

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(railBorder, Dock.Right); body.Children.Add(railBorder); body.Children.Add(transPanel);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp };
        DockPanel.SetDock(liveStrip, Dock.Top); root.Children.Add(liveStrip); root.Children.Add(body);

        BuildStrip();
        ctx.AddDeviceRefresher(Refresh);   // curve dots + histogram follow playback
        Refresh();
        return root;
    }
}
