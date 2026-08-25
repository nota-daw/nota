// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Compressor (kind 1) body, mockup 2n on the shared shell
// (700×260, full-bleed): a LIVE strip (character models + Thresh/Ratio/Mix) over a body
// of the transfer plot + gain-reduction history (graphs 264) | a TIMING panel (Peak/RMS
// · auto-release · Attack/Release/Knee/Lookahead · side-chain row) | an OUTPUT rail
// (auto-gain · Makeup · Range). Params are the device's raw-unit params.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class CompressorDeviceBody : IDeviceBody
{
    private const int Threshold = 0, Ratio = 1, Attack = 2, Release = 3, Makeup = 4, Knee = 5, Mix = 6,
                      Lookahead = 7, Detection = 8, AutoRelease = 9, AutoGain = 10, Range = 11, Character = 12, ScHP = 13, ScLP = 14, ScListen = 15;
    private static readonly string[] Chars = { "Clean", "Glue", "Punch", "Opto", "FET" };

    public double Width => 700;
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine; int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        float Mn(int p) => engine.DeviceParamMin(track, di, p);
        float Mx(int p) => engine.DeviceParamMax(track, di, p);
        void SetR(int p, float v) => engine.DeviceSetParam(track, di, p, v);
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p)), 0, n - 1);

        var readouts = new System.Collections.Generic.List<Action>();
        Control Lbl(string t, double fs, IBrush c) => new TextBlock { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c, VerticalAlignment = VerticalAlignment.Center };
        Control Row(double sp, params Control[] cs) { var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) r.Children.Add(c); return r; }

        var transfer = new CompTransfer(engine, track, di) { VerticalAlignment = VerticalAlignment.Stretch };
        var grHist = new CompGrHistory { VerticalAlignment = VerticalAlignment.Stretch };

        // Gauge knob over a raw min..max param.
        Control Cell(string name, int p, Func<double, string> fmt, IBrush? arc = null, double size = 34)
        {
            double mn = Mn(p), mx = Mx(p), span = Math.Max(1e-6, mx - mn);
            var val = new TextBlock { Text = fmt(P(p)), FontSize = 8, Foreground = TextPrimary };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var knob = new Knob((P(p) - mn) / span, 1.0) { Accent = true, ArcColor = arc, Default = (engine.DeviceParamDefault(track, di, p) - mn) / span, Width = size, Height = size };
            knob.ValueChanged += v => { float raw = (float)(mn + v * span); SetR(p, raw); val.Text = fmt(raw); transfer.Tick(); };
            knob.GestureBegin += () => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
            knob.GestureEnd += () => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
            MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, di, p), name);
            readouts.Add(() => { if (!knob.Dragging) { double c = (P(p) - mn) / span; if (Math.Abs(c - knob.Value) > 1e-3) { knob.Value = c; val.Text = fmt(P(p)); } } });
            return KnobCell(name, knob, val, size + 16);
        }
        Control Chips(int p, string[] names, IBrush? tint = null)
        {
            int n = names.Length; var arr = new Border[n];
            void Hi() { int cur = Sel(p, n); for (int i = 0; i < n; i++) { bool on = i == cur; arr[i].Background = on ? AccentSubtleB : Brushes.Transparent; arr[i].BorderBrush = on ? (tint ?? Brass) : Brushes.Transparent; ((TextBlock)arr[i].Child!).Foreground = on ? (tint ?? AccentBright) : TextTertiary; } }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < n; i++) { int iv = i; var c = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(6, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = names[i], FontSize = 9, Foreground = TextTertiary } }; c.PointerPressed += (_, _) => { SetR(p, iv); Hi(); transfer.Tick(); }; arr[i] = c; row.Children.Add(c); }
            readouts.Add(Hi); Hi();
            var chipsHost = new Border { Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = row };
            MidiLearn.Bind(chipsHost, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return chipsHost;
        }
        Control Toggle(int p, string label)
        {
            var b = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.SemiBold } };
            void Hi() { bool on = P(p) > 0.5f; b.Background = on ? AccentSubtleB : Sunken; b.BorderBrush = on ? Brass : BorderDef; ((TextBlock)b.Child!).Foreground = on ? AccentBright : TextTertiary; }
            b.PointerPressed += (_, _) => { SetR(p, P(p) > 0.5f ? 0f : 1f); Hi(); };
            readouts.Add(Hi); Hi();
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), label);
            return b;
        }
        // Horizontal slider (label · track · value) over a raw param; optional log scale.
        Control HSlider(int p, string name, Func<double, string> fmt, bool log, IBrush accent, double tw, double lw = 0)
        {
            double mn = Mn(p), mx = Mx(p);
            double lmn = log ? Math.Log(mn) : mn, lspan = (log ? Math.Log(mx) : mx) - lmn;
            double NormOf(double v) { double x = log ? Math.Log(Math.Clamp(v, mn, mx)) : Math.Clamp(v, mn, mx); return (x - lmn) / Math.Max(1e-9, lspan); }
            double ValOf(double n) { double x = lmn + Math.Clamp(n, 0, 1) * lspan; return log ? Math.Exp(x) : x; }
            var trk = new Border { Width = tw, Height = 3, Background = Sunken, CornerRadius = new CornerRadius(2) };
            var fill = new Border { Height = 3, Background = accent, CornerRadius = new CornerRadius(2) };
            var handle = new Border { Width = 8, Height = 9, Background = new SolidColorBrush(Color.Parse("#A39D8F")), CornerRadius = new CornerRadius(2) };
            var canvas = new Canvas { Width = tw, Height = 9, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            Canvas.SetTop(trk, 3); Canvas.SetTop(fill, 3); Canvas.SetTop(handle, 0);
            canvas.Children.Add(trk); canvas.Children.Add(fill); canvas.Children.Add(handle);
            var val = new TextBlock { FontSize = 9, Foreground = TextPrimary, Width = 44, VerticalAlignment = VerticalAlignment.Center };
            val.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            bool drag = false;
            void Vis(double v) { double n = NormOf(v); fill.Width = Math.Max(0, n * tw); Canvas.SetLeft(handle, n * tw - 4); val.Text = fmt(v); }
            void From(PointerEventArgs e) { double n = Math.Clamp(e.GetPosition(canvas).X / tw, 0, 1); float v = (float)ValOf(n); SetR(p, v); Vis(v); transfer.Tick(); }
            canvas.PointerPressed += (_, e) => { drag = true; engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""); e.Pointer.Capture(canvas); From(e); };
            canvas.PointerMoved += (_, e) => { if (drag) From(e); };
            canvas.PointerReleased += (_, e) => { if (drag) { drag = false; engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""); e.Pointer.Capture(null); } };
            readouts.Add(() => { if (!drag) Vis(P(p)); });
            Vis(P(p));
            var lbl = new TextBlock { Text = name, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = accent == Teal ? Teal : TextTertiary, VerticalAlignment = VerticalAlignment.Center };
            if (lw > 0) lbl.Width = lw;
            var sl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { lbl, canvas, val } };
            MidiLearn.Bind(sl, MidiTarget.DeviceParam(track, di, p), name);
            return sl;
        }

        string Db1(double v) => $"{v:+0.0;-0.0;0.0}";
        string DbT(double v) => $"{v:0.0} dB";
        string RatioF(double v) => $"{v:0}:1";
        string Pct(double v) => $"{v:0}%";
        string Ms(double v) => v < 10 ? $"{v:0.0} ms" : $"{v:0} ms";
        string Hz(double v) => v >= 1000 ? $"{v / 1000:0.0}k" : $"{v:0}";

        // ---- top strip: character models + threshold/ratio/mix sliders ----
        var live = new Border { Height = 34, Background = new SolidColorBrush(Color.Parse("#1E1C18")), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0), Children = {
                Chips(Character, Chars),
                HSlider(Threshold, "THRESH", DbT, false, Brass, 60), HSlider(Ratio, "RATIO", RatioF, true, Brass, 52), HSlider(Mix, "MIX", Pct, false, Brass, 52) } } };

        // ---- graphs column (transfer plot · GR history; both label themselves on-graph) ----
        var graphsCol = new Grid { Width = 264, RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 5, VerticalAlignment = VerticalAlignment.Stretch };
        graphsCol.Children.Add(transfer);
        grHist.Height = 64; Grid.SetRow(grHist, 1); graphsCol.Children.Add(grHist);

        // ---- TIMING panel ----
        Control Panel(string title, Control body) => new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Child = new StackPanel { Spacing = 5, Children = { Lbl(title, 8, TextTertiary), body } } };
        var autoRel = Toggle(AutoRelease, "Auto-release"); DockPanel.SetDock(autoRel, Dock.Right);
        var detRow = new DockPanel { LastChildFill = false, Children = { Chips(Detection, new[] { "Peak", "RMS" }), autoRel } };
        var timing = Panel("TIMING", new StackPanel { Spacing = 6, Children = {
            detRow,
            Row(2, Cell("Attack", Attack, Ms), Cell("Release", Release, Ms), Cell("Knee", Knee, DbT), Cell("Lookahead", Lookahead, Ms)) } });

        // Sidechain row (teal — a detector path).
        Control Sidechain()
        {
            var ids = new System.Collections.Generic.List<int> { -1 };
            var combo = new ComboBox { FontSize = 10, Height = 22, MinWidth = 96 };
            combo.Items.Add("Internal");
            for (int i = 0; i < engine.TrackCount; i++) { if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == track) continue; ids.Add(ti.Id); combo.Items.Add($"{i + 1} · {(ti.IsReturn ? "Return" : ti.IsInstrument ? "Instr" : "Audio")}"); }
            combo.SelectedIndex = Math.Max(0, ids.IndexOf(engine.DeviceSidechainSource(track, di)));
            combo.SelectionChanged += (_, _) => { int s = combo.SelectedIndex; if (s >= 0 && s < ids.Count) { engine.SetDeviceSidechainSource(track, di, ids[s]); ctx.NotifyChanged(); } };
            var pre = new Border[2]; var preRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            void HiPre() { bool p = engine.DeviceSidechainTapPre(track, di); ((TextBlock)pre[0].Child!).Foreground = p ? AccentBright : TextTertiary; pre[0].Background = p ? AccentSubtleB : Brushes.Transparent; ((TextBlock)pre[1].Child!).Foreground = !p ? AccentBright : TextTertiary; pre[1].Background = !p ? AccentSubtleB : Brushes.Transparent; }
            for (int i = 0; i < 2; i++) { bool preTap = i == 0; var c = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = preTap ? "Pre" : "Post", FontSize = 9, Foreground = TextTertiary } }; c.PointerPressed += (_, _) => { engine.SetDeviceSidechainTapPre(track, di, preTap); HiPre(); }; pre[i] = c; preRow.Children.Add(c); }
            var preBox = new Border { Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = preRow };
            HiPre();
            var listen = Toggle(ScListen, "Listen"); DockPanel.SetDock(listen, Dock.Right);
            var topRow = new DockPanel { LastChildFill = false, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { combo, preBox } }, listen } };
            // HP and LP side by side (2 columns) so the sliders line up on the grid.
            var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
            var hp = HSlider(ScHP, "HP", Hz, true, Teal, 68, lw: 16);
            var lp = HSlider(ScLP, "LP", Hz, true, Teal, 68, lw: 16);
            Grid.SetColumn(lp, 1); filters.Children.Add(hp); filters.Children.Add(lp);
            return new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6),
                Child = new StackPanel { Spacing = 5, Children = { Lbl("SIDECHAIN", 8, TextTertiary), topRow, filters } } };
        }
        var timingCol = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { timing, Sidechain() } };

        // ---- output rail ----
        var outRail = new Border { Width = 104, Background = new SolidColorBrush(Color.Parse("#1B1916")), BorderBrush = BorderDef, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(7, 8),
            Child = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = {
                Lbl("OUTPUT", 8, TextPrimary), Toggle(AutoGain, "Auto gain"),
                Cell("Makeup", Makeup, Db1, null, 40), Cell("Range", Range, v => v >= 47.5 ? "off" : $"−{v:0}", null, 36) } } };
        DockPanel.SetDock(outRail, Dock.Right);

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Margin = new Thickness(9, 8) };
        content.Children.Add(graphsCol); Grid.SetColumn(timingCol, 1); content.Children.Add(timingCol);
        var body = new DockPanel { LastChildFill = true, Children = { outRail, content } };

        ctx.AddDeviceRefresher(() =>
        {
            grHist.Push(engine.DeviceGainReduction(track, di));
            transfer.Tick();
            foreach (var a in readouts) a();
        });
        foreach (var a in readouts) a();

        DockPanel.SetDock(live, Dock.Top);
        return new DockPanel { LastChildFill = true, Background = new SolidColorBrush(Color.Parse("#171613")), Children = { live, body } };
    }
}
