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
        public RulerControl(ArrangementView o) { _o = o; Cursor = HandCursor; }

        private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
        private static readonly Cursor EdgeCursor = new(StandardCursorType.SizeWestEast);
        private const double EdgeGrab = 5;      // px either side of a loop edge that grabs it
        private const double ArrowZone = 14;    // px at the end of the ruler the off-screen arrow owns

        // A click scrubs the playhead; a drag draws a loop region. On the loop brace a drag
        // moves the region, on either edge it resizes that edge alone (#125).
        private enum Gesture { None, Pending, Draw, Move, ResizeStart, ResizeEnd, Done }
        private Gesture _gesture;
        private Gesture _onRelease;             // what this press becomes once it drags
        private double _pressBeat, _pressX;
        private double _origS, _origE;

        private double BeatAt(PointerEventArgs e) => _o._scrollBeats + e.GetPosition(this).X / _o._pixelsPerBeat;

        // Which loop edge (−1 start, +1 end, 0 none) is under x; the closer one on a short loop.
        private int EdgeAt(double x)
        {
            if (!(_o._loopE > _o._loopS)) return 0;
            double ds = Math.Abs(x - _o.BeatToX(_o._loopS)), de = Math.Abs(x - _o.BeatToX(_o._loopE));
            if (Math.Min(ds, de) > EdgeGrab) return 0;
            return ds <= de ? -1 : 1;
        }
        private bool OnBrace(double beat) => _o._loopE > _o._loopS && beat > _o._loopS && beat < _o._loopE;
        private bool OnArrow(double x)
        {
            int side = _o.PlayheadOffscreen();
            return side < 0 ? x < ArrowZone : side > 0 && x > Bounds.Width - ArrowZone;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            if (pt.Properties.IsRightButtonPressed) { ShowMenu(); e.Handled = true; return; }
            if (!pt.Properties.IsLeftButtonPressed) return;
            double x = e.GetPosition(this).X;
            _pressBeat = BeatAt(e); _pressX = x;
            _origS = _o._loopS; _origE = _o._loopE;
            _gesture = Gesture.Pending;
            int edge = EdgeAt(x);
            if (OnArrow(x)) { _o.RevealPlayhead(); _gesture = Gesture.Done; }   // the off-screen arrow (#105)
            // Double-click on the loop brace turns the loop off / on; the region stays (#59).
            else if (e.ClickCount == 2 && _o.ToggleLoopAt(_pressBeat)) _gesture = Gesture.Done;
            // A click still seeks; only a drag past a few pixels becomes one of these.
            _onRelease = edge < 0 ? Gesture.ResizeStart : edge > 0 ? Gesture.ResizeEnd
                       : OnBrace(_pressBeat) ? Gesture.Move : Gesture.Draw;
            e.Pointer.Capture(this); e.Handled = true;
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            double x = e.GetPosition(this).X;
            if (_gesture == Gesture.None)
            {
                // Hover: a sideways arrow over a loop edge says it can be dragged.
                int edge = EdgeAt(x);
                var want = edge != 0 ? EdgeCursor : HandCursor;
                if (Cursor != want) Cursor = want;
                if (edge != _hotEdge) { _hotEdge = edge; InvalidateVisual(); }
                return;
            }
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _gesture == Gesture.Done) return;
            double cur = BeatAt(e);
            if (_gesture == Gesture.Pending && Math.Abs(x - _pressX) > 4) _gesture = _onRelease;
            bool free = (e.KeyModifiers & KeyModifiers.Alt) != 0;   // ⌥ = off the grid
            switch (_gesture)
            {
                case Gesture.Draw: _o.SetLoopPreview(_pressBeat, cur); break;
                case Gesture.Move: _o.SetLoopMovePreview(_origS, _origE, cur - _pressBeat, free); break;
                case Gesture.ResizeStart: _o.SetLoopEdgePreview(start: true, cur, free); break;
                case Gesture.ResizeEnd: _o.SetLoopEdgePreview(start: false, cur, free); break;
            }
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            switch (_gesture)
            {
                case Gesture.Pending: _o.SeekTo(_pressBeat); break;   // plain click → scrub playhead
                case Gesture.Draw: _o.CommitLoopPreview(); break;      // drag → loop region
                case Gesture.Move or Gesture.ResizeStart or Gesture.ResizeEnd: _o.CommitLoopEdit(); break;
            }
            _gesture = Gesture.None;
            Cursor = EdgeAt(e.GetPosition(this).X) != 0 ? EdgeCursor : HandCursor;
            e.Pointer.Capture(null);
        }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            if (_gesture is Gesture.Draw or Gesture.Move or Gesture.ResizeStart or Gesture.ResizeEnd) _o.CancelLoopPreview();
            _gesture = Gesture.None;
        }
        protected override void OnPointerExited(PointerEventArgs e)
        {
            if (_hotEdge != 0) { _hotEdge = 0; InvalidateVisual(); }
        }
        private int _hotEdge;   // the loop edge under the pointer, drawn brighter

        // Right-click: the loop region's commands (#125). Clearing forgets the region — unlike
        // turning the loop off, which keeps it for a double-click to bring back.
        private void ShowMenu()
        {
            var flyout = new MenuFlyout();
            bool has = _o._loopE > _o._loopS;
            var toggle = new MenuItem { Header = _o._loopActive ? "Turn loop off" : "Turn loop on", Icon = MenuKit.Icon(GlyphKind.Cycle), InputGesture = MenuKit.LoopKey };
            toggle.Click += (_, _) => _o.SetLoopOn(!_o._loopActive);
            var sel = new MenuItem { Header = "Loop selection", Icon = MenuKit.Icon(GlyphKind.Select), IsEnabled = _o.SelectionLoopRange() is not null };
            sel.Click += (_, _) => { if (_o.SelectionLoopRange() is { } r) _o.SetLoopRegion(r.Start, r.End); };
            var clear = new MenuItem { Header = "Clear loop region", Icon = MenuKit.Icon(GlyphKind.Trash), IsEnabled = has };
            clear.Click += (_, _) => _o.ClearLoopRegion();
            flyout.Items.Add(toggle);
            flyout.Items.Add(sel);
            flyout.Items.Add(new Separator());
            flyout.Items.Add(clear);
            flyout.ShowAt(this, showAtPointer: true);
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            ctx.FillRectangle(ChromeBg, new Rect(0, 0, w, h));

            // Loop brace across the ruler (drawn under bars/labels/playhead). A switched-off
            // loop keeps its region, drawn in grey, so a double-click can bring it back.
            if (_o._loopE > _o._loopS)
            {
                double lx0 = _o.BeatToX(_o._loopS), lx1 = _o.BeatToX(_o._loopE);
                if (lx1 > 0 && lx0 < w)
                {
                    var (fill, edge) = _o._loopActive ? (LoopBrace, LoopEdge) : (LoopBraceOff, LoopEdgeOff);
                    double cx0 = Math.Max(0, lx0), cx1 = Math.Min(w, lx1);
                    ctx.FillRectangle(fill, new Rect(cx0, 0, cx1 - cx0, h));
                    if (lx0 >= 0) ctx.DrawLine(edge, new Point(lx0, 0), new Point(lx0, h));
                    if (lx1 <= w) ctx.DrawLine(edge, new Point(lx1, 0), new Point(lx1, h));
                    // The edge under the pointer (or being dragged) brightens: it can be grabbed.
                    int hot = _gesture == Gesture.ResizeStart ? -1 : _gesture == Gesture.ResizeEnd ? 1 : _gesture is Gesture.None or Gesture.Pending ? _hotEdge : 0;
                    if (hot != 0)
                    {
                        double hx = hot < 0 ? lx0 : lx1;
                        if (hx >= 0 && hx <= w) ctx.DrawLine(LoopEdgeHot, new Point(hx, 0), new Point(hx, h));
                    }
                }
            }

            // Bar numbers in mono 7. The almanac labels every 4 bars at its base scale
            // (16px a bar); zoomed in far enough that a single bar has room, every bar is named.
            int firstBar = (int)Math.Floor(_o._scrollBeats / _o._beatsPerBar);
            double barPx = _o._pixelsPerBeat * _o._beatsPerBar;
            int labelEvery = barPx >= 40 ? 1 : 4;
            for (int bar = firstBar; ; bar++)
            {
                double beat = bar * _o._beatsPerBar;
                double x = _o.BeatToX(beat);
                if (x > w) break;
                if (x < 0) continue;
                ctx.DrawLine(BarPen, new Point(x, h - 7), new Point(x, h));
                if (bar % labelEvery != 0) continue;
                var ft = new FormattedText((bar + 1).ToString(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, NotaFonts.Mono, NotaType.Axis, RulerText);
                ctx.DrawText(ft, new Point(x + 3, 3));
            }

            // Playhead flag: a 7×5 brass triangle at the top of the ruler.
            double px = _o.BeatToX(_o._playheadBeats);
            if (px >= -5 && px <= w + 5)
            {
                var tri = new StreamGeometry();
                using (var g = tri.Open())
                {
                    g.BeginFigure(new Point(px - 3.5, 0), true);
                    g.LineTo(new Point(px + 3.5, 0));
                    g.LineTo(new Point(px, 5));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(PlayheadBrush, null, tri);
            }

            // Playhead off screen (#105): a small arrow at that end of the ruler; a click on
            // it brings the playhead into view.
            int side = _o.PlayheadOffscreen();
            if (side != 0)
            {
                double ax = side < 0 ? 4 : w - 4, cy = h / 2, d = side < 0 ? 1 : -1;
                ctx.FillRectangle(ChromeBg, new Rect(side < 0 ? 0 : w - ArrowZone, 0, ArrowZone, h));   // over a bar label
                var arrow = new StreamGeometry();
                using (var g = arrow.Open())
                {
                    g.BeginFigure(new Point(ax, cy), true);
                    g.LineTo(new Point(ax + d * 6, cy - 4.5));
                    g.LineTo(new Point(ax + d * 6, cy + 4.5));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(PlayheadBrush, null, arrow);
            }

            // While a loop region is drawn, moved or resized: its start, end and length.
            if (_o._loopDragging && _o._loopE > _o._loopS)
            {
                var ft = new FormattedText(_o.LoopReadout(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, NotaFonts.Mono, NotaType.Axis, ReadoutText);
                double tx = Math.Clamp(_o.BeatToX(_o._loopE) + 6, 2, Math.Max(2, w - ft.Width - 6));
                var box = new Rect(tx - 3, 2, ft.Width + 6, h - 4);
                ctx.FillRectangle(ChromeBg, box);
                ctx.DrawText(ft, new Point(tx, (h - ft.Height) / 2));
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

            // Vertical grid: the snap grid's lines at this zoom — what you see is what you snap to.
            _o.DrawGrid(ctx, w, h);

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

            // Browser drag-over: the return / master the drop would land on.
            if (_o.FooterDropRow >= 0)
            {
                var r = new Rect(0, _o.FooterDropRow * FooterRowH, w, FooterRowH);
                ctx.FillRectangle(DropWash, r);
                ctx.DrawRectangle(null, DropEdgePen, r.Deflate(1));
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

            // Playhead across the footer lanes: a 1px brass line.
            double px = _o.BeatToX(_o._playheadBeats);
            if (px >= 0 && px <= w)
            {
                ctx.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, h));
            }
        }
    }
}
