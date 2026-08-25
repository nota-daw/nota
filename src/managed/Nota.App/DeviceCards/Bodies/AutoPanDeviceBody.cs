// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Auto Pan (device kind 9) body: an LFO island
// (waveform chips + Rate/Shape) and a MOTION island (Amount/Phase/Mix, Phase reading
// tremolo↔pan in degrees) beside a live viz island (per-channel gain curves + a
// moving L—R pan dot). All controls are the device's generic params → automation +
// persist for free.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class AutoPanDeviceBody : IDeviceBody
{
    // Param indices — must match AutoPan.h.
    private const int Rate = 0, Amount = 1, Waveform = 2, Shape = 3, Phase = 4, Mix = 5;
    private static readonly string[] Waves = { "Sine", "Tri", "Saw", "Sqr", "S&H" };

    public double Width => 560;
    public bool AutoWidth => true;   // fixed-width islands → card sizes to content

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void SetP(int p, float v) => engine.DeviceSetParam(track, di, p, v);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));

        var viz = new AutoPanViz { VerticalAlignment = VerticalAlignment.Stretch, Width = 220 };
        void SyncViz()
        {
            int wv = Math.Clamp((int)Math.Round(P(Waveform) * 4), 0, 4);
            viz.Set(wv, P(Amount), P(Shape), P(Phase), engine.DeviceGainReduction(track, di));
        }
        ctx.AddDeviceRefresher(SyncViz);   // 60 Hz: also advances the live pan dot

        // ---- formatters ----
        static string PctF(double v) => $"{v * 100:0}%";
        string RateF(double v) => $"{Exp(v, 0.01, 40):0.00} Hz";
        static string DegF(double v) => $"{v * 360:0}°";

        // ---- gauge-knob cell ----
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
        Border Panel(Control body) => new()
        {
            Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 8), Child = body,
        };
        static TextBlock Head(string t) => new() { Text = t, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TextTertiary, Margin = new Thickness(0, 0, 0, 7) };

        // ---- Waveform chips (live-synced) ----
        var chips = new Border[Waves.Length];
        void SyncWave() { int cur = Math.Clamp((int)Math.Round(P(Waveform) * 4), 0, 4); for (int i = 0; i < chips.Length; i++) { bool on = i == cur; chips[i].Background = on ? Brass : Card2; ((TextBlock)chips[i].Child!).Foreground = on ? OnAccent : TextSecondary; } }
        var waveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (int i = 0; i < Waves.Length; i++)
        {
            int vi = i;
            var chip = new Border
            {
                Background = Card2, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = Waves[i], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextSecondary },
            };
            chip.PointerPressed += (_, e) => { e.Handled = true; SetP(Waveform, vi / 4f); SyncWave(); SyncViz(); };
            chips[i] = chip; waveRow.Children.Add(chip);
        }
        SyncWave();
        ctx.AddDeviceRefresher(SyncWave);
        MidiLearn.Bind(waveRow, MidiTarget.DeviceParam(track, di, Waveform), engine.DeviceParamName(track, di, Waveform));

        // ---- LFO island: waveform + Rate/Shape ----
        var lfoPanel = Panel(new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children =
        {
            Head("LFO"),
            waveRow,
            KnobRow(Cell("Rate", Rate, RateF), Cell("Shape", Shape, PctF, Teal)),
        } });

        // ---- MOTION island: Amount/Phase/Mix (Phase caption tremolo↔pan) ----
        var phaseCap = new TextBlock { FontSize = 8, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center };
        void SyncPhaseCap() { float d = P(Phase); phaseCap.Text = d < 0.03f ? "tremolo" : d > 0.47f && d < 0.53f ? "auto-pan" : "offset"; }
        SyncPhaseCap();
        ctx.AddDeviceRefresher(SyncPhaseCap);
        var motionPanel = Panel(new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children =
        {
            Head("MOTION"),
            KnobRow(Cell("Amount", Amount, PctF, Teal), Cell("Phase", Phase, DegF, Teal), Cell("Mix", Mix, PctF)),
            phaseCap,
        } });

        // ---- viz island ----
        var vizPanel = Panel(viz);
        vizPanel.VerticalAlignment = VerticalAlignment.Stretch;

        SyncViz();

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"), ColumnSpacing = 6, HorizontalAlignment = HorizontalAlignment.Left };
        grid.Children.Add(lfoPanel);
        Grid.SetColumn(motionPanel, 1); grid.Children.Add(motionPanel);
        Grid.SetColumn(vizPanel, 2); grid.Children.Add(vizPanel);
        return grid;
    }
}
