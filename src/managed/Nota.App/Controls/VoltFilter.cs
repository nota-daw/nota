// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

    // Interactive filter response for Nota Volt: drag anywhere to set cutoff (X) and
    // resonance (Y) of the targeted filter, recording gestures. Curve shape follows the
    // filter type (LP/HP/BP/Notch).
    internal sealed class VoltFilter : Control, IMidiLearnRegions
    {
        private static readonly IBrush Sunken = NotaPalette.BgSunken;
        private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
        private static readonly IBrush AccentBright = NotaPalette.AccentBright;
        private static readonly IBrush GridB = NotaPalette.Wash(NotaPalette.BorderStrong, 0x50);
        private static readonly Typeface Face = new(FontFamily.Default);
        private readonly IAudioEngine _e;
        private readonly int _t;
        private (int i, string id) _freq, _reso;
        private int _type;
        private string _title = "FILTER 1";
        private bool _drag;

        public VoltFilter(IAudioEngine e, int t) { _e = e; _t = t; MinWidth = 150; MinHeight = 96; }
        public void Target(string title, (int, string) freq, (int, string) reso, int type)
        { _title = title; _freq = freq; _reso = reso; _type = type; InvalidateVisual(); }
        public void Refresh() => InvalidateVisual();
        private float G((int i, string id) p) => p.i >= 0 ? _e.PluginParamGet(_t, -1, p.i) : 0f;

        // MIDI-learn: cutoff (X) and resonance (Y) are only set by dragging this pad, so
        // expose them as left (Cutoff) / right (Reso) halves that each learn one param.
        public IReadOnlyList<(Rect rect, MidiTarget target, string name)> GetMidiLearnRegions()
        {
            var list = new List<(Rect, MidiTarget, string)>(2);
            var (x0, x1, top, bot) = Geo(); double w = (x1 - x0) / 2.0;
            if (_freq.i >= 0) list.Add((new Rect(x0, top, w, bot - top), MidiTarget.PluginParam(_t, -1, _freq.i), $"{_title} · Cutoff"));
            if (_reso.i >= 0) list.Add((new Rect(x0 + w, top, w, bot - top), MidiTarget.PluginParam(_t, -1, _reso.i), $"{_title} · Reso"));
            return list;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        { _drag = true; Gest(true); e.Pointer.Capture(this); Apply(e.GetPosition(this)); e.Handled = true; }
        protected override void OnPointerMoved(PointerEventArgs e) { if (_drag) Apply(e.GetPosition(this)); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        { if (_drag) { _drag = false; Gest(false); e.Pointer.Capture(null); } }

        private void Gest(bool begin)
        {
            void G1((int i, string id) p) { if (p.i < 0) return; if (begin) _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id); else _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id); }
            G1(_freq); G1(_reso);
        }
        private static readonly IBrush FillB = NotaPalette.Wash(NotaPalette.Accent, 0x18);
        private static readonly IBrush AxisB = NotaPalette.TextDisabled;
        private static readonly IBrush HandleB = NotaPalette.AccentBright;

        private (double x0, double x1, double top, double bot) Geo()
        { return (8, Bounds.Width - 8, 15, Bounds.Height - 14); }

        private void Apply(Point p)
        {
            var (x0, x1, top, bot) = Geo();
            double f = Math.Clamp((p.X - x0) / Math.Max(1, x1 - x0), 0, 1);
            double r = Math.Clamp(1 - (p.Y - top) / Math.Max(1, bot - top), 0, 1);
            if (_freq.i >= 0) _e.PluginParamSet(_t, -1, _freq.i, (float)f);
            if (_reso.i >= 0) _e.PluginParamSet(_t, -1, _reso.i, (float)r);
            InvalidateVisual();
        }

        // The filter's magnitude curve as a top polyline (left→right), per type.
        private System.Collections.Generic.List<Point> Curve(double x0, double x1, double top, double bot, double cx, double res)
        {
            double flatY = top + (bot - top) * 0.34;
            double peakY = Math.Max(top + 2, flatY - res * (flatY - top) * 0.92);
            double kx = Math.Max(x0, cx - 14), rx = Math.Min(x1, cx + 14);
            double mid = (peakY + bot) / 2 + (bot - peakY) * 0.15;
            var p = new System.Collections.Generic.List<Point>();
            switch (_type)
            {
                case 1: // HP: roll up to cutoff, then flat
                    p.Add(new(x0, bot)); p.Add(new((x0 + cx) / 2, mid)); p.Add(new(cx, peakY)); p.Add(new(rx, flatY)); p.Add(new(x1, flatY)); break;
                case 2: // BP: peak at cutoff
                    p.Add(new(x0, bot)); p.Add(new(kx, mid)); p.Add(new(cx, peakY)); p.Add(new(rx, mid)); p.Add(new(x1, bot)); break;
                case 3: // Notch: flat with a dip
                    p.Add(new(x0, flatY)); p.Add(new(kx, flatY)); p.Add(new(cx, bot)); p.Add(new(rx, flatY)); p.Add(new(x1, flatY)); break;
                default: // LP: flat to cutoff, resonance peak, roll off
                    p.Add(new(x0, flatY)); p.Add(new(kx, flatY)); p.Add(new(cx, peakY)); p.Add(new((cx + x1) / 2, mid)); p.Add(new(x1, bot)); break;
            }
            return p;
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height; if (w <= 0) return;
            ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 5, 5);
            var (x0, x1, top, bot) = Geo();
            // Grid: 3 horizontal (mid dashed) + 3 vertical.
            var gp = new Pen(GridB, 1);
            var dgp = new Pen(GridB, 1) { DashStyle = DashStyle.Dash };
            for (int i = 1; i <= 3; i++) { double gy = top + (bot - top) * i / 4.0; ctx.DrawLine(i == 2 ? dgp : gp, new Point(x0, gy), new Point(x1, gy)); }
            for (int i = 1; i <= 3; i++) { double gx = x0 + (x1 - x0) * i / 4.0; ctx.DrawLine(gp, new Point(gx, top), new Point(gx, bot)); }

            double cut = G(_freq), res = G(_reso);
            double cx = x0 + cut * (x1 - x0);
            var pts = Curve(x0, x1, top, bot, cx, res);

            // Filled area under the curve + the curve line.
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(new Point(pts[0].X, bot), true);
                foreach (var pt in pts) gc.LineTo(pt);
                gc.LineTo(new Point(pts[^1].X, bot));
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(FillB, null, geo);
            var pen = new Pen(AccentBright, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            for (int i = 1; i < pts.Count; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);

            // Cutoff marker line + draggable handle at the curve's cutoff point.
            ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0x40), 1), new Point(cx, top), new Point(cx, bot));
            double hy = _type == 3 ? bot : Math.Max(top + 2, top + (bot - top) * 0.34 - res * ((bot - top) * 0.34) * 0.92);
            ctx.DrawEllipse(HandleB, new Pen(Sunken, 2), new Point(cx, hy), 4.5, 4.5);

            // Axis labels: dB left, frequency scale along the bottom.
            void Lbl(string t, double x, double y, IBrush b) => ctx.DrawText(new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, b), new Point(x, y));
            Lbl("+12", x0 + 1, top - 1, AxisB);
            Lbl("−48 dB", x0 + 1, bot - 10, AxisB);
            string[] fq = { "20", "100", "1k", "10k", "20k" };
            for (int i = 0; i < 5; i++) { double lx = x0 + (x1 - x0) * i / 4.0; Lbl(fq[i], Math.Clamp(lx - 6, x0, x1 - 18), bot + 2, AxisB); }
            double hz = 20.0 * Math.Pow(900.0, cut);
            Lbl(hz >= 1000 ? $"● {hz / 1000.0:0.0}k" : $"● {hz:0} Hz", x1 - 52, top - 1, AccentBright);
        }
    }
