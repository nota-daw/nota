// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement · Overview — the strip above the ruler. It maps the whole
// project onto one narrow band (a hairline per clip, one line per track, in its track
// colour) and draws the current viewport as a quiet frame over it, so the visible span is readable at a glance
// and reachable with one gesture: drag the window to scroll, drag its edges (or drag
// vertically) to zoom, click anywhere to jump there.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public sealed partial class ArrangementView
{
    private const double OverviewH = 34;

    private readonly OverviewControl _overview;
    private Border _overviewRow = null!;
    private TextBlock _overviewRange = null!;
    // Last playhead position in overview pixels — the 30 Hz tick repaints the strip only
    // when the marker actually moves a pixel (the mini-clips underneath are static).
    private int _ovPlayheadPx = int.MinValue;

    /// <summary>Show/hide the Overview strip (View ▸ Toggle overview).</summary>
    public bool ShowOverview
    {
        get => _overviewRow.IsVisible;
        set
        {
            if (_overviewRow.IsVisible == value) return;
            _overviewRow.IsVisible = value;
            if (value) { UpdateOverviewRange(); _overview.InvalidateVisual(); }
        }
    }

    // The strip row: the same header-column chrome as the ruler row (label + visible-range
    // readout on one line), then the strip itself over the lane column.
    private Border BuildOverviewRow()
    {
        var label = new TextBlock
        {
            Text = "OVERVIEW", Classes = { "SectionLabel" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        _overviewRange = new TextBlock
        {
            Text = "", FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _overviewRange.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        _overviewRange.BindResource(TextBlock.ForegroundProperty, "Brush.TextDisabled");
        var labels = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children = { label, _overviewRange },
        };

        var grid = new Grid { Height = OverviewH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var header = HeaderPanel(labels);
        header.BindResource(Border.BackgroundProperty, "Brush.BgSunken");   // the strip is a well, header included
        grid.Children.Add(header);
        Grid.SetColumn(_overview, 1);
        grid.Children.Add(_overview);

        // A hairline under the strip separates it from the ruler (structure over shadows).
        var row = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        row.BindResource(Border.BorderBrushProperty, "Brush.BorderDefault");
        return row;
    }

    // ---- view maths shared with the strip ---------------------------------

    /// <summary>Beats currently visible in the lane column.</summary>
    internal double ViewportBeats
        => _lanes.Bounds.Width > 0 && _pixelsPerBeat > 0 ? _lanes.Bounds.Width / _pixelsPerBeat : _totalBeats;

    /// <summary>The beat span the strip maps onto its width: the project length, stretched
    /// if the viewport somehow reaches past it (very low zoom) so the window always fits.</summary>
    internal double OverviewSpan => Math.Max(16, Math.Max(_totalBeats, _scrollBeats + ViewportBeats));

    /// <summary>Scroll + zoom in one step, as the strip's drags author them. Both are clamped
    /// (zoom to the toolbar's range, scroll to the scrollbar's) before the views repaint.</summary>
    internal void ApplyOverviewView(double pixelsPerBeat, double startBeat)
    {
        _pixelsPerBeat = Math.Clamp(pixelsPerBeat, 4, 240);
        _scrollBeats = Math.Max(0, startBeat);
        SyncScroll(_lanes.Bounds.Width);   // clamps the scroll to range + syncs the scrollbar
        Redraw();
    }

    /// <summary>Fit the whole project in the lane column (double-click on the strip).</summary>
    internal void ZoomToFitProject()
    {
        double w = _lanes.Bounds.Width;
        if (w <= 0) return;
        ApplyOverviewView(w / OverviewSpan, 0);
    }

    // Bar range readout under the strip label — refreshed with every redraw, so it follows
    // scroll, zoom and project growth.
    private void UpdateOverviewRange()
    {
        if (_overviewRange is null || !_overviewRow.IsVisible) return;
        int bpb = Math.Max(1, _beatsPerBar);
        int first = (int)Math.Floor(_scrollBeats / bpb) + 1;
        int last = Math.Max(first, (int)Math.Ceiling((_scrollBeats + ViewportBeats) / bpb));
        int total = Math.Max(last, (int)Math.Ceiling(OverviewSpan / bpb));
        _overviewRange.Text = $"bar {first}–{last} / {total}";
    }

    // Repaint the strip when the playhead crossed into another of its pixels (called from
    // the transport tick, where the rest of the strip is unchanged).
    private void TickOverviewPlayhead()
    {
        if (!_overviewRow.IsVisible) return;
        double w = _overview.Bounds.Width;
        if (w <= 0) return;
        int px = (int)(_playheadBeats / OverviewSpan * w);
        if (px == _ovPlayheadPx) return;
        _ovPlayheadPx = px;
        _overview.InvalidateVisual();
    }

    // ---- the strip --------------------------------------------------------

    private sealed class OverviewControl : Control
    {
        // The viewport window is a neutral frame with a faint lift — brass stays reserved for
        // the playhead and the loop, so the strip reads at a glance without a second accent.
        private static readonly IPen OvFrame = new Pen(NotaPalette.TextMuted, 1);
        private static readonly IBrush OvWindow = NotaPalette.Wash(NotaPalette.TextPrimary, 0x0D);
        private static readonly IBrush OvEmptyText = NotaPalette.TextDisabled;
        private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
        private static readonly Cursor EdgeCursor = new(StandardCursorType.SizeWestEast);

        // Mini-clip fills per palette index (deactivated clips keep the hue, lose the weight).
        private static readonly Dictionary<int, IBrush> _ovClip = new();
        private static readonly Dictionary<int, IBrush> _ovClipOff = new();
        private static IBrush MiniFill(int colorIndex, bool active)
        {
            var cache = active ? _ovClip : _ovClipOff;
            if (cache.TryGetValue(colorIndex, out var b)) return b;
            b = ClipTint(colorIndex, active ? 0.6 : 0.22);
            cache[colorIndex] = b;
            return b;
        }

        private const double EdgeGrab = 5;   // px around a window edge that grabs it
        private const double PadY = 5;
        private const double LineH = 2;       // one hairline per clip
        private const double LineStride = 3.4; // track-to-track pitch; tightens when the rows don't fit

        private const double AxisLock = 4;   // px of travel that commits a drag to one axis

        private enum Drag { None, Pan, EdgeLeft, EdgeRight }
        // A pan drag commits to one axis on its first few pixels: sideways scrolls at a
        // fixed zoom, up/down zooms. Without the lock, the hand's vertical wobble resized
        // the viewport window the whole way — most visibly once the scroll hit an end, where
        // breathing width was the only thing left to see.
        private enum Axis { Undecided, Horizontal, Vertical }

        private readonly ArrangementView _o;
        private Drag _drag;
        private Axis _axis;
        private double _grabFrac;      // where in the window the pan grab started (0..1)
        private double _pressX;        // press point, for the axis decision
        private double _pressY;        // vertical drag = zoom (Ableton's overview gesture)
        private double _pressPpb;      // zoom at press, so the vertical drag is absolute
        private double _fixedBeat;     // the window edge an edge-drag holds still

        public OverviewControl(ArrangementView o)
        {
            _o = o;
            ClipToBounds = true;
            Cursor = HandCursor;
        }

        private double SpanBeats => _o.OverviewSpan;
        private double BeatToX(double beat) => beat / SpanBeats * Bounds.Width;
        private double BeatAt(double x) => Math.Max(0, x / Math.Max(1, Bounds.Width) * SpanBeats);

        // ---- input ---------------------------------------------------------

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            double vb = _o.ViewportBeats;
            double x0 = BeatToX(_o._scrollBeats), x1 = BeatToX(_o._scrollBeats + vb);

            if (e.ClickCount == 2) { _o.ZoomToFitProject(); e.Handled = true; return; }

            _pressX = p.X;
            _pressY = p.Y;
            _pressPpb = _o._pixelsPerBeat;
            _axis = Axis.Undecided;

            if (Math.Abs(p.X - x0) <= EdgeGrab && x1 - x0 > 3 * EdgeGrab)
            {
                _drag = Drag.EdgeLeft;
                _fixedBeat = _o._scrollBeats + vb;          // the right edge stays put
            }
            else if (Math.Abs(p.X - x1) <= EdgeGrab && x1 - x0 > 3 * EdgeGrab)
            {
                _drag = Drag.EdgeRight;
                _fixedBeat = _o._scrollBeats;               // the left edge stays put
            }
            else
            {
                // Inside the window → grab it where it was clicked. Outside → jump so the
                // window centres on the click, then keep dragging from its middle.
                _drag = Drag.Pan;
                bool inside = p.X >= x0 && p.X <= x1;
                _grabFrac = inside && vb > 0 ? Math.Clamp((BeatAt(p.X) - _o._scrollBeats) / vb, 0, 1) : 0.5;
                if (!inside) _o.ApplyOverviewView(_o._pixelsPerBeat, BeatAt(p.X) - vb * 0.5);
            }
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag == Drag.None)
            {
                double ex0 = BeatToX(_o._scrollBeats), ex1 = BeatToX(_o._scrollBeats + _o.ViewportBeats);
                bool onEdge = ex1 - ex0 > 3 * EdgeGrab
                              && (Math.Abs(p.X - ex0) <= EdgeGrab || Math.Abs(p.X - ex1) <= EdgeGrab);
                Cursor = onEdge ? EdgeCursor : HandCursor;
                return;
            }

            double laneW = _o._lanes.Bounds.Width;
            if (laneW <= 0) return;

            if (_drag == Drag.Pan)
            {
                // Commit to an axis once the drag has travelled far enough to mean one.
                if (_axis == Axis.Undecided)
                {
                    double dx = Math.Abs(p.X - _pressX), dy = Math.Abs(p.Y - _pressY);
                    if (dx > AxisLock || dy > AxisLock) _axis = dx >= dy ? Axis.Horizontal : Axis.Vertical;
                }
                // Sideways: scroll at the zoom the drag started with, so the window keeps its
                // width — pushing past either end simply stops. Up/down: zoom (drag down = in).
                double ppb = _axis == Axis.Vertical
                    ? Math.Clamp(_pressPpb * Math.Pow(1.012, p.Y - _pressY), 4, 240)
                    : _pressPpb;
                double vb = laneW / ppb;
                _o.ApplyOverviewView(ppb, BeatAt(p.X) - _grabFrac * vb);
                return;
            }

            // Edge drag: the opposite edge is pinned, so the window's width — the zoom —
            // follows the cursor. A minimum span keeps it from collapsing to nothing.
            double beat = BeatAt(p.X);
            double span = _drag == Drag.EdgeLeft ? _fixedBeat - beat : beat - _fixedBeat;
            span = Math.Max(laneW / 240.0, span);
            double newPpb = Math.Clamp(laneW / span, 4, 240);
            double start = _drag == Drag.EdgeLeft ? _fixedBeat - laneW / newPpb : _fixedBeat;
            _o.ApplyOverviewView(newPpb, start);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            _drag = Drag.None;
            _axis = Axis.Undecided;
            e.Pointer.Capture(null);
        }

        // Wheel over the strip drives the timeline exactly as it does over the lanes
        // (⌘ zooms toward the cursor, shift/horizontal scrolls), so the gesture set is one.
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            double laneX = (BeatAt(e.GetPosition(this).X) - _o._scrollBeats) * _o._pixelsPerBeat;
            _o.HandleLaneWheel(e, laneX);
            if (!e.Handled) { _o.ScrollByBeats(-WheelInput.Pixels(e.Delta.Y) / _o._pixelsPerBeat); e.Handled = true; }
        }

        // ---- paint ---------------------------------------------------------

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            ctx.FillRectangle(ChromeBg, new Rect(0, 0, w, h));

            double span = SpanBeats;

            int n = _o._tracks.Count;
            if (n == 0)
            {
                var hint = new FormattedText("the project appears here", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 10, OvEmptyText);
                ctx.DrawText(hint, new Point(10, (h - hint.Height) / 2));
            }
            else
            {
                // One hairline per row that holds clips, in row order — the strip reads as the
                // arrangement seen from far away. An open group's row is skipped (its children
                // follow with their own lines); a collapsed one stands in for them. Rows keep a
                // fixed pitch and squeeze together only when the strip can't hold them all.
                int lines = 0;
                for (int i = 0; i < n; i++)
                    if (!_o._tracks[i].IsGroup || _o._collapsed.Contains(_o._tracks[i].Id)) lines++;
                double rowH = Math.Min(LineStride, (h - 2 * PadY - LineH) / Math.Max(1, lines - 1));
                double bandH = Math.Min(LineH, Math.Max(1, rowH - 0.6));
                int line = 0;
                for (int i = 0; i < n; i++)
                {
                    var t = _o._tracks[i];
                    bool collapsedGroup = t.IsGroup && _o._collapsed.Contains(t.Id);
                    if (t.IsGroup && !collapsedGroup) continue;
                    double y = PadY + line++ * rowH;
                    if (collapsedGroup)
                    {
                        foreach (var m in t.GroupMini)
                            DrawMini(ctx, m.Start, m.Length, m.ColorIndex, true, y, bandH, span, w);
                    }
                    else
                    {
                        foreach (var c in t.Clips)
                            DrawMini(ctx, c.StartBeat, c.LengthBeats, t.ColorIndex, c.Active, y, bandH, span, w);
                    }
                }
            }

            // The viewport window: a neutral frame over a faint lift (its edges still grab).
            double x0 = BeatToX(_o._scrollBeats), x1 = BeatToX(_o._scrollBeats + _o.ViewportBeats);
            // Keep the window at least 2px wide, and never past the right end (a 2px window
            // at the very end is pushed left, not clamped to nothing).
            x0 = Math.Clamp(x0, 0, Math.Max(0, w - 2));
            x1 = Math.Clamp(x1, x0 + 2, Math.Max(x0 + 2, w));
            ctx.DrawRectangle(OvWindow, OvFrame, new Rect(x0 + 0.5, 2.5, Math.Max(1, x1 - x0 - 1), h - 5), 2, 2);

            // Loop region: a thin brass band along the top edge (the ruler owns the full
            // brace). Drawn over the scrim — a loop outside the viewport still has to show.
            if (_o._loopActive && _o._loopE > _o._loopS)
            {
                double lx0 = BeatToX(_o._loopS), lx1 = BeatToX(_o._loopE);
                ctx.FillRectangle(LoopBrace, new Rect(lx0, 0, Math.Max(1, lx1 - lx0), 3));
            }

            // Playhead over the whole project.
            double px = BeatToX(_o._playheadBeats);
            if (px >= 0 && px <= w) ctx.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, h));
        }

        private static void DrawMini(DrawingContext ctx, double startBeat, double lengthBeats,
                                     int colorIndex, bool active, double y, double bandH,
                                     double span, double w)
        {
            double x0 = startBeat / span * w;
            double x1 = (startBeat + lengthBeats) / span * w;
            if (x1 < 0 || x0 > w) return;
            ctx.DrawRectangle(MiniFill(colorIndex, active), null,
                new RoundedRect(new Rect(Math.Max(0, x0), y, Math.Max(1.5, Math.Min(w, x1) - Math.Max(0, x0)), bandH), bandH / 2));
        }
    }
}
