// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class ArrangementView
{
    // ---- ruler ------------------------------------------------------------
    private sealed class RulerControl : Control
    {
        private readonly ArrangementView _o;
        public RulerControl(ArrangementView o) { _o = o; Cursor = new Cursor(StandardCursorType.Hand); }

        private double _pressBeat;
        private bool _dragging;   // a click scrubs the playhead; a drag defines a loop region

        private double BeatAt(PointerEventArgs e) => _o._scrollBeats + e.GetPosition(this).X / _o._pixelsPerBeat;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _pressBeat = BeatAt(e);
            _dragging = false;
            e.Pointer.Capture(this); e.Handled = true;
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            double cur = BeatAt(e);
            if (!_dragging && Math.Abs(cur - _pressBeat) * _o._pixelsPerBeat > 4) _dragging = true;
            if (_dragging) _o.SetLoopPreview(_pressBeat, cur);
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_dragging) _o.CommitLoopPreview();      // drag → loop region
            else _o.SeekTo(_pressBeat);                 // plain click → scrub playhead
            _dragging = false;
            e.Pointer.Capture(null);
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            ctx.FillRectangle(ChromeBg, new Rect(0, 0, w, h));

            // Loop brace across the ruler (drawn under bars/labels/playhead).
            if (_o._loopActive && _o._loopE > _o._loopS)
            {
                double lx0 = _o.BeatToX(_o._loopS), lx1 = _o.BeatToX(_o._loopE);
                if (lx1 > 0 && lx0 < w)
                {
                    double cx0 = Math.Max(0, lx0), cx1 = Math.Min(w, lx1);
                    ctx.FillRectangle(LoopBrace, new Rect(cx0, 0, cx1 - cx0, h));
                    if (lx0 >= 0) ctx.DrawLine(LoopEdge, new Point(lx0, 0), new Point(lx0, h));
                    if (lx1 <= w) ctx.DrawLine(LoopEdge, new Point(lx1, 0), new Point(lx1, h));
                }
            }

            int firstBar = (int)Math.Floor(_o._scrollBeats / _o._beatsPerBar);
            for (int bar = firstBar; ; bar++)
            {
                double beat = bar * _o._beatsPerBar;
                double x = _o.BeatToX(beat);
                if (x > w) break;
                if (x < 0) continue;
                ctx.DrawLine(BarPen, new Point(x, h - 7), new Point(x, h));
                var ft = new FormattedText((bar + 1).ToString(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 9, RulerText);
                ctx.DrawText(ft, new Point(x + 3, 3));
            }

            // Playhead marker: a small brass triangle at the top of the ruler.
            double px = _o.BeatToX(_o._playheadBeats);
            if (px >= -5 && px <= w + 5)
            {
                var tri = new StreamGeometry();
                using (var g = tri.Open())
                {
                    g.BeginFigure(new Point(px - 4, 0), true);
                    g.LineTo(new Point(px + 4, 0));
                    g.LineTo(new Point(px, 6));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(PlayheadBrush, null, tri);
            }
        }
    }

    // ---- pinned return/master lanes --------------------------------------
    private sealed class FooterLaneControl : Control
    {
        private static readonly IBrush AutomationText = NotaPalette.TextDisabled; // Brush.TextDisabled
        private readonly ArrangementView _o;
        public FooterLaneControl(ArrangementView o) { _o = o; ClipToBounds = true; }

        private AutoPt? _mDrag;   // master-automation point being dragged

        private double MasterRowTop => _o._returns.Count * FooterRowH;
        private double MasterValueToY(float v)
        {
            double top = MasterRowTop + 6, bot = MasterRowTop + FooterRowH - 6;
            double t = (v - ArrangementView.MasterAutoMin) / (ArrangementView.MasterAutoMax - ArrangementView.MasterAutoMin);
            return bot - Math.Clamp(t, 0, 1) * (bot - top);
        }
        private float MasterYToValue(double py)
        {
            double top = MasterRowTop + 6, bot = MasterRowTop + FooterRowH - 6;
            double t = (bot - py) / Math.Max(1, bot - top);
            return ArrangementView.MasterAutoMin + (float)Math.Clamp(t, 0, 1) * (ArrangementView.MasterAutoMax - ArrangementView.MasterAutoMin);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!_o._automationMode) return;
            var pos = e.GetPosition(this);
            if (pos.Y < MasterRowTop) return;   // only the master row is editable
            bool right = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;
            double beat = Math.Max(0, _o.Snap(_o._scrollBeats + pos.X / _o._pixelsPerBeat));

            // Hit an existing point? Right-click / double-click removes it; left grabs it.
            for (int i = 0; i < _o._masterAuto.Count; i++)
            {
                var p = _o._masterAuto[i];
                if (Math.Abs(_o.BeatToX(p.Beat) - pos.X) <= 6 && Math.Abs(MasterValueToY(p.Value) - pos.Y) <= 6)
                {
                    if (right || e.ClickCount == 2) { _o._masterAuto.RemoveAt(i); _o.CommitMasterAuto(); InvalidateVisual(); }
                    else { _mDrag = p; e.Pointer.Capture(this); }
                    e.Handled = true;
                    return;
                }
            }
            if (right) return;
            var np = new AutoPt { Beat = beat, Value = MasterYToValue(pos.Y) };   // add + grab
            _o._masterAuto.Add(np);
            _mDrag = np;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_mDrag is null) return;
            var pos = e.GetPosition(this);
            _mDrag.Beat = Math.Max(0, _o.Snap(_o._scrollBeats + pos.X / _o._pixelsPerBeat));
            _mDrag.Value = MasterYToValue(pos.Y);
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_mDrag is null) return;
            _mDrag = null;
            e.Pointer.Capture(null);
            _o.CommitMasterAuto();
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            int rows = _o._returns.Count + 1; // returns + master

            for (int i = 0; i < rows; i++)
            {
                double y = i * FooterRowH;
                ctx.FillRectangle(LaneBgB, new Rect(0, y, w, FooterRowH));
                ctx.DrawLine(BeatPen, new Point(0, y + FooterRowH), new Point(w, y + FooterRowH));
            }

            // Vertical grid (bars always; beats when zoomed in).
            bool showBeats = _o._pixelsPerBeat >= 12;
            int firstBeat = (int)Math.Floor(_o._scrollBeats);
            for (int beat = firstBeat; ; beat++)
            {
                double x = _o.BeatToX(beat);
                if (x > w) break;
                if (x < 0) continue;
                bool isBar = beat % _o._beatsPerBar == 0;
                if (isBar) ctx.DrawLine(BarPen, new Point(x, 0), new Point(x, h));
                else if (showBeats) ctx.DrawLine(BeatPen, new Point(x, 0), new Point(x, h));
            }

            // Master row: master-volume automation (M9 follow-up) in automation mode,
            // else a quiet hint. Value axis 0..2 across the row.
            double my = _o._returns.Count * FooterRowH;
            if (_o._automationMode)
            {
                ctx.FillRectangle(AutoScrim, new Rect(0, my, w, FooterRowH));
                var pts = _o._masterAuto;
                if (pts.Count == 0)
                {
                    var hint = new FormattedText("click to add master volume points", CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, Typeface.Default, 9, AutomationText);
                    ctx.DrawText(hint, new Point(12, my + (FooterRowH - hint.Height) / 2));
                }
                else
                {
                    var ordered = pts.OrderBy(p => p.Beat).ToList();
                    double fx = _o.BeatToX(ordered[0].Beat), fy = MasterValueToY(ordered[0].Value);
                    ctx.DrawLine(new Pen(PlayheadBrush, 1), new Point(0, fy), new Point(fx, fy)); // hold before first
                    for (int k = 1; k < ordered.Count; k++)
                    {
                        double ax = _o.BeatToX(ordered[k - 1].Beat), ay = MasterValueToY(ordered[k - 1].Value);
                        double bx = _o.BeatToX(ordered[k].Beat), by = MasterValueToY(ordered[k].Value);
                        ctx.DrawLine(new Pen(PlayheadBrush, 1.6), new Point(ax, ay), new Point(bx, by));
                    }
                    double lx = _o.BeatToX(ordered[^1].Beat), ly = MasterValueToY(ordered[^1].Value);
                    ctx.DrawLine(new Pen(PlayheadBrush, 1), new Point(lx, ly), new Point(w, ly)); // hold after last
                    foreach (var p in ordered)
                    {
                        double x = _o.BeatToX(p.Beat), y = MasterValueToY(p.Value);
                        ctx.DrawEllipse(PlayheadBrush, null, new Point(x, y), 3, 3);
                    }
                }
            }
            else
            {
                var ft = new FormattedText("master volume", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 9, AutomationText);
                ctx.DrawText(ft, new Point(12, my + (FooterRowH - ft.Height) / 2));
            }

            // Loop region wash (matches the scrolling lanes).
            if (_o._loopActive && _o._loopE > _o._loopS)
            {
                double lx0 = _o.BeatToX(_o._loopS), lx1 = _o.BeatToX(_o._loopE);
                if (lx1 > 0 && lx0 < w)
                {
                    double cx0 = Math.Max(0, lx0), cx1 = Math.Min(w, lx1);
                    ctx.FillRectangle(LoopBand, new Rect(cx0, 0, cx1 - cx0, h));
                    if (lx0 >= 0) ctx.DrawLine(LoopEdge, new Point(lx0, 0), new Point(lx0, h));
                    if (lx1 <= w) ctx.DrawLine(LoopEdge, new Point(lx1, 0), new Point(lx1, h));
                }
            }

            // Playhead across the footer lanes (brass line + glow).
            double px = _o.BeatToX(_o._playheadBeats);
            if (px >= 0 && px <= w)
            {
                ctx.DrawLine(PlayheadGlow, new Point(px, 0), new Point(px, h));
                ctx.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, h));
            }
        }
    }
}
