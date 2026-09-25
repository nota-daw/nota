// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Clip for MIDI clips (nota-design "Nota Clip Editor" 1a): a 38px header — the clip
// cell, Notes | Envelopes, then the Scale switch with its key and mode, and the Tools
// toggle — over the 232px inspector (ClipPropsView), the piano roll (PianoRollView) and
// the 272px clip-tools panel (MidiToolsView). Envelopes is a mode of the same canvas: the
// notes dim under a wash and the clip envelope (Velocity / Volume) is drawn over them in
// brass, on the roll's own beat axis so zoom and scroll carry over.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.ClipEditorKit;

namespace Nota.App;

public sealed class ClipEditorView : UserControl
{
    public PianoRollView Roll { get; }

    private readonly EnvOverlay? _envOverlay;
    private readonly ClipPropsView _props;
    private readonly MidiToolsView _tools;
    private readonly ToggleButton _toolsToggle;
    private Action<int> _setTab = _ => { };
    private bool _envMode;

    public ClipEditorView(PianoRollView roll, string clipName, double startBeat,
                          IAudioEngine? engine = null, int trackId = -1, int clipIndex = -1, double lengthBeats = 4)
    {
        Roll = roll;
        if (engine is not null && trackId > 0 && clipIndex >= 0)
        {
            _envOverlay = new EnvOverlay(roll, engine, trackId, clipIndex, lengthBeats);
            var named = engine.GetClipName(trackId, clipIndex);
            if (!string.IsNullOrWhiteSpace(named)) clipName = named;
        }

        // Inspector | roll | clip-tools panel. The tools panel is a toggle rather than a
        // tab because its whole point is watching the roll change while you turn a knob.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        _props = new ClipPropsView(roll, startBeat);
        grid.Children.Add(_props);
        Grid.SetColumn(roll, 1);
        grid.Children.Add(roll);
        _tools = new MidiToolsView(roll) { IsVisible = false };
        Grid.SetColumn(_tools, 2);
        grid.Children.Add(_tools);

        _toolsToggle = new ToggleButton { Content = "Tools", Padding = new Thickness(12, 0), Classes = { "chip" } };
        _toolsToggle.Click += (_, _) => SetTools(_toolsToggle.IsChecked == true);

        var tabs = Segments(new[] { "Notes", "Envelopes" }, 0, i => ShowTab(i == 1), out _setTab);
        if (_envOverlay is null) Inactive.Set(tabs, true);   // session slots carry no clip envelope
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children = { ScaleControls(), HeaderDivider(), _toolsToggle },
        };
        var header = Header(roll.TrackBrush, clipName, "MIDI", tabs, null, right);
        DockPanel.SetDock(header, Dock.Top);
        Content = new DockPanel { Background = NotaPalette.Gutter, Children = { header, grid } };
    }

    /// <summary>Follows a clip that was moved or resized in the arrangement while this editor
    /// stayed on screen. The roll's own length is pushed by <c>PianoRollView.SetNotes</c>
    /// (which also refreshes the inspector's LENGTH/LOOP); this carries the pieces that
    /// otherwise keep their construction-time snapshot.</summary>
    public void SetClipBounds(double startBeat, double lengthBeats)
    {
        _props.SetStart(startBeat);
        _envOverlay?.SetLength(lengthBeats);
    }

    private void ShowTab(bool envelopes)
    {
        if (envelopes && _envOverlay is null) { _setTab(0); return; }
        _envMode = envelopes;
        Roll.SetOverlay(envelopes ? _envOverlay : null);
        if (envelopes && _tools.IsVisible) SetTools(false);
    }

    /// <summary>Shows or hides the clip-tools panel. Hiding drops the tool's preview, so
    /// walking away from a half-tweaked generator leaves the clip as it was.</summary>
    private void SetTools(bool show)
    {
        if (show && _envMode) { _setTab(0); ShowTab(false); }
        _tools.IsVisible = show;
        _toolsToggle.IsChecked = show;
        _tools.SetActive(show);
    }

    // ---- scale overlay ("Set Scale"): a switch, then the key and the mode ----
    private Action _syncScale = () => { };
    private TextBlock _keyText = null!, _modeText = null!;

    private Control ScaleControls()
    {
        _keyText = Mono("");
        _modeText = new TextBlock { FontSize = NotaType.Body };
        var sw = SwitchRow("Scale", () => Roll.ScaleOn, () => { Roll.SetScale(!Roll.ScaleOn, Roll.ScaleRoot, Roll.ScaleIndex); PaintScale(); }, out _syncScale);
        var key = Dropdown(_keyText, a => ShowMenu(a, PianoRollView.KeyNames, Roll.ScaleRoot, i => { Roll.SetScale(true, i, Roll.ScaleIndex); PaintScale(); }));
        var mode = Dropdown(_modeText, a => ShowMenu(a, PianoRollView.ScaleNames, Roll.ScaleIndex, i => { Roll.SetScale(true, Roll.ScaleRoot, i); PaintScale(); }));
        PaintScale();
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { sw, key, mode } };
    }

    private void PaintScale()
    {
        _syncScale();
        _keyText.Text = PianoRollView.KeyNames[Roll.ScaleRoot];
        _modeText.Text = PianoRollView.ScaleNames[Roll.ScaleIndex];
        var ink = Roll.ScaleOn ? NotaPalette.TextPrimary : NotaPalette.TextDisabled;
        _keyText.Foreground = ink;
        _modeText.Foreground = ink;
    }

    // ---- envelope overlay: a wash over the notes, the curve, a target picker ----------
    private sealed class EnvOverlay : UserControl
    {
        private static readonly string[] Targets = { "Velocity", "Volume" };
        private readonly IAudioEngine _engine;
        private readonly int _trackId, _clipIndex;
        private readonly EnvCanvas _canvas;
        private readonly TextBlock _targetText;
        private MidiClipEnvelope _target = MidiClipEnvelope.Velocity;

        public EnvOverlay(PianoRollView roll, IAudioEngine engine, int trackId, int clipIndex, double lengthBeats)
        {
            _engine = engine; _trackId = trackId; _clipIndex = clipIndex;
            _canvas = new EnvCanvas(roll, lengthBeats) { Committed = OnCommitted };
            _targetText = new TextBlock { Text = Targets[0], FontSize = NotaType.Body, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center };

            var chip = new Border
            {
                Height = 22, Background = NotaPalette.Panel, BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Control, Padding = new Thickness(8, 0), Margin = new Thickness(10, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "ENV", FontSize = 9, FontWeight = FontWeight.Bold, LetterSpacing = 1.08,
                            Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center,
                        },
                        _targetText,
                        new Glyph(GlyphKind.ChevronDown, 8) { Foreground = NotaPalette.TextDisabled, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            chip.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                ShowMenu(chip, Targets, _target == MidiClipEnvelope.Volume ? 1 : 0, i =>
                {
                    _target = i == 1 ? MidiClipEnvelope.Volume : MidiClipEnvelope.Velocity;
                    _targetText.Text = Targets[i];
                    Load();
                });
            };
            ToolTip.SetTip(chip, "Click to add a point · drag to move · right-click to delete");
            Content = new Grid
            {
                Children =
                {
                    new Border { Background = NotaPalette.Wash(NotaPalette.BgSunken, 0xB3), IsHitTestVisible = false },
                    _canvas,
                    chip,
                },
            };
            Load();
        }

        public void SetLength(double lengthBeats) => _canvas.SetLength(lengthBeats);

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

    // Custom-drawn envelope editor (0..1 over clip-local beats) on the roll's beat axis.
    // Points carry a per-segment curve (M9-D): drag a point to move it, drag a segment
    // line to bend it, right-click/double-click a point or segment to delete / reset to
    // linear. A brass line; nodes in Ink 3 ringed with the well, the one in hand in brass.
    private sealed class EnvCanvas : Control
    {
        private static readonly IPen EnvPen = new Pen(NotaPalette.Accent, 1.8);
        private static readonly IBrush NodeFill = NotaPalette.TextSecondary;
        private static readonly IBrush NodeHot = NotaPalette.Accent;
        private static readonly IPen NodeRing = new Pen(NotaPalette.BgSunken, 2);
        private static readonly IBrush EmptyInk = NotaPalette.TextTertiary;
        private readonly PianoRollView _roll;
        private readonly System.Collections.Generic.List<EnvPt> _pts = new();
        private double _clipBeats;
        private int _drag = -1, _hover = -1;
        private EnvPt? _bend;
        private const double HandlePx = 7;

        public Action<double[], double[], float[]>? Committed;
        public EnvCanvas(PianoRollView roll, double clipBeats)
        {
            _roll = roll;
            _clipBeats = Math.Max(1e-6, clipBeats);
            Background = Brushes.Transparent;   // take the pointer across the whole grid
        }

        public IBrush? Background { get; init; }

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

        private double BeatToX(double beat, double w) => _roll.BeatToX(beat);
        private double XToBeat(double x, double w) => Math.Clamp(_roll.XToBeat(x), 0, _clipBeats);
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

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) => _roll.RouteWheel(e, e.GetPosition(this).X);

        protected override void OnPointerExited(PointerEventArgs e)
        {
            if (_hover != -1) { _hover = -1; InvalidateVisual(); }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag < 0 && _bend is null)
            {
                int hv = HitPoint(p.X, p.Y, Bounds.Width, Bounds.Height);
                if (hv != _hover) { _hover = hv; InvalidateVisual(); }
            }
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
            if (Background is not null) ctx.FillRectangle(Background, new Rect(0, 0, w, h));
            if (_pts.Count == 0)   // empty state: one Ink 5 line
            {
                var ft = new FormattedText("Click to add a point", NotaNum.Culture, FlowDirection.LeftToRight, NotaFonts.Sans, NotaType.Body, EmptyInk);
                ctx.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
                return;
            }
            if (_pts.Count == 0) return;
            var ord = Sorted();
            var pen = EnvPen;
            double fy = ValToY(ord[0].Val, h);
            ctx.DrawLine(pen, new Point(Math.Max(0, BeatToX(0, w)), fy), new Point(BeatToX(ord[0].Beat, w), fy));
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
            ctx.DrawLine(pen, new Point(BeatToX(ord[^1].Beat, w), ly), new Point(Math.Min(w, BeatToX(_clipBeats, w)), ly));
            for (int i = 0; i < _pts.Count; i++)
            {
                var p = _pts[i];
                bool hot = i == _drag || i == _hover;
                ctx.DrawEllipse(hot ? NodeHot : NodeFill, NodeRing, new Point(BeatToX(p.Beat, w), ValToY(p.Val, h)), 4.5, 4.5);
            }
        }
    }
}
