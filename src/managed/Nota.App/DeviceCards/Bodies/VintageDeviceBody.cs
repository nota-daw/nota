// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Vintage (device kind 8) body: a MODE selector
// (Vinyl / Cassette / Reel / VHS / Tube / Analog), a CHARACTER knob group (Drive/Tone/
// Mix/Output) and a WEAR group (Wow/Flutter/Noise/Crackle/Wear, teal), beside a live
// character viz (saturation transfer curve + grain speckle + bandwidth bar). All
// controls are the device's generic params → automation + persist for free.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class VintageDeviceBody : IDeviceBody
{
    // Param indices — must match Vintage.h.
    private const int Mode = 0, Drive = 1, Tone = 2, Wow = 3, Flutter = 4, Noise = 5, Crackle = 6, Wear = 7, Mix = 8, Output = 9, OS = 10;
    private static readonly string[] OsNames = { "Off", "2×", "4×", "8×" };
    private static readonly string[] Modes = { "Vinyl", "Cassette", "Reel", "VHS", "Tube", "Analog" };

    public double Width => 700;   // the almanac device format: 700 × 260; the viz column takes the slack

    public string? Subtitle => "CHARACTER";   // the processing type, shown as the header badge

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void SetP(int p, float v) => engine.DeviceSetParam(track, di, p, v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");

        var viz = new VintageViz { VerticalAlignment = VerticalAlignment.Stretch, MinWidth = 200 };
        void SyncViz()
        {
            int m = Math.Clamp((int)Math.Round(P(Mode) * 5), 0, 5);
            float grain = Math.Clamp(P(Noise) * 0.6f + P(Crackle) * 0.5f, 0f, 1f);
            viz.Set(m, P(Drive), P(Tone), grain, P(Wear));
        }
        ctx.AddDeviceRefresher(viz.Tick);

        // ---- formatters ----
        static string PctF(double v) => $"{v * 100:0}\u2009%";
        static string Bip(double v) => $"{(v - 0.5) * 200:+0;−0;0}\u2009%";
        static string GainF(double v) => $"{(v - 0.5) * 24:+0.0;−0.0;0.0}\u2009dB";

        // ---- gauge-knob cell (shared Knob + value + caption) ----
        Control Cell(string name, int p, Func<double, string> fmt, IBrush? arc = null, double size = 40)
        {
            var value = new TextBlock { Text = fmt(P(p)), FontSize = 9, Foreground = TextPrimary };
            value.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
            knob.ValueChanged += v => { SetP(p, (float)v); value.Text = fmt(v); SyncViz(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            MidiLearn.Bind(knob, MidiTarget.DeviceParam(track, di, p), name);
            ctx.AddDeviceRefresher(() => { if (!knob.Dragging) { float c = P(p); if (Math.Abs(c - knob.Value) > 1e-3) { knob.Value = c; value.Text = fmt(c); } } });
            return KnobCell(name, knob, value, size + 18);
        }
        static StackPanel KnobRow(params Control[] cs)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var c in cs) sp.Children.Add(c);
            return sp;
        }
        Border Panel(Control body, bool teal = false) => new()
        {
            Background = Card2, BorderBrush = teal ? Teal : BorderDef,
            BorderThickness = teal ? new Thickness(2, 1, 1, 1) : new Thickness(1),
            CornerRadius = NotaRadius.Panel, Padding = new Thickness(9, 8), Child = body,
        };
        static TextBlock Head(string t, IBrush? c = null) => new() { Text = t, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, Margin = new Thickness(0, 0, 0, 7) };

        // ---- MODE selector: 6 chips in two rows, live-synced ----
        var chips = new Border[Modes.Length];
        void SyncMode() { int cur = Math.Clamp((int)Math.Round(P(Mode) * 5), 0, 5); for (int i = 0; i < chips.Length; i++) { bool on = i == cur; chips[i].Background = on ? Brass : Card2; ((TextBlock)chips[i].Child!).Foreground = on ? OnAccent : TextSecondary; } }
        var modeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 3, RowSpacing = 3 };
        for (int i = 0; i < Modes.Length; i++)
        {
            int vi = i;
            var chip = new Border
            {
                Background = Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control,
                Padding = new Thickness(0, 3), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = Modes[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center },
            };
            chip.PointerPressed += (_, e) => { e.Handled = true; SetP(Mode, vi / 5f); SyncMode(); SyncViz(); };
            chips[i] = chip;
            Grid.SetColumn(chip, i % 3); Grid.SetRow(chip, i / 3); modeGrid.Children.Add(chip);
        }
        SyncMode();
        ctx.AddDeviceRefresher(SyncMode);
        MidiLearn.Bind(modeGrid, MidiTarget.DeviceParam(track, di, Mode), engine.DeviceParamName(track, di, Mode));

        // ---- OVERSAMPLE chips (normalized param 0..1 → Off/2×/4×/8×) ----
        var osChips = new Border[4];
        void SyncOs() { int cur = Math.Clamp((int)Math.Round(P(OS) * 3), 0, 3); for (int i = 0; i < 4; i++) { bool on = i == cur; osChips[i].Background = on ? Brass : Card2; ((TextBlock)osChips[i].Child!).Foreground = on ? OnAccent : TextSecondary; } }
        var osRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (int i = 0; i < 4; i++)
        {
            int vi = i;
            var chip = new Border { Background = Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control, Padding = new Thickness(7, 2), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = OsNames[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center } };
            chip.PointerPressed += (_, e) => { e.Handled = true; SetP(OS, vi / 3f); SyncOs(); };
            osChips[i] = chip; osRow.Children.Add(chip);
        }
        SyncOs();
        ctx.AddDeviceRefresher(SyncOs);
        MidiLearn.Bind(osRow, MidiTarget.DeviceParam(track, di, OS), engine.DeviceParamName(track, di, OS));
        var osStrip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "OVERSAMPLE", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center }, osRow } };

        // ---- CHARACTER panel: mode + drive/tone/mix/output ----
        var charPanel = Panel(new StackPanel { Spacing = 8, Children =
        {
            Head("CHARACTER"),
            modeGrid,
            KnobRow(Cell("Drive", Drive, PctF), Cell("Tone", Tone, Bip), Cell("Mix", Mix, PctF, Teal), Cell("Output", Output, GainF)),
            osStrip,
        } });

        // ---- WEAR panel: wow/flutter/noise/crackle/wear (teal knobs = degradation) ----
        var wearPanel = Panel(new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children =
        {
            Head("WEAR"),
            KnobRow(Cell("Wow", Wow, PctF, Teal), Cell("Flutter", Flutter, PctF, Teal), Cell("Noise", Noise, PctF, Teal)),
            KnobRow(Cell("Crackle", Crackle, PctF, Teal), Cell("Wear", Wear, PctF, Teal)),
        } });

        // ---- SHAPE panel: the character viz as its own island ----
        var vizPanel = Panel(viz);
        vizPanel.VerticalAlignment = VerticalAlignment.Stretch;

        SyncViz();
        ctx.AddDeviceRefresher(SyncViz);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6 };
        grid.Children.Add(charPanel);
        Grid.SetColumn(wearPanel, 1); grid.Children.Add(wearPanel);
        Grid.SetColumn(vizPanel, 2); grid.Children.Add(vizPanel);
        return grid;
    }
}
