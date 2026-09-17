// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota AutoGain (device kind 18) body, built to the AutoGain
// mockup: a loudness-matching utility. A LIVE strip (Target LUFS · Scale · a REFERENCE
// sidechain picker · MATCH · AUTO) over a body of three columns — a CORRECTION column
// (Auto/Manual, the big applied-gain number with a boost/cut indicator, Trim), the LOUDNESS
// history graph in the middle, and a METERS + RESPONSE rail on the right. Routing a
// reference track (sidechain) makes the target follow that track's loudness instead of the
// Target param — so the TARGET control dims and the graph's target line moves with it.

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

internal sealed class AutoGainDeviceBody : IDeviceBody
{
    private const int Target = 0, Scale = 1, Auto = 2, Trim = 3, Response = 4, Window = 5, MaxGain = 6, Safe = 7, Ceiling = 8;
    // scope layout — must match AutoGain.h.
    private const int S_InLufs = 0, S_OutLufs = 1, S_InMom = 2, S_Target = 3, S_Applied = 4, S_TruePeak = 5, S_Corr = 6, S_ScLufs = 7, kScope = 9;

    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush RailBg = NotaPalette.SurfaceInset;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);
    private static readonly IBrush TealC = NotaPalette.Teal;
    private static readonly IBrush TealBright = NotaPalette.TealBright;
    private static readonly IBrush TealSubtle = NotaPalette.Wash(NotaPalette.Teal, 0x24);
    private static readonly IBrush TxtC = NotaPalette.TextPrimary;
    private static readonly IBrush MutedC = NotaPalette.TextTertiary;
    private static readonly IBrush LabelC = NotaPalette.TextSecondary;
    private static readonly IBrush Dim = NotaPalette.BorderStrong;
    private static readonly IBrush Green = NotaPalette.Success;
    private static readonly IBrush Yellow = NotaPalette.Warning;
    private static readonly IBrush HandleC = NotaPalette.TextSecondary;

    public double Width => 700;

    public string? Subtitle => "LEVELER";   // the processing type, shown as the header badge
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

        var hist = new AutoGainHistory(engine, track, di) { VerticalAlignment = VerticalAlignment.Stretch };
        ctx.AddDeviceRefresher(hist.Tick);

        // ---- formatters ----
        static string TargetF(double v) => $"{-36 + v * 36:0.0}\u2009LUFS";
        static string TrimF(double v) => $"{(v - 0.5) * 24:+0.0;−0.0;0.0}";
        static string WindowF(double v) => $"{0.4 * Math.Pow(25, v):0.0}\u2009s";
        static string MaxF(double v) => $"{v * 24:0}";

        // ---- horizontal slider (label | slot | value) ----
        Control HRow(int p, string label, Func<double, string> fmt, double labW, double valW, bool teal = false, bool bipolar = false)
        {
            var row = DeviceCardKit.SliderRow(labW > 0 ? label : "", () => P(p), n => { SetP(p, (float)n); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), bipolar: bipolar, labelWidth: labW, valueWidth: valW);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), label);
            readouts.Add(sync);
            return row;
        }

        // Segmented pill (n options over a normalized param). teal optional.
        Control Seg(int p, string[] opts, bool teal = false, Action? after = null)
        {
            int n = opts.Length;
            var seg = DeviceCardKit.Segments(opts, () => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1), iv => { SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); after?.Invoke(); }, out var sync);
            readouts.Add(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }

        // Teal toggle switch backed by a 0/1 param.
        Control Toggle(int p, string label)
        {
            var host = Switch(label, () => P(p) >= 0.5f, () => SetP(p, P(p) >= 0.5f ? 0f : 1f), out var sync);
            readouts.Add(sync);
            MidiLearn.Bind(host, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return host;
        }

        // ---- reference (sidechain) source combo ----
        var scIds = new List<int> { -1 };
        var scCombo = new ComboBox { FontSize = 9, Height = 20, Width = 104, Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
        scCombo.Items.Add("Target → fixed");
        for (int i = 0; i < engine.TrackCount; i++)
        {
            if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == track) continue;
            string kind = ti.IsReturn ? "Return" : ti.IsInstrument ? "Inst" : "Audio";
            scIds.Add(ti.Id); scCombo.Items.Add($"Ref: {i + 1} · {kind}");
        }
        scCombo.SelectedIndex = Math.Max(0, scIds.IndexOf(engine.DeviceSidechainSource(track, di)));

        // ---- LIVE strip ----
        var targetGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Width = 210, Children = {
            Cap("TARGET", MutedC, 40), new Border { Child = HRow(Target, "", TargetF, 0, 66), Width = 150 } } };
        scCombo.SelectionChanged += (_, _) => { int sel = scCombo.SelectedIndex; if (sel < 0 || sel >= scIds.Count) return; engine.SetDeviceSidechainSource(track, di, scIds[sel]); Inactive.Set(targetGroup, scIds[sel] >= 0, interactive: true); };
        Inactive.Set(targetGroup, engine.DeviceSidechainSource(track, di) >= 0, interactive: true);

        var match = new Border { Background = Amber, CornerRadius = NotaRadius.Control, Padding = new Thickness(12, 3), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "MATCH", FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextOnAccent } };
        match.PointerPressed += (_, e) => { e.Handled = true; SetP(Auto, 0f); foreach (var a in readouts) a(); };   // freeze the current correction

        var live = new Border { Height = 34, Background = HdrBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { LastChildFill = false, Margin = new Thickness(9, 0), Children = {
                WithDock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Children = {
                    Cap("LIVE"), targetGroup,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("SCALE"), Seg(Scale, new[] { "Mom", "Short", "Integ" }, after: hist.Tick) } } } }, Dock.Left),
                WithDock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("REF", TealC), scCombo } },
                    match, Toggle(Auto, "AUTO") } }, Dock.Right) } } };

        // ================= CORRECTION column =================
        var applied = new TextBlock { Text = "0.0", FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = TealBright, LineHeight = 22 };
        applied.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        // vertical boost/cut indicator (0 dB at centre)
        var bar = new Border { Width = 8, Background = Inset, CornerRadius = NotaRadius.Control, VerticalAlignment = VerticalAlignment.Stretch };
        var barCenter = new Border { Height = 1, Background = Dim, VerticalAlignment = VerticalAlignment.Center };
        var barFill = new Border { Width = 8, Background = TealC, CornerRadius = NotaRadius.Control, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Height = 0 };
        var barMarker = new Border { Width = 16, Height = 3, Background = TealBright, CornerRadius = NotaRadius.Clip, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        var barSlot = new Panel { Width = 30, Children = { new Border { Width = 8, HorizontalAlignment = HorizontalAlignment.Center, Child = bar }, barCenter, barFill, barMarker } };

        var correction = new DockPanel { LastChildFill = true, Margin = new Thickness(9, 7) };
        correction.AddDock(new Grid { Height = 10, ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Cap("CORRECTION"), WithCol(new TextBlock { Text = "auto", FontSize = 8, Foreground = MutedC, HorizontalAlignment = HorizontalAlignment.Right }, 1) } }, Dock.Top);
        correction.AddDock(new Border { Margin = new Thickness(0, 5), Child = Seg(Auto, new[] { "Manual", "Auto" }) }, Dock.Top);   // Manual=0, Auto=1
        // trim at bottom
        correction.AddDock(new Grid { Height = 12, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, Margin = new Thickness(0, 5, 0, 0), Children = {
            Cap("TRIM", MutedC, 30), WithCol(HRow(Trim, "", TrimF, 0, 0, bipolar: true), 1), WithCol(new TextBlock().WithMono(), 2) } }, Dock.Bottom);
        // centre: indicator + number
        var numCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Children = {
            applied, new TextBlock { Text = "dB applied", FontSize = 9, Foreground = MutedC }.WithMono(),
            new Border { Height = 8 },
            new TextBlock { Text = "boost +12", FontSize = 8, Foreground = NotaPalette.TextDisabled }.WithMono(),
            new TextBlock { Text = "0\u2009dB", FontSize = 8, Foreground = NotaPalette.TextDisabled }.WithMono(),
            new TextBlock { Text = "cut −12", FontSize = 8, Foreground = NotaPalette.TextDisabled }.WithMono() } };
        correction.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9, Children = { barSlot, WithCol(numCol, 1) } });
        var corrPanel = new Border { Width = 206, Child = correction };

        // ================= middle graph + stats =================
        var stats = new TextBlock { FontSize = 8, Foreground = NotaPalette.TextDisabled, HorizontalAlignment = HorizontalAlignment.Center }.WithMono();
        var mid = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 7, 8, 7) };
        mid.AddDock(new Border { Height = 12, Child = stats, [DockPanel.DockProperty] = Dock.Bottom }, Dock.Bottom);
        mid.Children.Add(new Border { Child = hist });

        // ================= METERS + RESPONSE rail =================
        Control Meter(string label, out Action<float, string> set, IBrush baseCol, bool centered = false)
        {
            var val = new TextBlock { FontSize = 9, Foreground = LabelC, Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center }.WithMono();
            var fill = new Border { Background = baseCol, CornerRadius = NotaRadius.Clip, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
            var center = centered ? new Border { Width = 1, Background = Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch } : null;
            var trk = new Panel { Height = 5, Children = { } }; trk.Children.Add(new Border { Background = Inset, CornerRadius = NotaRadius.Clip }); if (center != null) trk.Children.Add(center); trk.Children.Add(fill);
            set = (frac, text) => { double W = trk.Bounds.Width; if (centered) { double c = W * 0.5; double x = frac * c; fill.HorizontalAlignment = HorizontalAlignment.Left; fill.Margin = new Thickness(x >= 0 ? c : c + x, 0, 0, 0); fill.Width = Math.Abs(x); } else fill.Width = Math.Clamp(frac, 0, 1) * W; val.Text = text; };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto"), ColumnSpacing = 5, Height = 6, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Cap(label)); Grid.SetColumn(trk, 1); g.Children.Add(trk); Grid.SetColumn(val, 2); g.Children.Add(val);
            return g;
        }
        var inRow = Meter("IN", out var setIn, Green); var outRow = Meter("OUT", out var setOut, Green);
        var tpRow = Meter("TP", out var setTp, Green); var corRow = Meter("COR", out var setCor, Green, centered: true);

        var rail = new Border { Width = 184, Background = RailBg, BorderBrush = Border2, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 7),
            Child = new StackPanel { Spacing = 5, Children = {
                Cap("METERS"), inRow, outRow, tpRow, corRow,
                new Border { Height = 1, Background = NotaPalette.SurfaceRaised, Margin = new Thickness(0, 1) },
                new Border { BorderBrush = TealC, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(7, 0, 0, 0), Child =
                    new StackPanel { Spacing = 5, Children = {
                        Cap("RESPONSE", TealC),
                        Seg(Response, new[] { "Fast", "Slow" }, teal: true),
                        HRow(Window, "WINDOW", WindowF, 52, 30, teal: true),
                        HRow(MaxGain, "MAX GAIN", MaxF, 52, 24, teal: true),
                        Toggle(Safe, "TRUE-PEAK SAFE") } } } } } };

        // ================= assemble =================
        DockPanel.SetDock(corrPanel, Dock.Left); DockPanel.SetDock(rail, Dock.Right);
        var body = new DockPanel { LastChildFill = true, Children = { corrPanel, rail, mid } };
        DockPanel.SetDock(live, Dock.Top);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp, Children = { live, body } };

        // ---- live refresh from scope ----
        float MeterFrac(float lufs) => (float)Math.Clamp((lufs + 40) / 40.0, 0, 1);  // −40..0
        readouts.Add(() =>
        {
            int n = engine.DeviceScope(track, di, scope, kScope);
            if (n < kScope) return;
            float ap = scope[S_Applied];
            applied.Text = $"{ap:+0.0;−0.0;0.0}";
            bool boost = ap >= 0;
            applied.Foreground = boost ? TealBright : AmberLit;
            // indicator: marker + fill from centre
            double H = bar.Bounds.Height; double frac = Math.Clamp((12 - ap) / 24.0, 0, 1);
            barMarker.Margin = new Thickness(0, Math.Clamp(frac * H - 1.5, 0, Math.Max(0, H - 3)), 0, 0);
            double half = H * 0.5; double mag = Math.Clamp(Math.Abs(ap) / 12.0, 0, 1) * half;
            barFill.Background = boost ? TealC : Amber;
            barFill.Height = mag; barFill.VerticalAlignment = VerticalAlignment.Center;
            barFill.Margin = boost ? new Thickness(0, 0, 0, mag) : new Thickness(0, mag, 0, 0);
            setIn(MeterFrac(scope[S_InLufs]), $"{scope[S_InLufs]:0.0}");
            setOut(MeterFrac(scope[S_OutLufs]), $"{scope[S_OutLufs]:0.0}");
            setTp(MeterFrac(scope[S_TruePeak]), $"{scope[S_TruePeak]:0.0}");
            setCor(scope[S_Corr], $"{scope[S_Corr]:+0.00;−0.00;0.00}");
            stats.Text = $"IN {scope[S_InLufs]:0.0} · OUT {scope[S_OutLufs]:0.0} · Δ {scope[S_OutLufs] - scope[S_InLufs]:+0.0;−0.0;0.0}\u2009LUFS · TP {scope[S_TruePeak]:0.0}\u2009dB";
        });

        void RefreshAll() { foreach (var a in readouts) a(); }
        RefreshAll();
        return root;
    }

    private static Control WithDock(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }
    private static Control WithCol(Control c, int col) { Grid.SetColumn(c, col); return c; }
}

internal static class AutoGainExt
{
    public static TextBlock WithMono(this TextBlock t) { t.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return t; }
}
