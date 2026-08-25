// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Detail · Devices — built-in Nota Synth editor (instrument kind 0), mockup 2f. Left half:
// three labelled bands — OSCILLATOR (shape buttons), ENVELOPE (A/D/S/R) and FILTER & OUTPUT
// (cutoff/reso/gain), every knob reading a real unit (ms / dB / kHz). Right half: the live
// teal envelope + brass filter graphs (SynthViz). Pure re-skin — no DSP change.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class SynthInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "SUBTRACTIVE";

    // Waveform button glyphs (viewBox 0 0 22 16), matching the mockup shapes.
    private static readonly (string name, string path)[] Waves =
    {
        ("Saw",      "M1 13 L11 3 L11 13 L21 3 L21 13"),
        ("Square",   "M1 12 V4 H8 V12 H15 V4 H21"),
        ("Triangle", "M1 12 L6 4 L11 12 L16 4 L21 12"),
        ("Sine",     "M1 8 C4 1 8 1 11 8 C14 15 18 15 21 8"),
    };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Engine's perceptual map (Synth.h): lo * (hi/lo)^v.
    private static double ExpMap(float v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0f, 1f));
    private static string Secs(float v, double lo, double hi)
    { double s = ExpMap(v, lo, hi); return s < 1.0 ? $"{(s * 1000).ToString("0", Inv)} ms" : $"{s.ToString("0.00", Inv)} s"; }
    private static string Db(float v) => v <= 1e-4f ? "−∞ dB" : $"{(20.0 * Math.Log10(v)).ToString("0.0", Inv).Replace("-", "−")} dB";
    private static string KHz(float v)
    { double hz = ExpMap(v, 20, 18000); return hz >= 1000 ? $"{(hz / 1000).ToString("0.00", Inv)} kHz" : $"{hz.ToString("0", Inv)} Hz"; }

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;

        var adsr = new SynthViz(SynthViz.K.Adsr);
        var filter = new SynthViz(SynthViz.K.Filter);
        void Sync()
        {
            adsr.Set(G("attack"), G("decay"), G("sustain"), G("release"), G("cutoff"), G("resonance"));
            filter.Set(G("attack"), G("decay"), G("sustain"), G("release"), G("cutoff"), G("resonance"));
        }
        ctx.SetInstLiveViz(Sync);

        // ---- OSCILLATOR band: four shape buttons in one even row --------------
        var waveCells = new (Border box, Path glyph, TextBlock label)[4];
        void SyncWave()
        {
            int cur = idx.TryGetValue("wave", out var wi) ? (int)Math.Round(engine.PluginParamGet(track, -1, wi) * 3) : 0;
            for (int i = 0; i < 4; i++)
            {
                bool on = i == cur;
                waveCells[i].box.Background = on ? AccentSubtleB : Brushes.Transparent;
                waveCells[i].box.BorderBrush = on ? Brass : BorderStrong;
                var c = on ? AccentBright : TextTertiary;
                waveCells[i].glyph.Stroke = c; waveCells[i].label.Foreground = c;
            }
        }
        var waveRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 4 };
        for (int i = 0; i < 4; i++)
        {
            int wv = i;
            var glyph = new Path
            {
                Data = Geometry.Parse(Waves[i].path), Width = 18, Height = 13, Stretch = Stretch.Uniform,
                StrokeThickness = 1.7, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            };
            var label = new TextBlock { Text = Waves[i].name, FontSize = 9, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var inner = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 5,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Children = { glyph, label },
            };
            var box = new Border
            {
                Height = 28, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Cursor = new Cursor(StandardCursorType.Hand), Child = inner,
            };
            box.PointerPressed += (_, _) => { if (idx.TryGetValue("wave", out var wi)) { engine.PluginParamSet(track, -1, wi, wv / 3f); SyncWave(); } };
            waveCells[i] = (box, glyph, label);
            Grid.SetColumn(box, i); waveRow.Children.Add(box);
        }
        SyncWave();

        // ---- ENVELOPE + FILTER & OUTPUT bands ---------------------------------
        Control Knob(string id, string name, Func<float, string> fmt) =>
            InstrumentControls.InstKnob(ctx, idx, id, name, Sync, fmt, knobSize: 40, cellW: 80);

        var envRow = SpreadRow(
            Knob("attack", "Attack", v => Secs(v, 0.001, 2.0)),
            Knob("decay", "Decay", v => Secs(v, 0.002, 2.0)),
            Knob("sustain", "Sustain", Db),
            Knob("release", "Release", v => Secs(v, 0.002, 3.0)));

        var fltRow = SpreadRow(
            Knob("cutoff", "Cutoff", KHz),
            Knob("resonance", "Reso", v => v.ToString("0.00", CultureInfo.InvariantCulture)),
            Knob("gain", "Gain", Db));

        var left = new StackPanel { Width = 358, Spacing = 5, Children =
        {
            Band("OSCILLATOR", TextTertiary, waveRow, grow: false),
            Hairline(),
            Band("ENVELOPE", Teal, envRow, grow: true),
            Hairline(),
            Band("FILTER & OUTPUT", TextTertiary, fltRow, grow: true),
        } };

        // ---- graphs fill the remaining width, two equal rows ------------------
        adsr.VerticalAlignment = VerticalAlignment.Stretch;
        filter.VerticalAlignment = VerticalAlignment.Stretch;
        var graphs = new Grid { RowDefinitions = new RowDefinitions("*,*"), RowSpacing = 8, VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
        graphs.Children.Add(adsr);
        Grid.SetRow(filter, 1); graphs.Children.Add(filter);
        Sync();

        var graphHost = new Border
        {
            Background = NotaPalette.SurfaceRaised, BorderBrush = BorderDef, BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(10, 8), Child = graphs,
        };
        var bodyGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Stretch };
        var leftHost = new Border { Padding = new Thickness(10, 8, 10, 6), Child = left };
        bodyGrid.Children.Add(leftHost);
        Grid.SetColumn(graphHost, 1); bodyGrid.Children.Add(graphHost);
        return bodyGrid;
    }

    // A labelled band: an 8px caps caption over its content. grow → the row fills the
    // free vertical space (so the two knob bands centre their knobs evenly).
    private static Control Band(string caption, IBrush color, Control content, bool grow)
    {
        var lbl = new TextBlock { Text = caption, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = color, Margin = new Thickness(0, 0, 0, 4) };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(lbl);
        Grid.SetRow(content, 1); grid.Children.Add(content);
        if (grow) grid.VerticalAlignment = VerticalAlignment.Stretch;
        else grid.RowDefinitions = new RowDefinitions("Auto,Auto");
        return grid;
    }

    // A row of cells spread evenly across the band width, vertically centred.
    private static Control SpreadRow(params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", System.Linq.Enumerable.Repeat("*", cells.Length))), VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < cells.Length; i++) { cells[i].HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        return grid;
    }

    private static Control Hairline() => new Border { Height = 1, Background = BorderDef, Margin = new Thickness(0, 1) };
}
