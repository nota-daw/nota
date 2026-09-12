// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Piano roll (mockup 1e). Composed of a pinned beat ruler, a vertically
// scrollable keys column + note grid (88-key range, black keys tinted, white
// keys light), and a pinned velocity lane. The grid auto-fits the clip length
// horizontally (no horizontal scroll), so the ruler / grid / velocity always
// share one time axis. Notes render in the track colour with opacity encoding
// velocity; the selected note fills accent-bright. The control owns the note
// list; every edit invokes Commit so the window pushes the set to the engine.

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

namespace Nota.App;

public sealed class PianoRollView : UserControl
{
    public Action<IReadOnlyList<NotaNote>>? Commit { get; set; }
    /// <summary>Live, no-undo push during a note drag so playback follows instantly. The grid
    /// seeds one undo entry (via <see cref="Commit"/>) at the gesture start, then streams here.</summary>
    public Action<IReadOnlyList<NotaNote>>? CommitLive { get; set; }
    /// <summary>Polls the engine for currently-pressed live-input pitches (keyboard + MIDI) into
    /// the buffer, returning the count — used to light up the played key. Set by the host.</summary>
    public Func<int[], int>? PollHeldNotes { get; set; }
    public event Action? Changed;

    // Currently-pressed keys (from PollHeldNotes), for the played-key highlight.
    private readonly HashSet<int> _held = new();
    private readonly int[] _heldBuf = new int[16];
    private DispatcherTimer? _heldTimer;

    // --- per-clip view persistence (scroll/zoom don't reset when you re-enter a clip) ------
    private readonly record struct ViewState(double Ppb, double Scroll, double VOffset, bool HasV);
    private static readonly Dictionary<(int, int), ViewState> s_view = new();
    private (int, int) _clipKey = (int.MinValue, int.MinValue);
    private double? _pendingVOffset;   // absolute vertical offset to apply once the body is laid out
    private bool _pendingCenter;       // instead: centre on the clip's notes once laid out
    private bool _restoringView;       // suppress view-saves while we programmatically restore

    /// <summary>Identifies the edited clip so its scroll/zoom/vertical position persist across
    /// re-opens. Call before <see cref="SetNotes"/>.</summary>
    public void SetClipIdentity(int a, int b) => _clipKey = (a, b);

    private const int MinPitch = 21, MaxPitch = 108;   // A0..C8 (88 keys)
    private const double KeysW = 56, RowH = 14, RulerH = 20, VelH = 72;

    private readonly List<NotaNote> _notes = new();
    private double _lengthBeats = 4;
    private double _grid = 0.25;
    private readonly HashSet<int> _selection = new();   // indices into _notes
    private Color _trackColor = NotaPalette.TrackColors[3];

    // --- musical scale overlay ("Set Scale") ----------------
    // Session-global (static seed) so the chosen key/scale persists as you move
    // between clips, like a global scale setting. Off by default → chromatic, no
    // dimming. Masks mirror the engine's Nota Scale device (src/MidiScale.h).
    public static readonly string[] KeyNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    public static readonly string[] ScaleNames =
        { "Major", "Natural Minor", "Harmonic Minor", "Dorian", "Phrygian",
          "Lydian", "Mixolydian", "Pentatonic Major", "Pentatonic Minor", "Chromatic" };
    private static readonly ushort[] ScaleMasks =
        { 2741, 1453, 2477, 1709, 1451, 2773, 1717, 661, 1193, 4095 };

    private static bool s_scaleOn;
    private static int  s_root;
    private static int  s_scaleIndex;
    private bool _scaleOn = s_scaleOn;
    private int  _scaleRoot = s_root;
    private int  _scaleIndex = s_scaleIndex;

    public bool ScaleOn => _scaleOn;
    public int  ScaleRoot => _scaleRoot;
    public int  ScaleIndex => _scaleIndex;

    /// <summary>Sets the piano-roll scale overlay (key + mode). Persists across clips.</summary>
    public void SetScale(bool on, int root, int scaleIndex)
    {
        _scaleOn = s_scaleOn = on;
        _scaleRoot = s_root = ((root % 12) + 12) % 12;
        _scaleIndex = s_scaleIndex = Math.Clamp(scaleIndex, 0, ScaleMasks.Length - 1);
        Invalidate();
    }

    // In-scale when disabled → everything reads normally. Root = tonic of the key.
    private bool InScale(int pitch)
    {
        if (!_scaleOn) return true;
        int pc = (((pitch - _scaleRoot) % 12) + 12) % 12;
        return (ScaleMasks[_scaleIndex] & (1 << pc)) != 0;
    }
    private bool IsRoot(int pitch) => _scaleOn && ((((pitch - _scaleRoot) % 12) + 12) % 12) == 0;

    // Live playback cursor in clip-local beats; < 0 = hidden. Set by the host each tick.
    private double _playheadBeat = -1;

    // Note clipboard, shared across clips (copy here, paste into another clip). Stored
    // normalized so the earliest note starts at beat 0; s_clipAnchor is its origin.
    private static readonly List<NotaNote> s_clipboard = new();
    private static double s_clipAnchor;

    private readonly RulerLane _ruler;
    private readonly KeysColumn _keys;
    private readonly NoteGrid _gridControl;
    private readonly VelocityLane _vel;
    private readonly ScrollViewer _bodyScroll;
    private readonly ScrollBar _hScroll;

    // Horizontal zoom/scroll (mirrors ArrangementView). _pixelsPerBeat <= 0 means
    // "auto-fit to the viewport" until the first layout resolves it; after that the
    // user drives it with ⌘/Ctrl+wheel (zoom) and the scrollbar / Shift+wheel (scroll).
    private double _pixelsPerBeat;   // 0 = not yet fitted
    private double _scrollBeats;
    private const double MinPpb = 6, MaxPpb = 400;

    internal double PixelsPerBeat => _pixelsPerBeat;
    internal double ScrollBeats => _scrollBeats;
    internal double BeatToX(double b) => (b - _scrollBeats) * _pixelsPerBeat;
    internal double XToBeat(double x) => x / Math.Max(1e-6, _pixelsPerBeat) + _scrollBeats;
    internal void EnsurePpb(double viewportW)
    {
        if (_pixelsPerBeat <= 0 && viewportW > 0)
            _pixelsPerBeat = Math.Clamp(viewportW / _lengthBeats, MinPpb, MaxPpb);
    }

    private double MaxScrollBeats
    {
        get { double vp = _gridControl.Bounds.Width; double vpBeats = _pixelsPerBeat > 0 ? vp / _pixelsPerBeat : _lengthBeats; return Math.Max(0, _lengthBeats - vpBeats); }
    }

    internal void SetScrollBeats(double b)
    {
        _scrollBeats = Math.Clamp(b, 0, MaxScrollBeats);
        SyncScroll(); Invalidate(); SaveView();
    }

    internal void ScrollByBeats(double delta) => SetScrollBeats(_scrollBeats + delta);

    internal void ZoomAt(double factor, double beatAnchor)
    {
        EnsurePpb(_gridControl.Bounds.Width);
        double old = _pixelsPerBeat;
        _pixelsPerBeat = Math.Clamp(_pixelsPerBeat * factor, MinPpb, MaxPpb);
        if (Math.Abs(_pixelsPerBeat - old) < 1e-9) return;
        _scrollBeats = Math.Clamp(beatAnchor - (beatAnchor - _scrollBeats) * (old / _pixelsPerBeat), 0, MaxScrollBeats);
        SyncScroll(); Invalidate(); SaveView();
    }

    private void SyncScroll()
    {
        double vp = _gridControl.Bounds.Width;
        double vpBeats = _pixelsPerBeat > 0 ? vp / _pixelsPerBeat : _lengthBeats;
        _hScroll.ViewportSize = vpBeats;
        _hScroll.Maximum = Math.Max(0, _lengthBeats - vpBeats);
        _hScroll.Value = Math.Clamp(_scrollBeats, 0, _hScroll.Maximum);
        _hScroll.IsVisible = _hScroll.Maximum > 1e-6;
    }

    private static readonly IBrush Bg = NotaPalette.BgApp;
    private static readonly IBrush KeysBg = NotaPalette.BgSunken;
    private static readonly IBrush VelBg = NotaPalette.GridRow;
    private static readonly IBrush KeyWhite = NotaPalette.KeyWhite;
    private static readonly IBrush KeyBlack = NotaPalette.KeyBlack;
    private static readonly IBrush RowBlackTint = new SolidColorBrush(Color.FromArgb(0x2E, 0x00, 0x00, 0x00));
    // Scale overlay: out-of-scale rows/keys get a translucent dark wash (they read
    // dimmed / semi-transparent), in-scale rows a faint accent, the root a stronger one.
    private static readonly IBrush OutScaleWash = NotaPalette.Wash(NotaPalette.BgSunken, 0xB4);
    private static readonly IBrush InScaleTint = NotaPalette.Wash(NotaPalette.AccentBright, 0x1E);
    private static readonly IBrush RootTint = NotaPalette.Wash(NotaPalette.AccentBright, 0x40);
    private static readonly IPen PlayheadPen = new Pen(NotaPalette.AccentBright, 1.5);
    private static readonly IBrush KeyHover = NotaPalette.Wash(NotaPalette.AccentBright, 0x66);
    // Played-key highlight (currently-pressed keyboard / MIDI note): a solid key tint + a faint row wash.
    private static readonly IBrush HeldKeyFill = NotaPalette.AccentBright;
    private static readonly IBrush HeldRowTint = NotaPalette.Wash(NotaPalette.AccentBright, 0x30);
    private static readonly IPen KeyLine = new Pen(NotaPalette.BgApp, 1);
    private static readonly IPen RowLine = new Pen(NotaPalette.GridRow, 1);
    private static readonly IPen BeatPen = new Pen(NotaPalette.GridBeat, 1);
    private static readonly IPen BarPen = new Pen(NotaPalette.GridBar, 1);
    private static readonly IPen SubBeatPen = new Pen(NotaPalette.GridSubBeat, 1);   // sub-beat grid (finer than a beat)
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush EdgeHighlight = NotaPalette.AccentPale;
    private static readonly IBrush MarqueeFill = NotaPalette.Wash(NotaPalette.AccentBright, 0x28);
    private static readonly IPen MarqueePen = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0xA0), 1);
    private static readonly IBrush LabelText = NotaPalette.TextTertiary;
    private static readonly IBrush KeyLabel = NotaPalette.BgSunken;
    private static readonly IBrush Divider = NotaPalette.BorderDefault;
    private static readonly Typeface Mono = new("monospace");

    private static double RangeH => (MaxPitch - MinPitch + 1) * RowH;

    public PianoRollView()
    {
        // Clip the horizontally-scrolling lanes to their own column so notes / beat marks
        // scrolled past the left edge don't bleed over the fixed keyboard + label gutter.
        _ruler = new RulerLane(this) { ClipToBounds = true };
        _keys = new KeysColumn(this) { Height = RangeH };
        _gridControl = new NoteGrid(this) { Height = RangeH, ClipToBounds = true };
        _vel = new VelocityLane(this) { ClipToBounds = true };

        // Body: keys pinned left, grid fills; both scroll vertically together.
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*") };
        body.Children.Add(_keys);
        Avalonia.Controls.Grid.SetColumn(_gridControl, 1);
        body.Children.Add(_gridControl);
        _bodyScroll = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // Ruler row (corner + ruler) pinned above; velocity row pinned below.
        var rulerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*"), Height = RulerH };
        var corner = new Border { Background = KeysBg, BorderBrush = Divider, BorderThickness = new Thickness(0, 0, 1, 1) };
        rulerRow.Children.Add(corner);
        Avalonia.Controls.Grid.SetColumn(_ruler, 1);
        rulerRow.Children.Add(_ruler);

        var velRow = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*"), Height = VelH };
        var velLabelHost = new Border { Background = KeysBg, BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 1, 0) };
        velLabelHost.Child = new TextBlock { Text = "VEL", FontSize = 8, Foreground = LabelText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        velRow.Children.Add(velLabelHost);
        Avalonia.Controls.Grid.SetColumn(_vel, 1);
        velRow.Children.Add(_vel);

        // Horizontal zoom scrollbar pinned below the velocity lane, aligned under the
        // grid (56px keys gutter on the left).
        var scrollRow = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*") };
        _hScroll = new ScrollBar { Orientation = Orientation.Horizontal, Height = 10, IsVisible = false, Minimum = 0 };
        _hScroll.Scroll += (_, _) => SetScrollBeats(_hScroll.Value);
        Avalonia.Controls.Grid.SetColumn(_hScroll, 1);
        scrollRow.Children.Add(_hScroll);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        root.Children.Add(rulerRow);
        Avalonia.Controls.Grid.SetRow(_bodyScroll, 1);
        root.Children.Add(_bodyScroll);
        Avalonia.Controls.Grid.SetRow(velRow, 2);
        root.Children.Add(velRow);
        Avalonia.Controls.Grid.SetRow(scrollRow, 3);
        root.Children.Add(scrollRow);
        Content = root;

        // Persist vertical scroll per clip; apply a pending restore once the body is laid out.
        _bodyScroll.ScrollChanged += (_, _) => { if (!_restoringView) SaveView(); };
        _bodyScroll.LayoutUpdated += (_, _) => ApplyPendingVOffset();

        // Poll the pressed key(s) ~40 Hz while the editor is on screen.
        AttachedToVisualTree += (_, _) =>
        {
            if (_heldTimer is null)
            {
                _heldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
                _heldTimer.Tick += (_, _) => PollHeld();
            }
            _heldTimer.Start();
        };
        DetachedFromVisualTree += (_, _) => { _heldTimer?.Stop(); if (_held.Count > 0) { _held.Clear(); _keys.InvalidateVisual(); _gridControl.InvalidateVisual(); } };
    }

    private void PollHeld()
    {
        if (PollHeldNotes is null) return;
        int n = PollHeldNotes(_heldBuf);
        bool changed = n != _held.Count;
        if (!changed) { for (int i = 0; i < n; i++) if (!_held.Contains(_heldBuf[i])) { changed = true; break; } }
        if (!changed) return;
        _held.Clear();
        for (int i = 0; i < n; i++) _held.Add(_heldBuf[i]);
        _keys.InvalidateVisual();
        _gridControl.InvalidateVisual();
    }

    private void SaveView()
    {
        if (_clipKey.Item1 == int.MinValue || _pixelsPerBeat <= 0) return;
        s_view[_clipKey] = new ViewState(_pixelsPerBeat, _scrollBeats, _bodyScroll.Offset.Y, true);
    }

    private void ApplyPendingVOffset()
    {
        if (_pendingVOffset is null && !_pendingCenter) return;
        if (_bodyScroll.Viewport.Height <= 0) return;   // not laid out yet; LayoutUpdated retries
        double vy = _pendingCenter ? CenterOffsetForNotes() : _pendingVOffset!.Value;
        _pendingVOffset = null; _pendingCenter = false;
        double maxY = Math.Max(0, _bodyScroll.Extent.Height - _bodyScroll.Viewport.Height);
        _restoringView = true;
        _bodyScroll.Offset = new Vector(_bodyScroll.Offset.X, Math.Clamp(vy, 0, maxY));
        _restoringView = false;
    }

    public double LengthBeats => _lengthBeats;
    public double Grid { get => _grid; set { _grid = Math.Max(1.0 / 32, value); Invalidate(); } }

    public void SetTrackColor(Color c) { _trackColor = c; Invalidate(); }

    public void SetNotes(IEnumerable<NotaNote> notes, double lengthBeats)
    {
        _notes.Clear();
        _notes.AddRange(notes);
        _lengthBeats = Math.Max(1, lengthBeats);
        _selection.Clear();
        _keys.Height = RangeH;
        _gridControl.Height = RangeH;

        // Restore this clip's saved scroll/zoom if we've edited it before; otherwise fit the
        // length horizontally and centre vertically on the clip's own notes.
        if (s_view.TryGetValue(_clipKey, out var vs) && vs.HasV)
        {
            _pixelsPerBeat = Math.Clamp(vs.Ppb, MinPpb, MaxPpb);
            _scrollBeats = Math.Max(0, vs.Scroll);
            _pendingVOffset = vs.VOffset; _pendingCenter = false;
        }
        else
        {
            _pixelsPerBeat = 0;   // auto-fit to length on next layout
            _scrollBeats = 0;
            _pendingVOffset = null; _pendingCenter = true;
        }
        Invalidate();
        Changed?.Invoke();
        ApplyPendingVOffset();   // apply now if laid out; LayoutUpdated retries otherwise
    }

    // Vertical offset that centres the clip's note range (falls back to ~C5 for an empty clip).
    private double CenterOffsetForNotes()
    {
        double vh = _bodyScroll.Viewport.Height;
        if (vh <= 0) vh = 12 * RowH;
        if (_notes.Count > 0)
        {
            int minP = int.MaxValue, maxP = int.MinValue;
            foreach (var n in _notes) { if (n.Pitch < minP) minP = n.Pitch; if (n.Pitch > maxP) maxP = n.Pitch; }
            double mid = (minP + maxP) * 0.5;
            return Math.Max(0, (MaxPitch - mid) * RowH - vh * 0.5);
        }
        return Math.Max(0, (MaxPitch - 72) * RowH);   // C5 near the top
    }

    public string SelectionInfo
    {
        get
        {
            if (_selection.Count == 0) return "No selection";
            if (_selection.Count == 1)
            {
                int i = _selection.First();
                if (i >= 0 && i < _notes.Count)
                {
                    var n = _notes[i];
                    return $"1 note · {NoteName(n.Pitch)} · vel {(int)Math.Round(n.Velocity * 127)}";
                }
            }
            return $"{_selection.Count} notes";
        }
    }

    /// <summary>True when the interactive note grid holds keyboard focus (so Cmd+D
    /// duplicates notes rather than the arrangement clip).</summary>
    public bool GridFocused => _gridControl.IsFocused;

    /// <summary>Duplicates the selected notes, shifted right by the selection span, and
    /// selects the copies. Returns false if nothing was selected. Undoable (one Commit).</summary>
    public bool DuplicateSelection()
    {
        var sel = _selection.Where(i => i >= 0 && i < _notes.Count).ToList();
        if (sel.Count == 0) return false;
        double minStart = double.MaxValue, maxEnd = double.MinValue;
        foreach (int i in sel) { var n = _notes[i]; minStart = Math.Min(minStart, n.StartBeat); maxEnd = Math.Max(maxEnd, n.StartBeat + n.LengthBeats); }
        double shift = Math.Max(_grid, maxEnd - minStart);
        double cap = Math.Max(0, _lengthBeats - _grid);
        var copies = new List<NotaNote>(sel.Count);
        foreach (int i in sel) { var n = _notes[i]; n.StartBeat = Math.Min(n.StartBeat + shift, cap); copies.Add(n); }
        int baseIdx = _notes.Count;
        _notes.AddRange(copies);
        _selection.Clear();
        for (int k = 0; k < copies.Count; k++) _selection.Add(baseIdx + k);
        Invalidate();
        Commit?.Invoke(_notes);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes the selected notes. Undoable (one Commit).</summary>
    public bool DeleteSelection()
    {
        if (_selection.Count == 0) return false;
        foreach (int i in _selection.OrderByDescending(x => x))
            if (i >= 0 && i < _notes.Count) _notes.RemoveAt(i);
        _selection.Clear();
        Invalidate();
        Commit?.Invoke(_notes);
        Changed?.Invoke();
        return true;
    }

    public void Quantize(double strength)
    {
        for (int i = 0; i < _notes.Count; i++)
        {
            var n = _notes[i];
            double q = Math.Round(n.StartBeat / _grid) * _grid;
            n.StartBeat += (q - n.StartBeat) * Math.Clamp(strength, 0, 1);
            _notes[i] = n;
        }
        Invalidate();
        Commit?.Invoke(_notes);
        Changed?.Invoke();
    }

    public void TransposeBy(int semis)
    {
        for (int i = 0; i < _notes.Count; i++)
        {
            var n = _notes[i];
            n.Pitch = Math.Clamp(n.Pitch + semis, 0, 127);
            _notes[i] = n;
        }
        Invalidate();
        Commit?.Invoke(_notes);
        Changed?.Invoke();
    }

    /// <summary>Live playback cursor in clip-local beats; playing=false or an out-of-range
    /// beat hides it. Cheap: repaints only the grid + ruler, not the whole roll.</summary>
    public void SetPlayhead(double clipLocalBeat, bool playing)
    {
        double b = (playing && clipLocalBeat >= 0 && clipLocalBeat <= _lengthBeats) ? clipLocalBeat : -1;
        bool wasHidden = _playheadBeat < 0;
        if (Math.Abs(b - _playheadBeat) < 1e-6 && (b < 0) == wasHidden) return;
        _playheadBeat = b;
        _gridControl.InvalidateVisual();
        _ruler.InvalidateVisual();
    }

    /// <summary>Copies the selected notes to the shared clipboard (normalized to beat 0).</summary>
    public bool CopySelection()
    {
        var sel = _selection.Where(i => i >= 0 && i < _notes.Count).Select(i => _notes[i]).ToList();
        if (sel.Count == 0) return false;
        double min = sel.Min(n => n.StartBeat);
        s_clipboard.Clear();
        s_clipAnchor = min;
        foreach (var n in sel) { var c = n; c.StartBeat -= min; s_clipboard.Add(c); }
        return true;
    }

    /// <summary>Copies then deletes the selection. Undoable.</summary>
    public bool CutSelection() => CopySelection() && DeleteSelection();

    /// <summary>Pastes the clipboard — anchored at the playback cursor when visible, else at
    /// the copied notes' original position — and selects the pasted notes. Undoable.</summary>
    public bool PasteClipboard()
    {
        if (s_clipboard.Count == 0) return false;
        double anchor = _playheadBeat >= 0 ? Math.Round(_playheadBeat / _grid) * _grid : s_clipAnchor;
        int baseIdx = _notes.Count;
        foreach (var c in s_clipboard)
        {
            var n = c;
            n.StartBeat = anchor + c.StartBeat;
            if (n.StartBeat >= _lengthBeats) continue;                    // past the clip end → skip
            if (n.StartBeat + n.LengthBeats > _lengthBeats)               // trim a note that overruns
                n.LengthBeats = Math.Max(_grid, _lengthBeats - n.StartBeat);
            _notes.Add(n);
        }
        if (_notes.Count == baseIdx) return false;
        _selection.Clear();
        for (int k = baseIdx; k < _notes.Count; k++) _selection.Add(k);
        Invalidate(); Commit?.Invoke(_notes); Changed?.Invoke();
        return true;
    }

    /// <summary>Nudges the selection by dPitch semitones and/or dBeat beats, clamped to the
    /// pitch range and clip bounds (moves as a block, stopping at the edge). Undoable.
    /// Scrolls vertically to keep pitch-moved notes in view.</summary>
    public bool MoveSelection(int dPitch, double dBeat)
    {
        var sel = _selection.Where(i => i >= 0 && i < _notes.Count).ToList();
        if (sel.Count == 0) return false;
        double minStart = double.MaxValue, maxEnd = double.MinValue;
        int minP = 128, maxP = -1;
        foreach (int i in sel)
        {
            var n = _notes[i];
            minStart = Math.Min(minStart, n.StartBeat);
            maxEnd = Math.Max(maxEnd, n.StartBeat + n.LengthBeats);
            minP = Math.Min(minP, n.Pitch);
            maxP = Math.Max(maxP, n.Pitch);
        }
        double db = dBeat;
        if (minStart + db < 0) db = -minStart;
        if (maxEnd + db > _lengthBeats) db = _lengthBeats - maxEnd;
        int dp = dPitch;
        if (minP + dp < MinPitch) dp = MinPitch - minP;
        if (maxP + dp > MaxPitch) dp = MaxPitch - maxP;
        if (Math.Abs(db) < 1e-9 && dp == 0) return false;
        foreach (int i in sel) { var n = _notes[i]; n.StartBeat += db; n.Pitch += dp; _notes[i] = n; }
        Invalidate(); Commit?.Invoke(_notes); Changed?.Invoke();
        if (dp != 0) EnsurePitchVisible(dp > 0 ? maxP + dp : minP + dp);
        return true;
    }

    // Scroll the body so `pitch`'s row is within the viewport (keeps arrow-moved notes visible).
    private void EnsurePitchVisible(int pitch)
    {
        var sv = _bodyScroll;
        double viewH = sv.Viewport.Height;
        if (viewH <= 0) return;
        double y = (MaxPitch - pitch) * RowH, top = sv.Offset.Y, margin = RowH;
        if (y < top + margin) sv.Offset = new Vector(sv.Offset.X, Math.Max(0, y - margin));
        else if (y + RowH > top + viewH - margin) sv.Offset = new Vector(sv.Offset.X, y + RowH - viewH + margin);
    }

    private void Invalidate()
    {
        _ruler.InvalidateVisual();
        _keys.InvalidateVisual();
        _gridControl.InvalidateVisual();
        _vel.InvalidateVisual();
    }

    private static bool IsBlack(int pitch) { int n = ((pitch % 12) + 12) % 12; return n is 1 or 3 or 6 or 8 or 10; }
    private static string NoteName(int pitch)
    {
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        return names[((pitch % 12) + 12) % 12] + (pitch / 12 - 1);
    }

    // --- adaptive grid (subdivides as you zoom in) ------------------------
    private static readonly double[] GridLadder = { 0.0625, 0.125, 0.25, 0.5, 1, 2, 4 };

    // Finest grid step (beats) keeping ≥ ~9px between lines: bars → beats → 1/8 →
    // 1/16 → 1/32 → 1/64 as the zoom increases.
    internal double GridStepBeats()
    {
        double ppb = _pixelsPerBeat;
        if (ppb <= 0) return 1.0;
        foreach (var s in GridLadder) if (s * ppb >= 9) return s;
        return 4.0;
    }

    // Coarser step for ruler labels (needs room for text); never finer than a 1/16.
    internal double LabelStepBeats()
    {
        double ppb = _pixelsPerBeat;
        if (ppb <= 0) return 1.0;
        double[] steps = { 0.25, 0.5, 1, 2, 4 };
        foreach (var s in steps) if (s * ppb >= 36) return s;
        return 4.0;
    }

    // Line weight for a vertical grid line at beat b: bar > beat > sub-beat.
    internal IPen GridPen(double b)
    {
        if (Math.Abs(b / 4 - Math.Round(b / 4)) < 1e-6) return BarPen;
        if (Math.Abs(b - Math.Round(b)) < 1e-6) return BeatPen;
        return SubBeatPen;
    }

    // Ruler label at beat b: "bar.beat", plus a 1/16 index when mid-beat (e.g. 3.2.3).
    internal static string GridLabel(double b)
    {
        int bar = (int)Math.Floor(b / 4) + 1;
        double inBar = b - Math.Floor(b / 4) * 4;
        int beat = (int)Math.Floor(inBar) + 1;
        double frac = inBar - Math.Floor(inBar);
        if (frac < 1e-6) return $"{bar}.{beat}";
        return $"{bar}.{beat}.{(int)Math.Round(frac * 4) + 1}";
    }

    // ---- ruler -----------------------------------------------------------
    private sealed class RulerLane : Control
    {
        private readonly PianoRollView _o;
        public RulerLane(PianoRollView o) { _o = o; }
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0) return;
            ctx.FillRectangle(KeysBg, new Rect(0, 0, w, h));
            _o.EnsurePpb(w);
            double ppb = _o.PixelsPerBeat;
            double viewEnd = Math.Min(_o._lengthBeats, _o.ScrollBeats + w / Math.Max(1e-6, ppb));
            // Grid lines subdivide with zoom; labels use a coarser step with room for text.
            double step = _o.GridStepBeats();
            double startB = Math.Max(0, Math.Floor(_o.ScrollBeats / step) * step);
            for (int k = 0; ; k++)
            {
                double b = startB + k * step;
                if (b > viewEnd + 1e-9) break;
                double x = _o.BeatToX(b);
                ctx.DrawLine(_o.GridPen(b), new Point(x, 0), new Point(x, h));
            }
            double lstep = _o.LabelStepBeats();
            double startL = Math.Max(0, Math.Ceiling(_o.ScrollBeats / lstep - 1e-9) * lstep);
            for (int k = 0; ; k++)
            {
                double b = startL + k * lstep;
                if (b > viewEnd + 1e-9) break;
                var ft = new FormattedText(GridLabel(b), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 9, LabelText);
                ctx.DrawText(ft, new Point(_o.BeatToX(b) + 3, 4));
            }
            if (_o._playheadBeat >= 0)
            {
                double px = _o.BeatToX(_o._playheadBeat);
                if (px >= 0 && px <= w) ctx.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, h));
            }
        }
    }

    // ---- keys column -----------------------------------------------------
    private sealed class KeysColumn : Control
    {
        private readonly PianoRollView _o;
        private int _hover = -1;
        public KeysColumn(PianoRollView o) { _o = o; }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            int p = (int)Math.Clamp(MaxPitch - Math.Floor(e.GetPosition(this).Y / RowH), MinPitch, MaxPitch);
            if (p != _hover) { _hover = p; InvalidateVisual(); }
        }
        protected override void OnPointerExited(PointerEventArgs e)
        {
            if (_hover != -1) { _hover = -1; InvalidateVisual(); }
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width;
            ctx.FillRectangle(KeysBg, new Rect(0, 0, w, Bounds.Height));
            for (int p = MinPitch; p <= MaxPitch; p++)
            {
                double y = (MaxPitch - p) * RowH;
                ctx.FillRectangle(IsBlack(p) ? KeyBlack : KeyWhite, new Rect(0, y, w, RowH - 1));
                if (_o._scaleOn)
                {
                    if (_o.IsRoot(p)) ctx.FillRectangle(RootTint, new Rect(0, y, w, RowH - 1));
                    else if (!_o.InScale(p)) ctx.FillRectangle(OutScaleWash, new Rect(0, y, w, RowH - 1));
                }
                if (p % 12 == 0)
                {
                    var ft = new FormattedText(NoteName(p), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, KeyLabel);
                    ctx.DrawText(ft, new Point(w - ft.Width - 4, y + 2));
                }
            }
            // Currently-pressed keys (computer keyboard / MIDI): light the key solid + label it.
            if (_o._held.Count > 0)
                foreach (int hp in _o._held)
                {
                    if (hp < MinPitch || hp > MaxPitch) continue;
                    double y = (MaxPitch - hp) * RowH;
                    ctx.FillRectangle(HeldKeyFill, new Rect(0, y, w, RowH - 1));
                    var ft = new FormattedText(NoteName(hp), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 9, KeyLabel);
                    ctx.DrawText(ft, new Point(w - ft.Width - 4, y + 1));
                }
            // Hovered key: highlight it and label its note name (works for black keys too,
            // where the octave-C label wouldn't otherwise show).
            if (_hover >= MinPitch)
            {
                double hy = (MaxPitch - _hover) * RowH;
                ctx.FillRectangle(KeyHover, new Rect(0, hy, w, RowH - 1));
                var ft = new FormattedText(NoteName(_hover), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 9, KeyLabel);
                ctx.DrawText(ft, new Point(w - ft.Width - 4, hy + 1));
            }
        }
    }

    // ---- note grid (interactive) -----------------------------------------
    private sealed class NoteGrid : Control
    {
        private readonly PianoRollView _o;
        private enum Mode { None, Move, Resize, Marquee }
        private Mode _drag = Mode.None;
        private bool _dragMoved;

        // Move/resize: each selected note's state at grab time + the primary grabbed.
        private readonly Dictionary<int, (double start, int pitch, double len)> _orig = new();
        private int _primary = -1;
        private double _grabBeat;       // pointer-beat minus primary start, at grab (Move)
        private double _resizeAnchor;   // snapped pointer-beat at grab (Resize)

        // Marquee (grid coords) + the selection to union with when additive.
        private Point _marqueeA, _marqueeB;
        private bool _marqueeAdd;
        private readonly HashSet<int> _selBase = new();

        // Hover affordance (mirrors the arrangement grid): resize cursor on the trim edge, a
        // move/four-arrows cursor over a note body, plain arrow on empty space.
        private int _hoverNote = -1;
        private int _cursorKind;   // 0 arrow, 1 resize, 2 move
        private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
        private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
        private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
        private const double EdgePx = 6;

        // Live drag (feature): the pre-drag note list (to seed one undo entry) + whether the
        // live stream has started this gesture.
        private readonly List<NotaNote> _preDrag = new();
        private bool _liveStarted;

        // Vertical auto-scroll while a drag runs past the viewport edges.
        private DispatcherTimer? _autoScroll;
        private double _autoVelY;
        private Point _lastGrid;

        public NoteGrid(PianoRollView o) { _o = o; Focusable = true; }

        private double Ppb => _o.PixelsPerBeat;
        private double BeatToX(double b) => _o.BeatToX(b);
        private double XToBeat(double x) => _o.XToBeat(x);
        private static double PitchToY(int p) => (MaxPitch - p) * RowH;
        private int YToPitch(double y) => (int)Math.Clamp(MaxPitch - Math.Floor(y / RowH), MinPitch, MaxPitch);
        private double Snap(double b) => Math.Round(b / _o._grid) * _o._grid;

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0) return;
            _o.EnsurePpb(w);
            ctx.FillRectangle(Bg, new Rect(0, 0, w, h));
            for (int p = MinPitch; p <= MaxPitch; p++)
            {
                double y = PitchToY(p);
                if (IsBlack(p)) ctx.FillRectangle(RowBlackTint, new Rect(0, y, w, RowH));
                if (_o._scaleOn)
                {
                    if (_o.IsRoot(p)) ctx.FillRectangle(RootTint, new Rect(0, y, w, RowH));
                    else if (_o.InScale(p)) ctx.FillRectangle(InScaleTint, new Rect(0, y, w, RowH));
                    else ctx.FillRectangle(OutScaleWash, new Rect(0, y, w, RowH));
                }
                ctx.DrawLine(RowLine, new Point(0, y + RowH), new Point(w, y + RowH));
            }
            // Played-key rows: a faint wash across the grid so you see where the pressed note sits.
            if (_o._held.Count > 0)
                foreach (int hp in _o._held)
                    if (hp >= MinPitch && hp <= MaxPitch)
                        ctx.FillRectangle(HeldRowTint, new Rect(0, PitchToY(hp), w, RowH));
            double ppb = Ppb;
            double step = _o.GridStepBeats();
            double viewEnd = Math.Min(_o._lengthBeats, _o.ScrollBeats + w / Math.Max(1e-6, ppb));
            double startB = Math.Max(0, Math.Floor(_o.ScrollBeats / step) * step);
            for (int k = 0; ; k++)
            {
                double b = startB + k * step;
                if (b > viewEnd + 1e-9) break;
                double x = BeatToX(b);
                ctx.DrawLine(_o.GridPen(b), new Point(x, 0), new Point(x, h));
            }
            var notes = _o._notes;
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n.Pitch < MinPitch || n.Pitch > MaxPitch) continue;
                double x = BeatToX(n.StartBeat), y = PitchToY(n.Pitch);
                double nw = Math.Max(3, n.LengthBeats * ppb);
                bool sel = _o._selection.Contains(i);
                var c = _o._trackColor;
                IBrush fill;
                if (sel) fill = AccentBright;
                else
                {
                    double a = 0.35 + 0.60 * Math.Clamp(n.Velocity, 0, 1);
                    if (!_o.InScale(n.Pitch)) a *= 0.4;   // out-of-scale notes read faint
                    fill = new SolidColorBrush(Color.FromArgb((byte)(255 * a), c.R, c.G, c.B));
                }
                ctx.FillRectangle(fill, new Rect(x, y + 1, nw, RowH - 2), 2);
                // Resize-edge affordance: a bright bar on the edge a drag would trim.
                bool edgeHi = (_drag == Mode.Resize && sel) || (_drag == Mode.None && _hoverNote == i);
                if (edgeHi) ctx.FillRectangle(EdgeHighlight, new Rect(x + nw - 2.5, y + 1, 2.5, RowH - 2));
            }
            if (_o._playheadBeat >= 0)
            {
                double px = BeatToX(_o._playheadBeat);
                if (px >= 0 && px <= w) ctx.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, h));
            }
            if (_drag == Mode.Marquee)
            {
                var r = RectOf(_marqueeA, _marqueeB);
                ctx.FillRectangle(MarqueeFill, r);
                ctx.DrawRectangle(null, MarqueePen, r);
            }
        }

        private static Rect RectOf(Point a, Point b)
            => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        private int HitTest(Point p, out bool edge)
        {
            edge = false;
            var notes = _o._notes;
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                var n = notes[i];
                if (n.Pitch < MinPitch || n.Pitch > MaxPitch) continue;
                double x = BeatToX(n.StartBeat), y = PitchToY(n.Pitch), nw = Math.Max(3, n.LengthBeats * Ppb);
                if (new Rect(x, y, nw, RowH).Contains(p)) { edge = p.X >= x + nw - EdgePx; return i; }
            }
            return -1;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            Focus();
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            int hit = HitTest(p, out bool edge);

            // Double-click: add a note on empty space, delete one under the cursor.
            if (e.ClickCount == 2)
            {
                if (hit >= 0) { _o._notes.RemoveAt(hit); _o._selection.Clear(); }
                else
                {
                    var nt = new NotaNote(YToPitch(p.Y), Math.Max(0, Snap(XToBeat(p.X))), _o._grid * 4, 0.8f);
                    _o._notes.Add(nt);
                    _o._selection.Clear(); _o._selection.Add(_o._notes.Count - 1);
                }
                _o.Invalidate(); _o.Commit?.Invoke(_o._notes); _o.Changed?.Invoke();
                e.Handled = true;
                return;
            }

            if (hit >= 0)
            {
                if (shift)   // toggle membership, no drag
                {
                    if (!_o._selection.Add(hit)) _o._selection.Remove(hit);
                    _o.Invalidate(); _o.Changed?.Invoke();
                    e.Handled = true;
                    return;
                }
                if (!_o._selection.Contains(hit)) { _o._selection.Clear(); _o._selection.Add(hit); }
                e.Pointer.Capture(this);
                _primary = hit;
                _dragMoved = false;
                _orig.Clear();
                foreach (int i in _o._selection)
                    if (i >= 0 && i < _o._notes.Count) { var nn = _o._notes[i]; _orig[i] = (nn.StartBeat, nn.Pitch, nn.LengthBeats); }
                _preDrag.Clear(); _preDrag.AddRange(_o._notes); _liveStarted = false;
                if (edge) { _drag = Mode.Resize; _resizeAnchor = Snap(XToBeat(p.X)); Cursor = ResizeCursor; _cursorKind = 1; }
                else { _drag = Mode.Move; _grabBeat = XToBeat(p.X) - _o._notes[hit].StartBeat; Cursor = MoveCursor; _cursorKind = 2; }
                _lastGrid = p;
                _o.Invalidate(); _o.Changed?.Invoke();
            }
            else   // marquee on empty space
            {
                _selBase.Clear();
                if (shift) _selBase.UnionWith(_o._selection); else _o._selection.Clear();
                _marqueeAdd = shift;
                _drag = Mode.Marquee; _marqueeA = _marqueeB = p; _lastGrid = p;
                e.Pointer.Capture(this);
                _o.Invalidate(); _o.Changed?.Invoke();
            }
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag == Mode.None) { UpdateHover(p); return; }
            _lastGrid = p;
            DragUpdate(p);
            EvaluateAutoScroll(p);
        }

        // Apply the current drag using a grid-space point (also re-used by auto-scroll).
        private void DragUpdate(Point p)
        {
            if (_drag == Mode.Move)
            {
                double targetStart = Snap(XToBeat(p.X) - _grabBeat);
                var pr = _orig[_primary];
                double dBeat = targetStart - pr.start;
                int dPitch = YToPitch(p.Y) - pr.pitch;
                // Clamp the shared delta so the whole group stays in-bounds (keeps shape).
                double minStart = double.MaxValue; int minP = int.MaxValue, maxP = int.MinValue;
                foreach (var v in _orig.Values) { minStart = Math.Min(minStart, v.start); minP = Math.Min(minP, v.pitch); maxP = Math.Max(maxP, v.pitch); }
                double cap = Math.Max(0, _o._lengthBeats - _o._grid);
                dBeat = Math.Clamp(dBeat, -minStart, Math.Max(0, cap - MaxStart()));
                dPitch = Math.Clamp(dPitch, MinPitch - minP, MaxPitch - maxP);
                foreach (var kv in _orig)
                {
                    var n = _o._notes[kv.Key];
                    n.StartBeat = kv.Value.start + dBeat;
                    n.Pitch = kv.Value.pitch + dPitch;
                    _o._notes[kv.Key] = n;
                }
                _dragMoved = true;
                PushLive();
            }
            else if (_drag == Mode.Resize)
            {
                double dLen = Snap(XToBeat(p.X)) - _resizeAnchor;
                foreach (var kv in _orig)
                {
                    var n = _o._notes[kv.Key];
                    n.LengthBeats = Math.Max(_o._grid, kv.Value.len + dLen);
                    _o._notes[kv.Key] = n;
                }
                _dragMoved = true;
                PushLive();
            }
            else if (_drag == Mode.Marquee)
            {
                _marqueeB = p;
                RecomputeMarquee();
            }
            _o.Invalidate();
            _o.Changed?.Invoke();
        }

        // Stream the in-progress drag to the engine (no undo) so playback follows the note
        // instantly. On the first movement we seed ONE undo entry with the pre-drag state via
        // the normal Commit; every subsequent frame uses the no-undo live path.
        private void PushLive()
        {
            if (_o.CommitLive is null) return;   // no live path wired (e.g. session) → commit on release
            if (!_liveStarted) { _liveStarted = true; _o.Commit?.Invoke(_preDrag); }
            _o.CommitLive(_o._notes);
        }

        private double MaxStart() { double m = double.MinValue; foreach (var v in _orig.Values) m = Math.Max(m, v.start); return m; }

        private void RecomputeMarquee()
        {
            var r = RectOf(_marqueeA, _marqueeB);
            _o._selection.Clear();
            if (_marqueeAdd) _o._selection.UnionWith(_selBase);
            for (int i = 0; i < _o._notes.Count; i++)
            {
                var n = _o._notes[i];
                double x = BeatToX(n.StartBeat), y = PitchToY(n.Pitch), nw = Math.Max(3, n.LengthBeats * Ppb);
                if (r.Intersects(new Rect(x, y, nw, RowH))) _o._selection.Add(i);
            }
        }

        private void UpdateHover(Point p)
        {
            int hit = HitTest(p, out bool edge);
            int kind = hit < 0 ? 0 : edge ? 1 : 2;   // 0 arrow, 1 resize edge, 2 move body
            if (kind != _cursorKind) { _cursorKind = kind; Cursor = kind == 1 ? ResizeCursor : kind == 2 ? MoveCursor : ArrowCursor; }
            int hn = kind == 1 ? hit : -1;   // the bright trim-edge affordance shows only on the edge
            if (hn != _hoverNote) { _hoverNote = hn; _o.Invalidate(); }
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            if (_cursorKind != 0) { _cursorKind = 0; Cursor = ArrowCursor; }
            if (_hoverNote != -1) { _hoverNote = -1; _o.Invalidate(); }
            base.OnPointerExited(e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            StopAutoScroll();
            if (_drag == Mode.None) return;
            var was = _drag;
            _drag = Mode.None;
            e.Pointer.Capture(null);
            _orig.Clear();
            if (was != Mode.Marquee && _dragMoved)
            {
                // Live path: undo was seeded on the first move; push the final state (no undo).
                // No live path (session): fall back to a single committing push.
                if (_o.CommitLive is not null && _liveStarted) _o.CommitLive(_o._notes);
                else _o.Commit?.Invoke(_o._notes);
            }
            _liveStarted = false;
            _cursorKind = 0; Cursor = ArrowCursor;   // re-evaluated on the next hover
            _o.Invalidate();
            _o.Changed?.Invoke();
        }

        // ⌘/Ctrl+wheel zooms toward the cursor; Shift+wheel scrolls horizontally;
        // plain wheel is left to the body ScrollViewer for vertical key navigation.
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            var mods = e.KeyModifiers;
            if ((mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            {
                _o.ZoomAt(WheelInput.ZoomFactor(e.Delta.Y, 1.2), XToBeat(e.GetPosition(this).X));
                e.Handled = true;
            }
            else if ((mods & KeyModifiers.Shift) != 0)
            {
                _o.ScrollByBeats(-WheelInput.Pixels(e.Delta.Y) / _o.PixelsPerBeat);
                e.Handled = true;
            }
            else if (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
            {
                // Horizontal trackpad swipe → scroll the grid; plain vertical is left to the
                // body ScrollViewer for key navigation.
                _o.ScrollByBeats(-WheelInput.Pixels(e.Delta.X) / _o.PixelsPerBeat);
                e.Handled = true;
            }
        }

        // --- vertical auto-scroll: keep dragging notes past the viewport edges ---
        private void EvaluateAutoScroll(Point gridP)
        {
            var sv = _o._bodyScroll;
            double top = sv.Offset.Y, vh = sv.Viewport.Height;
            if (vh <= 0) { StopAutoScroll(); return; }
            double yInView = gridP.Y - top;
            const double margin = 28, speed = 14;
            double vel = 0;
            if (yInView < margin) vel = -speed * (1 - Math.Clamp(yInView, 0, margin) / margin);
            else if (yInView > vh - margin) vel = speed * (1 - Math.Clamp(vh - yInView, 0, margin) / margin);
            _autoVelY = vel;
            if (Math.Abs(vel) < 0.01) StopAutoScroll(); else StartAutoScroll();
        }

        private void StartAutoScroll()
        {
            if (_autoScroll != null) return;
            _autoScroll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _autoScroll.Tick += (_, _) =>
            {
                var sv = _o._bodyScroll;
                double maxY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
                double newY = Math.Clamp(sv.Offset.Y + _autoVelY, 0, maxY);
                double dy = newY - sv.Offset.Y;
                if (Math.Abs(dy) < 0.01) { StopAutoScroll(); return; }
                sv.Offset = new Vector(sv.Offset.X, newY);
                _lastGrid = new Point(_lastGrid.X, _lastGrid.Y + dy);   // same screen point, new grid-y
                DragUpdate(_lastGrid);
                EvaluateAutoScroll(_lastGrid);
            };
            _autoScroll.Start();
        }

        private void StopAutoScroll() { _autoScroll?.Stop(); _autoScroll = null; _autoVelY = 0; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool meta = (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0;
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            if (!meta && (e.Key == Key.Delete || e.Key == Key.Back)) { if (_o.DeleteSelection()) e.Handled = true; return; }
            if (!meta && e.Key == Key.Escape && _o._selection.Count > 0)
            {
                _o._selection.Clear(); _o.Invalidate(); _o.Changed?.Invoke(); e.Handled = true; return;
            }
            // Arrow keys nudge the selection: ←/→ by the grid in time, ↑/↓ by a semitone
            // (Shift = an octave). The move auto-scrolls to keep pitch-shifted notes in view.
            if (!meta && _o._selection.Count > 0)
            {
                switch (e.Key)
                {
                    case Key.Left:  if (_o.MoveSelection(0, -_o._grid)) e.Handled = true; return;
                    case Key.Right: if (_o.MoveSelection(0, +_o._grid)) e.Handled = true; return;
                    case Key.Up:    if (_o.MoveSelection(shift ? 12 : 1, 0)) e.Handled = true; return;
                    case Key.Down:  if (_o.MoveSelection(shift ? -12 : -1, 0)) e.Handled = true; return;
                }
            }
            if (meta && e.Key == Key.A)
            {
                _o._selection.Clear();
                for (int i = 0; i < _o._notes.Count; i++) _o._selection.Add(i);
                _o.Invalidate(); _o.Changed?.Invoke(); e.Handled = true; return;
            }
            if (meta && e.Key == Key.D)   // cross-platform fallback (macOS uses the native menu)
            {
                if (_o.DuplicateSelection()) e.Handled = true;
                return;
            }
            // Clipboard (the Edit-menu items carry no gesture, so these reach us here).
            if (meta && e.Key == Key.C) { if (_o.CopySelection()) e.Handled = true; return; }
            if (meta && e.Key == Key.X) { if (_o.CutSelection()) e.Handled = true; return; }
            if (meta && e.Key == Key.V) { if (_o.PasteClipboard()) e.Handled = true; return; }
            base.OnKeyDown(e);
        }
    }

    // ---- velocity lane (interactive) -------------------------------------
    private sealed class VelocityLane : Control
    {
        private readonly PianoRollView _o;
        private bool _drag;
        private int _velNote = -1;
        private int _hover = -1;
        public VelocityLane(PianoRollView o) { _o = o; }

        private double Ppb => _o.PixelsPerBeat;

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0) return;
            _o.EnsurePpb(w);
            ctx.FillRectangle(VelBg, new Rect(0, 0, w, h));
            double ppb = Ppb;
            double step = _o.GridStepBeats();
            double viewEnd = Math.Min(_o._lengthBeats, _o.ScrollBeats + w / Math.Max(1e-6, ppb));
            double startB = Math.Max(0, Math.Floor(_o.ScrollBeats / step) * step);
            for (int k = 0; ; k++)
            {
                double b = startB + k * step;
                if (b > viewEnd + 1e-9) break;
                ctx.DrawLine(_o.GridPen(b), new Point(_o.BeatToX(b), 0), new Point(_o.BeatToX(b), h));
            }
            var notes = _o._notes;
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                double x = _o.BeatToX(n.StartBeat);
                double bh = Math.Clamp(n.Velocity, 0, 1) * (h - 6);
                bool sel = _o._selection.Contains(i);
                bool hov = i == _hover;
                IBrush b = sel ? AccentBright : new SolidColorBrush(hov ? Brighten(_o._trackColor) : _o._trackColor);
                ctx.FillRectangle(b, new Rect(x, h - bh, 5, bh), 1);
                // Hover / selected: cap the top edge (the velocity drag handle).
                if (hov || sel) ctx.FillRectangle(EdgeHighlight, new Rect(x, h - bh - 1, 5, 2));
            }
        }

        private static Color Brighten(Color c)
            => Color.FromRgb((byte)Math.Min(255, c.R + 48), (byte)Math.Min(255, c.G + 48), (byte)Math.Min(255, c.B + 48));

        private int Nearest(double x)
        {
            int best = -1; double bd = 14;
            for (int i = 0; i < _o._notes.Count; i++)
            {
                double d = Math.Abs(_o.BeatToX(_o._notes[i].StartBeat) + 2.5 - x);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            int hit = Nearest(e.GetPosition(this).X);
            if (hit < 0) return;
            // Keep an existing multi-selection if the grabbed note is part of it;
            // otherwise this note becomes the selection.
            if (!_o._selection.Contains(hit)) { _o._selection.Clear(); _o._selection.Add(hit); }
            _velNote = hit; _drag = true;
            Apply(e.GetPosition(this).Y);
            _o.Invalidate(); _o.Changed?.Invoke();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_drag) { Apply(e.GetPosition(this).Y); _o.Invalidate(); _o.Changed?.Invoke(); return; }
            int hv = Nearest(e.GetPosition(this).X);           // hover highlight (no drag)
            if (hv != _hover) { _hover = hv; InvalidateVisual(); }
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            if (_hover != -1) { _hover = -1; InvalidateVisual(); }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_drag) { _drag = false; _o.Commit?.Invoke(_o._notes); _o.Changed?.Invoke(); }
        }

        private void Apply(double y)
        {
            if (_velNote < 0 || _velNote >= _o._notes.Count) return;
            var n = _o._notes[_velNote];
            n.Velocity = (float)Math.Clamp((Bounds.Height - y) / (Bounds.Height - 6), 0.02, 1.0);
            _o._notes[_velNote] = n;
        }
    }
}
