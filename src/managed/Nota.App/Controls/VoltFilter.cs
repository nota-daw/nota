// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

    // Interactive filter response for Nota Volt (and Bass, Aurora, Operator): drag anywhere to
    // set cutoff (X) and resonance (Y) of the targeted filter, recording gestures. The real
    // 2-pole response by type (LP / HP / BP / Notch / Morph) on the shared filter graph
    // (FilterGraphBase: spectrum, cutoff on the axis, hover marker). ⌥-click types the
    // cutoff, then (Tab) the resonance; double-click resets both (specs/035-typed-value-entry).
    internal sealed class VoltFilter : FilterGraphBase, IMidiLearnRegions, IValueEntryHost
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
        protected override (double Lo, double Hi) Span => FreqRange;
        protected override double CutoffHz => FreqRange.Lo * Math.Pow(FreqRange.Hi / FreqRange.Lo, G(_freq));
        protected override double ResponseDb(double hz) => MagDb(hz, CutoffHz, G(_reso));

        public VoltFilter(IAudioEngine e, int t) { _e = e; _t = t; MinWidth = 150; MinHeight = 96; ValueEntry.AttachHost(this); }
        public void Target(string title, (int, string) freq, (int, string) reso, int type)
        {
            if (title != _title || freq != _freq || reso != _reso) _fields = null;
            _title = title; _freq = freq; _reso = reso; _type = type; InvalidateVisual();
        }

        // ---- typed entry: the whole pad is the handle (a press anywhere moves it) ----
        private ValueField[]? _fields;
        public IReadOnlyList<ValueField> AllFields => _fields ??= new[]
        {
            Field(_freq, "Cutoff", v => NotaNum.Hz(FreqRange.Lo * Math.Pow(FreqRange.Hi / FreqRange.Lo, v))),
            Field(_reso, "Reso", NotaNum.Pct),
        }.OfType<ValueField>().ToArray();
        private ValueField? Field((int i, string id) p, string name, Func<double, string> text) => p.i < 0 ? null
            : ValueEntry.InstrumentParam(_e, _t, p.i, p.id, $"{NotaText.Label(_title)} · {name}", text, InvalidateVisual);
        public IReadOnlyList<ValueField>? FieldsAt(Point p) => AllFields.Count > 0 ? AllFields : null;
        public Rect AnchorOf(ValueField field)
        {
            var (x0, x1, top, _) = Geo();
            return ValueEntry.Near(new Point(x0 + G(_freq) * (x1 - x0), top), Bounds.Size);
        }
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
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click: the parameter menu
            _drag = Dragging = true; Gest(true); e.Pointer.Capture(this); Apply(e.GetPosition(this)); e.Handled = true; }
        protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag) Apply(e.GetPosition(this)); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        { if (_drag) { _drag = Dragging = false; InvalidateVisual(); Gest(false); e.Pointer.Capture(null); } }

        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        { if (_drag) { _drag = Dragging = false; InvalidateVisual(); Gest(false); } base.OnPointerCaptureLost(e); }

        private void Gest(bool begin)
        {
            void G1((int i, string id) p) { if (p.i < 0) return; if (begin) _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id); else _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id); }
            G1(_freq); G1(_reso);
        }
        private const double DbTop = 12, DbBot = -48;   // the +12 … −48 dB range the graph has always shown

        /// <summary>24 dB/oct: the response is drawn squared (two poles more), like Auto Filter's.</summary>
        public bool Slope24 { get; set; }

        /// <summary>Kept for the cards that set it; the cutoff now lives on the axis (#531).</summary>
        public bool ShowCutoff { get; set; } = true;

        // The plot spans the window's width, so the cutoff's x is linear in its normalised value.
        private (double x0, double x1, double top, double bot) Geo()
        { var r = PlotRect; return (r.X, r.Right, r.Y + 4, r.Bottom - 3); }

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
            DrawBack(ctx);
            var (lo, hi) = FreqRange;
            var (x0, x1, _, _) = Geo();
            double cut = G(_freq), res = G(_reso);
            double fc = lo * Math.Pow(hi / lo, cut);
            int n = (int)Math.Clamp(w, 24, 400);
            var pts = new Point[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / (n - 1), f = lo * Math.Pow(hi / lo, t);
                pts[i] = new Point(x0 + t * (x1 - x0), YOf(MagDb(f, fc, res)));
            }
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(pts[0], false);
                for (int i = 1; i < n; i++) g.LineTo(pts[i]);
                g.EndFigure(false);
            }
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(pts[0].X, PlotRect.Bottom), true);
                foreach (var p in pts) g.LineTo(p);
                g.LineTo(new Point(pts[^1].X, PlotRect.Bottom));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(NotaPalette.Wash(NotaPalette.Accent, 0x1E), null, area);
            ctx.DrawGeometry(null, new Pen(NotaPalette.AccentBright, NotaGraph.PrimaryWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);

            // The handle sits on the drawn curve at the cutoff (read off the line, so a notch's
            // bottom, which only the exact cutoff reaches, doesn't pull it below the curve).
            double cx = x0 + cut * (x1 - x0), pos = cut * (n - 1);
            int i0 = Math.Clamp((int)Math.Floor(pos), 0, n - 2);
            double cy = pts[i0].Y + (pts[i0 + 1].Y - pts[i0].Y) * (pos - i0);
            DrawFront(ctx, cy);
            if (_drag) ctx.DrawEllipse(NotaPalette.Wash(NotaPalette.Accent, 0x40), null, new Point(cx, cy), 8, 8);
            ctx.DrawEllipse(_drag ? NotaPalette.AccentBright : NotaGraph.Ground, new Pen(NotaPalette.AccentBright, 1.6), new Point(cx, cy), 3.8, 3.8);
        }
    }
