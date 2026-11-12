// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Tempo editing shares the arrangement coordinate model; preview maps use the
// native compiler without publishing edits or touching the reference recording.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class ArrangementView
{
    private TempoLane _tempoLane = null!;
    private Grid _tempoRow = null!;
    private bool _hasTempoMap;
    private ITempoPreview? _tempoPreview;
    private double _tempoHeight = 112;
    private bool _tempoCollapsed;
    private double _tempoScrollSeconds;
    private bool TempoAxis => _hasTempoMap || _tempoPreview is not null;
    private double TempoSeconds(double beat) => _tempoPreview?.BeatToSeconds(beat) ?? _engine?.BeatToSeconds(beat) ?? beat * .5;
    private double TempoBeat(double seconds) => _tempoPreview?.SecondsToBeat(seconds) ?? _engine?.SecondsToBeat(seconds) ?? seconds * 2;
    private double AxisOriginSeconds => _tempoPreview is not null ? _tempoScrollSeconds : TempoSeconds(_scrollBeats);
    private double SecondsToX(double seconds) => (seconds - AxisOriginSeconds) * _pixelsPerBeat * 2;
    private double XToSeconds(double x) => AxisOriginSeconds + x / (_pixelsPerBeat * 2);
    private double XToBeat(double x) => TempoAxis ? TempoBeat(XToSeconds(x)) : _scrollBeats + x / _pixelsPerBeat;
    private double ScrollBeatForX(double beat, double x, double? pixelsPerBeat = null)
    {
        double ppb = pixelsPerBeat ?? _pixelsPerBeat;
        return Math.Max(0, TempoAxis ? TempoBeat(TempoSeconds(beat) - x / (ppb * 2)) : beat - x / ppb);
    }
    public int TimeSignatureDenominator { get; set; } = 4;
    private double TempoBarLength => _beatsPerBar * 4.0 / Math.Max(1, TimeSignatureDenominator);
    private double GridBarLength => TempoAxis ? TempoBarLength : _beatsPerBar;

    private Control BuildTempoRow()
    {
        _tempoLane = new TempoLane(this);
        var toggle = new Button { Content = Loc.T("tempo.track"), Classes = { "ghost" },
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
        toggle.Click += (_, _) =>
        {
            _tempoCollapsed = !_tempoCollapsed;
            _tempoRow.Height = _tempoCollapsed ? 24 : _tempoHeight;
            _tempoLane.IsVisible = !_tempoCollapsed;
        };
        ToolTip.SetTip(toggle, Loc.T("tempo.collapse"));
        var header = HeaderPanel(toggle);
        _tempoRow = new Grid { Height = _tempoHeight, ClipToBounds = true, Children = { header, _tempoLane } };
        AddSideRow(_tempoRow, header, _tempoLane);
        DockPanel.SetDock(_tempoRow, Dock.Top);
        return _tempoRow;
    }

    private void ReloadTempo()
    {
        if (_engine is not { } engine || _tempoLane.IsDragging) return;
        var points = engine.TempoPoints;
        _hasTempoMap = points.Count > 0;
        _tempoLane.Load(points.Count > 0 ? points : new[] { new TempoPoint(0, engine.TempoAt(0)) });
    }

    private void PreviewTempo(IReadOnlyList<TempoPoint> points)
    {
        if (_engine is null) return;
        if (_tempoPreview is null) _tempoScrollSeconds = _engine.BeatToSeconds(_scrollBeats);
        var next = _engine.CreateTempoPreview(points);
        _tempoPreview?.Dispose();
        _tempoPreview = next;
        Redraw();
    }

    private void EndTempoPreview(bool commit, IReadOnlyList<TempoPoint> points)
    {
        if (commit && _engine is { } engine)
        {
            engine.SetDeferWarpBuild(true);
            try { engine.SetTempoPoints(points); }
            finally { engine.SetDeferWarpBuild(false); }
            _hasTempoMap = true;
            _scrollBeats = engine.SecondsToBeat(_tempoScrollSeconds);
            BuildTempoWarpCaches();
        }
        _tempoPreview?.Dispose(); _tempoPreview = null;
        if (commit) { Refresh(); SessionChanged?.Invoke(); }
        else { ReloadTempo(); Redraw(); }
    }

    private async void BuildTempoWarpCaches()
    {
        var engine = _engine;
        if (engine is null) return;
        while (ReferenceEquals(engine, _engine) && engine.WarpBuildPoll() > 0)
            await Task.Delay(15);
    }

    private (double start, double end) ClipScreenEdges(ClipVM clip)
    {
        if (_tempoPreview is not null && !clip.IsMidi && clip.PeakSource is { WarpEnabled: 0 } && _engine is { } engine)
            return (SecondsToX(engine.BeatToSeconds(clip.StartBeat)), SecondsToX(engine.BeatToSeconds(clip.StartBeat + clip.LengthBeats)));
        return (BeatToX(clip.StartBeat), BeatToX(clip.StartBeat + clip.LengthBeats));
    }

    private sealed class TempoLane : Control
    {
        private readonly ArrangementView _o;
        private List<TempoPoint> _points = new();
        private TempoPoint[] _before = Array.Empty<TempoPoint>();
        private readonly HashSet<int> _selection = new();
        private enum Drag { None, Point, Handle1, Handle2, Marquee, Scale, Resize }
        private Drag _drag;
        private int _index = -1;
        private Point _press, _pointer;
        private double _pressBeat, _pressBpm, _barBeat, _minBpm = 30, _maxBpm = 240, _startHeight;
        private bool _moved, _ctrl, _inside, _scaleChanged;
        private double[] _snapTimes = Array.Empty<double>();
        private double _scaleTargetSeconds, _scaleOriginSeconds, _scaleSecondsPerPixel;
        private static readonly Cursor ScaleCursor = new(StandardCursorType.SizeWestEast);
        private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeNorthSouth);
        private static readonly Cursor PointCursor = new(StandardCursorType.Cross);
        private TopLevel? _top;
        public bool IsDragging => _drag != Drag.None;
        public TempoLane(ArrangementView owner) {
            _o = owner; Focusable = true; ClipToBounds = true;
            LostFocus += (_, _) => { if (IsDragging) Cancel(); };
        }
        public void Load(IReadOnlyList<TempoPoint> points)
        {
            bool changed = !_points.SequenceEqual(points);
            _points = points.ToList();
            if (changed) _selection.Clear();
            _minBpm = Math.Max(1, Math.Min(30, _points.Min(p => p.Bpm) * .8));
            _maxBpm = Math.Min(999, Math.Max(240, _points.Max(p => p.Bpm) * 1.2));
            InvalidateVisual();
        }
        private double Y(double bpm) => 20 + (1 - (bpm - _minBpm) / (_maxBpm - _minBpm)) * Math.Max(1, Bounds.Height - 36);
        private double Bpm(double y) => Math.Clamp(_minBpm + (1 - (y - 20) / Math.Max(1, Bounds.Height - 36)) * (_maxBpm - _minBpm), 1, 999);
        private Point PointAt(int i) => new(_o.BeatToX(_points[i].Beat), Y(_points[i].Bpm));
        private Point Handle(int i, bool second)
        {
            var a = _points[i]; var b = _points[i + 1];
            double x = second ? a.X2 : a.X1, y = second ? a.Y2 : a.Y1;
            return new Point(_o.BeatToX(a.Beat + (b.Beat - a.Beat) * x), Y(a.Bpm + ((b.IncomingBpm > 0 ? b.IncomingBpm : b.Bpm) - a.Bpm) * y));
        }
        private int Hit(Point pos)
        {
            for (int i = 0; i < _points.Count; ++i) if (Distance(PointAt(i), pos) <= 7) return i;
            return -1;
        }
        private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _top = TopLevel.GetTopLevel(this);
            _top?.AddHandler(KeyDownEvent, TrackKey, RoutingStrategies.Tunnel, handledEventsToo: true);
            _top?.AddHandler(KeyUpEvent, TrackKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            Cancel();
            _top?.RemoveHandler(KeyDownEvent, TrackKey);
            _top?.RemoveHandler(KeyUpEvent, TrackKey);
            _top = null;
            base.OnDetachedFromVisualTree(e);
        }
        private void TrackKey(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && IsDragging) { Cancel(); e.Handled = true; return; }
            bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            if (e.RoutedEvent == KeyUpEvent && e.Key is Key.LeftCtrl or Key.RightCtrl) ctrl = false;
            if (_ctrl != ctrl) { _ctrl = ctrl; UpdateCursor(); InvalidateVisual(); }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Cancel(); _selection.Clear(); InvalidateVisual(); e.Handled = true; }
            else if (e.Key is Key.Delete or Key.Back)
            {
                if (_selection.Count > 0)
                {
                    var next = TempoMapping.DeleteSelected(_points, _selection);
                    _selection.Clear();
                    if (next.Length != _points.Count) Commit(next);
                }
                e.Handled = true; // an initial-only selection must never delete clips underneath
            }
            else base.OnKeyDown(e);
        }
        private void UpdateCursor() => Cursor = _ctrl ? ScaleCursor : _pointer.Y >= Bounds.Height - 5 ? ResizeCursor : PointCursor;
        protected override void OnPointerEntered(PointerEventArgs e) {
            _inside = true; _ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            _pointer = e.GetPosition(this); UpdateCursor(); InvalidateVisual(); base.OnPointerEntered(e);
        }
        protected override void OnPointerExited(PointerEventArgs e) { _inside = false; InvalidateVisual(); base.OnPointerExited(e); }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (_o._engine is null) return;
            Focus(); _pointer = _press = e.GetPosition(this);
            _ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            _pressBeat = _o.XToBeat(_press.X); _pressBpm = Bpm(_press.Y);
            _before = _points.ToArray(); _moved = _scaleChanged = false;
            var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsRightButtonPressed) { _drag = Drag.Marquee; _selection.Clear(); }
            else if (!properties.IsLeftButtonPressed) return;
            else if (_press.Y >= Bounds.Height - 5) { _drag = Drag.Resize; _startHeight = _o._tempoHeight; }
            else if (_ctrl)
            {
                _barBeat = Math.Max(_o.TempoBarLength, Math.Round(_pressBeat / _o.TempoBarLength) * _o.TempoBarLength);
                _drag = Drag.Scale;
                _scaleOriginSeconds = _o._engine.BeatToSeconds(_o._scrollBeats);
                _scaleSecondsPerPixel = _o.TempoAxis ? 1 / (_o._pixelsPerBeat * 2)
                    : 60 / (_o._engine.TempoAt(_o._scrollBeats) * _o._pixelsPerBeat);
                _o._tempoScrollSeconds = _scaleOriginSeconds;
                _snapTimes = BuildSnapTimes();
            }
            else
            {
                foreach (int i in _selection.OrderBy(i => i))
                    if (i + 1 < _points.Count && _points[i].Shape == TempoShape.Smooth)
                    {
                        if (Distance(Handle(i, false), _press) <= 7) { _drag = Drag.Handle1; _index = i; break; }
                        if (Distance(Handle(i, true), _press) <= 7) { _drag = Drag.Handle2; _index = i; break; }
                    }
                if (_drag == Drag.None)
                {
                    _index = Hit(_press); _selection.Clear();
                    if (_index >= 0)
                    {
                        _selection.Add(_index);
                        if (e.ClickCount == 2) { ShowBpmEditor(_index); e.Handled = true; return; }
                        _drag = Drag.Point;
                    }
                    else if (e.ClickCount == 2)
                    {
                        double beat = Math.Max(0, _o.Snap(_pressBeat));
                        if (!_points.Any(p => Math.Abs(p.Beat - beat) < 1e-6))
                            Commit(_points.Append(new TempoPoint(beat, _pressBpm)).OrderBy(p => p.Beat).ToArray());
                        e.Handled = true; return;
                    }
                    else { InvalidateVisual(); e.Handled = true; return; }
                }
            }
            e.Pointer.Capture(this); e.Handled = true; InvalidateVisual();
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            _pointer = e.GetPosition(this); _ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            UpdateCursor();
            if (_drag == Drag.None) { InvalidateVisual(); return; }
            _moved |= Distance(_pointer, _press) > 4;
            if (!_moved) return;
            switch (_drag)
            {
                case Drag.Resize:
                    _o._tempoHeight = Math.Clamp(_startHeight + _pointer.Y - _press.Y, 64, 360);
                    _o._tempoRow.Height = _o._tempoHeight; break;
                case Drag.Marquee:
                    double beat = _o.XToBeat(_pointer.X), bpm = Bpm(_pointer.Y);
                    _selection.Clear();
                    for (int i = 0; i < _points.Count; ++i)
                        if (_points[i].Beat >= Math.Min(beat, _pressBeat) && _points[i].Beat <= Math.Max(beat, _pressBeat) &&
                            _points[i].Bpm >= Math.Min(bpm, _pressBpm) && _points[i].Bpm <= Math.Max(bpm, _pressBpm)) _selection.Add(i);
                    break;
                case Drag.Point:
                    // Use the original time map while dragging, so preview grid movement
                    // cannot feed back into the point's own pointer coordinates.
                    double b = _o._engine!.SecondsToBeat(_o.XToSeconds(_pointer.X));
                    if (!_o.TempoAxis) b = _o._scrollBeats + _pointer.X / _o._pixelsPerBeat;
                    b = _index == 0 ? 0 : Math.Clamp(_o.SnapMaybe(b, e.KeyModifiers), _before[_index - 1].Beat + 1e-5,
                        _index + 1 < _before.Length ? _before[_index + 1].Beat - 1e-5 : 1e8);
                    _points[_index] = _before[_index] with { Beat = b, Bpm = Bpm(_pointer.Y), IncomingBpm = 0 };
                    _o.PreviewTempo(_points); break;
                case Drag.Handle1 or Drag.Handle2:
                    var a = _points[_index]; var end = _points[_index + 1];
                    double hb = _o._engine!.SecondsToBeat(_o.XToSeconds(_pointer.X));
                    double hx = Math.Clamp((hb - a.Beat) / (end.Beat - a.Beat), 0, 1);
                    double diff = (end.IncomingBpm > 0 ? end.IncomingBpm : end.Bpm) - a.Bpm;
                    double hy = Math.Abs(diff) < 1e-9 ? .5 : Math.Clamp((Bpm(_pointer.Y) - a.Bpm) / diff, 0, 1);
                    _points[_index] = _drag == Drag.Handle1 ? a with { X1 = Math.Min(hx, a.X2), Y1 = hy }
                        : a with { X2 = Math.Max(hx, a.X1), Y2 = hy };
                    _o.PreviewTempo(_points); break;
                case Drag.Scale:
                    double seconds = SnapSeconds(_scaleOriginSeconds + _pointer.X * _scaleSecondsPerPixel);
                    if (TempoMapping.MapBar(_o._engine!, _before, _barBeat, seconds, (e.KeyModifiers & KeyModifiers.Alt) == 0) is { } mapped)
                    { _points = mapped.ToList(); _o.PreviewTempo(_points); _scaleChanged = true; _scaleTargetSeconds = seconds; }
                    else { _scaleChanged = false; _points = _before.ToList(); if (_o._tempoPreview is not null) _o.EndTempoPreview(false, _before); }
                    break;
            }
            InvalidateVisual(); e.Handled = true;
        }
        private double[] BuildSnapTimes()
        {
            if (_o._engine is not { } engine) return Array.Empty<double>();
            var times = _before.Select(p => engine.BeatToSeconds(p.Beat)).ToList();
            // Existing warp markers include the detected transients from 'To transients'.
            foreach (var t in _o._tracks)
                foreach (var c in t.Clips.Where(c => !c.IsMidi))
                {
                    if (!engine.TryGetAudioClipInfo(t.Id, c.ClipIndex, out var clip)) continue;
                    var source = new double[4096]; var beats = new double[4096];
                    int count = Math.Min(source.Length, engine.GetClipWarpMarkers(t.Id, c.ClipIndex, source, beats));
                    bool hasSample = engine.TryGetSampleInfo(clip.SampleId, out var sample);
                    for (int i = 0; i < count; ++i)
                    {
                        double time = clip.WarpEnabled != 0 ? engine.BeatToSeconds(c.StartBeat + beats[i] - clip.WarpPlayStart)
                            : hasSample && sample.SampleRate > 0 ? engine.BeatToSeconds(c.StartBeat) + (source[i] - clip.SourceOffsetFrames) / (sample.SampleRate * Math.Pow(2, clip.PitchSemitones / 12.0)) : double.NaN;
                        if (double.IsFinite(time) && time >= engine.BeatToSeconds(c.StartBeat) && time <= engine.BeatToSeconds(c.StartBeat + c.LengthBeats)) times.Add(time);
                    }
                }
            return times.OrderBy(t => t).ToArray();
        }
        private double SnapSeconds(double seconds)
        {
            double best = 7 * (_drag == Drag.Scale ? _scaleSecondsPerPixel : 1 / (_o._pixelsPerBeat * 2)), result = seconds;
            foreach (double time in _snapTimes)
                if (Math.Abs(time - seconds) < best) { best = Math.Abs(time - seconds); result = time; }
            return result;
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            var drag = _drag; _drag = Drag.None;
            if (drag == Drag.Scale && _scaleChanged && TempoMapping.MapBar(_o._engine!, _before, _barBeat, _scaleTargetSeconds,
                (e.KeyModifiers & KeyModifiers.Alt) == 0) is { } final) _points = final.ToList();
            bool commit = _moved && (drag is Drag.Point or Drag.Handle1 or Drag.Handle2 || drag == Drag.Scale && _scaleChanged);
            if (commit) _o.EndTempoPreview(true, _points.ToArray());
            else if (_o._tempoPreview is not null) _o.EndTempoPreview(false, _before);
            if (drag == Drag.Marquee && !_moved) ShowMenu(Hit(_press));
            e.Pointer.Capture(null); e.Handled = true; InvalidateVisual();
        }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { if (IsDragging) Cancel(); base.OnPointerCaptureLost(e); }
        private void Cancel()
        {
            _drag = Drag.None;
            if (_o._tempoPreview is not null) _o.EndTempoPreview(false, _before);
            _scaleChanged = false; InvalidateVisual();
        }
        private void Commit(IReadOnlyList<TempoPoint> points)
        {
            _o._tempoScrollSeconds = _o._engine?.BeatToSeconds(_o._scrollBeats) ?? 0;
            _o.EndTempoPreview(true, points);
        }
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) => _o.HandleLaneWheel(e, e.GetPosition(this).X);
        private void ShowMenu(int index)
        {
            var menu = new MenuFlyout();
            if (index >= 0)
            {
                _selection.Clear(); _selection.Add(index);
                var edit = new MenuItem { Header = Loc.T("tempo.edit") };
                edit.Click += (_, _) => ShowBpmEditor(index); menu.Items.Add(edit);
                foreach (var shape in Enum.GetValues<TempoShape>())
                {
                    string label = shape switch { TempoShape.Step => Loc.T("tempo.step"), TempoShape.Linear => Loc.T("tempo.linear"), _ => Loc.T("tempo.smooth") };
                    var item = new MenuItem { Header = label };
                    item.Click += (_, _) => { _points[index] = _points[index] with { Shape = shape }; Commit(_points.ToArray()); };
                    menu.Items.Add(item);
                }
                var delete = new MenuItem { Header = Loc.T("tempo.delete"), IsEnabled = index > 0 };
                delete.Click += (_, _) => Commit(TempoMapping.DeleteSelected(_points, _selection)); menu.Items.Add(delete);
            }
            else
            {
                var add = new MenuItem { Header = Loc.T("tempo.add") };
                add.Click += (_, _) =>
                {
                    double beat = Math.Max(0, _o.Snap(_pressBeat));
                    if (!_points.Any(p => Math.Abs(p.Beat - beat) < 1e-6))
                        Commit(_points.Append(new TempoPoint(beat, _pressBpm)).OrderBy(p => p.Beat).ToArray());
                };
                menu.Items.Add(add);
            }
            menu.ShowAt(this, showAtPointer: true); InvalidateVisual();
        }
        private void ShowBpmEditor(int index)
        {
            var field = new NumericUpDown { Minimum = 1, Maximum = 999, Increment = .1m, Value = (decimal)_points[index].Bpm,
                FormatString = "0.00", Width = 130 };
            var apply = new Button { Content = Loc.T("tempo.apply") };
            var flyout = new Flyout { Content = new StackPanel { Spacing = 6, Children = { field, apply } } };
            void Accept() { if (field.Value is { } bpm) { _points[index] = _points[index] with { Bpm = (double)bpm, IncomingBpm = 0 }; Commit(_points.ToArray()); } flyout.Hide(); }
            apply.Click += (_, _) => Accept();
            field.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } else if (e.Key == Key.Escape) { flyout.Hide(); e.Handled = true; } };
            flyout.ShowAt(this); field.Focus();
        }
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            ctx.FillRectangle(NotaPalette.BgSunken, new Rect(0, 0, w, h));
            _o.DrawGrid(ctx, w, h);
            var curve = new Pen(NotaPalette.VizInk, 1.5);
            var selected = new Pen(NotaPalette.Accent, 1);
            // Sample through the same native compiler used by playback, including handles.
            if (_o._engine is { } engine)
            {
                Point? previous = null;
                int nextBoundary = 1;
                for (double x = 0; x <= w; x += 3)
                {
                    double beat = _o.XToBeat(x);
                    double bpm = _o._tempoPreview?.TempoAt(beat) ?? engine.TempoAt(beat);
                    var point = new Point(x, Y(bpm));
                    if (previous is { } p)
                    {
                        while (nextBoundary < _points.Count)
                        {
                            double boundaryX = _o.BeatToX(_points[nextBoundary].Beat);
                            if (boundaryX > x) break;
                            if (_points[nextBoundary - 1].Shape == TempoShape.Step && boundaryX > p.X)
                            {
                                var edge = StepEdge(nextBoundary);
                                ctx.DrawLine(curve, p, edge.HoldEnd);
                                ctx.DrawLine(curve, edge.HoldEnd, edge.JumpEnd);
                                p = edge.JumpEnd;
                            }
                            ++nextBoundary;
                        }
                        ctx.DrawLine(curve, p, point);
                    }
                    previous = point;
                }
                string readout = (_o._tempoPreview?.TempoAt(_o.XToBeat(_pointer.X)) ?? engine.TempoAt(_o._playheadBeats)).ToString("0.00", NotaNum.Culture) + " BPM";
                var text = new NotaFormattedText(readout, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Sans, NotaType.Readout, NotaPalette.TextPrimary);
                ctx.DrawText(text, new Point(8, 2));
            }
            for (int i = 0; i < _points.Count; ++i)
            {
                var p = PointAt(i);
                if (p.X >= -6 && p.X <= w + 6) ctx.DrawEllipse(_selection.Contains(i) ? NotaPalette.Accent : NotaPalette.VizInk, null, p, 4, 4);
                if (_selection.Contains(i) && i + 1 < _points.Count && _points[i].Shape == TempoShape.Smooth)
                    foreach (bool second in new[] { false, true })
                    {
                        var handle = Handle(i, second);
                        ctx.DrawLine(selected, second ? new Point(_o.BeatToX(_points[i + 1].Beat), Y(_points[i + 1].IncomingBpm > 0 ? _points[i + 1].IncomingBpm : _points[i + 1].Bpm)) : p, handle);
                        ctx.DrawRectangle(NotaPalette.BgSunken, selected, new Rect(handle.X - 3, handle.Y - 3, 6, 6));
                    }
            }
            if (_drag == Drag.Marquee && _moved)
            {
                var from = new Point(_o.BeatToX(_pressBeat), Y(_pressBpm));
                var rect = new Rect(new Point(Math.Min(from.X, _pointer.X), Math.Min(from.Y, _pointer.Y)),
                    new Point(Math.Max(from.X, _pointer.X), Math.Max(from.Y, _pointer.Y)));
                ctx.DrawRectangle(NotaPalette.Wash(NotaPalette.Accent, 0x24), selected, rect);
            }
            if (_inside && _ctrl || _drag == Drag.Scale)
                ctx.DrawLine(selected, new Point(_pointer.X, 0), new Point(_pointer.X, h));
            ctx.DrawLine(new Pen(NotaPalette.Seam, 1), new Point(0, h - 1), new Point(w, h - 1));
        }

        private (Point HoldEnd, Point JumpEnd) StepEdge(int pointIndex)
        {
            double x = _o.BeatToX(_points[pointIndex].Beat);
            return (new Point(x, Y(_points[pointIndex - 1].Bpm)), new Point(x, Y(_points[pointIndex].Bpm)));
        }
    }
}
