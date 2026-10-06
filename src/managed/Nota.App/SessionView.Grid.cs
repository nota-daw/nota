// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session view — the grid: the scene rail, column headers, and the custom-drawn cells
// (clip slot, group slot, scene). Cells paint straight from engine state at the UI clock;
// only previews (notes / peaks), names and colours are cached per Refresh.

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

namespace Nota.App;

public sealed partial class SessionView
{
    private const double TopRowH = 24;      // launch triangle + name + length
    private const double StopRowH = 24;
    private const double SpacerH = 22;

    private readonly HashSet<int> _armed = new();
    private int _recTrack, _recScene = -1;
    private double _recElapsed;

    // Clip drag between slots: the cell the pointer is over while a clip is dragged.
    private (int TrackId, int Scene)? _dragTarget;

    private void ReadRecordTarget()
    {
        if (_engine.TryGetSessionRecordTarget(out int t, out int s, out double el)) { _recTrack = t; _recScene = s; _recElapsed = el; }
        else { _recTrack = 0; _recScene = -1; _recElapsed = 0; }
    }

    // ---- scene rail ---------------------------------------------------------------

    private void AddSceneRail()
    {
        _headerRow.Children.Add(new Border
        {
            Width = SceneRailW, Height = HeaderH, Padding = new Thickness(10, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Text("SCENES", 9, NotaPalette.TextTertiary, FontWeight.Bold),
                    Mono(_sceneCount.ToString(CultureInfo.CurrentCulture), 9, NotaPalette.TextDisabled),
                },
            },
        });

        var mid = new StackPanel { Width = SceneRailW, Spacing = Gap };
        for (int s = 0; s < _sceneCount; s++)
        {
            var cell = new SceneCell(this, s);
            _sceneCells.Add(cell);
            mid.Children.Add(cell);
        }
        _cellsRow.Children.Add(mid);

        var addScene = new Button { Content = "+ Scene", Classes = { "ghost" }, Height = SpacerH, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(0) };
        ToolTip.SetTip(addScene, "Add a scene at the end");
        addScene.Click += (_, _) => { int s = _engine.InsertScene(_engine.SceneCount); Refresh(); if (s >= 0) Select(new Selection(SelKind.Scene, 0, s)); };

        var stopAll = new Button
        {
            Height = StopRowH, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 7,
                Children = { new Glyph(GlyphKind.Stop, 7), new TextBlock { Text = "Stop All Clips", FontSize = 10, FontWeight = FontWeight.SemiBold } },
            },
        };
        ToolTip.SetTip(stopAll, "Stop every session clip");
        stopAll.Click += (_, _) => { _engine.StopAllSession(); UpdateStates(); };

        var foot = new StackPanel { Width = SceneRailW, Spacing = Gap, Children = { addScene, stopAll } };
        foot.Children.Add(MixerRailLabels());
        _footerRow.Children.Add(foot);
    }

    // ---- columns ------------------------------------------------------------------

    private void AddColumn(Column c)
    {
        c.Header = new ColumnHeader(this, c);
        _headerRow.Children.Add(c.Header);

        var mid = new StackPanel { Width = c.Width, Spacing = Gap };
        for (int s = 0; s < _sceneCount; s++)
        {
            Control cell = c.Kind switch
            {
                ColKind.Track => new SlotCell(this, c, s),
                ColKind.Group => new GroupCell(this, c, s),
                _ => new BlankCell(c.Width),
            };
            c.Cells.Add(cell);
            mid.Children.Add(cell);
        }
        _cellsRow.Children.Add(mid);

        var foot = new StackPanel { Width = c.Width, Spacing = Gap };
        foot.Children.Add(new Border { Height = SpacerH });
        if (c.Kind is ColKind.Track or ColKind.Group)
        {
            var stop = new Button { Height = StopRowH, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(0), Content = new Glyph(GlyphKind.Stop, 7) };
            ToolTip.SetTip(stop, c.Kind == ColKind.Group ? "Stop every track in this group" : "Stop this track");
            stop.Click += (_, _) => { StopColumn(c); UpdateStates(); };
            foot.Children.Add(stop);
        }
        else foot.Children.Add(new Border { Height = StopRowH });
        c.Mixer = BuildMixer(c);
        foot.Children.Add(c.Mixer.Root);
        _footerRow.Children.Add(foot);
    }

    // ---- small builders -----------------------------------------------------------

    private static TextBlock Text(string text, double size, IBrush ink, FontWeight? weight = null)
        => new() { Text = text, FontSize = size, Foreground = ink, FontWeight = weight ?? FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Mono(string text, double size, IBrush ink)
    {
        var t = new TextBlock { Text = text, FontSize = size, Foreground = ink, VerticalAlignment = VerticalAlignment.Center };
        t.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        return t;
    }

    private static FormattedText Ft(string text, Typeface face, double size, IBrush ink, double maxWidth = double.PositiveInfinity)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, ink);
        if (!double.IsInfinity(maxWidth)) { ft.MaxTextWidth = Math.Max(1, maxWidth); ft.MaxLineCount = 1; ft.Trimming = TextTrimming.CharacterEllipsis; }
        return ft;
    }

    private static readonly IPen SelPen = new Pen(NotaPalette.Accent, 1);
    private static readonly IPen PlayheadPen = new Pen(NotaPalette.TextPrimary, 1);

    // The almanac's play triangle, drawn in a w×h box centred on c.
    private static void DrawTriangle(DrawingContext ctx, Point c, double w, double h, IBrush fill)
    {
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(new Point(c.X - w / 2, c.Y - h / 2), true);
            gc.LineTo(new Point(c.X + w / 2, c.Y));
            gc.LineTo(new Point(c.X - w / 2, c.Y + h / 2));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(fill, null, g);
    }

    // Progress pie: a dim disc with the played fraction filled clockwise from 12 o'clock.
    private static void DrawPie(DrawingContext ctx, Point c, double r, double frac)
    {
        ctx.DrawEllipse(NotaPalette.Wash(NotaPalette.Success, 0x33), null, c, r, r);
        frac = Math.Clamp(frac, 0, 1);
        if (frac <= 0.001) return;
        if (frac >= 0.999) { ctx.DrawEllipse(NotaPalette.Success, null, c, r, r); return; }
        double a = frac * Math.PI * 2;
        var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            gc.BeginFigure(c, true);
            gc.LineTo(new Point(c.X, c.Y - r));
            gc.ArcTo(end, new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise);
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(NotaPalette.Success, null, g);
    }

    private static void DrawSelection(DrawingContext ctx, Rect r)
        => ctx.DrawRectangle(null, SelPen, new Rect(r.X - 1.5, r.Y - 1.5, r.Width + 3, r.Height + 3).Normalize(), NotaRadius.TileValue - 1, NotaRadius.TileValue - 1);

    private static void FillCell(DrawingContext ctx, Rect r, IBrush bg, IBrush? border)
    {
        ctx.DrawRectangle(bg, border is null ? null : new Pen(border, 1), r.Deflate(0.5), NotaRadius.BadgeValue, NotaRadius.BadgeValue);
    }

    /// <summary>The track slot under a point in this view's coordinates (clip drag target).</summary>
    private (Column Col, int Scene)? SlotAt(Point p)
    {
        foreach (var c in LaunchCols)
            for (int s = 0; s < c.Cells.Count; s++)
                if (this.TranslatePoint(p, c.Cells[s]) is { } q && new Rect(c.Cells[s].Bounds.Size).Contains(q)) return (c, s);
        return null;
    }

    private void PromptText(Control anchor, string current, Action<string> commit)
    {
        var box = new TextBox { Text = current, Width = 180, FontSize = 12, Classes = { "field" } };
        var flyout = new Flyout { Content = box, Placement = Avalonia.Controls.PlacementMode.Pointer };
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter) { commit((box.Text ?? "").Trim()); flyout.Hide(); ke.Handled = true; }
            else if (ke.Key == Key.Escape) { flyout.Hide(); ke.Handled = true; }
        };
        flyout.ShowAt(anchor);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { box.SelectAll(); box.Focus(); }, Avalonia.Threading.DispatcherPriority.Input);
    }

    // ---- column header -------------------------------------------------------------

    private sealed class ColumnHeader : Border
    {
        private readonly SessionView _o;
        private readonly Column _c;
        private readonly Border? _arr;

        public ColumnHeader(SessionView o, Column c)
        {
            _o = o; _c = c;
            Width = c.Width; Height = HeaderH;
            CornerRadius = NotaRadius.Badge;
            BorderThickness = new Thickness(1);
            ClipToBounds = true;

            var stripe = new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Background = c.Kind == ColKind.Master ? NotaPalette.Accent : c.Brush };
            var name = new TextBlock
            {
                Text = c.Name, FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = c.Kind == ColKind.Return ? NotaPalette.TextSecondary : NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center,
            };
            var top = new DockPanel { LastChildFill = true };
            if (c.Kind == ColKind.Group)
            {
                bool folded = o._collapsed.Contains(c.TrackId);
                var chevron = new Border
                {
                    Width = 14, Height = 14, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new Glyph(folded ? GlyphKind.ChevronRight : GlyphKind.ChevronDown, 8) { Foreground = NotaPalette.TextSecondary },
                    Margin = new Thickness(0, 0, 4, 0),
                };
                ToolTip.SetTip(chevron, folded ? "Show the group's tracks" : "Fold the group into one column");
                chevron.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    if (!o._collapsed.Add(c.TrackId)) o._collapsed.Remove(c.TrackId);
                    o.Refresh();
                };
                DockPanel.SetDock(chevron, Dock.Left);
                top.Children.Add(chevron);
            }
            if (c.Kind == ColKind.Track)
            {
                var arrText = new TextBlock { Text = "ARR", FontSize = 8, FontWeight = FontWeight.Medium, Foreground = NotaPalette.Accent };
                arrText.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
                _arr = new Border
                {
                    BorderBrush = NotaPalette.AccentDim, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Clip,
                    Padding = new Thickness(3, 0), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand), Child = arrText, IsVisible = false,
                };
                ToolTip.SetTip(_arr, "Back to Arrangement for this track");
                _arr.PointerPressed += (_, e) => { e.Handled = true; o._engine.TrackBackToArrangement(c.TrackId); o.UpdateStates(); };
                DockPanel.SetDock(_arr, Dock.Right);
                top.Children.Add(_arr);
            }
            top.Children.Add(name);

            var kind = new TextBlock { Text = c.KindLabel, FontSize = 8, FontWeight = FontWeight.Medium, Foreground = NotaPalette.TextDisabled, TextTrimming = TextTrimming.CharacterEllipsis };
            kind.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            var stack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 3, 8, 0), Children = { top, kind } };
            Child = new Panel { Children = { stripe, stack } };

            PointerPressed += (_, e) =>
            {
                var pp = e.GetCurrentPoint(this).Properties;
                if (pp.IsRightButtonPressed) { e.Handled = true; o.ShowHeaderMenu(this, c); return; }
                if (!pp.IsLeftButtonPressed || c.Kind is not (ColKind.Track or ColKind.Group)) return;
                if (e.ClickCount == 2) { o.PromptRenameTrack(this, c.TrackId); return; }
                o.Select(new Selection(SelKind.Slot, c.TrackId, o._sel.Scene));
            };
            Sync();
        }

        public void Sync()
        {
            bool sel = _o._sel.Kind == SelKind.Slot && _o._sel.TrackId == _c.TrackId && _c.Kind is ColKind.Track or ColKind.Group;
            Background = sel ? NotaPalette.SelHeaderBg : _c.Kind is ColKind.Return or ColKind.Master ? NotaPalette.Panel : NotaPalette.SurfaceCard;
            BorderBrush = sel ? NotaPalette.AccentDim : Brushes.Transparent;
            if (_arr is not null) _arr.IsVisible = _o._engine.SessionPlayingSlot(_c.TrackId) >= 0;
        }
    }

    // ---- cells ----------------------------------------------------------------------

    /// <summary>A cell polled at the UI clock: repaints itself only when its look changed.</summary>
    private interface ILiveCell { void Tick(); }

    private sealed class BlankCell : Control
    {
        public BlankCell(double w) { Width = w; Height = CellH; }
        public override void Render(DrawingContext ctx) => FillCell(ctx, new Rect(Bounds.Size), NotaPalette.SurfaceDeep, null);
    }

    /// <summary>A track's clip slot: empty (stop / record affordance), a clip, or a take
    /// being recorded.</summary>
    private sealed class SlotCell : Control, ILiveCell
    {
        private readonly SessionView _o;
        private readonly Column _c;
        private readonly int _scene;
        private readonly bool _filled;
        private readonly string _name = "";
        private readonly string _len = "";
        private readonly double _lenBeats;
        private readonly SolidColorBrush _brush;
        private readonly bool _stopButton;
        private readonly List<(double Start, double Len, int Pitch)> _notes = new();
        private int _minPitch, _maxPitch;
        private readonly float[]? _peaks;
        private int _peakCount;
        private bool _dropHover, _hoverLaunch, _hoverCentre;
        private Point _pressAt;
        private bool _pressed, _dragging, _launched;

        public SlotCell(SessionView o, Column c, int scene)
        {
            _o = o; _c = c; _scene = scene;
            Width = c.Width; Height = CellH;
            var eng = o._engine;
            _filled = eng.SessionSlotState(c.TrackId, scene) != 0;
            _brush = _filled ? o.ClipBrush(c, scene) : c.Brush;
            _stopButton = eng.GetSessionSlotStopButton(c.TrackId, scene);
            if (_filled)
            {
                _name = o.ClipName(c, scene);
                _lenBeats = eng.SessionSlotLength(c.TrackId, scene);
                _len = FormatLength(_lenBeats);
                if (c.IsInstrument)
                {
                    foreach (var n in eng.GetSessionNotes(c.TrackId, scene))
                        _notes.Add((n.StartBeat, n.LengthBeats, n.Pitch));
                    if (_notes.Count > 0) { _minPitch = _notes.Min(n => n.Pitch); _maxPitch = _notes.Max(n => n.Pitch); }
                }
                else
                {
                    _peaks = new float[44 * 2];
                    _peakCount = eng.GetSessionSlotPeaks(c.TrackId, scene, _peaks, 44);
                }
            }

            DragDrop.SetAllowDrop(this, true);
            DragDrop.AddDragOverHandler(this, (_, e) => e.DragEffects = BrowserView.IsAcceptableDrag(e) ? DragDropEffects.Copy : DragDropEffects.None);
            DragDrop.AddDragEnterHandler(this, (_, e) => { if (BrowserView.IsAcceptableDrag(e)) { _dropHover = true; InvalidateVisual(); } });
            DragDrop.AddDragLeaveHandler(this, (_, _) => { _dropHover = false; InvalidateVisual(); });
            DragDrop.AddDropHandler(this, (_, e) =>
            {
                _dropHover = false; InvalidateVisual();
                var items = BrowserView.DroppedItems(e);
                if (items.Count > 0) { _o.RaiseDrop(items[0], c.TrackId, scene, c.IsInstrument); e.Handled = true; }
            });
        }

        private static readonly Rect LaunchZone = new(3, 3, 18, 18);
        private Rect CentreZone => new(Bounds.Width / 2 - 8, Bounds.Height / 2 - 8, 16, 16);
        private bool Recording => _o._recTrack == _c.TrackId && _o._recScene == _scene;
        private int _lastState = -1;
        private bool _lastArmed, _lastDrag;

        public void Tick()
        {
            int st = Recording ? 4 : _o._engine.SessionSlotState(_c.TrackId, _scene);
            bool armed = _o._armed.Contains(_c.TrackId);
            bool drag = _o._dragTarget == (_c.TrackId, _scene);
            bool animating = st is 2 or 3 or 4;   // blink, progress, elapsed
            if (st == _lastState && armed == _lastArmed && drag == _lastDrag && !animating) return;
            _lastState = st; _lastArmed = armed; _lastDrag = drag;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            bool hl = LaunchZone.Contains(p), hc = CentreZone.Contains(p);
            if (hl != _hoverLaunch || hc != _hoverCentre) { _hoverLaunch = hl; _hoverCentre = hc; InvalidateVisual(); }
            Cursor = (_filled || Recording) && hl || !_filled && hc ? new Cursor(StandardCursorType.Hand) : Cursor.Default;

            if (_pressed && _filled && !_dragging && Math.Abs(p.X - _pressAt.X) + Math.Abs(p.Y - _pressAt.Y) > 5) _dragging = true;
            if (_dragging)
            {
                Cursor = new Cursor(StandardCursorType.DragMove);
                var hit = _o.SlotAt(e.GetPosition(_o));
                var t = hit is { } h && (h.Col.TrackId, h.Scene) != (_c.TrackId, _scene) ? (h.Col.TrackId, h.Scene) : ((int, int)?)null;
                if (t != _o._dragTarget) { _o._dragTarget = t; _o.UpdateStates(); }
            }
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            if (_hoverLaunch || _hoverCentre) { _hoverLaunch = _hoverCentre = false; InvalidateVisual(); }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            var p = pt.Position;
            if (pt.Properties.IsRightButtonPressed)
            {
                _o.Select(new Selection(SelKind.Slot, _c.TrackId, _scene));
                _o.ShowSlotMenu(this, _c, _scene);
                e.Handled = true;
                return;
            }
            if (!pt.Properties.IsLeftButtonPressed) return;
            e.Handled = true;

            if (Recording && LaunchZone.Contains(p)) { _o._engine.StopSessionRecord(); _o.Refresh(); return; }
            if (_filled && LaunchZone.Contains(p))
            {
                _o._engine.LaunchSlotVelocity(_c.TrackId, _scene, 1f);
                _launched = true;
                e.Pointer.Capture(this);   // a Gate / Repeat clip stops when the button lets go
                _o.UpdateStates();
                return;
            }
            if (!_filled && !Recording && CentreZone.Contains(p))
            {
                if (_o._armed.Contains(_c.TrackId)) { _o._engine.RecordSessionSlot(_c.TrackId, _scene); _o.Refresh(); }
                else if (_stopButton) { _o._engine.StopSlot(_c.TrackId); _o.UpdateStates(); }
                return;
            }

            _o.Select(new Selection(SelKind.Slot, _c.TrackId, _scene));
            if (e.ClickCount == 2)
            {
                if (_filled) _o.SlotEditRequested?.Invoke(_c.TrackId, _scene);
                else if (_c.IsInstrument) { _o.InsertMidiClip(_c.TrackId, _scene); _o.SlotEditRequested?.Invoke(_c.TrackId, _scene); }
                return;
            }
            _pressed = true; _pressAt = p;
            e.Pointer.Capture(this);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_launched) { _launched = false; _o._engine.ReleaseSlot(_c.TrackId, _scene); }
            bool wasDragging = _dragging;
            _pressed = _dragging = false;
            e.Pointer.Capture(null);
            if (wasDragging && _o._dragTarget is { } t)
            {
                bool copy = (e.KeyModifiers & KeyModifiers.Alt) != 0;   // Alt+drag copies
                _o._dragTarget = null;
                if (_o.MoveOrCopySlot(_c.TrackId, _scene, t.TrackId, t.Scene, move: !copy))
                {
                    _o.Select(new Selection(SelKind.Slot, t.TrackId, t.Scene));
                    _o.Say(copy ? "Copied clip" : "Moved clip");
                }
                else _o.Say("Drop a MIDI clip on a MIDI track, audio on audio.");
                return;
            }
            if (_o._dragTarget is not null) { _o._dragTarget = null; _o.UpdateStates(); }
        }

        public override void Render(DrawingContext ctx)
        {
            var r = new Rect(Bounds.Size);
            var eng = _o._engine;
            bool selected = _o._sel.Kind == SelKind.Slot && _o._sel.TrackId == _c.TrackId && _o._sel.Scene == _scene;
            bool dropTarget = _dropHover || _o._dragTarget == (_c.TrackId, _scene);

            if (Recording) { RenderRecording(ctx, r); if (selected) DrawSelection(ctx, r); return; }

            if (!_filled)
            {
                FillCell(ctx, r, NotaPalette.SurfaceCard, dropTarget ? NotaPalette.Accent : null);
                var c = r.Center;
                if (_o._armed.Contains(_c.TrackId))
                    ctx.DrawEllipse(null, new Pen(_hoverCentre ? NotaPalette.Record : NotaPalette.TextMuted, 1.5), c, 3.25, 3.25);
                else if (_stopButton)
                    ctx.DrawRectangle(_hoverCentre ? NotaPalette.TextSecondary : NotaPalette.BorderStrong, null, new Rect(c.X - 3, c.Y - 3, 6, 6), 1, 1);
                if (selected) DrawSelection(ctx, r);
                return;
            }

            int state = eng.SessionSlotState(_c.TrackId, _scene);
            bool playing = state == 3, queued = state == 2;
            bool leaving = playing && QueuedElsewhere();   // another slot on this track is about to take over
            double pos = playing ? eng.SessionSlotPosition(_c.TrackId) : 0;
            double frac = playing && _lenBeats > 0 ? pos / _lenBeats : 0;

            IBrush bg = NotaPalette.Wash(_brush, playing ? (byte)0x40 : (byte)0x24);
            IBrush border = dropTarget ? NotaPalette.Accent
                : playing ? NotaPalette.Success
                : queued ? (_o._blinkOn ? NotaPalette.Warning : NotaPalette.Wash(NotaPalette.Warning, 0x66))
                : NotaPalette.Wash(_brush, 0x55);
            FillCell(ctx, r, bg, border);

            // Launch: a triangle (queued blinks between full and dim Caution), or the
            // progress pie while the clip plays.
            var lc = LaunchZone.Center;
            bool blinkDim = (queued || leaving) && !_o._blinkOn;
            if (_hoverLaunch) ctx.DrawRectangle(NotaPalette.Wash(NotaPalette.TextPrimary, 0x14), null, LaunchZone, NotaRadius.ClipValue, NotaRadius.ClipValue);
            if (playing && !leaving) DrawPie(ctx, lc, 5.5, frac);
            else
            {
                IBrush tri = queued ? (blinkDim ? NotaPalette.Wash(NotaPalette.Warning, 0x66) : NotaPalette.Warning)
                    : playing ? (blinkDim ? NotaPalette.Wash(NotaPalette.Success, 0x66) : NotaPalette.Success)
                    : _hoverLaunch ? NotaPalette.TextPrimary : NotaPalette.TextSecondary;
                DrawTriangle(ctx, lc, 8, 9, tri);
            }

            var lenFt = Ft(_len, NotaFonts.Mono, 9, playing ? NotaPalette.TextStrong : NotaPalette.TextMuted);
            double lenX = r.Width - 6 - lenFt.Width;
            ctx.DrawText(lenFt, new Point(lenX, (TopRowH - lenFt.Height) / 2));
            var nameFt = Ft(_name, NotaFonts.SansMedium, 11, NotaPalette.TextPrimary, lenX - 27 - 4);
            ctx.DrawText(nameFt, new Point(27, (TopRowH - nameFt.Height) / 2));

            var area = new Rect(7, 25, r.Width - 14, r.Height - 31);
            DrawPreview(ctx, area);
            if (playing)
            {
                double x = area.X + Math.Clamp(frac, 0, 1) * area.Width;
                ctx.DrawLine(PlayheadPen, new Point(Math.Round(x) + 0.5, area.Y - 2), new Point(Math.Round(x) + 0.5, area.Bottom + 2));
            }
            if (selected) DrawSelection(ctx, r);
        }

        private bool QueuedElsewhere()
        {
            for (int s = 0; s < _o._sceneCount; s++)
                if (s != _scene && _o._engine.SessionSlotState(_c.TrackId, s) == 2) return true;
            return false;
        }

        private void DrawPreview(DrawingContext ctx, Rect area)
        {
            if (_c.IsInstrument)
            {
                if (_notes.Count == 0 || _lenBeats <= 0) return;
                int range = Math.Max(1, _maxPitch - _minPitch);
                foreach (var n in _notes)
                {
                    if (n.Start >= _lenBeats) continue;
                    double x = area.X + n.Start / _lenBeats * area.Width;
                    double w = Math.Max(1.5, Math.Min(n.Len, _lenBeats - n.Start) / _lenBeats * area.Width);
                    double y = area.Y + (_maxPitch - n.Pitch) / (double)range * (area.Height - 3);
                    ctx.DrawRectangle(_brush, null, new Rect(x, y, w, 3), 1, 1);
                }
                return;
            }
            DrawPeaks(ctx, area, _peaks, _peakCount, _brush);
        }

        private void RenderRecording(DrawingContext ctx, Rect r)
        {
            bool waiting = _o._recElapsed <= 0;
            FillCell(ctx, r, NotaPalette.Wash(NotaPalette.Record, 0x33), NotaPalette.Record);
            var lc = LaunchZone.Center;
            ctx.DrawEllipse(waiting && !_o._blinkOn ? NotaPalette.Wash(NotaPalette.Record, 0x66) : NotaPalette.Record, null, lc, 4.5, 4.5);
            var lenFt = Ft(FormatDuration(_o._recElapsed), NotaFonts.Mono, 9, NotaPalette.DangerBright);
            double lenX = r.Width - 6 - lenFt.Width;
            ctx.DrawText(lenFt, new Point(lenX, (TopRowH - lenFt.Height) / 2));
            var nameFt = Ft(waiting ? "Rec in…" : "Recording", NotaFonts.SansMedium, 11, NotaPalette.TextHeading, lenX - 31);
            ctx.DrawText(nameFt, new Point(27, (TopRowH - nameFt.Height) / 2));
            if (!_c.IsInstrument)
            {
                var buf = new float[44 * 2];
                int n = _o._engine.AudioRecordPeaks(buf, 44);
                DrawPeaks(ctx, new Rect(7, 25, r.Width - 14, r.Height - 31), buf, n, NotaPalette.DangerBright);
            }
        }
    }

    // Waveform preview: one bar per peak bucket, centred, scaled to the loudest bucket.
    private static void DrawPeaks(DrawingContext ctx, Rect area, float[]? peaks, int count, IBrush ink)
    {
        if (peaks is null || count <= 0) return;
        float max = 1e-6f;
        for (int i = 0; i < count; i++) max = Math.Max(max, Math.Max(Math.Abs(peaks[i * 2]), Math.Abs(peaks[i * 2 + 1])));
        double step = area.Width / count, bw = Math.Max(1, step * 0.62);
        for (int i = 0; i < count; i++)
        {
            double a = Math.Max(Math.Abs(peaks[i * 2]), Math.Abs(peaks[i * 2 + 1])) / max;
            double h = Math.Max(1, a * area.Height);
            ctx.DrawRectangle(ink, null, new Rect(area.X + i * step, area.Y + (area.Height - h) / 2, bw, h), 0.5, 0.5);
        }
    }

    /// <summary>A group's slot: launches every child slot in the scene; shows how many
    /// children hold a clip there and one bar per child.</summary>
    private sealed class GroupCell : Control, ILiveCell
    {
        private readonly SessionView _o;
        private readonly Column _g;
        private readonly int _scene;
        private readonly List<(int Id, bool Has, SolidColorBrush Brush)> _kids = new();
        private bool _hoverLaunch;

        public GroupCell(SessionView o, Column g, int scene)
        {
            _o = o; _g = g; _scene = scene;
            Width = g.Width; Height = CellH;
            foreach (int id in g.Children)
                _kids.Add((id, o._engine.SessionSlotState(id, scene) != 0, ArrangementView.TrackBrush(ArrangementView.EffectiveColorIndex(o._engine, id))));
        }

        private static readonly Rect LaunchZone = new(3, 3, 18, 18);
        private bool Any => _kids.Any(k => k.Has);
        private int _lastSig = -1;

        public void Tick()
        {
            int sig = 0;
            foreach (var k in _kids) sig = sig * 5 + (k.Has ? _o._engine.SessionSlotState(k.Id, _scene) : 0);
            if (sig == _lastSig) return;
            _lastSig = sig;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            bool h = Any && LaunchZone.Contains(e.GetPosition(this));
            if (h != _hoverLaunch) { _hoverLaunch = h; Cursor = h ? new Cursor(StandardCursorType.Hand) : Cursor.Default; InvalidateVisual(); }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            if (!pt.Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            var p = pt.Position;
            if (Any && LaunchZone.Contains(p)) { _o.LaunchGroup(_g, _scene); _o.UpdateStates(); return; }
            if (!Any && new Rect(Bounds.Width / 2 - 8, Bounds.Height / 2 - 8, 16, 16).Contains(p)) { _o.StopColumn(_g); _o.UpdateStates(); return; }
            _o.Select(new Selection(SelKind.Slot, _g.TrackId, _scene));
        }

        public override void Render(DrawingContext ctx)
        {
            var r = new Rect(Bounds.Size);
            bool selected = _o._sel.Kind == SelKind.Slot && _o._sel.TrackId == _g.TrackId && _o._sel.Scene == _scene;
            if (!Any)
            {
                FillCell(ctx, r, NotaPalette.SurfaceCard, null);
                ctx.DrawRectangle(NotaPalette.BorderStrong, null, new Rect(r.Center.X - 3, r.Center.Y - 3, 6, 6), 1, 1);
                if (selected) DrawSelection(ctx, r);
                return;
            }
            var eng = _o._engine;
            bool playingAny = _kids.Any(k => k.Has && eng.SessionSlotState(k.Id, _scene) == 3);
            FillCell(ctx, r, NotaPalette.Wash(_g.Brush, playingAny ? (byte)0x2E : (byte)0x17), playingAny ? NotaPalette.Wash(NotaPalette.Success, 0x80) : null);
            DrawTriangle(ctx, LaunchZone.Center, 8, 9, playingAny ? NotaPalette.Success : _hoverLaunch ? NotaPalette.TextPrimary : NotaPalette.TextSecondary);
            var label = Ft($"{_kids.Count(k => k.Has)} of {_kids.Count}", NotaFonts.Sans, 10, NotaPalette.TextSecondary);
            ctx.DrawText(label, new Point(27, (TopRowH - label.Height) / 2));

            double y = r.Height - 7 - 3;
            for (int i = _kids.Count - 1; i >= 0 && y > TopRowH; i--, y -= 6)
            {
                var k = _kids[i];
                IBrush ink = !k.Has ? NotaPalette.BorderDefault : eng.SessionSlotState(k.Id, _scene) == 3 ? NotaPalette.Success : k.Brush;
                ctx.DrawRectangle(ink, null, new Rect(7, y, r.Width - 14, 3), 1, 1);
            }
            if (selected) DrawSelection(ctx, r);
        }
    }

    /// <summary>A scene row in the rail: launch box, colour, name, index, tempo / signature.</summary>
    private int? _sceneDragTarget;

    private int SceneAt(Point p)
    {
        for (int s = 0; s < _sceneCells.Count; s++)
            if (this.TranslatePoint(p, _sceneCells[s]) is { } q && q.Y >= -Gap && q.Y < _sceneCells[s].Bounds.Height) return s;
        return p.Y < 0 ? 0 : _sceneCells.Count - 1;
    }

    private sealed class SceneCell : Control
    {
        private readonly SessionView _o;
        private readonly int _scene;
        private readonly string _name;
        private readonly bool _named;
        private readonly IBrush _color;
        private readonly string _badge;
        private bool _hoverLaunch;

        public SceneCell(SessionView o, int scene)
        {
            _o = o; _scene = scene;
            Width = SceneRailW; Height = CellH;
            string n = o._engine.GetSceneName(scene);
            _named = n.Length > 0;
            _name = _named ? n : $"Scene {scene + 1}";
            o._engine.TryGetSceneProps(scene, out var p);
            _color = p.Color >= 0 ? ArrangementView.TrackBrush(p.Color) : NotaPalette.BorderStrong;
            var parts = new List<string>();
            if (p.Tempo > 0) parts.Add(NotaNum.Str(p.Tempo, "0.##"));
            if (p.SigNum > 0 && p.SigDen > 0) parts.Add($"{p.SigNum}/{p.SigDen}");
            _badge = string.Join(" · ", parts);
        }

        private static readonly Rect LaunchBox = new(8, (CellH - 22) / 2, 22, 22);
        private int _lastSig = -1;
        private bool _pressed, _dragging;
        private Point _pressAt;

        public void Tick()
        {
            int sig = 0;
            bool queued = false;
            foreach (var c in _o.LaunchCols)
            {
                int st = _o._engine.SessionSlotState(c.TrackId, _scene);
                if (st == 2) queued = true;
                sig = unchecked(sig * 7 + st);
            }
            if (_o._sceneDragTarget == _scene) sig ^= 0x5A5A;
            if (sig == _lastSig && !queued) return;
            _lastSig = sig;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            bool h = LaunchBox.Contains(p);
            if (h != _hoverLaunch) { _hoverLaunch = h; Cursor = h ? new Cursor(StandardCursorType.Hand) : Cursor.Default; InvalidateVisual(); }
            // Drag a scene up or down the rail to reorder it.
            if (_pressed && !_dragging && Math.Abs(p.Y - _pressAt.Y) > 5) _dragging = true;
            if (_dragging)
            {
                Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
                int t = _o.SceneAt(e.GetPosition(_o));
                int? target = t == _scene ? null : t;
                if (target != _o._sceneDragTarget) { _o._sceneDragTarget = target; _o.InvalidateAll(); }
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            bool wasDragging = _dragging;
            _pressed = _dragging = false;
            e.Pointer.Capture(null);
            if (_o._sceneDragTarget is { } to && wasDragging)
            {
                _o._sceneDragTarget = null;
                if (_o._engine.MoveScene(_scene, to)) { _o.Refresh(); _o.Select(new Selection(SelKind.Scene, 0, to)); _o.Say("Moved scene"); }
                return;
            }
            if (_o._sceneDragTarget is not null) { _o._sceneDragTarget = null; _o.InvalidateAll(); }
        }

        protected override void OnPointerExited(PointerEventArgs e) { if (_hoverLaunch) { _hoverLaunch = false; InvalidateVisual(); } }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            if (pt.Properties.IsRightButtonPressed)
            {
                _o.Select(new Selection(SelKind.Scene, 0, _scene));
                _o.ShowSceneMenu(this, _scene);
                e.Handled = true;
                return;
            }
            if (!pt.Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (LaunchBox.Contains(pt.Position)) { _o.LaunchSceneRow(_scene); _o.UpdateStates(); return; }
            _o.Select(new Selection(SelKind.Scene, 0, _scene));
            if (e.ClickCount == 2) { _o.PromptText(this, _named ? _name : "", t => { _o._engine.SetSceneName(_scene, t); _o.RefreshScene(_scene); }); return; }
            _pressed = true; _pressAt = pt.Position;
            e.Pointer.Capture(this);
        }

        public override void Render(DrawingContext ctx)
        {
            var r = new Rect(Bounds.Size);
            var eng = _o._engine;
            bool playing = false, all = true, queued = false, any = false;
            foreach (var c in _o.LaunchCols)
            {
                int st = eng.SessionSlotState(c.TrackId, _scene);
                if (st == 0) continue;
                any = true;
                if (st == 3) playing = true; else all = false;
                if (st == 2) queued = true;
            }
            bool selected = _o._sel.Kind == SelKind.Scene && _o._sel.Scene == _scene;
            bool rowSel = _o._sel.Kind == SelKind.Slot && _o._sel.Scene == _scene;
            IBrush? edge = _o._sceneDragTarget == _scene ? NotaPalette.Accent : any && playing && all ? NotaPalette.Wash(NotaPalette.Success, 0x66) : null;
            FillCell(ctx, r, rowSel ? NotaPalette.SurfaceRaised : NotaPalette.SurfaceCard, edge);

            ctx.DrawRectangle(_hoverLaunch ? NotaPalette.SurfaceHover : NotaPalette.BgSunken, new Pen(NotaPalette.BorderDefault, 1), LaunchBox.Deflate(0.5), NotaRadius.BadgeValue, NotaRadius.BadgeValue);
            IBrush tri = queued ? (_o._blinkOn ? NotaPalette.Warning : NotaPalette.Wash(NotaPalette.Warning, 0x66)) : playing ? NotaPalette.Success : NotaPalette.TextSecondary;
            DrawTriangle(ctx, LaunchBox.Center, 8, 9, tri);

            double x = LaunchBox.Right + 8;
            ctx.DrawRectangle(_color, null, new Rect(x, r.Height / 2 - 3, 6, 6), 1, 1);
            x += 14;
            double right = r.Width - 8;
            if (_badge.Length > 0)
            {
                var b = Ft(_badge, NotaFonts.Mono, 9, NotaPalette.TextSecondary);
                var box = new Rect(right - b.Width - 8, r.Height / 2 - 8, b.Width + 8, 16);
                ctx.DrawRectangle(null, new Pen(NotaPalette.BorderDefault, 1), box.Deflate(0.5), NotaRadius.BadgeValue, NotaRadius.BadgeValue);
                ctx.DrawText(b, new Point(box.X + 4, box.Y + (16 - b.Height) / 2));
                right = box.X - 6;
            }
            var idx = Ft((_scene + 1).ToString("00", CultureInfo.CurrentCulture), NotaFonts.Mono, 9, NotaPalette.TextDisabled);
            var name = Ft(_name, NotaFonts.SansSemiBold, 12, _named ? NotaPalette.TextPrimary : NotaPalette.TextTertiary, Math.Max(10, right - x - idx.Width - 6));
            ctx.DrawText(name, new Point(x, (r.Height - name.Height) / 2));
            ctx.DrawText(idx, new Point(x + name.Width + 6, (r.Height - name.Height) / 2 + (name.Baseline - idx.Baseline)));
            if (selected) DrawSelection(ctx, r);
        }
    }

    // ---- context menus -------------------------------------------------------------------

    private void ShowSlotMenu(Control anchor, Column c, int scene)
    {
        bool filled = SlotFilled(c.TrackId, scene);
        var f = new MenuFlyout();
        if (filled)
        {
            f.Items.Add(MenuKit.Item("Launch", GlyphKind.Play, () => _engine.LaunchSlot(c.TrackId, scene)));
            f.Items.Add(MenuKit.Item("Edit", GlyphKind.Edit, () => SlotEditRequested?.Invoke(c.TrackId, scene)));
            f.Items.Add(MenuKit.Item("Rename…", GlyphKind.Edit, () => RenameSelection(), new KeyGesture(Key.F2)));
            f.Items.Add(new Separator());
            f.Items.Add(MenuKit.Item("Copy", GlyphKind.Copy, () => CopySelection(), MenuKit.CopyKey));
            f.Items.Add(MenuKit.Item("Cut", GlyphKind.Cut, () => CutSelection(), MenuKit.CutKey));
        }
        f.Items.Add(MenuKit.Item("Paste", GlyphKind.Paste, () => PasteSelection(), MenuKit.PasteKey, CanPaste));
        if (filled)
        {
            f.Items.Add(MenuKit.Item("Duplicate", GlyphKind.Duplicate, () => DuplicateSelection(), MenuKit.DuplicateKey));
            f.Items.Add(MenuKit.Item("Copy to Arrangement", GlyphKind.Arrow, () => CopySelectionToArrangement(), MenuKit.CopyToOtherViewKey));
            f.Items.Add(new Separator());
            f.Items.Add(MenuKit.Item("Delete", GlyphKind.Trash, () => DeleteSelection(), MenuKit.DeleteKey));
        }
        else
        {
            f.Items.Add(new Separator());
            if (c.IsInstrument) f.Items.Add(MenuKit.Item("Insert MIDI clip", GlyphKind.Plus, () => InsertMidiClip(c.TrackId, scene)));
            f.Items.Add(MenuKit.Item("Record here", GlyphKind.Record, () => { _engine.RecordSessionSlot(c.TrackId, scene); Refresh(); }));
            bool stop = _engine.GetSessionSlotStopButton(c.TrackId, scene);
            f.Items.Add(MenuKit.Item(stop ? "Remove stop button" : "Add stop button", GlyphKind.Stop, () => { _engine.SetSessionSlotStopButton(c.TrackId, scene, !stop); RefreshSlot(c.TrackId, scene); }));
        }
        f.Items.Add(new Separator());
        foreach (var mi in AddTrackItems(c.TrackId)) f.Items.Add(mi);
        f.ShowAt(anchor, showAtPointer: true);
    }

    // "Add …" entries: the new track lands after `anchorTrackId` (inside it for a group), or at the end.
    private IEnumerable<MenuItem> AddTrackItems(int anchorTrackId)
    {
        yield return MenuKit.Item("Add instrument track", GlyphKind.Plus, () => AddTrackRequested?.Invoke(NewTrackKind.Instrument, anchorTrackId));
        yield return MenuKit.Item("Add audio track", GlyphKind.Plus, () => AddTrackRequested?.Invoke(NewTrackKind.Audio, anchorTrackId));
        yield return MenuKit.Item("Add return track", GlyphKind.Plus, () => AddTrackRequested?.Invoke(NewTrackKind.Return, anchorTrackId));
    }

    private void ShowAddTrackMenu(Control anchor, int anchorTrackId)
    {
        var f = new MenuFlyout();
        foreach (var mi in AddTrackItems(anchorTrackId)) f.Items.Add(mi);
        f.ShowAt(anchor, showAtPointer: true);
    }

    // Column header menu: rename / delete the track, or add one next to it.
    private void ShowHeaderMenu(Control anchor, Column c)
    {
        var f = new MenuFlyout();
        if (c.Kind != ColKind.Master)
        {
            f.Items.Add(MenuKit.Item("Rename…", GlyphKind.Edit, () => PromptRenameTrack(anchor, c.TrackId)));
            f.Items.Add(MenuKit.Item(c.Kind == ColKind.Group ? "Delete group and its tracks" : "Delete track", GlyphKind.Trash, () =>
            {
                _engine.RemoveTracks(new[] { c.TrackId });   // a group takes its tracks along, one undo step
                Refresh();
                ArrangementChanged?.Invoke();
            }));
            f.Items.Add(new Separator());
        }
        foreach (var mi in AddTrackItems(c.Kind == ColKind.Master ? -1 : c.TrackId)) f.Items.Add(mi);
        f.ShowAt(anchor, showAtPointer: true);
    }

    private void PromptRenameTrack(Control anchor, int trackId)
        => PromptText(anchor, _engine.GetTrackName(trackId), t =>
        {
            _engine.SetTrackName(trackId, t);
            Refresh();
            ArrangementChanged?.Invoke();   // the arrangement header shows the same name
        });

    private void ShowSceneMenu(Control anchor, int scene)
    {
        var f = new MenuFlyout();
        f.Items.Add(MenuKit.Item("Launch scene", GlyphKind.Play, () => LaunchSceneRow(scene)));
        f.Items.Add(MenuKit.Item("Stop scene", GlyphKind.Stop, () => _engine.StopScene(scene)));
        f.Items.Add(MenuKit.Item("Rename…", GlyphKind.Edit, () => RenameSelection(), new KeyGesture(Key.F2)));
        f.Items.Add(new Separator());
        f.Items.Add(MenuKit.Item("Insert scene below", GlyphKind.Plus, () => InsertSceneAtSelection(), MenuKit.Cmd(Key.I)));
        f.Items.Add(MenuKit.Item("Duplicate scene", GlyphKind.Duplicate, () => DuplicateSelection(), MenuKit.DuplicateKey));
        f.Items.Add(MenuKit.Item("Capture playing clips below", GlyphKind.Copy, CaptureScene));
        f.Items.Add(MenuKit.Item("Paste", GlyphKind.Paste, () => PasteSelection(), MenuKit.PasteKey, ClipTransfer.FromArrangement(_engine)));
        f.Items.Add(MenuKit.Item("Copy scene to Arrangement", GlyphKind.Arrow, () => CopySelectionToArrangement(), MenuKit.CopyToOtherViewKey, SelectedSlots().Length > 0));
        f.Items.Add(new Separator());
        f.Items.Add(MenuKit.Item("Delete scene", GlyphKind.Trash, () => DeleteSelection(), MenuKit.DeleteKey, _sceneCount > 1));
        f.ShowAt(anchor, showAtPointer: true);
    }
}
