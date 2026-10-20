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
// Rows zoom vertically (⌘⌥+wheel, drag on the keys, ⌥+pinch, ⇧F = Fit to notes);
// the row height is per-clip view state like the horizontal zoom.
// With note audition on (the clip editor's speaker chip, #46) a note sounds on the track's
// instrument while it is placed, pressed or dragged; the keys column plays its keys (#47).

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
using Nota.Application.Editing;
using Nota.Application.Midi;

namespace Nota.App;

public sealed partial class PianoRollView : UserControl
{
    public Action<IReadOnlyList<NotaNote>>? Commit { get; set; }
    /// <summary>Live, no-undo push during a note drag so playback follows instantly. The grid
    /// seeds one undo entry (via <see cref="Commit"/>) at the gesture start, then streams here.</summary>
    public Action<IReadOnlyList<NotaNote>>? CommitLive { get; set; }
    /// <summary>Polls the engine for currently-pressed live-input pitches (keyboard + MIDI) into
    /// the buffer, returning the count — used to light up the played key. Set by the host.</summary>
    public Func<int[], int>? PollHeldNotes { get; set; }
    /// <summary>Moves the transport to a clip-local beat (#50): a click or drag on the ruler.
    /// Null (a session slot, which runs its own clock) leaves the ruler inert.</summary>
    public Action<double>? Seek { get; set; }
    /// <summary>Starts / ends a note on the clip's own track (note audition, #46; the playable
    /// keys, #47): pitch and velocity 0..1. Null leaves the roll silent. Set by the host.</summary>
    public Action<int, float>? AuditionOn { get; set; }
    public Action<int>? AuditionOff { get; set; }

    /// <summary>Note audition (#46): placing, clicking or dragging a note plays it on the
    /// track's instrument. App-wide like FL Studio's speaker toggle; on by default, seeded
    /// from and saved to Settings by the host. The keys column plays whatever this says.</summary>
    public static bool AuditionEnabled { get; set; } = true;
    /// <summary>Fired when a clip editor's speaker chip flips <see cref="AuditionEnabled"/>.</summary>
    public static event Action? AuditionToggled;
    public static void SetAuditionEnabled(bool on)
    {
        if (AuditionEnabled == on) return;
        AuditionEnabled = on;
        AuditionToggled?.Invoke();
    }

    // The one pitch sounding from the roll (−1 = none): a new audition ends the previous one,
    // so a drag across pitches re-triggers instead of stacking notes.
    private int _auditionPitch = -1;
    /// <summary>Sound <paramref name="pitch"/> until <see cref="StopAudition"/>. The note grid
    /// asks only while audition is on; the keys column always plays (force).</summary>
    internal void Audition(int pitch, float velocity, bool force = false, int slot = -1)
    {
        int track = TrackOfSlot(slot >= 0 ? slot : _set.Active);
        if (AuditionOn is null || (!force && !AuditionEnabled) || (pitch == _auditionPitch && track == _auditionTrack)) return;
        StopAudition();
        if (pitch < MinPitch || pitch > MaxPitch) return;
        _auditionPitch = pitch;
        _auditionTrack = track;
        AuditionOn(pitch, Math.Clamp(velocity, 0.05f, 1f));
        _keys.InvalidateVisual();
    }
    internal void StopAudition()
    {
        if (_auditionPitch < 0) return;
        int p = _auditionPitch;
        _auditionPitch = -1;
        AuditionOff?.Invoke(p);
        _auditionTrack = -1;
        _keys.InvalidateVisual();
    }

    /// <summary>Selects every note of <paramref name="pitch"/> (⌘/Ctrl-click on a key, #47);
    /// <paramref name="add"/> keeps the current selection.</summary>
    internal void SelectPitch(int pitch, bool add)
    {
        SettlePlacement();   // a pending placement lands first: it can cut or remove notes
        if (!add) _selection.Clear();
        for (int i = 0; i < _notes.Count; i++)
            if (_notes[i].Pitch == pitch && !IsLocked(i)) _selection.Add(i);
        Invalidate();
        Changed?.Invoke();
    }

    // The recording take shown in this roll (#56): its held notes grow on the grid and new
    // notes settle in. Null when the roll shows any other clip.
    private RecordPreview? _recPreview;
    private double _recClipStart;
    /// <summary>Show (or, with null, hide) the recording take's held notes; clipStart is the
    /// take clip's timeline start — it moves left while early notes land.</summary>
    internal void SetRecordTake(RecordPreview? preview, double clipStart)
    {
        if (preview is null && _recPreview is null) return;
        _recPreview = preview;
        _recClipStart = clipStart;
        _gridControl.InvalidateVisual();
    }
    /// <summary>A note's recorded MPE by <see cref="NotaNote.ExprId"/>, drawn over the note.
    /// Curves never change under an id, so they are fetched once. Set by the host.</summary>
    public Func<int, NotaExprPoint[]>? ExpressionOf { get; set; }
    public event Action? Changed;

    private readonly Dictionary<int, NotaExprPoint[]> _exprCache = new();
    private NotaExprPoint[] ExprOf(int id)
    {
        if (id <= 0 || ExpressionOf is null) return Array.Empty<NotaExprPoint>();
        if (!_exprCache.TryGetValue(id, out var pts)) _exprCache[id] = pts = ExpressionOf(id);
        return pts;
    }

    // Currently-pressed keys (from PollHeldNotes), for the played-key highlight.
    private readonly HashSet<int> _held = new();
    private readonly int[] _heldBuf = new int[16];
    private DispatcherTimer? _heldTimer;

    // --- per-clip view persistence (scroll/zoom don't reset when you re-enter a clip) ------
    private readonly record struct ViewState(double Ppb, double Scroll, double VOffset, bool HasV, double RowH);
    private static readonly Dictionary<(int, int), ViewState> s_view = new();
    private (int, int) _clipKey = (int.MinValue, int.MinValue);
    private double? _pendingVOffset;   // absolute vertical offset to apply once the body is laid out
    private bool _pendingCenter;       // instead: centre on the clip's notes once laid out
    private bool _restoringView;       // suppress view-saves while we programmatically restore
    private bool _saveAfterApply;      // a zoom/fit queued the offset: save it once applied

    /// <summary>Identifies the edited clip so its scroll/zoom/vertical position persist across
    /// re-opens. Call before <see cref="SetNotes"/>.</summary>
    public void SetClipIdentity(int a, int b) => _clipKey = (a, b);

    private const int MinPitch = 21, MaxPitch = 108;   // A0..C8 (88 keys)
    private const double KeysW = 48, RulerH = 24, VelH = 72;

    // Vertical zoom (specs/019-piano-roll-vertical-zoom): the row height is per-clip view
    // state, driven by ⌘⌥+wheel, a drag on the keys, ⌥+pinch and Fit to notes.
    private const double MinRowH = 6, DefaultRowH = 12, MaxRowH = 36;
    private double _rowH = DefaultRowH;
    internal double RowH => _rowH;

    // The notes on screen, in editor beats (specs/029): beats from the open clip set's origin —
    // the clip's own beats when one clip is open. _owner[i] is note i's clip (its ClipSet slot);
    // the two lists change together, only through AddNote / RemoveNoteAt / ReplaceAll.
    private readonly List<NotaNote> _notes = new();
    private readonly List<int> _owner = new();
    private ClipSet _set = ClipSet.Single(-1, -1, 0, 4);
    private readonly List<ISolidColorBrush> _slotBrushes = new();
    private double _lengthBeats = 4;   // the set's span: the scrollable range
    private readonly HashSet<int> _selection = new();   // indices into _notes
    private ISolidColorBrush _trackBrush = NotaPalette.TrackBrushes[3];   // read .Color at draw time: it follows the theme
    private Color _trackColor => _trackBrush.Color;

    // --- musical scale overlay ("Set Scale") ----------------
    // Session-global (static seed) so the chosen key/scale persists as you move
    // between clips, like a global scale setting. Off by default → chromatic, no
    // dimming. The key/scale tables are shared with the clip tools and the engine's
    // Nota Scale device (src/MidiScale.h) — see MidiScales.
    public static string[] KeyNames => MidiScales.KeyNames;
    public static string[] ScaleNames => MidiScales.Names;
    private static ushort[] ScaleMasks => MidiScales.Masks;

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

    // Note clipboard, shared across clips (copy here, paste into another clip): each note at
    // its beat in the clip it was copied from, with that clip (specs/029 pastes it back there).
    private readonly record struct ClipboardNote(NotaNote Note, int TrackId, int ClipIndex);
    private static readonly List<ClipboardNote> s_clipboard = new();

    // The last note touched — clicked, moved, resized or its velocity drawn — in any clip (#44):
    // a new note takes its length and velocity. Before any is touched, a new note is one grid
    // step at 0.8.
    private static double s_lastLen = double.NaN;
    private static float s_lastVel = 0.8f;
    private void Remember(int i)
    {
        if (i < 0 || i >= _notes.Count) return;
        s_lastLen = _notes[i].LengthBeats;
        s_lastVel = _notes[i].Velocity;
    }
    private double NewNoteLen(double start, int slot)
    {
        double len = double.IsNaN(s_lastLen) ? MinNoteLen : Math.Max(MinNoteLen, s_lastLen);
        return Math.Max(MinNoteLen, Math.Min(len, _set.EditorEnd(slot) - start));
    }

    /// <summary>The clip a note drawn at <paramref name="beat"/> goes into (specs/029 FR-007):
    /// the active clip when it holds the beat, else another open, editable clip of the active
    /// clip's track that does; −1 when none does (nothing is drawn).</summary>
    private int DrawTarget(double beat)
    {
        if (_set.Contains(_set.Active, beat)) return _set.Active;
        int track = _set.ActiveClip.TrackId;
        for (int k = 0; k < _set.Count; k++)
            if (k != _set.Active && _set.Clips[k].TrackId == track && _set.IsEditable(k) && _set.Contains(k, beat)) return k;
        return -1;
    }

    private readonly RulerLane _ruler;
    private readonly ClipStrip _strip;
    /// <summary>A clip's name for the clip strip (null → "Track N"). Set by the host.</summary>
    public Func<int, int, string?>? ClipNameOf { get; set; }
    private readonly KeysColumn _keys;
    private readonly NoteGrid _gridControl;
    private readonly VelocityLane _vel;
    private readonly Border _velHost;
    private readonly Border _overlayHost = new() { IsVisible = false, ClipToBounds = true };
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
        {
            _pixelsPerBeat = Math.Clamp(viewportW / _lengthBeats, MinPpb, MaxPpb);
            // The fit happens mid-render: tell the snap chip after it, not during.
            Dispatcher.UIThread.Post(NotifyGrid);
        }
        // A newly opened multi-clip set too long to fit: start at the active clip.
        if (_pendingScrollTo is { } to && _pixelsPerBeat > 0 && viewportW > 0)
        {
            _pendingScrollTo = null;
            double vpBeats = viewportW / _pixelsPerBeat;
            _scrollBeats = Math.Clamp(to, 0, Math.Max(0, _lengthBeats - vpBeats));
            Dispatcher.UIThread.Post(SyncScroll);
        }
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

    // The keyboard is dark (nota-design "Nota Clip Editor"): white keys sit one step above
    // the panel, black keys below the well, and the lanes echo them — so the notes, not the
    // keys, are the brightest thing in the editor.
    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IBrush KeysBg = NotaPalette.Panel;
    private static readonly IBrush VelBg = NotaPalette.BgSunken;
    private static readonly IBrush KeyWhite = NotaPalette.KeyWhite;
    private static readonly IBrush KeyBlack = NotaPalette.KeyBlack;
    private static readonly IBrush LaneWhite = NotaPalette.CanvasBg;
    private static readonly IBrush LaneBlack = NotaPalette.KeyBlack;
    private static readonly IBrush LoopStrip = NotaPalette.Accent;
    private static readonly IBrush NoteSel = NotaPalette.Accent;
    private static readonly IPen NoteSelEdge = new Pen(NotaPalette.AccentBright, 1);
    private static readonly IBrush GhostFill = NotaPalette.AccentSubtle;
    private static readonly IPen GhostEdge = new Pen(NotaPalette.AccentBright, 1);
    private static readonly IBrush GhostStem = NotaPalette.AccentDim;
    private static readonly IBrush RulerBarText = NotaPalette.TextStrong;
    // Past the clip's end (seen once you zoom out): a dark wash and an Ink 2 end line, so the
    // clip's edge reads at a glance.
    private static readonly IBrush OutsideClip = NotaPalette.Wash(NotaPalette.SurfaceAbyss, 0xC8);
    // Several clips open (specs/029): the active clip's notes at full strength, the other
    // clips' quieter, and reference (locked) clips' quieter still — present, but out of the way.
    private const double OtherClipAlpha = 0.6, ReferenceClipAlpha = 0.28;
    private static readonly IPen ClipEndPen = new Pen(NotaPalette.TextStrong, 1);
    // Scale overlay: out-of-scale rows/keys get a translucent dark wash (they read
    // dimmed / semi-transparent), in-scale rows a faint accent, the root a stronger one.
    private static readonly IBrush OutScaleWash = NotaPalette.Wash(NotaPalette.BgSunken, 0xB4);
    // Scale rows are a neutral lift, not the accent: a key at rest is neither live nor chosen.
    private static readonly IBrush InScaleTint = NotaPalette.Wash(NotaPalette.VizInk, 0x0C);
    private static readonly IBrush RootTint = NotaPalette.Wash(NotaPalette.VizInk, 0x1E);
    private static readonly IPen PlayheadPen = new Pen(NotaPalette.Accent, 1);   // the playhead: 1px accent
    private static readonly IBrush KeyHover = NotaPalette.Wash(NotaPalette.AccentBright, 0x66);
    // Played-key highlight (currently-pressed keyboard / MIDI note): a solid key tint + a faint row wash.
    private static readonly IBrush HeldKeyFill = NotaPalette.Accent;
    private static readonly IBrush HeldRowTint = NotaPalette.Wash(NotaPalette.AccentBright, 0x30);
    private static readonly IPen KeyLine = new Pen(NotaPalette.GraphBorder, 1);
    private static readonly IPen KeyOctaveLine = new Pen(NotaPalette.BorderStrong, 1);
    private static readonly IPen RowLine = new Pen(NotaPalette.GridSubBeat, 1);
    private static readonly IPen RowOctaveLine = new Pen(NotaPalette.BorderDefault, 1);
    private static readonly IPen BeatPen = new Pen(NotaPalette.GraphBorder, 1);
    private static readonly IPen BarPen = new Pen(NotaPalette.BorderStrong, 1);
    private static readonly IPen SubBeatPen = new Pen(NotaPalette.GridSubBeat, 1);   // sub-beat grid (finer than a beat)
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush EdgeHighlight = NotaPalette.AccentPale;
    private static readonly IBrush MarqueeFill = NotaPalette.Wash(NotaPalette.AccentBright, 0x28);
    private static readonly IPen MarqueePen = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0xA0), 1);
    private static readonly IBrush LabelText = NotaPalette.TextTertiary;
    private static readonly IBrush KeyLabel = NotaPalette.TextMuted;
    private static readonly IBrush KeyLabelHot = NotaPalette.TextPrimary;
    private static readonly IBrush KeyLabelOnAccent = NotaPalette.TextOnAccent;
    private static readonly IBrush Divider = NotaPalette.BorderDefault;
    // Recorded MPE over a note: the bend as the pitch line it played, pressure as a shade
    // rising from the note's floor.
    private static readonly IPen BendPen = new Pen(NotaPalette.TextPrimary, 1.25, lineCap: PenLineCap.Round);
    private static readonly IPen BendPenOnSel = new Pen(NotaPalette.TextOnAccent, 1.25, lineCap: PenLineCap.Round);
    private static readonly IBrush PressureShade = NotaPalette.Wash(NotaPalette.SurfaceAbyss, 0x66);
    private static readonly Typeface Mono = NotaFonts.Mono;

    private double RangeH => (MaxPitch - MinPitch + 1) * RowH;

    public PianoRollView()
    {
        // Clip the horizontally-scrolling lanes to their own column so notes / beat marks
        // scrolled past the left edge don't bleed over the fixed keyboard + label gutter.
        _ruler = new RulerLane(this) { ClipToBounds = true };
        _strip = new ClipStrip(this) { ClipToBounds = true, IsVisible = false, Height = ClipStrip.RowH };
        _keys = new KeysColumn(this) { Height = RangeH };
        _gridControl = new NoteGrid(this) { Height = RangeH, ClipToBounds = true };
        _vel = new VelocityLane(this) { ClipToBounds = true };
        _expr = new ExpressionLane(this) { ClipToBounds = true };
        _velHost = new Border { BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 0, 0), Child = _vel };

        // Body: keys pinned left, grid fills; both scroll vertically together.
        // Top-aligned: zoomed out past the viewport, the 88 rows stay pinned to the top.
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{KeysW},*"), VerticalAlignment = VerticalAlignment.Top };
        body.Children.Add(_keys);
        Avalonia.Controls.Grid.SetColumn(_gridControl, 1);
        body.Children.Add(_gridControl);
        _bodyScroll = new ScrollViewer
        {
            Content = body,
            Background = Bg,   // the well below the rows when they don't fill the viewport
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // Ruler row (corner + ruler) pinned above; velocity row pinned below.
        var rulerRow = new Grid { ColumnDefinitions = new ColumnDefinitions($"{KeysW},*"), Height = RulerH };
        var corner = new Border { Background = KeysBg, BorderBrush = Divider, BorderThickness = new Thickness(0, 0, 1, 1) };
        rulerRow.Children.Add(corner);
        Avalonia.Controls.Grid.SetColumn(_ruler, 1);
        rulerRow.Children.Add(_ruler);

        // The clip strip (several clips open): corner + strip, under the ruler.
        var stripRow = new Grid { ColumnDefinitions = new ColumnDefinitions($"{KeysW},*") };
        stripRow.Children.Add(new Border { Background = KeysBg, BorderBrush = Divider, BorderThickness = new Thickness(0, 0, 1, 1) });
        Avalonia.Controls.Grid.SetColumn(_strip, 1);
        stripRow.Children.Add(_strip);
        stripRow.Bind(IsVisibleProperty, _strip.GetObservable(IsVisibleProperty));

        var velRow = new Grid { ColumnDefinitions = new ColumnDefinitions($"{KeysW},*"), Height = VelH };
        _laneRow = velRow;
        // The lane's gutter picks what it edits: velocity, or a recorded-MPE dimension.
        var velLabelHost = new Border { Background = KeysBg, BorderBrush = Divider, BorderThickness = new Thickness(0, 1, 1, 0) };
        velLabelHost.Child = _laneTabs = new LaneTabs(this);
        velRow.Children.Add(velLabelHost);
        Avalonia.Controls.Grid.SetColumn(_velHost, 1);
        velRow.Children.Add(_velHost);

        // Horizontal zoom scrollbar pinned below the velocity lane, aligned under the
        // grid (56px keys gutter on the left).
        var scrollRow = new Grid { ColumnDefinitions = new ColumnDefinitions($"{KeysW},*") };
        _hScroll = new ScrollBar { Orientation = Orientation.Horizontal, Height = 10, IsVisible = false, Minimum = 0 };
        _hScroll.Scroll += (_, _) => SetScrollBeats(_hScroll.Value);
        Avalonia.Controls.Grid.SetColumn(_hScroll, 1);
        scrollRow.Children.Add(_hScroll);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto") };
        root.Children.Add(rulerRow);
        Avalonia.Controls.Grid.SetRow(stripRow, 1);
        root.Children.Add(stripRow);
        Avalonia.Controls.Grid.SetRow(_bodyScroll, 2);
        root.Children.Add(_bodyScroll);
        // Overlay slot over the note grid (the clip editor's Envelopes mode draws there).
        Avalonia.Controls.Grid.SetRow(_overlayHost, 2);
        _overlayHost.Margin = new Thickness(KeysW, 0, 0, 0);
        root.Children.Add(_overlayHost);
        Avalonia.Controls.Grid.SetRow(velRow, 3);
        root.Children.Add(velRow);
        Avalonia.Controls.Grid.SetRow(scrollRow, 4);
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
        DetachedFromVisualTree += (_, _) => { StopAudition(); _heldTimer?.Stop(); if (_held.Count > 0) { _held.Clear(); _keys.InvalidateVisual(); _gridControl.InvalidateVisual(); } };

        // The grid setting (and Quantize step) is app-wide: follow it while on screen only —
        // a roll is rebuilt for every clip, so a lasting subscription would leak.
        void OnGrid(EditorKind k) { if (k == EditorKind.PianoRoll) Invalidate(); }
        AttachedToVisualTree += (_, _) => Grids.Changed += OnGrid;
        DetachedFromVisualTree += (_, _) => Grids.Changed -= OnGrid;
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
        s_view[_clipKey] = new ViewState(_pixelsPerBeat, _scrollBeats, _bodyScroll.Offset.Y, true, _rowH);
    }

    private void ApplyPendingVOffset()
    {
        if (_pendingVOffset is null && !_pendingCenter) return;
        if (_bodyScroll.Viewport.Height <= 0) return;   // not laid out yet; LayoutUpdated retries
        // After a vertical zoom-in the extent only grows on the next layout pass; an offset
        // set before that would be clamped to the old, shorter range.
        if (_bodyScroll.Extent.Height < RangeH - 0.5) return;
        double vy = _pendingCenter ? CenterOffsetForNotes() : _pendingVOffset!.Value;
        _pendingVOffset = null; _pendingCenter = false;
        double maxY = Math.Max(0, RangeH - _bodyScroll.Viewport.Height);
        _restoringView = true;
        _bodyScroll.Offset = new Vector(_bodyScroll.Offset.X, Math.Clamp(vy, 0, maxY));
        _restoringView = false;
        if (_saveAfterApply) { _saveAfterApply = false; SaveView(); }
    }

    /// <summary>Sets the row height, keeping the pitch at <paramref name="viewportY"/> (in
    /// the body viewport) where it is. Clamped to 6–36 px; ignored mid note-gesture so a
    /// drag never lands on a pitch the user didn't point at. View state only — no undo.</summary>
    internal void SetRowHeightAt(double h, double viewportY)
    {
        if (_gridControl.Dragging) return;
        double h0 = _rowH, h1 = Math.Clamp(h, MinRowH, MaxRowH);
        if (Math.Abs(h1 - h0) < 1e-9) return;
        double c = (_pendingVOffset ?? _bodyScroll.Offset.Y) + viewportY;
        _rowH = h1;
        _keys.Height = RangeH;
        _gridControl.Height = RangeH;
        _pendingCenter = false;
        _pendingVOffset = c * h1 / h0 - viewportY;
        _saveAfterApply = true;
        ApplyPendingVOffset();   // now if the extent already fits; LayoutUpdated otherwise
        Invalidate();
        SaveView();
    }

    internal void ZoomRowsAt(double factor, double viewportY) => SetRowHeightAt(_rowH * factor, viewportY);

    /// <summary>⌘⌥ (Ctrl+Alt) + wheel: the vertical-zoom wheel, checked before ⌘ + wheel.</summary>
    private static bool IsRowZoomWheel(KeyModifiers m)
        => (m & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 && (m & KeyModifiers.Alt) != 0;

    /// <summary>Fit to notes (⇧F): every note of the clip in view, one row of margin above
    /// and below, centred; an empty clip goes back to the default height around C5.</summary>
    public void FitToNotes()
    {
        if (_gridControl.Dragging) return;
        double vh = _bodyScroll.Viewport.Height;
        double h = DefaultRowH;
        if (_notes.Count > 0 && vh > 0)
        {
            int minP = int.MaxValue, maxP = int.MinValue;
            foreach (var n in _notes) { if (n.Pitch < minP) minP = n.Pitch; if (n.Pitch > maxP) maxP = n.Pitch; }
            h = vh / (maxP - minP + 1 + 2);
        }
        _rowH = Math.Clamp(h, MinRowH, MaxRowH);
        _keys.Height = RangeH;
        _gridControl.Height = RangeH;
        _pendingVOffset = null; _pendingCenter = true;
        _saveAfterApply = true;
        ApplyPendingVOffset();
        Invalidate();
        SaveView();
    }

    public double LengthBeats => _lengthBeats;

    /// <summary>Beats per bar from the transport: bar lines, bar-based grid steps and ruler
    /// numbers follow the time signature.</summary>
    public int BeatsPerBar { get => _beatsPerBar; set { if (value > 0 && value != _beatsPerBar) { _beatsPerBar = value; Invalidate(); } } }
    private int _beatsPerBar = 4;

    /// <summary>The app-wide grid state this roll reads (its setting and the Quantize step).</summary>
    internal EditorGrids Grids => EditorGrids.Shared;

    /// <summary>The snap grid at the current zoom (specs/003-adaptive-snap-grid): the step
    /// note edits snap to and the finest line drawn. Resolved on demand (allocation-free), so
    /// zoom and setting changes never leave it stale.</summary>
    internal ResolvedGrid SnapGridState => SnapGrid.Resolve(Grids.Get(EditorKind.PianoRoll), _pixelsPerBeat, _beatsPerBar);

    /// <summary>The step in effect, in beats: nudges, minimum note length, duplicate shift.</summary>
    internal double SnapStep => SnapGridState.Snap.Beats(_beatsPerBar);

    /// <summary>The shortest note an edit leaves: one step, or a 1/32 while snap is off.</summary>
    private double MinNoteLen => SnapGridState.SnapOn ? SnapStep : 0.125;

    /// <summary>A beat on the grid (unchanged while snap is off).</summary>
    internal double SnapBeat(double b)
    {
        var g = SnapGridState;
        return g.SnapOn ? SnapGrid.SnapBeat(b, g.Snap.Beats(_beatsPerBar), _beatsPerBar) : b;
    }

    /// <summary>The step in effect changed (zoom, time signature, the setting) — the header
    /// snap chip relabels.</summary>
    public event Action? GridChanged;
    private ResolvedGrid _lastGrid;

    public void SetTrackColor(ISolidColorBrush b) { _trackBrush = b; Invalidate(); }
    public ISolidColorBrush TrackBrush => _trackBrush;

    // ---- the open clip set (specs/029-multi-clip-editing) ------------------------
    /// <summary>The clips this roll edits; a set of one for the single-clip editor.</summary>
    public ClipSet Clips => _set;
    public OpenClip ActiveClip => _set.ActiveClip;
    /// <summary>Raised when another clip becomes active (strip click, a note of it clicked or
    /// drawn into) or a clip is locked / unlocked; the header, inspector and envelopes follow.</summary>
    public event Action? ActiveClipChanged;
    /// <summary>Arrangement beat of editor beat 0 — the host converts playhead and seek with it.</summary>
    public double Origin => _set.Origin;
    /// <summary>Length of the active clip (the inspector's LENGTH / LOOP).</summary>
    public double ActiveLengthBeats => _set.ActiveClip.LengthBeats;

    /// <summary>Track colour of note i's clip; the active colour for a one-clip set.</summary>
    private Color ColorOf(int i)
    {
        int s = i >= 0 && i < _owner.Count ? _owner[i] : 0;
        return s < _slotBrushes.Count && _set.IsMulti ? _slotBrushes[s].Color : _trackColor;
    }
    internal Color SlotColor(int slot) => slot < _slotBrushes.Count && _set.IsMulti ? _slotBrushes[slot].Color : _trackColor;
    /// <summary>Whether note i belongs to a reference (read-only) clip.</summary>
    internal bool IsLocked(int i) => i >= 0 && i < _owner.Count && !_set.IsEditable(_owner[i]);
    /// <summary>How strongly note i reads: its own clip is full strength when active (or the
    /// only clip), quieter otherwise, and a reference clip quieter still.</summary>
    internal double ClipEmphasis(int i)
    {
        if (!_set.IsMulti || i < 0 || i >= _owner.Count || _owner[i] == _set.Active) return 1;
        return _set.IsEditable(_owner[i]) ? OtherClipAlpha : ReferenceClipAlpha;
    }

    private void AddNote(NotaNote n, int owner) { _notes.Add(n); _owner.Add(owner); }
    private void RemoveNoteAt(int i) { _notes.RemoveAt(i); _owner.RemoveAt(i); }
    private void ReplaceAll(IEnumerable<NotaNote> notes, IEnumerable<int> owners)
    {
        var n = notes.ToList(); var o = owners.ToList();   // either may alias the lists being replaced
        _notes.Clear(); _notes.AddRange(n);
        _owner.Clear(); _owner.AddRange(o);
        System.Diagnostics.Debug.Assert(_notes.Count == _owner.Count);
    }
    private void RemoveWhere(Func<int, bool> drop)
    {
        for (int i = _notes.Count - 1; i >= 0; i--) if (drop(i)) RemoveNoteAt(i);
    }

    /// <summary>Loads one clip's notes (clip-local beats); the Session slot editor and anything
    /// that edits a single clip without a <see cref="ClipSet"/>.</summary>
    public void SetNotes(IEnumerable<NotaNote> notes, double lengthBeats)
    {
        var list = notes.ToList();
        _set = ClipSet.Single(-1, -1, 0, Math.Max(1, lengthBeats));
        _slotBrushes.Clear();
        Load(list, Enumerable.Repeat(0, list.Count), Math.Max(1, lengthBeats));
    }

    /// <summary>Loads a set of clips (specs/029): <paramref name="perClip"/> holds each clip's
    /// notes in its own beats; <paramref name="brushOf"/> gives a track's colour. A set of one is
    /// exactly the single-clip editor.</summary>
    public void SetClips(ClipSet set, IReadOnlyList<ClipNotes> perClip, Func<int, ISolidColorBrush> brushOf)
    {
        bool rescroll = !ReferenceEquals(set, _set) && set.IsMulti;
        _set = set;
        _slotBrushes.Clear();
        foreach (var c in set.Clips) _slotBrushes.Add(brushOf(c.TrackId));
        _trackBrush = _slotBrushes[set.Active];
        var notes = new List<NotaNote>(); var owners = new List<int>();
        set.Merge(perClip, notes, owners);
        // Multi-clip view state is not remembered per clip: fit the span, then show the active clip.
        if (set.IsMulti) _clipKey = (int.MinValue, int.MinValue);
        Load(notes, owners, Math.Max(1, set.SpanBeats));
        if (rescroll) _pendingScrollTo = set.EditorStart(set.Active);
        ActiveClipChanged?.Invoke();
    }

    private double? _pendingScrollTo;   // editor beat to bring into view once the zoom is fitted

    /// <summary>Makes clip <paramref name="slot"/> the active one (a locked clip is unlocked).
    /// A pending placement lands first.</summary>
    public void SetActiveClip(int slot)
    {
        if (slot == _set.Active && _set.IsEditable(slot)) return;
        SettlePlacement();
        if (!_set.SetActive(slot)) return;
        _trackBrush = slot < _slotBrushes.Count ? _slotBrushes[slot] : _trackBrush;
        Invalidate();
        ActiveClipChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Locks (<paramref name="on"/>) or unlocks clip <paramref name="slot"/> as a
    /// read-only reference. Refused for the active clip. Its notes leave the selection.</summary>
    public bool SetReference(int slot, bool on)
    {
        SettlePlacement();
        if (!_set.SetReference(slot, on)) return false;
        if (on) _selection.RemoveWhere(i => i < _owner.Count && _owner[i] == slot);
        Invalidate();
        ActiveClipChanged?.Invoke();
        Changed?.Invoke();
        return true;
    }

    /// <summary>The track a note being heard plays on (its own clip's track); −1 for the host's
    /// own track. Read by the host's <see cref="AuditionOn"/> / <see cref="AuditionOff"/>.</summary>
    public int AuditionTrackId => _auditionTrack;
    private int _auditionTrack = -1;
    private int TrackOfSlot(int slot) => slot >= 0 && slot < _set.Count ? _set.Clips[slot].TrackId : -1;

    private void Load(IEnumerable<NotaNote> notes, IEnumerable<int> owners, double lengthBeats)
    {
        _previewBackup = null;
        _placementPending = false;   // a reload (undo, recording, another clip) is the truth now
        ReplaceAll(notes, owners);
        _lengthBeats = lengthBeats;
        _selection.Clear();
        _strip.Relayout();
        RememberCommitted();
        _saveAfterApply = false;   // a zoom queued on the previous clip must not save onto this one

        // Restore this clip's saved scroll/zoom if we've edited it before; otherwise fit the
        // length horizontally and centre vertically on the clip's own notes at the default
        // row height. Row heights are per clip: zooming one never changes another.
        bool saved = s_view.TryGetValue(_clipKey, out var vs) && vs.HasV;
        _rowH = saved ? Math.Clamp(vs.RowH, MinRowH, MaxRowH) : DefaultRowH;
        _keys.Height = RangeH;
        _gridControl.Height = RangeH;
        if (saved)
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
    /// selects the copies. Returns false if nothing was selected (or nothing fits). Undoable
    /// (one Commit). Copies follow the same reach as moved notes (specs/029).</summary>
    public bool DuplicateSelection()
    {
        SettlePlacement();
        var sel = _selection.Where(i => i >= 0 && i < _notes.Count).ToList();
        if (sel.Count == 0) return false;
        double minStart = double.MaxValue, maxEnd = double.MinValue;
        foreach (int i in sel) { var n = _notes[i]; minStart = Math.Min(minStart, n.StartBeat); maxEnd = Math.Max(maxEnd, n.StartBeat + n.LengthBeats); }
        double shift = Math.Max(SnapStep, maxEnd - minStart);
        var copies = new List<NotaNote>(sel.Count);
        var copyOwners = new List<int>(sel.Count);
        foreach (int i in sel)
        {
            // Each copy lands where the shift puts it: in its own clip, or — past the seam — in
            // the next open clip of its track (as a moved note would). Past every open clip it is
            // dropped, as a paste past the clip's end is, instead of piling up at the edge.
            var n = _notes[i];
            n.StartBeat += shift;
            int slot = _set.ShiftTarget(_owner[i], n.StartBeat);
            if (slot < 0) continue;
            double end = _set.EditorEnd(slot);
            if (n.StartBeat + n.LengthBeats > end) n.LengthBeats = Math.Max(MinNoteLen, end - n.StartBeat);
            copies.Add(n);
            copyOwners.Add(slot);
        }
        if (copies.Count == 0) return false;
        // Like a duplicated clip in the arrangement, the copies overwrite what they land on
        // straight away, with the same rule a settled placement uses (NoteOverwrite).
        int baseIdx = _notes.Count;
        for (int k = 0; k < copies.Count; k++) AddNote(copies[k], copyOwners[k]);
        var placed = Enumerable.Range(baseIdx, copies.Count).ToList();
        var settled = new List<NotaNote>(_notes.Count);
        var placedOut = new List<int>(copies.Count);
        var ownersOut = new List<int>(_notes.Count);
        NoteOverwrite.Settle(_notes, placed, settled, placedOut, _owner, ownersOut);
        ReplaceAll(settled, ownersOut);
        _selection.Clear();
        _selection.UnionWith(placedOut);
        Invalidate();
        CommitNotes();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes the selected notes. Undoable (one Commit).</summary>
    public bool DeleteSelection()
    {
        if (_selection.Count == 0) return false;
        _placementPending = false;   // the placed notes are what goes
        foreach (int i in _selection.OrderByDescending(x => x))
            if (i >= 0 && i < _notes.Count) RemoveNoteAt(i);
        _selection.Clear();
        Invalidate();
        CommitNotes();
        Changed?.Invoke();
        return true;
    }

    /// <summary>True when the scale overlay is on and some editable note falls outside it.</summary>
    public bool HasOutOfScaleNotes
    {
        get
        {
            if (!_scaleOn) return false;
            for (int i = 0; i < _notes.Count; i++) if (!IsLocked(i) && !InScale(_notes[i].Pitch)) return true;
            return false;
        }
    }

    /// <summary>Removes every editable note outside the scale overlay. No-op with the scale off.
    /// Undoable (one Commit).</summary>
    public bool RemoveOutOfScaleNotes()
    {
        if (!HasOutOfScaleNotes) return false;
        SettlePlacement();
        RemoveWhere(i => !IsLocked(i) && !InScale(_notes[i].Pitch));
        _selection.Clear();
        Invalidate();
        CommitNotes();
        Changed?.Invoke();
        return true;
    }

    // --- clip-tool preview -------------------------------------------------------
    // A tool's result is shown in the roll and streamed to the engine with no undo
    // checkpoint, exactly like a note drag, so the knobs are audible while they move.
    // The clips' own notes are held here until the preview is applied or dropped.
    private List<NotaNote>? _previewBackup;
    private List<int>? _previewOwners;

    public bool Previewing => _previewBackup is not null;

    // Indices into _notes that a running preview added; empty when nothing is previewing.
    private readonly HashSet<int> _ghosts = new();
    private bool IsGhost(int i) => _previewBackup is not null && _ghosts.Contains(i);
    private static (int, long, long, int) NoteKey(NotaNote n)
        => (n.Pitch, (long)Math.Round(n.StartBeat * 1e4), (long)Math.Round(n.LengthBeats * 1e4), (int)Math.Round(n.Velocity * 127));

    /// <summary>The clip's notes ignoring any preview in flight — what a tool reads.</summary>
    public IReadOnlyList<NotaNote> SourceNotes => _previewBackup ?? _notes;

    /// <summary>Indices into <see cref="SourceNotes"/> that are selected. Only meaningful
    /// while no preview is running, which is when a tool captures its working set.</summary>
    public IReadOnlyCollection<int> SelectionIndices => _selection;

    /// <summary>Every open clip's notes (clip-local beats) ignoring a preview in flight, and
    /// each clip's selected notes as indices into its own list — a tool's per-clip working set
    /// (specs/029: a tool runs once per clip, so what it adds stays in that clip).</summary>
    public (List<ClipNotes> Clips, List<HashSet<int>> Selected) SourceByClip()
    {
        var notes = _previewBackup ?? _notes;
        var owners = _previewOwners ?? _owner;
        var clips = _set.Split(notes, owners);
        var selected = new List<HashSet<int>>(clips.Count);
        var local = new int[clips.Count];
        for (int k = 0; k < clips.Count; k++) selected.Add(new HashSet<int>());
        for (int i = 0; i < notes.Count; i++)
        {
            int o = owners[i];
            if (o < 0 || o >= clips.Count) continue;
            if (_previewBackup is null && _selection.Contains(i)) selected[o].Add(local[o]);
            local[o]++;
        }
        return (clips, selected);
    }

    /// <summary>Puts a tool's result on screen and in the engine without touching undo.</summary>
    public void ShowPreview(IReadOnlyList<NotaNote> notes)
        => ShowPreview(new[] { new ClipNotes(_set.ActiveClip.TrackId, _set.ActiveClip.ClipIndex, notes) });

    /// <summary>As <see cref="ShowPreview(IReadOnlyList{NotaNote})"/> for several clips: each
    /// entry replaces its clip's notes (clip-local beats); clips without an entry keep theirs.</summary>
    public void ShowPreview(IReadOnlyList<ClipNotes> perClip)
    {
        // The tool computed its result from the notes as they stand, so a pending placement
        // is part of what it replaces; settling now would only reshuffle stale indices.
        _placementPending = false;
        _previewBackup ??= new List<NotaNote>(_notes);
        _previewOwners ??= new List<int>(_owner);
        var merged = _set.Split(_previewBackup, _previewOwners);
        foreach (var cn in perClip)
        {
            int slot = _set.IndexOf(cn.TrackId, cn.ClipIndex);
            if (_set.Count == 1) slot = 0;   // a one-clip set: whatever its ids, the result is its
            if (slot >= 0) merged[slot] = new ClipNotes(merged[slot].TrackId, merged[slot].ClipIndex, cn.Notes);
        }
        var notes = new List<NotaNote>(); var owners = new List<int>();
        _set.Merge(merged, notes, owners);
        ReplaceAll(notes, owners);
        // What the tool added (not in the clip before) draws as a brass outline over the notes.
        var before = new HashSet<(int, int, long, long, int)>(Enumerable.Range(0, _previewBackup.Count)
            .Select(i => Keyed(_previewBackup[i], _previewOwners[i])));
        _ghosts.Clear();
        for (int i = 0; i < _notes.Count; i++) if (!before.Contains(Keyed(_notes[i], _owner[i]))) _ghosts.Add(i);
        _selection.Clear();
        EnsureNotesVisible();
        Invalidate();
        PushLive(_notes, _owner);
        Changed?.Invoke();

        static (int, int, long, long, int) Keyed(NotaNote n, int o) { var k = NoteKey(n); return (o, k.Item1, k.Item2, k.Item3, k.Item4); }
    }

    /// <summary>Centres the pitch range when not one note is on screen — which is what a
    /// generator writing into a different register leaves behind. A preview you cannot see
    /// is worse than no preview; scrolling only when nothing is visible keeps the view put
    /// for every ordinary edit.</summary>
    public void EnsureNotesVisible()
    {
        if (_notes.Count == 0) return;
        double vh = _bodyScroll.Viewport.Height;
        if (vh <= 0) { _pendingCenter = true; return; }
        double top = _bodyScroll.Offset.Y, bottom = top + vh;
        foreach (var n in _notes)
        {
            double y = (MaxPitch - n.Pitch) * RowH;
            if (y >= top && y + RowH <= bottom) return;
        }
        _pendingVOffset = CenterOffsetForNotes();
        ApplyPendingVOffset();
    }

    /// <summary>Drops the preview and puts the clip back, again without an undo entry —
    /// so a tool the user backed out of leaves no trace in the history.</summary>
    public void CancelPreview()
    {
        if (_previewBackup is null) return;
        ReplaceAll(_previewBackup, _previewOwners!);
        _previewBackup = null;
        _previewOwners = null;
        _selection.Clear();
        Invalidate();
        PushLive(_notes, _owner);
        Changed?.Invoke();
    }

    /// <summary>Keeps the previewed notes as one undoable edit.</summary>
    public void ApplyPreview()
    {
        if (_previewBackup is null) return;
        _placementPending = false;
        var original = _previewBackup;
        var originalOwners = _previewOwners!;
        _selection.Clear();
        Invalidate();
        // A commit checkpoints whatever the engine currently holds — and the live stream
        // has already put the preview there, so committing straight away would make undo
        // land on the preview. Rewind the engine to the clip's own notes first (no undo
        // step of its own), then commit, so the one entry points at the original.
        PushLive(original, originalOwners);
        CommitNotes();
        Changed?.Invoke();
    }

    // ---- pushing notes to the engine -------------------------------------------------
    /// <summary>Several-clip commit (specs/029): the clips whose notes changed, each in its own
    /// beats. The host makes one undo step of the whole list. When set, it replaces
    /// <see cref="Commit"/>.</summary>
    public Action<IReadOnlyList<ClipNotes>>? CommitClips { get; set; }
    /// <summary>Live, no-undo variant of <see cref="CommitClips"/> (drags, tool previews).</summary>
    public Action<IReadOnlyList<ClipNotes>>? CommitClipsLive { get; set; }

    private bool HasLivePath => CommitClipsLive is not null || CommitLive is not null;

    // What each clip holds in the engine as far as this roll knows: what it loaded or last pushed.
    private NotaNote[][] _pushed = Array.Empty<NotaNote[]>();

    private void RememberCommitted()
        => _pushed = _set.Split(_notes, _owner).Select(c => c.Notes.ToArray()).ToArray();

    private static bool SameNotes(IReadOnlyList<NotaNote> a, NotaNote[] b)
    {
        if (a.Count != b.Length) return false;
        for (int i = 0; i < b.Length; i++)
        {
            var x = a[i]; var y = b[i];
            if (x.Pitch != y.Pitch || x.StartBeat != y.StartBeat || x.LengthBeats != y.LengthBeats
                || x.Velocity != y.Velocity || x.ExprId != y.ExprId) return false;
        }
        return true;
    }

    // The clips whose notes differ from what was last pushed — or, for the undo seed of a
    // drag, every editable clip (a drag may carry notes into any of them).
    private List<ClipNotes> DirtyClips(IReadOnlyList<NotaNote> notes, IReadOnlyList<int> owners, bool all)
    {
        var split = _set.Split(notes, owners);
        if (_pushed.Length != split.Count) _pushed = new NotaNote[split.Count][];
        var dirty = new List<ClipNotes>();
        for (int k = 0; k < split.Count; k++)
        {
            bool changed = _pushed[k] is null || !SameNotes(split[k].Notes, _pushed[k]);
            if (!changed && !(all && _set.IsEditable(k))) continue;
            _pushed[k] = split[k].Notes.ToArray();
            dirty.Add(split[k]);
        }
        return dirty;
    }

    /// <summary>Pushes a note set as one undoable edit. Any clip-tool preview in flight is
    /// superseded — the notes on screen become the clip's own and the tools rail re-reads
    /// them on the next <see cref="Changed"/>. Without this, quantizing (or drawing a note)
    /// while a generator previewed would leave the rail holding a baseline it could later
    /// revert the clip to. <paramref name="seed"/>: the pre-drag state that seeds a drag's
    /// undo step — every editable clip goes.</summary>
    private void CommitNotes(IReadOnlyList<NotaNote>? notes = null, IReadOnlyList<int>? owners = null, bool seed = false)
    {
        _previewBackup = null;
        _previewOwners = null;
        notes ??= _notes;
        owners ??= _owner;
        if (CommitClips is not null)
        {
            var dirty = DirtyClips(notes, owners, seed);
            if (dirty.Count > 0) CommitClips(dirty);
        }
        else Commit?.Invoke(notes);
    }

    /// <summary>Streams notes to the engine with no undo checkpoint.</summary>
    private void PushLive(IReadOnlyList<NotaNote> notes, IReadOnlyList<int> owners)
    {
        if (CommitClipsLive is not null)
        {
            var dirty = DirtyClips(notes, owners, false);
            if (dirty.Count > 0) CommitClipsLive(dirty);
        }
        else CommitLive?.Invoke(notes);
    }

    /// <summary>Moves note starts toward the Quantize step (its own value, never the zoom's).
    /// Notes of reference clips stay put.</summary>
    public void Quantize(double strength)
    {
        SettlePlacement();
        double qStep = Grids.Quantize.Beats(_beatsPerBar);
        for (int i = 0; i < _notes.Count; i++)
        {
            if (IsLocked(i)) continue;
            var n = _notes[i];
            double q = SnapGrid.SnapBeat(n.StartBeat, qStep, _beatsPerBar);
            n.StartBeat += (q - n.StartBeat) * Math.Clamp(strength, 0, 1);
            _notes[i] = n;
        }
        Invalidate();
        CommitNotes();
        Changed?.Invoke();
    }

    public void TransposeBy(int semis)
    {
        SettlePlacement();
        for (int i = 0; i < _notes.Count; i++)
        {
            if (IsLocked(i)) continue;
            var n = _notes[i];
            n.Pitch = Math.Clamp(n.Pitch + semis, 0, 127);
            _notes[i] = n;
        }
        Invalidate();
        CommitNotes();
        Changed?.Invoke();
    }

    /// <summary>Live playback cursor in editor beats (clip-local for one clip); playing=false
    /// or an out-of-range beat hides it. Cheap: repaints only the grid + ruler, not the whole roll.</summary>
    public void SetPlayhead(double clipLocalBeat, bool playing)
    {
        double b = (playing && clipLocalBeat >= 0 && clipLocalBeat <= _lengthBeats) ? clipLocalBeat : -1;
        bool wasHidden = _playheadBeat < 0;
        if (Math.Abs(b - _playheadBeat) < 1e-6 && (b < 0) == wasHidden) return;
        _playheadBeat = b;
        _gridControl.InvalidateVisual();
        _ruler.InvalidateVisual();
    }

    /// <summary>Copies the selected notes to the shared clipboard, each with the clip it came
    /// from and its beat in that clip (specs/029).</summary>
    public bool CopySelection()
    {
        var sel = _selection.Where(i => i >= 0 && i < _notes.Count).OrderBy(i => i).ToList();
        if (sel.Count == 0) return false;
        s_clipboard.Clear();
        foreach (int i in sel)
        {
            var c = _notes[i];
            var clip = _set.Clips[_owner[i]];
            c.StartBeat = _set.ToLocal(_owner[i], c.StartBeat);
            s_clipboard.Add(new ClipboardNote(c, clip.TrackId, clip.ClipIndex));
        }
        return true;
    }

    /// <summary>Copies then deletes the selection. Undoable.</summary>
    public bool CutSelection() => CopySelection() && DeleteSelection();

    /// <summary>Pastes the clipboard on top of the copied notes' original position (whatever
    /// the transport is doing) and selects the copies, ready to be moved off with the arrows or
    /// the mouse: back into the clip they came from when it is open, else into the active clip
    /// at the same clip-relative beats. The copies overwrite nothing until they are let go
    /// (SettlePlacement). Undoable.</summary>
    public bool PasteClipboard()
    {
        if (s_clipboard.Count == 0) return false;
        SettlePlacement();
        int baseIdx = _notes.Count;
        foreach (var c in s_clipboard)
        {
            var n = c.Note;
            var (slot, at) = _set.PasteTarget(c.TrackId, c.ClipIndex, c.Note.StartBeat);
            double end = _set.EditorEnd(slot);
            n.StartBeat = at;
            if (n.StartBeat >= end - 1e-9) continue;                     // past the clip end → skip
            if (n.StartBeat + n.LengthBeats > end)                         // trim a note that overruns
                n.LengthBeats = Math.Max(MinNoteLen, end - n.StartBeat);
            AddNote(n, slot);
        }
        if (_notes.Count == baseIdx) return false;
        _selection.Clear();
        for (int k = baseIdx; k < _notes.Count; k++) _selection.Add(k);
        _placementPending = true;
        Invalidate(); CommitNotes(); Changed?.Invoke();
        return true;
    }

    /// <summary>The allowed shift of the selected notes, in beats, so that every note still
    /// starts within its reach (its clip, joined with neighbouring open clips of its track) and
    /// — for a nudge — still ends within it; the selection moves as a block.</summary>
    private (double Lo, double Hi) ShiftRange(IEnumerable<int> sel, Func<int, (double start, double len, int owner)> at, bool endsInside)
    {
        double lo = double.MinValue, hi = double.MaxValue;
        foreach (int i in sel)
        {
            var (start, len, owner) = at(i);
            var (rs, re) = _set.Reach(owner);
            lo = Math.Max(lo, rs - start);
            hi = Math.Min(hi, endsInside ? re - (start + len) : Math.Max(0, re - MinNoteLen - start));
        }
        return (Math.Min(lo, 0), Math.Max(hi, 0));
    }

    /// <summary>Nudges the selection by dPitch semitones and/or dBeat beats, clamped to the
    /// pitch range and clip bounds (moves as a block, stopping at the edge). A note whose start
    /// crosses into a neighbouring open clip of its track moves into that clip. Undoable.
    /// Scrolls vertically to keep pitch-moved notes in view.</summary>
    public bool MoveSelection(int dPitch, double dBeat)
    {
        var sel = _selection.Where(i => i >= 0 && i < _notes.Count).ToList();
        if (sel.Count == 0) return false;
        int minP = 128, maxP = -1;
        foreach (int i in sel)
        {
            minP = Math.Min(minP, _notes[i].Pitch);
            maxP = Math.Max(maxP, _notes[i].Pitch);
        }
        var (lo, hi) = ShiftRange(sel, i => (_notes[i].StartBeat, _notes[i].LengthBeats, _owner[i]), endsInside: true);
        double db = Math.Clamp(dBeat, lo, hi);
        int dp = dPitch;
        if (minP + dp < MinPitch) dp = MinPitch - minP;
        if (maxP + dp > MaxPitch) dp = MaxPitch - maxP;
        if (Math.Abs(db) < 1e-9 && dp == 0) return false;
        foreach (int i in sel)
        {
            var n = _notes[i]; n.StartBeat += db; n.Pitch += dp; _notes[i] = n;
            _owner[i] = _set.OwnerAfterMove(_owner[i], n.StartBeat);
        }
        _placementPending = true;
        Invalidate(); CommitNotes(); Changed?.Invoke();
        if (dp != 0) EnsurePitchVisible(dp > 0 ? maxP + dp : minP + dp);
        return true;
    }

    /// <summary>Lengthens (dir +1) or shortens (dir −1) every selected note by one grid step,
    /// keeping starts; never below the minimum length or past its clip's end. Undoable.</summary>
    public bool ResizeSelection(int dir)
    {
        if (_selection.Count == 0) return false;
        double step = SnapGridState.SnapOn ? SnapStep : MinNoteLen;
        if (!NoteOverwrite.ResizeBy(_notes, _selection, dir * step, MinNoteLen, i => _set.EditorEnd(_owner[i]))) return false;
        _placementPending = true;
        Invalidate(); CommitNotes(); Changed?.Invoke();
        return true;
    }

    /// <summary>Moves every selected note's velocity by <paramref name="steps127"/> MIDI steps
    /// (clamped to 1–127). Not a placement: nothing gets overwritten. Undoable.</summary>
    public bool NudgeVelocity(int steps127)
    {
        bool changed = false;
        foreach (int i in _selection)
        {
            if (i < 0 || i >= _notes.Count) continue;
            var n = _notes[i];
            float v = NoteOverwrite.VelocityBy(n.Velocity, steps127);
            if (v == n.Velocity) continue;
            n.Velocity = v;
            _notes[i] = n;
            changed = true;
        }
        if (!changed) return false;
        Invalidate(); CommitNotes(); Changed?.Invoke();
        return true;
    }

    // ---- pending placement (specs/007-midi-note-editing) -------------------------
    // Notes moved, stretched, drawn or pasted overwrite same-pitch notes they land on, but
    // only once the selection is let go: until then the notes underneath stay whole and are
    // drawn marked, so a pasted copy can be walked off its original with the arrows. Only
    // notes of the same clip overwrite each other (specs/029).
    private bool _placementPending;

    // The selection as a list for the per-frame overwrite marks (Fate is allocation-free over a
    // list); rebuilt only when the selection changed since the last frame.
    private readonly List<int> _placedForRender = new();
    private IReadOnlyList<int> PlacedForRender()
    {
        if (_placedForRender.Count != _selection.Count || !_placedForRender.TrueForAll(_selection.Contains))
        {
            _placedForRender.Clear();
            _placedForRender.AddRange(_selection);
        }
        return _placedForRender;
    }

    /// <summary>Applies a pending placement: the selected notes overwrite the same-pitch notes
    /// of their own clip they cover (NoteOverwrite). Its own undo step. Called whenever the
    /// selection is let go — selection change, focus loss, whole-clip edits, and by the host
    /// before save/export. Returns true when notes were cut or removed (indices may have shifted).</summary>
    public bool SettlePlacement()
    {
        if (!_placementPending) return false;
        _placementPending = false;
        if (_selection.Count == 0) return false;
        var placed = _selection.Where(i => i >= 0 && i < _notes.Count).ToList();
        var settled = new List<NotaNote>(_notes.Count);
        var placedOut = new List<int>(placed.Count);
        var ownersOut = new List<int>(_notes.Count);
        NoteOverwrite.Settle(_notes, placed, settled, placedOut, _owner, ownersOut);
        bool changed = settled.Count != _notes.Count;
        for (int i = 0; !changed && i < settled.Count; i++)
            changed = settled[i].LengthBeats != _notes[i].LengthBeats;
        if (!changed) return false;
        ReplaceAll(settled, ownersOut);
        _selection.Clear();
        _selection.UnionWith(placedOut);
        Invalidate(); CommitNotes(); Changed?.Invoke();
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

    /// <summary>Puts a control over the note grid (below the ruler, right of the keys) —
    /// or removes it with null. It is repainted with the roll, so it can map beats through
    /// <see cref="BeatToX"/> and follow zoom and scroll.</summary>
    public void SetOverlay(Control? overlay)
    {
        _overlayHost.Child = overlay;
        _overlayHost.IsVisible = overlay is not null;
    }

    /// <summary>The selected notes, in list order, each at its beat in its own clip.</summary>
    public IReadOnlyList<NotaNote> SelectedNotes()
        => _selection.Where(i => i >= 0 && i < _notes.Count).OrderBy(i => i).Select(i =>
        {
            var n = _notes[i];
            n.StartBeat = _set.ToLocal(_owner[i], n.StartBeat);
            return n;
        }).ToList();

    /// <summary>The ⌘/Ctrl-wheel zoom and Shift/horizontal-wheel scroll of the note grid, for an
    /// overlay that sits on top of it.</summary>
    internal void RouteWheel(PointerWheelEventArgs e, double x)
    {
        var mods = e.KeyModifiers;
        if ((mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            ZoomAt(WheelInput.ZoomFactor(e.Delta.Y, 1.2), XToBeat(x));
        else if ((mods & KeyModifiers.Shift) != 0)
            ScrollByBeats(-WheelInput.Pixels(e.Delta.Y) / Math.Max(1e-6, PixelsPerBeat));
        else if (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
            ScrollByBeats(-WheelInput.Pixels(e.Delta.X) / Math.Max(1e-6, PixelsPerBeat));
        else return;
        e.Handled = true;
    }

    /// <summary>Darkens everything outside the open clips and marks each covered stretch's
    /// edges with a line; with one clip, everything right of its end.</summary>
    private void DrawOutsideClip(DrawingContext ctx, double w, double h)
    {
        double from = 0;   // editor beat where the current uncovered stretch starts
        foreach (var (s, e) in CoveredSpans())
        {
            Wash(from, s, edgeAtEnd: true);
            from = e;
        }
        Wash(from, double.PositiveInfinity, edgeAtEnd: false);

        void Wash(double b0, double b1, bool edgeAtEnd)
        {
            double x0 = Math.Max(0, BeatToX(b0)), x1 = double.IsPositiveInfinity(b1) ? w : Math.Min(w, BeatToX(b1));
            if (x1 > x0) ctx.FillRectangle(OutsideClip, new Rect(x0, 0, x1 - x0, h));
            // The edge line where a clip ends (b0) and, between clips, where the next begins.
            double xe = BeatToX(b0);
            if (b0 > 0 && xe >= 0 && xe < w) ctx.DrawLine(ClipEndPen, new Point(xe + 0.5, 0), new Point(xe + 0.5, h));
            double xs = BeatToX(b1);
            if (edgeAtEnd && b1 > b0 && xs >= 0 && xs < w) ctx.DrawLine(ClipEndPen, new Point(xs + 0.5, 0), new Point(xs + 0.5, h));
        }
    }

    // The editor-beat stretches covered by at least one open clip, merged, in order.
    private List<(double Start, double End)> CoveredSpans()
    {
        var spans = new List<(double, double)>();
        foreach (var (s, e) in Enumerable.Range(0, _set.Count).Select(k => (_set.EditorStart(k), _set.EditorEnd(k))).OrderBy(t => t.Item1))
        {
            if (spans.Count > 0 && s <= spans[^1].Item2 + 1e-9) spans[^1] = (spans[^1].Item1, Math.Max(spans[^1].Item2, e));
            else spans.Add((s, e));
        }
        return spans;
    }

    private void Invalidate()
    {
        _overlayHost.Child?.InvalidateVisual();
        _ruler.InvalidateVisual();
        _strip.InvalidateVisual();
        _keys.InvalidateVisual();
        _gridControl.InvalidateVisual();
        _vel.InvalidateVisual();
        _expr.InvalidateVisual();
        NotifyGrid();
    }

    private void NotifyGrid()
    {
        var g = SnapGridState;
        if (g != _lastGrid) { _lastGrid = g; GridChanged?.Invoke(); }
    }

    private static bool IsBlack(int pitch) { int n = ((pitch % 12) + 12) % 12; return n is 1 or 3 or 6 or 8 or 10; }
    internal static string NoteName(int pitch)
    {
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        return names[((pitch % 12) + 12) % 12] + (pitch / 12 - 1);
    }

    // --- snap grid lines (the same positions SnapBeat lands on) -----------

    /// <summary>The vertical grid of a lane: one line per snap position at the drawn step, in
    /// the visible part of the clip. Shared by the note grid, the velocity and MPE lanes.</summary>
    internal void DrawGridLines(DrawingContext ctx, double w, double h)
    {
        double ppb = _pixelsPerBeat;
        if (ppb <= 0) return;
        double viewEnd = Math.Min(_lengthBeats, ScrollBeats + w / ppb);
        double step = SnapGridState.Draw.Beats(_beatsPerBar);
        foreach (double b in SnapGrid.GridLines(ScrollBeats, viewEnd, step, _beatsPerBar))
        {
            double x = BeatToX(b) + 0.5;
            ctx.DrawLine(GridPen(b), new Point(x, 0), new Point(x, h));
        }
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
        if (Math.Abs(b / _beatsPerBar - Math.Round(b / _beatsPerBar)) < 1e-6) return BarPen;
        if (Math.Abs(b - Math.Round(b)) < 1e-6) return BeatPen;
        return SubBeatPen;
    }

    // Ruler label at beat b: "bar.beat", plus a 1/16 index when mid-beat (e.g. 3.2.3).
    internal string GridLabel(double b)
    {
        int bar = (int)Math.Floor(b / _beatsPerBar) + 1 + RulerBarOffset;
        double inBar = b - Math.Floor(b / _beatsPerBar) * _beatsPerBar;
        int beat = (int)Math.Floor(inBar) + 1;
        double frac = inBar - Math.Floor(inBar);
        if (frac < 1e-6) return $"{bar}.{beat}";
        return $"{bar}.{beat}.{(int)Math.Round(frac * 4) + 1}";
    }

    // Ruler bar numbers: the clip's own bars for one clip, the arrangement's when several are
    // open (their origin sits on a bar line, see ClipSet).
    private int RulerBarOffset => _set.IsMulti ? (int)Math.Round(_set.Origin / Math.Max(1, _beatsPerBar)) : 0;

    // ---- ruler -----------------------------------------------------------
    private sealed class RulerLane : Control
    {
        private readonly PianoRollView _o;
        private bool _scrub;
        public RulerLane(PianoRollView o) { _o = o; }

        // Click places the playhead, a drag scrubs it (snapped to the grid), as on the
        // arrangement ruler (#50).
        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            Cursor = _o.Seek is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (_o.Seek is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _scrub = true;
            e.Pointer.Capture(this);
            SeekAt(e.GetPosition(this).X);
            e.Handled = true;
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_scrub) SeekAt(e.GetPosition(this).X);
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!_scrub) return;
            _scrub = false;
            e.Pointer.Capture(null);
        }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            _scrub = false;
            base.OnPointerCaptureLost(e);
        }
        private void SeekAt(double x)
        {
            double b = Math.Clamp(_o.SnapBeat(_o.XToBeat(x)), 0, _o._lengthBeats);
            if (Math.Abs(b - _o._playheadBeat) < 1e-9) return;
            _o.Seek?.Invoke(b);
            _o.SetPlayhead(b, true);   // immediate feedback; the transport tick keeps it in sync
        }
        // Beats only: a bar reads "3" in Ink 2, a beat "3.2" in Ink 5, set on the baseline in
        // mono 9 just right of its tick. The loop is a 3px brass strip along the top.
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0) return;
            ctx.FillRectangle(KeysBg, new Rect(0, 0, w, h));
            _o.EnsurePpb(w);
            double ppb = _o.PixelsPerBeat;
            double viewEnd = Math.Min(_o._lengthBeats, _o.ScrollBeats + w / Math.Max(1e-6, ppb));
            foreach (var (cs, ce) in _o.CoveredSpans())
            {
                double loopEnd = Math.Min(w, _o.BeatToX(ce));
                double loopStart = Math.Max(0, _o.BeatToX(cs));
                if (loopEnd > loopStart) ctx.FillRectangle(LoopStrip, new Rect(loopStart, 0, loopEnd - loopStart, 3));
            }
            // Label every beat while there is room for "12.4", else bars only.
            double step = ppb >= 30 ? 1 : _o._beatsPerBar;
            double startB = Math.Max(0, Math.Floor(_o.ScrollBeats / step) * step);
            for (int k = 0; ; k++)
            {
                double b = startB + k * step;
                if (b > viewEnd + 1e-9) break;
                double x = _o.BeatToX(b);
                int bpb = _o._beatsPerBar;
                bool bar = Math.Abs(b / bpb - Math.Round(b / bpb)) < 1e-6;
                ctx.DrawLine(bar ? BarPen : BeatPen, new Point(x + 0.5, 0), new Point(x + 0.5, h));
                if (b >= _o._lengthBeats - 1e-9) continue;
                int barNo = (int)Math.Floor(b / bpb) + 1 + _o.RulerBarOffset, beatNo = (int)Math.Round(b - Math.Floor(b / bpb) * bpb) + 1;
                string label = bar ? barNo.ToString(NotaNum.Culture) : $"{barNo}.{beatNo}";
                var ft = new FormattedText(label, NotaNum.Culture, FlowDirection.LeftToRight, Mono, 9, bar ? RulerBarText : LabelText);
                ctx.DrawText(ft, new Point(x + 4, h - 5 - ft.Height));
            }
            _o.DrawOutsideClip(ctx, w, h);
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
        private double RowH => _o.RowH;

        // A press plays the key on the track's instrument (#47) until release. Dragging
        // vertically past a few pixels turns the press into the row zoom of specs/019 — down
        // taller, up shorter, the pressed key held at the press height — and lets the note
        // go; with Shift held the drag glides through the keys instead. ⌘/Ctrl-click
        // selects every note of that pitch (⇧ adds to the selection) and plays nothing.
        private static readonly Cursor ZoomCursor = new(StandardCursorType.SizeNorthSouth);
        private const double ZoomSlop = 4;
        private enum Press { None, Key, Glide, Zoom }
        private Press _press;
        private double _pressY, _pressRowH;

        private int PitchAt(double y) => (int)Math.Clamp(MaxPitch - Math.Floor(y / RowH), MinPitch, MaxPitch);
        private const float KeyVelocity = 0.8f;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _o._gridControl.Dragging) return;
            int pitch = PitchAt(e.GetPosition(this).Y);
            e.Handled = true;
            if (ArrangementView.IsPrimaryDown(e.KeyModifiers))
            {
                _o.SelectPitch(pitch, (e.KeyModifiers & KeyModifiers.Shift) != 0);
                return;
            }
            _press = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? Press.Glide : Press.Key;
            _pressY = e.GetPosition(_o._bodyScroll).Y;
            _pressRowH = RowH;
            _o.Audition(pitch, KeyVelocity, force: true);
            e.Pointer.Capture(this);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_press == Press.None) return;
            e.Pointer.Capture(null);
            EndPress();
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => EndPress();

        private void EndPress()
        {
            if (_press == Press.None) return;
            _press = Press.None;
            _o.StopAudition();
            Cursor = null;
        }

        // ⌘⌥+wheel zooms rows here too; any other wheel is left to the body ScrollViewer.
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            if (IsRowZoomWheel(e.KeyModifiers))
            {
                _o.ZoomRowsAt(WheelInput.ZoomFactor(e.Delta.Y, 1.2), e.GetPosition(_o._bodyScroll).Y);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_press == Press.Key && Math.Abs(e.GetPosition(_o._bodyScroll).Y - _pressY) > ZoomSlop)
            {
                _press = Press.Zoom;   // a drag, not a key press: zoom, and let the note go
                _o.StopAudition();
                Cursor = ZoomCursor;
            }
            if (_press == Press.Zoom)
            {
                double dy = e.GetPosition(_o._bodyScroll).Y - _pressY;
                _o.SetRowHeightAt(_pressRowH * Math.Exp(dy * 0.01), _pressY);
                return;
            }
            int p = PitchAt(e.GetPosition(this).Y);
            if (_press == Press.Glide) _o.Audition(p, KeyVelocity, force: true);
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
                ctx.FillRectangle(IsBlack(p) ? KeyBlack : KeyWhite, new Rect(0, y, w, RowH));
                if (_o._scaleOn)
                {
                    if (_o.IsRoot(p)) ctx.FillRectangle(RootTint, new Rect(0, y, w, RowH));
                    else if (!_o.InScale(p)) ctx.FillRectangle(OutScaleWash, new Rect(0, y, w, RowH));
                }
                ctx.DrawLine(p % 12 == 0 ? KeyOctaveLine : KeyLine, new Point(0, y + RowH - 0.5), new Point(w, y + RowH - 0.5));
                if (p % 12 == 0) DrawKeyLabel(ctx, p, y, w, KeyLabel);
            }
            // Currently-pressed keys (computer keyboard / MIDI): light the key solid + label it.
            if (_o._held.Count > 0)
                foreach (int hp in _o._held)
                {
                    if (hp < MinPitch || hp > MaxPitch) continue;
                    double y = (MaxPitch - hp) * RowH;
                    ctx.FillRectangle(HeldKeyFill, new Rect(0, y, w, RowH - 1));
                    DrawKeyLabel(ctx, hp, y, w, KeyLabelOnAccent);
                }
            // The key the roll itself is sounding (a pressed key, an auditioned note).
            if (_o._auditionPitch >= MinPitch && !_o._held.Contains(_o._auditionPitch))
            {
                double y = (MaxPitch - _o._auditionPitch) * RowH;
                ctx.FillRectangle(HeldKeyFill, new Rect(0, y, w, RowH - 1));
                DrawKeyLabel(ctx, _o._auditionPitch, y, w, KeyLabelOnAccent);
            }
            // Hovered key: highlight it and label its note name (works for black keys too,
            // where the octave-C label wouldn't otherwise show).
            if (_hover >= MinPitch && _hover != _o._auditionPitch)
            {
                double hy = (MaxPitch - _hover) * RowH;
                ctx.FillRectangle(KeyHover, new Rect(0, hy, w, RowH - 1));
                DrawKeyLabel(ctx, _hover, hy, w, KeyLabelHot);
            }
        }

        private void DrawKeyLabel(DrawingContext ctx, int pitch, double y, double w, IBrush ink)
        {
            var ft = new FormattedText(NoteName(pitch), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, ink);
            ctx.DrawText(ft, new Point(w - ft.Width - 5, y + (RowH - ft.Height) / 2));
        }
    }

    // ---- note grid (interactive) -----------------------------------------
    private sealed class NoteGrid : Control
    {
        private readonly PianoRollView _o;
        private enum Mode { None, Move, Resize, ResizeLeft, Marquee }
        private Mode _drag = Mode.None;
        /// <summary>A note move / resize / box selection is in progress.</summary>
        internal bool Dragging => _drag != Mode.None;
        private bool _dragMoved;

        // Move/resize: each selected note's state at grab time + the primary grabbed.
        private readonly Dictionary<int, (double start, int pitch, double len, int owner)> _orig = new();
        private int _primary = -1;
        private double _grabBeat;       // pointer-beat minus primary start, at grab (Move)
        private double _edgeGrab;       // pointer beat minus the grabbed edge at press (Resize / ResizeLeft)

        // Marquee (grid coords) + the selection to union with when additive.
        private Point _marqueeA, _marqueeB;
        private bool _marqueeAdd;
        private readonly HashSet<int> _selBase = new();

        // Hover affordance (mirrors the arrangement grid): resize cursor on the trim edge, a
        // move/four-arrows cursor over a note body, plain arrow on empty space.
        private int _hoverNote = -1;
        private bool _hoverLeft;   // the hovered trim edge is the note's start
        private int _cursorKind;   // 0 arrow, 1 resize, 2 move
        private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
        private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
        private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
        private const double EdgePx = 6;

        // Live drag (feature): the pre-drag note list (to seed one undo entry) + whether the
        // live stream has started this gesture.
        private readonly List<NotaNote> _preDrag = new();
        private readonly List<int> _preDragOwners = new();
        private bool _liveStarted;

        // Vertical auto-scroll while a drag runs past the viewport edges.
        private DispatcherTimer? _autoScroll;
        private double _autoVelY;
        private Point _lastGrid;

        private readonly List<(Rect, Color)> _doomed = new();   // per-frame scratch for the overwrite marks

        public NoteGrid(PianoRollView o)
        {
            _o = o;
            Focusable = true;
            // Leaving the grid (another panel, another clip, a dialog) lets the placement go.
            LostFocus += (_, _) => _o.SettlePlacement();
            // ⌥+pinch on a trackpad zooms rows (raised by the macOS backend; elsewhere never).
            AddHandler(PointerTouchPadGestureMagnifyEvent, (_, e) =>
            {
                if ((e.KeyModifiers & KeyModifiers.Alt) == 0) return;
                _o.ZoomRowsAt(1 + e.Delta.X, e.GetPosition(_o._bodyScroll).Y);
                e.Handled = true;
            });
        }

        private double Ppb => _o.PixelsPerBeat;
        private double BeatToX(double b) => _o.BeatToX(b);
        private double XToBeat(double x) => _o.XToBeat(x);
        private double RowH => _o.RowH;
        private double PitchToY(int p) => (MaxPitch - p) * RowH;
        private int YToPitch(double y) => (int)Math.Clamp(MaxPitch - Math.Floor(y / RowH), MinPitch, MaxPitch);
        // Alt held during the gesture positions freely, as in the arrangement (FR-009).
        private bool _free;
        private double Snap(double b) => _free ? b : _o.SnapBeat(b);

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0) return;
            _o.EnsurePpb(w);
            ctx.FillRectangle(Bg, new Rect(0, 0, w, h));
            for (int p = MinPitch; p <= MaxPitch; p++)
            {
                double y = PitchToY(p);
                ctx.FillRectangle(IsBlack(p) ? LaneBlack : LaneWhite, new Rect(0, y, w, RowH));
                if (_o._scaleOn)
                {
                    if (_o.IsRoot(p)) ctx.FillRectangle(RootTint, new Rect(0, y, w, RowH));
                    else if (_o.InScale(p)) ctx.FillRectangle(InScaleTint, new Rect(0, y, w, RowH));
                    else ctx.FillRectangle(OutScaleWash, new Rect(0, y, w, RowH));
                }
                ctx.DrawLine(p % 12 == 0 ? RowOctaveLine : RowLine, new Point(0, y + RowH - 0.5), new Point(w, y + RowH - 0.5));
            }
            // Played-key rows: a faint wash across the grid so you see where the pressed note sits.
            if (_o._held.Count > 0)
                foreach (int hp in _o._held)
                    if (hp >= MinPitch && hp <= MaxPitch)
                        ctx.FillRectangle(HeldRowTint, new Rect(0, PitchToY(hp), w, RowH));
            double ppb = Ppb;
            _o.DrawGridLines(ctx, w, h);
            var notes = _o._notes;
            // Other clips' notes first, so the active clip's draw on top where they overlap.
            bool multi = _o._set.IsMulti;
            for (int pass = multi ? 0 : 1; pass < 2; pass++)
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n.Pitch < MinPitch || n.Pitch > MaxPitch) continue;
                if (multi && (_o._owner[i] == _o._set.Active) != (pass == 1)) continue;
                double x = BeatToX(n.StartBeat), y = PitchToY(n.Pitch);
                double nw = Math.Max(3, n.LengthBeats * ppb);
                bool sel = _o._selection.Contains(i);
                var r = new Rect(x, y, Math.Max(2, nw - 1), RowH - 1);
                if (_o.IsGhost(i))   // a tool's preview: brass outline over the clip's own notes
                {
                    ctx.DrawRectangle(GhostFill, GhostEdge, r.Deflate(0.5), 2, 2);
                    continue;
                }
                if (sel) ctx.DrawRectangle(NoteSel, NoteSelEdge, r.Deflate(0.5), 2, 2);
                else
                {
                    // Track colour, quieter the softer the note — and, with several clips open,
                    // quieter for clips other than the active one.
                    var c = _o.ColorOf(i);
                    double a = (0.45 + 0.55 * Math.Clamp(n.Velocity, 0, 1)) * _o.ClipEmphasis(i);
                    if (!_o.InScale(n.Pitch)) a *= 0.4;   // out-of-scale notes read faint
                    // A pending placement covers part of this note: the part settling will cut
                    // (or all of it, when it goes) reads faint behind a dashed track-colour edge.
                    var fate = _o._placementPending && _o._selection.Count > 0
                        ? NoteOverwrite.Fate(i, notes, _o.PlacedForRender(), _o._owner) : default;
                    double appear = _o._recPreview?.Progress(n.Pitch, _o._recClipStart + n.StartBeat) ?? 1;
                    if (fate.Kind == NoteFateKind.Kept && appear < 1)
                        ctx.FillRectangle(RecordPreview.AppearBrush(c, appear), r, 2);
                    else if (fate.Kind == NoteFateKind.Kept)
                        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * a), c.R, c.G, c.B)), r, 2);
                    else
                    {
                        double keepW = fate.Kind == NoteFateKind.Cut ? Math.Min(r.Width, fate.CutLength * ppb) : 0;
                        if (keepW > 0)
                            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * a), c.R, c.G, c.B)), r.WithWidth(keepW), 2);
                        var doomed = new Rect(r.X + keepW, r.Y, Math.Max(1, r.Width - keepW), r.Height);
                        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * a * 0.25), c.R, c.G, c.B)), doomed, 2);
                        _doomed.Add((doomed, c));   // its dashed edge goes on top of the notes covering it
                    }
                }
                if (n.ExprId > 0 || _o._expr.Editing(i)) DrawExpression(ctx, i, n, r, sel, ppb);
                // Resize-edge affordance: a bright bar on the edge a drag would trim.
                bool edgeHi = ((_drag == Mode.Resize || _drag == Mode.ResizeLeft) && sel) || (_drag == Mode.None && _hoverNote == i && !_o.IsLocked(i));
                if (edgeHi)
                {
                    bool left = _drag == Mode.ResizeLeft || (_drag == Mode.None && _hoverLeft);
                    ctx.FillRectangle(EdgeHighlight, new Rect(left ? x : x + r.Width - 2.5, y + 1, 2.5, RowH - 3));
                }
            }
            // The dashed edges of what a pending placement will cut, drawn over the placed notes
            // so a note hidden under a pasted copy still shows that it is there and will go.
            if (_doomed.Count > 0)
            {
                foreach (var (d, dc) in _doomed)
                    ctx.DrawRectangle(null, new Pen(new SolidColorBrush(dc), 1, DashStyle.Dash), d.Deflate(0.5), 2, 2);
                _doomed.Clear();
            }
            // Keys held into the recording take (#56): from the key-down to the capture clock,
            // not editable — they become ordinary notes when released.
            if (_o._recPreview is { } rp)
                foreach (var hn in rp.Held)
                {
                    if (hn.Pitch < MinPitch || hn.Pitch > MaxPitch) continue;
                    double s = hn.StartBeat - _o._recClipStart, e = Math.Max(hn.StartBeat, rp.NowBeat) - _o._recClipStart;
                    double hx = BeatToX(s), hw = Math.Max(3, (e - s) * ppb);
                    if (hx + hw < 0 || hx > w) continue;
                    var c = _o._trackColor;
                    double p = rp.Progress(hn.Pitch, hn.StartBeat);
                    var hr = new Rect(hx, PitchToY(hn.Pitch), Math.Max(2, hw - 1), RowH - 1);
                    ctx.FillRectangle(p < 1 ? RecordPreview.AppearBrush(c, p)
                        : new SolidColorBrush(Color.FromArgb((byte)(255 * (0.45 + 0.55 * Math.Clamp(hn.Velocity, 0, 1))), c.R, c.G, c.B)), hr, 2);
                }
            _o.DrawOutsideClip(ctx, w, h);
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

        // The note's recorded MPE: pressure shades up from its floor, the bend is drawn as the
        // pitch it played (a semitone per row), both held flat before the first point and after
        // the last, and both kept within the note's length.
        private void DrawExpression(DrawingContext ctx, int index, NotaNote n, Rect r, bool sel, double ppb)
        {
            double x0 = r.X, x1 = r.Right, mid = r.Y + RowH * 0.5;
            List<Point> Curve(NoteExpressionDim d, Func<float, double> y)
            {
                var line = new List<Point>();
                // A curve being edited in the lane shows here live.
                foreach (var (b, v) in _o._expr.EditingCurve(index, d) ?? _o.CurveOf(n, d))
                {
                    double x = Math.Clamp(x0 + b * ppb, x0, x1);
                    if (line.Count == 0 && x > x0) line.Add(new Point(x0, y(v)));
                    line.Add(new Point(x, y(v)));
                }
                if (line.Count > 0 && line[^1].X < x1) line.Add(new Point(x1, line[^1].Y));
                return line;
            }
            var pressure = Curve(NoteExpressionDim.Pressure, v => r.Bottom - Math.Clamp(v, 0, 1) * (r.Height - 1));
            if (pressure.Count > 1)
            {
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(new Point(pressure[0].X, r.Bottom), true);
                    foreach (var p in pressure) gc.LineTo(p);
                    gc.LineTo(new Point(pressure[^1].X, r.Bottom));
                    gc.EndFigure(true);
                }
                using (ctx.PushClip(r)) ctx.DrawGeometry(PressureShade, null, g);
            }
            var bend = Curve(NoteExpressionDim.Bend, v => mid - v * RowH);
            if (bend.Count > 1)
            {
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(bend[0], false);
                    for (int i = 1; i < bend.Count; i++) gc.LineTo(bend[i]);
                    gc.EndFigure(false);
                }
                if (!sel) ctx.DrawGeometry(null, BendPen, g);
                else
                {
                    // Brass where it leaves the (brass) note, dark ink across the note itself.
                    ctx.DrawGeometry(null, CurvePen, g);
                    using (ctx.PushClip(r)) ctx.DrawGeometry(null, BendPenOnSel, g);
                }
            }
        }

        private static Rect RectOf(Point a, Point b)
            => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        // The note under p, and which trim edge (if any) p is on: +1 end, −1 start, 0 body.
        // On a short note each edge zone shrinks to a third, so the body stays grabbable.
        private int HitTest(Point p, out int edge)
        {
            edge = 0;
            var notes = _o._notes;
            // The active clip's notes are on top, so they win a click where clips overlap.
            int best = -1, bestEdge = 0;
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                var n = notes[i];
                if (n.Pitch < MinPitch || n.Pitch > MaxPitch || _o.IsLocked(i)) continue;
                double x = BeatToX(n.StartBeat), y = PitchToY(n.Pitch), nw = Math.Max(3, n.LengthBeats * Ppb);
                if (!new Rect(x, y, nw, RowH).Contains(p)) continue;
                if (_o._owner[i] != _o._set.Active)
                {
                    if (best < 0) { best = i; double z = Math.Min(EdgePx, nw / 3); bestEdge = p.X >= x + nw - z ? 1 : p.X <= x + z ? -1 : 0; }
                    continue;
                }
                double zone = Math.Min(EdgePx, nw / 3);
                edge = p.X >= x + nw - zone ? 1 : p.X <= x + zone ? -1 : 0;
                return i;
            }
            edge = bestEdge;
            return best;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            Focus();
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            _free = (e.KeyModifiers & KeyModifiers.Alt) != 0;
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            int hit = HitTest(p, out int edge);
            // Any press that changes the selection lets a pending placement go first; settling
            // can cut or remove notes, so hit-test again on the settled notes.
            bool keepsSelection = e.ClickCount != 2 && !shift && hit >= 0 && _o._selection.Contains(hit);
            if (!keepsSelection && _o.SettlePlacement()) hit = HitTest(p, out edge);

            // Double-click: add a note on empty space, delete one under the cursor.
            if (e.ClickCount == 2)
            {
                if (hit >= 0) { _o.RemoveNoteAt(hit); _o._selection.Clear(); }
                else
                {
                    double at = Math.Max(0, Snap(XToBeat(p.X)));
                    int slot = _o.DrawTarget(at);
                    if (slot < 0) { e.Handled = true; return; }   // no open clip of the active track here
                    if (slot != _o._set.Active) _o.SetActiveClip(slot);
                    var nt = new NotaNote(YToPitch(p.Y), at, _o.NewNoteLen(at, slot), s_lastVel);
                    _o.AddNote(nt, slot);
                    _o._selection.Clear(); _o._selection.Add(_o._notes.Count - 1);
                    _o._placementPending = true;   // a drawn note overwrites what it lands on
                    _o.Audition(nt.Pitch, nt.Velocity, slot: slot);   // heard until the button comes up
                    e.Pointer.Capture(this);
                }
                _o.Invalidate(); _o.CommitNotes(); _o.Changed?.Invoke();
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
                // A note of another clip makes that clip active (header, inspector, envelopes).
                if (_o._selection.Count == 1 && _o._owner[hit] != _o._set.Active) _o.SetActiveClip(_o._owner[hit]);
                e.Pointer.Capture(this);
                _o.Audition(_o._notes[hit].Pitch, _o._notes[hit].Velocity, slot: _o._owner[hit]);
                _primary = hit;
                _o.Remember(hit);
                _dragMoved = false;
                _orig.Clear();
                foreach (int i in _o._selection)
                    if (i >= 0 && i < _o._notes.Count) { var nn = _o._notes[i]; _orig[i] = (nn.StartBeat, nn.Pitch, nn.LengthBeats, _o._owner[i]); }
                _preDrag.Clear(); _preDrag.AddRange(_o._notes);
                _preDragOwners.Clear(); _preDragOwners.AddRange(_o._owner);
                _liveStarted = false;
                if (edge != 0)
                {
                    _drag = edge > 0 ? Mode.Resize : Mode.ResizeLeft;
                    var g = _orig[hit];
                    _edgeGrab = XToBeat(p.X) - (edge > 0 ? g.start + g.len : g.start);
                    Cursor = ResizeCursor; _cursorKind = 1;
                }
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
            _free = (e.KeyModifiers & KeyModifiers.Alt) != 0;   // live, so Alt can toggle mid-drag
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
                // Clamp the shared delta so the whole group stays in-bounds (keeps shape): every
                // start within its reach — its clip and the open clips of its track it touches.
                int minP = int.MaxValue, maxP = int.MinValue;
                foreach (var v in _orig.Values) { minP = Math.Min(minP, v.pitch); maxP = Math.Max(maxP, v.pitch); }
                var (lo, hi) = _o.ShiftRange(_orig.Keys, i => (_orig[i].start, _orig[i].len, _orig[i].owner), endsInside: false);
                dBeat = Math.Clamp(dBeat, lo, hi);
                dPitch = Math.Clamp(dPitch, MinPitch - minP, MaxPitch - maxP);
                foreach (var kv in _orig)
                {
                    var n = _o._notes[kv.Key];
                    n.StartBeat = kv.Value.start + dBeat;
                    n.Pitch = kv.Value.pitch + dPitch;
                    _o._notes[kv.Key] = n;
                    _o._owner[kv.Key] = _o._set.OwnerAfterMove(kv.Value.owner, n.StartBeat);
                }
                _dragMoved = true;
                PushLive();
                // A drag to a new pitch re-triggers the grabbed note there (#46).
                _o.Audition(_o._notes[_primary].Pitch, _o._notes[_primary].Velocity, slot: _o._owner[_primary]);
            }
            else if (_drag == Mode.Resize)
            {
                // The grabbed note's end lands on the grid (not just the drag distance); the
                // rest of the selection changes by the same amount.
                var pr = _orig[_primary];
                double dLen = Snap(XToBeat(p.X) - _edgeGrab) - (pr.start + pr.len);
                foreach (var kv in _orig)
                {
                    var n = _o._notes[kv.Key];
                    n.LengthBeats = Math.Max(_o.MinNoteLen, kv.Value.len + dLen);
                    _o._notes[kv.Key] = n;
                }
                _dragMoved = true;
                PushLive();
            }
            else if (_drag == Mode.ResizeLeft)
            {
                // Move the start, keep the end: clamp so no note passes its own end (minus
                // one grid step) or the clip's start.
                var pr = _orig[_primary];
                double dStart = Snap(XToBeat(p.X) - _edgeGrab) - pr.start;
                foreach (var v in _orig.Values)
                {
                    double floor = _o._set.EditorStart(v.owner) - v.start;
                    dStart = Math.Clamp(dStart, floor, Math.Max(floor, v.len - _o.MinNoteLen));
                }
                foreach (var kv in _orig)
                {
                    var n = _o._notes[kv.Key];
                    n.StartBeat = kv.Value.start + dStart;
                    n.LengthBeats = kv.Value.len - dStart;
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
            if (!_o.HasLivePath) return;   // no live path wired → commit on release
            if (!_liveStarted) { _liveStarted = true; _o.CommitNotes(_preDrag, _preDragOwners, seed: true); }
            _o.PushLive(_o._notes, _o._owner);
        }

        private void RecomputeMarquee()
        {
            var r = RectOf(_marqueeA, _marqueeB);
            _o._selection.Clear();
            if (_marqueeAdd) _o._selection.UnionWith(_selBase);
            for (int i = 0; i < _o._notes.Count; i++)
            {
                var n = _o._notes[i];
                double x = BeatToX(n.StartBeat), y = PitchToY(n.Pitch), nw = Math.Max(3, n.LengthBeats * Ppb);
                if (!_o.IsLocked(i) && r.Intersects(new Rect(x, y, nw, RowH))) _o._selection.Add(i);
            }
        }

        private void UpdateHover(Point p)
        {
            int hit = HitTest(p, out int edge);
            int kind = hit < 0 ? 0 : edge != 0 ? 1 : 2;   // 0 arrow, 1 resize edge, 2 move body
            if (kind != _cursorKind) { _cursorKind = kind; Cursor = kind == 1 ? ResizeCursor : kind == 2 ? MoveCursor : ArrowCursor; }
            int hn = kind == 1 ? hit : -1;   // the bright trim-edge affordance shows only on the edge
            bool left = edge < 0;
            if (hn != _hoverNote || left != _hoverLeft) { _hoverNote = hn; _hoverLeft = left; _o.Invalidate(); }
        }

        // Whatever ends the press (release, a window losing the pointer) ends its note.
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            _o.StopAudition();
            base.OnPointerCaptureLost(e);
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
            _o.StopAudition();
            if (_drag == Mode.None) { e.Pointer.Capture(null); return; }
            var was = _drag;
            _drag = Mode.None;
            e.Pointer.Capture(null);
            _orig.Clear();
            if ((was == Mode.Resize || was == Mode.ResizeLeft) && _dragMoved) _o.Remember(_primary);
            if (was != Mode.Marquee && _dragMoved)
            {
                _o._placementPending = true;   // overwrites once the selection is let go
                // Live path: undo was seeded on the first move; push the final state (no undo).
                // No live path (session): fall back to a single committing push.
                if (_o.HasLivePath && _liveStarted) _o.PushLive(_o._notes, _o._owner);
                else _o.CommitNotes();
            }
            _liveStarted = false;
            _cursorKind = 0; Cursor = ArrowCursor;   // re-evaluated on the next hover
            _o.Invalidate();
            _o.Changed?.Invoke();
        }

        // ⌘⌥/Ctrl+Alt+wheel zooms rows around the pitch under the cursor; ⌘/Ctrl+wheel zooms
        // time toward the cursor; Shift+wheel scrolls horizontally; plain wheel is left to
        // the body ScrollViewer for vertical key navigation.
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            var mods = e.KeyModifiers;
            if (IsRowZoomWheel(mods))
            {
                _o.ZoomRowsAt(WheelInput.ZoomFactor(e.Delta.Y, 1.2), e.GetPosition(_o._bodyScroll).Y);
                e.Handled = true;
            }
            else if ((mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
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
            // ⇧F: Fit to notes. Handled here, the window's computer-keyboard note map never
            // sees it, so no F sounds.
            if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Shift) { _o.FitToNotes(); e.Handled = true; return; }
            if (!meta && e.Key == Key.Escape && _o._selection.Count > 0)
            {
                _o.SettlePlacement();
                _o._selection.Clear(); _o.Invalidate(); _o.Changed?.Invoke(); e.Handled = true; return;
            }
            // Arrow keys nudge the selection: ←/→ by the grid in time, ↑/↓ by a semitone
            // (Shift = an octave). The move auto-scrolls to keep pitch-shifted notes in view.
            // ⇧←/⇧→ shorten/lengthen by the grid; ⌥↑/⌥↓ change velocity by 10 (⌥⇧ by 1).
            bool alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
            if (!meta && _o._selection.Count > 0)
            {
                if (alt)
                {
                    switch (e.Key)
                    {
                        case Key.Up:   if (_o.NudgeVelocity(shift ? 1 : 10)) e.Handled = true; return;
                        case Key.Down: if (_o.NudgeVelocity(shift ? -1 : -10)) e.Handled = true; return;
                    }
                }
                else switch (e.Key)
                {
                    case Key.Left when shift:  if (_o.ResizeSelection(-1)) e.Handled = true; return;
                    case Key.Right when shift: if (_o.ResizeSelection(+1)) e.Handled = true; return;
                    case Key.Left:  if (_o.MoveSelection(0, -_o.SnapStep)) e.Handled = true; return;
                    case Key.Right: if (_o.MoveSelection(0, +_o.SnapStep)) e.Handled = true; return;
                    case Key.Up:    if (_o.MoveSelection(shift ? 12 : 1, 0)) e.Handled = true; return;
                    case Key.Down:  if (_o.MoveSelection(shift ? -12 : -1, 0)) e.Handled = true; return;
                }
            }
            if (meta && e.Key == Key.A)
            {
                _o.SettlePlacement();
                _o._selection.Clear();
                for (int i = 0; i < _o._notes.Count; i++) if (!_o.IsLocked(i)) _o._selection.Add(i);
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
            _o.DrawGridLines(ctx, w, h);
            // A 2px stem per note with a 6×3 cap — the cap is the drag handle.
            var notes = _o._notes;
            for (int i = 0; i < notes.Count; i++)
            {
                if (_o.IsLocked(i)) continue;   // a reference clip's velocities are not on offer
                var n = notes[i];
                double x = _o.BeatToX(n.StartBeat) + 3;
                double bh = Math.Clamp(n.Velocity, 0, 1) * (h - 8);
                bool sel = _o._selection.Contains(i);
                bool hov = i == _hover;
                IBrush b;
                if (_o.IsGhost(i)) b = GhostStem;
                else if (sel) b = NoteSel;
                else
                {
                    var c = hov ? Brighten(_o.ColorOf(i)) : _o.ColorOf(i);
                    double a = (0.45 + 0.55 * Math.Clamp(n.Velocity, 0, 1)) * _o.ClipEmphasis(i);
                    b = new SolidColorBrush(Color.FromArgb((byte)(255 * a), c.R, c.G, c.B));
                }
                ctx.FillRectangle(b, new Rect(x, h - bh, 2, bh));
                ctx.FillRectangle(hov && !sel ? EdgeHighlight : b, new Rect(x - 2, h - bh - 2, 6, 3), 1);
            }
            _o.DrawOutsideClip(ctx, w, h);
        }

        private static Color Brighten(Color c)
            => Color.FromRgb((byte)Math.Min(255, c.R + 48), (byte)Math.Min(255, c.G + 48), (byte)Math.Min(255, c.B + 48));

        private double Usable => Math.Max(1, Bounds.Height - 8);
        private double CapY(int i) => Bounds.Height - Math.Clamp(_o._notes[i].Velocity, 0, 1) * Usable;

        // The stem under the pointer. Notes that start together (a chord, stacked pitches)
        // share one x, so among the stems within reach the one whose cap is nearest the
        // pointer's height wins — each stem stays reachable by grabbing it at its cap.
        private int Nearest(Point p)
        {
            int best = -1; double bx = 14, by = double.MaxValue;
            for (int i = 0; i < _o._notes.Count; i++)
            {
                if (_o.IsLocked(i)) continue;
                double dx = Math.Abs(_o.BeatToX(_o._notes[i].StartBeat) + 4 - p.X);
                if (dx > 14) continue;
                double dy = Math.Abs(CapY(i) - p.Y);
                // Nearer in x wins outright; stems at (about) the same x go by cap height.
                if (dx < bx - 2 || (Math.Abs(dx - bx) <= 2 && dy < by)) { best = i; bx = dx; by = dy; }
            }
            return best;
        }

        // Grab: the stem under the pointer, and — when it is part of the selection — every
        // selected note with it, each keeping its offset (so a stacked chord moves together).
        private readonly Dictionary<int, float> _grabbed = new();
        private double _grabY;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            int hit = Nearest(p);
            if (hit < 0) return;
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            // The lane never takes focus, so a stem that changes the selection lets a pending
            // placement go here; settling can shift indices, so find the stem again.
            if ((shift || !_o._selection.Contains(hit)) && _o.SettlePlacement())
            {
                hit = Nearest(p);
                if (hit < 0) return;
            }
            if (shift) _o._selection.Add(hit);
            else if (!_o._selection.Contains(hit)) { _o._selection.Clear(); _o._selection.Add(hit); }
            _grabbed.Clear();
            foreach (int i in _o._selection)
                if (i >= 0 && i < _o._notes.Count) _grabbed[i] = _o._notes[i].Velocity;
            _velNote = hit; _drag = true;
            // A click on one stem sets it where you clicked; a group moves by the drag distance.
            if (_grabbed.Count == 1) Apply(p.Y);
            _grabY = p.Y;
            e.Pointer.Capture(this);
            _o.Invalidate(); _o.Changed?.Invoke();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag) { Apply(p.Y); _o.Invalidate(); _o.Changed?.Invoke(); return; }
            int hv = Nearest(p);           // hover highlight (no drag)
            if (hv != _hover) { _hover = hv; InvalidateVisual(); }
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            if (_hover != -1) { _hover = -1; InvalidateVisual(); }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!_drag) return;
            _drag = false; _grabbed.Clear(); e.Pointer.Capture(null);
            _o.Remember(_velNote);
            _o.CommitNotes(); _o.Changed?.Invoke();
        }

        private void Apply(double y)
        {
            if (_grabbed.Count == 1)
            {
                if (_velNote < 0 || _velNote >= _o._notes.Count) return;
                var n = _o._notes[_velNote];
                n.Velocity = (float)Math.Clamp((Bounds.Height - y) / Usable, 0.02, 1.0);
                _o._notes[_velNote] = n;
                return;
            }
            float d = (float)((_grabY - y) / Usable);
            foreach (var (i, v0) in _grabbed)
            {
                if (i < 0 || i >= _o._notes.Count) continue;
                var n = _o._notes[i];
                n.Velocity = Math.Clamp(v0 + d, 0.02f, 1f);
                _o._notes[i] = n;
            }
        }
    }
}
