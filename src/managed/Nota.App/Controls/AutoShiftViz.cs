// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Nota Auto Shift pitch trace (mockup 3i): draws the device's two published histories —
// the detected pitch as a teal dotted cloud and the corrected output as a solid brass
// line — over labelled note lanes for a one-octave window that follows the corrected
// pitch, with the current target lane lit. A tuner needs to show both signals, so you
// can see how much correction the device is applying over the last few seconds.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AutoShiftViz : Control
{
    private static readonly IBrush Sunken = new SolidColorBrush(Color.Parse("#100F0D"));
    private static readonly IBrush BorderDef = new SolidColorBrush(Color.Parse("#221F1A"));
    private static readonly IBrush Brass = new SolidColorBrush(Color.Parse("#D8A03D"));
    private static readonly IBrush Teal = new SolidColorBrush(Color.Parse("#5B9E9C"));
    private static readonly IBrush TealDim = new SolidColorBrush(Color.FromArgb(0x8C, 0x5B, 0x9E, 0x9C));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6E6A5E"));
    private static readonly IBrush Faint = new SolidColorBrush(Color.Parse("#4A463D"));
    private static readonly IBrush LaneLine = new SolidColorBrush(Color.FromArgb(0x2A, 0x3A, 0x36, 0x2D));
    private static readonly IBrush WhiteLane = new SolidColorBrush(Color.FromArgb(0x44, 0x3A, 0x36, 0x2D));
    private static readonly IBrush TargetLine = new SolidColorBrush(Color.FromArgb(0x55, 0xF0, 0xC0, 0x60));
    private static readonly IBrush TargetLabel = new SolidColorBrush(Color.Parse("#F0C060"));
    private static readonly Typeface Mono = new(new FontFamily("Geist Mono, monospace"));
    private static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
    private static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private static bool IsWhite(int pc) => pc is 0 or 2 or 4 or 5 or 7 or 9 or 11;

    private float[] _det = Array.Empty<float>(), _corr = Array.Empty<float>();
    private int _n;
    private double _center = 60;   // smoothed window centre (MIDI)

    public AutoShiftViz() { MinWidth = 160; MinHeight = 80; }

    // det/corr are normalized MIDI (v*127), interleaved histories from the device.
    public void SetTraces(float[] det, float[] corr, int n)
    {
        _det = det; _corr = corr; _n = n;
        // Follow the latest voiced corrected note so the octave window scrolls smoothly.
        for (int k = n - 1; k >= 0; k--)
            if (corr.Length > k && corr[k] > 1e-4f) { _center += (corr[k] * 127.0 - _center) * 0.15; break; }
        InvalidateVisual();
    }

    public override void Render(DrawingContext g)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        g.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 6, 6);

        double pad = 6;
        double headY = pad + 1, plotTop = pad + 14, plotBot = h - pad - 11;
        double gutter = 22, x0 = pad + gutter, x1 = w - pad;

        // Header + legend.
        g.DrawText(new FormattedText("PITCH TRACE", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, 8, Muted), new Point(pad, headY));
        var det = new FormattedText("┄ detected", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Teal);
        var cor = new FormattedText("— corrected", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Brass);
        g.DrawText(cor, new Point(x1 - cor.Width, headY));
        g.DrawText(det, new Point(x1 - cor.Width - det.Width - 8, headY));

        // One-octave window centred on the (smoothed) corrected pitch.
        int lowMidi = (int)Math.Round(_center) - 6;
        double span = 12.0, ph = plotBot - plotTop;
        double Y(double midi) => plotBot - Math.Clamp((midi - lowMidi) / span, 0, 1) * ph;

        // Note lanes: white notes labelled, target lane lit.
        int targetNote = int.MinValue;
        for (int k = _n - 1; k >= 0; k--) if (_corr.Length > k && _corr[k] > 1e-4f) { targetNote = (int)Math.Round(_corr[k] * 127.0); break; }
        for (int m = lowMidi; m <= lowMidi + 12; m++)
        {
            int pc = ((m % 12) + 12) % 12;
            double y = Y(m);
            bool white = IsWhite(pc);
            g.DrawLine(new Pen(white ? WhiteLane : LaneLine, 1), new Point(x0, y), new Point(x1, y));
            if (white)
            {
                var lbl = new FormattedText(Names[pc] + (m / 12 - 1), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, m == targetNote ? TargetLabel : Faint);
                g.DrawText(lbl, new Point(pad - 1, y - lbl.Height / 2));
            }
        }
        if (targetNote != int.MinValue)
        {
            double ty = Y(targetNote);
            g.DrawLine(new Pen(TargetLine, 1), new Point(x0, ty), new Point(x1, ty));
        }

        // Traces. Detected = teal dots (raw), corrected = brass line + dots.
        int n = Math.Min(_n, Math.Min(_det.Length, _corr.Length));
        double X(int k) => x0 + (n <= 1 ? 0 : (double)k / (n - 1) * (x1 - x0));
        for (int k = 0; k < n; k++)
        {
            float dv = _det[k];
            if (dv > 1e-4f) g.DrawEllipse(TealDim, null, new Point(X(k), Y(dv * 127.0)), 1.4, 1.4);
        }
        var pen = new Pen(Brass, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        StreamGeometry? geo = null; StreamGeometryContext? gc = null; bool open = false;
        void Flush() { if (gc != null) { gc.Dispose(); g.DrawGeometry(null, pen, geo!); gc = null; geo = null; } open = false; }
        for (int k = 0; k < n; k++)
        {
            float cv = _corr[k];
            if (cv <= 1e-4f) { Flush(); continue; }
            var p = new Point(X(k), Y(cv * 127.0));
            if (!open) { geo = new StreamGeometry(); gc = geo.Open(); gc.BeginFigure(p, false); open = true; }
            else gc!.LineTo(p);
            if (k == n - 1) g.DrawEllipse(Brass, null, p, 2.4, 2.4);
        }
        Flush();

        // Time axis.
        g.DrawText(new FormattedText("−2 s", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Faint), new Point(x0, plotBot + 2));
        if (targetNote != int.MinValue)
        {
            int pc = ((targetNote % 12) + 12) % 12;
            var tt = new FormattedText(Names[pc] + (targetNote / 12 - 1) + " target", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, TargetLabel);
            g.DrawText(tt, new Point((x0 + x1) / 2 - tt.Width / 2, plotBot + 2));
        }
        var now = new FormattedText("now", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, Faint);
        g.DrawText(now, new Point(x1 - now.Width, plotBot + 2));
    }
}
