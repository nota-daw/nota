// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — built-in Delay body (kind 3), mockup 2h on the shared shell
// (700×260): a LIVE strip (Feedback · Spread · Freeze · Taps readout · Mix) over the
// per-channel TAPS graph (flex) | TIME panel (Sync/ms · division buttons · Link · Ping ·
// WOW) | OUTPUT rail (dry/wet · out). Divisions are a row of buttons, not a dropdown.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class DelayDeviceBody : IDeviceBody
{
    private const int SyncMode = 0, TimeL = 1, TimeR = 2, DivL = 3, DivR = 4, LinkLR = 5, Feedback = 6,
                      Spread = 7, PingPong = 8, WowRate = 9, WowDepth = 10, Freeze = 11, DryWet = 12, Output = 13;
    private static readonly string[] DivNames = { "1/16", "1/8T", "1/8", "1/8.", "1/4T", "1/4", "1/4.", "1/2" };
    private static readonly double[] DivBeats = { 0.25, 1.0 / 3, 0.5, 0.75, 2.0 / 3, 1.0, 1.5, 2.0 };

    public double Width => 700;
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine; int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void SetP(int p, float v) => engine.DeviceSetParam(track, di, p, v);
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);

        var taps = new DelayTaps { VerticalAlignment = VerticalAlignment.Stretch };
        var tapsLbl = new TextBlock { FontSize = 9, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        tapsLbl.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        void SyncGraph()
        {
            bool sync = P(SyncMode) > 0.5f, link = P(LinkLR) > 0.5f;
            int dL = Sel(DivL, 8), dR = link ? dL : Sel(DivR, 8);
            double fracL, fracR; string label; string[] ruler;
            if (sync) { fracL = DivBeats[dL] / 8; fracR = DivBeats[dR] / 8; label = $"L {DivNames[dL]} · R {DivNames[dR]}"; ruler = new[] { "0", "1 bar", "2 bars" }; }
            else { double msL = P(TimeL) * 2000, msR = link ? msL : P(TimeR) * 2000; fracL = msL / 2000; fracR = msR / 2000; label = $"L {msL:0} · R {msR:0} ms"; ruler = new[] { "0", "1 s", "2 s" }; }
            taps.Set(fracL, fracR, P(Feedback), P(PingPong) > 0.5f, label, ruler);
            tapsLbl.Text = label;
        }
        ctx.AddDeviceRefresher(SyncGraph);

        string Pct(double v) => $"{v * 100:0}%";
        string SpreadF(double v) => $"{v * 50:0} ms";
        string RateF(double v) => $"{Exp(v, 0.05, 8):0.00} Hz";
        string DbF(double v) => v <= 0.001 ? "−∞" : $"{20 * Math.Log10(v * 2):+0.0;-0.0;0.0}";
        string MsF(double v) => $"{v * 2000:0} ms";

        Control Cell(string name, int p, Func<double, string> fmt, IBrush? arc = null, double size = 34)
        {
            var val = new TextBlock { Text = fmt(P(p)), FontSize = 8, Foreground = TextPrimary };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.ValueChanged += v => { SetP(p, (float)v); val.Text = fmt(v); SyncGraph(); };
            knob.GestureBegin += () => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
            knob.GestureEnd += () => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
            MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, di, p), name);
            ctx.AddDeviceRefresher(() => { if (!knob.Dragging) { float c = P(p); if (Math.Abs(c - knob.Value) > 1e-3) { knob.Value = c; val.Text = fmt(c); } } });
            return KnobCell(name, knob, val, size + 16);
        }
        Control Row(params Control[] cs) { var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center }; foreach (var c in cs) sp.Children.Add(c); return sp; }

        Control Slider(int p, string name, Func<double, string> fmt, double w)
        {
            var bg = new Border { Width = w, Height = 3, Background = Sunken, CornerRadius = new CornerRadius(2) };
            var fill = new Border { Height = 3, Background = Brass, CornerRadius = new CornerRadius(2) };
            var handle = new Border { Width = 8, Height = 9, Background = new SolidColorBrush(Color.Parse("#A39D8F")), CornerRadius = new CornerRadius(2) };
            var canvas = new Canvas { Width = w, Height = 9, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            Canvas.SetTop(bg, 3); Canvas.SetTop(fill, 3); Canvas.SetTop(handle, 0);
            canvas.Children.Add(bg); canvas.Children.Add(fill); canvas.Children.Add(handle);
            var val = new TextBlock { FontSize = 9, Foreground = TextPrimary, Width = 44, VerticalAlignment = VerticalAlignment.Center };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            bool drag = false;
            void Vis(double v) { fill.Width = Math.Max(0, v * w); Canvas.SetLeft(handle, v * w - 4); val.Text = fmt(v); }
            void From(PointerEventArgs e) { double v = Math.Clamp(e.GetPosition(canvas).X / w, 0, 1); SetP(p, (float)v); Vis(v); SyncGraph(); }
            canvas.PointerPressed += (_, e) => { drag = true; engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""); e.Pointer.Capture(canvas); From(e); };
            canvas.PointerMoved += (_, e) => { if (drag) From(e); };
            canvas.PointerReleased += (_, e) => { if (drag) { drag = false; engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""); e.Pointer.Capture(null); } };
            ctx.AddDeviceRefresher(() => { if (!drag) Vis(P(p)); });
            Vis(P(p));
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = name, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center }, canvas, val } };
        }
        Control Toggle(int p, string label)
        {
            var b = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.SemiBold } };
            void Hi() { bool on = P(p) > 0.5f; b.Background = on ? AccentSubtleB : Sunken; b.BorderBrush = on ? Brass : BorderDef; ((TextBlock)b.Child!).Foreground = on ? AccentBright : TextTertiary; }
            b.PointerPressed += (_, _) => { SetP(p, P(p) > 0.5f ? 0f : 1f); Hi(); SyncGraph(); };
            ctx.AddDeviceRefresher(Hi); Hi();
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return b;
        }
        // Division chip row over a param (8 steps).
        Control DivChips(int p)
        {
            var arr = new Border[8];
            void Hi() { int cur = Sel(p, 8); for (int i = 0; i < 8; i++) { bool on = i == cur; arr[i].Background = on ? AccentSubtleB : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? AccentBright : TextTertiary; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < 8; i++) { int iv = i; var c = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = DivNames[i], FontSize = 8, Foreground = TextTertiary } }; c.PointerPressed += (_, _) => { SetP(p, iv / 7f); Hi(); SyncGraph(); }; arr[i] = c; row.Children.Add(c); }
            ctx.AddDeviceRefresher(Hi); Hi();
            var divSeg = new Border { Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
            MidiLearn.Bind(divSeg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return divSeg;
        }
        Control Seg(int p, string[] opts)
        {
            int n = opts.Length; var arr = new Border[n];
            void Hi() { int cur = Sel(p, n); for (int i = 0; i < n; i++) { bool on = i == cur; arr[i].Background = on ? AccentSubtleB : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? AccentBright : TextTertiary; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < n; i++) { int iv = i; var c = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = opts[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextTertiary } }; c.PointerPressed += (_, _) => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); Hi(); SyncGraph(); RebuildTime(); }; arr[i] = c; row.Children.Add(c); }
            ctx.AddDeviceRefresher(Hi); Hi();
            var seg = new Border { Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }

        // TIME panel: sync/ms + link/ping, then L/R rows (chips in sync, knobs in ms).
        var timeHost = new ContentControl();
        void RebuildTime()
        {
            bool sync = P(SyncMode) > 0.5f;
            Control LR(string ch, int divP, int timeP) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = {
                new TextBlock { Text = ch, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TextSecondary, Width = 12, VerticalAlignment = VerticalAlignment.Center },
                sync ? DivChips(divP) : Cell("", timeP, MsF, null, 30) } };
            timeHost.Content = new StackPanel { Spacing = 5, Children = { LR("L", DivL, TimeL), LR("R", DivR, TimeR) } };
        }
        RebuildTime();

        Control Band(string title, Control body)
        {
            var head = new TextBlock { Text = title, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, Margin = new Thickness(0, 0, 0, 3) };
            return new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 5), Child = new StackPanel { Children = { head, body } } };
        }
        var timePanel = Band("TIME", new StackPanel { Spacing = 6, Children = {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { Seg(SyncMode, new[] { "ms", "Sync" }), Toggle(LinkLR, "Link"), Toggle(PingPong, "Ping") } },
            timeHost } });
        var wowBand = Band("WOW", Row(Cell("Rate", WowRate, RateF, Teal), Cell("Depth", WowDepth, Pct, Teal)));
        var bands = new StackPanel { Width = 288, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { timePanel, wowBand } };

        var rail = new Border { Width = 96, Background = new SolidColorBrush(Color.Parse("#1B1916")), BorderBrush = BorderDef, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(6, 8),
            Child = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = {
                Cell("Dry/Wet", DryWet, Pct, Teal, 40), Cell("Out", Output, DbF, null, 38) } } };
        DockPanel.SetDock(rail, Dock.Right);

        var live = new Border { Height = 34, Background = new SolidColorBrush(Color.Parse("#1E1C18")), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0), Children = {
                Slider(Feedback, "FEEDBACK", Pct, 64), Slider(Spread, "SPREAD", SpreadF, 52), Toggle(Freeze, "Freeze"),
                new TextBlock { Text = "TAPS", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center }, tapsLbl,
                Slider(DryWet, "MIX", Pct, 52) } } };

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Margin = new Thickness(9, 8) };
        content.Children.Add(taps); Grid.SetColumn(bands, 1); content.Children.Add(bands);
        var contentDock = new DockPanel { LastChildFill = true, Children = { rail, content } };

        DockPanel.SetDock(live, Dock.Top);
        SyncGraph();
        return new DockPanel { LastChildFill = true, Children = { live, contentDock } };
    }
}
