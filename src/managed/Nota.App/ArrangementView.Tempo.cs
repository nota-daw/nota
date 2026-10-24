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
using Avalonia.Interactivity;
using Avalonia.Threading;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Domain;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class ArrangementView
{
    private double _tempoH = 52;
    private readonly TempoTrackControl _tempoControl;
    private Border _tempoRow = null!;
    private Grid _tempoGrid = null!;

    public Func<IReadOnlyList<TempoPoint>>? GetTempoPoints { get; set; }
    public Action<IReadOnlyList<TempoPoint>>? SetTempoPoints { get; set; }

    public bool ShowTempoTrack
    {
        get => _tempoRow.IsVisible;
        set
        {
            if (_tempoRow.IsVisible == value) return;
            _tempoRow.IsVisible = value;
            if (value) _tempoControl.InvalidateVisual();
        }
    }

    public void RefreshTempoTrack() => _tempoControl.InvalidateVisual();
    public void ClearTempoSelection() => _tempoControl.ClearSelection();

    private Border BuildTempoRow()
    {
        var label = new TextBlock
        {
            Text = "Tempo",
            Classes = { "SectionLabel" },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _tempoGrid = new Grid { Height = _tempoH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        _tempoGrid.Children.Add(HeaderPanel(label));
        Grid.SetColumn(_tempoControl, 1);
        _tempoGrid.Children.Add(_tempoControl);

        // Resize handle as overlay at bottom of tempo track (inside the track height)
        var resizeHandle = new TempoResizeHandle(this);
        _tempoGrid.Children.Add(resizeHandle);
        Grid.SetColumnSpan(resizeHandle, 2);
        Grid.SetRow(resizeHandle, 0);
        resizeHandle.VerticalAlignment = VerticalAlignment.Bottom;
        resizeHandle.HorizontalAlignment = HorizontalAlignment.Stretch;

        // Border with 1px bottom seam, no extra padding
        var row = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = _tempoGrid };
        row.BindResource(Border.BorderBrushProperty, "Brush.Hairline");
        return row;
    }

    private sealed class TempoResizeHandle : Control
    {
        private readonly ArrangementView _o;
        private bool _dragging;
        private double _startY, _startH;

        public TempoResizeHandle(ArrangementView owner)
        {
            _o = owner;
            Height = 4;
            VerticalAlignment = VerticalAlignment.Bottom;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragging = true;
            _startY = e.GetPosition(_o).Y;
            _startH = _o._tempoH;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!_dragging) return;
            double dy = e.GetPosition(_o).Y - _startY;
            _o._tempoH = Math.Clamp(_startH + dy, 32, 240);
            _o._tempoGrid.Height = _o._tempoH;
            _o._tempoControl.Height = _o._tempoH;
            _o._tempoControl.InvalidateVisual();
            _o.InvalidateMeasure();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            // Draw the resize gripper: a centered horizontal line with dots
            double cx = Bounds.Width / 2;
            var pen = new Pen(NotaPalette.BorderStrong, 1.2);
            ctx.DrawLine(pen, new Point(cx - 12, Bounds.Height / 2), new Point(cx + 12, Bounds.Height / 2));
        }
    }

    private sealed class TempoTrackControl : Control
    {
        private static readonly IBrush LaneBg = NotaPalette.Wash(NotaPalette.BgSunken, 0xFF);
        private static readonly IBrush GridInk = NotaPalette.Wash(NotaPalette.BorderDefault, 0x88);
        private static readonly IBrush CurveInk = NotaPalette.AccentBright;
        private static readonly IBrush PointFill = NotaPalette.AccentBright;
        private static readonly IBrush SelectedFill = NotaPalette.TextPrimary;
        private static readonly IBrush LabelInk = NotaPalette.TextPrimary;
        private static readonly IBrush BoxFill = NotaPalette.Wash(NotaPalette.AccentBright, 0x24);
        private static readonly IPen CurvePen = new Pen(NotaPalette.AccentBright, 1.5);
        private static readonly IPen GridPen = new Pen(NotaPalette.Wash(NotaPalette.BorderDefault, 0x88), 1);
        private static readonly IPen BoxPen = new Pen(NotaPalette.AccentBright, 1);
        private static readonly Cursor Hand = new(StandardCursorType.Hand);
        private static readonly Cursor Move = new(StandardCursorType.SizeAll);

        private enum Gesture { None, Point, Box, PendingAdd }

        private readonly ArrangementView _o;
        private List<TempoPoint> _working = [];
        private readonly HashSet<int> _selected = [];
        private Gesture _gesture;
        private int _active = -1;
        private Point _press;
        private double _pressBeat;
        private bool _changed;
        private Rect? _box;

        public TempoTrackControl(ArrangementView owner)
        {
            _o = owner;
            ClipToBounds = true;
            Cursor = Hand;
            Focusable = true;
            LostFocus += (_, _) => ClearSelection();
        }

        public void ClearSelection()
        {
            if (_selected.Count == 0) return;
            _selected.Clear();
            InvalidateVisual();
        }

        private IReadOnlyList<TempoPoint> Points => _o.GetTempoPoints?.Invoke() ?? new[] { new TempoPoint(0, 120) };

        private double BeatAt(double x) => Math.Max(0, _o._scrollBeats + x / Math.Max(1, _o._pixelsPerBeat));
        private double BpmAt(double y) => Math.Clamp(Bpm.Min + (Bounds.Height - 7 - y) / Math.Max(1, Bounds.Height - 14) * (Bpm.Max - Bpm.Min), Bpm.Min, Bpm.Max);
        private double YAt(double bpm) => Bounds.Height - 7 - (Math.Clamp(bpm, Bpm.Min, Bpm.Max) - Bpm.Min) / (Bpm.Max - Bpm.Min) * Math.Max(1, Bounds.Height - 14);

        private int HitPoint(Point p)
        {
            var points = Points;
            int hit = -1;
            double best = 9.0;
            for (int i = 0; i < points.Count; i++)
            {
                var q = new Point(_o.BeatToX(points[i].Beat), YAt(points[i].Bpm));
                double d = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                if (d <= best) { best = d; hit = i; }
            }
            return hit;
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            // Zero/empty bounds (mid-layout, hidden row, degenerate resize pass) used to make
            // Math.Clamp below run with min > max and throw ArgumentException out of the render
            // commit — a hard crash that took the app down. Nothing to draw in an empty box.
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            var area = new Rect(Bounds.Size);
            ctx.DrawRectangle(LaneBg, null, area);

            double step = _o.SnapGridState.Draw.Beats(_o._beatsPerBar);
            double bar = _o._beatsPerBar > 0 ? _o._beatsPerBar : 4;
            // Guard the zoom too: a zero/negative pixels-per-beat made the visible-beat range
            // infinite, so GridLines looped forever before it ever got to draw.
            foreach (double b in SnapGrid.GridLines(_o._scrollBeats, _o._scrollBeats + Bounds.Width / Math.Max(1, _o._pixelsPerBeat), step, _o._beatsPerBar))
            {
                double x = _o.BeatToX(b);
                var pen = Math.Abs(b / bar - Math.Round(b / bar)) < 1e-6 ? BarPen : Math.Abs(b - Math.Round(b)) < 1e-6 ? BeatPen : SubBeatPen;
                if (x >= 0 && x <= Bounds.Width) ctx.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
            }

            var points = Points.OrderBy(p => p.Beat).ToArray();
            if (points.Length == 0) return;
            for (int i = 0; i < points.Length - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                double x0 = _o.BeatToX(a.Beat), x1 = _o.BeatToX(b.Beat);
                if (x1 < 0 || x0 > Bounds.Width) continue;
                Point prev = new(x0, YAt(a.Bpm));
                for (int n = 1; n <= 20; n++)
                {
                    double t = n / 20.0;
                    double shaped = a.Interpolation switch
                    {
                        TempoInterpolation.Square => 0,
                        TempoInterpolation.Triangle => t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t),
                        TempoInterpolation.Smooth => t * t * (3 - 2 * t),
                        _ => t,
                    };
                    var cur = new Point(x0 + (x1 - x0) * t, YAt(a.Bpm + (b.Bpm - a.Bpm) * shaped));
                    ctx.DrawLine(CurvePen, prev, cur);
                    prev = cur;
                }
            }
            for (int i = 0; i < points.Length; i++)
            {
                double x = _o.BeatToX(points[i].Beat), y = YAt(points[i].Bpm);
                if (x < -10 || x > Bounds.Width + 10) continue;
                bool selected = _selected.Contains(i);
                ctx.DrawEllipse(selected ? SelectedFill : PointFill, selected ? CurvePen : null, new Point(x, y), selected ? 5 : 4, selected ? 5 : 4);
                // Hide tempo value labels at low zoom (pixels per beat < 12) where they're unreadable
                if (_o._pixelsPerBeat >= 12)
                {
                    var ft = new FormattedText(points[i].Bpm.ToString("0.##", CultureInfo.CurrentCulture),
                        CultureInfo.CurrentCulture, FlowDirection.LeftToRight, NotaFonts.SansBold, 9, LabelInk);
                    ctx.DrawText(ft, new Point(Math.Clamp(x + 6, 2, Math.Max(2, Bounds.Width - ft.Width - 2)), Math.Clamp(y - ft.Height - 1, 0, Math.Max(0, Bounds.Height - ft.Height))));
                }
            }
            if (_box is { } box)
            {
                ctx.DrawRectangle(BoxFill, BoxPen, box);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            var modifiers = e.KeyModifiers;
            if (e.Key == Key.A &&
                (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 &&
                (modifiers & (KeyModifiers.Alt | KeyModifiers.Shift)) == 0)
            {
                _selected.Clear();
                for (int i = 0; i < Points.Count; i++) _selected.Add(i);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if ((e.Key == Key.Delete || e.Key == Key.Back) && _selected.Count > 0)
            {
                var next = Points.ToList();
                foreach (int index in _selected.Where(i => i > 0 && i < next.Count).OrderByDescending(i => i))
                    next.RemoveAt(index);
                if (next.Count != Points.Count) _o.SetTempoPoints?.Invoke(next);
                _selected.Clear();
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            bool isCtrlCmd = IsPrimaryDown(e.KeyModifiers);
            if (pt.Properties.IsRightButtonPressed && !isCtrlCmd)
            {
                ShowMenu(e.GetPosition(this));
                e.Handled = true;
                return;
            }
            if (!pt.Properties.IsLeftButtonPressed && !pt.Properties.IsRightButtonPressed) return;
            Focus();
            _press = e.GetPosition(this);
            _pressBeat = BeatAt(_press.X);
            _active = HitPoint(_press);
            _changed = false;

            if (e.ClickCount >= 2 && _active >= 0 && !isCtrlCmd)
            {
                _selected.Clear();
                _selected.Add(_active);
                ShowBpmEditor(_active);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_active >= 0)
            {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !_selected.Contains(_active))
                {
                    _selected.Clear();
                    _selected.Add(_active);
                }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    if (!_selected.Add(_active)) _selected.Remove(_active);
                }
                _working = Points.ToList();
                if (_o._engine is not null)
                {
                    _o._engine.BeginUndoGroup();
                    _undoGroupOpen = true;
                }
                _gesture = Gesture.Point;
                Cursor = Move;
            }
            else
            {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _selected.Clear();
                _working = Points.ToList();
                _gesture = Gesture.PendingAdd;
                _box = new Rect(_press, _press);
            }
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var pos = e.GetPosition(this);
            if (_gesture == Gesture.None)
            {
                Cursor = HitPoint(pos) >= 0 ? Move : Hand;
                return;
            }
            if (Math.Abs(pos.X - _press.X) + Math.Abs(pos.Y - _press.Y) > 3) _changed = true;
            if (_gesture == Gesture.PendingAdd && _changed) _gesture = Gesture.Box;
            if (_gesture == Gesture.Point && _active >= 0 && _active < _working.Count)
            {
                var p = _working[_active];
                double rawBeat = BeatAt(pos.X);
                double beat = _active == 0 ? 0 : Math.Max(0, _o.Snap(rawBeat));
                double bpm = Math.Round(BpmAt(pos.Y), 2);
                _working[_active] = p with { Beat = beat, Bpm = bpm };
                var sorted = _working.OrderBy(p => p.Beat).ToList();
                // Ensure strictly increasing beats with minimum delta (matching native validation)
                // First pass: collapse duplicates (delta <= 0), keep the later point
                var deduped = new List<TempoPoint>();
                foreach (var sp in sorted)
                {
                    if (deduped.Count > 0 && sp.Beat - deduped[^1].Beat <= 0)
                        deduped[^1] = sp;
                    else
                        deduped.Add(sp);
                }
                // Second pass: ensure minimum separation
                const double MinBeatDelta = 1e-4;
                for (int i = 1; i < deduped.Count; i++)
                {
                    double delta = deduped[i].Beat - deduped[i - 1].Beat;
                    if (delta < MinBeatDelta)
                        deduped[i] = deduped[i] with { Beat = deduped[i - 1].Beat + MinBeatDelta };
                }
                // Third pass: re-check in case pushing forward created new collisions
                for (int i = 1; i < deduped.Count; i++)
                {
                    double delta = deduped[i].Beat - deduped[i - 1].Beat;
                    if (delta < MinBeatDelta)
                        deduped[i] = deduped[i] with { Beat = deduped[i - 1].Beat + MinBeatDelta };
                }
                _o.SetTempoPoints?.Invoke(deduped);
                InvalidateVisual();
            }
            else if (_gesture == Gesture.Box)
            {
                _box = new Rect(Math.Min(_press.X, pos.X), Math.Min(_press.Y, pos.Y), Math.Abs(pos.X - _press.X), Math.Abs(pos.Y - _press.Y));
                InvalidateVisual();
            }
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_gesture == Gesture.None) return;
            if (_gesture == Gesture.Point && _changed)
            {
                var sorted = _working.OrderBy(p => p.Beat).ToList();
                // Ensure strictly increasing beats with minimum delta (matching native validation)
                // First pass: collapse duplicates (delta <= 0), keep the later point
                var deduped = new List<TempoPoint>();
                foreach (var sp in sorted)
                {
                    if (deduped.Count > 0 && sp.Beat - deduped[^1].Beat <= 0)
                        deduped[^1] = sp;
                    else
                        deduped.Add(sp);
                }
                // Second pass: ensure minimum separation
                const double MinBeatDelta = 1e-4;
                for (int i = 1; i < deduped.Count; i++)
                {
                    double delta = deduped[i].Beat - deduped[i - 1].Beat;
                    if (delta < MinBeatDelta)
                        deduped[i] = deduped[i] with { Beat = deduped[i - 1].Beat + MinBeatDelta };
                }
                // Third pass: re-check in case pushing forward created new collisions
                for (int i = 1; i < deduped.Count; i++)
                {
                    double delta = deduped[i].Beat - deduped[i - 1].Beat;
                    if (delta < MinBeatDelta)
                        deduped[i] = deduped[i] with { Beat = deduped[i - 1].Beat + MinBeatDelta };
                }
                _o.SetTempoPoints?.Invoke(deduped);
                _selected.Clear();
                int selected = deduped.FindIndex(p => p.Beat == (_active == 0 ? 0 : Math.Round(BeatAt(e.GetPosition(this).X) * 4) / 4));
                if (selected >= 0) _selected.Add(selected);
            }
            else if (_gesture == Gesture.PendingAdd && !_changed)
            {
                AddPoint(_pressBeat, BpmAt(_press.Y));
            }
            else if (_gesture == Gesture.Box && _box is { } box)
            {
                _selected.Clear();
                var points = Points;
                for (int i = 0; i < points.Count; i++)
                    if (box.Contains(new Point(_o.BeatToX(points[i].Beat), YAt(points[i].Bpm)))) _selected.Add(i);
            }
            CloseUndoGroup();
            _gesture = Gesture.None;
            _active = -1;
            _box = null;
            e.Pointer.Capture(null);
            Cursor = Hand;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            CloseUndoGroup();
            _gesture = Gesture.None;
            _active = -1;
            _box = null;
            InvalidateVisual();
            base.OnPointerCaptureLost(e);
        }

        private void CloseUndoGroup()
        {
            if (!_undoGroupOpen) return;
            _undoGroupOpen = false;
            _o._engine?.EndUndoGroup();
        }

        private void AddPoint(double beat, double bpm)
        {
            var next = Points.ToList();
            beat = Math.Max(0, _o.Snap(beat));
            if (next.Any(p => Math.Abs(p.Beat - beat) < 1e-6)) return;
            if (next.Count >= 4096) return;
            next.Add(new TempoPoint(beat, Math.Round(Math.Clamp(bpm, Bpm.Min, Bpm.Max), 2), TempoInterpolation.Linear));
            var sorted = next.OrderBy(p => p.Beat).ToList();
            _o.SetTempoPoints?.Invoke(sorted);
            _selected.Clear();
            int at = sorted.FindIndex(p => Math.Abs(p.Beat - beat) < 1e-6);
            if (at >= 0) _selected.Add(at);
            InvalidateVisual();
        }

        private void ShowBpmEditor(int index)
        {
            var points = Points;
            if (index < 0 || index >= points.Count) return;
            var original = points[index];
            _bpmEditorFlyout?.Hide();

            var input = new TextBox
            {
                Width = 96,
                Height = 26,
                Padding = new Thickness(4, 2),
                Text = original.Bpm.ToString("0.##", NotaNum.Culture),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            input.BindResource(TextBox.FontFamilyProperty, "Font.Mono");
            input.BindResource(TextBox.ForegroundProperty, "Brush.TextPrimary");

            var apply = new Button { Content = "Apply", Classes = { "cell" } };
            var cancel = new Button { Content = "Cancel", Classes = { "cell" } };
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 6,
                Children = { cancel, apply },
            };
            var content = new StackPanel
            {
                Width = 176,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Tempo (BPM)", Classes = { "SectionLabel" } },
                    input,
                    actions,
                },
            };
            var flyout = new Flyout
            {
                Content = new Border
                {
                    Background = NotaPalette.SurfaceCard,
                    BorderBrush = NotaPalette.BorderDefault,
                    BorderThickness = new Thickness(1),
                    CornerRadius = NotaRadius.Control,
                    Padding = new Thickness(8),
                    Child = content,
                },
            };
            _bpmEditorFlyout = flyout;

            void Commit()
            {
                if (!double.TryParse(input.Text, NumberStyles.Float, NotaNum.Culture, out double bpm) ||
                    !double.IsFinite(bpm)) return;
                var next = Points.ToList();
                if (index >= next.Count || Math.Abs(next[index].Beat - original.Beat) > 1e-9)
                {
                    flyout.Hide();
                    return;
                }
                next[index] = next[index] with { Bpm = Math.Clamp(bpm, Bpm.Min, Bpm.Max) };
                _o.SetTempoPoints?.Invoke(next);
                flyout.Hide();
                InvalidateVisual();
            }

            apply.Click += (_, _) => Commit();
            cancel.Click += (_, _) => flyout.Hide();
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
                else if (e.Key == Key.Escape) { flyout.Hide(); e.Handled = true; }
            };
            flyout.Closed += (_, _) =>
            {
                if (ReferenceEquals(_bpmEditorFlyout, flyout)) _bpmEditorFlyout = null;
            };
            flyout.ShowAt(this);
            input.Focus();
            input.SelectAll();
        }

        private bool _undoGroupOpen;
        private Flyout? _bpmEditorFlyout;

        private void ShowMenu(Point pos)
        {
            int hit = HitPoint(pos);
            var fly = new MenuFlyout();
            var add = new MenuItem { Header = "Add tempo point" };
            add.Click += (_, _) => AddPoint(BeatAt(pos.X), BpmAt(pos.Y));
            fly.Items.Add(add);

            bool canRemove = _selected.Count > 0 || hit > 0;
            if (canRemove)
            {
                fly.Items.Add(new Separator());
                string removeHeader = _selected.Count > 1 ? "Remove tempo points" : "Remove tempo point";
                var remove = new MenuItem { Header = removeHeader, IsEnabled = canRemove };
                remove.Click += (_, _) =>
                {
                    var list = Points.ToList();
                    var targets = _selected.Count > 0
                        ? _selected.Where(i => i > 0 && i < list.Count).OrderByDescending(i => i).ToList()
                        : (hit > 0 && hit < list.Count ? new List<int> { hit } : new List<int>());
                    foreach (int index in targets) list.RemoveAt(index);
                    if (targets.Count > 0) _o.SetTempoPoints?.Invoke(list);
                    _selected.Clear();
                    InvalidateVisual();
                };
                fly.Items.Add(remove);
                fly.Items.Add(new Separator());
            }

            foreach (var curve in Enum.GetValues<TempoInterpolation>())
            {
                var selected = hit >= 0 ? Points[hit].Interpolation : _selected.Select(i => i >= 0 && i < Points.Count ? Points[i].Interpolation : TempoInterpolation.Linear).FirstOrDefault();
                var mi = new MenuItem { Header = curve.ToString(), ToggleType = MenuItemToggleType.Radio, IsChecked = curve == selected };
                mi.Click += (_, _) =>
                {
                    var list = Points.ToList();
                    int[] targets = _selected.Count > 0 ? _selected.Where(i => i < list.Count).ToArray() : hit >= 0 ? new[] { hit } : Array.Empty<int>();
                    foreach (int i in targets) list[i] = list[i] with { Interpolation = curve };
                    _o.SetTempoPoints?.Invoke(list);
                    InvalidateVisual();
                };
                fly.Items.Add(mi);
            }
            fly.ShowAt(this, showAtPointer: true);
        }
    }
}
