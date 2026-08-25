// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Beat Repeat timeline (mockup 3h): the interval window drawn as 64 slots — empty
// slots are wells, the captured slice is full brass, and each repeat generation is one
// step dimmer so the decay is visible — with beat/bar ticks and the live playhead. It
// shows the effect (capture → repeats → fade), not a flat rectangle.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class BeatRepeatViz : Control
{
    private static readonly IBrush Sunken = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush BorderDef = new SolidColorBrush(Color.Parse("#221F1A"));
    private static readonly IBrush Well = new SolidColorBrush(Color.Parse("#1B1916"));
    private static readonly IBrush WellBd = new SolidColorBrush(Color.FromArgb(0x40, 0x3A, 0x36, 0x2D));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush Faint = new SolidColorBrush(Color.Parse("#4A463D"));
    private static readonly IBrush AmberLit = new SolidColorBrush(Color.Parse("#F0C060"));
    private static readonly IBrush Sub = new SolidColorBrush(Color.Parse("#A39D8F"));
    private static readonly IBrush BarLine = new SolidColorBrush(Color.Parse("#4A463D"));
    private static readonly Color Brass = Color.Parse("#D8A03D");
    private static readonly Typeface Mono = new(new FontFamily("Geist Mono, monospace"));
    private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    private float[] _slots = Array.Empty<float>();
    private int _n;
    private double _intervalBeats = 4, _bpm = 120, _phase;

    public BeatRepeatViz() { MinWidth = 200; MinHeight = 90; }

    public void Set(float[] slots, int n, double intervalBeats, double bpm, double phase)
    { _slots = slots; _n = n; _intervalBeats = Math.Max(0.25, intervalBeats); _bpm = bpm; _phase = Math.Clamp(phase, 0, 1); InvalidateVisual(); }

    private static IBrush BrassA(double v) => new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(0.30 + 0.70 * v, 0, 1)), Brass.R, Brass.G, Brass.B));

    public override void Render(DrawingContext g)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        g.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 6, 6);

        double pad = 6;
        double headY = pad, x0 = pad, x1 = w - pad;
        double top = pad + 13, bot = h - pad - 21, labY = bot + 1, ly = h - pad - 8;

        // Header: capture readout + BPM.
        g.DrawText(new FormattedText("TIMELINE", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, 8, Muted), new Point(x0, headY));
        int onsets = 0; for (int k = 1; k < _n; k++) if (_slots[k] > 1e-3f && _slots[k - 1] <= 1e-3f) onsets++;
        if (_n > 0 && _slots.Length > 0 && _slots[0] > 1e-3f) onsets++;
        double bars = _intervalBeats / 4.0;
        string capBar = bars >= 1 ? $"{bars:0.#} bar" : $"1/{4.0 / _intervalBeats:0}";
        g.DrawText(new FormattedText($"capture {capBar} → {onsets} rep", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, AmberLit), new Point(x0 + 62, headY));
        var bpm = new FormattedText($"{_bpm:0} BPM", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Muted);
        g.DrawText(bpm, new Point(x1 - bpm.Width, headY));

        // Slots.
        int n = Math.Max(1, _n);
        double sw = (x1 - x0) / n;
        for (int k = 0; k < n; k++)
        {
            float v = k < _slots.Length ? _slots[k] : 0f;
            var r = new Rect(x0 + k * sw + 0.5, top, Math.Max(1, sw - 1), bot - top);
            if (v <= 1e-3f) g.DrawRectangle(Well, new Pen(WellBd, 1), r, 1, 1);
            else g.DrawRectangle(BrassA(v), null, r, 1, 1);
        }

        // Beat / bar ticks + bar numbers.
        int beats = (int)Math.Round(_intervalBeats);
        for (int b = 1; b < beats; b++)
        {
            double x = x0 + (b / _intervalBeats) * (x1 - x0);
            g.DrawLine(new Pen(b % 4 == 0 ? BarLine : WellBd, 1), new Point(x, top), new Point(x, bot));
        }
        int barsN = Math.Max(1, (int)Math.Round(_intervalBeats / 4.0));
        for (int b = 0; b < barsN; b++)
            g.DrawText(new FormattedText((b + 1).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Faint), new Point(x0 + (b / (double)barsN) * (x1 - x0) + 1, labY));

        // Playhead.
        double px = x0 + _phase * (x1 - x0);
        g.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0xF0, 0xC0, 0x60)), 3), new Point(px, top), new Point(px, bot));
        g.DrawLine(new Pen(AmberLit, 1.5), new Point(px, top), new Point(px, bot));

        // Legend.
        void Swatch(double x, byte a, string label)
        {
            g.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, Brass.R, Brass.G, Brass.B)), null, new Rect(x, ly, 8, 8), 2, 2);
            g.DrawText(new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Sub), new Point(x + 11, ly - 1));
        }
        Swatch(x0, 255, "captured");
        Swatch(x0 + 78, 0x66, "repeat · decay");
        double phx = x0 + 192;
        g.DrawRectangle(AmberLit, null, new Rect(phx, ly, 2, 8));
        g.DrawText(new FormattedText("playhead", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Sub), new Point(phx + 6, ly - 1));
    }
}
