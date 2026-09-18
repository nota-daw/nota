// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Shutter (device kind 19) body, built to the Shutter
// mockup: a noise gate. A LIVE strip (Threshold · Return · Flip · Lookahead) over a body of
// three columns — an ENVELOPE column (open/shut state + A/H/R glyph + Attack/Hold/Release/
// Floor), the SIGNAL graph in the middle (input level + gate-gain curve + threshold/return
// lines), and a METERS + DETECTOR rail on the right (in/GR meters, open LED, and an external
// sidechain key through its own band-pass with a Listen monitor).

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

internal sealed class ShutterDeviceBody : IDeviceBody
{
    private const int Threshold = 0, Return = 1, Attack = 2, Hold = 3, Release = 4, Floor = 5,
                      Lookahead = 6, Flip = 7, DetHP = 8, DetLP = 9, Listen = 10;
    private const int S_InDb = 0, S_GateGain = 1, S_GrDb = 2, S_DetDb = 3, S_Open = 4, kScope = 5;

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
    private static readonly IBrush Green = NotaPalette.Success;
    // Gate reduction and the shut state are the device working, not an alert: red belongs to
    // recording and overload, so shut reads as quiet ink and reduction as brass data.
    private static readonly IBrush Red = NotaPalette.Accent;
    private static readonly IBrush ThrC = NotaPalette.Threshold;
    private static readonly IBrush HandleC = NotaPalette.TextSecondary;

    public double Width => 700;

    public string? Subtitle => "GATE";   // the processing type, shown as the header badge
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
        var scope = new float[kScope];
        Control Cap(string t, IBrush? c = null, double w = 0) { var tb = new TextBlock { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? MutedC, VerticalAlignment = VerticalAlignment.Center }; if (w > 0) tb.Width = w; return tb; }

        double ThrDb() => -70 + P(Threshold) * 70;
        double RetDb() => ThrDb() - P(Return) * 24;

        var signal = new ShutterSignal(engine, track, di) { VerticalAlignment = VerticalAlignment.Stretch };
        void SyncLines() => signal.SetLines(ThrDb(), RetDb());
        ctx.AddDeviceRefresher(signal.Tick);

        // ---- formatters ----
        static string ThrF(double v) => $"{-70 + v * 70:0.0}\u2009dB";
        static string RetF(double v) => $"{v * 24:0.0}\u2009dB";
        static string Ms(double v, double lo, double hi) { double m = Exp(v, lo, hi); return m >= 100 ? $"{m:0}\u2009ms" : m >= 10 ? $"{m:0.0}\u2009ms" : $"{m:0.00}\u2009ms"; }
        static string AttF(double v) => Ms(v, 0.01, 100);
        static string HoldF(double v) => Ms(v, 0.1, 500);
        static string RelF(double v) => Ms(v, 1, 2000);
        static string FloorF(double v) => v <= 0.001 ? "−∞\u2009dB" : $"{-70 + v * 70:0.0}\u2009dB";

        // ---- horizontal slider ----
        Control HRow(int p, string label, Func<double, string> fmt, double labW, double valW, bool teal = false, bool bipolar = false)
        {
            var row = DeviceCardKit.SliderRow(labW > 0 ? label : "", () => P(p), n => { SetP(p, (float)n); SyncLines(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), bipolar: bipolar, labelWidth: labW, valueWidth: valW);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), label);
            readouts.Add(sync);
            return row;
        }

        // Segmented pill over a normalized param.
        Control Seg(int p, string[] opts, bool teal = false)
        {
            int n = opts.Length;
            var seg = DeviceCardKit.Segments(opts, () => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1), iv => SetP(p, n > 1 ? iv / (float)(n - 1) : 0f), out var sync);
            readouts.Add(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }

        // ---- LIVE strip ----
        var live = new Border { Height = 34, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), Children = {
                WithDock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = {
                    Cap("LIVE"),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Width = 210, Children = { Cap("THRESH", MutedC, 56), new Border { Child = HRow(Threshold, "", ThrF, 0, 58), Width = 140 } } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Width = 130, Children = { Cap("RETURN", MutedC, 38), new Border { Child = HRow(Return, "", RetF, 0, 48), Width = 80 } } } } }, Dock.Left),
                WithDock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("FLIP"), Seg(Flip, new[] { "Gate", "Duck" }) } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("LOOK"), Seg(Lookahead, new[] { "0", "1", "5\u2009ms" }) } } } }, Dock.Right) } } };

        // ================= ENVELOPE column =================
        var state = new TextBlock { Text = "OPEN", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Green, LineHeight = 20 }.WithMono();
        var stateSub = new TextBlock { Text = "gate · GR 0.0\u2009dB", FontSize = 9, Foreground = MutedC }.WithMono();
        var envTop = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = {
            new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Children = { state, stateSub } },
            WithCol(new Border { Width = 64, Height = 38, Child = new AHRGlyph(), VerticalAlignment = VerticalAlignment.Center }, 1) } };

        var env = new StackPanel { Spacing = 5, Margin = new Thickness(9, 7) };
        env.Children.Add(new Grid { Height = 10, ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { Cap("ENVELOPE"), WithCol(new TextBlock { Text = "attack · hold · release", FontSize = 8, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Right }, 1) } });
        env.Children.Add(envTop);
        env.Children.Add(new Border { Height = 1, Background = NotaPalette.SurfaceRaised, Margin = new Thickness(0, 1) });
        env.Children.Add(new Grid { Height = 12, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, Children = { Cap("ATTACK", MutedC, 52), WithCol(HRow(Attack, "", AttF, 0, 0), 1), WithCol(new TextBlock { Text = AttF(P(Attack)), FontSize = 9, Foreground = TxtC, Width = 46, TextAlignment = TextAlignment.Right }.WithMono().Track(readouts, t => t.Text = AttF(P(Attack))), 2) } });
        env.Children.Add(new Grid { Height = 12, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, Children = { Cap("HOLD", MutedC, 52), WithCol(HRow(Hold, "", HoldF, 0, 0), 1), WithCol(new TextBlock { Text = HoldF(P(Hold)), FontSize = 9, Foreground = TxtC, Width = 46, TextAlignment = TextAlignment.Right }.WithMono().Track(readouts, t => t.Text = HoldF(P(Hold))), 2) } });
        env.Children.Add(new Grid { Height = 12, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, Children = { Cap("RELEASE", MutedC, 52), WithCol(HRow(Release, "", RelF, 0, 0), 1), WithCol(new TextBlock { Text = RelF(P(Release)), FontSize = 9, Foreground = TxtC, Width = 46, TextAlignment = TextAlignment.Right }.WithMono().Track(readouts, t => t.Text = RelF(P(Release))), 2) } });
        env.Children.Add(new Grid { Height = 12, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, Children = { Cap("FLOOR", MutedC, 52), WithCol(HRow(Floor, "", FloorF, 0, 0), 1), WithCol(new TextBlock { Text = FloorF(P(Floor)), FontSize = 9, Foreground = TxtC, Width = 46, TextAlignment = TextAlignment.Right }.WithMono().Track(readouts, t => t.Text = FloorF(P(Floor))), 2) } });
        var envPanel = new Border { Width = 206, Child = env };

        // ================= middle signal graph =================
        var mid = new Border { Padding = new Thickness(0, 7, 0, 7), Child = signal };

        // ================= METERS + DETECTOR rail =================
        Control Meter(string label, out Border fill, out TextBlock val, IBrush baseCol, out Border tick)
        {
            val = new TextBlock { FontSize = 9, Foreground = LabelC, Width = 38, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center }.WithMono();
            fill = new Border { Background = baseCol, CornerRadius = NotaRadius.Clip, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, Width = 0 };
            tick = new Border { Width = 1, Background = ThrC, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, IsVisible = false };
            var trk = new Panel { Height = 5, Children = { new Border { Background = Inset, CornerRadius = NotaRadius.Clip }, fill, tick } };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto"), ColumnSpacing = 5, Height = 6, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Cap(label)); Grid.SetColumn(trk, 1); g.Children.Add(trk); Grid.SetColumn(val, 2); g.Children.Add(val);
            g.Tag = trk;   // so refresher can read the track width
            return g;
        }
        var inRow = Meter("IN", out var inFill, out var inVal, Green, out var inTick);
        var grRow = Meter("GR", out var grFill, out var grVal, Red, out var _);
        var inTrk = (Panel)((Grid)inRow).Tag!; var grTrk = (Panel)((Grid)grRow).Tag!;

        var led = new Border { Width = 8, Height = 8, CornerRadius = NotaRadius.Control, Background = Green, VerticalAlignment = VerticalAlignment.Center };
        var ledTxt = new TextBlock { Text = "OPEN", FontSize = 9, FontWeight = FontWeight.Bold, Foreground = Green, VerticalAlignment = VerticalAlignment.Center };
        var duty = new TextBlock { Text = "", FontSize = 8, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }.WithMono();
        var ledRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6, Children = { led, WithCol(ledTxt, 1), WithCol(duty, 2) } };

        // detector: EXT toggle + source combo + SC EQ (band-pass) + LISTEN.
        var scIds = new List<int> { -1 };
        var scCombo = new ComboBox { FontSize = 9, Height = 20, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 0) };
        scCombo.Items.Add("Internal (this track)");
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == track) continue;
            string kind = ti.IsReturn ? "Return" : ti.IsInstrument ? "Inst" : "Audio";
            scIds.Add(ti.Id); scCombo.Items.Add($"Key: {i + 1} · {kind}");
        }
        scCombo.SelectedIndex = Math.Max(0, scIds.IndexOf(engine.DeviceSidechainSource(track, di)));
        var extSw = MakeToggle(() => engine.DeviceSidechainSource(track, di) >= 0, on => {
            int src = on ? (scIds.Count > 1 ? scIds[1] : -1) : -1; engine.SetDeviceSidechainSource(track, di, src);
            scCombo.SelectedIndex = Math.Max(0, scIds.IndexOf(src));
        }, "EXT", readouts);
        scCombo.SelectionChanged += (_, _) => { int sel = scCombo.SelectedIndex; if (sel < 0 || sel >= scIds.Count) return; engine.SetDeviceSidechainSource(track, di, scIds[sel]); };

        var detEq = new ShutterDetectorEQ();
        detEq.Set(P(DetHP), P(DetLP));
        detEq.HpChanged += v => { Begin(DetHP); SetP(DetHP, (float)v); End(DetHP); };
        detEq.LpChanged += v => { Begin(DetLP); SetP(DetLP, (float)v); End(DetLP); };
        readouts.Add(() => detEq.Set(P(DetHP), P(DetLP)));

        var listenChip = new Border { BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(5, 0), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "LISTEN", FontSize = 8, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center } };
        void ListenSync() { bool on = P(Listen) >= 0.5f; listenChip.Background = on ? NotaPalette.AccentSubtle : Brushes.Transparent; listenChip.BorderBrush = on ? NotaPalette.BorderBrass : Dim; ((TextBlock)listenChip.Child!).Foreground = on ? NotaPalette.AccentHover : MutedC; }
        listenChip.PointerPressed += (_, e) => { e.Handled = true; SetP(Listen, P(Listen) >= 0.5f ? 0f : 1f); ListenSync(); };
        readouts.Add(ListenSync);

        var scEqBox = new Border { Background = Inset, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(4), Child =
            new DockPanel { LastChildFill = true, Children = {
                new Grid { Height = 11, [DockPanel.DockProperty] = Dock.Top, ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Cap("SC EQ", MutedC), WithCol(listenChip, 1) } },
                detEq } } };

        var detector = new Border { BorderBrush = TealC, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(7, 0, 0, 0), Child =
            new DockPanel { LastChildFill = true, Children = {
                new Grid { Height = 12, [DockPanel.DockProperty] = Dock.Top, ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Cap("DETECTOR", TealC), WithCol(extSw, 1) } },
                new Border { Height = 20, Margin = new Thickness(0, 4), [DockPanel.DockProperty] = Dock.Top, Child = scCombo },
                scEqBox } } };

        var rail = new Border { Width = 176, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 7), Child =
            new DockPanel { LastChildFill = true, Children = {
                new StackPanel { [DockPanel.DockProperty] = Dock.Top, Spacing = 5, Children = { Cap("METERS"), inRow, grRow, ledRow,
                    new Border { Height = 1, Background = NotaPalette.SurfaceRaised, Margin = new Thickness(0, 2) } } },
                detector } } };

        // ================= assemble =================
        DockPanel.SetDock(envPanel, Dock.Left); DockPanel.SetDock(rail, Dock.Right);
        var body = new DockPanel { LastChildFill = true, Children = { envPanel, rail, mid } };
        DockPanel.SetDock(live, Dock.Top);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp, Children = { live, body } };

        // ---- live refresh from scope ----
        readouts.Add(() =>
        {
            int n = engine.DeviceScope(track, di, scope, kScope);
            if (n < kScope) return;
            bool open = scope[S_Open] >= 0.5f && scope[S_GateGain] > 0.5f;
            state.Text = open ? "OPEN" : "SHUT"; state.Foreground = open ? Green : NotaPalette.TextSecondary;
            stateSub.Text = $"gate · GR {scope[S_GrDb]:0.0}\u2009dB";
            led.Background = open ? Green : Dim; ledTxt.Text = open ? "OPEN" : "SHUT"; ledTxt.Foreground = open ? Green : MutedC;
            duty.Text = $"{scope[S_GateGain] * 100:0}\u2009% open";
            double inW = inTrk.Bounds.Width, grW = grTrk.Bounds.Width;
            inFill.Width = Math.Clamp((scope[S_InDb] + 60) / 60, 0, 1) * inW;
            inVal.Text = $"{scope[S_InDb]:0.0}";
            double tf = Math.Clamp((ThrDb() + 60) / 60, 0, 1);
            inTick.IsVisible = true; inTick.Margin = new Thickness(tf * inW, 0, 0, 0);
            grFill.Width = Math.Clamp(scope[S_GrDb] / 60, 0, 1) * grW;
            grVal.Text = scope[S_GrDb] > 59 ? "−∞" : $"{-scope[S_GrDb]:0.0}";
        });

        void RefreshAll() { foreach (var a in readouts) a(); }
        SyncLines();
        RefreshAll();
        return root;
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
    private static Control WithCol(Control c, int col) { Grid.SetColumn(c, col); return c; }

    // Small teal toggle with a state getter/setter, registered to the refresher list.
    private static Control MakeToggle(Func<bool> get, Action<bool> set, string label, List<Action> readouts)
    {
        var b = DeviceCardKit.Switch(label, get, () => set(!get()), out var sync);
        readouts.Add(sync);
        return b;
    }
}

internal static class ShutterExt
{
    public static TextBlock Track(this TextBlock t, System.Collections.Generic.List<Action> readouts, Action<TextBlock> upd) { readouts.Add(() => upd(t)); return t; }
}
