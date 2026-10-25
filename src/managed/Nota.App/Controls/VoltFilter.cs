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

    // Interactive filter response for Nota Volt (and Bass, Aurora, Operator): drag anywhere to
    // set cutoff (X) and resonance (Y) of the targeted filter, recording gestures. Drawn like
    // Auto Filter's curve: the real 2-pole response by type (LP / HP / BP / Notch / Morph) over a
    // log-frequency grid, the handle on the curve and the cutoff top-right.
    internal sealed class VoltFilter : Control, IMidiLearnRegions
    {
        private readonly IAudioEngine _e;
        private readonly int _t;
        private (int i, string id) _freq, _reso;
        private int _type;
        private string _title = "FILTER 1";
        private bool _drag;

        /// <summary>The instrument's own cutoff map, lo → hi in Hz (Volt's is the default).
        /// The readout and the frequency scale both come from it, so the graph says what the
        /// engine actually does.</summary>
        public (double Lo, double Hi) FreqRange { get; set; } = (20.0, 18000.0);

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
        { if (_drag) { _drag = false; InvalidateVisual(); Gest(false); e.Pointer.Capture(null); } }

        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        { if (_drag) { _drag = false; InvalidateVisual(); Gest(false); } base.OnPointerCaptureLost(e); }

        private void Gest(bool begin)
        {
            void G1((int i, string id) p) { if (p.i < 0) return; if (begin) _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id); else _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id); }
            G1(_freq); G1(_reso);
        }
        private const double DbTop = 18, DbBot = -36;

        /// <summary>24 dB/oct: the response is drawn squared (two poles more), like Auto Filter's.</summary>
        public bool Slope24 { get; set; }

        /// <summary>Write the cutoff top-right; off where the card's own header above already shows it (Operator).</summary>
        public bool ShowCutoff { get; set; } = true;

        // The plot spans the window's width, so the cutoff's x is linear in its normalised value.
        private (double x0, double x1, double top, double bot) Geo()
        { return (0, Bounds.Width, NotaGraph.PlotTop - 6, Bounds.Height - NotaGraph.PlotFloor); }

        private void Apply(Point p)
        {
            var (x0, x1, top, bot) = Geo();
            double f = Math.Clamp((p.X - x0) / Math.Max(1, x1 - x0), 0, 1);
            double r = Math.Clamp(1 - (p.Y - top) / Math.Max(1, bot - top), 0, 1);
            if (_freq.i >= 0) _e.PluginParamSet(_t, -1, _freq.i, (float)f);
            if (_reso.i >= 0) _e.PluginParamSet(_t, -1, _reso.i, (float)r);
            InvalidateVisual();
        }

        // The magnitude of a 2-pole state-variable filter at f for cutoff fc (the same model as
        // Auto Filter's curve), by type; Morph (Aurora) is a bell that resonance raises.
        private double MagDb(double f, double fc, double res)
        {
            // The engines' resonance mapping (VoltSynth::resoToK, Bass, Operator): k = 2 − 1.9·res, q = 1/k.
            double q = 1.0 / Math.Clamp(2.0 - 1.9 * Math.Clamp(res, 0.0, 1.0), 0.05, 2.0), w = f / fc, w2 = w * w;
            double den = Math.Max(1e-9, Math.Sqrt((1 - w2) * (1 - w2) + w / q * (w / q)));
            double m = _type switch
            {
                1 => w2 / den,
                2 => w / q / den,
                3 => Math.Abs(1 - w2) / den,
                4 => 1 + res * 3 * (w / q / den),
                _ => 1 / den,
            };
            if (Slope24 && _type != 4) m *= m;
            return 20 * Math.Log10(Math.Max(m, 1e-5));
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0) return;
            var frame = new Rect(0, 0, w, h);
            NotaGraph.Window(ctx, frame);
            using var clip = ctx.PushClip(new RoundedRect(frame.Deflate(1), NotaGraph.Radius));
            var (x0, x1, top, bot) = Geo();
            double lo = FreqRange.Lo, hi = FreqRange.Hi, span = Math.Log(hi / lo);
            double X(double hz) => x0 + Math.Log(hz / lo) / span * (x1 - x0);
            double Y(double db) => top + Math.Clamp((DbTop - db) / (DbTop - DbBot), 0, 1) * (bot - top);

            // Decades across, 0 dB and −18 dB down: the same grid as Auto Filter.
            foreach (var f in new[] { 100.0, 1000, 10000 }) if (f > lo && f < hi) ctx.DrawLine(NotaGraph.GridPen, new Point(X(f), 0), new Point(X(f), h));
            foreach (var db in new[] { 0.0, -18 }) ctx.DrawLine(NotaGraph.GridPen, new Point(0, Y(db)), new Point(w, Y(db)));

            double cut = G(_freq), res = G(_reso);
            double fc = lo * Math.Pow(hi / lo, cut);
            int n = (int)Math.Clamp(w, 24, 400);
            var pts = new Point[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / (n - 1), f = lo * Math.Pow(hi / lo, t);
                pts[i] = new Point(x0 + t * (x1 - x0), Y(MagDb(f, fc, res)));
            }
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(pts[0], false);
                for (int i = 1; i < n; i++) g.LineTo(pts[i]);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, NotaGraph.PrimaryPen, line);

            // The handle sits on the drawn curve at the cutoff (read off the line, so a notch's
            // bottom, which only the exact cutoff reaches, doesn't pull it below the curve); a
            // faint rule marks the cutoff while dragged.
            double cx = x0 + cut * (x1 - x0), pos = cut * (n - 1);
            int i0 = Math.Clamp((int)Math.Floor(pos), 0, n - 2);
            double cy = pts[i0].Y + (pts[i0 + 1].Y - pts[i0].Y) * (pos - i0);
            if (_drag) ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.Accent, 0x50), 1), new Point(cx, 0), new Point(cx, h));
            NotaGraph.Node(ctx, new Point(cx, cy), active: true);

            NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopLeft, "+18");
            NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, NotaNum.Hz(lo));
            NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, NotaNum.Hz(hi));
            if (ShowCutoff) NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopRight, NotaNum.Hz(fc), _drag ? NotaPalette.AccentBright : NotaPalette.TextSecondary);
        }
    }
