// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Ceiling (kind 14) body, mockup 3o on the shared
// shell (700×260, full-bleed). A LIVE strip (big GR number + hanging GR bar over
// Ceiling / Gain / Release, plus an Auto-release switch) sits above a body of the
// 4-second LEVEL history + GAIN REDUCTION lane | a CHARACTER + loudness rail
// (peak in/out · max GR · LUFS-S / LUFS-I · true peak, then teal Lookahead / Stereo
// link, a side-chain source picker and Reset peaks). Params are the device's raw
// units; the meters come from the packed device scope (dB).

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

internal sealed class CeilingDeviceBody : IDeviceBody
{
    private const int Ceil = 0, Gain = 1, Release = 2, AutoRelease = 3, Character = 4, Lookahead = 5, StereoLink = 6;
    // Packed scope layout (Ceiling::scopeRead).
    private const int S_InPeak = 0, S_OutPeak = 1, S_Gr = 2, S_LufsM = 3, S_LufsS = 4, S_LufsI = 5, S_TruePeak = 6;
    private static readonly (string n, string sub)[] Chars =
        { ("Clean", "transparent"), ("Punch", "lets transients through"), ("Glue", "slow, dense") };

    public double Width => 700;

    public string? Subtitle => "LIMITER";   // the processing type, shown as the header badge
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine; int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        float Mn(int p) => engine.DeviceParamMin(track, di, p);
        float Mx(int p) => engine.DeviceParamMax(track, di, p);
        void SetR(int p, float v) => engine.DeviceSetParam(track, di, p, v);
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p)), 0, n - 1);

        var readouts = new List<Action>();
        var scope = new float[8];

        // Rolling meter history for the two plots.
        var meters = new CeilingMeters();
        var levelPlot = new CeilingLevelPlot(meters) { VerticalAlignment = VerticalAlignment.Stretch };
        var grLane = new CeilingGrLane(meters) { Height = 46 };

        // Peak-holds are latched on the UI side so "Reset peaks" is trivial.
        float peakInHold = -120, peakOutHold = -120, maxGrHold = 0, truePeakHold = -120;

        Control Lbl(string t, double fs, IBrush c, bool bold = true) => new TextBlock { Text = t, FontSize = fs,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, Foreground = c, VerticalAlignment = VerticalAlignment.Center };

        // Horizontal slider (label · track · value) over a raw param; optional log scale.
        Control HSlider(int p, string name, Func<double, string> fmt, bool log, IBrush accent, double tw, double lw = 0)
        {
            double mn = Mn(p), mx = Mx(p);
            double lmn = log ? Math.Log(mn) : mn, lspan = (log ? Math.Log(mx) : mx) - lmn;
            double NormOf(double v) { double x = log ? Math.Log(Math.Clamp(v, mn, mx)) : Math.Clamp(v, mn, mx); return (x - lmn) / Math.Max(1e-9, lspan); }
            double ValOf(double n) { double x = lmn + Math.Clamp(n, 0, 1) * lspan; return log ? Math.Exp(x) : x; }
            var row = DeviceCardKit.SliderRow(name, () => NormOf(P(p)), n => { SetR(p, (float)ValOf(n)); }, () => fmt(P(p)), out var sync,
                begin: () => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""),
                end: () => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, ""),
                labelWidth: lw, trackWidth: tw, valueWidth: 40);
            readouts.Add(sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), name);
            return row;
        }
        Control Toggle(int p, string label, IBrush tint)
        {
            var b = new Border { CornerRadius = NotaRadius.Control, BorderThickness = new Thickness(1), Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold } };
            void Hi() { bool on = P(p) > 0.5f; b.Background = on ? NotaPalette.AccentSubtle : Sunken; b.BorderBrush = on ? NotaPalette.BorderBrass : BorderDef; ((TextBlock)b.Child!).Foreground = on ? NotaPalette.AccentHover : TextTertiary; }
            b.PointerPressed += (_, _) => { SetR(p, P(p) > 0.5f ? 0f : 1f); Hi(); };
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, p), label);
            readouts.Add(Hi); Hi(); return b;
        }

        string DbT(double v) => $"{v:0.0}\u2009dB";
        string Db1(double v) => $"{v:+0.0;−0.0;0.0}";
        string Ms(double v) => v < 10 ? $"{v:0.0}\u2009ms" : $"{v:0}\u2009ms";
        string Pct(double v) => $"{v:0}\u2009%";
        string MDb(double v) => v <= -119 ? "—" : $"{v:+0.0;−0.0;0.0}";

        // ---------- LIVE strip: GR readout + Ceiling/Gain/Release + auto-release ----------
        var grNum = new TextBlock { Text = "0.0", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Teal, VerticalAlignment = VerticalAlignment.Center };
        grNum.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var grFill = new Border { Height = 8, Background = Teal, CornerRadius = NotaRadius.Control, HorizontalAlignment = HorizontalAlignment.Left };
        var grBar = new Border { Width = 96, Height = 8, Background = Sunken, CornerRadius = NotaRadius.Control, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center, Child = grFill };
        var live = new Border { Height = 34, Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0), Children = {
                Lbl("GR", 8, TextTertiary), grNum, grBar,
                HSlider(Ceil, "CEILING", DbT, false, Brass, 58), HSlider(Gain, "GAIN", Db1, false, Brass, 52), HSlider(Release, "RELEASE", Ms, true, Brass, 52),
                Toggle(AutoRelease, "AUTO RELEASE", Teal) } } };

        // ---------- graphs column: level plot fills, GR lane docked below ----------
        var grLaneWrap = new Border { Height = 46, Margin = new Thickness(0, 5, 0, 0), Child = grLane };
        DockPanel.SetDock(grLaneWrap, Dock.Bottom);
        var graphs = new DockPanel { LastChildFill = true, Children = { grLaneWrap, levelPlot } };
        var graphsHost = new Border { Margin = new Thickness(8, 7, 0, 7), Child = graphs };

        // ---------- right rail: CHARACTER + loudness + advanced + sidechain ----------
        var charHi = new List<Action>();
        Control CharChip(int i)
        {
            var name = new TextBlock { Text = Chars[i].n, FontSize = 9, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var sub = new TextBlock { Text = Chars[i].sub, FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            var row = new DockPanel { LastChildFill = false, Children = { name } };
            DockPanel.SetDock(sub, Dock.Right); row.Children.Add(sub);
            var b = new Border { Height = 16, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Padding = new Thickness(7, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = row };
            void Hi() { bool on = Sel(Character, 3) == i; b.Background = on ? Brass : Brushes.Transparent; b.BorderBrush = on ? Brass : BorderDef; name.Foreground = on ? NotaPalette.TextOnAccent : TextSecondary; sub.Foreground = on ? NotaPalette.TextOnAccent : TextTertiary; }
            b.PointerPressed += (_, _) => { SetR(Character, i); foreach (var a in charHi) a(); };
            charHi.Add(Hi); readouts.Add(Hi); Hi();
            MidiLearn.Bind(b, MidiTarget.DeviceParam(track, di, Character), engine.DeviceParamName(track, di, Character));
            return b;
        }
        var charStack = new StackPanel { Spacing = 3, Children = { Lbl("CHARACTER", 8, TextTertiary) } };
        for (int i = 0; i < 3; i++) charStack.Children.Add(CharChip(i));

        // Loudness meta grid (2 columns, 3 rows).
        var metaGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 3 };
        TextBlock MetaCell(string label, int col, int row, out TextBlock value)
        {
            var l = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
            var v = new TextBlock { FontSize = 9, Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            v.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var d = new DockPanel { LastChildFill = false, Children = { l } };
            DockPanel.SetDock(v, Dock.Right); d.Children.Add(v);
            Grid.SetColumn(d, col); Grid.SetRow(d, row); metaGrid.Children.Add(d);
            value = v; return l;
        }
        MetaCell("PEAK IN", 0, 0, out var vPeakIn); MetaCell("PEAK OUT", 1, 0, out var vPeakOut);
        MetaCell("MAX GR", 0, 1, out var vMaxGr); MetaCell("LUFS-S", 1, 1, out var vLufsS);
        MetaCell("LUFS-I", 0, 2, out var vLufsI); MetaCell("TRUE PK", 1, 2, out var vTruePeak);

        // Advanced (teal — detector shaping).
        var advBox = new Border { BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Teal, Padding = new Thickness(6, 0, 0, 0),
            Child = new StackPanel { Spacing = 3, Children = {
                HSlider(Lookahead, "LOOK", Ms, false, Teal, 96, lw: 34), HSlider(StereoLink, "LINK", Pct, false, Teal, 96, lw: 34) } } };

        // Side-chain source picker (compact) + Reset peaks.
        var scIds = new List<int> { -1 };
        var scCombo = new ComboBox { FontSize = 9, Height = 20, MinWidth = 96 };
        scCombo.Items.Add("SC: Internal");
        for (int i = 0; i < engine.TrackCount; i++) { if (!engine.TryGetTrackInfo(i, out var ti) || ti.Id == track) continue; scIds.Add(ti.Id); scCombo.Items.Add($"SC: {i + 1} · {(ti.IsReturn ? "Ret" : ti.IsInstrument ? "Instr" : "Aud")}"); }
        scCombo.SelectedIndex = Math.Max(0, scIds.IndexOf(engine.DeviceSidechainSource(track, di)));
        scCombo.SelectionChanged += (_, _) => { int s = scCombo.SelectedIndex; if (s >= 0 && s < scIds.Count) { engine.SetDeviceSidechainSource(track, di, scIds[s]); ctx.NotifyChanged(); } };
        var resetBtn = new Border { CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), BorderBrush = BorderStrong, Background = Card2, Padding = new Thickness(8, 2), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Reset peaks", FontSize = 9, Foreground = TextSecondary } };
        resetBtn.PointerPressed += (_, _) => { peakInHold = -120; peakOutHold = -120; maxGrHold = 0; truePeakHold = -120; };
        var bottomRow = new DockPanel { LastChildFill = false, Children = { scCombo } };
        DockPanel.SetDock(resetBtn, Dock.Right); bottomRow.Children.Add(resetBtn);

        IBrush Div() => NotaPalette.SurfaceRaised;
        var rail = new Border { Width = 224, Background = NotaPalette.SurfaceInset, BorderBrush = BorderDef, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(8, 7),
            Child = new StackPanel { Spacing = 5, Children = {
                charStack,
                new Border { Height = 1, Background = Div() },
                metaGrid,
                new Border { Height = 1, Background = Div() },
                advBox,
                new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { bottomRow } } } } };
        DockPanel.SetDock(rail, Dock.Right);

        var body = new DockPanel { LastChildFill = true, Children = { rail, graphsHost } };

        // ---------- live refresh ----------
        void Refresh()
        {
            int cnt = engine.DeviceScope(track, di, scope, scope.Length);
            float grDb = 0, inDb = -120, outDb = -120, lufsS = -120, lufsI = -120, tpDb = -120;
            if (cnt >= 7) { inDb = scope[S_InPeak]; outDb = scope[S_OutPeak]; grDb = scope[S_Gr]; lufsS = scope[S_LufsS]; lufsI = scope[S_LufsI]; tpDb = scope[S_TruePeak]; }
            meters.Ceiling = P(Ceil);
            meters.Push(inDb, outDb, grDb);
            levelPlot.Tick(); grLane.Tick();

            grNum.Text = $"{-grDb:0.0}"; grNum.Foreground = grDb > 3.0f ? NotaPalette.Warning : Teal;
            grFill.Width = Math.Clamp(grDb / 6.0, 0, 1) * 96; grFill.Background = grDb > 3.0f ? NotaPalette.Warning : Teal;

            if (inDb > peakInHold) peakInHold = inDb;
            if (outDb > peakOutHold) peakOutHold = outDb;
            if (grDb > maxGrHold) maxGrHold = grDb;
            if (tpDb > truePeakHold) truePeakHold = tpDb;
            vPeakIn.Text = MDb(peakInHold); vPeakIn.Foreground = peakInHold > 0 ? NotaPalette.Danger : TextPrimary;
            vPeakOut.Text = MDb(peakOutHold);
            vMaxGr.Text = maxGrHold > 0.05f ? $"−{maxGrHold:0.0}" : "0.0"; vMaxGr.Foreground = maxGrHold > 3 ? NotaPalette.Warning : TextPrimary;
            vLufsS.Text = lufsS <= -119 ? "—" : $"{lufsS:0.0}";
            vLufsI.Text = lufsI <= -119 ? "—" : $"{lufsI:0.0}";
            float ceil = P(Ceil);
            vTruePeak.Text = MDb(truePeakHold);
            vTruePeak.Foreground = truePeakHold <= ceil + 0.05f ? Success : NotaPalette.Danger;

            foreach (var a in readouts) a();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();

        DockPanel.SetDock(live, Dock.Top);
        return new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp, Children = { live, body } };
    }
}
