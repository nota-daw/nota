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

    public string? Subtitle => "DYNAMICS";   // the processing type, shown as the header badge
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
            int n = names.Length;
            var host = DeviceCardKit.Segments(names, () => Sel(p, n), iv => { SetR(p, iv); transfer.Tick(); }, out var sync);
            readouts.Add(sync);
            MidiLearn.Bind(host, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return host;
        }
        Control Toggle(int p, string label)
        {
            var b = new Border { CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.SemiBold } };
            void Hi() { bool on = P(p) > 0.5f; b.Background = on ? NotaPalette.AccentSubtle : Sunken; b.BorderBrush = on ? NotaPalette.BorderBrass : BorderDef; ((TextBlock)b.Child!).Foreground = on ? NotaPalette.AccentHover : TextTertiary; }
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
            var row = DeviceCardKit.SliderRow(name, () => NormOf(P(p)), n => { SetR(p, (float)ValOf(n)); transfer.Tick(); }, () => fmt(P(p)), out var sync,
                begin: () => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""),
                end: () => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""),
                labelWidth: lw, trackWidth: tw, valueWidth: 44);
            readouts.Add(sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), name);
            return row;
        }

        string Db1(double v) => $"{v:+0.0;−0.0;0.0}";
        string DbT(double v) => $"{v:0.0}\u2009dB";
        string RatioF(double v) => $"{v:0}:1";
        string Pct(double v) => $"{v:0}\u2009%";
        string Ms(double v) => v < 10 ? $"{v:0.0}\u2009ms" : $"{v:0}\u2009ms";
        string Hz(double v) => v >= 1000 ? $"{v / 1000:0.0}k" : $"{v:0}";

        // ---- top strip: character models + threshold/ratio/mix sliders ----
        var live = new Border { Height = 34, Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0), Children = {
                Chips(Character, Chars),
                HSlider(Threshold, "THRESH", DbT, false, Brass, 60), HSlider(Ratio, "RATIO", RatioF, true, Brass, 52), HSlider(Mix, "MIX", Pct, false, Brass, 52) } } };

        // ---- graphs column (transfer plot · GR history; both label themselves on-graph) ----
        var graphsCol = new Grid { Width = 264, RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 5, VerticalAlignment = VerticalAlignment.Stretch };
        graphsCol.Children.Add(transfer);
        grHist.Height = 64; Grid.SetRow(grHist, 1); graphsCol.Children.Add(grHist);

        // ---- TIMING panel ----
        Control Panel(string title, Control body) => new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, Padding = new Thickness(8, 6), Child = new StackPanel { Spacing = 5, Children = { Lbl(title, 8, TextTertiary), body } } };
        var autoRel = Toggle(AutoRelease, "Auto-release"); DockPanel.SetDock(autoRel, Dock.Right);
        var detRow = new DockPanel { LastChildFill = false, Children = { Chips(Detection, new[] { "Peak", "RMS" }), autoRel } };
        var timing = Panel("TIMING", new StackPanel { Spacing = 6, Children = {
            detRow,
            Row(2, Cell("Attack", Attack, Ms), Cell("Release", Release, Ms), Cell("Knee", Knee, DbT), Cell("Lookahead", Lookahead, Ms)) } });

        // Sidechain row (teal — a detector path).
        Control Sidechain()
        {
            var ids = new System.Collections.Generic.List<int> { -1 };
            var combo = new ComboBox { FontSize = 9, Height = 22, MinWidth = 96 };
            combo.Items.Add("Internal");
            for (int i = 0; i < engine.TrackCount; i++) { if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == track) continue; ids.Add(ti.Id); combo.Items.Add($"{i + 1} · {(ti.IsReturn ? "Return" : ti.IsInstrument ? "Instr" : "Audio")}"); }
            combo.SelectedIndex = Math.Max(0, ids.IndexOf(engine.DeviceSidechainSource(track, di)));
            combo.SelectionChanged += (_, _) => { int s = combo.SelectedIndex; if (s >= 0 && s < ids.Count) { engine.SetDeviceSidechainSource(track, di, ids[s]); ctx.NotifyChanged(); } };
            var preBox = DeviceCardKit.Segments(new[] { "Pre", "Post" }, () => engine.DeviceSidechainTapPre(track, di) ? 0 : 1,
                i => engine.SetDeviceSidechainTapPre(track, di, i == 0), out _);
            var listen = Toggle(ScListen, "Listen"); DockPanel.SetDock(listen, Dock.Right);
            var topRow = new DockPanel { LastChildFill = false, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { combo, preBox } }, listen } };
            // HP and LP side by side (2 columns) so the sliders line up on the grid.
            var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
            var hp = HSlider(ScHP, "HP", Hz, true, Teal, 68, lw: 16);
            var lp = HSlider(ScLP, "LP", Hz, true, Teal, 68, lw: 16);
            Grid.SetColumn(lp, 1); filters.Children.Add(hp); filters.Children.Add(lp);
            return new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, Padding = new Thickness(8, 6),
                Child = new StackPanel { Spacing = 5, Children = { Lbl("SIDECHAIN", 8, TextTertiary), topRow, filters } } };
        }
        var timingCol = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { timing, Sidechain() } };

        // ---- output rail ----
        var outRail = new Border { Width = 104, Background = NotaPalette.SurfaceInset, BorderBrush = BorderDef, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(7, 8),
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
        return new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp, Children = { live, body } };
    }
}
