// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in EQ-3 (device kind 16) body, built to mockup 3j: a
// PERFORMANCE EQ, not a small EQ-8. A CROSSOVER strip (Low/Mid & Mid/High crossover
// sliders as real Hz + a 24/48 dB slope toggle) over a body of three band strips
// (Low / Mid / High, each a vertical fader with a centre detent + KILL button, hued
// rust / amber / slate from the track palette) next to a large response graph with the
// pre-EQ spectrum behind it. The bands are grabbed and dropped, so they get faders — the
// curve is display-only.

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

internal sealed class Eq3DeviceBody : IDeviceBody
{
    // Param indices — must match Eq3.h.
    private const int Low = 0, Mid = 1, High = 2, LowKill = 3, MidKill = 4, HighKill = 5,
                      FreqLo = 6, FreqHi = 7, Slope = 8, Gain = 9;

    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush FieldBorder = NotaPalette.GraphBorder;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush LabelC = NotaPalette.TextSecondary;
    private static readonly IBrush KillC = NotaPalette.Danger;
    private static readonly IBrush KillSubtle = NotaPalette.Wash(NotaPalette.Danger, 0x2E);
    private static readonly IBrush DetentC = NotaPalette.BorderStrong;
    private static readonly IBrush HandleC = NotaPalette.TextSecondary;

    private static readonly (string name, string span, IBrush hue)[] Bands =
    {
        ("LOW",  "20 – 250 Hz",    NotaPalette.Ink("#C4756A")),
        ("MID",  "250 Hz – 2.5 k", NotaPalette.Ink("#C99C55")),
        ("HIGH", "2.5 – 20 kHz",   NotaPalette.Ink("#6D8FB5")),
    };

    public double Width => 700;
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void SetP(int p, float v) => engine.DeviceSetParam(track, di, p, v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));

        var readouts = new List<Action>();
        Control Cap(string t, IBrush? c = null) => new TextBlock { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? MutedC, VerticalAlignment = VerticalAlignment.Center };

        // ---- response curve (display-only) ----
        var curve = new EqThreeCurve(engine, track, di) { VerticalAlignment = VerticalAlignment.Stretch };
        void SyncCurve() => curve.Set(P(Low), P(Mid), P(High), P(LowKill) >= 0.5f, P(MidKill) >= 0.5f, P(HighKill) >= 0.5f,
                                      Exp(P(FreqLo), 50, 2000), Exp(P(FreqHi), 500, 18000), P(Slope) >= 0.5f);
        ctx.AddDeviceRefresher(curve.Tick);

        // ---- formatters ----
        static string BandDb(double v) => $"{(v - 0.5) * 30:+0.0;-0.0;0.0}";
        string HzLo(double v) { double f = Exp(v, 50, 2000); return f >= 1000 ? $"{f / 1000:0.00} kHz" : $"{(int)Math.Round(f)} Hz"; }
        string HzHi(double v) { double f = Exp(v, 500, 18000); return f >= 1000 ? $"{f / 1000:0.00} kHz" : $"{(int)Math.Round(f)} Hz"; }

        // ---- vertical band fader with a centre (0 dB) detent ----
        Control VFader(int p, IBrush hue)
        {
            var trackBar = new Border { Width = 4, Background = Inset, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
            var detent = new Border { Height = 1, Background = DetentC, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 22, Height = 9, Background = NotaPalette.SurfaceHover, BorderBrush = NotaPalette.TextDisabled, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Child = new Border { Height = 2, Background = hue, CornerRadius = new CornerRadius(1), Margin = new Thickness(3, 2.5, 3, 0), VerticalAlignment = VerticalAlignment.Top } };
            var slot = new Panel { Width = 26, Children = { trackBar, detent, handle } };
            bool drag = false;
            void Upd() { double v = P(p); double H = slot.Bounds.Height; double y = (1 - v) * (H - 9); handle.Margin = new Thickness(0, Math.Clamp(y, 0, Math.Max(0, H - 9)), 0, 0); }
            void SetFromY(double yPos) { double H = slot.Bounds.Height; double v = Math.Clamp(1 - yPos / Math.Max(1, H - 9), 0, 1); if (Math.Abs(v - 0.5) < 0.025) v = 0.5; SetP(p, (float)v); SyncCurve(); Upd(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); Begin(p); SetFromY(e.GetPosition(slot).Y - 4.5); };
            slot.PointerMoved += (_, e) => { if (drag) SetFromY(e.GetPosition(slot).Y - 4.5); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); End(p); } };
            MidiLearn.Bind(slot, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            readouts.Add(() => { if (!drag) Upd(); });
            return slot;
        }

        // ---- KILL toggle ----
        Control KillBtn(int p)
        {
            var tb = new TextBlock { Text = "KILL", FontSize = 8, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(0, 1), HorizontalAlignment = HorizontalAlignment.Stretch, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            void Sync() { bool on = P(p) >= 0.5f; b.Background = on ? KillSubtle : Brushes.Transparent; b.BorderBrush = on ? KillC : DetentC; tb.Foreground = on ? KillC : MutedC; }
            b.PointerPressed += (_, e) => { e.Handled = true; Begin(p); SetP(p, P(p) >= 0.5f ? 0f : 1f); End(p); Sync(); SyncCurve(); };
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), "KILL");
            readouts.Add(Sync);
            return b;
        }

        // ---- one band strip ----
        Control BandStrip(int idx, int gainP, int killP)
        {
            var (name, span, hue) = Bands[idx];
            var db = new TextBlock { Text = BandDb(P(gainP)), FontSize = 10, Foreground = TxtC, HorizontalAlignment = HorizontalAlignment.Center };
            db.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            readouts.Add(() => db.Text = BandDb(P(gainP)));
            var span2 = new TextBlock { Text = span, FontSize = 8, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center };
            span2.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var col = new DockPanel { LastChildFill = true, Margin = new Thickness(5, 5) };
            DockPanel.SetDock(col.AddDock(new TextBlock { Text = name, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = LabelC, HorizontalAlignment = HorizontalAlignment.Center }, Dock.Top), Dock.Top);
            col.AddDock(span2, Dock.Top);
            col.AddDock(KillBtn(killP), Dock.Bottom);
            col.AddDock(db, Dock.Bottom);
            col.Children.Add(new Border { Child = VFader(gainP, hue), Margin = new Thickness(0, 4) });
            return new Border { Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Width = 84,
                Child = new Border { BorderBrush = hue, BorderThickness = new Thickness(0, 2, 0, 0), CornerRadius = new CornerRadius(6, 6, 0, 0), Child = col } };
        }

        // Crossover slider (label + slider + Hz) for the CROSSOVER strip.
        Control Xover(int p, string label, IBrush hue, Func<double, string> fmt)
        {
            var fill = new Border { Height = 3, Background = Amber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var track2 = new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 8, Height = 9, Background = HandleC, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 11, Width = 64, Children = { track2, fill, handle } };
            var val = new TextBlock { Text = fmt(P(p)), FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, Width = 52, TextAlignment = TextAlignment.Right };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            bool drag = false;
            void Upd() { double v = P(p); double W = slot.Bounds.Width; double hx = v * W; handle.Margin = new Thickness(Math.Clamp(hx - 4, 0, Math.Max(0, W - 8)), 0, 0, 0); fill.Width = hx; val.Text = fmt(v); }
            void SetFromX(double x) { double v = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); SetP(p, (float)v); SyncCurve(); Upd(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); Begin(p); SetFromX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetFromX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); End(p); } };
            readouts.Add(() => { if (!drag) Upd(); });
            var lab = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = hue, VerticalAlignment = VerticalAlignment.Center };
            var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { lab, slot, val } };
            MidiLearn.Bind(host, MidiTarget.DeviceParam(track, di, p), label);
            return host;
        }

        // Slope segmented toggle (24 / 48 dB).
        Control SlopeSeg()
        {
            var opts = new[] { "24 dB", "48 dB" }; var cells = new Border[2]; var texts = new TextBlock[2];
            void Sync() { bool hi = P(Slope) >= 0.5f; for (int i = 0; i < 2; i++) { bool on = (i == 1) == hi; cells[i].Background = on ? AmberSubtle : Brushes.Transparent; cells[i].BorderBrush = on ? Amber : Brushes.Transparent; texts[i].Foreground = on ? AmberLit : MutedC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < 2; i++) { int iv = i; var tb = new TextBlock { Text = opts[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = MutedC }; var c = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(7, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb }; c.PointerPressed += (_, e) => { e.Handled = true; SetP(Slope, iv == 1 ? 1f : 0f); SyncCurve(); Sync(); }; cells[i] = c; texts[i] = tb; row.Children.Add(c); }
            readouts.Add(Sync);
            var seg = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, Slope), engine.DeviceParamName(track, di, Slope));
            return seg;
        }

        // ================= CROSSOVER strip =================
        var strip = new Border { Height = 34, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), Children = {
                WithDock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Children = {
                    Cap("CROSSOVER"),
                    Xover(FreqLo, "LOW / MID", Bands[0].hue, HzLo),
                    Xover(FreqHi, "MID / HIGH", Bands[2].hue, HzHi) } }, Dock.Left),
                WithDock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("SLOPE"), SlopeSeg() } }, Dock.Right) } } };

        // ================= band strips =================
        var strips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 7), VerticalAlignment = VerticalAlignment.Stretch };
        strips.Children.Add(BandStrip(0, Low, LowKill));
        strips.Children.Add(BandStrip(1, Mid, MidKill));
        strips.Children.Add(BandStrip(2, High, HighKill));
        var stripsPanel = new Border { Width = 276, Child = strips };

        // ================= graph =================
        var graph = new Border { Padding = new Thickness(0, 7, 8, 7), Child = new Border { Background = Inset, BorderBrush = FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = curve, ClipToBounds = true } };

        // ================= assemble =================
        DockPanel.SetDock(stripsPanel, Dock.Left);
        var body = new DockPanel { LastChildFill = true, Children = { stripsPanel, graph } };
        DockPanel.SetDock(strip, Dock.Top);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp, Children = { strip, body } };

        void RefreshAll() { foreach (var a in readouts) a(); }
        SyncCurve();
        ctx.AddDeviceRefresher(() => { SyncCurve(); RefreshAll(); });
        RefreshAll();
        return root;
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
}

internal static class Eq3DockExt
{
    public static Control AddDock(this DockPanel dp, Control child, Dock dock) { DockPanel.SetDock(child, dock); dp.Children.Add(child); return child; }
}
