// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Forge (device kind 17) body, built to mockup 3m: a
// multi-stage saturator with every stage on screen. A LIVE strip (Amount / Tone / Wet +
// four drawn routing choices) over a body of three columns: a STAGES chain (each stage a
// row with an algorithm, drive, output trim and teal self-feedback that dims when off), a
// TRANSFER curve + HARMONICS bar chart in the middle, and a SHAPE (Tone / Bias / Width) +
// teal MODULATION (LFO→Drive · Env→Tone · rate/sync) rail on the right.

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

internal sealed class ForgeDeviceBody : IDeviceBody
{
    private const int Amount = 0, Tone = 1, Wet = 2, Output = 3, Bias = 4, WidthP = 5, Routing = 6,
                      LfoDrive = 7, EnvTone = 8, LfoRate = 9, LfoSync = 10, S1Type = 11, S1On = 15, OS = 26;

    private static readonly string[] RouteNames = { "Serial", "Parallel", "Mid/Side", "Multiband" };
    private static readonly string[] OsNames = { "Off", "2×", "4×", "8×" };
    private static readonly string[] DivNames = { "2/1", "1/1", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64" };

    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TealSubtle = NotaPalette.Wash(NotaPalette.Teal, 0x24);
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush LabelC = NotaPalette.TextSecondary;
    private static readonly IBrush Dim = NotaPalette.BorderStrong;
    private static readonly IBrush HandleC = NotaPalette.TextSecondary;

    public double Width => 700;

    public string? Subtitle => "SATURATION";   // the processing type, shown as the header badge
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void SetP(int p, float v) => engine.DeviceSetParam(track, di, p, v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");

        var readouts = new List<Action>();
        Control Cap(string t, IBrush? c = null, double w = 0) { var tb = new TextBlock { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? MutedC, VerticalAlignment = VerticalAlignment.Center }; if (w > 0) tb.Width = w; return tb; }

        // ---- viz ----
        var transfer = new ForgeTransferCurve(engine, track, di) { VerticalAlignment = VerticalAlignment.Stretch };
        var harm = new ForgeHarmonics(engine, track, di) { Height = 52 };
        void SyncViz() { transfer.Sync(); harm.Sync(); }
        ctx.AddDeviceRefresher(transfer.Tick);

        // ---- formatters ----
        static string AmtF(double v) => $"+{v * 30:0.0}\u2009dB";
        static string ToneF(double v) => $"{(v - 0.5) * 24:+0.0;−0.0;0.0}\u2009dB";
        static string WetF(double v) => $"{v * 100:0}\u2009%";
        static string BiasF(double v) => $"{(v - 0.5) * 200:+0;−0;0}\u2009%";
        static string WidthF(double v) => $"{v * 200:0}\u2009%";
        static string OutF(double v) => $"{(v - 0.5) * 24:+0.0;−0.0;0.0}";
        static string PctF(double v) => $"{v * 100:0}\u2009%";
        static string FbF(double v) => v < 0.005 ? "—" : $"{v * 100:0}\u2009%";
        string RateF(double v) => P(LfoSync) >= 0.5f ? DivNames[Math.Clamp((int)Math.Round(v * 7), 0, 7)] + " sync" : $"{0.05 * Math.Pow(20 / 0.05, v):0.0}\u2009Hz";

        // ---- horizontal slider (label | slot | value) ----
        Control HRow(int p, string label, Func<double, string> fmt, double labW, double valW, bool teal = false, bool bipolar = false)
        {
            var accent = teal ? TealC : Amber;
            var fill = new Border { Height = 3, Background = accent, CornerRadius = NotaRadius.Clip, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var track2 = new Border { Height = 3, Background = Inset, CornerRadius = NotaRadius.Clip, VerticalAlignment = VerticalAlignment.Center };
            var center = bipolar ? new Border { Width = 1, Background = Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 1) } : null;
            var handle = new Border { Width = 8, Height = 9, Background = HandleC, CornerRadius = NotaRadius.Clip, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var slot = new Panel { Height = 11, MinWidth = 30 }; slot.Children.Add(track2); if (center != null) slot.Children.Add(center); slot.Children.Add(fill); slot.Children.Add(handle);
            var val = new TextBlock { Text = fmt(P(p)), FontSize = 9, Foreground = TxtC, VerticalAlignment = VerticalAlignment.Center }; val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); if (valW > 0) { val.Width = valW; val.TextAlignment = TextAlignment.Right; }
            bool drag = false;
            void Upd() { double v = P(p), W = slot.Bounds.Width, hx = v * W; handle.Margin = new Thickness(Math.Clamp(hx - 4, 0, Math.Max(0, W - 8)), 0, 0, 0); if (bipolar) { double c = W * 0.5, a = Math.Min(c, hx), b = Math.Max(c, hx); fill.Margin = new Thickness(a, 0, 0, 0); fill.Width = Math.Max(0, b - a); } else fill.Width = hx; val.Text = fmt(v); }
            void SetX(double x) { SetP(p, (float)Math.Clamp(x / Math.Max(1, slot.Bounds.Width), 0, 1)); SyncViz(); Upd(); }
            slot.PointerPressed += (_, e) => { drag = true; e.Pointer.Capture(slot); Begin(p); SetX(e.GetPosition(slot).X); };
            slot.PointerMoved += (_, e) => { if (drag) SetX(e.GetPosition(slot).X); };
            slot.PointerReleased += (_, e) => { if (drag) { drag = false; e.Pointer.Capture(null); End(p); } };
            MidiLearn.Bind(slot, MidiTarget.DeviceParam(track, di, p), label);
            readouts.Add(() => { if (!drag) Upd(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Center };
            if (labW > 0) { var l = Cap(label, teal ? TealC : MutedC, labW); g.Children.Add(l); }
            Grid.SetColumn(slot, 1); g.Children.Add(slot); Grid.SetColumn(val, 2); g.Children.Add(val);
            return g;
        }

        // ---- routing icon chips ----
        Control RoutingChips()
        {
            var cells = new Border[4]; var icons = new RoutingIcon[4]; var texts = new TextBlock[4];
            void Sync() { int cur = Math.Clamp((int)Math.Round(P(Routing) * 3), 0, 3); for (int i = 0; i < 4; i++) { bool on = i == cur; cells[i].Background = on ? Amber : Brushes.Transparent; cells[i].BorderBrush = on ? Amber : Dim; icons[i].Color = on ? NotaPalette.TextOnAccent : LabelC; texts[i].Foreground = on ? NotaPalette.TextOnAccent : LabelC; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < 4; i++) { int iv = i; var ic = new RoutingIcon(i) { Width = 18, Height = 13, VerticalAlignment = VerticalAlignment.Center }; var tb = new TextBlock { Text = RouteNames[i], FontSize = 9, Foreground = LabelC, VerticalAlignment = VerticalAlignment.Center }; var c = new Border { Height = 22, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(5, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { ic, tb } } }; c.PointerPressed += (_, e) => { e.Handled = true; SetP(Routing, iv / 3f); SyncViz(); Sync(); }; cells[i] = c; icons[i] = ic; texts[i] = tb; row.Children.Add(c); }
            readouts.Add(Sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, Routing), engine.DeviceParamName(track, di, Routing));
            return row;
        }

        // ---- oversampling quality selector (Off / 2× / 4× / 8×) ----
        Control OsChips()
        {
            var seg = DeviceCardKit.Segments(OsNames, () => Math.Clamp((int)Math.Round(P(OS) * 3), 0, 3), i => { Begin(OS); SetP(OS, i / 3f); End(OS); }, out var sync, minSegWidth: 30);
            readouts.Add(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, OS), engine.DeviceParamName(track, di, OS));
            return seg;
        }

        // ---- one stage row ----
        Control StageRow(int s)
        {
            int baseP = S1Type + s * 5, typeP = baseP, driveP = baseP + 1, outP = baseP + 2, fbP = baseP + 3, onP = baseP + 4;
            var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Badge, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
            var idx = new TextBlock { Text = (s + 1).ToString(), FontSize = 9, Foreground = MutedC, VerticalAlignment = VerticalAlignment.Center }; idx.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var name = new TextBlock { FontSize = 9, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
            var outVal = new TextBlock { FontSize = 9, Foreground = LabelC, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Width = 34 }; outVal.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var fbVal = new TextBlock { FontSize = 8, Foreground = LabelC, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Width = 26 }; fbVal.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");

            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { dot, idx, name } };
            var headRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            headRow.Children.Add(head); Grid.SetColumn(outVal, 1); headRow.Children.Add(outVal);

            var line2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = {
                Cap("DRIVE", MutedC, 30),
                new Border { Child = HRow(driveP, "", PctF, 0, 0), Width = 58, VerticalAlignment = VerticalAlignment.Center },
                Cap("FEEDBACK", TealC, 42),
                new Border { Child = HRow(fbP, "", FbF, 0, 0, teal: true), Width = 32, VerticalAlignment = VerticalAlignment.Center },
                fbVal } };

            var border = new Border { BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(7, 4), Background = RailBg, BorderBrush = Border2,
                Child = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { headRow, line2 } } };

            // out trim: drag on the out value (vertical) — quick, keeps line1 compact.
            bool od = false; double oy = 0;
            outVal.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
            outVal.PointerPressed += (_, e) => { od = true; oy = e.GetPosition(outVal).Y; Begin(outP); e.Pointer.Capture(outVal); e.Handled = true; };
            outVal.PointerMoved += (_, e) => { if (od) { double dy = oy - e.GetPosition(outVal).Y; oy = e.GetPosition(outVal).Y; SetP(outP, (float)Math.Clamp(P(outP) + dy * 0.01, 0, 1)); SyncViz(); outVal.Text = OutF(P(outP)) + "\u2009dB"; } };
            outVal.PointerReleased += (_, e) => { if (od) { od = false; End(outP); e.Pointer.Capture(null); } };

            dot.PointerPressed += (_, e) => { e.Handled = true; Begin(onP); SetP(onP, P(onP) >= 0.5f ? 0f : 1f); End(onP); SyncViz(); };
            name.PointerPressed += (_, e) => { e.Handled = true; int t = (int)Math.Round(P(typeP) * (ForgeMath.Algos - 1)); t = (t + 1) % ForgeMath.Algos; SetP(typeP, t / (float)(ForgeMath.Algos - 1)); SyncViz(); };

            readouts.Add(() =>
            {
                bool on = P(onP) >= 0.5f;
                dot.Background = on ? Amber : Dim;
                Inactive.Set(border, !on, interactive: true);
                int t = Math.Clamp((int)Math.Round(P(typeP) * (ForgeMath.Algos - 1)), 0, ForgeMath.Algos - 1);
                name.Text = ForgeMath.AlgoNames[t]; name.Foreground = on ? TxtC : MutedC;
                outVal.Text = OutF(P(outP)) + "\u2009dB";
                fbVal.Text = FbF(P(fbP));
            });
            // Stage algorithm (click the name to cycle) and on/off dot are discrete params too.
            MidiLearn.Bind(name, MidiTarget.DeviceParam(track, di, typeP), engine.DeviceParamName(track, di, typeP));
            MidiLearn.Bind(dot, MidiTarget.DeviceParam(track, di, onP), engine.DeviceParamName(track, di, onP));
            return border;
        }

        Control MiniToggle(int p, string label)
        {
            var host = Switch(label, () => P(p) >= 0.5f, () => SetP(p, P(p) >= 0.5f ? 0f : 1f), out var sync);
            readouts.Add(sync);
            MidiLearn.Bind(host, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return host;
        }

        // ================= LIVE strip =================
        var live = new Border { Height = 34, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), Children = {
                WithDock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = {
                    Cap("LIVE"),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Width = 150, Children = { Cap("AMOUNT", MutedC, 46), new Border { Child = HRow(Amount, "", AmtF, 0, 52), Width = 100 } } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Width = 118, Children = { Cap("TONE", MutedC, 30), new Border { Child = HRow(Tone, "", ToneF, 0, 46, bipolar: true), Width = 76 } } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Width = 96, Children = { Cap("WET", MutedC, 24), new Border { Child = HRow(Wet, "", WetF, 0, 36), Width = 62 } } } } }, Dock.Left),
                WithDock(RoutingChips(), Dock.Right) } } };

        // ================= STAGES column =================
        var stagesHdr = new Grid { Height = 10, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        stagesHdr.Children.Add(Cap("STAGES"));
        var sub = new TextBlock { Text = "drive · out · feedback", FontSize = 8, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(sub, 1); stagesHdr.Children.Add(sub);
        var stages = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 7) };
        DockPanel.SetDock(stagesHdr, Dock.Top); stages.Children.Add(stagesHdr);
        var stageStack = new Grid { RowDefinitions = new RowDefinitions("*,4,*,4,*"), Margin = new Thickness(0, 4, 0, 0) };
        for (int s = 0; s < 3; s++) { var row = StageRow(s); Grid.SetRow(row, s * 2); stageStack.Children.Add(row); }
        stages.Children.Add(stageStack);
        var stagesPanel = new Border { Width = 288, Child = stages };

        // ================= middle: transfer + harmonics =================
        var mid = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 7, 0, 7) };
        mid.Children.Add(new Border { Height = 52, Child = harm, Margin = new Thickness(0, 4, 0, 0), [DockPanel.DockProperty] = Dock.Bottom });
        mid.Children.Add(new Border { Child = transfer });

        // ================= right rail: SHAPE + MODULATION =================
        var shape = new StackPanel { Spacing = 4, Children = {
            Cap("SHAPE", MutedC),
            HRow(Tone, "TONE", ToneF, 34, 44, bipolar: true),
            HRow(Bias, "BIAS", BiasF, 34, 44, bipolar: true),
            HRow(WidthP, "WIDTH", WidthF, 34, 44) } };
        var mod = new Border { BorderBrush = TealC, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(6, 0, 0, 0), Margin = new Thickness(0, 2, 0, 0),
            Child = new StackPanel { Spacing = 4, Children = {
                Cap("MODULATION", TealC),
                HRow(LfoDrive, "LFO → DRIVE", PctF, 60, 32, teal: true),
                HRow(EnvTone, "ENV → TONE", PctF, 60, 32, teal: true),
                HRow(LfoRate, "RATE", RateF, 34, 52, teal: true),
                MiniToggle(LfoSync, "SYNC") } } };
        var quality = new StackPanel { Spacing = 4, Children = {
            Cap("QUALITY", MutedC),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("OVERSAMPLE", MutedC), OsChips() } } } };
        var divider1 = new Border { Height = 1, Background = NotaPalette.SurfaceRaised };
        var divider2 = new Border { Height = 1, Background = NotaPalette.SurfaceRaised };
        var rail = new Border { Width = 176, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 7),
            Child = new StackPanel { Spacing = 5, Children = { shape, divider1, mod, divider2, quality } } };

        // ================= assemble =================
        DockPanel.SetDock(stagesPanel, Dock.Left); DockPanel.SetDock(rail, Dock.Right);
        var body = new DockPanel { LastChildFill = true, Children = { stagesPanel, rail, mid } };
        DockPanel.SetDock(live, Dock.Top);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp, Children = { live, body } };

        void RefreshAll() { foreach (var a in readouts) a(); }
        SyncViz();
        ctx.AddDeviceRefresher(() => { RefreshAll(); harm.Sync(); });
        RefreshAll();
        return root;
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
}

// Four routing-topology icons (mockup 3m): two-line diagrams picked by picture.
internal sealed class RoutingIcon : Control
{
    private readonly int _kind;
    private IBrush _color = NotaPalette.TextTertiary;
    public IBrush Color { get => _color; set { _color = value; InvalidateVisual(); } }
    public RoutingIcon(int kind) { _kind = kind; }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var pen = new Pen(_color, 1.4) { LineCap = PenLineCap.Round };
        double sx = w / 21.0, sy = h / 16.0;
        void L(double x1, double y1, double x2, double y2) => ctx.DrawLine(pen, new Point(x1 * sx, y1 * sy), new Point(x2 * sx, y2 * sy));
        switch (_kind)
        {
            case 0: L(2, 8, 8, 8); L(13, 8, 19, 8); break;                       // Serial
            case 1: L(2, 4, 19, 4); L(2, 12, 19, 12); break;                     // Parallel
            case 2: L(2, 4, 19, 4); L(2, 12, 10, 12); break;                     // Mid/Side
            default: L(2, 3, 19, 3); L(2, 8, 19, 8); L(2, 13, 19, 13); break;    // Multiband
        }
    }
}
