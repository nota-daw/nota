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

    // Interactive ADSR editor for Nota Volt: draw the envelope and drag its three
    // breakpoints (attack peak · decay/sustain · release end) to set the A/D/S/R params
    // directly, recording automation gestures. Retargetable between the amp / filter env.
    // ⌥-click a breakpoint to type its values (attack → A, decay/sustain → D then S,
    // release → R; Tab walks them), double-click resets them (specs/035-typed-value-entry).
    internal sealed class VoltEnv : Control, IMidiLearnRegions, IValueEntryHost
    {
        private static readonly IBrush Sunken = NotaPalette.BgSunken;
        private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
        private static readonly IBrush Ink = NotaPalette.VizInk;
        private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
        private static readonly IBrush GridB = NotaGraph.Grid;
        private static readonly Typeface Face = NotaFonts.Sans;
        private readonly IAudioEngine _e;
        private readonly int _t;
        private (int i, string id) _a, _d, _s, _r;
        private string _title = "AMP ENV";
        private int _drag = -1;   // 0 attack · 1 decay/sustain · 2 release

        // Curve ink (Nota Operator tints modulator envelopes teal); defaults to neutral ink.
        // The node being dragged turns accent.
        public IBrush Accent { get; set; } = Ink;

        // Draw the title in the plot's corner. Off when the card already names the graph
        // in a header above it (Nota Bass).
        public bool ShowTitle { get; set; } = true;

        // A word after the title in its own ink (Nota Operator: the operator's role, carrier or
        // modulator); it names nothing for MIDI learn.
        public string? Note { get; set; }
        public IBrush NoteInk { get; set; } = TextTertiary;

        // The read-out of stage k (0 A · 1 D · 2 S · 3 R) at a 0..1 position, for typing;
        // without it a typed value is read back from the engine's own parameter text.
        public Func<int, double, string>? Format { get => _format; set { _format = value; _stages = null; } }
        private Func<int, double, string>? _format;

        public VoltEnv(IAudioEngine e, int t) { _e = e; _t = t; MinWidth = 150; MinHeight = 96; ValueEntry.AttachHost(this); }
        public void Target(string title, (int, string) a, (int, string) d, (int, string) s, (int, string) r)
        {
            if (title != _title || a != _a || d != _d || s != _s || r != _r) _stages = null;   // re-targeted every tick by some cards
            _title = title; _a = a; _d = d; _s = s; _r = r; InvalidateVisual();
        }
        public void Refresh() => InvalidateVisual();

        // ---- typed entry ------------------------------------------------------------
        private ValueField?[]? _stages;   // A · D · S · R of the current target; null where unmapped

        private ValueField?[] Stages => _stages ??= Enumerable.Range(0, 4).Select(BuildField).ToArray();

        private ValueField? BuildField(int stage)
        {
            var p = stage switch { 0 => _a, 1 => _d, 2 => _s, _ => _r };
            if (p.i < 0) return null;
            void Begin() => _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id);
            void End() => _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id);
            string name = $"{NotaText.Label(_title)} · {new[] { "Attack", "Decay", "Sustain", "Release" }[stage]}";
            double def = _e.PluginParamDefault(_t, -1, p.i);
            return Format is { } fmt
                ? new ValueField(name, () => G(p), n => fmt(stage, n), n => Set(p, n), Begin, End, def) { After = InvalidateVisual }
                : ValueEntry.Probing(name, () => G(p), n => Set(p, n), () => _e.PluginParamText(_t, -1, p.i), Begin, End, InvalidateVisual) with { Default = def };
        }

        /// <summary>The typeable value of stage <paramref name="stage"/> (0 A · 1 D · 2 S · 3 R), for a read-out row.</summary>
        public ValueField? Field(int stage) => Stages[stage];

        public IReadOnlyList<ValueField> AllFields => Stages.OfType<ValueField>().ToArray();

        private IReadOnlyList<ValueField>? Of(params int[] stages)
            => stages.Select(k => Stages[k]).OfType<ValueField>().ToArray() is { Length: > 0 } fs ? fs : null;

        // A breakpoint within 8 px, or a segment within 6 px: the rise → attack, the fall → decay
        // then sustain, the sustain line → sustain, the release fall → release.
        public IReadOnlyList<ValueField>? FieldsAt(Point p)
        {
            var (x0, _, top, bot) = Geo(); var (xa, xd, sy, xh, xr) = Nodes();
            static double Dist(Point p, Point a, Point b)
            {
                Vector ab = b - a, ap = p - a; double l2 = ab.X * ab.X + ab.Y * ab.Y;
                double t = l2 <= 0 ? 0 : Math.Clamp((ap.X * ab.X + ap.Y * ab.Y) / l2, 0, 1);
                return Point.Distance(p, a + ab * t);
            }
            var nodes = new (Point at, int[] stages)[] { (new(xa, top), [0]), (new(xd, sy), [1, 2]), (new(xr, bot), [3]) };
            var near = nodes.MinBy(n => Point.Distance(n.at, p));
            if (Point.Distance(near.at, p) <= 8) return Of(near.stages);
            if (Dist(p, new Point(x0, bot), new Point(xa, top)) <= 6) return Of(0);
            if (Dist(p, new Point(xa, top), new Point(xd, sy)) <= 6) return Of(1, 2);
            if (Dist(p, new Point(xd, sy), new Point(xh, sy)) <= 6) return Of(2);
            if (Dist(p, new Point(xh, sy), new Point(xr, bot)) <= 6) return Of(3);
            return null;
        }

        public Rect AnchorOf(ValueField field)
        {
            var (_, _, top, bot) = Geo(); var (xa, xd, sy, _, xr) = Nodes();
            int k = Array.IndexOf(Stages, field);
            return ValueEntry.Near(k switch { 0 => new Point(xa, top), 3 => new Point(xr, bot), _ => new Point(xd, sy) }, Bounds.Size);
        }

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
        { double pad = 6; return (pad, Bounds.Width - pad, ShowTitle ? 22 : pad + 4, Bounds.Height - pad - 9); }   // the attack peak sits under the title
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
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click: the parameter menu
            var p = e.GetPosition(this); var (xa, xd, sy, _, xr) = Nodes(); var (_, _, top, bot) = Geo();
            double dA = Math.Abs(p.X - xa) + Math.Abs(p.Y - top);
            double dDS = Math.Abs(p.X - xd) + Math.Abs(p.Y - sy);
            double dR = Math.Abs(p.X - xr) + Math.Abs(p.Y - bot);
            _drag = (dA <= dDS && dA <= dR) ? 0 : (dDS <= dR ? 1 : 2);
            Gest(true); e.Pointer.Capture(this); Apply(p); e.Handled = true;
        }
        protected override void OnPointerMoved(PointerEventArgs e) { if (_drag >= 0) Apply(e.GetPosition(this)); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        { if (_drag >= 0) { Gest(false); _drag = -1; InvalidateVisual(); e.Pointer.Capture(null); } }

        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        { if (_drag >= 0) { Gest(false); _drag = -1; InvalidateVisual(); } base.OnPointerCaptureLost(e); }

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
            double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0) return;
            var frame = new Rect(0, 0, w, h);
            NotaGraph.Window(ctx, frame);
            var (x0, x1, top, bot) = Geo();
            for (int i = 1; i <= 3; i++) { double gy = top + (bot - top) * i / 4.0; ctx.DrawLine(NotaGraph.GridPen, new Point(0, gy), new Point(w, gy)); }
            var (xa, xd, sy, xh, xr) = Nodes();
            foreach (var gx in new[] { xa, xd, xh }) ctx.DrawLine(NotaGraph.GridPen, new Point(gx, top), new Point(gx, bot));

            // Drawn like Auto Filter's windows: a straight attack, then decay and release falling
            // away exponentially (as the engine's one-pole stages do); no fill under the line.
            void Shape(StreamGeometryContext g)
            {
                g.LineTo(new Point(xa, top));   // linear attack
                g.CubicBezierTo(new Point(xa + (xd - xa) * 0.25, sy - (sy - top) * 0.15), new Point(xa + (xd - xa) * 0.55, sy), new Point(xd, sy));
                g.LineTo(new Point(xh, sy));
                g.CubicBezierTo(new Point(xh + (xr - xh) * 0.25, bot - (bot - sy) * 0.15), new Point(xh + (xr - xh) * 0.55, bot), new Point(xr, bot));
            }
            var line = new StreamGeometry();
            using (var g = line.Open()) { g.BeginFigure(new Point(x0, bot), false); Shape(g); g.EndFigure(false); }
            ctx.DrawGeometry(null, new Pen(Accent, NotaGraph.PrimaryWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);

            var nodes = new[] { new Point(xa, top), new Point(xd, sy), new Point(xr, bot) };
            for (int k = 0; k < nodes.Length; k++)
                NotaGraph.Node(ctx, nodes[k], active: _drag == k, ink: _drag == k ? null : Accent == Ink ? NotaPalette.TextSecondary : Accent);
            if (ShowTitle)
            {
                NotaGraph.Title(ctx, frame, _title);
                if (!string.IsNullOrEmpty(Note))
                    ctx.DrawText(new NotaFormattedText(Note, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, NotaGraph.TitleSize, NoteInk, this),
                        new Point(6 + NotaGraph.TitleWidth(_title) + 6, 4));
            }
        }
    }
