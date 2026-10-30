// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Take lanes (#187, specs/067): the passes of an audio loop recording, one lane per take under
// the track's clip area, folded by default. Swiping a range on a lane comps that take in over
// the range; the lane's menu uses a whole take, deletes it or flattens the track's takes. Every
// edit is one engine call and one undo step; the comp itself is the track's ordinary clips.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class ArrangementView
{
    internal sealed class TakeVM
    {
        public int Id;
        public int Number;
        public double StartBeat;
        public double LengthBeats;
        public float[]? Peaks;
        public int PeakCount;
    }

    private const double TakeLaneH = 36;
    private const int TakePeakBuckets = 1024;

    // Open take lanes are view state, like a group's fold: not saved, folded on open.
    private readonly HashSet<int> _takesOpen = new();
    // A take's audio never changes, so its peaks are fetched once per (track, take, sample).
    private Dictionary<(int track, int take, long sample), (float[] peaks, int count)> _takePeaks = new();
    private readonly List<(Border lane, int trackId, int index)> _takeHeaders = new();

    internal bool TakesOpen(TrackVM t) => t.Takes.Count > 0 && _takesOpen.Contains(t.Id);
    private double TakesHeight(TrackVM t) => TakesOpen(t) ? t.Takes.Count * TakeLaneH : 0;

    private void ToggleTakes(int trackId)
    {
        if (!_takesOpen.Remove(trackId)) _takesOpen.Add(trackId);
        Refresh();
    }

    private void LoadTakes(IAudioEngine eng, TrackVM tvm, Dictionary<(int, int, long), (float[], int)> peaks)
    {
        int n = eng.TakeCount(tvm.Id);
        for (int k = 0; k < n; k++)
        {
            if (!eng.TryGetTakeInfo(tvm.Id, k, out var info)) continue;
            var key = (tvm.Id, info.Id, info.SampleId);
            if (!_takePeaks.TryGetValue(key, out var p))
            {
                var buf = new float[TakePeakBuckets * 2];
                p = (buf, eng.GetTakePeaks(tvm.Id, k, buf, TakePeakBuckets));
            }
            peaks[key] = p;
            tvm.Takes.Add(new TakeVM
            {
                Id = info.Id, Number = info.Number, StartBeat = info.StartBeat, LengthBeats = info.LengthBeats,
                Peaks = p.peaks, PeakCount = p.count,
            });
        }
    }

    /// <summary>The take lane under <paramref name="y"/>: its row and the take, or null.</summary>
    internal (int row, TakeVM take)? TakeLaneAt(double y)
    {
        int row = RowAtY(y);
        if (row < 0 || !TakesOpen(_tracks[row])) return null;
        double top = RowTop(row) + ClipAreaHeightAt(row);
        if (y < top) return null;
        int k = (int)((y - top) / TakeLaneH);
        var takes = _tracks[row].Takes;
        return k >= 0 && k < takes.Count ? (row, takes[k]) : null;
    }

    private void CompTake(int trackId, int takeId, double a, double b)
    {
        if (_engine is null || Math.Abs(b - a) < 1e-9) return;
        if (_engine.CompTake(trackId, takeId, Math.Min(a, b), Math.Max(a, b))) Refresh();
    }

    private MenuFlyout BuildTakeMenu(int trackId, TakeVM take)
    {
        var flyout = new MenuFlyout();
        var header = MenuKit.Header($"Take {take.Number}", null, "");
        var use = MenuKit.Item("Use whole take", GlyphKind.Check,
            () => CompTake(trackId, take.Id, take.StartBeat, take.StartBeat + take.LengthBeats));
        var flatten = MenuKit.Danger("Flatten takes", GlyphKind.Flatten,
            () => { if (_engine?.FlattenTakes(trackId) == true) { _takesOpen.Remove(trackId); Refresh(); } });
        var del = MenuKit.Danger("Delete take", GlyphKind.Trash,
            () => { if (_engine?.DeleteTake(trackId, take.Id) == true) Refresh(); });
        MenuKit.AddGroups(flyout.Items, new Control[] { header }, new Control[] { use, flatten }, new Control[] { del });
        return flyout;
    }

    // Header disclosure on an audio track with takes: ▸/▾ and the take count.
    private Control? TakesToggle(TrackVM t)
    {
        if (t.Takes.Count == 0) return null;
        bool open = _takesOpen.Contains(t.Id);
        var toggle = new Border
        {
            Background = Brushes.Transparent, Padding = new Thickness(2, 0), Margin = new Thickness(0, 0, 4, 0),
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 3,
                Children =
                {
                    new Glyph(open ? GlyphKind.ChevronDown : GlyphKind.ChevronRight, 8)
                        { Foreground = Brush("Brush.TextSecondary"), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock
                    {
                        Text = $"{t.Takes.Count}", FontSize = 9, Classes = { "Mono" },
                        Foreground = Brush("Brush.TextSecondary"), VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        ToolTip.SetTip(toggle, open ? "Hide takes" : $"Show {t.Takes.Count} takes");
        toggle.PointerPressed += (_, e) => { e.Handled = true; ToggleTakes(t.Id); };
        return toggle;
    }

    // Header column beside open lanes: "Take N" per lane, with the lane's menu on right-click.
    private void AddTakeHeaders()
    {
        _takeHeaders.Clear();
        foreach (var t in _tracks)
        {
            if (!TakesOpen(t)) continue;
            for (int k = 0; k < t.Takes.Count; k++)
            {
                var take = t.Takes[k];
                int trackId = t.Id;
                var lane = new Border
                {
                    Height = TakeLaneH, Background = Brush("Brush.BgSunken"),
                    BorderBrush = Brush("Brush.BorderDefault"), BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(22, 0, 8, 0),
                    Child = new TextBlock
                    {
                        Text = $"Take {take.Number}", FontSize = 11, Foreground = Brush("Brush.TextSecondary"),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                lane.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(lane).Properties.IsRightButtonPressed) return;
                    e.Handled = true;
                    BuildTakeMenu(trackId, take).ShowAt(lane, showAtPointer: true);
                };
                _takeHeaders.Add((lane, trackId, k));
                _headers.Children.Add(lane);
            }
        }
        PlaceTakeHeaders();
    }

    private void PlaceTakeHeaders()
    {
        foreach (var (lane, trackId, k) in _takeHeaders)
        {
            int i = _tracks.FindIndex(t => t.Id == trackId);
            lane.IsVisible = i >= 0 && TakesOpen(_tracks[i]);
            if (!lane.IsVisible) continue;
            lane.Width = _headerW;
            Canvas.SetLeft(lane, 0);
            Canvas.SetTop(lane, RowTop(i) + ClipAreaHeightAt(i) + k * TakeLaneH);
        }
    }

    private static readonly IBrush TakeLaneBg = NotaPalette.Wash(NotaPalette.BgSunken, 0xC0);

    private sealed partial class LaneControl
    {
        // A swipe on a take lane: the take, its track, and the snapped range so far.
        // A press that never travels is a click and leaves the comp alone (spec US3).
        private (int trackId, int takeId, double a, double b)? _takeSwipe;
        private double _takePressX;
        private bool _takeMoved;

        private double BeatAt(double x) => _o._scrollBeats + x / _o._pixelsPerBeat;

        private bool TakeLanePressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            if (_o.TakeLaneAt(pt.Position.Y) is not { } hit) return false;
            e.Handled = true;
            int trackId = _o._tracks[hit.row].Id;
            if (pt.Properties.IsRightButtonPressed)
            {
                _o.BuildTakeMenu(trackId, hit.take).ShowAt(this, showAtPointer: true);
                return true;
            }
            if (!pt.Properties.IsLeftButtonPressed) return true;
            double a = Math.Max(0, _o.SnapMaybe(BeatAt(pt.Position.X), e.KeyModifiers));
            _takePressX = pt.Position.X;
            _takeMoved = false;
            _takeSwipe = (trackId, hit.take.Id, a, a);
            e.Pointer.Capture(this);
            InvalidateVisual();
            return true;
        }

        private void TakeSwipeMoved(PointerEventArgs e)
        {
            if (_takeSwipe is not { } s) return;
            var pos = e.GetPosition(this);
            if (!_takeMoved && Math.Abs(pos.X - _takePressX) < DragThreshold) return;
            _takeMoved = true;
            double b = Math.Max(0, _o.SnapMaybe(BeatAt(pos.X), e.KeyModifiers));
            if (b == s.b) return;
            _takeSwipe = s with { b = b };
            InvalidateVisual();
        }

        private void TakeSwipeReleased(PointerReleasedEventArgs e)
        {
            var s = _takeSwipe!.Value;
            bool moved = _takeMoved;
            _takeSwipe = null;
            _takeMoved = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            if (moved) _o.CompTake(s.trackId, s.takeId, s.a, s.b);
        }

        // The lanes under a track's clip area: each take's recorded range dimmed, the sections
        // the comp plays from it in the track colour, and the swipe in progress.
        private void DrawTakeLanes(DrawingContext ctx, TrackVM t, double top, double w)
        {
            var (_, _, _, body) = ClipColors(t.ColorIndex);
            var ink = ClipInk(t.ColorIndex);
            for (int k = 0; k < t.Takes.Count; k++)
            {
                var take = t.Takes[k];
                double y = top + k * TakeLaneH;
                ctx.FillRectangle(TakeLaneBg, new Rect(0, y, w, TakeLaneH));
                ctx.DrawLine(BeatPen, new Point(0, y + TakeLaneH), new Point(w, y + TakeLaneH));

                double x0 = _o.BeatToX(take.StartBeat), x1 = _o.BeatToX(take.StartBeat + take.LengthBeats);
                var r = new Rect(x0, y + 3, Math.Max(2, x1 - x0), TakeLaneH - 6);
                if (x1 >= 0 && x0 <= w)
                {
                    using (ctx.PushOpacity(0.35))
                    {
                        ctx.DrawRectangle(body, null, r, NotaRadius.ClipValue, NotaRadius.ClipValue);
                        DrawTakePeaks(ctx, take, r, r, ink);
                    }
                    foreach (var c in t.Clips)
                    {
                        if (c.TakeId != take.Id) continue;
                        double cx0 = Math.Max(r.X, _o.BeatToX(c.StartBeat)), cx1 = Math.Min(r.Right, _o.BeatToX(c.StartBeat + c.LengthBeats));
                        if (cx1 <= cx0 || cx1 < 0 || cx0 > w) continue;
                        var used = new Rect(cx0, r.Y, cx1 - cx0, r.Height);
                        ctx.DrawRectangle(body, null, used, NotaRadius.ClipValue, NotaRadius.ClipValue);
                        DrawTakePeaks(ctx, take, r, used, ink);
                    }
                }

                if (_takeSwipe is { } s && s.trackId == t.Id && s.takeId == take.Id && s.a != s.b)
                {
                    double sx0 = _o.BeatToX(Math.Min(s.a, s.b)), sx1 = _o.BeatToX(Math.Max(s.a, s.b));
                    var band = new Rect(sx0, y + 1, sx1 - sx0, TakeLaneH - 2);
                    ctx.FillRectangle(MarqueeFill, band);
                    ctx.DrawRectangle(null, MarqueePen, band);
                }

                // The lane's name stays at the left edge while the take scrolls past.
                var ft = new FormattedText($"Take {take.Number}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, NotaFonts.SansSemiBold, 9.5, RulerText);
                double lx = Math.Max(4, Math.Min(x0 + 4, w - ft.Width - 8));
                var plate = new Rect(lx - 3, y + 4, ft.Width + 6, ft.Height + 1);
                ctx.DrawRectangle(LabelPlate, null, plate, 2, 2);
                ctx.DrawText(ft, new Point(lx, y + 4));
            }
        }

        // The take's waveform over `full` (its recorded range), painted only inside `part`.
        private void DrawTakePeaks(DrawingContext ctx, TakeVM take, Rect full, Rect part, IBrush brush)
        {
            if (take.Peaks is not { } pk || take.PeakCount <= 0 || full.Width < 1) return;
            double mid = full.Y + full.Height / 2, amp = full.Height / 2 - 1;
            int n = take.PeakCount;
            double px0 = Math.Max(Math.Max(part.X, full.X), 0), px1 = Math.Min(Math.Min(part.Right, full.Right), Bounds.Width);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
                for (double px = Math.Floor(px0); px < px1; px++)
                {
                    int b0 = (int)((px - full.X) / full.Width * n), b1 = (int)((px + 1 - full.X) / full.Width * n);
                    b0 = Math.Clamp(b0, 0, n - 1);
                    b1 = Math.Clamp(Math.Max(b1, b0 + 1), b0 + 1, n);
                    float min = 0, max = 0;
                    for (int b = b0; b < b1; b++) { min = Math.Min(min, pk[b * 2]); max = Math.Max(max, pk[b * 2 + 1]); }
                    min = Math.Clamp(min, -1f, 1f); max = Math.Clamp(max, -1f, 1f);
                    AddRect(g, px, mid - max * amp, 1, Math.Max(1, (max - min) * amp));
                }
            using (ctx.PushClip(part))
                ctx.DrawGeometry(brush, null, geo);
        }
    }
}
