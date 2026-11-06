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
using Nota.Application.Localization;

namespace Nota.App;

    /// <summary>One time axis for the envelope graphs of a card (#531): each graph lays its
    /// stages out in seconds against the longest envelope of the set, so two envelopes compare
    /// at a glance. Graphs join a scale by being given the same instance.</summary>
    internal sealed class EnvTimeScale
    {
        private readonly List<VoltEnv> _members = new();
        internal void Join(VoltEnv env) { if (!_members.Contains(env)) _members.Add(env); }

        /// <summary>The seconds the plot's time stretch shows: the longest A + D + R of the set.</summary>
        internal double Span => Math.Max(0.05, _members.Count == 0 ? 0.05 : _members.Max(m => m.TimedSeconds()));

        internal void InvalidateAll() { foreach (var m in _members) m.InvalidateVisual(); }
    }

    // Interactive ADSR editor shared by the subtractive built-ins (Volt, Aurora, Operator, Bass,
    // Physical): draw the envelope and drag its three breakpoints (attack peak · decay/sustain ·
    // release end) to set the A/D/S/R params directly, recording automation gestures; drag the
    // diamond in the middle of the decay or release to bend that stage (#531). The curve is the
    // engine's own (EnvShape.h): straight, or bent toward the analog RC curve by Exp.
    // ⌥-click a breakpoint to type its values (attack → A, decay/sustain → D then S,
    // release → R; Tab walks them), double-click resets them (specs/035-typed-value-entry).
    internal sealed class VoltEnv : Control, IMidiLearnRegions, IValueEntryHost
    {
        private static readonly Typeface Face = NotaFonts.Sans;
        private static string Letters => Loc.T("device.env.stage_letters");   // A D S R
        private const double HoldFrac = 0.18;   // the sustain stretch: held, not timed
        private const double BarH = 11, TickH = 10;
        private readonly IAudioEngine _e;
        private readonly int _t;
        private (int i, string id) _a, _d, _s, _r;
        private (int i, string id) _dc = (-1, ""), _rc = (-1, ""), _exp = (-1, "");
        private string _title = "AMP ENV";
        private int _drag = -1;   // 0 attack · 1 decay/sustain · 2 release · 3 decay curve · 4 release curve
        private int _hover = -1;  // the segment under the pointer: 0 A · 1 D · 2 S · 3 R
        private double _dragPps;  // the scale frozen for a drag (px per second)
        private double _bendStartY, _bendStart;
        private int _playStage = 4; private double _playP;

        // Curve ink: the accent by default; Nota Operator / Aurora tint modulator envelopes teal.
        public IBrush? Accent { get; set; }
        private IBrush Ink => Accent ?? NotaPalette.AccentBright;

        // Draw the title in the plot's corner. Off by default (#531): the card names the graph
        // in a header above it.
        public bool ShowTitle { get; set; }

        // A word after the title in its own ink (Nota Operator: the operator's role, carrier or
        // modulator); it names nothing for MIDI learn.
        public string? Note { get; set; }
        public IBrush NoteInk { get; set; } = NotaPalette.TextTertiary;

        // The read-out of stage k (0 A · 1 D · 2 S · 3 R) at a 0..1 position, for typing and
        // the drag label; without it a typed value is read back from the engine's own text.
        public Func<int, double, string>? Format { get => _format; set { _format = value; _stages = null; } }
        private Func<int, double, string>? _format;

        /// <summary>The seconds of stage k (0 A · 1 D · 3 R) at a 0..1 position — the engine's
        /// map. With it the stages are laid out in time (shared through <see cref="Scale"/>);
        /// without it, as fractions of the width.</summary>
        public Func<int, double, double>? Seconds { get; set; }

        public EnvTimeScale? Scale { get => _scale; set { _scale = value; value?.Join(this); } }
        private EnvTimeScale? _scale;

        public VoltEnv(IAudioEngine e, int t) { _e = e; _t = t; MinWidth = 150; MinHeight = 96; ValueEntry.AttachHost(this); }
        public void Target(string title, (int, string) a, (int, string) d, (int, string) s, (int, string) r)
        {
            if (title != _title || a != _a || d != _d || s != _s || r != _r) _stages = null;   // re-targeted every tick by some cards
            _title = title; _a = a; _d = d; _s = s; _r = r; InvalidateVisual();
        }

        /// <summary>The shape params of the target: decay curve, release curve (0.5 = straight)
        /// and Exp (≥ 0.5 bends every stage toward the analog curve).</summary>
        public void Shape((int, string) decayCurve, (int, string) releaseCurve, (int, string) exp)
        { _dc = decayCurve; _rc = releaseCurve; _exp = exp; InvalidateVisual(); }

        /// <summary>The sounding voice's place on this envelope: stage 0 A · 1 D · 2 S · 3 R
        /// (4: none) and the progress through it.</summary>
        public void SetPlay(int stage, double p)
        {
            if (stage == _playStage && Math.Abs(p - _playP) < 1e-4) return;
            _playStage = stage; _playP = p; InvalidateVisual();
        }

        public void Refresh() { if (_scale is { } s) s.InvalidateAll(); else InvalidateVisual(); }

        // ---- the engine's curve (EnvShape.h) ----------------------------------------
        private const double ExpBend = 0.6;
        private bool ExpOn => _exp.i >= 0 && G(_exp) >= 0.5f;
        private double BendOf((int i, string id) p) => Math.Clamp((p.i >= 0 ? (G(p) - 0.5) * 2 : 0) + (ExpOn ? ExpBend : 0), -1, 1);
        private static double Curve(double p, double k)
        {
            if (k == 0) return p;
            double e = 1 + 4 * Math.Abs(k);
            return k > 0 ? 1 - Math.Pow(1 - p, e) : Math.Pow(p, e);
        }

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
                ? new ValueField(name, () => G(p), n => fmt(stage, n), n => Set(p, n), Begin, End, def) { After = Refresh }
                : ValueEntry.Probing(name, () => G(p), n => Set(p, n), () => _e.PluginParamText(_t, -1, p.i), Begin, End, Refresh) with { Default = def };
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
            var (_, _, top, bot) = Geo(); var n = Nodes();
            var nodes = new (Point at, int[] stages)[] { (new(n.xa, top), [0]), (new(n.xd, n.sy), [1, 2]), (new(n.xr, bot), [3]) };
            var near = nodes.MinBy(q => Point.Distance(q.at, p));
            if (Point.Distance(near.at, p) <= 8) return Of(near.stages);
            return SegmentAt(p.X) switch { 0 => Of(0), 1 => Of(1, 2), 2 => Of(2), 3 => Of(3), _ => null };
        }

        public Rect AnchorOf(ValueField field)
        {
            var (_, _, top, bot) = Geo(); var n = Nodes();
            int k = Array.IndexOf(Stages, field);
            return ValueEntry.Near(k switch { 0 => new Point(n.xa, top), 3 => new Point(n.xr, bot), _ => new Point(n.xd, n.sy) }, Bounds.Size);
        }

        // MIDI-learn: the A/D/S/R params live in this drag editor too, so expose them as
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

        // ---- geometry ---------------------------------------------------------------
        private float G((int i, string id) p) => p.i >= 0 ? _e.PluginParamGet(_t, -1, p.i) : 0f;

        // The plot: the curve between top and bot, the segment bar and the time ticks under it.
        private (double x0, double x1, double top, double bot) Geo()
        {
            double pad = 7;
            return (pad, Bounds.Width - pad - 30, ShowTitle ? 22 : pad + 3, Bounds.Height - BarH - TickH - 4);   // 30: the sustain badge
        }

        private double Sec(int stage, (int i, string id) p) => Seconds is { } f ? Math.Max(0, f(stage, G(p))) : 0;

        /// <summary>This envelope's timed length (A + D + R) in seconds, for the shared scale.</summary>
        internal double TimedSeconds() => Sec(0, _a) + Sec(1, _d) + Sec(3, _r);

        private double Pps()
        {
            var (x0, x1, _, _) = Geo(); double span = Math.Max(1, x1 - x0);
            if (_drag >= 0 && _dragPps > 0) return _dragPps;
            double sec = _scale?.Span ?? Math.Max(0.05, TimedSeconds());
            return span * (1 - HoldFrac) / sec;
        }

        private (double xa, double xd, double sy, double xh, double xr) Nodes()
        {
            var (x0, x1, top, bot) = Geo(); double span = Math.Max(1, x1 - x0);
            double xa, xd, xh, xr;
            if (Seconds is not null)
            {
                double pps = Pps(), min = 2;
                xa = x0 + Math.Max(min, Sec(0, _a) * pps);
                xd = Math.Min(x1, xa + Math.Max(min, Sec(1, _d) * pps));
                xh = Math.Min(x1, xd + HoldFrac * span);
                xr = Math.Min(x1, xh + Math.Max(min, Sec(3, _r) * pps));
            }
            else
            {
                xa = x0 + G(_a) * 0.30 * span;
                xd = Math.Min(x1, xa + G(_d) * 0.25 * span);
                xh = Math.Min(x1, xd + HoldFrac * span);
                xr = Math.Min(x1, xh + G(_r) * 0.25 * span);
            }
            double sy = top + (1 - G(_s)) * (bot - top);
            return (xa, xd, sy, xh, xr);
        }

        private int SegmentAt(double x)
        {
            var (x0, _, _, _) = Geo(); var n = Nodes();
            if (x < x0 - 4 || x > n.xr + 6) return -1;
            return x < n.xa ? 0 : x < n.xd ? 1 : x < n.xh ? 2 : 3;
        }

        // A point of the curve: stage 0 A · 1 D · 3 R at progress p (sustain sits flat).
        private Point At(int stage, double p)
        {
            var (x0, _, top, bot) = Geo(); var n = Nodes();
            double s = G(_s), h = bot - top;
            double Y(double level) => bot - level * h;
            return stage switch
            {
                0 => new Point(x0 + (n.xa - x0) * p, Y(Curve(p, ExpOn ? ExpBend : 0))),
                1 => new Point(n.xa + (n.xd - n.xa) * p, Y(1 - (1 - s) * Curve(p, BendOf(_dc)))),
                2 => new Point(n.xd + (n.xh - n.xd) * p, Y(s)),
                _ => new Point(n.xh + (n.xr - n.xh) * p, Y(s * (1 - Curve(p, BendOf(_rc))))),
            };
        }

        private Point Diamond(int stage) => At(stage == 3 ? 1 : 3, 0.5);   // 3 → the decay's, 4 → the release's

        // ---- pointer ------------------------------------------------------------------
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click: the parameter menu
            var p = e.GetPosition(this); var n = Nodes(); var (_, _, top, bot) = Geo();
            _dragPps = Pps();
            var cands = new List<(int k, double d)>
            {
                (0, Math.Abs(p.X - n.xa) + Math.Abs(p.Y - top)),
                (1, Math.Abs(p.X - n.xd) + Math.Abs(p.Y - n.sy)),
                (2, Math.Abs(p.X - n.xr) + Math.Abs(p.Y - bot)),
            };
            if (_dc.i >= 0) { var q = Diamond(3); cands.Add((3, Math.Abs(p.X - q.X) + Math.Abs(p.Y - q.Y))); }
            if (_rc.i >= 0) { var q = Diamond(4); cands.Add((4, Math.Abs(p.X - q.X) + Math.Abs(p.Y - q.Y))); }
            var best = cands.MinBy(c => c.d);
            // A diamond is a small target: it wins only when the press is right on it.
            if (best.k >= 3 && best.d > 12) best = cands.Where(c => c.k < 3).MinBy(c => c.d);
            _drag = best.k;
            if (_drag >= 3) { _bendStartY = p.Y; _bendStart = G(_drag == 3 ? _dc : _rc); }
            if (e.ClickCount == 2 && _drag >= 3)
            {
                var c = _drag == 3 ? _dc : _rc;
                Gest(true); Set(c, _e.PluginParamDefault(_t, -1, c.i)); Gest(false); _drag = -1; Refresh(); e.Handled = true; return;
            }
            Gest(true); e.Pointer.Capture(this); Apply(p); e.Handled = true;
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag >= 0) { Apply(p); return; }
            int h = SegmentAt(p.X);
            if (h != _hover) { _hover = h; InvalidateVisual(); }
        }
        protected override void OnPointerExited(PointerEventArgs e)
        { if (_hover >= 0) { _hover = -1; InvalidateVisual(); } base.OnPointerExited(e); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        { if (_drag >= 0) { Gest(false); _drag = -1; Refresh(); e.Pointer.Capture(null); } }

        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        { if (_drag >= 0) { Gest(false); _drag = -1; Refresh(); } base.OnPointerCaptureLost(e); }

        private void Gest(bool begin)
        {
            void G1((int i, string id) p) { if (p.i < 0) return; if (begin) _e.BeginAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id); else _e.EndAutomationWrite(_t, AutomationTarget.PluginParam, -1, -1, p.id); }
            if (_drag == 0) G1(_a);
            else if (_drag == 1) { G1(_d); G1(_s); }
            else if (_drag == 2) G1(_r);
            else if (_drag == 3) G1(_dc);
            else if (_drag == 4) G1(_rc);
        }
        private void Set((int i, string id) p, double v) { if (p.i >= 0) _e.PluginParamSet(_t, -1, p.i, (float)Math.Clamp(v, 0, 1)); }

        // The position whose seconds are `sec` (the maps are monotonic): a bisection.
        private double NormFor(int stage, double sec)
        {
            double lo = 0, hi = 1;
            for (int i = 0; i < 28; i++) { double mid = (lo + hi) / 2; if (Seconds!(stage, mid) < sec) lo = mid; else hi = mid; }
            return (lo + hi) / 2;
        }

        private void Apply(Point p)
        {
            var (x0, x1, top, bot) = Geo(); double span = Math.Max(1, x1 - x0);
            var n = Nodes();
            if (_drag >= 3)
            {
                // Up bends the stage toward a fast start, down toward a slow one.
                Set(_drag == 3 ? _dc : _rc, _bendStart + (_bendStartY - p.Y) / 160.0);
            }
            else if (Seconds is not null)
            {
                double pps = Math.Max(1e-6, _dragPps);
                if (_drag == 0) Set(_a, NormFor(0, Math.Max(0, p.X - x0) / pps));
                else if (_drag == 1) { Set(_d, NormFor(1, Math.Max(0, p.X - n.xa) / pps)); Set(_s, 1 - (p.Y - top) / Math.Max(1, bot - top)); }
                else Set(_r, NormFor(3, Math.Max(0, p.X - n.xh) / pps));
            }
            else
            {
                if (_drag == 0) Set(_a, (p.X - x0) / (0.30 * span));
                else if (_drag == 1) { Set(_d, (p.X - n.xa) / (0.25 * span)); Set(_s, 1 - (p.Y - top) / Math.Max(1, bot - top)); }
                else Set(_r, (p.X - n.xh) / (0.25 * span));
            }
            Refresh();
        }

        // ---- drawing ------------------------------------------------------------------
        private FormattedText Text(string s, IBrush ink, double size = NotaGraph.AxisSize, FontWeight weight = FontWeight.Normal)
            => new NotaFormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Face.FontFamily, FontStyle.Normal, weight), size, ink, this);

        private string StageText(int stage)
        {
            var p = stage switch { 0 => _a, 1 => _d, 2 => _s, _ => _r };
            if (p.i < 0) return "";
            return Format is { } f ? f(stage, G(p)) : _e.PluginParamText(_t, -1, p.i);
        }

        private static IBrush Wash(IBrush b, byte alpha)
            => b is ISolidColorBrush s ? new SolidColorBrush(Color.FromArgb(alpha, s.Color.R, s.Color.G, s.Color.B)) : b;

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0) return;
            var plot = new Rect(0, 0, w, h - BarH - TickH);
            NotaGraph.Window(ctx, plot);
            var (x0, x1, top, bot) = Geo();
            var n = Nodes();
            var ink = Ink;
            int active = _drag switch { 0 => 0, 1 => 1, 2 => 3, 3 => 1, 4 => 3, _ => _hover };

            // Level guides only: the segment bar under the plot says where each stage is.
            for (int i = 1; i <= 3; i++) { double gy = top + (bot - top) * i / 4.0; ctx.DrawLine(NotaGraph.GridPen, new Point(1, gy), new Point(w - 1, gy)); }

            // The sustain stretch: held while the key is down, not a time — tinted and named.
            var hold = new Rect(n.xd, top - 2, Math.Max(0, n.xh - n.xd), bot - top + 2);
            ctx.DrawRectangle(Wash(NotaPalette.TextSecondary, 0x10), null, hold);
            var held = Text(Loc.T("device.env.held"), NotaPalette.TextAxis);
            if (hold.Width > held.Width + 4) ctx.DrawText(held, new Point(n.xd + (hold.Width - held.Width) / 2, top));

            // The curve: sampled from the engine's own shape, with a light fill under it.
            var pts = new List<Point> { new(x0, bot) };
            for (int st = 0; st < 4; st++)
            {
                if (st == 2) { pts.Add(At(2, 1)); continue; }
                for (int i = 1; i <= 24; i++) pts.Add(At(st, i / 24.0));
            }
            var line = new StreamGeometry();
            using (var g = line.Open()) { g.BeginFigure(pts[0], false); foreach (var q in pts.Skip(1)) g.LineTo(q); g.EndFigure(false); }
            var area = new StreamGeometry();
            using (var g = area.Open()) { g.BeginFigure(pts[0], true); foreach (var q in pts.Skip(1)) g.LineTo(q); g.LineTo(new Point(n.xr, bot)); g.EndFigure(true); }
            ctx.DrawGeometry(Wash(ink, 0x24), null, area);
            ctx.DrawGeometry(null, new Pen(ink, NotaGraph.PrimaryWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);

            // The sustain level: a dashed line to the edge and its value in a badge.
            var dash = new Pen(Wash(ink, 0x90), 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            ctx.DrawLine(dash, new Point(n.xh, n.sy), new Point(w - 30, n.sy));
            var sv = Text(StageText(2), ink, NotaGraph.AxisSize, FontWeight.SemiBold);
            var badge = new Rect(w - 29, Math.Clamp(n.sy - sv.Height / 2 - 1, 1, plot.Height - sv.Height - 3), 27, sv.Height + 2);
            ctx.DrawRectangle(Wash(ink, 0x26), null, badge, 3, 3);
            ctx.DrawText(sv, new Point(badge.X + (badge.Width - sv.Width) / 2, badge.Y + 1));

            // The bend handles: a diamond in the middle of decay and release.
            void Diam(Point c, bool on)
            {
                double r = on ? 4.5 : 3.5;
                var gd = new StreamGeometry();
                using (var g = gd.Open())
                {
                    g.BeginFigure(new Point(c.X, c.Y - r), true); g.LineTo(new Point(c.X + r, c.Y)); g.LineTo(new Point(c.X, c.Y + r)); g.LineTo(new Point(c.X - r, c.Y)); g.EndFigure(true);
                }
                ctx.DrawGeometry(on ? ink : NotaGraph.Ground, new Pen(ink, 1.2), gd);
            }
            if (_dc.i >= 0) Diam(Diamond(3), _drag == 3);
            if (_rc.i >= 0) Diam(Diamond(4), _drag == 4);

            // The breakpoints: hollow in the curve's ink; the dragged one gets a halo and its value.
            var nodes = new[] { new Point(n.xa, top), new Point(n.xd, n.sy), new Point(n.xr, bot) };
            for (int k = 0; k < nodes.Length; k++)
            {
                bool on = _drag == k;
                if (on) ctx.DrawEllipse(Wash(ink, 0x40), null, nodes[k], 8, 8);
                ctx.DrawEllipse(on ? ink : NotaGraph.Ground, new Pen(ink, 1.6), nodes[k], 3.6, 3.6);
            }
            if (_drag >= 0)
            {
                int stage = _drag switch { 0 => 0, 1 => 1, 2 => 3, _ => -1 };
                string label = stage >= 0 ? $"{Letters[stage]} {StageText(stage)}"
                    : $"{(_drag == 3 ? "D" : "R")} {Loc.T("device.env.curve")} {NotaNum.Str((BendOf(_drag == 3 ? _dc : _rc)) * 100, "+0;−0;0")}";
                var lt = Text(label, NotaPalette.TextPrimary, NotaGraph.AxisSize, FontWeight.SemiBold);
                var anchor = _drag switch { 0 => nodes[0], 1 => nodes[1], 2 => nodes[2], 3 => Diamond(3), _ => Diamond(4) };
                double lx = Math.Clamp(anchor.X - lt.Width / 2 - 4, 1, w - lt.Width - 9), ly = anchor.Y - lt.Height - 12;
                if (ly < 1) ly = anchor.Y + 10;
                var tip = new Rect(lx, ly, lt.Width + 8, lt.Height + 3);
                ctx.DrawRectangle(NotaPalette.SurfaceRaised, new Pen(NotaPalette.BorderDefault, 1), tip, 3, 3);
                ctx.DrawText(lt, new Point(lx + 4, ly + 1.5));
            }

            // The sounding voice's place on the curve.
            if (_playStage is >= 0 and <= 3)
            {
                var dot = _playStage == 2 ? At(2, 0.5) : At(_playStage, Math.Clamp(_playP, 0, 1));
                ctx.DrawEllipse(NotaPalette.TextPrimary, new Pen(NotaGraph.Ground, 1.5), dot, 3.2, 3.2);
            }

            if (ShowTitle)
            {
                NotaGraph.Title(ctx, plot, _title);
                if (!string.IsNullOrEmpty(Note))
                    ctx.DrawText(new NotaFormattedText(Note, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, NotaGraph.TitleSize, NoteInk, this),
                        new Point(6 + NotaGraph.TitleWidth(_title) + 6, 4));
            }

            // The segment bar: which stretch of the curve each stage is; the one being edited lit.
            double by = h - BarH - TickH + 2;
            var segs = new[] { (x0, n.xa), (n.xa, n.xd), (n.xd, n.xh), (n.xh, n.xr) };
            for (int k = 0; k < 4; k++)
            {
                var (a, b) = segs[k];
                var r = new Rect(a + 0.5, by, Math.Max(1, b - a - 1), BarH - 2);
                bool on = active == k;
                ctx.DrawRectangle(on ? Wash(ink, 0x40) : NotaPalette.SurfaceRaised, null, r, 2, 2);
                var lt = Text(Letters[k].ToString(), on ? NotaPalette.TextPrimary : NotaPalette.TextTertiary, 8, FontWeight.SemiBold);
                if (r.Width > lt.Width + 2) ctx.DrawText(lt, new Point(r.X + (r.Width - lt.Width) / 2, r.Y + (r.Height - lt.Height) / 2));
            }

            // The time ticks of the shared scale: one run from the note-on under attack and decay,
            // one from the key-up under the release (the held stretch between has no time).
            if (Seconds is not null)
            {
                double pps = Pps(), sec = (x1 - x0) * (1 - HoldFrac) / Math.Max(1e-6, pps);
                double step = NiceStep(sec / 3);
                double lastRight = -1;
                void Run(double from, double to)
                {
                    for (double tsec = 0; ; tsec += step)
                    {
                        double x = from + tsec * pps;
                        if (x > to + 0.5) break;
                        var tt = Text(NotaNum.Time(tsec), NotaPalette.TextAxis, 7.5);
                        double tx = Math.Clamp(tsec == 0 ? x : x - tt.Width / 2, 0, w - tt.Width);
                        if (tx <= lastRight + 3) continue;
                        ctx.DrawText(tt, new Point(tx, h - TickH + 1));
                        lastRight = tx + tt.Width;
                    }
                }
                Run(x0, n.xd);
                Run(n.xh, n.xr);
            }
        }

        private static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw))), f = raw / mag;
            return (f < 1.5 ? 1 : f < 3.5 ? 2 : f < 7.5 ? 5 : 10) * mag;
        }
    }
