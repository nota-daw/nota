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

    // Interactive ADSR editor for Nota Volt: draw the envelope and drag its three
    // breakpoints (attack peak · decay/sustain · release end) to set the A/D/S/R params
    // directly, recording automation gestures. Retargetable between the amp / filter env.
    internal sealed class VoltEnv : Control, IMidiLearnRegions
    {
        private static readonly IBrush Sunken = NotaPalette.BgSunken;
        private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
        private static readonly IBrush AccentBright = NotaPalette.AccentBright;
        private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
        private static readonly IBrush GridB = NotaGraph.Grid;
        private static readonly Typeface Face = NotaFonts.Mono;
        private readonly IAudioEngine _e;
        private readonly int _t;
        private (int i, string id) _a, _d, _s, _r;
        private string _title = "AMP ENV";
        private int _drag = -1;   // 0 attack · 1 decay/sustain · 2 release

        // Curve accent (Nota Operator tints modulator envelopes teal); defaults to brass.
        public IBrush Accent { get; set; } = AccentBright;

        // Draw the title in the plot's corner. Off when the card already names the graph
        // in a header above it (Nota Bass).
        public bool ShowTitle { get; set; } = true;

        public VoltEnv(IAudioEngine e, int t) { _e = e; _t = t; MinWidth = 150; MinHeight = 96; }
        public void Target(string title, (int, string) a, (int, string) d, (int, string) s, (int, string) r)
        { _title = title; _a = a; _d = d; _s = s; _r = r; InvalidateVisual(); }
        public void Refresh() => InvalidateVisual();

        // MIDI-learn: the A/D/S/R params live only in this drag editor, so expose them as
        // four vertical strips (left→right) that each learn one stage.
        public IReadOnlyList<(Rect rect, MidiTarget target, string name)> GetMidiLearnRegions()
        {
            var list = new List<(Rect, MidiTarget, string)>(4);
            var (x0, x1, top, bot) = Geo(); double w = (x1 - x0) / 4.0;
            void Add(int k, (int i, string id) p, string suffix)
            { if (p.i >= 0) list.Add((new Rect(x0 + k * w, top, w, bot - top), MidiTarget.PluginParam(_t, -1, p.i), $"{_title} · {suffix}")); }
            Add(0, _a, "Attack"); Add(1, _d, "Decay"); Add(2, _s, "Sustain"); Add(3, _r, "Release");
            return list;
        }

        private float G((int i, string id) p) => p.i >= 0 ? _e.PluginParamGet(_t, -1, p.i) : 0f;
        private (double x0, double x1, double top, double bot) Geo()
        { double pad = 6; return (pad, Bounds.Width - pad, pad + 11, Bounds.Height - pad - 9); }
        private (double xa, double xd, double sy, double xh, double xr) Nodes()
        {
            var (x0, x1, top, bot) = Geo(); double span = x1 - x0;
            double xa = x0 + G(_a) * 0.30 * span;
            double xd = Math.Min(x1, xa + G(_d) * 0.25 * span);
            double xh = Math.Min(x1, xd + 0.20 * span);
            double xr = Math.Min(x1, xh + G(_r) * 0.25 * span);
            double sy = top + (1 - G(_s)) * (bot - top);
            return (xa, xd, sy, xh, xr);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var p = e.GetPosition(this); var (xa, xd, sy, _, xr) = Nodes(); var (_, _, top, bot) = Geo();
            double dA = Math.Abs(p.X - xa) + Math.Abs(p.Y - top);
            double dDS = Math.Abs(p.X - xd) + Math.Abs(p.Y - sy);
            double dR = Math.Abs(p.X - xr) + Math.Abs(p.Y - bot);
            _drag = (dA <= dDS && dA <= dR) ? 0 : (dDS <= dR ? 1 : 2);
            Gest(true); e.Pointer.Capture(this); Apply(p); e.Handled = true;
        }
        protected override void OnPointerMoved(PointerEventArgs e) { if (_drag >= 0) Apply(e.GetPosition(this)); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        { if (_drag >= 0) { Gest(false); _drag = -1; e.Pointer.Capture(null); } }

        private void Gest(bool begin)
        {
            void G1((int i, string id) p) { if (p.i < 0) return; if (begin) _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id); else _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id); }
            if (_drag == 0) G1(_a);
            else if (_drag == 1) { G1(_d); G1(_s); }
            else G1(_r);
        }
        private void Set((int i, string id) p, double v) { if (p.i >= 0) _e.PluginParamSet(_t, -1, p.i, (float)Math.Clamp(v, 0, 1)); }
        private void Apply(Point p)
        {
            var (x0, x1, top, bot) = Geo(); double span = Math.Max(1, x1 - x0);
            double xa = x0 + G(_a) * 0.30 * span;
            if (_drag == 0) Set(_a, (p.X - x0) / (0.30 * span));
            else if (_drag == 1) { Set(_d, (p.X - xa) / (0.25 * span)); Set(_s, 1 - (p.Y - top) / Math.Max(1, bot - top)); }
            else { double xh = Math.Min(x1, xa + G(_d) * 0.25 * span + 0.20 * span); Set(_r, (p.X - xh) / (0.25 * span)); }
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height; if (w <= 0) return;
            NotaGraph.Window(ctx, new Rect(0, 0, w, h));
            var (x0, x1, top, bot) = Geo();
            var gp = new Pen(GridB, 1);
            for (int i = 1; i <= 3; i++) { double gy = top + (bot - top) * i / 4.0; ctx.DrawLine(gp, new Point(x0, gy), new Point(x1, gy)); }
            var pen = new Pen(Accent, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var (xa, xd, sy, xh, xr) = Nodes();
            ctx.DrawLine(pen, new Point(x0, bot), new Point(xa, top));
            ctx.DrawLine(pen, new Point(xa, top), new Point(xd, sy));
            ctx.DrawLine(pen, new Point(xd, sy), new Point(xh, sy));
            ctx.DrawLine(pen, new Point(xh, sy), new Point(xr, bot));
            foreach (var pt in new[] { new Point(xa, top), new Point(xd, sy), new Point(xr, bot) })
                ctx.DrawEllipse(Accent, null, pt, 3.2, 3.2);
            if (ShowTitle)
                ctx.DrawText(new FormattedText(_title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, TextTertiary), new Point(x0, 5));
        }
    }
