// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A compact editor for a Session-view AUDIO slot — the audio counterpart of the piano-roll
// slot editor. Session audio loops the whole sample over the slot's loop length, so the
// editable properties are the loop length and playback gain; the waveform is shown for
// reference. (Trim / warp / pitch aren't honoured by the session render, so they're omitted.)

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed class SessionAudioSlotEditor : UserControl
{
    private static readonly IBrush Card = NotaPalette.SurfaceCard;
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush Text2 = NotaPalette.TextSecondary;
    private static readonly IBrush Text3 = NotaPalette.TextTertiary;

    private readonly IAudioEngine _engine;
    private readonly int _trackId, _scene;

    public SessionAudioSlotEditor(IAudioEngine engine, int trackId, int scene, IBrush color)
    {
        _engine = engine; _trackId = trackId; _scene = scene;

        var title = new TextBlock
        {
            Text = $"Track {trackId} · Scene {scene + 1} · audio", FontSize = 11, FontWeight = FontWeight.SemiBold,
            Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center,
        };

        var wave = new WaveStrip(LoadPeaks(), color) { Height = 96 };
        var waveBox = new Border
        {
            Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Child = wave, ClipToBounds = true,
        };

        // Gain: fader maps 0..1 -> 0..2 linear (unity at 0.5), with a dB readout.
        float gain = _engine.SessionSlotGain(trackId, scene);
        var gainFader = new MiniFader(Math.Clamp(gain / 2.0, 0, 1), 1.0) { Default = 0.5, Width = 180 };
        var gainDb = new TextBlock { FontSize = 10, Classes = { "Mono" }, Foreground = Text3, Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        void ShowGain(double f) { double g = f * 2.0; gainDb.Text = g <= 1e-4 ? "-inf" : $"{AudioMath.LinToDb(g):+0.0;-0.0} dB"; }
        gainFader.ValueChanged += f => { _engine.SetSessionSlotGain(trackId, scene, (float)(f * 2.0)); ShowGain(f); };
        ShowGain(gainFader.Value);
        var gainRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { Label("GAIN"), gainFader, gainDb } };

        // Loop length: quick presets (the same set as the slot's right-click menu).
        var lenRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        lenRow.Children.Add(Label("LOOP"));
        double curLen = _engine.SessionSlotLength(trackId, scene);
        foreach (double beats in new[] { 1.0, 2.0, 4.0, 8.0, 16.0 })
        {
            double b = beats;
            var chip = LengthChip($"{b:0.#}", Math.Abs(curLen - b) < 1e-6);
            chip.PointerPressed += (_, _) =>
            {
                _engine.SetSessionSlotLength(trackId, scene, b);
                foreach (var c in lenRow.Children) if (c is Border cb && cb.Tag is double tb)
                    Paint(cb, Math.Abs(tb - b) < 1e-6);
            };
            lenRow.Children.Add(chip);
        }

        var controls = new StackPanel { Spacing = 10, Margin = new Thickness(12, 10, 12, 12), Children = { gainRow, lenRow } };

        var body = new DockPanel { LastChildFill = true };
        var head = new Border { Height = 34, Padding = new Thickness(12, 0), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = title };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(controls, Dock.Bottom);
        body.Children.Add(head);
        body.Children.Add(controls);
        var waveWrap = new Border { Padding = new Thickness(12, 12, 12, 0), Child = waveBox };
        body.Children.Add(waveWrap);

        Content = new Border { Background = Card, Child = body };
    }

    private static TextBlock Label(string t) => new() { Text = t, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = Text3, VerticalAlignment = VerticalAlignment.Center, Width = 34 };

    private static Border LengthChip(string t, bool active)
    {
        var b = new Border
        {
            Height = 22, MinWidth = 34, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 0), Tag = double.Parse(t, System.Globalization.CultureInfo.InvariantCulture),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = new TextBlock { Text = t + "b", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Paint(b, active);
        return b;
    }

    private static void Paint(Border chip, bool active)
    {
        chip.Background = active ? NotaPalette.AccentSubtle : NotaPalette.SurfaceRaised;
        chip.BorderBrush = active ? NotaPalette.Accent : NotaPalette.BorderStrong;
        if (chip.Child is TextBlock tb) tb.Foreground = active ? NotaPalette.AccentBright : Text2;
    }

    // Downsample the slot's sample into ~600 min/max buckets for the waveform strip.
    private float[] LoadPeaks()
    {
        if (!_engine.TryGetSessionAudioSlot(_trackId, _scene, out var slot) || slot.SampleId == 0)
            return Array.Empty<float>();
        float[] data = _engine.ReadSample(slot.SampleId);   // interleaved stereo
        long frames = data.Length / 2;
        if (frames <= 0) return Array.Empty<float>();
        const int buckets = 600;
        var peaks = new float[buckets * 2];   // min,max per bucket
        long per = Math.Max(1, frames / buckets);
        for (int i = 0; i < buckets; i++)
        {
            long start = i * per, end = Math.Min(frames, start + per);
            float mn = 0, mx = 0;
            for (long f = start; f < end; f++)
            {
                float s = 0.5f * (data[f * 2] + data[f * 2 + 1]);
                if (s < mn) mn = s; if (s > mx) mx = s;
            }
            peaks[i * 2] = mn; peaks[i * 2 + 1] = mx;
        }
        return peaks;
    }

    // Filled min/max waveform.
    private sealed class WaveStrip : Control
    {
        private readonly float[] _peaks;
        private readonly IBrush _fill;
        public WaveStrip(float[] peaks, IBrush fill) { _peaks = peaks; _fill = fill; }
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height, mid = h / 2;
            int n = _peaks.Length / 2;
            if (n == 0 || w <= 0) return;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(0, mid), true);
                for (int i = 0; i < n; i++) { double x = i / (double)(n - 1) * w; g.LineTo(new Point(x, mid - _peaks[i * 2 + 1] * mid)); }
                for (int i = n - 1; i >= 0; i--) { double x = i / (double)(n - 1) * w; g.LineTo(new Point(x, mid - _peaks[i * 2] * mid)); }
                g.EndFigure(true);
            }
            ctx.DrawGeometry(_fill, null, geo);
        }
    }
}
