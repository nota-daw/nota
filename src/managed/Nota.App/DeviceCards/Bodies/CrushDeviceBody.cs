// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Crush (device kind 12) body, built to mockup 3l:
// a 700×260 standard shell with a LIVE strip (Bits · Rate · WET sliders + Anti-Alias
// toggle), a quantiser waveform display + spectrum, a MODE selector (Digital/Analog/
// Fold), a GRIT panel (Dither/Jitter/Noise Floor, teal spine), and an OUTPUT panel
// (Post Filter / Dry/Wet). All controls are the device's generic params → automation
// + persist for free.

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

internal sealed class CrushDeviceBody : IDeviceBody
{
    // Param indices — must match Crush.h.
    private const int Bits = 0, Rate = 1, Mode = 2, Dither = 3, Jitter = 4, NoiseFloor = 5,
                      PostFilter = 6, DryWet = 7, AntiAlias = 8, Output = 9, Drive = 10;

    private static readonly string[] Modes = { "Digital", "Analog", "Fold" };
    private static readonly string[] ModeSubs = { "hard truncate", "soft clip", "wavefold" };
    // Defaults mirror Crush.h constructor (index order): Bits, Rate, Mode, Dither, Jitter,
    // NoiseFloor, PostFilter, DryWet, AntiAlias, Output, Drive.
    private static readonly float[] Defaults = { 0.55f, 0.35f, 0f, 0.15f, 0f, 0f, 0.85f, 1f, 0f, 0.5f, 0.333f };

    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush FieldBorder = NotaPalette.GraphBorder;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);
    private static readonly IBrush TealSubtle = NotaPalette.Wash(NotaPalette.Teal, 0x24);
    private static readonly IBrush RowLit = NotaPalette.SurfaceRaised;
    internal static readonly IBrush GridLine = NotaPalette.SurfaceCard;
    internal static readonly IBrush GridCenter = NotaPalette.BorderDefault;
    internal static readonly IBrush SrcLine = NotaPalette.TextDisabled;
    internal static readonly IBrush NyqLine = NotaPalette.Wash(NotaPalette.AccentBright, 0x73);

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

        // ---- formatters ----
        string BitsF(double v) => $"{1 + v * 23:0.0} bit";
        string RateF(double v) { double hz = Exp(v, 500, 44100 * 0.48); return hz >= 1000 ? $"{hz / 1000:0.0} kHz" : $"{(int)Math.Round(hz)} Hz"; }
        static string PctF(double v) => $"{v * 100:0}%";
        static string DriveF(double v) { double db = -12 + v * 36; return $"{(db >= 0 ? "+" : "")}{db:0.0} dB"; }
        static string GainF(double v) { double db = (v - 0.5) * 24; return $"{(db >= 0 ? "+" : "")}{db:0.0} dB"; }

        // ---- quantiser viz ----
        var quantViz = new CrushQuantiserViz { VerticalAlignment = VerticalAlignment.Stretch };
        void SyncQuant() => quantViz.Set(P(Bits), P(Rate), P(Mode), P(Dither), P(Jitter), P(NoiseFloor), P(Drive));
        ctx.AddDeviceRefresher(quantViz.Tick);

        // ---- spectrum viz ----
        var specViz = new CrushSpectrumViz { VerticalAlignment = VerticalAlignment.Stretch };
        void SyncSpec() => specViz.Set(P(Bits), P(Rate), P(AntiAlias));
        ctx.AddDeviceRefresher(specViz.Tick);

        static TextBlock Cap(string t, IBrush? c = null) => new() { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? MutedC, VerticalAlignment = VerticalAlignment.Center };

        // ---- horizontal param slider (LIVE strip + rails) ----
        Control HSlider(int p, string label, Func<double, string> fmt, double lw, double vw, bool bipolar = false)
        {
            var fill = new Border { Height = 3, Background = Amber, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var track2 = new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            var center = bipolar ? new Border { Width = 1, Background = NotaPalette.BorderStrong, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 1) } : null;
            var handle = new Border { Width = 8, Height = 10, Background = NotaPalette.TextSecondary, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 11, MinWidth = 40 }; slot.Children.Add(track2); if (center != null) slot.Children.Add(center); slot.Children.Add(fill); slot.Children.Add(handle);
            var val = new TextBlock { Text = fmt(P(p)), FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center }; val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); if (vw > 0) { val.Width = vw; val.TextAlignment = TextAlignment.Right; }
            bool drag = false;
            void Upd() { double v = P(p); double W = slot.Bounds.Width; double hx = v * W; handle.Margin = new Thickness(Math.Clamp(hx - 4, 0, Math.Max(0, W - 8)), 0, 0, 0); if (bipolar) { double c = W * 0.5; double a = Math.Min(c, hx), b = Math.Max(c, hx); fill.Margin = new Thickness(a, 0, 0, 0); fill.Width = Math.Max(0, b - a); } else fill.Width = hx; val.Text = fmt(v); }
            void SetFromX(double x) { double v = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); SetP(p, (float)v); SyncQuant(); SyncSpec(); Upd(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); Begin(p); SetFromX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetFromX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); End(p); } };
            MidiLearn.Bind(slot, MidiTarget.DeviceParam(track, di, p), label);
            readouts.Add(() => { if (!drag) Upd(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            if (lw > 0) { var lbl = Cap(label); ((TextBlock)lbl).Width = lw; g.Children.Add(lbl); }
            Grid.SetColumn(slot, 1); g.Children.Add(slot); Grid.SetColumn(val, 2); g.Children.Add(val);
            return g;
        }

        // ---- rail slider (teal for GRIT) ----
        Control RailSlider(int p, string label, Func<double, string> fmt, IBrush? color = null)
        {
            var c = color ?? Amber;
            var fill = new Border { Height = 3, Background = c, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var track2 = new Border { Height = 3, Background = Inset, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            var handle = new Border { Width = 8, Height = 9, Background = NotaPalette.TextSecondary, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 10, MinWidth = 40, Children = { track2, fill, handle } };
            var val = new TextBlock { Text = fmt(P(p)), FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center, Width = 48, TextAlignment = TextAlignment.Right };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            bool drag = false;
            void Upd() { double v = P(p); double W = slot.Bounds.Width; double hx = v * W; handle.Margin = new Thickness(Math.Clamp(hx - 4, 0, Math.Max(0, W - 8)), 0, 0, 0); fill.Width = hx; val.Text = fmt(v); }
            void SetFromX(double x) { double v = Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1); SetP(p, (float)v); SyncQuant(); SyncSpec(); Upd(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); Begin(p); SetFromX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetFromX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); End(p); } };
            MidiLearn.Bind(slot, MidiTarget.DeviceParam(track, di, p), label);
            readouts.Add(() => { if (!drag) Upd(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, Width = 38, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(slot, 1); g.Children.Add(slot); Grid.SetColumn(val, 2); g.Children.Add(val);
            return g;
        }

        // ---- mode selector chips ----
        Control ModeSelector()
        {
            var chips = new Border[Modes.Length]; var texts = new TextBlock[Modes.Length]; var subs = new TextBlock[Modes.Length];
            void Sync() { int cur = (int)Math.Round(P(Mode) * 2); for (int i = 0; i < Modes.Length; i++) { bool on = i == cur; chips[i].Background = on ? AmberSubtle : Inset; chips[i].BorderBrush = on ? Amber : Border2; texts[i].Foreground = on ? AmberLit : MutedC; subs[i].Foreground = on ? AmberLit : MutedC; } }
            var col = new StackPanel { Spacing = 2 };
            for (int i = 0; i < Modes.Length; i++)
            {
                int iv = i;
                var tb = new TextBlock { Text = Modes[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = MutedC };
                var sub = new TextBlock { Text = ModeSubs[i], FontSize = 8, Foreground = MutedC };
                var c = new Border { Height = 15, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { tb, new TextBlock { Text = ModeSubs[i], FontSize = 8, Foreground = MutedC, [DockPanel.DockProperty] = Dock.Right } } } };
                c.PointerPressed += (_, e) => { e.Handled = true; SetP(Mode, iv / 2f); SyncQuant(); SyncSpec(); RefreshAll(); };
                chips[i] = c; texts[i] = tb; subs[i] = sub; col.Children.Add(c);
            }
            readouts.Add(Sync);
            MidiLearn.Bind(col, MidiTarget.DeviceParam(track, di, Mode), engine.DeviceParamName(track, di, Mode));
            return col;
        }

        // ---- anti-alias toggle ----
        Control AaToggle()
        {
            var sw = new ToggleSwitch(P(AntiAlias) >= 0.5f);
            sw.Changed += on => { SetP(AntiAlias, on ? 1f : 0f); SyncSpec(); };
            readouts.Add(() => { bool on = P(AntiAlias) >= 0.5f; if (sw.IsOn != on) sw.IsOn = on; });
            var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { sw, new TextBlock { Text = "ANTI-ALIAS", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TealC, VerticalAlignment = VerticalAlignment.Center } } };
            MidiLearn.Bind(host, MidiTarget.DeviceParam(track, di, AntiAlias), engine.DeviceParamName(track, di, AntiAlias));
            return host;
        }

        // ---- rail button (Init / Bypass) ----
        Control RailBtn(string label, Action onClick)
        {
            var b = new Border { Background = RowLit, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(0, 2), HorizontalAlignment = HorizontalAlignment.Stretch, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = label, FontSize = 9, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Center } };
            b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
            return b;
        }

        // ================= LIVE strip =================
        var live = new Border { Height = 34, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), Children = {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Width = 150, Children = { Cap("BITS"), HSlider(Bits, "", BitsF, 0, 50) } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Width = 156, Children = { Cap("RATE"), HSlider(Rate, "", RateF, 0, 54) } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Width = 150, Children = { Cap("DRIVE"), HSlider(Drive, "", DriveF, 0, 52) } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Width = 98, Children = { Cap("WET"), HSlider(DryWet, "", PctF, 0, 32) } } } },
                new StackPanel { [DockPanel.DockProperty] = Dock.Right, VerticalAlignment = VerticalAlignment.Center, Children = { AaToggle() } } } } };

        // ================= quantiser panel =================
        var Dim = NotaPalette.TextDisabled;
        static TextBlock Tiny(string t, IBrush c) => new() { Text = t, FontSize = 8, Foreground = c, VerticalAlignment = VerticalAlignment.Center };
        TextBlock TinyMono(string t, IBrush c) { var b = Tiny(t, c); b.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return b; }

        var quantSpec = TinyMono("", Amber);   // "6.0-bit @ 11.0 kHz"
        var quantHdr = new DockPanel { LastChildFill = false, Height = 11, Children = {
            WithDock(new TextBlock { Text = "QUANTISER", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center }, Dock.Left),
            WithDock(quantSpec, Dock.Right),
            WithDock(new TextBlock { Text = "┄ source ", FontSize = 8, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,0) }, Dock.Right) } };
        var quantLevels = TinyMono("", Dim);   // "64 levels"
        var quantHold = TinyMono("", Dim);     // "hold 3 smp"
        var quantFooter = new DockPanel { LastChildFill = false, Height = 10, Children = {
            WithDock(quantLevels, Dock.Left), WithDock(quantHold, Dock.Right) } };
        readouts.Add(() => {
            double bits = 1 + P(Bits) * 23; double lv = Math.Pow(2, bits);
            string lvS = lv >= 1e6 ? $"{lv / 1e6:0.0} M" : lv >= 1e3 ? $"{lv / 1e3:0.0} k" : $"{(long)Math.Round(lv)}";
            double hz = Exp(P(Rate), 500, 44100 * 0.48); int hold = Math.Max(1, (int)Math.Round(44100.0 / hz));
            quantSpec.Text = $"▮ {bits:0.0}-bit @ {(hz >= 1000 ? $"{hz / 1000:0.0} kHz" : $"{(int)hz} Hz")}";
            quantLevels.Text = $"{lvS} levels"; quantHold.Text = $"hold {hold} smp";
        });
        var quantPanel = new Border { Padding = new Thickness(8, 4), Child = new Border { Background = Inset, BorderBrush = FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4), Child = new DockPanel { LastChildFill = true, Children = { WithDock(quantHdr, Dock.Top), WithDock(quantFooter, Dock.Bottom), quantViz } } } };

        // ================= spectrum panel =================
        var specHdr = new DockPanel { LastChildFill = false, Height = 10, Children = {
            WithDock(new TextBlock { Text = "SPECTRUM", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center }, Dock.Left),
            WithDock(Tiny("▮ aliased images", TealC), Dock.Right),
            WithDock(new TextBlock { Text = "▮ signal ", FontSize = 8, Foreground = Amber, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,0) }, Dock.Right) } };
        var specPanel = new Border { Height = 66, Padding = new Thickness(8, 0, 8, 4), Child = new Border { Background = Inset, BorderBrush = FieldBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4), Child = new DockPanel { LastChildFill = true, Children = { WithDock(specHdr, Dock.Top), specViz } } } };

        // ================= left column (quantiser fills, spectrum fixed at bottom) =================
        var leftCol = new DockPanel { LastChildFill = true, Children = { WithDock(specPanel, Dock.Bottom), quantPanel } };

        // ================= right rail (mode + grit + output) =================
        var modeSection = new StackPanel { Spacing = 2, Children = {
            new TextBlock { Text = "MODE", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC },
            ModeSelector() } };

        var gritSection = new StackPanel { Spacing = 3, Children = {
            new TextBlock { Text = "GRIT", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TealC },
            RailSlider(Dither, "DITHER", PctF, TealC),
            RailSlider(Jitter, "JITTER", PctF, TealC),
            RailSlider(NoiseFloor, "NOISE", PctF, TealC) } };

        var outSection = new StackPanel { Spacing = 3, Children = {
            new TextBlock { Text = "OUTPUT", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = MutedC },
            RailSlider(PostFilter, "FILTER", RateF),
            RailSlider(Output, "GAIN", GainF) } };

        var rightRail = new Border { Width = 212, Background = RailBg, Padding = new Thickness(8, 7), Child =
            new DockPanel { LastChildFill = false, Children = {
                WithDock(new StackPanel { Spacing = 4, Children = { modeSection, new Border { Height = 1, Background = RowLit }, new Border { BorderBrush = TealC, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(6, 0, 0, 0), Child = new StackPanel { Spacing = 3, Children = { gritSection, new Border { Height = 1, Background = RowLit }, outSection } } } } }, Dock.Top),
                WithDock(new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 4, Margin = new Thickness(0, 6, 0, 0), Children = {
                    RailBtn("Init", () => { for (int p = 0; p < Defaults.Length; p++) { Begin(p); SetP(p, Defaults[p]); End(p); } SyncQuant(); SyncSpec(); RefreshAll(); }),
                    WithCol(RailBtn("Bypass", () => { engine.SetDeviceBypassed(track, di, !engine.DeviceBypassed(track, di)); ctx.RequestRebuild(); }), 1) } }, Dock.Bottom) } } };

        // ================= assemble =================
        DockPanel.SetDock(rightRail, Dock.Right);
        var body = new DockPanel { LastChildFill = true, Children = { rightRail, leftCol } };
        DockPanel.SetDock(live, Dock.Top);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp, Children = { live, body } };

        void RefreshAll() { foreach (var a in readouts) a(); }
        SyncQuant(); SyncSpec();
        ctx.AddDeviceRefresher(() => { SyncQuant(); SyncSpec(); RefreshAll(); });
        RefreshAll();
        return root;
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
    private static Control WithCol(Control c, int col) { Grid.SetColumn(c, col); return c; }
}

// ---- custom-drawn quantiser waveform display ----
internal sealed class CrushQuantiserViz : Control
{
    private float _bits = 0.55f, _rate = 0.35f, _mode = 0f, _dither = 0.15f, _jitter = 0f, _noiseFloor = 0f, _drive = 0.333f;
    private double _phase;
    private readonly Random _rng = new();

    public void Set(float bits, float rate, float mode, float dither, float jitter, float noiseFloor, float drive)
    {
        _bits = bits; _rate = rate; _mode = mode; _dither = dither; _jitter = jitter; _noiseFloor = noiseFloor; _drive = drive;
    }

    public void Tick() { _phase += 0.02; if (_phase > 1.0) _phase -= 1.0; InvalidateVisual(); }

    private static double FoldViz(double x, double threshold)
    {
        double limit = threshold * 4.0;
        x = Math.Clamp(x, -limit, limit);
        int iter = 0;
        while (Math.Abs(x) > threshold && iter < 8) { if (x > threshold) x = 2 * threshold - x; else if (x < -threshold) x = -2 * threshold - x; iter++; }
        return x / threshold;
    }

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        // Level grid
        float bits = 1f + _bits * 23f;
        int levels = (int)Math.Pow(2, bits);
        int shown = Math.Min(levels, 32);
        var gridPen = new Pen(CrushDeviceBody.GridLine, 1);
        var centerPen = new Pen(CrushDeviceBody.GridCenter, 1);
        for (int i = 0; i <= shown; i++)
        {
            double y = h * (1.0 - i / (double)shown);
            dc.DrawLine(i == shown / 2 ? centerPen : gridPen, new Point(0, y), new Point(w, y));
        }

        // Source waveform (thin grey line) + crushed output (brass steps)
        var srcPen = new Pen(CrushDeviceBody.SrcLine, 1);
        var crushPen = new Pen(NotaPalette.Accent, 1.5);
        var crushFill = NotaPalette.Wash(NotaPalette.Accent, 0x4D);
        double scale = levels - 1.0;
        double invScale = 1.0 / scale;
        int holdMax = Math.Max(1, (int)(44100.0 / (500.0 * Math.Pow(44100.0 * 0.48 / 500.0, _rate))));
        int cnt = 0;
        double held = 0;
        var srcPts = new List<Point>();
        var crushPts = new List<Point>();

        double driveGain = Math.Pow(10, (-12 + _drive * 36) / 20.0);
        for (int x = 0; x < (int)w; x++)
        {
            double ph = (_phase + x / w) % 1.0;
            double src = Math.Sin(ph * Math.PI * 2) * 0.8;
            srcPts.Add(new Point(x, h * (0.5 - src * 0.45)));

            if (--cnt <= 0) { cnt = holdMax; held = src * driveGain; }
            double q = held;
            if (_dither > 0) { double d = (_rng.NextDouble() - _rng.NextDouble()) * _dither * 0.5 * invScale; q += d; }
            // mode shaping (mirror the DSP): 0 Digital, 1 Analog soft-clip, 2 Fold
            int mode = (int)Math.Round(_mode * 2);
            if (mode == 1) q = Math.Tanh(q * 1.5) / 0.905;
            else if (mode == 2) q = FoldViz(q * 2.5, 0.7);
            q = Math.Clamp(Math.Round(q * scale) * invScale, -1.05, 1.05);
            crushPts.Add(new Point(x, h * (0.5 - q * 0.45)));
        }

        // Draw source as polyline
        if (srcPts.Count > 1)
        {
            var srcGeo = new StreamGeometry();
            using (var ctx = srcGeo.Open()) { ctx.BeginFigure(srcPts[0], false); for (int i = 1; i < srcPts.Count; i++) ctx.LineTo(srcPts[i]); ctx.EndFigure(false); }
            dc.DrawGeometry(null, srcPen, srcGeo);
        }

        // Draw crushed as filled steps
        if (crushPts.Count > 1)
        {
            var crushGeo = new StreamGeometry();
            using (var ctx = crushGeo.Open())
            {
                ctx.BeginFigure(new Point(crushPts[0].X, h * 0.5), true);
                for (int i = 0; i < crushPts.Count; i++) ctx.LineTo(crushPts[i]);
                ctx.LineTo(new Point(crushPts[^1].X, h * 0.5));
                ctx.EndFigure(true);
            }
            dc.DrawGeometry(crushFill, crushPen, crushGeo);
        }
    }
}

// ---- custom-drawn spectrum display ----
internal sealed class CrushSpectrumViz : Control
{
    private float _bits = 0.55f, _rate = 0.35f, _antiAlias = 0f;
    private double _phase;
    private readonly Random _rng = new();

    public void Set(float bits, float rate, float antiAlias) { _bits = bits; _rate = rate; _antiAlias = antiAlias; }

    public void Tick() { _phase += 0.03; if (_phase > 1.0) _phase -= 1.0; InvalidateVisual(); }

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        double targetHz = 500.0 * Math.Pow(44100.0 * 0.48 / 500.0, _rate);
        double nyqNorm = targetHz / (44100.0 * 0.48);
        double nyqX = w * nyqNorm;

        // Nyquist line
        var nyqPen = new Pen(CrushDeviceBody.NyqLine, 1);
        dc.DrawLine(nyqPen, new Point(nyqX, 0), new Point(nyqX, h));

        // Spectrum bars: signal (brass) + aliased images (teal)
        var sigBrush = NotaPalette.Accent;
        var aliBrush = NotaPalette.Teal;
        int bars = 26;
        double barW = w / bars;
        for (int i = 0; i < bars; i++)
        {
            double freqNorm = i / (double)(bars - 1);
            double barH;
            bool isAlias = freqNorm > nyqNorm * 0.95;

            if (isAlias)
            {
                // Aliased images: mirror around nyquist
                double mirror = nyqNorm - (freqNorm - nyqNorm);
                mirror = Math.Max(0, mirror);
                barH = h * 0.15 * (1.0 - mirror) * (1.0 - _antiAlias * 0.8) * (0.5 + 0.5 * Math.Sin(_phase * 3 + i * 0.5));
            }
            else
            {
                barH = h * 0.6 * (1.0 - freqNorm) * (0.6 + 0.4 * Math.Sin(_phase * 2 + i * 0.3));
            }

            barH = Math.Max(1, barH);
            var brush = isAlias ? aliBrush : sigBrush;
            double opacity = isAlias ? 0.7 * (1.0 - _antiAlias) : 0.85;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), ((SolidColorBrush)brush).Color.R, ((SolidColorBrush)brush).Color.G, ((SolidColorBrush)brush).Color.B)), null, new Rect(i * barW + 1, h - barH, Math.Max(1, barW - 2), barH));
        }
    }
}
