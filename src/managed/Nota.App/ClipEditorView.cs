// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Clip (mockup 1e): composes the 200px props rail (ClipPropsView) with
// the piano roll (PianoRollView). A header row carries the Notes | Envelopes
// tabs; Envelopes (M9 follow-up) swaps in a per-clip envelope editor with a
// Velocity / Volume target selector.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

public sealed class ClipEditorView : UserControl
{
    private static readonly IBrush Panel = NotaPalette.SurfaceCard;
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush AccentSubtle = NotaPalette.AccentSubtle;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;

    public PianoRollView Roll { get; }

    private readonly ContentControl _rightHost;
    private readonly EnvPanel? _envPanel;
    private readonly ClipPropsView _props;
    private Border _notesTab = null!, _envTab = null!;

    public ClipEditorView(PianoRollView roll, string clipName, double startBeat,
                          IAudioEngine? engine = null, int trackId = -1, int clipIndex = -1, double lengthBeats = 4)
    {
        Roll = roll;

        _rightHost = new ContentControl { Content = roll };
        if (engine is not null && trackId > 0 && clipIndex >= 0)
            _envPanel = new EnvPanel(engine, trackId, clipIndex, lengthBeats);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        _props = new ClipPropsView(roll, clipName, startBeat);
        grid.Children.Add(_props);
        Grid.SetColumn(_rightHost, 1);
        grid.Children.Add(_rightHost);

        var header = TabsHeader();
        DockPanel.SetDock(header, Dock.Top);
        var dock = new DockPanel();
        dock.Children.Add(header);
        dock.Children.Add(grid);
        Content = dock;
    }

    /// <summary>Follows a clip that was moved or resized in the arrangement while this editor
    /// stayed on screen. The roll's own length is pushed by <c>PianoRollView.SetNotes</c>
    /// (which also refreshes the props rail's LENGTH/LOOP); this carries the pieces that
    /// otherwise keep their construction-time snapshot.</summary>
    public void SetClipBounds(double startBeat, double lengthBeats)
    {
        _props.SetStart(startBeat);
        _envPanel?.SetLength(lengthBeats);
    }

    private void ShowTab(bool envelopes)
    {
        if (envelopes && _envPanel is null) return;
        _rightHost.Content = envelopes ? _envPanel : Roll;
        PaintTab(_notesTab, !envelopes);
        PaintTab(_envTab, envelopes);
    }

    private static void PaintTab(Border tab, bool active)
    {
        tab.Background = active ? AccentSubtle : Brushes.Transparent;
        tab.BorderBrush = active ? Brass : Brushes.Transparent;
        ((TextBlock)tab.Child!).Foreground = active ? AccentBright : TextTertiary;
    }

    private Control TabsHeader()
    {
        _notesTab = Tab("Notes");
        _notesTab.PointerPressed += (_, _) => ShowTab(false);
        _envTab = Tab("Envelopes");
        _envTab.PointerPressed += (_, _) => ShowTab(true);
        _envTab.IsEnabled = _envPanel is not null;
        _envTab.Opacity = _envPanel is not null ? 1 : 0.5;
        PaintTab(_notesTab, true);
        PaintTab(_envTab, false);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { _notesTab, _envTab } };
        var scale = ScaleControls();
        DockPanel.SetDock(scale, Dock.Right);
        return new Border
        {
            Height = 30, Background = Panel, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 0),
            Child = new DockPanel { LastChildFill = true, Children = { scale, tabs } },
        };
    }

    // ---- scale overlay pickers ("Set Scale") ----------------
    private Border _scaleToggle = null!, _keyChip = null!, _scaleChip = null!;

    private Control ScaleControls()
    {
        _scaleToggle = Chip();
        _keyChip = Chip();
        _scaleChip = Chip();
        _scaleToggle.PointerPressed += (_, e) => { e.Handled = true; Roll.SetScale(!Roll.ScaleOn, Roll.ScaleRoot, Roll.ScaleIndex); UpdateScaleChips(); };
        _keyChip.PointerPressed += (_, e) => { e.Handled = true; ShowMenu(_keyChip, PianoRollView.KeyNames, Roll.ScaleRoot, i => { Roll.SetScale(true, i, Roll.ScaleIndex); UpdateScaleChips(); }); };
        _scaleChip.PointerPressed += (_, e) => { e.Handled = true; ShowMenu(_scaleChip, PianoRollView.ScaleNames, Roll.ScaleIndex, i => { Roll.SetScale(true, Roll.ScaleRoot, i); UpdateScaleChips(); }); };
        UpdateScaleChips();
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "SCALE", FontSize = 8, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) },
                _scaleToggle, _keyChip, _scaleChip,
            },
        };
    }

    private void UpdateScaleChips()
    {
        bool on = Roll.ScaleOn;
        SetChip(_scaleToggle, on ? "On" : "Off", on);
        SetChip(_keyChip, PianoRollView.KeyNames[Roll.ScaleRoot] + "  ▾", on);
        SetChip(_scaleChip, PianoRollView.ScaleNames[Roll.ScaleIndex] + "  ▾", on);
        _keyChip.Opacity = on ? 1 : 0.5;
        _scaleChip.Opacity = on ? 1 : 0.5;
    }

    private static Border Chip() => new()
    {
        Height = 22, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
        Background = NotaPalette.SurfaceRaised, BorderBrush = NotaPalette.BorderStrong,
        Padding = new Thickness(9, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand),
        Child = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Foreground = TextSecondary },
    };

    private static void SetChip(Border chip, string text, bool active)
    {
        var tb = (TextBlock)chip.Child!;
        tb.Text = text;
        tb.Foreground = active ? AccentBright : TextSecondary;
        chip.Background = active ? AccentSubtle : NotaPalette.SurfaceRaised;
        chip.BorderBrush = active ? Brass : NotaPalette.BorderStrong;
    }

    private static void ShowMenu(Control anchor, string[] items, int current, Action<int> pick)
    {
        var f = new MenuFlyout();
        for (int i = 0; i < items.Length; i++)
        {
            int idx = i;
            var mi = new MenuItem { Header = items[i] };
            if (i == current) mi.Icon = new TextBlock { Text = "•", Foreground = AccentBright };
            mi.Click += (_, _) => pick(idx);
            f.Items.Add(mi);
        }
        f.ShowAt(anchor);
    }

    private static Border Tab(string text) => new()
    {
        Height = 22, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
        Padding = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand),
        Child = new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
    };

    // ---- envelope panel: target selector + editable curve -----------------
    private sealed class EnvPanel : UserControl
    {
        private readonly IAudioEngine _engine;
        private readonly int _trackId, _clipIndex;
        private readonly EnvCanvas _canvas;
        private readonly TextBlock _targetText;
        private MidiClipEnvelope _target = MidiClipEnvelope.Velocity;

        public EnvPanel(IAudioEngine engine, int trackId, int clipIndex, double lengthBeats)
        {
            _engine = engine; _trackId = trackId; _clipIndex = clipIndex;
            _canvas = new EnvCanvas(lengthBeats) { Committed = OnCommitted };
            _targetText = new TextBlock { Text = "Velocity ▾", FontSize = 10, Foreground = AccentBright, VerticalAlignment = VerticalAlignment.Center };

            var chip = new Border
            {
                Height = 22, Background = NotaPalette.SurfaceRaised, BorderBrush = NotaPalette.BorderStrong,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 0),
                Cursor = new Cursor(StandardCursorType.Hand), Child = _targetText,
            };
            chip.PointerPressed += (_, e) => { e.Handled = true; CycleTarget(); };
            var hint = new TextBlock { Text = "click to add · drag to move · right-click to delete", FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var bar = new Border
            {
                Height = 30, Background = Panel, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 0),
                Child = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { chip, hint } },
            };
            DockPanel.SetDock(bar, Dock.Top);
            var dock = new DockPanel();
            dock.Children.Add(bar);
            dock.Children.Add(new Border { Background = Sunken, Child = _canvas });
            Content = dock;
            Load();
        }

        public void SetLength(double lengthBeats) => _canvas.SetLength(lengthBeats);

        private void CycleTarget()
        {
            _target = _target == MidiClipEnvelope.Velocity ? MidiClipEnvelope.Volume : MidiClipEnvelope.Velocity;
            _targetText.Text = (_target == MidiClipEnvelope.Volume ? "Volume" : "Velocity") + " ▾";
            Load();
        }

        private void Load()
        {
            var pts = _engine.GetMidiClipEnvelope(_trackId, _clipIndex, _target);
            var b = new double[pts.Length]; var v = new double[pts.Length]; var c = new float[pts.Length];
            for (int i = 0; i < pts.Length; i++) { b[i] = pts[i].Beat; v[i] = pts[i].Value; c[i] = pts[i].Curve; }
            _canvas.SetPoints(b, v, c);
        }

        private void OnCommitted(double[] beats, double[] values, float[] curves)
        {
            var pts = new AutomationPoint[beats.Length];
            for (int i = 0; i < beats.Length; i++) pts[i] = new AutomationPoint(beats[i], (float)values[i], curves[i]);
            _engine.SetMidiClipEnvelope(_trackId, _clipIndex, _target, pts);
        }
    }

    private sealed class EnvPt { public double Beat; public double Val; public float Curve; }

    // Custom-drawn envelope editor (0..1 over clip-local beats). Points carry a
    // per-segment curve (M9-D): drag a point to move it, drag a segment line to bend
    // it, right-click/double-click a point or segment to delete / reset to linear.
    private sealed class EnvCanvas : Control
    {
        private static readonly IBrush Bg = NotaPalette.BgSunken;
        private static readonly IBrush GridBeat = new SolidColorBrush(Color.FromArgb(0x50, 0x3A, 0x36, 0x2D));
        private static readonly IBrush EnvLine = NotaPalette.AccentBright;
        private readonly System.Collections.Generic.List<EnvPt> _pts = new();
        private double _clipBeats;
        private int _drag = -1;
        private EnvPt? _bend;
        private const double HandlePx = 7;

        public Action<double[], double[], float[]>? Committed;
        public EnvCanvas(double clipBeats) { _clipBeats = Math.Max(1e-6, clipBeats); }

        // The clip was trimmed/stretched in the arrangement: re-span the beat axis. Points keep
        // their beats (the engine clamps them to the new length), so only the mapping changes.
        public void SetLength(double clipBeats)
        {
            double v = Math.Max(1e-6, clipBeats);
            if (Math.Abs(v - _clipBeats) < 1e-9) return;
            _clipBeats = v;
            InvalidateVisual();
        }

        public void SetPoints(double[] beats, double[] values, float[] curves)
        {
            _pts.Clear();
            for (int i = 0; i < beats.Length; i++) _pts.Add(new EnvPt { Beat = beats[i], Val = values[i], Curve = curves[i] });
            InvalidateVisual();
        }

        private double BeatToX(double beat, double w) => beat / _clipBeats * w;
        private double XToBeat(double x, double w) => Math.Clamp(x / Math.Max(1, w), 0, 1) * _clipBeats;
        private static double ValToY(double v, double h) { double pad = 6; return pad + (1 - Math.Clamp(v, 0, 1)) * (h - 2 * pad); }
        private static double YToVal(double y, double h) { double pad = 6; return Math.Clamp((h - pad - y) / Math.Max(1, h - 2 * pad), 0, 1); }
        private static double Shape(double t, float curve) => curve == 0f ? t : Math.Pow(t, Math.Pow(2.0, -curve * 4.0));

        private int HitPoint(double x, double y, double w, double h)
        {
            for (int i = 0; i < _pts.Count; i++)
                if (Math.Abs(BeatToX(_pts[i].Beat, w) - x) <= HandlePx && Math.Abs(ValToY(_pts[i].Val, h) - y) <= HandlePx) return i;
            return -1;
        }
        // The left point of the segment whose (curved) line is under (x,y), or null.
        private EnvPt? HitSegment(double x, double y, double w, double h)
        {
            var ord = Sorted();
            double beat = XToBeat(x, w);
            for (int k = 1; k < ord.Count; k++)
            {
                var a = ord[k - 1]; var b = ord[k];
                if (beat < a.Beat || beat > b.Beat) continue;
                double span = b.Beat - a.Beat;
                if (span <= 0) return null;
                double vy = a.Val + (b.Val - a.Val) * Shape((beat - a.Beat) / span, a.Curve);
                return Math.Abs(ValToY(vy, h) - y) <= 6 ? a : null;
            }
            return null;
        }
        private System.Collections.Generic.List<EnvPt> Sorted()
        {
            var ord = new System.Collections.Generic.List<EnvPt>(_pts);
            ord.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            return ord;
        }
        private void Commit()
        {
            _pts.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            var beats = new double[_pts.Count]; var vals = new double[_pts.Count]; var curves = new float[_pts.Count];
            for (int i = 0; i < _pts.Count; i++) { beats[i] = _pts[i].Beat; vals[i] = _pts[i].Val; curves[i] = _pts[i].Curve; }
            Committed?.Invoke(beats, vals, curves);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            double w = Bounds.Width, h = Bounds.Height;
            var p = e.GetPosition(this);
            bool right = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;
            int hit = HitPoint(p.X, p.Y, w, h);
            if (hit >= 0)
            {
                if (right || e.ClickCount == 2) { _pts.RemoveAt(hit); Commit(); InvalidateVisual(); }
                else { _drag = hit; e.Pointer.Capture(this); }
                e.Handled = true;
                return;
            }
            // On a segment line? Bend it (or reset its curve).
            if (HitSegment(p.X, p.Y, w, h) is { } seg)
            {
                if (right || e.ClickCount == 2) { seg.Curve = 0f; Commit(); InvalidateVisual(); }
                else { _bend = seg; e.Pointer.Capture(this); }
                e.Handled = true;
                return;
            }
            if (right) return;
            var np = new EnvPt { Beat = XToBeat(p.X, w), Val = YToVal(p.Y, h) };   // add + grab
            _pts.Add(np);
            _drag = _pts.IndexOf(np);
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag >= 0)
            {
                _pts[_drag].Beat = XToBeat(p.X, Bounds.Width);
                _pts[_drag].Val = YToVal(p.Y, Bounds.Height);
                InvalidateVisual();
            }
            else if (_bend is { } bl)
            {
                // Bend so the segment midpoint reaches the pointer value (M9-D law).
                var ord = Sorted();
                int k = ord.IndexOf(bl);
                if (k < 0 || k + 1 >= ord.Count) return;
                double v0 = bl.Val, v1 = ord[k + 1].Val;
                if (Math.Abs(v1 - v0) < 1e-4) { bl.Curve = 0f; InvalidateVisual(); return; }
                double frac = Math.Clamp((YToVal(p.Y, Bounds.Height) - v0) / (v1 - v0), 0.02, 0.98);
                double ee = Math.Log(frac) / Math.Log(0.5);
                bl.Curve = (float)Math.Clamp(-Math.Log2(ee) / 4.0, -1.0, 1.0);
                InvalidateVisual();
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_drag < 0 && _bend is null) return;
            _drag = -1; _bend = null; e.Pointer.Capture(null); Commit();
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            ctx.FillRectangle(Bg, new Rect(0, 0, w, h));
            for (int b = 0; b <= (int)Math.Floor(_clipBeats + 1e-6); b++)
            {
                double x = BeatToX(b, w);
                if (x > w) break;
                ctx.DrawLine(new Pen(GridBeat, b % 4 == 0 ? 1 : 0.6), new Point(x, 0), new Point(x, h));
            }
            if (_pts.Count == 0) return;
            var ord = Sorted();
            var pen = new Pen(EnvLine, 1.6);
            double fy = ValToY(ord[0].Val, h);
            ctx.DrawLine(pen, new Point(0, fy), new Point(BeatToX(ord[0].Beat, w), fy));
            for (int i = 1; i < ord.Count; i++)
            {
                var a = ord[i - 1]; var b = ord[i];
                double ax = BeatToX(a.Beat, w), ay = ValToY(a.Val, h), bx = BeatToX(b.Beat, w), by = ValToY(b.Val, h);
                if (a.Curve == 0f) { ctx.DrawLine(pen, new Point(ax, ay), new Point(bx, by)); continue; }
                Point prev = new(ax, ay);
                for (int s = 1; s <= 16; s++)
                {
                    double tt = s / 16.0;
                    double vy = a.Val + (b.Val - a.Val) * Shape(tt, a.Curve);
                    Point cur = new(ax + (bx - ax) * tt, ValToY(vy, h));
                    ctx.DrawLine(pen, prev, cur); prev = cur;
                }
            }
            double ly = ValToY(ord[^1].Val, h);
            ctx.DrawLine(pen, new Point(BeatToX(ord[^1].Beat, w), ly), new Point(w, ly));
            foreach (var p in ord)
                ctx.DrawEllipse(EnvLine, null, new Point(BeatToX(p.Beat, w), ValToY(p.Val, h)), 3, 3);
        }
    }
}
