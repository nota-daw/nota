// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the built-in Amplifier (guitar amp, kind 6) body, mockup 2m: a full
// 700×260 shell. Model, Gain and Mix ride the LIVE strip; the body shows the waveshaper
// TRANSFER curve + HARMONICS spectrum (left), the TONE STACK with its own EQ response and the
// four tone knobs (middle), and a CABINET/OUTPUT rail (cab type · mic · axis · gate · output).
// Was "Nota Amp". The transfer / harmonics / EQ maths mirror Amp.h so picture == sound.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class AmpDeviceBody : IDeviceBody
{
    // Param indices — mirror Amp.h.
    private const int PModel = 0, PGain = 1, PBass = 2, PMiddle = 3, PTreble = 4, PPresence = 5,
                      POutput = 6, PMix = 7, PCabOn = 8, PCabType = 9, PMic = 10, PAxis = 11, PGate = 12, POs = 13;
    private static readonly string[] Models = { "Clean", "Boost", "Blues", "Rock", "Lead", "Heavy", "Bass" };
    private static readonly string[] OsNames = { "Off", "2×", "4×", "8×" };
    private static readonly float[] DriveMul = { 1.5f, 3.0f, 6.0f, 12.0f, 22.0f, 40.0f, 4.0f };
    private static readonly int[] Stages = { 1, 1, 1, 2, 2, 3, 1 };
    private static readonly float[] Bias = { 0.00f, 0.05f, 0.10f, 0.15f, 0.20f, 0.28f, 0.00f };
    private static readonly string[] Cabs = { "Match", "1×12", "2×12", "4×12", "1×15" };
    private static readonly string[] Mics = { "Dyn", "Cond", "Rib" };

    private static readonly IBrush HdrBg = NotaPalette.SurfaceCard;
    private static readonly IBrush Rail = NotaPalette.SurfaceInset;
    private static readonly IBrush Bd = NotaPalette.BorderDefault;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush Amber = NotaPalette.Accent;
    private static readonly IBrush AmberLit = NotaPalette.AccentBright;
    private static readonly IBrush Txt = NotaPalette.TextPrimary;
    private static readonly IBrush Muted = NotaPalette.TextTertiary;
    private static readonly IBrush Sub = NotaPalette.TextSecondary;
    private static readonly IBrush Card = NotaPalette.SurfaceRaised;
    private static readonly IBrush Ink = NotaPalette.TextOnAccent;
    private static readonly IBrush AmberSubtle = NotaPalette.Wash(NotaPalette.Accent, 0x28);

    public double Width => 700;

    public string? Subtitle => "AMP SIM";   // the processing type, shown as the header badge
    public bool FullBleed => true;

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float G(int p) => engine.DeviceGetParam(track, di, p);
        void S(int p, double v) => engine.DeviceSetParam(track, di, p, (float)v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        int Gi(int p) => (int)Math.Round(G(p));

        static TextBlock Cap(string t, IBrush? c = null, double fs = 8) => new() { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? Muted, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }

        // ---- viz ----
        var transfer = new AmpCurve { VerticalAlignment = VerticalAlignment.Stretch, MinHeight = 60 };
        var harm = new AmpHarmonics { Height = 54 };
        var tone = new AmpToneCurve { Height = 56 };
        void Sync()
        {
            int m = Math.Clamp(Gi(PModel), 0, 6);
            float g01 = Math.Clamp(G(PGain) / 10f, 0f, 1f);
            float drive = DriveMul[m] * (0.35f + g01 * g01 * 3f);
            transfer.Set(drive, Stages[m], Bias[m], $"{Models[m]} · {G(PGain):0.0}");
            harm.Set(drive, Stages[m], Bias[m]);
            tone.Set(G(PBass), G(PMiddle), G(PTreble), G(PPresence));
        }
        ctx.AddDeviceRefresher(Sync);

        // ---- generic controls ----
        Control MiniSlider(string label, int p, double min, double max, Func<double, string> fmt, double w)
        {
            var row = DeviceCardKit.SliderRow(label, () => (G(p) - min) / (max - min), n => { S(p, min + n * (max - min)); Sync(); }, () => fmt(G(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), trackWidth: w, valueWidth: 28);
            ctx.AddDeviceRefresher(sync);
            MidiLearn.Bind(row, MidiTarget.DeviceParam(track, di, p), label);
            return row;
        }
        // Discrete chip group over a param (0..n-1).
        Control Chips(int p, string[] names, double fs = 9, double padX = 5, double minW = 0)
        {
            var seg = DeviceCardKit.Segments(names, () => Math.Clamp(Gi(p), 0, names.Length - 1), iv => { S(p, iv); Sync(); }, out var sync, minSegWidth: minW);
            ctx.AddDeviceRefresher(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }
        // Oversampling quality chips (normalized param 0..1 → Off/2×/4×/8×).
        Control OsChips()
        {
            var seg = DeviceCardKit.Segments(OsNames, () => Math.Clamp((int)Math.Round(G(POs) * 3), 0, 3), i => S(POs, i / 3f), out var sync, minSegWidth: 22);
            ctx.AddDeviceRefresher(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, POs), engine.DeviceParamName(track, di, POs));
            return seg;
        }
        // Segmented (inset frame) over a param.
        Control Seg(int p, string[] names)
        {
            var seg = DeviceCardKit.Segments(names, () => Math.Clamp(Gi(p), 0, names.Length - 1), iv => S(p, iv), out var sync, fill: true);
            ctx.AddDeviceRefresher(sync);
            MidiLearn.Bind(seg, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));
            return seg;
        }
        Control MiniRow(string label, int p, Func<double, string> fmt)
        {
            var row = DeviceCardKit.SliderRow(label, () => G(p), n => S(p, n), () => fmt(G(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), labelWidth: 26, valueWidth: 34);
            ctx.AddDeviceRefresher(sync);
            return row;
        }
        Control Toggle(int p)
        {
            var sw = DeviceCardKit.Switch("", () => G(p) >= 0.5f, () => { S(p, G(p) >= 0.5f ? 0 : 1); Sync(); }, out var syncT);
            ctx.AddDeviceRefresher(syncT);
            return sw;
        }

        // ---- LIVE strip ----
        var liveL = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { Chips(PModel, Models, 9, 5, 34), MiniSlider("GAIN", PGain, 0, 10, v => $"{v:0.0}", 62) } };
        var mix = MiniSlider("MIX", PMix, 0, 1, v => $"{v * 100:0}\u2009%", 44); mix.HorizontalAlignment = HorizontalAlignment.Right;
        var liveGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(mix, 1); liveGrid.Children.Add(liveL); liveGrid.Children.Add(mix);
        var liveStrip = new Border { Height = 34, Background = HdrBg, BorderBrush = Bd, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 0), Child = liveGrid };

        // ---- left column: transfer + harmonics ----
        var leftCol = new DockPanel { Width = 250, LastChildFill = true };
        DockPanel.SetDock(harm, Dock.Bottom); harm.Margin = new Thickness(0, 5, 0, 0);
        leftCol.Children.Add(harm); leftCol.Children.Add(transfer);
        var leftPanel = new Border { Width = 250, Padding = new Thickness(8, 7), Child = leftCol };

        // ---- middle: tone stack ----
        var toneHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { Cap("TONE STACK"), Cap("pre-cab, model-dependent", Muted) } };
        var toneKnobs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (int p in new[] { PBass, PMiddle, PTreble, PPresence }) toneKnobs.Children.Add(DeviceParamControls.ParamRow(ctx, di, p));
        var toneDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toneHead, Dock.Top); DockPanel.SetDock(tone, Dock.Top); tone.Margin = new Thickness(0, 5, 0, 0);
        toneDock.Children.Add(toneHead); toneDock.Children.Add(tone); toneDock.Children.Add(toneKnobs);
        var tonePanel = new Border { Background = Rail, BorderBrush = Bd, BorderThickness = new Thickness(1, 0, 1, 0), Padding = new Thickness(9, 6), Child = toneDock };

        // ---- right rail: cabinet + output ----
        var cabHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Toggle(PCabOn), Cap("CABINET") } };
        // Lay the 5 cab chips as a 3×2 grid so they fit the 124px rail.
        var cabGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 2, RowSpacing = 2 };
        {
            var arr = new Border[Cabs.Length];
            void Hi() { int cur = Math.Clamp(Gi(PCabType), 0, Cabs.Length - 1); for (int i = 0; i < Cabs.Length; i++) { bool on = i == cur; arr[i].Background = on ? Amber : Card; arr[i].BorderBrush = on ? Amber : Bd; ((TextBlock)arr[i].Child!).Foreground = on ? NotaPalette.TextOnAccent : Sub; } }
            for (int i = 0; i < Cabs.Length; i++) { int iv = i; var c = new Border { CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Padding = new Thickness(0, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = new TextBlock { Text = Cabs[i], FontSize = 8, FontWeight = FontWeight.SemiBold, Foreground = Sub, HorizontalAlignment = HorizontalAlignment.Center } }; c.PointerPressed += (_, _) => { S(PCabType, iv); Hi(); Sync(); }; arr[i] = c; Grid.SetColumn(c, i % 3); Grid.SetRow(c, i / 3); cabGrid.Children.Add(c); }
            ctx.AddDeviceRefresher(Hi); Hi();
        }
        var micRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var micSeg = Seg(PMic, Mics); Grid.SetColumn(micSeg, 1); micRow.Children.Add(Cap("MIC")); micRow.Children.Add(micSeg);
        var outKnob = DeviceParamControls.ParamRow(ctx, di, POutput);
        var railStack = new StackPanel { Spacing = 5, Children =
        {
            cabHead, cabGrid, micRow,
            MiniRow("AXIS", PAxis, v => v <= 0.02 ? "on" : $"off {v * 100:0}"),
            new Border { Height = 1, Background = Card },
            MiniRow("GATE", PGate, v => v <= 0.001 ? "off" : $"−{75 - v * 55:0.0}\u2009dB"),
            new Border { Height = 1, Background = Card },
            Cap("OVERSAMPLE"), OsChips(),
        } };
        var railDock = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Stretch };
        DockPanel.SetDock(railStack, Dock.Top);
        var outWrap = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Children = { outKnob } };
        DockPanel.SetDock(outWrap, Dock.Bottom);
        railDock.Children.Add(railStack); railDock.Children.Add(outWrap);
        var rightRail = new Border { Width = 124, Background = Rail, Padding = new Thickness(8, 6), Child = railDock };

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(leftPanel, Dock.Left); DockPanel.SetDock(rightRail, Dock.Right);
        body.Children.Add(leftPanel); body.Children.Add(rightRail); body.Children.Add(tonePanel);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp };
        DockPanel.SetDock(liveStrip, Dock.Top); root.Children.Add(liveStrip); root.Children.Add(body);

        Sync();
        return root;
    }
}
